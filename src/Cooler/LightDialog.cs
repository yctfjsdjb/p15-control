using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace P15
{
    /// <summary>散热器灯效设置弹窗</summary>
    static class CoolerLightDialog
    {
        public sealed class Result { public Bs2Cooler.RgbMode Mode; public Color Color; public int Speed, Bright; }

        public static Result Show(IWin32Window owner, Result init)
        {
            var p = Theme.P;
            using (var f = new GlassForm())
            {
                f.FormBorderStyle = FormBorderStyle.None; f.StartPosition = FormStartPosition.CenterParent;
                f.Size = new Size(340, 410); f.ShowInTaskbar = false; f.KeyPreview = true;
                                f.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) f.DialogResult = DialogResult.Cancel; };
                var res = new Result { Mode = init.Mode, Color = init.Color, Speed = init.Speed, Bright = init.Bright };

                var title = new Label2("散热器灯效") { Font = Theme.H2, Bounds = new Rectangle(18, 14, 300, 26) };
                title.MouseDown += (s, e) => f.DragMove();
                f.Controls.Add(title);

                f.Controls.Add(new Label2("效果", true) { Bounds = new Rectangle(18, 50, 60, 22) });
                string[] names = { "纯色", "呼吸", "彩虹流光", "单色旋转", "随温度变色" };
                var modes = new Choice[5];
                for (int i = 0; i < 5; i++)
                {
                    int idx = i;
                    modes[i] = new Choice(null, names[i], null) { Bounds = new Rectangle(18 + (i % 3) * 102, 74 + (i / 3) * 38, 96, 32) };
                    f.Controls.Add(modes[i]);
                }

                f.Controls.Add(new Label2("颜色", true) { Bounds = new Rectangle(18, 158, 60, 22) });
                var cols = new[] { Color.FromArgb(255, 255, 255), Color.FromArgb(255, 0, 0), Color.FromArgb(255, 120, 0), Color.FromArgb(255, 230, 0),
                                   Color.FromArgb(0, 255, 0), Color.FromArgb(0, 230, 255), Color.FromArgb(0, 80, 255), Color.FromArgb(170, 0, 255) };
                var sws = new List<Swatch>();
                foreach (var c in cols)
                {
                    var sw = new Swatch(c) { Location = new Point(14 + sws.Count * 32, 182) };
                    sws.Add(sw); f.Controls.Add(sw);
                }
                var more = new Btn("…") { Bounds = new Rectangle(14 + 8 * 32 + 4, 182, 40, 28) };
                f.Controls.Add(more);
                var preview = new Owner2Box { Bounds = new Rectangle(18, 216, 304, 8) };
                f.Controls.Add(preview);

                var lb = new Label2("亮度", true) { Bounds = new Rectangle(18, 236, 200, 22) };
                var sb = new Slider { Max = 10, Bounds = new Rectangle(10, 258, 320, 28), Value = res.Bright / 10 };
                var ls = new Label2("速度", true) { Bounds = new Rectangle(18, 292, 200, 22) };
                var ss = new Slider { Max = 9, Bounds = new Rectangle(10, 314, 320, 28), Value = res.Speed - 1 };
                f.Controls.AddRange(new Control[] { lb, sb, ls, ss });

                Action sync = () =>
                {
                    for (int i = 0; i < 5; i++) modes[i].Selected = (int)res.Mode == i;
                    bool needColor = res.Mode == Bs2Cooler.RgbMode.Pure || res.Mode == Bs2Cooler.RgbMode.Breathe || res.Mode == Bs2Cooler.RgbMode.Rotate;
                    foreach (var sw in sws) { sw.Enabled = needColor; sw.Selected = needColor && sw.Color.ToArgb() == res.Color.ToArgb(); }
                    more.Enabled = needColor;
                    bool table = res.Mode != Bs2Cooler.RgbMode.Temperature;
                    sb.Enabled = table; ss.Enabled = table && res.Mode != Bs2Cooler.RgbMode.Pure;
                    lb.Text = "亮度  " + res.Bright + "%";
                    ls.Text = "速度  " + res.Speed + " / 10";
                    preview.Fill = needColor ? res.Color : (Color?)null;
                    preview.Invalidate();
                };
                for (int i = 0; i < 5; i++) { int idx = i; modes[i].Click += (s, e) => { res.Mode = (Bs2Cooler.RgbMode)idx; sync(); }; }
                foreach (var sw in sws) sw.Click += (s, e) => { res.Color = ((Swatch)s).Color; sync(); };
                more.Click += (s, e) =>
                {
                    using (var d = new ColorDialog { FullOpen = true, Color = res.Color })
                        if (d.ShowDialog(f) == DialogResult.OK) { res.Color = d.Color; sync(); }
                };
                sb.ValueChanged += (s, e) => { res.Bright = Math.Max(10, sb.Value * 10); sync(); };
                ss.ValueChanged += (s, e) => { res.Speed = ss.Value + 1; sync(); };

                var ok = new Btn("应用", true) { Bounds = new Rectangle(176, 360, 146, 34) };
                var cancel = new Btn("取消") { Bounds = new Rectangle(18, 360, 146, 34) };
                ok.Click += (s, e) => f.DialogResult = DialogResult.OK;
                cancel.Click += (s, e) => f.DialogResult = DialogResult.Cancel;
                f.Controls.Add(ok); f.Controls.Add(cancel);
                sync();
                return f.ShowDialog(owner) == DialogResult.OK ? res : null;
            }
        }

        /// <summary>颜色预览条（彩虹/随温度时显示渐变）</summary>
        sealed class Owner2Box : Owner
        {
            public Color? Fill;
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics; Glass.PaintBack(this, g);
                var r = new RectangleF(0, 0, Width - 1, Height - 1);
                if (Fill.HasValue) { Theme.Fill(g, r, 4, Fill.Value); return; }
                using (var br = new LinearGradientBrush(ClientRectangle, Color.Red, Color.Blue, 0f))
                {
                    var cb = new ColorBlend
                    {
                        Colors = new[] { Color.Red, Color.Orange, Color.Yellow, Color.Lime, Color.Cyan, Color.Blue, Color.Magenta },
                        Positions = new[] { 0f, 0.17f, 0.33f, 0.5f, 0.67f, 0.83f, 1f }
                    };
                    br.InterpolationColors = cb;
                    using (var path = Theme.Round(r, 4)) { g.SmoothingMode = SmoothingMode.AntiAlias; g.FillPath(br, path); }
                }
            }
        }
    }

}
