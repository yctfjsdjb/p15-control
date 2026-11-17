using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace P15
{
    /// <summary>风扇曲线编辑器：拖动中间两个点（温度 → 转速%）</summary>
    sealed class FanCurveEditor : Owner
    {
        FanCurve c;
        int drag = -1, hover = -1;
        public event EventHandler Changed;
        const int L = 42, Tp = 10, R = 12, B = 26, TMin = 30;

        public FanCurve Curve { get { return c; } set { c = value; Invalidate(); } }

        RectangleF Plot { get { return new RectangleF(L, Tp, Width - L - R, Height - Tp - B); } }
        PointF ToScreen(int t, int d)
        {
            var p = Plot;
            return new PointF(p.X + (t - TMin) * p.Width / (100 - TMin), p.Bottom - d * p.Height / 100f);
        }
        PointF[] Points()
        {
            return new[] { ToScreen(c.T1, c.D1), ToScreen(c.T2, c.D2), ToScreen(c.T3, c.D3), ToScreen(100, 100) };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; var p = Theme.P;
            Back(g);
            Theme.Glass(g, new RectangleF(0.5f, 0.5f, Width - 2, Height - 2), 10, p.Inset);
            var plot = Plot;
            using (var grid = new Pen(p.Stroke))
                for (int i = 0; i <= 4; i++)
                {
                    float y = plot.Bottom - i * plot.Height / 4;
                    g.DrawLine(grid, plot.Left, y, plot.Right, y);
                    Theme.Text(g, (i * 25) + "%", Theme.Small, p.SubText, new RectangleF(0, y - 9, L - 4, 18), ContentAlignment.MiddleRight);
                }
            for (int t = TMin; t <= 100; t += 10)
            {
                var x = ToScreen(t, 0).X;
                Theme.Text(g, t + "°", Theme.Small, p.SubText, new RectangleF(x - 16, plot.Bottom + 4, 32, 18), ContentAlignment.MiddleCenter);
            }
            if (c == null) return;
            var pts = Points();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = new GraphicsPath())
            {
                path.AddLine(pts[0].X, plot.Bottom, pts[0].X, pts[0].Y);
                path.AddLines(pts);
                path.AddLine(pts[3].X, pts[3].Y, pts[3].X, plot.Bottom);
                using (var fill = new LinearGradientBrush(new RectangleF(plot.X, plot.Y - 1, plot.Width, plot.Height + 2), Color.FromArgb(60, p.Text), Color.FromArgb(0, p.Text), 90f)) g.FillPath(fill, path);
            }
            using (var pen = new Pen(p.Text, 2f) { LineJoin = LineJoin.Round }) g.DrawLines(pen, pts);
            for (int i = 0; i < 4; i++)
            {
                bool movable = i == 1 || i == 2;
                float r = movable ? (i == drag || i == hover ? 7 : 6) : 4;
                if (movable) using (var gl = new SolidBrush(Color.FromArgb(40, p.Text))) g.FillEllipse(gl, pts[i].X - r - 4, pts[i].Y - r - 4, r * 2 + 8, r * 2 + 8);
                using (var b = new SolidBrush(movable ? p.Solid : p.SubText)) g.FillEllipse(b, pts[i].X - r, pts[i].Y - r, r * 2, r * 2);
                if (movable) using (var w = new SolidBrush(p.AccentText)) g.FillEllipse(w, pts[i].X - 2.5f, pts[i].Y - 2.5f, 5, 5);
            }
            int sel = drag >= 0 ? drag : hover;
            if (sel == 1 || sel == 2)
            {
                int t = sel == 1 ? c.T2 : c.T3, d = sel == 1 ? c.D2 : c.D3;
                var s = t + "°C → " + d + "%";
                var pt = pts[sel];
                var box = new RectangleF(Math.Min(pt.X + 8, Width - 96), Math.Max(pt.Y - 30, 2), 88, 22);
                Theme.Fill(g, box, 6, Color.FromArgb(235, p.Menu)); Theme.Line(g, box, 6, p.WindowBorder);
                Theme.Text(g, s, Theme.Small, p.Text, box, ContentAlignment.MiddleCenter);
            }
        }

        int Hit(Point m)
        {
            if (c == null) return -1;
            var pts = Points();
            for (int i = 1; i <= 2; i++)
                if (Math.Abs(pts[i].X - m.X) < 12 && Math.Abs(pts[i].Y - m.Y) < 12) return i;
            return -1;
        }

        protected override void OnMouseDown(MouseEventArgs e) { drag = Hit(e.Location); Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (drag >= 0) { drag = -1; Invalidate(); var h = Changed; if (h != null) h(this, EventArgs.Empty); }
            base.OnMouseUp(e);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (drag < 0)
            {
                int h = Hit(e.Location);
                Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
                if (h != hover) { hover = h; Invalidate(); }
                return;
            }
            var plot = Plot;
            int t = (int)Math.Round(TMin + (e.X - plot.X) * (100 - TMin) / plot.Width);
            int d = (int)Math.Round((plot.Bottom - e.Y) * 100 / plot.Height);
            if (drag == 1)
            {
                c.T2 = Math.Max(c.T1 + 1, Math.Min(c.T3 - 1, t));
                c.D2 = Math.Max(c.D1 + 1, Math.Min(c.D3 - 1, d));
            }
            else
            {
                c.T3 = Math.Max(c.T2 + 1, Math.Min(99, t));
                c.D3 = Math.Max(c.D2 + 1, Math.Min(99, d));
            }
            Invalidate();
        }
    }

    /// <summary>简单滑块</summary>
    public sealed class Slider : Owner
    {
        int val, max = 10;
        public event EventHandler ValueChanged;
        public int Max { get { return max; } set { max = value; Invalidate(); } }
        public int Value { get { return val; } set { val = Math.Max(0, Math.Min(max, value)); Invalidate(); } }
        public Slider() { Height = 28; Cursor = Cursors.Hand; }
        float X(int v) { return 10 + v * (Width - 20f) / max; }
        void SetFromMouse(int x)
        {
            int v = (int)Math.Round((x - 10) * max / (Width - 20f));
            v = Math.Max(0, Math.Min(max, v));
            if (v != val) { val = v; Invalidate(); var h = ValueChanged; if (h != null) h(this, EventArgs.Empty); }
        }
        protected override void OnMouseDown(MouseEventArgs e) { if (Enabled) { Capture = true; SetFromMouse(e.X); } base.OnMouseDown(e); }
        protected override void OnMouseMove(MouseEventArgs e) { if (Capture && e.Button == MouseButtons.Left) SetFromMouse(e.X); base.OnMouseMove(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; var p = Theme.P;
            Back(g);
            float cy = Height / 2f, kx = X(val);
            Theme.Fill(g, new RectangleF(10, cy - 2.5f, Width - 20, 5), 2.5f, p.Track);
            if (kx > 12)
            {
                if (Enabled) Theme.FillGrad(g, new RectangleF(10, cy - 2.5f, kx - 10, 5), 2.5f);
                else Theme.Fill(g, new RectangleF(10, cy - 2.5f, kx - 10, 5), 2.5f, p.SubText);
            }
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (Enabled && Hover) using (var gl = new SolidBrush(Color.FromArgb(40, p.Text))) g.FillEllipse(gl, kx - 12, cy - 12, 24, 24);
            using (var sh = new SolidBrush(Color.FromArgb(50, 0, 0, 0))) g.FillEllipse(sh, kx - 8, cy - 7, 16, 16);
            using (var w = new SolidBrush(Enabled ? p.Solid : p.SubText)) g.FillEllipse(w, kx - 8, cy - 8, 16, 16);
            if (Enabled) using (var c = new SolidBrush(p.AccentText)) g.FillEllipse(c, kx - 3, cy - 3, 6, 6);
        }
    }

    /// <summary>驱动页的一行：圆点 + 名称/说明 + 右侧操作按钮或状态</summary>
    sealed class DrvRow : Owner
    {
        public string Title = "", Sub = "", Status = "", ActionText;
        public Color Dot;
        public Color? StatusColor;
        public event EventHandler Action;
        bool hotBtn;
        public DrvRow() { Height = 50; }
        RectangleF BtnRect { get { return new RectangleF(Width - 70, (Height - 28) / 2f, 60, 28); } }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool h = ActionText != null && BtnRect.Contains(e.Location);
            if (h != hotBtn) { hotBtn = h; Cursor = h ? Cursors.Hand : Cursors.Default; Invalidate(); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { hotBtn = false; base.OnMouseLeave(e); }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (ActionText != null && BtnRect.Contains(e.Location)) { var h = Action; if (h != null) h(this, EventArgs.Empty); }
            base.OnMouseClick(e);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; var p = Theme.P;
            Back(g);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(Dot)) g.FillEllipse(b, 4, Height / 2f - 4, 8, 8);
            int right = ActionText != null ? 80 : 96;
            Theme.Text(g, Title, Theme.Body, p.Text, new RectangleF(20, string.IsNullOrEmpty(Sub) ? 0 : 6, Width - 20 - right, string.IsNullOrEmpty(Sub) ? Height : 20));
            if (!string.IsNullOrEmpty(Sub))
                Theme.Text(g, Sub, Theme.Small, p.SubText, new RectangleF(20, 27, Width - 20 - right, 18));
            if (ActionText != null)
            {
                var r = BtnRect;
                Theme.FillGrad(g, r, 14);
                if (hotBtn) Theme.Fill(g, r, 14, Color.FromArgb(50, 0, 0, 0));
                Theme.Text(g, ActionText, Theme.Small, p.AccentText, r, ContentAlignment.MiddleCenter);
            }
            else
                Theme.Text(g, Status, Theme.Small, StatusColor ?? p.SubText, new RectangleF(Width - 96, 0, 90, Height), ContentAlignment.MiddleRight);
        }
    }

    /// <summary>可折叠分组标题</summary>
    sealed class SectionHead : Owner
    {
        public bool Open;
        public string Hint = "";
        public SectionHead(string text) { Text = text; Height = 30; Cursor = Cursors.Hand; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; var p = Theme.P;
            Back(g);
            Theme.Text(g, Text, Theme.H2, p.Text, new RectangleF(2, 0, Width - 130, Height));
            Theme.Text(g, Open ? "\uE70E" : "\uE70D", Theme.IconTiny, p.SubText, new RectangleF(Width - 20, 0, 20, Height), ContentAlignment.MiddleCenter);
            Theme.Text(g, Hint, Theme.Small, p.SubText, new RectangleF(Width - 140, 0, 116, Height), ContentAlignment.MiddleRight);
        }
    }

    /// <summary>颜色色块</summary>
    public sealed class Swatch : Owner
    {
        public Color Color { get; set; }
        bool sel;
        public bool Selected { get { return sel; } set { sel = value; Invalidate(); } }
        public Swatch(Color c) { Color = c; Size = new Size(28, 28); Cursor = Cursors.Hand; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; var p = Theme.P;
            Back(g);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (sel && Enabled) using (var pen = new Pen(p.Text, 2)) g.DrawEllipse(pen, 1, 1, Width - 3, Height - 3);
            else if (Hover && Enabled) using (var pen = new Pen(p.SubText, 1.5f)) g.DrawEllipse(pen, 1.5f, 1.5f, Width - 4, Height - 4);
            using (var b = new SolidBrush(Enabled ? Color : Color.FromArgb(70, Color))) g.FillEllipse(b, 5, 5, Width - 11, Height - 11);
        }
    }
}
