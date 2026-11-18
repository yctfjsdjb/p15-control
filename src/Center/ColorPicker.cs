// 主题化的取色器：替换系统 ColorDialog。磨砂玻璃窗口 + 饱和度/亮度面板 + 色相条，只在打开时存在。
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace P15
{
    public static class ColorPicker
    {
        /// <summary>弹出取色器；确定返回所选颜色，取消返回 null</summary>
        public static Color? Show(IWin32Window owner, Color init, string title = "自定义颜色")
        {
            float h, s, v; ToHsv(init, out h, out s, out v);
            using (var f = new GlassForm())
            {
                f.StartPosition = FormStartPosition.CenterParent;
                f.Size = new Size(340, 392); f.ShowInTaskbar = false; f.KeyPreview = true;

                var t = new Label2(title) { Font = Theme.H2, Bounds = new Rectangle(18, 14, 304, 26) };
                t.MouseDown += (o, e) => f.DragMove();
                var plane = new SvPlane { Bounds = new Rectangle(11, 46, 318, 190), Hue = h, S = s, V = v };
                var bar = new HueBar { Bounds = new Rectangle(8, 244, 324, 28), Hue = h };
                var chip = new Chip { Bounds = new Rectangle(18, 286, 30, 30) };
                var hex = new Label2("") { Font = Theme.Num, Bounds = new Rectangle(58, 284, 120, 34) };
                var rgb = new Label2("", true) { Font = Theme.NumSmall, Bounds = new Rectangle(160, 284, 162, 34), Align = ContentAlignment.MiddleRight };
                var cancel = new Btn("取消") { Bounds = new Rectangle(18, 336, 146, 34) };
                var ok = new Btn("确定", true) { Bounds = new Rectangle(176, 336, 146, 34) };

                Action sync = () =>
                {
                    var c = FromHsv(plane.Hue, plane.S, plane.V);
                    chip.Color = c; chip.Invalidate();
                    hex.Text = "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
                    rgb.Text = "R " + c.R + "   G " + c.G + "   B " + c.B;
                };
                plane.Changed += sync;
                bar.Changed += () => { plane.Hue = bar.Hue; plane.Invalidate(); sync(); };
                ok.Click += (o, e) => f.DialogResult = DialogResult.OK;
                cancel.Click += (o, e) => f.DialogResult = DialogResult.Cancel;
                f.KeyDown += (o, e) =>
                {
                    if (e.KeyCode == Keys.Escape) f.DialogResult = DialogResult.Cancel;
                    else if (e.KeyCode == Keys.Enter) f.DialogResult = DialogResult.OK;
                };
                f.Controls.AddRange(new Control[] { t, plane, bar, chip, hex, rgb, cancel, ok });
                sync();
                return f.ShowDialog(owner) == DialogResult.OK ? FromHsv(plane.Hue, plane.S, plane.V) : (Color?)null;
            }
        }

        // ---------- HSV（H 0~360，S/V 0~1） ----------
        public static void ToHsv(Color c, out float h, out float s, out float v)
        {
            float r = c.R / 255f, g = c.G / 255f, b = c.B / 255f;
            float max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
            v = max; s = max <= 0 ? 0 : d / max;
            if (d <= 0) h = 0;
            else if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * ((b - r) / d + 2);
            else h = 60 * ((r - g) / d + 4);
            if (h < 0) h += 360;
        }

        public static Color FromHsv(float h, float s, float v)
        {
            h = ((h % 360) + 360) % 360;
            float c = v * s, x = c * (1 - Math.Abs((h / 60) % 2 - 1)), m = v - c, r, g, b;
            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }
            Func<float, int> k = u => Math.Max(0, Math.Min(255, (int)Math.Round((u + m) * 255)));
            return Color.FromArgb(k(r), k(g), k(b));
        }

        /// <summary>饱和度（横向）/ 亮度（纵向）面板，四周留出标记圈的空间</summary>
        sealed class SvPlane : Owner
        {
            const int Pad = 7;
            public float Hue, S, V;
            public event Action Changed;
            public SvPlane() { Cursor = Cursors.Cross; }
            RectangleF Area { get { return new RectangleF(Pad, Pad, Width - Pad * 2, Height - Pad * 2); } }

            void Pick(Point pt)
            {
                var a = Area;
                S = Math.Max(0, Math.Min(1, (pt.X - a.X) / a.Width));
                V = 1 - Math.Max(0, Math.Min(1, (pt.Y - a.Y) / a.Height));
                Invalidate();
                var h = Changed; if (h != null) h();
            }
            protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { Capture = true; Pick(e.Location); } base.OnMouseDown(e); }
            protected override void OnMouseMove(MouseEventArgs e) { if (Capture && e.Button == MouseButtons.Left) Pick(e.Location); base.OnMouseMove(e); }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics; Back(g);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var a = Area;
                var grow = RectangleF.Inflate(a, 1, 1);   // 渐变刷比区域大一圈，避免边缘出现反色细线
                using (var path = Theme.Round(a, 8))
                {
                    using (var sat = new LinearGradientBrush(grow, Color.White, FromHsv(Hue, 1, 1), 0f)) g.FillPath(sat, path);
                    using (var val = new LinearGradientBrush(grow, Color.FromArgb(0, 0, 0, 0), Color.Black, 90f)) g.FillPath(val, path);
                    using (var pen = new Pen(Theme.P.Stroke)) g.DrawPath(pen, path);
                }
                float x = a.X + S * a.Width, y = a.Y + (1 - V) * a.Height;
                using (var sh = new Pen(Color.FromArgb(110, 0, 0, 0), 4f)) g.DrawEllipse(sh, x - 6.5f, y - 6.5f, 13, 13);
                using (var w = new Pen(Color.White, 2f)) g.DrawEllipse(w, x - 6.5f, y - 6.5f, 13, 13);
            }
        }

        /// <summary>色相条：样式与滑块一致（细轨道 + 白色圆钮），钮心显示当前色相</summary>
        sealed class HueBar : Owner
        {
            public float Hue;
            public event Action Changed;
            public HueBar() { Cursor = Cursors.Hand; }
            float X0 { get { return 10; } }
            float W { get { return Width - 20f; } }

            void Pick(int x)
            {
                Hue = Math.Max(0, Math.Min(359.9f, (x - X0) / W * 360));
                Invalidate();
                var h = Changed; if (h != null) h();
            }
            protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { Capture = true; Pick(e.X); } base.OnMouseDown(e); }
            protected override void OnMouseMove(MouseEventArgs e) { if (Capture && e.Button == MouseButtons.Left) Pick(e.X); base.OnMouseMove(e); }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics; Back(g);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float cy = Height / 2f;
                var track = new RectangleF(X0, cy - 4, W, 8);
                using (var path = Theme.Round(track, 4))
                using (var br = new LinearGradientBrush(RectangleF.Inflate(track, 1, 0), Color.Red, Color.Red, 0f))
                {
                    var cb = new ColorBlend(7);
                    cb.Colors = new[] { Color.FromArgb(255, 0, 0), Color.FromArgb(255, 255, 0), Color.FromArgb(0, 255, 0), Color.FromArgb(0, 255, 255),
                                        Color.FromArgb(0, 0, 255), Color.FromArgb(255, 0, 255), Color.FromArgb(255, 0, 0) };
                    cb.Positions = new[] { 0f, 1 / 6f, 2 / 6f, 3 / 6f, 4 / 6f, 5 / 6f, 1f };
                    br.InterpolationColors = cb;
                    g.FillPath(br, path);
                }
                float kx = X0 + Hue / 360f * W;
                if (Hover) using (var gl = new SolidBrush(Color.FromArgb(40, Theme.P.Text))) g.FillEllipse(gl, kx - 12, cy - 12, 24, 24);
                using (var sh = new SolidBrush(Color.FromArgb(60, 0, 0, 0))) g.FillEllipse(sh, kx - 9, cy - 8, 18, 18);
                using (var w = new SolidBrush(Theme.P.Solid)) g.FillEllipse(w, kx - 9, cy - 9, 18, 18);
                using (var c = new SolidBrush(FromHsv(Hue, 1, 1))) g.FillEllipse(c, kx - 5, cy - 5, 10, 10);
            }
        }

        /// <summary>当前颜色的圆形色块</summary>
        sealed class Chip : Owner
        {
            public Color Color;
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics; Back(g);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var b = new SolidBrush(Color)) g.FillEllipse(b, 1, 1, Width - 3, Height - 3);
                using (var pen = new Pen(Theme.P.Stroke)) g.DrawEllipse(pen, 1, 1, Width - 3, Height - 3);
            }
        }
    }
}
