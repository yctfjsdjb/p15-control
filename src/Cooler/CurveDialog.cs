using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace P15
{
    /// <summary>智能变频「自定义」曲线：30~100°C 共 8 个点，上下拖动设置转速（与官方一致）</summary>
    static class CurveDialog
    {
        sealed class Editor : Owner
        {
            public int[] V;
            public int Max = 4000;
            int drag = -1, hover = -1;
            const int L = 44, T = 14, R = 14, B = 26, Min = 1300, TopRpm = 4000;
            RectangleF Plot { get { return new RectangleF(L, T, Width - L - R, Height - T - B); } }
            PointF Pt(int i) { var p = Plot; return new PointF(p.X + i * p.Width / 7f, p.Bottom - (V[i] - Min) * p.Height / (TopRpm - Min)); }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics; var p = Theme.P; var pl = Plot;
                Back(g);
                Theme.Glass(g, new RectangleF(0.5f, 0.5f, Width - 2, Height - 2), 10, p.Inset);
                using (var grid = new Pen(p.Stroke))
                    for (int r = 1300; r <= 4000; r += 900)
                    {
                        float y = pl.Bottom - (r - Min) * pl.Height / (TopRpm - Min);
                        g.DrawLine(grid, pl.Left, y, pl.Right, y);
                        Theme.Text(g, r.ToString(), Theme.Small, p.SubText, new RectangleF(0, y - 9, L - 4, 18), ContentAlignment.MiddleRight);
                    }
                for (int i = 0; i < 8; i++)
                    Theme.Text(g, (30 + i * 10) + "°", Theme.Small, p.SubText, new RectangleF(Pt(i).X - 16, pl.Bottom + 4, 32, 18), ContentAlignment.MiddleCenter);
                // 当前供电允许的最高转速
                if (Max < TopRpm)
                {
                    float my = pl.Bottom - (Max - Min) * pl.Height / (TopRpm - Min);
                    using (var pen = new Pen(p.Warn) { DashStyle = DashStyle.Dash }) g.DrawLine(pen, pl.Left, my, pl.Right, my);
                }
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var pts = new PointF[8];
                for (int i = 0; i < 8; i++) pts[i] = Pt(i);
                using (var path = new GraphicsPath())
                {
                    path.AddLine(pts[0].X, pl.Bottom, pts[0].X, pts[0].Y); path.AddLines(pts); path.AddLine(pts[7].X, pts[7].Y, pts[7].X, pl.Bottom);
                    using (var fill = new LinearGradientBrush(new RectangleF(pl.X, pl.Y - 1, pl.Width, pl.Height + 2), Color.FromArgb(60, p.Text), Color.FromArgb(0, p.Text), 90f)) g.FillPath(fill, path);
                }
                using (var pen = new Pen(p.Text, 2f) { LineJoin = LineJoin.Round }) g.DrawLines(pen, pts);
                for (int i = 0; i < 8; i++)
                {
                    float r = i == drag || i == hover ? 7 : 5.5f;
                    using (var gl = new SolidBrush(Color.FromArgb(40, p.Text))) g.FillEllipse(gl, pts[i].X - r - 3, pts[i].Y - r - 3, r * 2 + 6, r * 2 + 6);
                    using (var b = new SolidBrush(p.Solid)) g.FillEllipse(b, pts[i].X - r, pts[i].Y - r, r * 2, r * 2);
                    using (var w = new SolidBrush(p.AccentText)) g.FillEllipse(w, pts[i].X - 2.5f, pts[i].Y - 2.5f, 5, 5);
                }
                int sel = drag >= 0 ? drag : hover;
                if (sel >= 0)
                {
                    var s = (30 + sel * 10) + "°C → " + V[sel] + " rpm";
                    var box = new RectangleF(Math.Min(pts[sel].X + 8, Width - 124), Math.Max(pts[sel].Y - 30, 2), 116, 22);
                    Theme.Fill(g, box, 6, Color.FromArgb(240, p.Menu)); Theme.Line(g, box, 6, p.WindowBorder);
                    Theme.Text(g, s, Theme.Small, p.Text, box, ContentAlignment.MiddleCenter);
                }
            }

            int Hit(Point m) { for (int i = 0; i < 8; i++) { var pt = Pt(i); if (Math.Abs(pt.X - m.X) < 14 && Math.Abs(pt.Y - m.Y) < 14) return i; } return -1; }
            protected override void OnMouseDown(MouseEventArgs e) { drag = Hit(e.Location); Invalidate(); base.OnMouseDown(e); }
            protected override void OnMouseUp(MouseEventArgs e) { drag = -1; Invalidate(); base.OnMouseUp(e); }
            protected override void OnMouseMove(MouseEventArgs e)
            {
                if (drag < 0)
                {
                    int h = Hit(e.Location);
                    Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
                    if (h != hover) { hover = h; Invalidate(); }
                    return;
                }
                var pl = Plot;
                int v = (int)Math.Round((Min + (pl.Bottom - e.Y) * (TopRpm - Min) / pl.Height) / 50.0) * 50;
                V[drag] = Math.Max(Min, Math.Min(TopRpm, v));
                // 保持曲线不下降：温度越高转速不能越低
                for (int i = drag + 1; i < 8; i++) V[i] = Math.Max(V[i], V[drag]);
                for (int i = drag - 1; i >= 0; i--) V[i] = Math.Min(V[i], V[drag]);
                Invalidate();
            }
        }

        public static int[] Show(IWin32Window owner, int[] curve, int maxRpm)
        {
            var p = Theme.P;
            using (var f = new GlassForm())
            {
                f.FormBorderStyle = FormBorderStyle.None; f.StartPosition = FormStartPosition.CenterParent;
                f.Size = new Size(340, 400); f.ShowInTaskbar = false; f.KeyPreview = true;
                                f.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) f.DialogResult = DialogResult.Cancel; };
                var title = new Label2("自定义温度-转速曲线") { Font = Theme.H2, Bounds = new Rectangle(18, 14, 300, 26) };
                title.MouseDown += (s, e) => f.DragMove();
                f.Controls.Add(title);
                f.Controls.Add(new Label2("上下拖动圆点。按 CPU/GPU 中较高的温度调速" + (maxRpm < 4000 ? "；虚线为当前供电的最高转速 " + maxRpm : ""), true)
                { Bounds = new Rectangle(18, 40, 304, 36), Align = ContentAlignment.TopLeft });
                var ed = new Editor { V = (int[])curve.Clone(), Max = maxRpm, Bounds = new Rectangle(14, 80, 312, 250) };
                f.Controls.Add(ed);
                var reset = new Btn("恢复默认") { Bounds = new Rectangle(18, 348, 96, 34) };
                var cancel = new Btn("取消") { Bounds = new Rectangle(122, 348, 96, 34) };
                var ok = new Btn("保存", true) { Bounds = new Rectangle(226, 348, 96, 34) };
                reset.Click += (s, e) => { ed.V = (int[])Bs2Cooler.DefaultCustom.Clone(); ed.Invalidate(); };
                cancel.Click += (s, e) => f.DialogResult = DialogResult.Cancel;
                ok.Click += (s, e) => f.DialogResult = DialogResult.OK;
                f.Controls.AddRange(new Control[] { reset, cancel, ok });
                return f.ShowDialog(owner) == DialogResult.OK ? ed.V : null;
            }
        }
    }
}
