// 轻量自绘控件：P15 CONTROL 黑色透明磨砂玻璃（系统亚克力）。
// 极简单色：分区用细线和留白，不套卡片；控件是磨砂玻璃小块；选中/主操作用黑白实色反转。
// 唯一的图形符号是几何闪电（品牌、性能模式、托盘）。
using System;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace P15
{
    public sealed class Palette
    {
        public Color Base;                 // 不透明底色（兜底）
        public Color Tint;                 // 亚克力色调（透明度由档位决定）
        public Color Card;                 // 分区层（透明，不再画卡片底）
        public Color Inset;                // 控件磨砂块
        public Color Hover;                // 悬停
        public Color Stroke, Highlight;    // 细分隔线
        public Color Text, SubText;
        public Color Sel, SelStroke, SelText;
        public Color Solid, AccentText;    // 实色（选中、主按钮、开关开启）和其上的文字
        public Color Accent, AccentA, AccentB;
        public Color Good, Warn, Bad;
        public Color Track;
        public Color Menu, WindowBorder;
    }

    public static class Theme
    {
        // 石墨黑玻璃
        public static readonly Palette Dark = new Palette
        {
            Base = Color.FromArgb(23, 24, 27), Tint = Color.FromArgb(23, 24, 27), Card = Color.FromArgb(0, 0, 0, 0),
            Inset = Color.FromArgb(20, 255, 255, 255), Hover = Color.FromArgb(34, 255, 255, 255),
            Stroke = Color.FromArgb(28, 255, 255, 255), Highlight = Color.FromArgb(28, 255, 255, 255),
            Text = Color.FromArgb(242, 243, 245), SubText = Color.FromArgb(156, 159, 167),
            Sel = Color.FromArgb(242, 243, 245), SelStroke = Color.FromArgb(0, 0, 0, 0), SelText = Color.FromArgb(20, 21, 24),
            Solid = Color.FromArgb(242, 243, 245), AccentText = Color.FromArgb(20, 21, 24),
            Accent = Color.FromArgb(242, 243, 245), AccentA = Color.FromArgb(242, 243, 245), AccentB = Color.FromArgb(242, 243, 245),
            Good = Color.FromArgb(242, 243, 245), Warn = Color.FromArgb(227, 154, 85), Bad = Color.FromArgb(227, 154, 85),
            Track = Color.FromArgb(46, 255, 255, 255), Menu = Color.FromArgb(34, 35, 39), WindowBorder = Color.FromArgb(66, 68, 74)
        };

        public static Palette P = Dark;
        /// <summary>透明度档位：0 轻透 / 1 标准 / 2 通透</summary>
        public static int Level = 1;
        public static event Action Changed;
        public static void SetLevel(int level) { Level = Math.Max(0, Math.Min(2, level)); Raise(); }
        static void Raise() { var h = Changed; if (h != null) h(); }
        /// <summary>窗口销毁时清掉所有订阅，避免已销毁的控件被静态事件引用住（内存泄漏）</summary>
        public static void ClearHandlers() { Changed = null; }

        /// <summary>亚克力色调的不透明度（越大越不透）</summary>
        public static int TintAlpha { get { return new[] { 140, 90, 35 }[Level]; } }

        // 字体：中文用微软雅黑 UI，数字和字标用 Bahnschrift（DIN 风格的工业字体，系统自带）
        public static readonly Font Title = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold);
        public static readonly Font H2 = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold);
        public static readonly Font Body = new Font("Microsoft YaHei UI", 9.5f);
        public static readonly Font BodyBold = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
        public static readonly Font Small = new Font("Microsoft YaHei UI", 8.5f);
        public static readonly Font SmallBold = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold);
        public static readonly Font Hero = new Font("Bahnschrift SemiBold Condensed", 34f);
        public static readonly Font Big = new Font("Bahnschrift SemiBold", 17f);
        public static readonly Font Num = new Font("Bahnschrift SemiBold", 13f);
        public static readonly Font NumSmall = new Font("Bahnschrift", 9.5f);
        static readonly string IconFace = PickIconFace();
        public static readonly Font Icon = new Font(IconFace, 13f);
        // 图标字体只建一次（每次 new Font 都会占用 GDI 句柄且不会随控件释放）
        public static readonly Font IconSmall = new Font(IconFace, 8f);
        public static readonly Font IconTiny = new Font(IconFace, 9f);
        public static readonly Font IconMid = new Font(IconFace, 22f);
        public static readonly Font IconLarge = new Font(IconFace, 30f);

        static string PickIconFace()
        {
            try { using (new FontFamily("Segoe Fluent Icons")) return "Segoe Fluent Icons"; } catch { return "Segoe MDL2 Assets"; }
        }

        public static GraphicsPath Round(RectangleF r, float rad) { return Brand.Round(r, rad); }

        public static void Fill(Graphics g, RectangleF r, float rad, Color c)
        {
            if (c.A == 0) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Round(r, rad)) using (var b = new SolidBrush(c)) g.FillPath(b, path);
        }

        public static void Line(Graphics g, RectangleF r, float rad, Color c)
        {
            if (c.A == 0) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Round(r, rad)) using (var pen = new Pen(c)) g.DrawPath(pen, path);
        }

        /// <summary>水平细线（像素对齐，不抗锯齿）</summary>
        public static void Rule(Graphics g, float x1, float x2, float y, Color c)
        {
            var sm = g.SmoothingMode; g.SmoothingMode = SmoothingMode.None;
            using (var pen = new Pen(c)) g.DrawLine(pen, x1, (int)y, x2, (int)y);
            g.SmoothingMode = sm;
        }

        /// <summary>单色画刷（保留旧接口名；界面里不再使用彩色渐变）</summary>
        public static Brush Grad(RectangleF r) { return new SolidBrush(P.Text); }

        /// <summary>实色块：主按钮、选中、开关开启、进度</summary>
        public static void FillGrad(Graphics g, RectangleF r, float rad) { Fill(g, r, rad, P.Solid); }

        /// <summary>磨砂玻璃块（平面，不加描边和反光）</summary>
        public static void Glass(Graphics g, RectangleF r, float rad, Color fill) { Fill(g, r, rad, fill); }

        /// <summary>选中态：黑白反转的实色块</summary>
        public static void Lit(Graphics g, RectangleF r, float rad) { Fill(g, r, rad, P.Sel); }

        /// <summary>图标字形（单色）</summary>
        public static void GradText(Graphics g, string s, Font f, RectangleF r, StringAlignment h = StringAlignment.Center)
        {
            Text(g, s, f, P.Text, r, h == StringAlignment.Center ? ContentAlignment.MiddleCenter : ContentAlignment.MiddleLeft);
        }

        static StringFormat Fmt(StringAlignment h, StringAlignment v)
        {
            var f = new StringFormat(StringFormat.GenericTypographic);
            f.FormatFlags = StringFormatFlags.MeasureTrailingSpaces;
            f.Alignment = h; f.LineAlignment = v; f.Trimming = StringTrimming.EllipsisCharacter;
            return f;
        }
        static readonly StringFormat FLeft = Fmt(StringAlignment.Near, StringAlignment.Center), FTopLeft = Fmt(StringAlignment.Near, StringAlignment.Near),
            FCenter = Fmt(StringAlignment.Center, StringAlignment.Center), FRight = Fmt(StringAlignment.Far, StringAlignment.Center),
            FTopCenter = Fmt(StringAlignment.Center, StringAlignment.Near);

        /// <summary>文字：GDI+ 灰度抗锯齿，透明度正确（GDI 文字在透明玻璃上会变透明）</summary>
        public static void Text(Graphics g, string s, Font f, Color c, RectangleF r, ContentAlignment a = ContentAlignment.MiddleLeft)
        {
            if (string.IsNullOrEmpty(s)) return;
            StringFormat sf;
            switch (a)
            {
                case ContentAlignment.MiddleCenter: sf = FCenter; break;
                case ContentAlignment.TopLeft: sf = FTopLeft; break;
                case ContentAlignment.MiddleRight: sf = FRight; break;
                case ContentAlignment.TopCenter: sf = FTopCenter; break;
                default: sf = FLeft; break;
            }
            var th = g.TextRenderingHint;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (var b = new SolidBrush(c)) g.DrawString(s, f, b, r, sf);
            g.TextRenderingHint = th;
        }

        /// <summary>单行文字尺寸（与 Text 的绘制方式一致）</summary>
        public static SizeF Measure(Graphics g, string s, Font f) { return g.MeasureString(s, f, PointF.Empty, FTopLeft); }
    }

    /// <summary>所有自绘控件的基类：带透明度绘制（透出系统亚克力）、跟随主题重绘。</summary>
    public class Owner : Control
    {
        protected bool Hover;
        public Owner()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.Selectable, false);   // 不抢键盘焦点，避免可滚动页面自动滚到按钮处
            Theme.Changed += OnTheme;
            Font = Theme.Body;
        }
        void OnTheme() { if (!IsDisposed) Invalidate(); }
        protected override void Dispose(bool disposing) { Theme.Changed -= OnTheme; base.Dispose(disposing); }
        protected override void OnMouseEnter(EventArgs e) { Hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { Hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void WndProc(ref Message m) { if (!P15.Glass.Paint(this, ref m, OnPaint)) base.WndProc(ref m); }
        /// <summary>画玻璃背景（透明，透出亚克力）</summary>
        protected void Back(Graphics g) { P15.Glass.PaintBack(this, g); }
    }

    /// <summary>分区：标题 + 顶部细线（页面第一个分区不画线），不画卡片底</summary>
    public class Card : Panel, IGlassLayer
    {
        public string Caption;
        public Card()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Theme.Changed += OnTheme;
            Padding = new Padding(14, 12, 14, 12);
        }
        public Color LayerTint { get { return Theme.P.Card; } }
        void OnTheme() { if (!IsDisposed) Invalidate(true); }
        protected override void Dispose(bool disposing) { Theme.Changed -= OnTheme; base.Dispose(disposing); }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void WndProc(ref Message m) { if (!Glass.Paint(this, ref m, OnPaint)) base.WndProc(ref m); }
        protected override void OnLocationChanged(EventArgs e) { base.OnLocationChanged(e); Invalidate(); }

        bool First
        {
            get
            {
                var sc = Parent as ScrollableControl;
                int y = Top - (sc != null ? sc.AutoScrollPosition.Y : 0);
                return y < 14;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; var p = Theme.P;
            Glass.PaintBack(this, g);
            if (!First) Theme.Rule(g, 14, Width - 14, 0, p.Stroke);
            if (!string.IsNullOrEmpty(Caption))
                Theme.Text(g, Caption, Theme.H2, p.Text, new RectangleF(14, 10, Width - 28, 22));
        }
    }

    /// <summary>文字标签</summary>
    public class Label2 : Owner
    {
        public bool Sub;
        public Color? Fore;
        public ContentAlignment Align = ContentAlignment.MiddleLeft;
        public Label2(string text, bool sub = false) { Text = text; Sub = sub; Font = sub ? Theme.Small : Theme.Body; }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Back(e.Graphics);
            Theme.Text(e.Graphics, Text, Font, Fore ?? (Sub ? Theme.P.SubText : Theme.P.Text), ClientRectangle, Align);
        }
    }

    /// <summary>选项块：磨砂块，选中时黑白反转。不画装饰图标；性能模式带闪电</summary>
    public class Choice : Owner
    {
        public string Glyph, Sub;
        /// <summary>右侧显示几何闪电（性能模式）</summary>
        public bool Bolt;
        bool sel;
        public bool Selected { get { return sel; } set { if (sel != value) { sel = value; Invalidate(); } } }
        public Choice(string glyph, string title, string sub) { Glyph = glyph; Text = title; Sub = sub; Cursor = Cursors.Hand; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; var p = Theme.P;
            Back(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            float rad = Math.Min(8, r.Height / 2);
            bool lit = sel && Enabled;
            Theme.Fill(g, r, rad, lit ? p.Sel : sel ? p.Track : Hover && Enabled ? p.Hover : p.Inset);
            var fg = lit ? p.SelText : (Enabled ? p.Text : p.SubText);
            var sub = lit ? Color.FromArgb(165, p.SelText) : p.SubText;
            int x = 14, right = Bolt ? 30 : 10;
            if (string.IsNullOrEmpty(Sub))
                Theme.Text(g, Text, lit ? Theme.BodyBold : Theme.Body, fg, new RectangleF(2, 0, Width - 4, Height), ContentAlignment.MiddleCenter);
            else
            {
                Theme.Text(g, Text, Theme.H2, fg, new RectangleF(x, Height / 2f - 19, Width - x - right, 20));
                Theme.Text(g, Sub, Theme.Small, sub, new RectangleF(x, Height / 2f + 2, Width - x - right, Height / 2f - 3), ContentAlignment.TopLeft);
            }
            if (Bolt)
                Brand.DrawBolt(g, new RectangleF(Width - 26, Height / 2f - 9, 13, 18), fg);
        }
    }

    /// <summary>按钮：主按钮为实色，次按钮为磨砂块</summary>
    public class Btn : Owner
    {
        public bool Primary;
        public Btn(string text, bool primary = false) { Text = text; Primary = primary; Cursor = Cursors.Hand; Height = 34; }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; var p = Theme.P;
            Back(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            float rad = Math.Min(8, r.Height / 2);
            Color fg;
            if (!Enabled) { Theme.Fill(g, r, rad, p.Inset); fg = p.SubText; }
            else if (Primary)
            {
                Theme.Fill(g, r, rad, p.Solid);
                if (Hover) Theme.Fill(g, r, rad, Color.FromArgb(36, 0, 0, 0));
                fg = p.AccentText;
            }
            else { Theme.Fill(g, r, rad, Hover ? p.Hover : p.Inset); fg = p.Text; }
            Theme.Text(g, Text, Primary && Enabled ? Theme.BodyBold : Theme.Body, fg, ClientRectangle, ContentAlignment.MiddleCenter);
        }
    }

    /// <summary>开关</summary>
    public class Toggle : Owner
    {
        bool on;
        public event EventHandler Toggled;
        public bool On { get { return on; } set { if (on != value) { on = value; Invalidate(); } } }
        public Toggle() { Size = new Size(44, 24); Cursor = Cursors.Hand; }
        protected override void OnClick(EventArgs e)
        {
            if (!Enabled) return;
            On = !On;
            var h = Toggled; if (h != null) h(this, e);
            base.OnClick(e);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; var p = Theme.P;
            Back(g);
            var r = new RectangleF(2, 2.5f, Width - 4.5f, Height - 5.5f);
            bool lit = on && Enabled;
            Theme.Fill(g, r, r.Height / 2, lit ? p.Solid : p.Track);
            float d = r.Height - 6, x = on ? r.Right - d - 3 : r.X + 3;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var knob = !Enabled ? Color.FromArgb(120, p.SubText) : lit ? p.AccentText : Color.FromArgb(230, 231, 234);
            using (var b = new SolidBrush(knob)) g.FillEllipse(b, x, r.Y + 3, d, d);
        }
    }

    /// <summary>带标题与说明的一行设置 + 右侧控件</summary>
    public class SettingRow : GlassPanel
    {
        public readonly Label2 Title, Desc;
        public SettingRow(string title, string desc, Control right, int height = 48)
        {
            Height = height;
            Title = new Label2(title) { Location = new Point(0, 2), Size = new Size(200, 22) };
            Desc = new Label2(desc, true) { Location = new Point(0, 24), Size = new Size(240, height - 24) };
            Controls.Add(Title); Controls.Add(Desc);
            if (right != null) { right.Anchor = AnchorStyles.Right | AnchorStyles.Top; Controls.Add(right); }
            Resize += (s, e) =>
            {
                if (right != null) right.Location = new Point(Width - right.Width, (Height - right.Height) / 2);
                int w = Width - (right == null ? 0 : right.Width + 10);
                Title.Width = w; Desc.Width = w;
            };
        }
    }

    /// <summary>下拉选择（替代原生 ComboBox）</summary>
    public sealed class Select : Owner
    {
        public sealed class ItemList : Collection<object>
        {
            readonly Select owner;
            internal ItemList(Select o) { owner = o; }
            protected override void ClearItems() { base.ClearItems(); owner.sel = -1; owner.Invalidate(); }
            protected override void RemoveItem(int i) { base.RemoveItem(i); if (owner.sel >= Count) owner.sel = -1; owner.Invalidate(); }
        }

        public readonly ItemList Items;
        int sel = -1;
        public event EventHandler SelectedIndexChanged;

        public Select() { Items = new ItemList(this); Size = new Size(90, 28); Cursor = Cursors.Hand; }

        public int SelectedIndex
        {
            get { return sel; }
            set
            {
                if (value < -1 || value >= Items.Count) value = -1;
                if (value == sel) return;
                sel = value; Invalidate();
                var h = SelectedIndexChanged; if (h != null) h(this, EventArgs.Empty);
            }
        }

        public object SelectedItem
        {
            get { return sel >= 0 && sel < Items.Count ? Items[sel] : null; }
            set { SelectedIndex = value == null ? -1 : Items.IndexOf(value); }
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (!Enabled || Items.Count == 0) return;
            var m = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = false, MinimumSize = new Size(Width + 8, 0) };
            GlassMenuRenderer.Style(m);
            for (int i = 0; i < Items.Count; i++)
            {
                var it = new ToolStripMenuItem(Items[i].ToString()) { Tag = i, Checked = i == sel, AutoSize = false, Size = new Size(Math.Max(Width, 80), 30) };
                m.Items.Add(it);
            }
            m.ItemClicked += (s, ev) => SelectedIndex = (int)ev.ClickedItem.Tag;
            m.Closed += (s, ev) => BeginInvoke(new Action(m.Dispose));
            m.Show(this, new Point(-4, Height + 2));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; var p = Theme.P;
            Back(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Theme.Fill(g, r, 7, !Enabled ? p.Inset : Hover ? p.Hover : p.Inset);
            var s = SelectedItem;
            Theme.Text(g, s == null ? "" : s.ToString(), Theme.Body, Enabled ? p.Text : p.SubText, new RectangleF(10, 0, Width - 30, Height));
            Theme.Text(g, "", Theme.IconSmall, p.SubText, new RectangleF(Width - 22, 0, 14, Height), ContentAlignment.MiddleCenter);
        }
    }

    /// <summary>主题化的消息框</summary>
    public static class Dialog
    {
        public static bool Ask(IWin32Window owner, string title, string msg, string ok = "确定", string cancel = "取消")
        {
            using (var f = new GlassForm())
            {
                // 按文字多少自动调整高度，长说明也能完整显示
                int mh = TextRenderer.MeasureText(msg, Theme.Body, new Size(284, 0), TextFormatFlags.WordBreak).Height + 10;
                mh = Math.Max(40, Math.Min(360, mh));
                f.StartPosition = FormStartPosition.CenterParent;
                f.Size = new Size(324, 54 + mh + 66); f.ShowInTaskbar = false; f.KeyPreview = true;
                var t = new Label2(title) { Font = Theme.H2, Location = new Point(20, 18), Size = new Size(284, 26) };
                var m = new Label2(msg) { Location = new Point(20, 52), Size = new Size(284, mh), Align = ContentAlignment.TopLeft };
                int by = 52 + mh + 12;
                var bOk = new Btn(ok, true) { Location = new Point(cancel == null ? 20 : 166, by), Width = cancel == null ? 284 : 138 };
                f.Controls.Add(t); f.Controls.Add(m); f.Controls.Add(bOk);
                bOk.Click += (s, e) => { f.DialogResult = DialogResult.OK; };
                if (cancel != null)
                {
                    var bNo = new Btn(cancel) { Location = new Point(20, by), Width = 138 };
                    bNo.Click += (s, e) => { f.DialogResult = DialogResult.Cancel; };
                    f.Controls.Add(bNo);
                }
                f.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) f.DialogResult = DialogResult.Cancel; };
                t.MouseDown += (s, e) => f.DragMove();
                return f.ShowDialog(owner) == DialogResult.OK;
            }
        }
        public static void Info(IWin32Window owner, string title, string msg) { Ask(owner, title, msg, "知道了", null); }
    }

    public static class AppIcon
    {
        /// <summary>程序内绘制图标；同一款式只生成一次（图标句柄不会自动释放，反复生成会泄漏）</summary>
        static readonly System.Collections.Generic.Dictionary<int, Icon> cache = new System.Collections.Generic.Dictionary<int, Icon>();

        static Icon Cached(int key, Func<Bitmap> make)
        {
            Icon ic;
            if (!cache.TryGetValue(key, out ic))
                using (var bmp = make()) cache[key] = ic = Icon.FromHandle(bmp.GetHicon());
            return ic;
        }

        /// <summary>窗口/任务栏图标：石墨方块 + 白色闪电</summary>
        public static Icon Get(int variant = 0, int size = 32) { return Cached(size, () => Brand.Tile(size)); }

        /// <summary>托盘图标：单色闪电，跟随任务栏深浅色；性能模式实心，其它模式描边</summary>
        public static Icon Tray(bool filled)
        {
            int size = SystemInformation.SmallIconSize.Width;
            bool light = false;
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    light = k != null && Convert.ToInt32(k.GetValue("SystemUsesLightTheme", 0)) == 1;
            }
            catch { }
            return Cached(100000 + size * 4 + (filled ? 2 : 0) + (light ? 1 : 0), () => Brand.TrayGlyph(size, filled, light));
        }
    }
}
