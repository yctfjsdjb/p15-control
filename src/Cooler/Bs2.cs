// 飞智 BS2 / BS2 Pro 散热器（蓝牙 LE）精简控制
// 协议参考 github.com/zhugeshu/Flydigi-BS2-Pro-Controller（MIT），这里只移植协议本身：
// 不另起进程、不调用 PowerShell / nvidia-smi，温度直接用 EC 读数，空闲时几乎零开销。
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace P15
{
    public enum Bs2State { Off, Searching, Connected }
    /// <summary>Smart* = 智能变频（按温度曲线）；Fixed = 固定转速（任意转速）；Gear1~4 为旧版档位，读取时转换成固定转速</summary>
    public enum Bs2Mode { SmartQuiet = 0, SmartPerf = 1, Gear1 = 2, Gear2 = 3, Gear3 = 4, Gear4 = 5, SmartCustom = 6, Fixed = 7 }

    public sealed class Bs2Cooler
    {
        static readonly Guid SvcUuid = new Guid("0000fff0-0000-1000-8000-00805f9b34fb");
        static readonly Guid WriteUuid = new Guid("0000fff2-0000-1000-8000-00805f9b34fb");
        static readonly Guid NotifyUuid = new Guid("0000fff1-0000-1000-8000-00805f9b34fb");

        // 飞智官方 BS2 Pro 温度曲线（官方配置 intelligent_thermalv4.json），按供电 5V / 9V / 20V 各一套
        static readonly int[][,] QuietByPower =
        {
            new[,] { { 30, 1300 }, { 40, 1300 }, { 50, 1700 }, { 60, 2100 }, { 70, 2400 }, { 80, 2700 }, { 90, 2700 }, { 100, 2700 } },
            new[,] { { 30, 1300 }, { 40, 1300 }, { 50, 1700 }, { 60, 2100 }, { 70, 2700 }, { 80, 3000 }, { 90, 3300 }, { 100, 3300 } },
            new[,] { { 30, 1300 }, { 40, 1300 }, { 50, 1700 }, { 60, 2100 }, { 70, 2700 }, { 80, 3000 }, { 90, 3300 }, { 100, 3500 } },
        };
        static readonly int[][,] PerfByPower =
        {
            new[,] { { 30, 1300 }, { 40, 1700 }, { 50, 2100 }, { 60, 2400 }, { 70, 2700 }, { 80, 2700 }, { 90, 2700 }, { 100, 2700 } },
            new[,] { { 30, 1700 }, { 40, 1700 }, { 50, 2100 }, { 60, 2700 }, { 70, 3300 }, { 80, 3300 }, { 90, 3300 }, { 100, 3300 } },
            new[,] { { 30, 1700 }, { 40, 1700 }, { 50, 2100 }, { 60, 2700 }, { 70, 3300 }, { 80, 3500 }, { 90, 4000 }, { 100, 4000 } },
        };
        static readonly int[] GearRpm = { 1700, 2400, 3000, 4000 };

        public volatile Bs2State State = Bs2State.Off;
        public volatile int CurrentRpm, TargetRpm, PowerMode = 3;   // 1=5V 2=9V 3=20V
        public string Firmware = "";
        public volatile int DeviceTarget;
        public volatile bool ActualLight, HasStatus;
        public event Action Changed;

        volatile Bs2Mode mode;
        volatile bool lightOn = true, lightDirty, modeDirty;
        Thread thread;
        volatile bool running;
        GattCharacteristic wr;
        BluetoothLEDevice dev;
        GattDeviceService svc;
        GattCharacteristic nt;
        double lastTemp;
        DateTime tempAt = DateTime.MinValue;

        public Bs2Mode Mode { get { return mode; } set { mode = value; modeDirty = true; lastTemp = 0; } }
        public bool LightOn { get { return lightOn; } set { lightOn = value; lightDirty = true; } }
        // ---------- 自定义灯效（官方 306 字节 RGBConfig 表：准备 0x41 → 31 包 0x47 → 完成 0x43） ----------
        public enum RgbMode { Pure = 0, Breathe = 1, Rainbow = 2, Rotate = 3, Temperature = 4 }
        readonly System.Collections.Concurrent.ConcurrentQueue<byte[]> pending = new System.Collections.Concurrent.ConcurrentQueue<byte[]>();
        public volatile bool RgbAcked;

        static byte[] RgbTable(RgbMode mode, int r, int g, int b, int loopTime, int brightness)
        {
            var t = new byte[306];
            t[1] = 2;                                         // 版本 2
            t[4] = (byte)Math.Max(1, Math.Min(60, loopTime)); // 每步时间（越小越快）
            t[5] = (byte)Math.Max(0, Math.Min(100, brightness));
            Action<int, int, int, int, int> put = (grp, idx, cr, cg, cb) => { int o = 6 + grp * 30 + idx * 3; t[o] = (byte)cr; t[o + 1] = (byte)cg; t[o + 2] = (byte)cb; };
            switch (mode)
            {
                case RgbMode.Pure:
                    t[3] = 0;
                    for (int grp = 0; grp < 10; grp++) put(grp, 0, r, g, b);
                    break;
                case RgbMode.Breathe:
                    t[3] = 1;                                     // 黑 ↔ 颜色
                    for (int grp = 0; grp < 10; grp++) put(grp, 1, r, g, b);
                    break;
                case RgbMode.Rainbow:
                    t[3] = 5;
                    int[,] rb = { { 0, 0, 255 }, { 0, 127, 127 }, { 0, 255, 0 }, { 127, 127, 0 }, { 255, 0, 0 }, { 127, 0, 127 } };
                    for (int grp = 0; grp < 10; grp++)
                        for (int k = 0; k < 6; k++) { int s = (k + grp) % 6; put(grp, k, rb[s, 0], rb[s, 1], rb[s, 2]); }
                    break;
                case RgbMode.Rotate:
                    t[3] = 5;
                    var cyc = new[] { new[] { r, g, b }, new[] { r / 4, g / 4, b / 4 }, new[] { 0, 0, 0 }, new[] { 0, 0, 0 }, new[] { 0, 0, 0 }, new[] { r / 4, g / 4, b / 4 } };
                    for (int grp = 0; grp < 10; grp++)
                        for (int k = 0; k < 6; k++) { var c = cyc[(k + grp) % 6]; put(grp, k, c[0], c[1], c[2]); }
                    break;
            }
            return t;
        }

        /// <summary>下发灯效（在后台连接线程里按顺序发送）</summary>
        public void SetRgb(RgbMode mode, int r, int g, int b, int loopTime, int brightness)
        {
            RgbAcked = false;
            if (mode == RgbMode.Temperature) { pending.Enqueue(Pack(0x44, 1)); return; }   // 灯色跟随温度档位
            pending.Enqueue(Pack(0x44, 0));
            var table = RgbTable(mode, r, g, b, loopTime, brightness);
            pending.Enqueue(Pack(0x41));
            for (int i = 0; i < 31; i++)
            {
                var p = new byte[11];
                p[0] = (byte)i;
                for (int k = 0; k < 10 && i * 10 + k < 306; k++) p[1 + k] = table[i * 10 + k];
                pending.Enqueue(Pack(0x47, p));
            }
            pending.Enqueue(Pack(0x43));
        }

        /// <summary>散热器自己改了灯（按键），只同步记录，不再下发</summary>
        public void SyncLight(bool on) { lightOn = on; }

        // ---------- 官方设备选项（保存在散热器里，只在修改时下发；未连接时排队，连上后发送） ----------
        /// <summary>通电自动启动（0x0C）</summary>
        public void SetAutoStart(bool on) { pending.Enqueue(Pack(0x0C, (byte)(on ? 1 : 0))); }
        /// <summary>智能启停（0x0D）：0 关闭 / 1 立即 / 2 延时</summary>
        public void SetSmartStartStop(int mode) { pending.Enqueue(Pack(0x0D, (byte)Math.Max(0, Math.Min(2, mode)))); }
        /// <summary>档位数显（0x48）</summary>
        public void SetGearLed(bool on) { pending.Enqueue(Pack(0x48, (byte)(on ? 1 : 0))); }
        /// <summary>最近一次设备回包（命令号, 结果），用于确认设置被接受</summary>
        public volatile int LastAckCmd = -1, LastAckVal = -1;

        readonly object life = new object();

        /// <summary>
        /// 启动后台连接线程。刚要求停止、线程还没退出时又打开：不另开线程，让原来的线程继续工作，
        /// 保证任何时候只有一个连接在控制散热器（也不会卡住界面等旧线程退出）。
        /// </summary>
        public void Start()
        {
            lock (life)
            {
                running = true;
                if (thread != null) return;
                thread = new Thread(Loop) { IsBackground = true, Name = "BS2" };
                thread.Start();
            }
        }

        /// <summary>停止连接并交还散热器自己的按键控制。wait=false 时只发出停止请求，不等线程退出（界面线程用）</summary>
        public void Stop(bool wait = true)
        {
            Thread t;
            lock (life) { running = false; t = thread; }
            if (wait && t != null) t.Join(3000);
            Raise();
        }

        void Raise() { var h = Changed; if (h != null) try { h(); } catch { } }

        // ---------- 协议：5A A5 cmd len payload sum ----------
        static byte[] Pack(byte cmd, params byte[] payload)
        {
            int len = payload.Length + 2;
            var b = new byte[payload.Length + 5];
            b[0] = 0x5A; b[1] = 0xA5; b[2] = cmd; b[3] = (byte)len;
            int sum = cmd + len;
            for (int i = 0; i < payload.Length; i++) { b[4 + i] = payload[i]; sum += payload[i]; }
            b[b.Length - 1] = (byte)sum;
            return b;
        }
        static byte[] Rpm(byte cmd, int rpm) { rpm = Math.Max(0, Math.Min(4000, rpm)); return Pack(cmd, (byte)rpm, (byte)(rpm >> 8)); }

        bool Send(byte[] data)
        {
            var c = wr;
            if (c == null) return false;
            // 散热器的写特征只支持"带回应写入"（属性 Read, Write），直接用它，最多等 2 秒
            try
            {
                var task = c.WriteValueAsync(Buf(data), GattWriteOption.WriteWithResponse).AsTask();
                return task.Wait(2000) && task.Result == GattCommunicationStatus.Success;
            }
            catch { return false; }
        }
        static T Wait<T>(Windows.Foundation.IAsyncOperation<T> op, int ms) where T : class
        {
            try { var t = op.AsTask(); return t.Wait(ms) ? t.Result : null; } catch { return null; }
        }
        static IBuffer Buf(byte[] d) { var w = new DataWriter(); w.WriteBytes(d); return w.DetachBuffer(); }

        void OnNotify(GattCharacteristic s, GattValueChangedEventArgs a)
        {
            try
            {
                var r = DataReader.FromBuffer(a.CharacteristicValue);
                var b = new byte[r.UnconsumedBufferLength];
                r.ReadBytes(b);
                if (b.Length < 5 || b[0] != 0x5A || b[1] != 0xA5) return;
                var p = b.Skip(4).Take(b.Length - 5).ToArray();
                switch (b[2])
                {
                    case 0x22: if (p.Length >= 4) { CurrentRpm = p[0] | (p[1] << 8); Raise(); } break;
                    case 0xEF:
                        // 散热器每 0.5 秒主动上报：[?, 模式(06 手动/07 托管), 灯(bit2), 当前转速 L H, 目标转速 L H, 计数...]
                        if (p.Length >= 7)
                        {
                            int cur = p[3] | (p[4] << 8);
                            bool light = (p[2] & 0x04) != 0;
                            bool changed = cur != CurrentRpm || light != ActualLight;
                            CurrentRpm = cur; DeviceTarget = p[5] | (p[6] << 8); ActualLight = light; HasStatus = true;
                            if (changed) Raise();
                        }
                        break;
                    case 0x07: if (p.Length >= 1) PowerMode = p[0]; break;
                    case 0x43: case 0x44: RgbAcked = p.Length >= 1 && p[0] == 1; Raise(); break;
                    case 0x0C: case 0x0D: case 0x48: LastAckCmd = b[2]; LastAckVal = p.Length >= 1 ? p[0] : -1; break;
                    case 0x01: Firmware = "v" + string.Join(".", p); break;
                }
            }
            catch { }
        }

        /// <summary>固定转速的目标：旧版 1~4 档按原档位转速，其余按 FixedRpm；都不超过当前供电允许的最高转速</summary>
        int FixedTarget()
        {
            int rpm = mode >= Bs2Mode.Gear1 && mode <= Bs2Mode.Gear4 ? GearRpm[(int)mode - 2] : fixedRpm;
            return Math.Min(MaxRpm, rpm);
        }

        /// <summary>当前供电下的最高转速（官方「最大超频转速」）：5V 2700 / 9V 3300 / 20V 4000</summary>
        public int MaxRpm { get { return PowerMode == 1 ? 2700 : PowerMode == 2 ? 3300 : 4000; } }

        volatile int fixedRpm = 2400;
        /// <summary>固定转速模式的目标转速</summary>
        public int FixedRpm { get { return fixedRpm; } set { fixedRpm = Math.Max(1300, Math.Min(4000, value)); if (mode == Bs2Mode.Fixed) modeDirty = true; } }

        /// <summary>自定义智能曲线：30/40/…/100°C 共 8 个点的转速（官方「自定义模式」默认值）</summary>
        public static readonly int[] DefaultCustom = { 1700, 1972, 2604, 3218, 3790, 4000, 4000, 4000 };
        volatile int[] customCurve = (int[])DefaultCustom.Clone();
        public int[] CustomCurve { get { return (int[])customCurve.Clone(); } set { if (value != null && value.Length == 8) { customCurve = (int[])value.Clone(); lastTemp = 0; } } }

        int CurveRpm(double t)
        {
            int pi = Math.Max(1, Math.Min(3, PowerMode)) - 1;
            int[,] c;
            if (mode == Bs2Mode.SmartCustom)
            {
                var cc = customCurve; c = new int[8, 2];
                for (int i = 0; i < 8; i++) { c[i, 0] = 30 + i * 10; c[i, 1] = cc[i]; }
            }
            else c = mode == Bs2Mode.SmartPerf ? PerfByPower[pi] : QuietByPower[pi];
            int n = c.GetLength(0), rpm = c[n - 1, 1];
            if (t <= c[0, 0]) rpm = c[0, 1];
            else
                for (int i = 0; i < n - 1; i++)
                    if (t >= c[i, 0] && t <= c[i + 1, 0])
                    { rpm = (int)(c[i, 1] + (t - c[i, 0]) / (c[i + 1, 0] - c[i, 0]) * (c[i + 1, 1] - c[i, 1])); break; }
            return Math.Max(1300, Math.Min(MaxRpm, rpm));
        }

        /// <summary>升温立即跟随，降温超过 2°C 才降速，避免风扇来回变速</summary>
        int SmartTarget()
        {
            // 温度每 2 秒读一次（用开销最小的 EC 读法），风扇调速完全够用
            if ((DateTime.Now - tempAt).TotalSeconds >= 2 || lastTemp == 0)
            {
                tempAt = DateTime.Now;
                var tp = Dchu.GetTemps();
                double t = tp != null && tp[0] > 0 && tp[0] < 120 ? Math.Max(tp[0], tp[1]) : 50;
                if (lastTemp == 0 || t > lastTemp || t < lastTemp - 2) lastTemp = t;
            }
            return CurveRpm(lastTemp);
        }

        // ---------- 连接与控制循环 ----------
        void Loop()
        {
            while (true)
            {
                while (running)
                {
                    try { Session(); } catch { }
                    Cleanup();
                    if (!running) break;
                    State = Bs2State.Searching; Raise();
                    for (int i = 0; i < 50 && running; i++) Thread.Sleep(100);   // 5 秒后重连
                }
                lock (life)
                {
                    if (running) continue;   // 停止的过程中又被打开了：接着连接
                    thread = null; State = Bs2State.Off;
                }
                Raise();
                return;
            }
        }

        void Session()
        {
            State = Bs2State.Searching; Raise();
            // 已配对设备上的 0xFFF0 服务（系统蓝牙里显示为 "FlyDigi BS2PRO"）
            // 所有蓝牙异步调用都带超时：散热器没响应时放弃这一轮，5 秒后重连，不会把后台线程卡死
            var list = Wait(DeviceInformation.FindAllAsync(GattDeviceService.GetDeviceSelectorFromUuid(SvcUuid)), 10000);
            if (list == null || list.Count == 0) return;
            svc = Wait(GattDeviceService.FromIdAsync(list[0].Id), 15000);
            if (svc == null) return;
            dev = svc.Device;
            wr = svc.GetCharacteristics(WriteUuid).FirstOrDefault();
            nt = svc.GetCharacteristics(NotifyUuid).FirstOrDefault();   // 必须保存在字段里，否则会被回收导致收不到回包
            if (wr == null || nt == null) return;
            nt.ValueChanged += OnNotify;
            var sub = nt.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify).AsTask();
            if (!sub.Wait(5000) || sub.Result != GattCommunicationStatus.Success) return;

            Send(Pack(0x07)); Thread.Sleep(150);
            Send(Pack(0x01)); Thread.Sleep(150);
            modeDirty = true; lightDirty = true;
            State = Bs2State.Connected; Raise();

            var last = DateTime.MinValue;
            while (running && dev.ConnectionStatus == BluetoothConnectionStatus.Connected)
            {
                if (lightDirty) { lightDirty = false; Send(Pack(0x46, (byte)(lightOn ? 1 : 0))); Thread.Sleep(50); }
                byte[] pk;
                while (pending.TryDequeue(out pk)) { Send(pk); Thread.Sleep(30); }
                bool smart = mode == Bs2Mode.SmartQuiet || mode == Bs2Mode.SmartPerf || mode == Bs2Mode.SmartCustom;
                if (modeDirty)
                {
                    modeDirty = false;
                    if (smart) Send(Pack(0x23));
                    else
                    {
                        // 固定转速：选最接近的硬件档位，并把该档绑定到目标转速（与官方相同的 0x08 + 0x26）
                        int rpm = FixedTarget();
                        int g = rpm <= 1700 ? 0 : rpm <= 2400 ? 1 : rpm <= 3000 ? 2 : 3;
                        Send(Pack(0x08, (byte)g)); Thread.Sleep(50);
                        Send(Pack(0x26, (byte)g, (byte)rpm, (byte)(rpm >> 8)));
                    }
                    Thread.Sleep(50);
                    last = DateTime.MinValue;
                }
                if ((DateTime.Now - last).TotalSeconds >= 1)
                {
                    last = DateTime.Now;
                    TargetRpm = smart ? SmartTarget() : FixedTarget();
                    Send(Rpm(0x21, TargetRpm));   // 实时转速由 0xEF 主动上报，无需再查询
                }
                Thread.Sleep(250);
            }
            if (!running) Send(Pack(0x24));   // 退出时交还散热器自己的按键控制
        }

        void Cleanup()
        {
            wr = null;
            try { if (nt != null) nt.ValueChanged -= OnNotify; } catch { }
            nt = null; HasStatus = false;
            try { if (svc != null) svc.Dispose(); } catch { }
            svc = null; dev = null;
            CurrentRpm = 0;
        }
    }
}
