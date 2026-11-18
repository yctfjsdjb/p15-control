// 蓝天(Clevo) DCHU 硬件接口封装。
// 所有命令号均来自对原厂 Control Center 3.0 / CC40 的分析与原厂日志(OutLog)对照，
// 只使用原厂同样的调用方式，不自行写入任何功耗限制。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Threading;

namespace P15
{
    public enum GpuMode { Unknown = 0, IntegratedOnly = 1, DiscreteOnly = 2, Hybrid = 3, Dynamic = 4 }
    public enum PowerMode { Unknown = -1, Quiet = 0, PowerSaving = 1, Performance = 2, Entertainment = 3 }

    public sealed class GpuInfo
    {
        public GpuMode Current;
        public int SupportMask;   // bit0 核显 bit1 独显直连 bit2 混合 bit3 Dynamic
        public bool Supports(GpuMode m) { return m >= GpuMode.IntegratedOnly && m <= GpuMode.Dynamic && (SupportMask & (1 << ((int)m - 1))) != 0; }
    }

    public sealed class SensorInfo
    {
        public int CpuTemp, GpuTemp, Fan1Pct, Fan2Pct, Fan1Rpm, Fan2Rpm;
        public bool Valid;
    }

    /// <summary>风扇曲线：温度 T(°C) → 转速 D(%)；第 4 点固定为 100°C/100%</summary>
    public sealed class FanCurve
    {
        public int T1, T2, T3, D1, D2, D3, T2Def, T3Def, D2Def, D3Def;
        public FanCurve Clone() { return (FanCurve)MemberwiseClone(); }
        public void Reset() { T2 = T2Def; T3 = T3Def; D2 = D2Def; D3 = D3Def; }
    }

    public sealed class KbLight
    {
        public bool On, SleepOn;
        public int Level, Mode, R, G, B, SleepSeconds;   // Mode: 8 单色 2 呼吸 3 循环 4 波浪
    }

    public sealed class ChargerInfo
    {
        public bool Supported, Enabled;
        public int Start, Stop;
        public int[] StartOptions = new int[0], StopOptions = new int[0];
    }

    public sealed class KeyboardInfo
    {
        // 原厂取值：0 = 不支持，1 = 开，2 = 关
        public int FnLock, WinKeyEnabled, WinFnSwap;
    }

    /// <summary>
    /// 硬件接口 InsydeDCHU.dll（原厂 Insyde / 蓝天文件）。安装包把它放在程序目录，优先用这一份；
    /// 程序目录里没有时，再去已安装的原厂软件里找：原厂「Fn 热键与 OSD」应用（CLEVOCO.FnhotkeysandOSD）或原厂控制中心。
    /// </summary>
    public static class DchuLoader
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibraryEx(string path, IntPtr file, int flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr h, string name);
        [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);

        /// <summary>实际加载的路径；null 表示没找到（原厂服务未安装）</summary>
        public static string LoadedFrom { get; private set; }

        public static void Ensure()
        {
            if (GetModuleHandle("InsydeDCHU.dll") != IntPtr.Zero) return;
            foreach (var path in Candidates())
            {
                try
                {
                    if (!File.Exists(path)) continue;
                    var h = LoadLibraryEx(path, IntPtr.Zero, 0x8);   // LOAD_WITH_ALTERED_SEARCH_PATH
                    if (h == IntPtr.Zero) continue;
                    // 只用带完整接口的版本（原厂有的旧版缺 SetDCHU_DataEx）
                    if (GetProcAddress(h, "SetDCHU_DataEx") != IntPtr.Zero && GetProcAddress(h, "ReadAppSettings") != IntPtr.Zero
                        && GetProcAddress(h, "WriteAppSettings") != IntPtr.Zero && GetProcAddress(h, "GetDCHU_Data_Buffer") != IntPtr.Zero)
                    { LoadedFrom = path; return; }
                    FreeLibrary(h);
                }
                catch { }
            }
        }

        static IEnumerable<string> Candidates()
        {
            yield return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "InsydeDCHU.dll");
            foreach (var pkg in FnKeyPackages())
            {
                yield return Path.Combine(pkg, @"FnKey\InsydeDCHU.dll");
                yield return Path.Combine(pkg, @"FnKey\DCHU\InsydeDCHU.dll");
            }
            var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            yield return Path.Combine(pf86, @"ControlCenter\DCHU\InsydeDCHU.dll");
            yield return Path.Combine(pf86, @"ControlCenter\AppInstall\InsydeDCHU.dll");
            yield return Path.Combine(pf86, @"ControlCenter\InsydeDCHU.dll");
        }

        /// <summary>原厂 Fn 热键应用的安装目录（新版本优先）</summary>
        static IEnumerable<string> FnKeyPackages()
        {
            var list = new List<string>();
            const string key = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";
            foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
                try
                {
                    using (var k = hive.OpenSubKey(key))
                        if (k != null)
                            foreach (var n in k.GetSubKeyNames())
                                if (n.StartsWith("CLEVOCO.", StringComparison.OrdinalIgnoreCase) && n.Contains("_x64_"))
                                    using (var sk = k.OpenSubKey(n))
                                    {
                                        var root = sk == null ? null : sk.GetValue("PackageRootFolder") as string;
                                        if (!string.IsNullOrEmpty(root) && !list.Contains(root)) list.Add(root);
                                    }
                }
                catch { }
            try
            {
                var apps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
                foreach (var d in Directory.GetDirectories(apps, "CLEVOCO.*_x64_*")) if (!list.Contains(d)) list.Add(d);
            }
            catch { }
            list.Sort(StringComparer.OrdinalIgnoreCase);
            list.Reverse();
            return list;
        }
    }

    public static class Dchu
    {
        const string Dll = "InsydeDCHU.dll";

        static Dchu() { DchuLoader.Ensure(); }
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        static extern int SetDCHU_DataEx(int cmd, byte[] inBuf, int len, byte[] outBuf);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        static extern int SetDCHU_Data(int cmd, byte[] buf, int len);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        static extern int GetDCHU_Data_Integer(int cmd, ref int value);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        static extern int ReadAppSettings(int page, int offset, int len, byte[] buf);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        static extern int WriteAppSettings(int page, int offset, int len, byte[] buf);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        static extern int GetDCHU_Data_Buffer(int cmd, byte[] buf);

        // ---------- 通用读写（与原厂 InsydeDriver 封装一致） ----------
        /// <summary>原厂 SetWMI(cmd, sub, data)：4 字节小端 data，最高字节为子命令</summary>
        public static bool SetWmi(int cmd, int sub, uint data)
        {
            return Guard(() => { var b = BitConverter.GetBytes(data); b[3] = (byte)sub; SetDCHU_Data(cmd, b, 4); return true; }, false);
        }
        /// <summary>原厂 SetWMI(cmd, data)：直接 4 字节</summary>
        public static bool SetWmiRaw(int cmd, uint data)
        {
            return Guard(() => { SetDCHU_Data(cmd, BitConverter.GetBytes(data), 4); return true; }, false);
        }
        public static int GetInt(int cmd) { return Guard(() => { int v = 0; GetDCHU_Data_Integer(cmd, ref v); return v; }, 0); }
        public static byte[] GetBuffer(int cmd) { return Guard(() => { var b = new byte[256]; GetDCHU_Data_Buffer(cmd, b); return b; }, null); }
        public static byte[] ReadApp(int page, int offset, int len)
        {
            return Guard(() => { var b = new byte[Math.Max(len, 4)]; ReadAppSettings(page, offset, len, b); return b; }, null);
        }
        public static bool WriteApp(int page, int offset, params byte[] data)
        {
            return Guard(() => { WriteAppSettings(page, offset, data.Length, data); return true; }, false);
        }
        /// <summary>写入原厂事件通道（OutLog），原厂 Fn 热键程序据此执行动作</summary>
        public static bool OutLog(string msg)
        {
            try
            {
                if (!EventLog.SourceExists("PowerBIOSServer_Out")) return false;
                using (var log = new EventLog("OutLog") { Source = "PowerBIOSServer_Out" }) log.WriteEntry(msg);
                return true;
            }
            catch (Exception ex) { LastError = ex.Message; return false; }
        }

        static readonly object Gate = new object();
        public static string LastError = "";

        static T Guard<T>(Func<T> f, T fallback)
        {
            try { lock (Gate) return f(); }
            catch (Exception ex) { LastError = ex.Message; return fallback; }
        }

        // cmd 4 子命令包读写（与原厂 SetWMIPackageEx 相同）
        public static byte[] Package(params byte[] data)
        {
            return Guard(() =>
            {
                var inb = new byte[256];
                Array.Copy(data, inb, Math.Min(data.Length, 256));
                var outb = new byte[256];
                SetDCHU_DataEx(4, inb, 256, outb);
                return outb;
            }, null);
        }

        public static bool Available
        {
            get { return Guard(() => { int v = 0; GetDCHU_Data_Integer(70, ref v); return true; }, false); }
        }

        // ---------- 显卡模式 ----------
        public static GpuInfo GetGpu()
        {
            var o = Package(21);
            if (o == null) return null;
            return new GpuInfo { Current = (GpuMode)o[0], SupportMask = o[1] };
        }

        /// <summary>写入显卡模式（重启后生效）。与原厂 CC3.0/CC40 一致：cmd4 子命令22。</summary>
        public static bool SetGpu(GpuMode mode)
        {
            var g = GetGpu();
            if (g == null || !g.Supports(mode)) return false;
            return Package(22, (byte)mode) != null;
        }

        // ---------- 性能模式 ----------
        public static PowerMode GetPowerMode()
        {
            return Guard(() =>
            {
                var b = new byte[4];
                ReadAppSettings(1, 1, 1, b);
                return b[0] <= 3 ? (PowerMode)b[0] : PowerMode.Unknown;
            }, PowerMode.Unknown);
        }

        public static bool FnKeyRunning
        {
            get
            {
                try { return Process.GetProcessesByName("FnKey").Length > 0; }
                catch { return false; }
            }
        }

        /// <summary>
        /// 切换性能模式。BIOS 收到的命令和原厂完全一样：WMI 121 / 子命令 25 / 数据 = 模式号
        /// （0 安静、1 省电、2 性能、3 娱乐；性能模式开了 CPU 动态加速时再加 0x80），这是原厂 Fn 热键程序日志里记录的同一条命令，
        /// 所以 BIOS 里修改过的功耗配置照常生效。
        ///   · 原厂 Fn 热键程序在运行：和原厂控制中心一样交给它执行（它还会顺带处理低刷新率和屏幕提示）；
        ///   · 没有原厂程序（被卸载、没装）或它没响应：本程序直接发送这条命令，并把模式记到原厂设置里。
        /// </summary>
        static readonly object ModeGate = new object();

        public static bool SetPowerMode(PowerMode mode, int timeoutMs = 2500)
        {
            if (mode == PowerMode.Unknown) return false;
            // 托盘菜单和窗口几乎同时切换时排队执行：保证最后点的模式最后生效，
            // 不会出现前一次等待超时后又补发一次，把后点的模式改回去
            lock (ModeGate)
            {
                if (FnKeyRunning && OutLog("clevocc30^101^" + (int)mode))
                {
                    var sw = Stopwatch.StartNew();
                    while (sw.ElapsedMilliseconds < timeoutMs)
                    {
                        Thread.Sleep(150);
                        if (GetPowerMode() == mode) return true;
                    }
                }
                return SetPowerModeDirect(mode);
            }
        }

        /// <summary>不经过原厂程序，直接向 BIOS 发送切换命令（与原厂 Fn 热键程序发送的命令相同）</summary>
        static bool SetPowerModeDirect(PowerMode mode)
        {
            uint data = (uint)mode;
            if (mode == PowerMode.Performance && CpuBoostSupported && GetCpuBoost()) data |= 0x80;
            if (!SetWmi(121, 25, data)) { if (LastError == "") LastError = "硬件接口不可用"; return false; }
            WriteApp(1, 1, (byte)mode);   // 原厂程序也把当前模式记在这里，原厂软件和本程序读到的都一致
            Thread.Sleep(100);
            if (GetPowerMode() == mode) return true;
            LastError = "BIOS 没有接受模式切换";
            return false;
        }

        // ---------- 传感器 ----------
        /// <summary>与原厂风扇程序 Read_FanSpeed 相同：数据包 12，转速 = 2156250 / 原始值</summary>
        public static SensorInfo GetSensors()
        {
            var o = GetBuffer(12);
            if (o == null) return new SensorInfo();
            int r1 = (o[2] << 8) | o[3], r2 = (o[4] << 8) | o[5];
            return new SensorInfo
            {
                Fan1Rpm = r1 > 0 ? (int)Math.Round(2156250.0 / r1) : 0,
                Fan2Rpm = r2 > 0 ? (int)Math.Round(2156250.0 / r2) : 0,
                Fan1Pct = o[16] * 100 / 255, Fan2Pct = o[19] * 100 / 255,
                CpuTemp = o[18], GpuTemp = o[21],
                Valid = o[18] > 0 && o[18] < 120
            };
        }

        /// <summary>只读 CPU/GPU 温度（cmd4 子命令3，CPU 开销约 4ms，是 GetSensors 的 1/10，后台用这个）</summary>
        public static int[] GetTemps()
        {
            var o = Package(3);
            return o == null ? null : new[] { (int)o[7], (int)o[8] };
        }

        // ---------- 风扇（与原厂 FanSpeedSetting 一致） ----------
        public const int FanAuto = 0, FanMax = 1, FanCustom = 6;

        public static int GetFanMode() { var b = ReadApp(4, 5, 1); return b == null ? -1 : b[0]; }

        public static bool SetFanMode(int mode)
        {
            bool ok = SetWmi(121, 1, (uint)mode);
            WriteApp(4, 5, (byte)mode);
            OutLog("113^" + mode);
            return ok;
        }

        /// <summary>读取风扇曲线（原厂 APP 数据第 4 页）。[0]=CPU [1]=GPU</summary>
        public static FanCurve[] GetFanCurves()
        {
            var p = ReadApp(4, 0, 256);
            if (p == null) return null;
            var r = new FanCurve[2];
            for (int f = 0; f < 2; f++)
            {
                int o = 16 + f * 18;
                r[f] = new FanCurve
                {
                    D1 = p[o], D2 = p[o + 1], D3 = p[o + 2], D2Def = p[o + 4], D3Def = p[o + 5],
                    T1 = p[o + 6], T2 = p[o + 7], T3 = p[o + 8], T2Def = p[o + 10], T3Def = p[o + 11]
                };
                if (r[f].T3 == 0) return null;
            }
            return r;
        }

        static int Slope(int d1, int d2, int t1, int t2)
        {
            return t2 == t1 ? 0 : (int)Math.Round((double)(d2 - d1) / (t2 - t1) * 2.55 * 16.0);
        }

        /// <summary>写入自定义风扇曲线并切到自定义模式（与原厂 Write_WMI14 + SetFanInfo + SetFanMode(6) 相同）</summary>
        public static bool SetFanCurves(FanCurve cpu, FanCurve gpu)
        {
            Func<int, byte> raw = d => (byte)Math.Round(d / 100.0 * 255.0);
            var fans = new[] { cpu, gpu };
            var t = new byte[256];
            for (int f = 0; f < 2; f++)
            {
                var c = fans[f];
                t[2 + f * 4] = (byte)c.T2; t[3 + f * 4] = raw(c.D2);
                t[4 + f * 4] = (byte)c.T3; t[5 + f * 4] = raw(c.D3);
                int[] rs = { Slope(c.D1, c.D2, c.T1, c.T2), Slope(c.D2, c.D3, c.T2, c.T3), Slope(c.D3, 100, c.T3, 100) };
                for (int k = 0; k < 3; k++) { t[14 + f * 6 + k * 2] = (byte)(rs[k] >> 8); t[15 + f * 6 + k * 2] = (byte)(rs[k] & 0xFF); }
            }
            bool ok = Guard(() => { SetDCHU_Data(14, t, 256); return true; }, false);
            if (!ok) return false;

            var p = ReadApp(4, 0, 256) ?? new byte[256];
            var page = new byte[256];
            Array.Copy(p, page, 8);
            page[5] = FanCustom;
            for (int f = 0; f < 2; f++)
            {
                var c = fans[f]; int o = 16 + f * 18;
                page[o] = (byte)c.D1; page[o + 1] = (byte)c.D2; page[o + 2] = (byte)c.D3; page[o + 3] = 100;
                page[o + 4] = (byte)c.D2Def; page[o + 5] = (byte)c.D3Def;
                page[o + 6] = (byte)c.T1; page[o + 7] = (byte)c.T2; page[o + 8] = (byte)c.T3; page[o + 9] = 100;
                page[o + 10] = (byte)c.T2Def; page[o + 11] = (byte)c.T3Def;
                int[] rs = { Slope(c.D1, c.D2, c.T1, c.T2), Slope(c.D2, c.D3, c.T2, c.T3), Slope(c.D3, 100, c.T3, 100) };
                for (int k = 0; k < 3; k++) { page[o + 12 + k * 2] = (byte)(rs[k] & 0xFF); page[o + 13 + k * 2] = (byte)(rs[k] >> 8); }
            }
            WriteApp(4, 0, page);
            return SetFanMode(FanCustom);
        }

        // ---------- 键盘灯（RGB 三区键盘，与原厂 LedKeyboardSetting 一致） ----------
        public static KbLight GetKbLight()
        {
            var p = ReadApp(2, 0, 100);
            if (p == null) return null;
            return new KbLight
            {
                Mode = p[32], Level = p[35], SleepOn = p[36] == 1,
                SleepSeconds = p[37] * 3600 + p[38] * 60 + p[39],
                On = p[80] == 1, R = p[81], G = p[82], B = p[83]
            };
        }

        static uint KbColor(int r, int g, int b)
        {
            if (r == 0 && g == 255 && b == 127) return (uint)(0x460000 | (r << 8) | g);
            return (uint)(((int)Math.Round(0.784313725490196 * b) << 16) | (r << 8) | g);
        }

        static readonly int[] KbLevels = { 0, 47, 95, 143, 191 };

        /// <summary>应用并保存键盘灯设置；保存到 BIOS APP 数据，原厂程序开机时会按这里恢复</summary>
        public static bool SetKbLight(KbLight k)
        {
            k.Level = Math.Max(1, Math.Min(4, k.Level));
            // 开关
            SetWmiRaw(103, 0xF4000000);
            SetWmiRaw(103, k.On ? 0xE007B001u : 0xE0000007u);
            if (k.On)
            {
                SetWmiRaw(103, 0xF4000000u + (uint)KbLevels[k.Level]);
                uint col = KbColor(k.R, k.G, k.B);
                switch (k.Mode)
                {
                    case 2: // 呼吸
                        for (uint z = 0xF0; z <= 0xF2; z++) SetWmiRaw(103, (z << 24) | col);
                        SetWmiRaw(103, 0x1002A000);
                        break;
                    case 3: SetWmiRaw(103, 0x33010000); break;          // 循环
                    case 4: SetWmiRaw(103, 0xB0000000); break;          // 波浪
                    default: // 单色（三区同色）
                        k.Mode = 8;
                        for (uint z = 0xF0; z <= 0xF2; z++) SetWmiRaw(103, (z << 24) | col);
                        break;
                }
            }
            // 背光自动关闭
            SetWmiRaw(121, k.SleepOn ? (0x18000000u + ((uint)k.SleepSeconds << 8)) | 0xFF : 0x18000000u);

            byte rr = (byte)k.R, gg = (byte)k.G, bb = (byte)k.B;
            WriteApp(2, 80, (byte)(k.On ? 1 : 0));
            WriteApp(2, 35, (byte)k.Level);
            WriteApp(2, 32, (byte)k.Mode);
            WriteApp(2, 81, rr, gg, bb);
            WriteApp(2, 85, rr, gg, bb); WriteApp(2, 89, rr, gg, bb); WriteApp(2, 93, rr, gg, bb);
            WriteApp(2, 36, (byte)(k.SleepOn ? 1 : 0));
            WriteApp(2, 37, (byte)(k.SleepSeconds / 3600), (byte)(k.SleepSeconds / 60 % 60), (byte)(k.SleepSeconds % 60));
            return true;
        }

        // ---------- 显示：蓝光滤镜 / 省电降刷新率 ----------
        public static int GetBlueLight() { var b = ReadApp(1, 34, 1); return b == null ? 0 : Math.Min(10, (int)b[0]); }

        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool SetDeviceGammaRamp(IntPtr dc, ushort[] ramp);

        /// <summary>与原厂 BlueLightFilter 相同：红绿线性，蓝色 = 256/(1+0.1×级别)</summary>
        public static bool ApplyBlueLight(int level)
        {
            double range = 1.0 + 0.1 * Math.Max(0, Math.Min(10, level));
            var ramp = new ushort[768];
            for (int i = 0; i < 256; i++)
            {
                ramp[i] = ramp[256 + i] = (ushort)(256 * i);
                ramp[512 + i] = (ushort)(256.0 / range * i);
            }
            var dc = GetDC(IntPtr.Zero);
            try { return SetDeviceGammaRamp(dc, ramp); } finally { ReleaseDC(IntPtr.Zero, dc); }
        }

        public static bool SetBlueLight(int level)
        {
            WriteApp(1, 34, (byte)level);
            return ApplyBlueLight(level);
        }

        public static bool GetLowRefresh() { var b = ReadApp(1, 28, 1); return b != null && b[0] == 1; }
        public static bool SetLowRefresh(bool on)
        {
            WriteApp(1, 28, (byte)(on ? 1 : 0));
            return OutLog("clevocc30^114");
        }

        // ---------- CPU 动态加速（原厂选项，仅性能模式下有效） ----------
        /// <summary>与原厂一致：BIOS 特性标志（GET 16）第 12 位 = 支持 DTT 动态加速，不支持时原厂不显示该选项</summary>
        public static bool CpuBoostSupported { get { return ((GetInt(16) >> 12) & 1) == 1; } }
        public static bool GetCpuBoost() { var b = ReadApp(1, 32, 1); return b != null && b[0] == 1; }
        public static bool SetCpuBoost(bool on)
        {
            if (GetPowerMode() != PowerMode.Performance) { LastError = "只有性能模式下才能设置"; return false; }
            WriteApp(1, 32, (byte)(on ? 1 : 0));
            return SetWmi(121, 25, on ? 130u : 2u);
        }

        // ---------- 摄像头（蓝天 0x22 开关 / 0x06 状态） ----------
        public static bool GetCamera() { return (GetInt(0x06) & 1) == 1; }
        public static bool SetCamera(bool on) { return SetWmiRaw(0x22, on ? 1u : 0u); }

        // ---------- 充电保护 ----------
        public static ChargerInfo GetCharger()
        {
            var o = Package(30);
            var c = new ChargerInfo();
            if (o == null) return c;
            c.Enabled = o[0] != 0; c.Start = o[1]; c.Stop = o[2];
            var s = new System.Collections.Generic.List<int>();
            for (int i = 16; i < 32; i++) if (o[i] > 0) s.Add(o[i]);
            var t = new System.Collections.Generic.List<int>();
            for (int i = 32; i < 48; i++) if (o[i] > 0) t.Add(o[i]);
            c.StartOptions = s.ToArray(); c.StopOptions = t.ToArray();
            c.Supported = s.Count > 0 && t.Count > 0;
            return c;
        }

        public static bool SetCharger(bool enabled, int start, int stop)
        {
            return Package(31, (byte)(enabled ? 1 : 0), (byte)start, (byte)stop) != null;
        }

        // ---------- 键盘 ----------
        public static KeyboardInfo GetKeyboard()
        {
            var o = Package(24);
            if (o == null) return new KeyboardInfo();
            return new KeyboardInfo { FnLock = o[0], WinKeyEnabled = o[1], WinFnSwap = o[2] };
        }

        public static bool SetKeyboard(KeyboardInfo k)
        {
            // 原厂：子命令25，[1]=Fn锁 [2]=Win键 [3]=Win/Fn互换，1=开 2=关；
            // 原厂对不支持的项发送"未勾选"的默认值(Fn锁关/Win键开/互换关)，这里保持一致
            byte a = (byte)(k.FnLock == 1 ? 1 : 2);
            byte b = (byte)(k.WinKeyEnabled == 2 ? 2 : 1);
            byte c = (byte)(k.WinFnSwap == 1 ? 1 : 2);
            return Package(25, a, b, c) != null;
        }
    }
}
