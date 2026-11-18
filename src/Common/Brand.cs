// P15 CONTROL 品牌：闪电标志 + 应用图标（纯矢量绘制，不带任何图片文件）
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace P15
{
    public static class Brand
    {
        public const string Name = "P15 CONTROL";
        public const string Version = "2.0.5";
        public const string Url = "https://github.com/yctfjsdjb/p15-control";
        public static readonly Color Ink = Color.FromArgb(242, 243, 245);    // 标志的白
        public static readonly Color Graphite = Color.FromArgb(30, 31, 35);  // 图标底色（石墨）

        // 几何闪电：平切的粗顶边 + 两条平行的长斜边 + 尖底，像电气标识而不是插画
        static readonly PointF[] Bolt =
        {
            new PointF(0.56f, 0.00f), new PointF(0.86f, 0.00f), new PointF(0.60f, 0.40f), new PointF(0.84f, 0.40f),
            new PointF(0.30f, 1.00f), new PointF(0.44f, 0.56f), new PointF(0.16f, 0.56f)
        };

        public static GraphicsPath BoltPath(RectangleF r)
        {
            var pts = new PointF[Bolt.Length];
            for (int i = 0; i < pts.Length; i++) pts[i] = new PointF(r.X + Bolt[i].X * r.Width, r.Y + Bolt[i].Y * r.Height);
            var p = new GraphicsPath();
            p.AddPolygon(pts);
            return p;
        }

        /// <summary>单色闪电（实心）</summary>
        public static void DrawBolt(Graphics g, RectangleF r, Color c)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = BoltPath(r)) using (var b = new SolidBrush(c)) g.FillPath(b, path);
            g.SmoothingMode = sm;
        }

        /// <summary>单色闪电（描边）</summary>
        public static void DrawBoltOutline(Graphics g, RectangleF r, Color c, float width)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = BoltPath(r)) using (var pen = new Pen(c, width) { LineJoin = LineJoin.Miter }) g.DrawPath(pen, path);
            g.SmoothingMode = sm;
        }

        /// <summary>应用图标：石墨圆角方块 + 白色闪电，平面单色，无渐变无发光</summary>
        public static Bitmap Tile(int size)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                float s = size, pad = size <= 20 ? 0.5f : s * 0.04f;
                var box = new RectangleF(pad, pad, s - pad * 2, s - pad * 2);
                using (var path = Round(box, s * 0.22f))
                {
                    using (var bg = new SolidBrush(Graphite)) g.FillPath(bg, path);
                    if (size >= 32)
                        using (var pen = new Pen(Color.FromArgb(28, 255, 255, 255), Math.Max(1f, s / 64f))) g.DrawPath(pen, path);
                }
                float bi = size <= 20 ? s * 0.16f : s * 0.21f;
                DrawBolt(g, new RectangleF(bi, bi, s - bi * 2, s - bi * 2), Ink);
            }
            return bmp;
        }

        /// <summary>托盘图标：和系统托盘一样的单色图形。性能模式实心，其它模式描边</summary>
        public static Bitmap TrayGlyph(int size, bool filled, bool lightTaskbar)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                var c = lightTaskbar ? Color.FromArgb(28, 28, 30) : Color.White;
                float w = Math.Max(1f, size / 16f);
                var r = new RectangleF(w, w * 0.5f, size - w * 2, size - w);
                if (filled) DrawBolt(g, r, c); else DrawBoltOutline(g, new RectangleF(r.X + w / 2, r.Y + w / 2, r.Width - w, r.Height - w), c, w);
            }
            return bmp;
        }
        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = rad * 2;
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        /// <summary>生成多尺寸 .ico（PNG 压缩条目，Vista 起支持）</summary>
        public static void WriteIco(string file, int[] sizes)
        {
            // 48 以上用 PNG 压缩，小尺寸用标准 32 位位图（兼容所有读取 ico 的程序）
            var pngs = new List<byte[]>();
            foreach (var sz in sizes)
                using (var bmp = Tile(sz))
                using (var ms = new System.IO.MemoryStream())
                {
                    if (sz >= 256) bmp.Save(ms, ImageFormat.Png);
                    else
                    {
                        var w = new System.IO.BinaryWriter(ms);
                        w.Write(40); w.Write(sz); w.Write(sz * 2); w.Write((short)1); w.Write((short)32);
                        w.Write(0); w.Write(0); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
                        for (int y = sz - 1; y >= 0; y--)
                            for (int x = 0; x < sz; x++) { var c = bmp.GetPixel(x, y); w.Write(c.B); w.Write(c.G); w.Write(c.R); w.Write(c.A); }
                        int maskRow = ((sz + 31) / 32) * 4;
                        w.Write(new byte[maskRow * sz]);
                        w.Flush();
                    }
                    pngs.Add(ms.ToArray());
                }
            using (var fs = System.IO.File.Create(file))
            using (var w = new System.IO.BinaryWriter(fs))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int off = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
                    w.Write(pngs[i].Length); w.Write(off); off += pngs[i].Length;
                }
                foreach (var p in pngs) w.Write(p);
            }
        }
    }
}
