// 飞智 BS2 / BS2 Pro 散热器插件（P15Cooler.dll）。
// 只有用户在控制中心里选择「安装」后才会释放并加载；不安装就完全不加载、不占任何资源。
using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace P15
{
    public sealed class CoolerModule : ICoolerModule
    {
        readonly Bs2Cooler cooler = new Bs2Cooler();
        Action<string, bool> say;
        Card card;
        Toggle coolOn, coolLight;
        Label2 coolStatus, fixedVal;
        Btn coolFx, bCurve;
        Choice tabSmart, tabFixed;
        readonly Choice[] smartModes = new Choice[3];
        Slider fixedSlider;
        DateTime lightSetAt = DateTime.MinValue;
        static readonly Bs2Mode[] SmartList = { Bs2Mode.SmartQuiet, Bs2Mode.SmartPerf, Bs2Mode.SmartCustom };

        void Say(string m, bool bad = false) { var s = say; if (s != null) s(m, bad); }
        bool IsSmart { get { return cooler.Mode == Bs2Mode.SmartQuiet || cooler.Mode == Bs2Mode.SmartPerf || cooler.Mode == Bs2Mode.SmartCustom; } }

        /// <summary>后台引擎：按保存的设置连接散热器，不建任何界面</summary>
        public void Start()
        {
            int m; int.TryParse(Settings.Get("bs2Mode", "0"), out m);
            int fr; int.TryParse(Settings.Get("bs2FixedRpm", "2400"), out fr);
            // 旧版 1~4 档转换成固定转速
            if (m >= 2 && m <= 5) { fr = new[] { 1700, 2400, 3000, 4000 }[m - 2]; m = (int)Bs2Mode.Fixed; Settings.Set("bs2Mode", m.ToString()); Settings.Set("bs2FixedRpm", fr.ToString()); }
            cooler.FixedRpm = fr;
            var curve = LoadCurve();
            if (curve != null) cooler.CustomCurve = curve;
            cooler.Mode = Enum.IsDefined(typeof(Bs2Mode), m) ? (Bs2Mode)m : Bs2Mode.SmartQuiet;
            cooler.LightOn = Settings.GetBool("bs2Light", true);
            // 开机（托盘启动）时：散热器开关打开且允许开机自动连接才连接
            if (Settings.GetBool("bs2On", false) && Settings.GetBool("bs2AutoConnect", true)) cooler.Start();
        }

        static int[] LoadCurve()
        {
            var s = Settings.Get("bs2Curve", "");
            if (s == "") return null;
            try { var v = Array.ConvertAll(s.Split(','), int.Parse); return v.Length == 8 ? v : null; } catch { return null; }
        }

        void SetMode(Bs2Mode m)
        {
            cooler.Mode = m;
            Settings.Set("bs2Mode", ((int)m).ToString());
            Sync();
        }

        public Control CreateCard(int width, Action<string, bool> sayCallback)
        {
            say = sayCallback;
            var c = card = new Card { Caption = "飞智 BS2 Pro", Size = new Size(width, 172) };
            int iw = c.Width - 28;
            coolOn = new Toggle { Location = new Point(c.Width - 14 - 44, 8) };
            coolStatus = new Label2("未启用", true) { Bounds = new Rectangle(112, 8, iw - 150, 24), Align = ContentAlignment.MiddleRight };
            c.Controls.Add(coolOn); c.Controls.Add(coolStatus);

            // 散热模式：智能变频 / 固定转速
            int tw = (iw - 6) / 2;
            tabSmart = new Choice(null, "智能变频", null) { Bounds = new Rectangle(14, 40, tw, 30) };
            tabFixed = new Choice(null, "固定转速", null) { Bounds = new Rectangle(20 + tw, 40, tw, 30) };
            tabSmart.Click += (s, e) => { if (!IsSmart) SetMode(SmartList[Math.Max(0, Array.IndexOf(SmartList, LastSmart()))]); };
            tabFixed.Click += (s, e) => { if (IsSmart) { Settings.Set("bs2LastSmart", ((int)cooler.Mode).ToString()); SetMode(Bs2Mode.Fixed); } };
            c.Controls.Add(tabSmart); c.Controls.Add(tabFixed);

            // 智能变频：静音 / 性能 / 自定义（自定义可编辑曲线）
            string[] sn = { "静音", "性能", "自定义" };
            for (int i = 0; i < 3; i++)
            {
                var m = SmartList[i];
                smartModes[i] = new Choice(null, sn[i], null) { Bounds = new Rectangle(14 + i * 72, 78, 66, 30) };
                smartModes[i].Click += (s, e) => SetMode(m);
                c.Controls.Add(smartModes[i]);
            }
            bCurve = new Btn("编辑曲线") { Bounds = new Rectangle(c.Width - 14 - 90, 78, 90, 30) };
            bCurve.Click += (s, e) => EditCurve();
            c.Controls.Add(bCurve);

            // 固定转速：任意转速滑块
            fixedSlider = new Slider { Bounds = new Rectangle(6, 79, iw - 74, 28) };
            fixedVal = new Label2("", false) { Bounds = new Rectangle(c.Width - 14 - 78, 78, 78, 30), Align = ContentAlignment.MiddleRight };
            fixedSlider.ValueChanged += (s, e) =>
            {
                cooler.FixedRpm = 1300 + fixedSlider.Value * 100;
                fixedVal.Text = cooler.FixedRpm + " rpm";
            };
            // 拖动过程中只改转速，松手再保存（避免拖动时反复写设置文件）
            fixedSlider.MouseUp += (s, e) => Settings.Set("bs2FixedRpm", cooler.FixedRpm.ToString());
            c.Controls.Add(fixedSlider); c.Controls.Add(fixedVal);

            coolLight = new Toggle();
            c.Controls.Add(new SettingRow("灯光", "", coolLight, 40) { Bounds = new Rectangle(14, 120, iw, 40) });
            coolFx = new Btn("灯效") { Bounds = new Rectangle(70, 125, 60, 30) };
            coolFx.Click += (s, e) => EditLight();
            var bSet = new Btn("功能设置") { Bounds = new Rectangle(136, 125, 84, 30) };
            bSet.Click += (s, e) => EditSettings();
            c.Controls.Add(coolFx); coolFx.BringToFront();
            c.Controls.Add(bSet); bSet.BringToFront();

            coolLight.On = cooler.LightOn;
            coolOn.On = Settings.GetBool("bs2On", false);
            coolLight.Toggled += (s, e) => { lightSetAt = DateTime.Now; cooler.LightOn = coolLight.On; Settings.SetBool("bs2Light", coolLight.On); };
            coolOn.Toggled += (s, e) =>
            {
                if (coolOn.On && !FlydigiServiceCheck()) { coolOn.On = false; return; }
                Settings.SetBool("bs2On", coolOn.On);
                if (coolOn.On) cooler.Start(); else Task.Run(() => cooler.Stop());
                Sync();
            };
            // 卡片随窗口销毁时取消订阅，引擎继续在后台运行
            Action onChanged = () => { if (c.IsHandleCreated && !c.IsDisposed) try { c.BeginInvoke(new Action(Sync)); } catch { } };
            cooler.Changed += onChanged;
            // 关了「开机自动连接」时开机不连；用户打开窗口时再连接
            if (coolOn.On && cooler.State == Bs2State.Off) cooler.Start();
            c.Disposed += (s, e) => { cooler.Changed -= onChanged; if (card == c) card = null; };
            Sync();
            return c;
        }

        Bs2Mode LastSmart()
        {
            int m; int.TryParse(Settings.Get("bs2LastSmart", "0"), out m);
            return Array.IndexOf(SmartList, (Bs2Mode)m) >= 0 ? (Bs2Mode)m : Bs2Mode.SmartQuiet;
        }

        public void Shutdown()
        {
            try { cooler.Stop(); } catch { }   // 断开时交还散热器自己的按键控制
        }

        void Sync()
        {
            if (card == null || card.IsDisposed) return;
            bool on = coolOn.On, smart = IsSmart;
            tabSmart.Selected = smart; tabFixed.Selected = !smart;
            tabSmart.Enabled = tabFixed.Enabled = on;
            for (int i = 0; i < 3; i++) { smartModes[i].Visible = smart; smartModes[i].Selected = cooler.Mode == SmartList[i]; smartModes[i].Enabled = on; }
            bCurve.Visible = smart && cooler.Mode == Bs2Mode.SmartCustom; bCurve.Enabled = on;
            fixedSlider.Visible = fixedVal.Visible = !smart;
            // 滑块上限随供电变化：5V 2700 / 9V 3300 / 20V 4000
            fixedSlider.Max = (cooler.MaxRpm - 1300) / 100;
            fixedSlider.Value = (Math.Min(cooler.FixedRpm, cooler.MaxRpm) - 1300) / 100;
            fixedSlider.Enabled = on;
            fixedVal.Text = Math.Min(cooler.FixedRpm, cooler.MaxRpm) + " rpm";
            coolLight.Enabled = on;
            coolFx.Enabled = on && cooler.State == Bs2State.Connected;
            bool live = on && cooler.State == Bs2State.Connected && cooler.HasStatus;
            // 灯光开关显示散热器的真实状态（包括按散热器自己的按键改的），刚点过的 2 秒内以点击为准
            if (live && (DateTime.Now - lightSetAt).TotalSeconds > 2 && coolLight.On != cooler.ActualLight)
            {
                coolLight.On = cooler.ActualLight;
                cooler.SyncLight(cooler.ActualLight);
            }
            coolStatus.Text = !on ? "未启用"
                : cooler.State != Bs2State.Connected ? "正在连接…"
                : cooler.CurrentRpm + " / " + cooler.MaxRpm + " rpm";
        }

        void EditCurve()
        {
            var r = CurveDialog.Show(card.FindForm(), cooler.CustomCurve, cooler.MaxRpm);
            if (r == null) return;
            cooler.CustomCurve = r;
            Settings.Set("bs2Curve", string.Join(",", r));
            Say("自定义曲线已保存并生效");
        }

        static int ParseStartStop() { int v; return int.TryParse(Settings.Get("bs2StartStop", "1"), out v) && v >= 0 && v <= 2 ? v : 1; }

        void EditSettings()
        {
            var init = new CoolerSettingsDialog.Result
            {
                AutoStart = Settings.GetBool("bs2AutoStart", true),
                StartStop = ParseStartStop(),
                GearLed = Settings.GetBool("bs2GearLed", true),
                AutoConnect = Settings.GetBool("bs2AutoConnect", true) && AutoStart.Enabled
            };
            var r = CoolerSettingsDialog.Show(card.FindForm(), init);
            if (r == null) return;
            // 设备上的选项只在改动时下发；没连上时会排队，连上后自动发送
            if (r.AutoStart != init.AutoStart) { cooler.SetAutoStart(r.AutoStart); Settings.SetBool("bs2AutoStart", r.AutoStart); }
            if (r.StartStop != init.StartStop) { cooler.SetSmartStartStop(r.StartStop); Settings.Set("bs2StartStop", r.StartStop.ToString()); }
            if (r.GearLed != init.GearLed) { cooler.SetGearLed(r.GearLed); Settings.SetBool("bs2GearLed", r.GearLed); }
            if (r.AutoConnect != init.AutoConnect)
            {
                Settings.SetBool("bs2AutoConnect", r.AutoConnect);
                if (r.AutoConnect)
                {
                    if (!AutoStart.Enabled) AutoStart.Set(true);   // 需要控制中心开机自启（托盘运行）才能开机连接
                    if (!coolOn.On) { coolOn.On = true; Settings.SetBool("bs2On", true); cooler.Start(); }
                }
                // 关闭时只是开机不自动连接，不影响控制中心本身的开机自启
            }
            Say(cooler.State == Bs2State.Connected ? "散热器设置已保存" : "设置已保存，散热器连上后自动生效");
            Sync();
        }

        void EditLight()
        {
            int m, sp, br; Color col;
            int.TryParse(Settings.Get("bs2RgbMode", "1"), out m);
            int.TryParse(Settings.Get("bs2RgbSpeed", "6"), out sp);
            int.TryParse(Settings.Get("bs2RgbBright", "100"), out br);
            try { col = ColorTranslator.FromHtml(Settings.Get("bs2RgbColor", "#0050FF")); } catch { col = Color.FromArgb(0, 80, 255); }
            var r = CoolerLightDialog.Show(card.FindForm(), new CoolerLightDialog.Result { Mode = (Bs2Cooler.RgbMode)m, Color = col, Speed = Math.Max(1, Math.Min(10, sp)), Bright = Math.Max(10, Math.Min(100, br)) });
            if (r == null) return;
            Settings.Set("bs2RgbMode", ((int)r.Mode).ToString());
            Settings.Set("bs2RgbSpeed", r.Speed.ToString());
            Settings.Set("bs2RgbBright", r.Bright.ToString());
            Settings.Set("bs2RgbColor", ColorTranslator.ToHtml(Color.FromArgb(r.Color.R, r.Color.G, r.Color.B)));
            // 速度 1~10 → 每步时间 50~5（越小越快）
            cooler.SetRgb(r.Mode, r.Color.R, r.Color.G, r.Color.B, (11 - r.Speed) * 5, r.Bright);
            if (!coolLight.On) { coolLight.On = true; lightSetAt = DateTime.Now; cooler.LightOn = true; Settings.SetBool("bs2Light", true); }
            Say("灯效已发送到散热器");
            Task.Delay(3000).ContinueWith(_ =>
            {
                var cc = card;
                if (cc != null && !cc.IsDisposed) cc.BeginInvoke(new Action(() => Say(cooler.RgbAcked ? "散热器已确认新灯效" : "散热器没有确认灯效，请确认已连接", !cooler.RgbAcked)));
            });
        }

        /// <summary>飞智太空站服务也会控制散热器，两边同时控制会互相抢。启用前提示停用它。</summary>
        bool FlydigiServiceCheck()
        {
            try
            {
                using (var sc = new System.ServiceProcess.ServiceController("Flydigi Space Station Service"))
                {
                    if (sc.Status != System.ServiceProcess.ServiceControllerStatus.Running) return true;
                    if (!Dialog.Ask(card.FindForm(), "停用飞智太空站？", "飞智太空站正在后台运行（约 80MB 内存），会和这里抢散热器的控制权。\n要停用它并取消开机自启吗？以后可在「服务」里恢复。", "停用", "取消"))
                        return false;
                    foreach (var p in Process.GetProcessesByName("Flydigi Space Station")) try { p.Kill(); } catch { }
                    sc.Stop(); sc.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                    Process.Start(new ProcessStartInfo("sc.exe", "config \"Flydigi Space Station Service\" start= disabled") { CreateNoWindow = true, UseShellExecute = false }).WaitForExit(5000);
                    Say("已停用飞智太空站服务");
                }
            }
            catch (InvalidOperationException) { }   // 没装飞智太空站
            catch (Exception ex) { Say("停用飞智服务失败：" + ex.Message, true); return false; }
            return true;
        }
    }
}
