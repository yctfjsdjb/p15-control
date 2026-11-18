using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace P15
{
    /// <summary>主页小数据：数字 + 单位，下面一行说明。不加底框</summary>
    sealed class Stat : Owner
    {
        public string Value = "--", Unit = "", Caption = "";
        bool hot;
        public Stat(string caption, string unit) { Caption = caption; Unit = unit; }
        public void Set(string v, float lvl = -1, bool warn = false)
        {
            if (v == Value && warn == hot) return;
            Value = v; hot = warn; Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; var p = Theme.P;
            Back(g);
            var vs = Theme.Measure(g, Value, Theme.Num);
            Theme.Text(g, Value, Theme.Num, hot ? p.Warn : p.Text, new RectangleF(0, 0, vs.Width + 4, 24), ContentAlignment.TopLeft);
            Theme.Text(g, Unit, Theme.Small, p.SubText, new RectangleF(vs.Width + 3, 6, Width - vs.Width - 3, 16), ContentAlignment.TopLeft);
            Theme.Text(g, Caption, Theme.Small, p.SubText, new RectangleF(0, 24, Width, 16), ContentAlignment.TopLeft);
        }
    }

    /// <summary>主页温度读数：大号数字 + 刻度格（像仪表上的电量格），下面是风扇转速</summary>
    sealed class Gauge : Owner
    {
        public string Caption, Value = "--", Sub = "";
        float level;
        bool hot;
        public Gauge(string caption) { Caption = caption; }
        public void Set(string v, float lvl = 0, bool warn = false)
        {
            lvl = Math.Max(0, Math.Min(1f, lvl));
            if (v == Value && Math.Abs(lvl - level) < 0.005f && warn == hot) return;
            Value = v; level = lvl; hot = warn; Invalidate();
        }
        public void SetSub(string s) { if (s != Sub) { Sub = s; Invalidate(); } }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; var p = Theme.P;
            Back(g);
            var col = hot ? p.Warn : p.Text;
            Theme.Text(g, Caption, Theme.Small, p.SubText, new RectangleF(0, 0, Width, 16), ContentAlignment.TopLeft);
            var vs = Theme.Measure(g, Value, Theme.Hero);
            Theme.Text(g, Value, Theme.Hero, col, new RectangleF(-1, 14, vs.Width + 6, vs.Height + 2), ContentAlignment.TopLeft);
            Theme.Text(g, "°C", Theme.Small, p.SubText, new RectangleF(vs.Width + 3, 24, 30, 16), ContentAlignment.TopLeft);
            // 刻度格：0~100°C，每格 4°C
            const int pitch = 6, tw = 3, th = 9, ty = 68;
            int n = (Width + pitch - tw) / pitch, lit = (int)Math.Round(level * n);
            var sm = g.SmoothingMode; g.SmoothingMode = SmoothingMode.None;
            using (var on = new SolidBrush(col)) using (var off = new SolidBrush(p.Track))
                for (int i = 0; i < n; i++) g.FillRectangle(i < lit ? on : off, i * pitch, ty, tw, th);
            g.SmoothingMode = sm;
            Theme.Text(g, Sub, Theme.Small, p.SubText, new RectangleF(0, ty + th + 6, Width, 16), ContentAlignment.TopLeft);
        }
    }

    /// <summary>标题栏标志：几何闪电 + P15 CONTROL 字标（粗细对比，单色）</summary>
    sealed class BrandMark : Owner
    {
        static Font bold, light;
        static FontFamily fb, fl;
        static void InitFont()
        {
            if (bold != null) return;
            try { fb = new FontFamily("Bahnschrift SemiBold"); fl = new FontFamily("Bahnschrift Light"); }
            catch { fb = new FontFamily("Segoe UI Semibold"); fl = new FontFamily("Segoe UI Light"); }
            bold = new Font(fb, 18f, FontStyle.Regular, GraphicsUnit.Pixel);
            light = new Font(fl, 18f, FontStyle.Regular, GraphicsUnit.Pixel);
        }
        static float Word(Graphics g, GraphicsPath path, string s, FontFamily fam, Font font, float x, float y, float track)
        {
            foreach (char ch in s)
            {
                var cs = ch.ToString();
                path.AddString(cs, fam, (int)FontStyle.Regular, font.Size, new PointF(x, y), StringFormat.GenericTypographic);
                x += g.MeasureString(cs, font, PointF.Empty, StringFormat.GenericTypographic).Width + track;
            }
            return x;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; var p = Theme.P;
            Back(g);
            InitFont();
            Brand.DrawBolt(g, new RectangleF(1, 7, 14, 21), p.Text);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = new GraphicsPath())
            {
                float x = Word(g, path, "P15", fb, bold, 22, 8, 0.4f);
                Word(g, path, "CONTROL", fl, light, x + 6, 8, 1.8f);
                using (var b = new SolidBrush(p.Text)) g.FillPath(b, path);
            }
        }
    }
    /// <summary>标题栏里的提示：临时替换字标显示，几秒后自动消失，不遮挡页面内容</summary>
    sealed class Toast : Label2
    {
        public Toast() : base("", false) { }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; var p = Theme.P;
            Back(g);
            var f = Theme.Measure(g, Text, Theme.Body).Width <= Width ? Theme.Body : Theme.Small;
            Theme.Text(g, Text, f, Fore ?? p.Text, ClientRectangle, ContentAlignment.MiddleLeft);
        }
    }
    public sealed class MainForm : GlassForm
    {
        const int W = 380, H = 700, HeaderH = 52, NavH = 60, Pad = 10;
        readonly GlassPanel content = new GlassPanel();
        // 页面第一次打开时才创建：打开窗口更快，没去过的页面不占内存
        readonly Panel[] pages = new Panel[6];
        Func<Panel>[] builders;
        readonly List<Choice> navs = new List<Choice>();
        readonly Toast status = new Toast();
        int page;
        readonly TrayHost host;
        WheelRouter wheel;
        readonly LiteScroll thumb = new LiteScroll();
        BrandMark brand;
        readonly System.Windows.Forms.Timer sayTimer = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer sensorTimer = new System.Windows.Forms.Timer { Interval = 2000 };

        // 主页
        Gauge sCpu, sGpu;
        Stat sCpuLoad, sCpuClk, sGpuLoad, sMem;
        readonly Dictionary<PowerMode, Choice> powerBtns = new Dictionary<PowerMode, Choice>();
        Toggle boost;
        SettingRow boostRow;
        SysMonitor sysMon = new SysMonitor();
        bool sampling;
        // 风扇
        Choice fAuto, fMax, fCustom, fTabCpu, fTabGpu;
        FanCurveEditor fEditor;
        FanCurve[] curves;
        int curveIdx;
        Label2 fInfo;
        // 灯光
        Toggle kbOn, kbSleep, lowRefresh;
        SettingRow lowRefreshRow;
        readonly Choice[] kbLevel = new Choice[4];
        readonly Dictionary<int, Choice> kbModes = new Dictionary<int, Choice>();
        readonly List<Swatch> swatches = new List<Swatch>();
        Select kbSleepSec;
        Slider blue;
        Label2 blueVal;
        KbLight kb;
        bool loadingUi;
        // 设备开关
        Toggle tpOn, camOn;
        Choice chgMax, chgRec, chgCustom;
        // 显卡
        Choice gDirect, gHybrid;
        Label2 gNow, gNext;
        Btn gReboot;
        // 设备
        Toggle chgOn, kbFn, kbWin, kbSwap;
        Select chgStart, chgStop;
        Btn chgApply;
        // 驱动
        DateTime drvChecked = DateTime.MinValue;
        // 设置
        readonly Choice[] tLevel = new Choice[3];
        Toggle autoStart, closeToTray;
        Label2 about;

        public MainForm(TrayHost host)
        {
            this.host = host;
            Text = Brand.Name;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(W, H);
            var wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Right - W - 16, wa.Bottom - H - 16);
            Icon = AppIcon.Get(0);
            KeyPreview = true;

            int glassLv; Theme.Level = int.TryParse(Settings.Get("glass", "1"), out glassLv) ? Math.Max(0, Math.Min(2, glassLv)) : 1;

            BuildHeader();
            content.SetBounds(0, HeaderH, W, H - HeaderH - NavH);
            Controls.Add(content);
            BuildNav();

            builders = new Func<Panel>[] { BuildHome, BuildFan, BuildLight, BuildDevice, BuildDrivers, BuildSettings };
            thumb.SetBounds(W - 9, 4, 8, content.Height - 8);
            content.Controls.Add(thumb);

            sayTimer.Tick += (s, e) => Say("");

            wheel = new WheelRouter(() => thumb);
            Application.AddMessageFilter(wheel);
            sensorTimer.Tick += (s, e) => RefreshSensors();
            host.PowerChanged += OnHostPowerChanged;
            ShowPage(0);
        }

        void OnHostPowerChanged() { if (!IsDisposed && page == 0) RefreshHome(); }

        /// <summary>窗口关闭即销毁：停掉定时器、释放计数器、解除所有静态事件订阅，后台只剩托盘</summary>
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            sensorTimer.Stop(); sensorTimer.Dispose();
            sayTimer.Stop(); sayTimer.Dispose();
            try { sysMon.Dispose(); } catch { }
            Application.RemoveMessageFilter(wheel);
            host.PowerChanged -= OnHostPowerChanged;
            Theme.ClearHandlers();
            base.OnFormClosed(e);   // 非模态窗口关闭后 WinForms 会自动 Dispose 整个窗口
        }

        // ================= 窗口外观 =================
        void BuildHeader()
        {
            var head = new GlassPanel { Bounds = new Rectangle(0, 0, W, HeaderH) };
            MouseEventHandler drag = (s, e) => { if (e.Button == MouseButtons.Left) DragMove(); };
            head.MouseDown += drag;
            brand = new BrandMark { Bounds = new Rectangle(Pad + 13, 8, 200, 36) };
            brand.MouseDown += drag;
            status.Bounds = new Rectangle(Pad + 14, 6, W - 86 - 8 - (Pad + 14), 40);
            status.Visible = false;
            status.MouseDown += drag;
            var min = new IconBtn("\uE921") { Bounds = new Rectangle(W - 86, 11, 34, 32) };
            var close = new IconBtn("\uE8BB") { Bounds = new Rectangle(W - 48, 11, 34, 32) };
            min.Click += (s, e) => WindowState = FormWindowState.Minimized;
            close.Click += (s, e) => Close();
            head.Controls.AddRange(new Control[] { brand, status, min, close });
            Controls.Add(head);
        }

        /// <summary>玻璃背景容器（页面留白用）</summary>
        sealed class Owner2 : GlassPanel { }

        /// <summary>底部导航条：只有顶部一条细线</summary>
        sealed class NavBar : GlassPanel
        {
            protected override void OnPaintBackground(PaintEventArgs e)
            {
                base.OnPaintBackground(e);
                Theme.Rule(e.Graphics, 0, Width, 0, Theme.P.Stroke);
            }
        }        void BuildNav()
        {
            string[] glyph = { "", "", "", "", "", "" };
            string[] name = { "主页", "性能", "灯光", "设备", "驱动", "设置" };
            var nav = new NavBar { Bounds = new Rectangle(0, H - NavH, W, NavH) };
            int bw = (W - 16) / glyph.Length;
            for (int i = 0; i < glyph.Length; i++)
            {
                int idx = i;
                var b = new NavBtn(glyph[i], name[i]) { Bounds = new Rectangle(8 + i * bw, 6, bw, NavH - 12) };
                b.Click += (s, e) => ShowPage(idx);
                navs.Add(b); nav.Controls.Add(b);
            }
            Controls.Add(nav);
        }

        sealed class IconBtn : Owner
        {
            readonly string glyph;
            public IconBtn(string g) { glyph = g; Cursor = Cursors.Hand; }
            protected override void OnPaint(PaintEventArgs e)
            {
                var gr = e.Graphics; var p = Theme.P;
                Back(gr);
                if (Hover) Theme.Fill(gr, new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 7, p.Inset);
                Theme.Text(gr, glyph, Theme.IconTiny, Hover ? p.Text : p.SubText, ClientRectangle, ContentAlignment.MiddleCenter);
            }
        }

        sealed class NavBtn : Choice
        {
            public NavBtn(string g, string t) : base(g, t, null) { }
            protected override void OnPaint(PaintEventArgs e)
            {
                var gr = e.Graphics; var p = Theme.P;
                Back(gr);
                var c = Selected || Hover ? p.Text : p.SubText;
                Theme.Text(gr, Glyph, Theme.Icon, c, new RectangleF(0, 4, Width, 24), ContentAlignment.MiddleCenter);
                Theme.Text(gr, Text, Selected ? Theme.SmallBold : Theme.Small, c, new RectangleF(0, 28, Width, 16), ContentAlignment.MiddleCenter);
                if (Selected) Theme.Fill(gr, new RectangleF(Width / 2f - 8, Height - 3, 16, 2), 1, p.Solid);
            }
        }
        Panel EnsurePage(int i)
        {
            if (pages[i] != null) return pages[i];
            var p = builders[i]();
            p.SetBounds(0, 0, scrollPages.Contains(p) ? W + LiteScroll.Hidden : W, content.Height);   // 原生滚动条在可见区域外，被裁掉
            p.Visible = false;
            content.Controls.Add(p);
            pages[i] = p;
            if (i == 0) AddCoolerCard();
            return p;
        }

        void ShowPage(int i)
        {
            page = i;
            Say("");
            var cur = EnsurePage(i);
            if (scrollPages.Contains(cur)) cur.AutoScrollPosition = Point.Empty;
            cur.Visible = true;
            for (int k = 0; k < pages.Length; k++) { if (pages[k] != null && k != i) pages[k].Visible = false; navs[k].Selected = k == i; }
            thumb.Target = scrollPages.Contains(pages[i]) ? pages[i] : null;
            if (i == 0 || i == 1) { if (i == 0) RefreshHome(); else { RefreshFan(); RefreshPerf(); } sensorTimer.Start(); } else sensorTimer.Stop();
            if (i == 2) RefreshLight();
            if (i == 3) { RefreshDevice(); RefreshPower(); }
            if (i == 4) ShowDriverPage();
            if (i == 5) RefreshSettings();
        }

        /// <param name="keep">进行中的提示（如「正在清理…」）不自动消失，直到下一条提示替换它</param>
        void Say(string msg, bool bad = false, bool keep = false)
        {
            if (IsDisposed) return;
            status.Fore = bad ? (Color?)Theme.P.Bad : null;
            status.Text = msg;
            bool show = !string.IsNullOrEmpty(msg);
            status.Visible = show;
            brand.Visible = !show;
            if (show) status.Invalidate();
            sayTimer.Stop();
            if (show && !keep) { sayTimer.Interval = bad ? 6000 : 3500; sayTimer.Start(); }   // 自动消失，计时器只在有提示时运行
        }

        static Card NewCard(Panel parent, string caption, int y, int h)
        {
            var c = new Card { Caption = caption, Bounds = new Rectangle(Pad, y, W - Pad * 2, h) };
            parent.Controls.Add(c);
            return c;
        }

        static Panel NewPage() { return new GlassPanel(); }

        // ================= 主页 =================
        Panel BuildHome()
        {
            var pg = NewPage();
            var c1 = NewCard(pg, null, 6, 170);
            int iw0 = c1.Width - 28, gw = (iw0 - 20) / 2;
            sCpu = new Gauge("CPU") { Bounds = new Rectangle(14, 10, gw, 100) };
            sGpu = new Gauge("GPU") { Bounds = new Rectangle(14 + gw + 20, 10, gw, 100) };
            c1.Controls.Add(sCpu); c1.Controls.Add(sGpu);
            sCpuLoad = new Stat("CPU 占用", "%"); sCpuClk = new Stat("CPU 频率", "GHz");
            sGpuLoad = new Stat("GPU 占用", "%"); sMem = new Stat("内存", "%");
            var st = new[] { sCpuLoad, sCpuClk, sGpuLoad, sMem };
            int sw = iw0 / 4;
            for (int i = 0; i < 4; i++) { st[i].Bounds = new Rectangle(14 + i * sw, 122, sw - 4, 42); c1.Controls.Add(st[i]); }

            var c2 = NewCard(pg, "性能模式", c1.Bottom + 10, 164);
            var modes = new[]
            {
                Tuple.Create(PowerMode.Quiet, "", "安静", "低噪音"),
                Tuple.Create(PowerMode.PowerSaving, "", "省电", "最长续航"),
                Tuple.Create(PowerMode.Performance, "", "性能", "满血释放"),
                Tuple.Create(PowerMode.Entertainment, "", "娱乐", "均衡日常"),
            };
            int cw = (c2.Width - 28 - 10) / 2;
            for (int i = 0; i < 4; i++)
            {
                var m = modes[i];
                var b = new Choice(m.Item2, m.Item3, m.Item4) { Bounds = new Rectangle(14 + (i % 2) * (cw + 10), 40 + (i / 2) * 60, cw, 54) };
                b.Bolt = m.Item1 == PowerMode.Performance;
                b.Click += async (s, e) => await SetPower(m.Item1);
                powerBtns[m.Item1] = b; c2.Controls.Add(b);
            }

            // CPU 动态加速：和原厂一样，只有 BIOS 声明支持（DTT 位）时才显示
            int y = c2.Bottom + 10;
            if (Dchu.CpuBoostSupported)
            {
                var c3 = NewCard(pg, null, y, 56);
                boost = new Toggle();
                boostRow = new SettingRow("CPU 动态加速", "原厂选项，只在性能模式下可用", boost, 40) { Bounds = new Rectangle(14, 8, c3.Width - 28, 40) };
                boost.Toggled += (s, e) =>
                {
                    bool ok = Dchu.SetCpuBoost(boost.On);
                    if (!ok) boost.On = !boost.On;
                    Say(ok ? (boost.On ? "已开启 CPU 动态加速" : "已关闭 CPU 动态加速") : "设置失败：" + Dchu.LastError, !ok);
                };
                c3.Controls.Add(boostRow);
                y += 64;
            }
            BuildGpuCard(pg, y);
            coolerY = y + 144;
            homeSpacer = new Owner2 { Bounds = new Rectangle(0, coolerY, 1, 70) };   // 底部留白（装了散热器插件后会下移）
            homePage = pg;
            MakeScrollable(pg);
            return pg;
        }

        void RefreshHome()
        {
            var pm = Dchu.GetPowerMode();
            foreach (var kv in powerBtns) kv.Value.Selected = kv.Key == pm;
            host.SyncTray(pm);
            if (boost != null) { boost.Enabled = pm == PowerMode.Performance; boost.On = Dchu.GetCpuBoost(); }
            RefreshSensors();
            RefreshGpu();
            if (pm == PowerMode.Unknown) Say("无法读取硬件状态：请在「驱动」页安装原厂底层服务", true);
        }

        int sensorTick;
        SensorInfo lastFans;

        /// <summary>
        /// 温度每 2 秒用开销小的 EC 读法（约 4ms）；风扇转速必须用开销大的读法（约 37ms），每 4 秒读一次
        /// </summary>
        readonly object sensorGate = new object();
        bool sensing;

        SensorInfo ReadSensors(bool force = false)
        {
            lock (sensorGate) return ReadSensorsLocked(force);
        }

        SensorInfo ReadSensorsLocked(bool force)
        {
            if (force || lastFans == null || ++sensorTick % 2 == 0) return lastFans = Dchu.GetSensors();
            var t = Dchu.GetTemps();
            if (t == null) return lastFans;
            return new SensorInfo { CpuTemp = t[0], GpuTemp = t[1], Fan1Rpm = lastFans.Fan1Rpm, Fan2Rpm = lastFans.Fan2Rpm, Fan1Pct = lastFans.Fan1Pct, Fan2Pct = lastFans.Fan2Pct, Valid = t[0] > 0 && t[0] < 120 };
        }

        /// <summary>读传感器（风扇转速一次约 37ms）放到后台线程，界面不会因此卡顿</summary>
        void RefreshSensors()
        {
            if (sensing || IsDisposed) return;
            sensing = true;
            Task.Run(() => ReadSensors()).ContinueWith(t =>
            {
                sensing = false;
                if (t.IsFaulted || IsDisposed) return;
                try { BeginInvoke(new Action(() => ShowSensors(t.Result))); } catch { }
            });
        }

        void ShowSensors(SensorInfo s)
        {
            if (IsDisposed || s == null) return;
            if (page == 1) { RefreshFanInfo(s); return; }
            if (page != 0) return;
            if (!s.Valid) { sCpu.Set("--"); sGpu.Set("--"); sCpu.SetSub(""); sGpu.SetSub(""); }
            else
            {
                sCpu.Set(s.CpuTemp.ToString(), s.CpuTemp / 100f, s.CpuTemp >= 85);
                if (s.GpuTemp > 0) sGpu.Set(s.GpuTemp.ToString(), s.GpuTemp / 100f, s.GpuTemp >= 85); else sGpu.Set("--");
                sCpu.SetSub("风扇 " + s.Fan1Rpm + " rpm"); sGpu.SetSub("风扇 " + s.Fan2Rpm + " rpm");
            }
            if (sampling) return;
            sampling = true;
            Task.Run(() => sysMon.Sample()).ContinueWith(t =>
            {
                sampling = false;
                if (t.IsFaulted || IsDisposed) return;
                var u = t.Result;
                try { BeginInvoke(new Action(() =>
                {
                    if (IsDisposed) return;
                    sCpuLoad.Set(u.CpuLoad < 0 ? "--" : u.CpuLoad.ToString(), u.CpuLoad / 100f);
                    sCpuClk.Set(u.CpuMHz < 0 ? "--" : (u.CpuMHz / 1000.0).ToString("0.0"), u.CpuMHz / 4400f);
                    sGpuLoad.Set(u.GpuLoad < 0 ? "--" : u.GpuLoad.ToString(), u.GpuLoad / 100f);
                    sMem.Set(u.MemPct < 0 ? "--" : u.MemPct.ToString(), u.MemPct / 100f);
                    var cap = u.MemTotalGB > 0 ? string.Format("内存 {0:0.0}/{1:0}G", u.MemUsedGB, u.MemTotalGB) : "内存";
                    if (cap != sMem.Caption) { sMem.Caption = cap; sMem.Invalidate(); }   // 已用内存变了但百分比没变时也要刷新
                })); } catch { }
            });
        }

        // ================= Windows 电源选项（设备页） =================
        Select pwPlan;
        readonly Choice[] pwMode = new Choice[3];
        readonly Dictionary<PowerOpts.Item, Select[]> pwCombos = new Dictionary<PowerOpts.Item, Select[]>();

        static readonly KeyValuePair<string, uint>[] LidOpts =
        {
            new KeyValuePair<string, uint>("不操作", 0), new KeyValuePair<string, uint>("睡眠", 1),
            new KeyValuePair<string, uint>("休眠", 2), new KeyValuePair<string, uint>("关机", 3)
        };
        static readonly uint[] DisplaySecs = { 60, 120, 180, 300, 600, 900, 1800, 3600, 0 };
        static readonly uint[] SleepSecs2 = { 300, 600, 900, 1800, 3600, 7200, 0 };

        static string Dur(uint s) { return s == 0 ? "从不" : s < 3600 ? (s / 60) + " 分钟" : (s / 3600) + " 小时"; }

        sealed class Opt
        {
            public string Text; public uint Value;
            public override string ToString() { return Text; }
        }

        void BuildPowerCard(Panel pg, int y)
        {
            var c = NewCard(pg, "电源选项", y, 296);
            powerCard = c;
            c.Controls.Add(new Label2("电源计划") { Bounds = new Rectangle(14, 40, 100, 30) });
            pwPlan = NewCombo(); pwPlan.Bounds = new Rectangle(c.Width - 14 - 184, 42, 184, 26);
            pwPlan.SelectedIndexChanged += (s, e) =>
            {
                if (loadingUi) return;
                var p = pwPlan.SelectedItem as PowerPlan;
                if (p == null) return;
                bool ok = PowerOpts.SetActive(p.Id);
                RefreshPower();
                Say(ok ? "已切换到电源计划：" + p.Name : "切换电源计划失败", !ok);
            };
            c.Controls.Add(pwPlan);

            c.Controls.Add(new Label2("电源模式") { Bounds = new Rectangle(14, 80, 100, 30) });
            string[] mn = { "节能", "平衡", "性能" };
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                pwMode[i] = new Choice(null, mn[i], null) { Bounds = new Rectangle(c.Width - 14 - (3 - i) * 62 + 2, 80, 58, 30) };
                pwMode[i].Click += (s, e) =>
                {
                    bool ok = PowerOpts.SetOverlay(idx);
                    RefreshPower();
                    Say(ok ? "Windows 电源模式：" + mn[idx] : "设置失败", !ok);
                };
                c.Controls.Add(pwMode[i]);
            }

            c.Controls.Add(new Label2("插电", true) { Bounds = new Rectangle(c.Width - 14 - 196, 120, 92, 20), Align = ContentAlignment.MiddleCenter });
            c.Controls.Add(new Label2("电池", true) { Bounds = new Rectangle(c.Width - 14 - 96, 120, 92, 20), Align = ContentAlignment.MiddleCenter });
            var rows = new[] { Tuple.Create(PowerOpts.Item.Lid, "合上盖子时"), Tuple.Create(PowerOpts.Item.Display, "关闭显示器"), Tuple.Create(PowerOpts.Item.Sleep, "进入睡眠") };
            for (int r = 0; r < rows.Length; r++)
            {
                var it = rows[r].Item1;
                c.Controls.Add(new Label2(rows[r].Item2) { Bounds = new Rectangle(14, 144 + r * 38, 110, 30) });
                var pair = new Select[2];
                for (int k = 0; k < 2; k++)
                {
                    bool ac = k == 0;
                    var cb = NewCombo();
                    cb.Bounds = new Rectangle(c.Width - 14 - 196 + k * 100, 146 + r * 38, 92, 26);
                    cb.SelectedIndexChanged += (s, e) =>
                    {
                        if (loadingUi) return;
                        var o = ((Select)s).SelectedItem as Opt;
                        if (o == null) return;
                        bool ok = PowerOpts.Write(it, ac, o.Value);
                        Say(ok ? "已保存（" + (ac ? "插电" : "电池") + "）" : "保存失败", !ok);
                    };
                    pair[k] = cb; c.Controls.Add(cb);
                }
                pwCombos[it] = pair;
            }
            c.Controls.Add(new Label2("修改的是当前电源计划，不涉及 BIOS 里的功耗限制", true)
            { Bounds = new Rectangle(14, 256, c.Width - 28, 34), Align = ContentAlignment.TopLeft });
        }

        void RefreshPower()
        {
            loadingUi = true;
            try
            {
                var act = PowerOpts.Active();
                pwPlan.Items.Clear();
                foreach (var p in PowerOpts.Plans()) { pwPlan.Items.Add(p); if (p.Id == act) pwPlan.SelectedItem = p; }

                bool balanced = act == PowerOpts.Balanced;
                int ov = PowerOpts.GetOverlay();
                for (int i = 0; i < 3; i++) { pwMode[i].Enabled = balanced; pwMode[i].Selected = balanced && ov == i; }

                foreach (var kv in pwCombos)
                    for (int k = 0; k < 2; k++)
                    {
                        var cb = kv.Value[k];
                        uint cur = PowerOpts.Read(kv.Key, k == 0);
                        cb.Items.Clear();
                        if (kv.Key == PowerOpts.Item.Lid)
                            foreach (var o in LidOpts) cb.Items.Add(new Opt { Text = o.Key, Value = o.Value });
                        else
                            foreach (var v in kv.Key == PowerOpts.Item.Display ? DisplaySecs : SleepSecs2) cb.Items.Add(new Opt { Text = Dur(v), Value = v });
                        Opt sel = null;
                        foreach (Opt o in cb.Items) if (o.Value == cur) sel = o;
                        if (sel == null && cur != uint.MaxValue) { sel = new Opt { Text = Dur(cur), Value = cur }; cb.Items.Add(sel); }
                        cb.SelectedItem = sel;
                    }
            }
            finally { loadingUi = false; }
        }

        // ================= 飞智散热器（可选插件） =================
        Control coolerCard;
        Panel homePage;
        Control homeSpacer;
        int coolerY = 566;
        Btn coolerBtn;
        Label2 coolerDesc;

        /// <summary>散热器引擎在后台托盘里运行，这里只为窗口建一张卡片</summary>
        void AddCoolerCard()
        {
            if (homePage == null || host.Cooler == null || coolerCard != null) return;
            coolerCard = host.Cooler.CreateCard(W - Pad * 2, (m, b) => Say(m, b));
            coolerCard.Location = new Point(Pad, coolerY + homePage.AutoScrollPosition.Y);
            homePage.Controls.Add(coolerCard);
            homeSpacer.Top = coolerCard.Bottom;
            thumb.Sync();
        }

        void InstallCooler()
        {
            CoolerPlugin.Wanted = true;
            var err = host.LoadCooler();
            if (err != null) { Say(err, true); return; }
            AddCoolerCard();
            RefreshSettings();
            Say("散热器功能已安装，在主页最下面打开开关即可连接");
        }

        void UninstallCooler()
        {
            if (!Dialog.Ask(this, "卸载散热器功能", "将断开散热器（交还散热器自己的按键控制），删除散热器插件文件和散热器相关设置。\n\n控制中心会自动重启一次，以彻底释放插件。以后需要可以再安装。", "卸载", "取消"))
                return;
            bool loaded = host.Cooler != null;
            if (loaded) { host.Cooler.Shutdown(); if (coolerCard != null) { coolerCard.Dispose(); coolerCard = null; } }
            CoolerPlugin.Wanted = false;
            Settings.RemovePrefix("bs2");
            if (!loaded) { CoolerPlugin.CleanupIfRemoved(); RefreshSettings(); Say("散热器功能已卸载"); return; }
            // 已加载的插件文件被占用，重启控制中心后由启动流程删除
            host.Restart();
        }

        /// <summary>首次使用时询问是否需要散热器功能</summary>
        bool askingCooler;
        void AskCoolerOnce()
        {
            if (CoolerPlugin.Asked || askingCooler) return;
            askingCooler = true;
            bool yes = Dialog.Ask(this, "飞智散热器", "你在用飞智 BS2 / BS2 Pro 散热器吗？\n\n需要的话会安装散热器控制，在主页显示一个散热器卡片。\n不需要就不会安装，也不会占用任何后台资源。以后可以在「设置 → 扩展功能」里随时安装。", "安装", "不需要");
            CoolerPlugin.Wanted = yes;
            if (yes) InstallCooler(); else RefreshSettings();
        }

        // ================= 风扇 =================
        Panel BuildFan()
        {
            var pg = NewPage();
            var c1 = NewCard(pg, "风扇模式", 6, 104);
            int bw = (c1.Width - 28 - 16) / 3;
            fAuto = new Choice("", "自动", null) { Bounds = new Rectangle(14, 42, bw, 48) };
            fMax = new Choice("", "最大", null) { Bounds = new Rectangle(22 + bw, 42, bw, 48) };
            fCustom = new Choice("", "自定义", null) { Bounds = new Rectangle(30 + bw * 2, 42, bw, 48) };
            fAuto.Click += (s, e) => SetFan(Dchu.FanAuto);
            fMax.Click += (s, e) => SetFan(Dchu.FanMax);
            fCustom.Click += (s, e) => SetFan(Dchu.FanCustom);
            c1.Controls.AddRange(new Control[] { fAuto, fMax, fCustom });

            var c2 = NewCard(pg, "自定义曲线", 120, 410);
            int tw = (c2.Width - 28 - 8) / 2;
            fTabCpu = new Choice(null, "CPU 风扇", null) { Bounds = new Rectangle(14, 40, tw, 32) };
            fTabGpu = new Choice(null, "GPU 风扇", null) { Bounds = new Rectangle(22 + tw, 40, tw, 32) };
            fTabCpu.Click += (s, e) => { curveIdx = 0; ShowCurve(); };
            fTabGpu.Click += (s, e) => { curveIdx = 1; ShowCurve(); };
            fEditor = new FanCurveEditor { Bounds = new Rectangle(14, 80, c2.Width - 28, 230) };
            fInfo = new Label2("拖动两个圆点调整：温度到达多少度时风扇转多快", true) { Bounds = new Rectangle(14, 314, c2.Width - 28, 36), Align = ContentAlignment.TopLeft };
            var bReset = new Btn("恢复默认") { Bounds = new Rectangle(14, 360, tw, 34) };
            var bSave = new Btn("保存并应用", true) { Bounds = new Rectangle(22 + tw, 360, tw, 34) };
            bReset.Click += (s, e) => { if (curves == null) return; curves[0].Reset(); curves[1].Reset(); ShowCurve(); Say("已恢复默认曲线（还未保存）"); };
            bSave.Click += (s, e) => SaveCurves();
            c2.Controls.AddRange(new Control[] { fTabCpu, fTabGpu, fEditor, fInfo, bReset, bSave });
            fanModeCard = c1; fanCurveCard = c2;
            perfCard = BuildPerfCards(pg, 540);
            fanSpacer = new Owner2 { Bounds = new Rectangle(0, 540 + 258, 1, 40) };   // 底部留白
            pg.Controls.Add(fanSpacer);
            fanPage = pg;
            MakeScrollable(pg);
            return pg;
        }

        Control fanModeCard, fanCurveCard, perfCard, fanSpacer;
        Panel fanPage;

        /// <summary>按顺序堆叠可见卡片（间距 10），隐藏的卡片不留空位</summary>
        void Stack(Panel pg, Control spacer, params Control[] cards)
        {
            int y = 6 + pg.AutoScrollPosition.Y;
            foreach (var c in cards) { if (!c.Visible) continue; c.Top = y; y += c.Height + 10; }
            if (spacer != null) spacer.Top = y;
            thumb.Sync();
        }

        // ================= 性能优化 =================
        Toggle pfCores, pfHags, pfHyper;
        SettingRow pfCoresRow, pfHagsRow, pfHyperRow;
        Btn pfReboot;
        Label2 pfNote;
        bool pfLoading;

        Control BuildPerfCards(Panel pg, int y)
        {
            var c = NewCard(pg, "性能优化", y, 258);
            int iw = c.Width - 28;
            pfCores = new Toggle(); pfHags = new Toggle(); pfHyper = new Toggle();
            pfCoresRow = new SettingRow("唤醒全部 CPU 核心", "", pfCores, 56) { Bounds = new Rectangle(14, 40, iw, 56) };
            pfHagsRow = new SettingRow("硬件加速 GPU 计划", "", pfHags, 56) { Bounds = new Rectangle(14, 100, iw, 56) };
            pfHyperRow = new SettingRow("关闭虚拟化平台", "", pfHyper, 56) { Bounds = new Rectangle(14, 160, iw, 56) };
            pfNote = new Label2("", true) { Bounds = new Rectangle(14, 222, iw - 100, 24) };
            pfReboot = new Btn("立即重启", true) { Bounds = new Rectangle(c.Width - 14 - 90, 218, 90, 28), Visible = false };
            pfReboot.Click += (s, e) => Reboot();
            c.Controls.AddRange(new Control[] { pfCoresRow, pfHagsRow, pfHyperRow, pfNote, pfReboot });

            pfCores.Toggled += (s, e) =>
            {
                if (pfLoading) return;
                Settings.SetBool("coresAwake", pfCores.On);   // 记住选择：开机或电源计划被切换后托盘会自动恢复
                bool ok = PerfTweaks.SetAllCoresAwake(pfCores.On);
                Say(ok ? (pfCores.On ? "已唤醒全部核心（插电时）" : "已恢复 Windows 默认核心休眠") : "设置失败", !ok);
                RefreshPerfForce();
            };
            pfHags.Toggled += (s, e) => { if (pfLoading) return; PerfTweaks.SetHags(pfHags.On); RefreshPerfForce(); };
            pfHyper.Toggled += (s, e) =>
            {
                if (pfLoading) return;
                if (pfHyper.On && !Dialog.Ask(this, "关闭虚拟化平台", "关闭后，WSL2、安卓模拟器、Windows 沙盒、Docker 等依赖虚拟化的软件将无法运行；游戏和日常软件的 CPU 开销会降低。\n\n重启后生效，随时可以在这里改回来。", "关闭虚拟化", "取消"))
                { pfLoading = true; pfHyper.On = false; pfLoading = false; return; }
                bool ok = PerfTweaks.SetHypervisor(!pfHyper.On);
                if (!ok) Say("设置失败", true);
                RefreshPerfForce();
            };
            return c;
        }

        Tuple<bool?, bool?, bool, bool> perfCache;
        DateTime perfAt = DateTime.MinValue;

        /// <summary>系统检测（含 bcdedit 子进程和 WMI）结果缓存 60 秒；改了设置时强制刷新</summary>
        async void RefreshPerf() { await RefreshPerfAsync(false); }
        async void RefreshPerfForce() { await RefreshPerfAsync(true); }

        async Task RefreshPerfAsync(bool force)
        {
            pfLoading = true;
            try { await RefreshPerfCore(force); }
            finally { pfLoading = false; }   // 检测出错也不能让三个开关一直处于「加载中」而点不动
        }

        async Task RefreshPerfCore(bool force)
        {
            int park = PerfTweaks.CoreParkingMin();
            pfCores.On = park >= 100;
            pfCoresRow.Desc.Text = "插电时核心不休眠，所有电源计划都生效\n当前最少唤醒 " + (park < 0 ? "?" : park + "%");

            if (force || perfCache == null || (DateTime.Now - perfAt).TotalSeconds > 60)
            {
                perfCache = await Task.Run(() => Tuple.Create(PerfTweaks.HagsRunning(), PerfTweaks.HagsConfigured(), PerfTweaks.HypervisorRunning(), PerfTweaks.HypervisorConfigured()));
                perfAt = DateTime.Now;
                if (IsDisposed) return;
            }
            var r = perfCache;
            bool? hagsRun = r.Item1, hagsCfg = r.Item2;
            bool hyperRun = r.Item3, hyperCfg = r.Item4;

            bool hagsWant = hagsCfg ?? (hagsRun ?? false);
            pfHags.On = hagsWant;
            pfHagsRow.Desc.Text = "DLSS 3 帧生成需要它\n现在" + (hagsRun == null ? "未知" : hagsRun.Value ? "已开启" : "未开启");
            pfHyper.On = !hyperCfg;
            pfHyperRow.Desc.Text = "降低游戏开销，模拟器 / WSL 将不可用\n虚拟化现在" + (hyperRun ? "运行中" : "已关闭");

            bool pending = (hagsRun.HasValue && hagsWant != hagsRun.Value) || (hyperCfg != hyperRun);
            pfReboot.Visible = pending;
            pfNote.Width = pending ? pfReboot.Left - 24 : pfNote.Parent.Width - 28;
            pfNote.Text = pending ? "有设置需要重启电脑后生效" : "不改动任何功耗限制，BIOS 配置不受影响";
            pfNote.Fore = pending ? (Color?)Theme.P.Warn : null;
            pfNote.Invalidate();
        }

        void RefreshFan()
        {
            int m = Dchu.GetFanMode();
            fAuto.Selected = m == Dchu.FanAuto; fMax.Selected = m == Dchu.FanMax; fCustom.Selected = m == Dchu.FanCustom;
            // 曲线只在「自定义」模式下有用，其它模式下收起
            fanCurveCard.Visible = m == Dchu.FanCustom;
            Stack(fanPage, fanSpacer, fanModeCard, fanCurveCard, perfCard);
            var c = Dchu.GetFanCurves();
            curves = c == null ? null : new[] { c[0].Clone(), c[1].Clone() };
            ShowCurve();
            Task.Run(() => ReadSensors(true)).ContinueWith(t => { if (!t.IsFaulted && !IsDisposed) try { BeginInvoke(new Action(() => { if (!IsDisposed && page == 1) RefreshFanInfo(t.Result); })); } catch { } });
        }

        void RefreshFanInfo(SensorInfo s)
        {
            if (!s.Valid) return;
            fInfo.Text = string.Format("当前  CPU {0}°C，{1} rpm    GPU {2}°C，{3} rpm\n拖动圆点调整曲线，保存后生效",
                s.CpuTemp, s.Fan1Rpm, s.GpuTemp, s.Fan2Rpm);
        }

        void ShowCurve()
        {
            fTabCpu.Selected = curveIdx == 0; fTabGpu.Selected = curveIdx == 1;
            fEditor.Curve = curves == null ? null : curves[curveIdx];
        }

        void SetFan(int mode)
        {
            bool ok = Dchu.SetFanMode(mode);
            RefreshFan();
            Say(ok ? "风扇已切换为" + (mode == Dchu.FanAuto ? "自动" : mode == Dchu.FanMax ? "最大" : "自定义") : "切换失败：" + Dchu.LastError, !ok);
        }

        void SaveCurves()
        {
            if (curves == null) { Say("无法读取风扇曲线", true); return; }
            bool ok = Dchu.SetFanCurves(curves[0], curves[1]);
            RefreshFan();
            Say(ok ? "自定义曲线已保存并应用" : "保存失败：" + Dchu.LastError, !ok);
        }

        // ================= 灯光（键盘背光 / 屏幕） =================
        Panel BuildLight()
        {
            var pg = NewPage();
            var c = NewCard(pg, "键盘背光", 6, 304);
            int iw = c.Width - 28;
            kbOn = new Toggle();
            c.Controls.Add(new SettingRow("背光", "也可以用 Fn 组合键调节", kbOn, 40) { Bounds = new Rectangle(14, 38, iw, 40) });
            kbOn.Toggled += (s, e) => ApplyKb();

            c.Controls.Add(new Label2("亮度", true) { Bounds = new Rectangle(14, 84, 44, 30) });
            int lw = (iw - 50 - 18) / 4;
            for (int i = 0; i < 4; i++)
            {
                int lv = i + 1;
                kbLevel[i] = new Choice(null, lv.ToString(), null) { Bounds = new Rectangle(64 + i * (lw + 6), 84, lw, 30) };
                kbLevel[i].Click += (s, e) => { if (kb != null) { kb.Level = lv; ApplyKb(); } };
                c.Controls.Add(kbLevel[i]);
            }

            c.Controls.Add(new Label2("灯效", true) { Bounds = new Rectangle(14, 122, 44, 30) });
            var modeNames = new[] { Tuple.Create(8, "单色"), Tuple.Create(2, "呼吸"), Tuple.Create(3, "循环"), Tuple.Create(4, "波浪") };
            for (int i = 0; i < 4; i++)
            {
                var mn = modeNames[i];
                var b = new Choice(null, mn.Item2, null) { Bounds = new Rectangle(64 + i * (lw + 6), 122, lw, 30) };
                b.Click += (s, e) => { if (kb != null) { kb.Mode = mn.Item1; ApplyKb(); } };
                kbModes[mn.Item1] = b; c.Controls.Add(b);
            }

            c.Controls.Add(new Label2("颜色", true) { Bounds = new Rectangle(14, 162, 44, 30) });
            var colors = new[] { Color.White, Color.FromArgb(255, 0, 0), Color.FromArgb(255, 120, 0), Color.FromArgb(255, 230, 0),
                                 Color.FromArgb(0, 255, 0), Color.FromArgb(0, 230, 255), Color.FromArgb(0, 0, 255), Color.FromArgb(170, 0, 255) };
            for (int i = 0; i < colors.Length; i++)
            {
                var sw = new Swatch(colors[i]) { Location = new Point(64 + i * 30, 163) };
                sw.Click += (s, e) => { if (kb != null) { var cc = ((Swatch)s).Color; kb.R = cc.R; kb.G = cc.G; kb.B = cc.B; if (kb.Mode != 2) kb.Mode = 8; ApplyKb(); } };
                swatches.Add(sw); c.Controls.Add(sw);
            }
            var more = new Btn("…") { Bounds = new Rectangle(iw - 18, 163, 32, 28) };
            more.Click += (s, e) =>
            {
                if (kb == null) return;
                var pick = ColorPicker.Show(this, Color.FromArgb(kb.R, kb.G, kb.B), "键盘灯颜色");
                if (pick.HasValue) { var pc = pick.Value; kb.R = pc.R; kb.G = pc.G; kb.B = pc.B; if (kb.Mode != 2) kb.Mode = 8; ApplyKb(); }
            };
            c.Controls.Add(more);
            kbDeps.AddRange(kbLevel); kbDeps.AddRange(kbModes.Values); kbDeps.AddRange(swatches); kbDeps.Add(more);

            kbSleep = new Toggle();
            c.Controls.Add(new SettingRow("闲置自动关闭背光", "不操作键盘一段时间后熄灭", kbSleep) { Bounds = new Rectangle(14, 204, iw, 48) });
            kbSleep.Toggled += (s, e) => ApplyKb();
            kbSleepSec = NewCombo(); kbSleepSec.Width = 100;
            foreach (var t in new[] { "15 秒", "30 秒", "1 分钟", "3 分钟", "5 分钟", "10 分钟" }) kbSleepSec.Items.Add(t);
            kbSleepSec.SelectedIndexChanged += (s, e) => { if (!loadingUi) ApplyKb(); };
            c.Controls.Add(new SettingRow("关闭时间", "", kbSleepSec, 34) { Bounds = new Rectangle(14, 258, iw, 34) });
            kbDeps.Add(kbSleep);


            var c2 = NewCard(pg, "屏幕", 320, 170);
            c2.Controls.Add(new Label2("蓝光滤镜", false) { Bounds = new Rectangle(14, 40, 120, 22) });
            blueVal = new Label2("0", true) { Bounds = new Rectangle(c2.Width - 64, 40, 50, 22), Align = ContentAlignment.MiddleRight };
            blue = new Slider { Max = 10, Bounds = new Rectangle(8, 64, c2.Width - 16, 28) };
            blue.ValueChanged += (s, e) => { blueVal.Text = blue.Value == 0 ? "关" : blue.Value.ToString(); Dchu.SetBlueLight(blue.Value); };
            lowRefresh = new Toggle();
            var rr = lowRefreshRow = new SettingRow("省电时降低刷新率", "省电/安静模式下自动切到低刷新率省电", lowRefresh) { Bounds = new Rectangle(14, 104, c2.Width - 28, 48) };
            lowRefresh.Toggled += (s, e) => { bool ok = Dchu.SetLowRefresh(lowRefresh.On); Say(ok ? "已保存，下次切换性能模式时生效" : "设置失败：" + Dchu.LastError, !ok); };
            c2.Controls.AddRange(new Control[] { blueVal, blue, rr });
            return pg;
        }

        static readonly int[] SleepSecs = { 15, 30, 60, 180, 300, 600 };
        readonly List<Control> kbDeps = new List<Control>();

        void RefreshLight()
        {
            loadingUi = true;
            kb = Dchu.GetKbLight();
            if (kb != null)
            {
                if (kb.Mode != 2 && kb.Mode != 3 && kb.Mode != 4) kb.Mode = 8;
                if (kb.SleepSeconds <= 0) kb.SleepSeconds = 30;
                kbOn.On = kb.On;
                for (int i = 0; i < 4; i++) kbLevel[i].Selected = kb.Level == i + 1;
                foreach (var kv in kbModes) kv.Value.Selected = kv.Key == kb.Mode;
                foreach (var sw in swatches) sw.Selected = sw.Color.R == kb.R && sw.Color.G == kb.G && sw.Color.B == kb.B;
                kbSleep.On = kb.SleepOn;
                int idx = Array.FindIndex(SleepSecs, v => v >= kb.SleepSeconds);
                kbSleepSec.SelectedIndex = idx < 0 ? SleepSecs.Length - 1 : idx;
            }
            // 背光关闭时，亮度/灯效/颜色/闲置设置都无意义，置灰
            bool on = kb != null && kb.On;
            foreach (var ctl in kbDeps) ctl.Enabled = on;
            kbSleepSec.Enabled = on && kbSleep.On;
            kbOn.Enabled = kb != null;
            int bl = Dchu.GetBlueLight();
            blue.Value = bl; blueVal.Text = bl == 0 ? "关" : bl.ToString();
            lowRefresh.On = Dchu.GetLowRefresh();
            // 切换刷新率由原厂 Fn 热键程序执行：没有它时这一项不起作用
            bool fn = Dchu.FnKeyRunning;
            lowRefresh.Enabled = fn;
            lowRefreshRow.Desc.Text = fn ? "省电/安静模式下自动切到低刷新率省电" : "需要原厂 Fn 热键程序（「驱动」页的原厂底层服务）";
            loadingUi = false;
        }

        void ApplyKb()
        {
            if (kb == null || loadingUi) return;
            kb.On = kbOn.On;
            kb.SleepOn = kbSleep.On;
            if (kbSleepSec.SelectedIndex >= 0) kb.SleepSeconds = SleepSecs[kbSleepSec.SelectedIndex];
            bool ok = Dchu.SetKbLight(kb);
            RefreshLight();
            Say(ok ? "键盘背光已更新" : "设置失败：" + Dchu.LastError, !ok);
        }

        static string PowerName(PowerMode m)
        {
            switch (m) { case PowerMode.Quiet: return "安静"; case PowerMode.PowerSaving: return "省电"; case PowerMode.Performance: return "性能"; case PowerMode.Entertainment: return "娱乐"; }
            return "未知";
        }

        bool busy;
        async Task SetPower(PowerMode m)
        {
            if (busy) return;
            if (Dchu.GetPowerMode() == m) { RefreshHome(); return; }
            busy = true;
            foreach (var b in powerBtns.Values) b.Enabled = false;
            Say("正在切换到" + PowerName(m) + "模式…", false, true);
            bool ok = await Task.Run(() => Dchu.SetPowerMode(m));
            busy = false;
            if (IsDisposed) return;
            foreach (var b in powerBtns.Values) b.Enabled = true;
            RefreshHome();
            Say(ok ? "已切换到" + PowerName(m) + "模式" : "切换失败：" + Dchu.LastError, !ok);
        }

        // ================= 显卡 =================
        static string GpuName(GpuMode m)
        {
            switch (m) { case GpuMode.DiscreteOnly: return "独显直连"; case GpuMode.Hybrid: return "混合模式"; case GpuMode.IntegratedOnly: return "仅核显"; case GpuMode.Dynamic: return "动态"; }
            return "未知";
        }

        /// <summary>主页上的显卡模式卡片</summary>
        void BuildGpuCard(Panel pg, int y)
        {
            var c = NewCard(pg, "显卡模式", y, 136);
            int iw = c.Width - 28, cw = (iw - 8) / 2;
            gNow = new Label2("", true) { Bounds = new Rectangle(110, 8, iw - 96, 24), Align = ContentAlignment.MiddleRight };
            gDirect = new Choice("", "独显直连", "帧数最高") { Bounds = new Rectangle(14, 40, cw, 54) };
            gHybrid = new Choice("", "混合模式", "省电安静") { Bounds = new Rectangle(22 + cw, 40, cw, 54) };
            gDirect.Click += (s, e) => SwitchGpu(GpuMode.DiscreteOnly);
            gHybrid.Click += (s, e) => SwitchGpu(GpuMode.Hybrid);
            gNext = new Label2("切换后需要重启电脑才会生效", true) { Bounds = new Rectangle(14, 100, iw - 104, 28) };
            gReboot = new Btn("立即重启", true) { Bounds = new Rectangle(c.Width - 14 - 96, 100, 96, 28), Visible = false };
            gReboot.Click += (s, e) => Reboot();
            c.Controls.AddRange(new Control[] { gNow, gDirect, gHybrid, gNext, gReboot });
        }

        void RefreshGpu()
        {
            var g = Dchu.GetGpu();
            if (g == null) { gNext.Text = "无法读取（原厂底层驱动未安装？）"; gDirect.Enabled = gHybrid.Enabled = false; return; }
            gDirect.Enabled = g.Supports(GpuMode.DiscreteOnly);
            gHybrid.Enabled = g.Supports(GpuMode.Hybrid);
            gDirect.Selected = g.Current == GpuMode.DiscreteOnly;
            gHybrid.Selected = g.Current == GpuMode.Hybrid;
            var running = RunningGpuMode();
            if (IsDisposed) return;
            gNow.Text = running == GpuMode.Unknown ? "" : "运行中：" + GpuName(running);
            bool pending = running != GpuMode.Unknown && running != g.Current;
            gReboot.Visible = pending;
            gNext.Text = pending ? "已设为" + GpuName(g.Current) + "，重启后生效" : "切换后需要重启电脑才会生效";
            gNext.Fore = pending ? (Color?)Theme.P.Warn : null;
            gNext.Invalidate();
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DisplayDevice
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string String;
            public int Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Id;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Key;
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplayDevices(string dev, uint i, ref DisplayDevice d, uint flags);
        static GpuMode runningGpu = GpuMode.Unknown;

        /// <summary>独显直连时系统里看不到 Intel 核显；混合模式下核显存在。切换显卡模式要重启，所以只查一次</summary>
        static GpuMode RunningGpuMode()
        {
            if (runningGpu != GpuMode.Unknown) return runningGpu;
            bool any = false;
            var d = new DisplayDevice { cb = Marshal.SizeOf(typeof(DisplayDevice)) };
            for (uint i = 0; EnumDisplayDevices(null, i, ref d, 0); i++, d.cb = Marshal.SizeOf(typeof(DisplayDevice)))
            {
                var id = d.Id ?? "";
                if (id.StartsWith(@"PCI\VEN_8086", StringComparison.OrdinalIgnoreCase)) return runningGpu = GpuMode.Hybrid;
                if (id.StartsWith(@"PCI\", StringComparison.OrdinalIgnoreCase)) any = true;
            }
            return any ? (runningGpu = GpuMode.DiscreteOnly) : GpuMode.Unknown;
        }

        void SwitchGpu(GpuMode m)
        {
            var g = Dchu.GetGpu();
            if (g == null || g.Current == m) return;
            if (!Dialog.Ask(this, "切换到" + GpuName(m), "需要重启电脑才会生效。\n请先保存好正在进行的工作。", "确定切换")) return;
            if (!Dchu.SetGpu(m)) { Say("切换失败：" + Dchu.LastError, true); return; }
            RefreshGpu();
            RefreshHome();
            if (Dialog.Ask(this, "已设置为" + GpuName(m), "现在重启电脑使其生效吗？", "立即重启", "稍后")) Reboot();
            else Say("已设置为" + GpuName(m) + "，重启后生效");
        }

        static void Reboot()
        {
            try { Process.Start(new ProcessStartInfo("shutdown.exe", "-r -t 0") { CreateNoWindow = true, UseShellExecute = false }); } catch { }
        }

        // ================= 设备（电池/键盘） =================
        static Select NewCombo() { return new Select(); }

        Panel BuildDevice()
        {
            var pg = NewPage();
            var c = NewCard(pg, "电池充电", 6, 210);
            int iw = c.Width - 28;
            int pw = (iw - 16) / 3;
            chgMax = new Choice(null, "最大电量", null) { Bounds = new Rectangle(14, 40, pw, 36) };
            chgRec = new Choice(null, "推荐 80%", null) { Bounds = new Rectangle(22 + pw, 40, pw, 36) };
            chgCustom = new Choice(null, "自定义", null) { Bounds = new Rectangle(30 + pw * 2, 40, pw, 36) };
            chgOn = new Toggle { Visible = false };
            chgMax.Click += (s, e) => { var ci = Dchu.GetCharger(); ApplyChargerValues(false, ci.Start, ci.Stop); };
            chgRec.Click += (s, e) => ApplyChargerValues(true, 70, 80);
            chgCustom.Click += (s, e) => { chgMax.Selected = chgRec.Selected = false; chgCustom.Selected = true; chgStart.Enabled = chgStop.Enabled = chgApply.Enabled = true; LayoutDevice(true); };
            chgStart = NewCombo(); chgStop = NewCombo();
            var r2 = new SettingRow("低于此电量开始充电", "", chgStart, 32) { Bounds = new Rectangle(14, 86, iw, 32) };
            var r3 = new SettingRow("充到此电量停止", "", chgStop, 32) { Bounds = new Rectangle(14, 122, iw, 32) };
            chgApply = new Btn("应用自定义", true) { Bounds = new Rectangle(14, 162, iw, 34) };
            chgApply.Click += (s, e) => ApplyCharger();
            c.Controls.AddRange(new Control[] { chgMax, chgRec, chgCustom, chgOn, r2, r3, chgApply });
            chgCard = c; chgRows = new Control[] { r2, r3, chgApply };

            var k = keyCard = NewCard(pg, "键盘按键", 226, 172);
            kbFn = new Toggle(); kbWin = new Toggle(); kbSwap = new Toggle();
            var k1 = new SettingRow("Fn 锁定", "F1~F12 默认执行 Fn 功能", kbFn, 40) { Bounds = new Rectangle(14, 38, iw, 40) };
            var k2 = new SettingRow("禁用 Win 键", "玩游戏时防止误触", kbWin, 40) { Bounds = new Rectangle(14, 82, iw, 40) };
            var k3 = new SettingRow("Win / Fn 键互换", "交换左下角 Win 与 Fn 键", kbSwap, 40) { Bounds = new Rectangle(14, 126, iw, 40) };
            EventHandler apply = (s, e) => ApplyKeyboard();
            kbFn.Toggled += apply; kbWin.Toggled += apply; kbSwap.Toggled += apply;
            k.Controls.AddRange(new Control[] { k1, k2, k3 });

            var d = switchCard = NewCard(pg, "设备开关", 408, 128);
            tpOn = new Toggle(); camOn = new Toggle();
            d.Controls.Add(new SettingRow("触控板", "关闭后外接鼠标不受影响", tpOn, 40) { Bounds = new Rectangle(14, 38, iw, 40) });
            d.Controls.Add(new SettingRow("摄像头", "关闭后从硬件上断开摄像头", camOn, 40) { Bounds = new Rectangle(14, 82, iw, 40) });
            tpOn.Toggled += async (s, e) =>
            {
                bool want = tpOn.On; tpOn.Enabled = false;
                bool ok = await Task.Run(() => Touchpad.Set(want));
                tpOn.Enabled = true;
                if (!ok) tpOn.On = !want;
                Say(ok ? (want ? "触控板已开启" : "触控板已关闭") : "触控板设置失败", !ok);
            };
            camOn.Toggled += async (s, e) =>
            {
                bool want = camOn.On;
                Dchu.SetCamera(want);
                await Task.Delay(1500);
                bool now = Dchu.GetCamera();
                camOn.On = now;
                Say(now == want ? (want ? "摄像头已开启" : "摄像头已关闭") : "摄像头设置失败", now != want);
            };
            BuildPowerCard(pg, 546);
            devSpacer = new Owner2 { Bounds = new Rectangle(0, 546 + 296, 1, 30) };   // 底部留白
            pg.Controls.Add(devSpacer);
            devPage = pg;
            MakeScrollable(pg);
            return pg;
        }

        Control chgCard, keyCard, switchCard, powerCard, devSpacer;
        Control[] chgRows;
        Panel devPage;

        /// <summary>充电卡片：只有选「自定义」时才展开百分比设置</summary>
        void LayoutDevice(bool custom)
        {
            foreach (var r in chgRows) r.Visible = custom;
            chgCard.Height = custom ? 210 : 88;
            Stack(devPage, devSpacer, chgCard, keyCard, switchCard, powerCard);
        }

        readonly List<Panel> scrollPages = new List<Panel>();

        /// <summary>页面可滚动：原生滚动条放在可见区域外（被裁掉），右边显示细滚动条，滚轮由 WheelRouter 处理</summary>
        void MakeScrollable(Panel pg)
        {
            pg.AutoScroll = true;
            scrollPages.Add(pg);
        }

        void RefreshDevice()
        {
            var c = Dchu.GetCharger();
            chgOn.On = c.Enabled;
            chgMax.Enabled = chgRec.Enabled = chgCustom.Enabled = c.Supported;
            chgMax.Selected = !c.Enabled;
            chgRec.Selected = c.Enabled && c.Start == 70 && c.Stop == 80;
            chgCustom.Selected = c.Enabled && !chgRec.Selected;
            chgStart.Enabled = chgStop.Enabled = chgApply.Enabled = c.Supported && chgCustom.Selected;
            LayoutDevice(chgCustom.Selected);
            var tp = Touchpad.Get();
            tpOn.Enabled = tp.HasValue; tpOn.On = tp ?? false;
            camOn.On = Dchu.GetCamera();
            chgStart.Items.Clear(); chgStop.Items.Clear();
            foreach (var v in c.StartOptions) chgStart.Items.Add(v + "%");
            foreach (var v in c.StopOptions) chgStop.Items.Add(v + "%");
            chgStart.SelectedItem = c.Start + "%"; chgStop.SelectedItem = c.Stop + "%";
            if (chgStart.SelectedIndex < 0 && chgStart.Items.Count > 0) chgStart.SelectedIndex = chgStart.Items.Count - 1;
            if (chgStop.SelectedIndex < 0 && chgStop.Items.Count > 0) chgStop.SelectedIndex = chgStop.Items.Count - 1;

            var k = Dchu.GetKeyboard();
            kbFn.Enabled = k.FnLock != 0; kbFn.On = k.FnLock == 1;
            kbWin.Enabled = k.WinKeyEnabled != 0; kbWin.On = k.WinKeyEnabled == 2;
            kbSwap.Enabled = k.WinFnSwap != 0; kbSwap.On = k.WinFnSwap == 1;
        }

        void ApplyCharger()
        {
            int a = ParsePct(chgStart.SelectedItem), b = ParsePct(chgStop.SelectedItem);
            if (a <= 0 || b <= 0) return;
            if (a >= b) { Say("开始充电的电量必须低于停止充电的电量", true); return; }
            ApplyChargerValues(true, a, b);
        }

        void ApplyChargerValues(bool enabled, int a, int b)
        {
            bool ok = Dchu.SetCharger(enabled, a, b);
            RefreshDevice();
            Say(ok ? (enabled ? "充电保护已开启：低于 " + a + "% 开始充，" + b + "% 停止" : "已设为最大电量（充满 100%）") : "设置失败：" + Dchu.LastError, !ok);
        }

        static int ParsePct(object o) { int v; return o != null && int.TryParse(o.ToString().TrimEnd('%'), out v) ? v : 0; }

        void ApplyKeyboard()
        {
            var k = new KeyboardInfo { FnLock = kbFn.On ? 1 : 2, WinKeyEnabled = kbWin.On ? 2 : 1, WinFnSwap = kbSwap.On ? 1 : 2 };
            bool ok = Dchu.SetKeyboard(k);
            RefreshDevice();
            Say(ok ? "键盘设置已保存" : "设置失败：" + Dchu.LastError, !ok);
        }

        // ================= 驱动 =================
        // 驱动页：未获取组件时只有介绍和获取按钮；获取后为 概览 → 需要处理 → 已安装(折叠) → 可选(折叠) → 工具
        Owner2 drvEmpty, drvFull;
        Dictionary<string, DriverState> drvStates;
        // 检测结果在托盘进程里缓存，并存一份到磁盘；打开驱动页直接显示，只有点「检测驱动 / 重新检测」才检测
        static Dictionary<string, DriverState> drvCache;
        static DateTime drvCacheAt;
        bool cleaning;
        bool drvChecking, showInstalled, showOptional;
        static string SetupExe { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "P15驱动安装器.exe"); } }

        Panel BuildDrivers()
        {
            var pg = NewPage();

            drvEmpty = new Owner2 { Bounds = new Rectangle(0, 0, W, content.Height) };
            var hero = NewCard(drvEmpty, null, 6, 312);
            hero.Controls.Add(new Label2("") { Font = Theme.IconLarge, Fore = Theme.P.Accent, Align = ContentAlignment.MiddleCenter, Bounds = new Rectangle(0, 34, hero.Width, 64) });
            hero.Controls.Add(new Label2("驱动管理") { Font = Theme.Title, Align = ContentAlignment.MiddleCenter, Bounds = new Rectangle(0, 104, hero.Width, 30) });
            hero.Controls.Add(new Label2("检测本机驱动，从七彩虹官网和 NVIDIA 官网\n下载并静默安装。\n驱动装好后可以删除组件，需要时再获取。", true)
            { Align = ContentAlignment.TopCenter, Bounds = new Rectangle(20, 142, hero.Width - 40, 60) });
            var bGet = new Btn("获取驱动组件", true) { Bounds = new Rectangle(40, 214, hero.Width - 80, 40) };
            bGet.Click += (s, e) => GetDriverComponent();
            hero.Controls.Add(bGet);
            hero.Controls.Add(new Label2("组件内置在控制中心里，不需要联网", true) { Align = ContentAlignment.MiddleCenter, Bounds = new Rectangle(0, 264, hero.Width, 24) });
            pg.Controls.Add(drvEmpty);

            drvFull = new Owner2 { Bounds = new Rectangle(0, 0, W, 10) };
            pg.Controls.Add(drvFull);
            MakeScrollable(pg);
            return pg;
        }

        void ShowDriverPage()
        {
            bool has = File.Exists(SetupExe);
            drvEmpty.Visible = !has; drvFull.Visible = has;
            if (!has) return;
            // 控制中心更新过：已获取的驱动安装器比控制中心旧，就换成内置的新版（安装器没在运行时）
            try
            {
                if (File.GetLastWriteTimeUtc(SetupExe) < File.GetLastWriteTimeUtc(Application.ExecutablePath) && Process.GetProcessesByName("P15驱动安装器").Length == 0)
                    ExtractSetup();
            }
            catch { }
            // 不自动检测：只显示上次「检测驱动」的结果；用驱动安装器装过的项目以安装器的记录为准
            if (drvChecking) return;
            if (drvCache == null) { DateTime at; drvCache = StateFile.LoadSnapshot(out at); drvCacheAt = at; }
            if (drvCache != null) { drvStates = StateFile.ApplyNewer(drvCache, drvCacheAt); drvChecked = drvCacheAt; }
            LayoutDrivers();
        }

        async void RefreshDrivers()
        {
            if (drvChecking) return;
            drvChecking = true; drvChecked = DateTime.Now;
            LayoutDrivers();
            Dictionary<string, DriverState> found = null;
            string err = null;
            try { found = await Task.Run(() => StateFile.Merge(Detector.Detect())); }
            catch (Exception ex) { err = ex.Message; }
            finally { drvChecking = false; }   // 检测出错时不能一直停在「检测中…」
            if (found != null)
            {
                drvStates = found; drvChecked = DateTime.Now;
                drvCache = found; drvCacheAt = drvChecked;
                var _ = Task.Run(() => StateFile.SaveSnapshot(found));
            }
            if (IsDisposed) return;
            LayoutDrivers();
            if (err != null) Say("检测驱动失败：" + err, true);
        }

        DriverState StateOf(DriverItem d)
        {
            DriverState s = null;
            if (drvStates != null) drvStates.TryGetValue(d.Id, out s);
            return s ?? new DriverState();
        }

        void LayoutDrivers()
        {
            var p = Theme.P;
            drvFull.SuspendLayout();
            foreach (var c in drvFull.Controls.Cast<Control>().ToList()) c.Dispose();
            int cw = W - Pad * 2, iw = cw - 28, y = 6;

            var need = Catalog.All.Where(d => d.Tier != Tier.Optional && StateOf(d).Status != DrvStatus.Installed && drvStates != null).ToList();
            var installed = Catalog.All.Where(d => StateOf(d).Status == DrvStatus.Installed).ToList();
            var optional = Catalog.All.Where(d => d.Tier == Tier.Optional && StateOf(d).Status != DrvStatus.Installed).ToList();

            // ---- 概览 ----
            var sum = new Card { Bounds = new Rectangle(Pad, y, cw, 150) };
            string glyph, title, sub; Color col;
            if (!drvChecking && drvStates == null) { glyph = ""; col = p.SubText; title = "还没有检测过"; sub = "点「检测驱动」查看本机驱动状态"; }
            else if (drvChecking || drvStates == null) { glyph = ""; col = p.SubText; title = "正在检测驱动…"; sub = "大约需要几秒钟"; }
            else if (need.Count == 0) { glyph = ""; col = p.Good; title = "驱动都已就绪"; sub = "已安装 " + installed.Count + " 项，" + (drvChecked.Date == DateTime.Today ? drvChecked.ToString("HH:mm") : drvChecked.ToString("M月d日 HH:mm")) + " 检测"; }
            else { glyph = ""; col = p.Warn; title = need.Count + " 项需要处理"; sub = string.Join("、", need.Take(3).Select(d => ShortName(d))) + (need.Count > 3 ? " 等" : ""); }
            sum.Controls.Add(new Label2(glyph) { Font = Theme.IconMid, Fore = col, Align = ContentAlignment.MiddleCenter, Bounds = new Rectangle(14, 18, 44, 52) });
            sum.Controls.Add(new Label2(title) { Font = Theme.Title, Bounds = new Rectangle(66, 18, iw - 52, 28) });
            sum.Controls.Add(new Label2(sub, true) { Bounds = new Rectangle(66, 46, iw - 52, 22) });
            if (need.Count > 0 && !drvChecking)
            {
                var bAll = new Btn("一键安装（" + need.Count + " 项）", true) { Bounds = new Rectangle(14, 92, iw - 106, 40) };
                bAll.Click += (s, e) => LaunchSetup("--install " + string.Join(",", need.Select(d => d.Id)));
                var bRe = new Btn("重新检测") { Bounds = new Rectangle(14 + iw - 96, 92, 96, 40) };
                bRe.Click += (s, e) => RefreshDrivers();
                sum.Controls.Add(bAll); sum.Controls.Add(bRe);
            }
            else
            {
                var bRe = new Btn(drvChecking ? "检测中…" : drvStates == null ? "检测驱动" : "重新检测", !drvChecking && drvStates == null) { Bounds = new Rectangle(14, 92, iw, 40), Enabled = !drvChecking };
                bRe.Click += (s, e) => RefreshDrivers();
                sum.Controls.Add(bRe);
            }
            drvFull.Controls.Add(sum);
            y += 160;

            if (drvStates != null && !drvChecking)
            {
                // ---- 需要处理 ----
                if (need.Count > 0)
                {
                    var c = new Card { Caption = "需要处理", Bounds = new Rectangle(Pad, y, cw, 0) };
                    int ry = 40;
                    foreach (var d in need) { c.Controls.Add(MakeRow(d, iw, ry, true)); ry += 52; }
                    c.Height = ry + 8; drvFull.Controls.Add(c); y += c.Height + 10;
                }
                // ---- 已安装（折叠） ----
                y = AddFoldCard("已安装", installed.Count + " 项", installed, ref showInstalled, y, cw, iw, false);
                // ---- 可选（折叠） ----
                if (optional.Count > 0)
                    y = AddFoldCard("可选驱动", optional.Count + " 项未装", optional, ref showOptional, y, cw, iw, true);
            }

            // ---- 工具 ----
            var t = new Card { Caption = "工具", Bounds = new Rectangle(Pad, y, cw, 0) };
            int ty = 38;
            Action<string, string, string, EventHandler> tool = (name, desc, btn, click) =>
            {
                var b = new Btn(btn) { Width = 64, Height = 30 };
                b.Click += click;
                t.Controls.Add(new SettingRow(name, desc, b, 44) { Bounds = new Rectangle(14, ty, iw, 44) });
                ty += 48;
            };
            tool("驱动安装器", "手动选择要安装的驱动", "打开", (s, e) => LaunchSetup(""));
            tool("收尾清理", "删除安装包缓存、NVIDIA 残留、旧驱动包", "清理", (s, e) => CleanupDrivers());
            tool("删除驱动组件", "驱动装好后可删除，需要时再获取", "删除", (s, e) => RemoveDriverComponent());
            t.Height = ty + 6; drvFull.Controls.Add(t); y += t.Height + 40;   // 底部留出状态栏位置

            drvFull.Height = y;
            drvFull.ResumeLayout();
            thumb.Sync();
        }

        static string ShortName(DriverItem d)
        {
            var n = d.Name; int i = n.IndexOf(" (");
            if (i < 0) i = n.IndexOf('（');
            return i > 0 ? n.Substring(0, i) : n;
        }

        DrvRow MakeRow(DriverItem d, int iw, int y, bool action)
        {
            var s = StateOf(d); var p = Theme.P;
            var r = new DrvRow { Bounds = new Rectangle(14, y, iw, 50), Title = d.Name };
            switch (s.Status)
            {
                case DrvStatus.Installed:
                    r.Dot = p.Good; r.Status = "已安装"; r.StatusColor = p.Good;
                    r.Sub = (s.Version != "" ? s.Version + " · " : "") + d.Source + (d.Background ? " · 常驻后台" : "");
                    break;
                case DrvStatus.Outdated:
                    r.Dot = p.Warn; r.ActionText = "更新"; r.Sub = s.Message != "" ? s.Message : "有新版本";
                    break;
                case DrvStatus.Failed:
                    r.Dot = p.Bad; r.ActionText = "重试"; r.Sub = "上次安装失败：" + s.Message;
                    break;
                default:
                    r.Dot = p.Warn; r.ActionText = "安装";
                    r.Sub = d.Note + " · 约" + d.SizeMB + "MB" + (d.Background && !d.Note.Contains("常驻后台") ? " · 常驻后台" : "");
                    break;
            }
            if (!action) r.ActionText = null;
            r.Action += (o, e) => LaunchSetup("--install " + d.Id);
            return r;
        }

        int AddFoldCard(string title, string hint, List<DriverItem> items, ref bool open, int y, int cw, int iw, bool action)
        {
            var c = new Card { Bounds = new Rectangle(Pad, y, cw, 0) };
            var head = new SectionHead(title) { Bounds = new Rectangle(14, 10, iw, 30), Open = open, Hint = hint };
            bool isInstalled = title == "已安装";
            head.Click += (s, e) => { if (isInstalled) showInstalled = !showInstalled; else showOptional = !showOptional; LayoutDrivers(); };
            c.Controls.Add(head);
            int ry = 46;
            if (open) foreach (var d in items) { c.Controls.Add(MakeRow(d, iw, ry, action)); ry += 52; }
            c.Height = open ? ry + 6 : 50;
            drvFull.Controls.Add(c);
            return y + c.Height + 10;
        }

        /// <summary>收尾清理：直接在控制中心里做，完成后只给一条提示</summary>
        async void CleanupDrivers()
        {
            if (cleaning) return;
            if (Process.GetProcessesByName("P15驱动安装器").Length > 0) { Say("驱动安装器正在运行，请先关闭它再清理", true); return; }
            cleaning = true;
            try
            {
                Say("正在查找可清理的内容…", false, true);
                var olds = await Task.Run(() => DriverCleanup.FindOldDrivers());
                long cache = await Task.Run(() => DriverCleanup.PendingBytes());
                if (IsDisposed) return;
                if (olds.Count == 0 && cache < 1048576) { Say("没有需要清理的内容"); return; }
                Say("");
                var msg = "将要清理：\n· 驱动安装包缓存和 NVIDIA 安装残留，约 " + (cache / 1048576) + " MB\n· 驱动库里被新版本取代的旧驱动包 " + olds.Count + " 个（正在使用的会自动跳过）";
                if (!Dialog.Ask(this, "收尾清理", msg, "开始清理")) return;
                Say("正在清理…", false, true);
                var r = await Task.Run(() => { int del = DriverCleanup.DeleteOldDrivers(olds); long freed = DriverCleanup.CleanFiles(); return Tuple.Create(del, freed); });
                if (IsDisposed) return;
                Say("清理完成：释放 " + (r.Item2 / 1048576) + " MB，删除旧驱动包 " + r.Item1 + " 个");
            }
            catch (Exception ex) { if (!IsDisposed) Say("清理失败：" + ex.Message, true); }
            finally { cleaning = false; }
        }

        /// <summary>把内置的驱动安装器释放到程序目录</summary>
        static bool ExtractSetup()
        {
            using (var res = typeof(MainForm).Assembly.GetManifestResourceStream("P15.DriverSetup.gz"))
            {
                if (res == null) return false;
                var tmp = SetupExe + ".tmp";
                using (var z = new System.IO.Compression.GZipStream(res, System.IO.Compression.CompressionMode.Decompress))
                using (var f = File.Create(tmp)) z.CopyTo(f);
                if (File.Exists(SetupExe)) File.Delete(SetupExe);
                File.Move(tmp, SetupExe);
                return true;
            }
        }

        /// <summary>从控制中心内置的压缩包里释放驱动安装器</summary>
        void GetDriverComponent()
        {
            try
            {
                if (!ExtractSetup()) { Say("控制中心里没有内置驱动组件", true); return; }
                ShowDriverPage();
                Say("驱动组件已就绪");
            }
            catch (Exception ex) { Say("获取驱动组件失败：" + ex.Message, true); }
        }

        /// <summary>删除驱动组件及其全部残留（已安装的驱动不受影响）</summary>
        async void RemoveDriverComponent()
        {
            if (!Dialog.Ask(this, "删除驱动组件", "将删除：驱动安装器、下载的驱动安装包和缓存、安装记录（C:\\ProgramData\\P15Center）、NVIDIA 安装残留、桌面上的驱动安装报告。\n\n已经装好的驱动不会受影响，以后需要时可以重新获取。", "删除", "取消"))
                return;
            Say("正在删除驱动组件…");
            var report = await Task.Run(() =>
            {
                var left = new List<string>();
                long freed = 0;
                foreach (var p in Process.GetProcessesByName("P15驱动安装器"))
                    try { p.Kill(); p.WaitForExit(5000); } catch { }

                Func<string, bool> delFile = f =>
                {
                    for (int i = 0; i < 5; i++)
                    {
                        try { if (!File.Exists(f)) return true; freed += new FileInfo(f).Length; File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); return true; }
                        catch { Thread.Sleep(400); }
                    }
                    return false;
                };
                Action<string> delDir = d =>
                {
                    if (!Directory.Exists(d)) return;
                    foreach (var f in Directory.GetFiles(d, "*", SearchOption.AllDirectories)) if (!delFile(f)) left.Add(f);
                    try { Directory.Delete(d, true); } catch { if (Directory.Exists(d)) left.Add(d); }
                };

                if (!delFile(SetupExe)) left.Add(SetupExe);
                delFile(SetupExe + ".tmp");
                delDir(StateFile.Dir);                 // C:\ProgramData\P15Center（缓存、解压文件、安装记录）
                delDir(@"C:\NVIDIA");                  // NVIDIA 安装包解压残留
                foreach (var desk in new[] { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory) })
                {
                    var r = Path.Combine(desk, "P15驱动安装报告.txt");
                    if (!delFile(r)) left.Add(r);
                }
                return Tuple.Create(freed, left);
            });
            drvCache = null;
            if (IsDisposed) return;   // 删除过程中窗口被关掉了
            drvStates = null;
            ShowDriverPage();
            if (report.Item2.Count == 0)
                Say("驱动组件已删除干净，释放 " + (report.Item1 / 1048576.0).ToString("0.#") + " MB");
            else
                Dialog.Info(this, "部分文件没删掉", "以下文件被占用，重启电脑后再删除一次即可：\n" + string.Join("\n", report.Item2.Take(5)));
        }


        void LaunchSetup(string args)
        {
            var exe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "P15驱动安装器.exe");
            if (!File.Exists(exe)) { ShowDriverPage(); Say("驱动组件已被删除，请重新获取", true); return; }
            try
            {
                var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true });
                Say("已打开驱动安装器");
                if (p != null)
                    Task.Run(() =>
                    {
                        p.WaitForExit();
                        // 安装器关闭后只按它写下的安装记录刷新显示，不自动检测
                        if (IsDisposed || !IsHandleCreated) return;
                        try { BeginInvoke(new Action(() => { if (!IsDisposed && page == 4) ShowDriverPage(); })); } catch { }
                    });
            }
            catch (Exception ex) { Say("无法启动安装器：" + ex.Message, true); }
        }

        // ================= 设置 =================
        Panel BuildSettings()
        {
            var pg = NewPage();
            // 外观只有黑色磨砂一种，这里只调透明度（亚克力色调的不透明度）
            var c = NewCard(pg, "透明度", 6, 92);
            string[] ln = { "轻透", "标准", "通透" };
            int lw = (c.Width - 28 - 12) / 3;
            for (int i = 0; i < 3; i++)
            {
                int lv = i;
                tLevel[i] = new Choice(null, ln[i], null) { Bounds = new Rectangle(14 + i * (lw + 6), 42, lw, 36) };
                tLevel[i].Click += (s, e) => { Settings.Set("glass", lv.ToString()); Theme.SetLevel(lv); RefreshSettings(); };
                c.Controls.Add(tLevel[i]);
            }
            var c2 = NewCard(pg, "常规", 108, 152);
            int iw = c2.Width - 28;
            autoStart = new Toggle(); closeToTray = new Toggle();
            c2.Controls.Add(new SettingRow("开机自动启动", "登录后在托盘运行，不弹权限确认", autoStart) { Bounds = new Rectangle(14, 40, iw, 48) });
            c2.Controls.Add(new SettingRow("关闭时最小化到托盘", "托盘右键可快速切换性能模式", closeToTray) { Bounds = new Rectangle(14, 94, iw, 48) });
            autoStart.Toggled += async (s, e) =>
            {
                bool want = autoStart.On;
                bool ok = await Task.Run(() => AutoStart.Set(want));
                if (!ok) { autoStart.On = !want; Say("设置开机自启失败", true); }
                else Say(want ? "已开启开机自启" : "已关闭开机自启");
            };
            closeToTray.Toggled += (s, e) => Settings.SetBool("closeToTray", closeToTray.On);

            var c3 = NewCard(pg, "关于", 376, 88);
            about = new Label2("", true) { Bounds = new Rectangle(14, 38, c3.Width - 28, 40), Align = ContentAlignment.TopLeft };
            c3.Controls.Add(about);

            var c4 = NewCard(pg, "扩展功能", 270, 96);
            coolerBtn = new Btn("安装") { Width = 72, Height = 30 };
            var row = new SettingRow("飞智 BS2 / BS2 Pro 散热器", "", coolerBtn, 48) { Bounds = new Rectangle(14, 40, c4.Width - 28, 48) };
            coolerDesc = row.Desc;
            coolerBtn.Click += (s, e) => { if (CoolerPlugin.Wanted) UninstallCooler(); else InstallCooler(); };
            c4.Controls.Add(row);
            return pg;
        }

        async void RefreshSettings()
        {
            if (pages[5] == null || IsDisposed) return;   // 设置页还没打开过
            for (int i = 0; i < 3; i++) tLevel[i].Selected = Theme.Level == i;
            closeToTray.On = Settings.GetBool("closeToTray", true);
            about.Text = Brand.Name + " " + Brand.Version + "，七彩虹 隐星 P15\n原厂 Fn 热键：" + (Dchu.FnKeyRunning ? "运行中" : "未运行") + "，硬件接口：" + (Dchu.Available ? "正常" : "不可用");
            bool cool = CoolerPlugin.Wanted;
            coolerBtn.Text = cool ? "卸载" : "安装";
            coolerDesc.Text = cool ? "已安装，在主页控制转速和灯光" : "未安装，不占用任何资源";
            bool auto = await Task.Run(() => AutoStart.Enabled);
            if (!IsDisposed) autoStart.On = auto;
        }

        // ================= 关闭 / 托盘 =================
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (!CoolerPlugin.Asked) BeginInvoke(new Action(AskCoolerOnce));   // 首次打开时询问一次
        }

        void ExitApp() { host.Exit(); }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // 系统关机/注销、或用户设置了「关闭即退出」：整个程序退出。
            // 其它情况（点 ✕、Alt+F4、外部发来的关闭消息）：只销毁窗口，后台托盘继续运行。
            bool quit = e.CloseReason == CloseReason.WindowsShutDown || e.CloseReason == CloseReason.TaskManagerClosing
                        || !Settings.GetBool("closeToTray", true);
            base.OnFormClosing(e);
            if (quit && !host.Exiting && !e.Cancel) host.ExitLater();   // 等窗口关完再退出程序
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) Close();
            base.OnKeyDown(e);
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible) sensorTimer.Stop();
        }

        /// <summary>最小化时停止读取传感器，还原后继续</summary>
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized) sensorTimer.Stop();
            else if ((page == 0 || page == 1) && !sensorTimer.Enabled && pages[page] != null) { RefreshSensors(); sensorTimer.Start(); }
        }
    }
}
