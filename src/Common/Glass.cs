// 透明磨砂玻璃：窗口背后是 Windows 实时亚克力（DWM 在显卡上合成，本程序不做任何模糊计算），
// 能隐约看到窗口后面的内容。所有控件用 GDI+ 画在带透明度的位图上再原样拷到窗口，
// 没画东西的地方 alpha = 0，直接透出亚克力；卡片是半透明的白/黑玻璃。
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace P15
{
    /// <summary>半透明的玻璃层（卡片、导航条）：其中的子控件背景要叠加这一层的色调</summary>
    public interface IGlassLayer { Color LayerTint { get; } }

    public static class Glass
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);
        [StructLayout(LayoutKind.Sequential)] struct ACCENT { public int State, Flags, Color, Anim; }
        [StructLayout(LayoutKind.Sequential)] struct WCAD { public int Attr; public IntPtr Data; public int Size; }
        [DllImport("user32.dll")] static extern int SetWindowCompositionAttribute(IntPtr h, ref WCAD d);
        [StructLayout(LayoutKind.Sequential)]
        struct PAINTSTRUCT
        {
            public IntPtr hdc; public int fErase; public int l, t, r, b; public int fRestore, fIncUpdate;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] rgb;
        }
        [DllImport("user32.dll")] static extern IntPtr BeginPaint(IntPtr h, ref PAINTSTRUCT p);
        [DllImport("user32.dll")] static extern bool EndPaint(IntPtr h, ref PAINTSTRUCT p);
        [StructLayout(LayoutKind.Sequential)] struct BMI { public int Size, W, H; public short Planes, Bits; public int Comp, SizeImg, X, Y, Used, Imp; }
        [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc, ref BMI b, int u, out IntPtr bits, IntPtr s, int o);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr d, int x, int y, int w, int h, IntPtr s, int sx, int sy, int rop);

        /// <summary>
        /// 接管 WM_PAINT / WM_ERASEBKGND：只为需要重绘的区域临时建一块 32 位位图，画完原样拷到窗口（保留透明度），随即释放。
        /// 返回 true 表示消息已处理。
        /// </summary>
        public static bool Paint(Control c, ref Message m, Action<PaintEventArgs> paint)
        {
            if (m.Msg == 0x14) { m.Result = (IntPtr)1; return true; }      // WM_ERASEBKGND：不擦背景
            if (m.Msg != 0x0F) return false;                                  // WM_PAINT
            var ps = new PAINTSTRUCT();
            IntPtr hdc = BeginPaint(c.Handle, ref ps);
            try
            {
                var rc = Rectangle.Intersect(Rectangle.FromLTRB(ps.l, ps.t, ps.r, ps.b), c.ClientRectangle);
                if (rc.Width <= 0 || rc.Height <= 0) return true;
                var bmi = new BMI { Size = 40, W = rc.Width, H = -rc.Height, Planes = 1, Bits = 32 };
                IntPtr bits;
                IntPtr dib = CreateDIBSection(hdc, ref bmi, 0, out bits, IntPtr.Zero, 0);
                if (dib == IntPtr.Zero) return true;
                try
                {
                    using (var bmp = new Bitmap(rc.Width, rc.Height, rc.Width * 4, PixelFormat.Format32bppPArgb, bits))
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.TranslateTransform(-rc.X, -rc.Y);
                        g.SetClip(rc);
                        try { paint(new PaintEventArgs(g, rc)); } catch { }
                    }
                    IntPtr mdc = CreateCompatibleDC(hdc);
                    var old = SelectObject(mdc, dib);
                    BitBlt(hdc, rc.X, rc.Y, rc.Width, rc.Height, mdc, 0, 0, 0x00CC0020);
                    SelectObject(mdc, old);
                    DeleteDC(mdc);
                }
                finally { DeleteObject(dib); }
            }
            finally { EndPaint(c.Handle, ref ps); }
            return true;
        }

        /// <summary>控件背景：透明（透出亚克力）+ 外层卡片/导航条的半透明色调</summary>
        public static void PaintBack(Control c, Graphics g)
        {
            var clip = Rectangle.Ceiling(g.VisibleClipBounds);
            if (clip.Width <= 0 || clip.Height <= 0) return;
            var cm = g.CompositingMode;
            g.CompositingMode = CompositingMode.SourceCopy;
            using (var b = new SolidBrush(Color.Transparent)) g.FillRectangle(b, clip);
            g.CompositingMode = cm;
            Layers(c.Parent, g, clip);
        }

        static void Layers(Control a, Graphics g, Rectangle clip)
        {
            if (a == null) return;
            Layers(a.Parent, g, clip);
            var l = a as IGlassLayer;
            if (l != null) using (var b = new SolidBrush(l.LayerTint)) g.FillRectangle(b, clip);
        }

        /// <summary>开启系统亚克力（实时模糊窗口后面的内容），色调和透明度跟随主题与档位</summary>
        public static void Acrylic(IntPtr h)
        {
            try
            {
                var t = Theme.P.Tint;
                int abgr = (Theme.TintAlpha << 24) | (t.B << 16) | (t.G << 8) | t.R;
                var a = new ACCENT { State = 4, Flags = 2, Color = abgr };
                int sz = Marshal.SizeOf(a);
                IntPtr p = Marshal.AllocHGlobal(sz);
                try
                {
                    Marshal.StructureToPtr(a, p, false);
                    var d = new WCAD { Attr = 19, Data = p, Size = sz };
                    SetWindowCompositionAttribute(h, ref d);
                }
                finally { Marshal.FreeHGlobal(p); }
            }
            catch { }
        }

        /// <summary>Win11 圆角 + 跟随主题的细边框</summary>
        public static void Dwm(IntPtr h, bool popup)
        {
            try
            {
                int v = popup ? 3 : 2; DwmSetWindowAttribute(h, 33, ref v, 4);
                var c = Theme.P.WindowBorder; int cr = c.R | (c.G << 8) | (c.B << 16);
                DwmSetWindowAttribute(h, 34, ref cr, 4);
            }
            catch { }
        }
    }

    /// <summary>透明磨砂玻璃窗口基类：无边框、圆角阴影、系统亚克力</summary>
    public class GlassForm : Form
    {
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

        public GlassForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Font = Theme.Body;
            BackColor = Theme.P.Base;
            Theme.Changed += OnTheme;
        }

        void OnTheme()
        {
            if (IsDisposed) return;
            BackColor = Theme.P.Base;
            if (IsHandleCreated) { Glass.Dwm(Handle, false); Glass.Acrylic(Handle); }
            Invalidate(true);
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ClassStyle |= 0x20000; return cp; }   // 阴影
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Glass.Dwm(Handle, false);
            Glass.Acrylic(Handle);
        }

        protected override void WndProc(ref Message m)
        {
            if (!Glass.Paint(this, ref m, e => { Glass.PaintBack(this, e.Graphics); OnPaint(e); })) base.WndProc(ref m);
        }

        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point p);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

        Dragger dragger;

        /// <summary>
        /// 按住拖动窗口：窗口自己跟着鼠标走。
        /// 不用系统的标题栏拖动——系统「拖动时显示窗口内容」关闭时，那种拖法只移动一个虚线框，松手后窗口才跳过去。
        /// 只在按住鼠标期间挂一个消息过滤器，松手立即移除。
        /// </summary>
        public void DragMove()
        {
            if (dragger != null) return;
            var src = Control.FromChildHandle(WindowFromPoint(Cursor.Position)) ?? this;
            dragger = new Dragger(this, src);
        }

        sealed class Dragger : IMessageFilter
        {
            readonly GlassForm f;
            readonly Control cap;
            readonly Point grab;
            public Dragger(GlassForm f, Control cap)
            {
                this.f = f; this.cap = cap;
                var p = Cursor.Position;
                grab = new Point(p.X - f.Left, p.Y - f.Top);
                cap.Capture = true;
                Application.AddMessageFilter(this);
            }
            public bool PreFilterMessage(ref Message m)
            {
                if (m.Msg == 0x0200)   // WM_MOUSEMOVE
                {
                    if ((Control.MouseButtons & MouseButtons.Left) == 0) { End(); return false; }
                    var p = Cursor.Position;
                    SetWindowPos(f.Handle, IntPtr.Zero, p.X - grab.X, p.Y - grab.Y, 0, 0, 0x0001 | 0x0004 | 0x0010);   // 只移动，不改大小/层级/激活
                    return true;
                }
                if (m.Msg == 0x0202 || m.Msg == 0x0205) End();   // 左键/右键松开
                return false;
            }
            void End()
            {
                Application.RemoveMessageFilter(this);
                try { if (!cap.IsDisposed) cap.Capture = false; } catch { }
                f.dragger = null;
            }
        }

        protected override void Dispose(bool disposing)
        {
            Theme.Changed -= OnTheme;
            base.Dispose(disposing);
        }
    }

    /// <summary>透明（玻璃）背景的容器</summary>
    public class GlassPanel : Panel
    {
        public GlassPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnPaintBackground(PaintEventArgs e) { Glass.PaintBack(this, e.Graphics); }
        protected override void WndProc(ref Message m)
        {
            if (!Glass.Paint(this, ref m, e => { OnPaintBackground(e); OnPaint(e); })) base.WndProc(ref m);
        }
    }

    /// <summary>
    /// 细滚动条：配合「原生滚动条被裁掉」的 AutoScroll 面板使用（面板比可见区域宽一个滚动条宽度）。
    /// </summary>
    public sealed class LiteScroll : Owner
    {
        Panel target;
        bool drag;
        float dragOff;
        public static int Hidden { get { return SystemInformation.VerticalScrollBarWidth; } }

        public LiteScroll() { Width = 8; }

        public Panel Target { get { return target; } set { target = value; Sync(); } }

        float Total { get { return target.DisplayRectangle.Height; } }
        float View { get { return target.ClientSize.Height; } }
        bool CanScroll { get { return target != null && Total > View + 1; } }

        RectangleF Thumb
        {
            get
            {
                float h = Math.Max(32, Height * View / Total);
                float pos = Math.Min(1, Math.Max(0, -target.AutoScrollPosition.Y / (Total - View)));
                return new RectangleF(2, (Height - h) * pos, Width - 4, h);
            }
        }

        public void Sync()
        {
            bool v = CanScroll;
            if (Visible != v) Visible = v;
            if (v) { BringToFront(); Invalidate(); }
        }

        public void ScrollTo(float y)
        {
            if (!CanScroll) return;
            int v = (int)Math.Max(0, Math.Min(Total - View, y));
            if (v == -target.AutoScrollPosition.Y) return;
            target.AutoScrollPosition = new Point(0, v);
            target.Invalidate(true);   // 玻璃背景跟着位置重画，避免滚动时整块平移造成错位
            Invalidate();
        }

        public void ScrollBy(int dy) { if (CanScroll) ScrollTo(-target.AutoScrollPosition.Y + dy); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (!CanScroll) return;
            var t = Thumb;
            if (e.Y >= t.Top && e.Y <= t.Bottom) { drag = true; dragOff = e.Y - t.Top; Capture = true; }
            else ScrollBy(e.Y < t.Top ? -(int)View : (int)View);
            base.OnMouseDown(e);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (drag && CanScroll)
            {
                float h = Thumb.Height;
                ScrollTo((e.Y - dragOff) / Math.Max(1, Height - h) * (Total - View));
            }
            base.OnMouseMove(e);
        }
        protected override void OnMouseUp(MouseEventArgs e) { drag = false; Capture = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Glass.PaintBack(this, g);
            if (!CanScroll) return;
            var t = Thumb;
            if (!(Hover || drag)) t = new RectangleF(t.X + 1, t.Y, t.Width - 2, t.Height);
            Theme.Fill(g, t, t.Width / 2, Color.FromArgb(Hover || drag ? 150 : 90, Theme.P.SubText));
        }
    }

    /// <summary>把滚轮交给当前可见的滚动区域（自绘控件不接收焦点）</summary>
    public sealed class WheelRouter : IMessageFilter
    {
        readonly Func<LiteScroll> pick;
        public WheelRouter(Func<LiteScroll> pick) { this.pick = pick; }
        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != 0x020A) return false;
            var s = pick();
            if (s == null || s.Target == null || !s.Target.IsHandleCreated || !s.Target.Visible) return false;
            var t = s.Target;
            if (t.FindForm() != Form.ActiveForm) return false;
            if (!t.ClientRectangle.Contains(t.PointToClient(Cursor.Position))) return false;
            int delta = (short)((long)m.WParam >> 16);
            s.ScrollBy(-delta / 2);
            return true;
        }
    }

    /// <summary>菜单（托盘右键、下拉框）：和玻璃主题一致的配色</summary>
    public sealed class GlassMenuRenderer : ToolStripRenderer
    {
        public static void Style(ToolStripDropDown m)
        {
            m.Renderer = new GlassMenuRenderer();
            m.Padding = new Padding(4, 6, 4, 6);
            m.Font = Theme.Body;
            m.HandleCreated += (s, e) => Glass.Dwm(((Control)s).Handle, true);
        }
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) { e.Graphics.Clear(Theme.P.Menu); }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (var pen = new Pen(Theme.P.WindowBorder)) e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            Theme.Fill(e.Graphics, new RectangleF(2, 1, e.Item.Width - 4, e.Item.Height - 2), 6, Theme.P.Hover);
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            var mi = e.Item as ToolStripMenuItem;
            e.TextColor = !e.Item.Enabled ? Theme.P.SubText : mi != null && mi.Checked ? Theme.P.Accent : Theme.P.Text;
            base.OnRenderItemText(e);
        }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var r = e.ImageRectangle;
            Brand.DrawBolt(e.Graphics, new RectangleF(r.X + r.Width / 2f - 5, r.Y + r.Height / 2f - 7, 10, 14), Theme.P.Text);
        }
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using (var pen = new Pen(Theme.P.WindowBorder)) e.Graphics.DrawLine(pen, 10, e.Item.Height / 2, e.Item.Width - 10, e.Item.Height / 2);
        }
    }
}
