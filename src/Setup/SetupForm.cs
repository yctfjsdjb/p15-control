using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace P15
{
    sealed class SetupRow : Owner
    {
        public readonly DriverItem Item;
        public DriverState State = new DriverState();
        public bool Checked;
        public int Progress = -1;
        public string Phase = "";
        public SetupRow(DriverItem d) { Item = d; Height = 64; Cursor = Cursors.Hand; }
        protected override void OnClick(EventArgs e) { if (Enabled) { Checked = !Checked; Invalidate(); } base.OnClick(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; var p = Theme.P;
            Back(g);
            Theme.Glass(g, new RectangleF(0.5f, 0.5f, Width - 2, Height - 6), 9, Hover && Enabled ? p.Hover : p.Inset);
            // 复选框
            var box = new RectangleF(10, 13, 18, 18);
            if (Checked) Theme.FillGrad(g, box, 5); else Theme.Glass(g, box, 5, p.Track);
            if (Checked) Theme.Text(g, "\uE73E", Theme.IconSmall, p.AccentText, box, ContentAlignment.MiddleCenter);
            Theme.Text(g, Item.Name, Theme.Body, p.Text, new RectangleF(36, 5, Width - 130, 22));
            Color c = State.Status == DrvStatus.Installed ? p.Good : State.Status == DrvStatus.Failed ? p.Bad
                    : State.Status == DrvStatus.Installing ? p.Accent : State.Status == DrvStatus.Unknown ? p.SubText : p.Warn;
            var st = State.Status == DrvStatus.Installing ? Phase : Catalog.StatusName(State.Status);
            Theme.Text(g, st, Theme.Small, c, new RectangleF(Width - 100, 5, 90, 22), ContentAlignment.MiddleRight);
            string sub = State.Status == DrvStatus.Failed && State.Message != "" ? State.Message
                       : Item.Note + " · " + Item.Source + (Item.Background ? " · 常驻后台" : "") + " · 约" + Item.SizeMB + "MB";
            Theme.Text(g, sub, Theme.Small, p.SubText, new RectangleF(36, 27, Width - 46, 30), ContentAlignment.TopLeft);
            if (Progress >= 0)
            {
                Theme.Fill(g, new RectangleF(36, Height - 10, Width - 46, 3), 1.5f, p.Track);
                if (Progress > 0) Theme.FillGrad(g, new RectangleF(36, Height - 10, Math.Max(3, (Width - 46) * Math.Min(100, Progress) / 100f), 3), 1.5f);
            }
        }
    }

    public sealed class SetupForm : GlassForm
    {
        const int W = 380, H = 700, Pad = 14;
        readonly List<SetupRow> rows = new List<SetupRow>();
        readonly Label2 status = new Label2("", true);
        Btn bInstall, bClean, bSelMissing;
        Panel list;
        readonly string mode;
        readonly string[] preselect;
        CancellationTokenSource cts;
        bool working;

        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr h, string app, string id);

        public SetupForm(string mode, string[] preselect)
        {
            this.mode = mode; this.preselect = preselect;
            Text = "P15 驱动安装器";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(W, H);
            Icon = AppIcon.Get(0);

            var title = new Label2("P15 驱动安装器") { Font = Theme.Title, Bounds = new Rectangle(Pad, 12, 250, 28) };
            title.MouseDown += (s, e) => DragMove();
            MouseDown += (s, e) => DragMove();
            var close = new Btn("✕") { Bounds = new Rectangle(W - 48, 10, 34, 32) };
            close.Click += (s, e) => Close();
            Controls.Add(title); Controls.Add(close);

            var card = new Card { Bounds = new Rectangle(Pad, 52, W - Pad * 2, H - 52 - 140) };
            // 列表：原生滚动条放在可见区域外被裁掉，右侧用细滚动条
            var view = new GlassPanel { Bounds = new Rectangle(8, 8, card.Width - 16, card.Height - 16) };
            list = new GlassPanel { Bounds = new Rectangle(0, 0, view.Width + LiteScroll.Hidden, view.Height), AutoScroll = true };
            var sb = new LiteScroll { Bounds = new Rectangle(view.Width - 8, 0, 8, view.Height) };
            view.Controls.Add(list); view.Controls.Add(sb);
            card.Controls.Add(view); Controls.Add(card);
            wheel = new WheelRouter(() => sb);
            Application.AddMessageFilter(wheel);
            int y = 0;
            foreach (Tier t in Enum.GetValues(typeof(Tier)))
            {
                list.Controls.Add(new Label2(Catalog.TierName(t), true) { Font = Theme.H2, Bounds = new Rectangle(4, y + 4, view.Width - 30, 20) });
                y += 28;
                foreach (var d in Catalog.All.Where(x => x.Tier == t))
                {
                    var r = new SetupRow(d) { Bounds = new Rectangle(0, y, view.Width - 12, 64) };
                    rows.Add(r); list.Controls.Add(r); y += 66;
                }
                y += 4;
            }

            sb.Target = list;
            int bw = (W - Pad * 2 - 10) / 2;
            bSelMissing = new Btn("勾选缺失的必装/推荐") { Bounds = new Rectangle(Pad, H - 128, bw, 34) };
            bClean = new Btn("收尾清理") { Bounds = new Rectangle(Pad + bw + 10, H - 128, bw, 34) };
            bInstall = new Btn("安装勾选项", true) { Bounds = new Rectangle(Pad, H - 86, W - Pad * 2, 38) };
            status.Bounds = new Rectangle(Pad, H - 42, W - Pad * 2, 34);
            status.Align = ContentAlignment.TopLeft;
            Controls.AddRange(new Control[] { bSelMissing, bClean, bInstall, status });
            bSelMissing.Click += (s, e) => SelectMissing();
            bInstall.Click += async (s, e) => { if (working) cts.Cancel(); else await InstallChecked(); };
            bClean.Click += async (s, e) => await Cleanup();

            Engine.Log = msg => Ui(() => status.Text = msg);
        }

        WheelRouter wheel;
        protected override void OnFormClosed(FormClosedEventArgs e) { Application.RemoveMessageFilter(wheel); base.OnFormClosed(e); }

        void Ui(Action a) { if (IsDisposed) return; if (InvokeRequired) BeginInvoke(a); else a(); }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            await Detect();
            if (preselect != null && preselect.Length > 0)
            {
                foreach (var r in rows) r.Checked = preselect.Contains(r.Item.Id);
                Refresh();
                await InstallChecked();
            }
            else if (mode == "missing") { SelectMissing(); if (rows.Any(r => r.Checked)) await InstallChecked(); else status.Text = "必装和推荐的驱动都已安装"; }
            else if (mode == "cleanup") await Cleanup();
            else SelectMissing();
        }

        async Task Detect()
        {
            status.Text = "正在检测已安装的驱动…";
            var res = await Task.Run(() => StateFile.Merge(Detector.Detect()));
            foreach (var r in rows) { DriverState s; if (res.TryGetValue(r.Item.Id, out s)) r.State = s; r.Invalidate(); }
            // NVIDIA：对比官网最新版本
            var nv = rows.First(r => r.Item.Id == "nvidia");
            try
            {
                var rel = await Task.Run(() => Engine.NvidiaLatest());
                var mine = Engine.NvVersionFromWin(nv.State.Version);
                if (nv.State.Status == DrvStatus.Installed && mine != "" && string.CompareOrdinal(mine, rel.Version) < 0)
                {
                    nv.State.Status = DrvStatus.Outdated;
                    nv.State.Message = "当前 " + mine + "，最新 " + rel.Version;
                    StateFile.Save("nvidia", DrvStatus.Outdated, nv.State.Version, nv.State.Message);
                }
                nv.Item.Note = "最新 " + rel.Version + "（" + rel.Date + "）；只装驱动+PhysX，不装 NVIDIA App";
                nv.Invalidate();
            }
            catch { }
            status.Text = "检测完成";
        }

        void SelectMissing()
        {
            foreach (var r in rows)
                r.Checked = r.Item.Tier != Tier.Optional && r.State.Status != DrvStatus.Installed;
            foreach (var r in rows) r.Invalidate();
        }

        void SetBusy(bool b)
        {
            working = b;
            bInstall.Text = b ? "停止（当前项完成后）" : "安装勾选项";
            bClean.Enabled = bSelMissing.Enabled = !b;
            foreach (var r in rows) r.Enabled = !b;
        }

        async Task InstallChecked()
        {
            var todo = rows.Where(r => r.Checked).ToList();
            if (todo.Count == 0) { status.Text = "请先勾选要安装的项目"; return; }
            cts = new CancellationTokenSource();
            SetBusy(true);
            int ok = 0, fail = 0;
            foreach (var r in todo)
            {
                if (cts.IsCancellationRequested) break;
                var row = r;
                row.State.Status = DrvStatus.Installing; row.Phase = "准备中"; row.Progress = 0; row.Invalidate();
                list.ScrollControlIntoView(row);
                StateFile.Save(row.Item.Id, DrvStatus.Installing, "", "");
                string err = null;
                try { await Task.Run(() => Installer.Install(row.Item, (ph, pct) => Ui(() => { row.Phase = ph; row.Progress = pct; row.Invalidate(); }), cts.Token)); }
                catch (OperationCanceledException) { err = "已取消"; }
                catch (Exception ex) { err = ex.Message; }

                // 以实际检测结果为准
                var det = await Task.Run(() => Detector.Detect());
                DriverState s; det.TryGetValue(row.Item.Id, out s);
                bool installed = s != null && s.Status == DrvStatus.Installed;
                if (row.Item.Kind == PkgKind.NvidiaLatest && err == null) installed = true;
                if (err == null && installed) { ok++; row.State = s ?? new DriverState { Status = DrvStatus.Installed }; row.State.Status = DrvStatus.Installed; StateFile.Save(row.Item.Id, DrvStatus.Installed, row.State.Version, ""); }
                else
                {
                    fail++;
                    var msg = err ?? "安装程序已运行，但系统里没有检测到驱动生效（可能需要重启后再检测）";
                    row.State = new DriverState { Status = DrvStatus.Failed, Message = msg };
                    StateFile.Save(row.Item.Id, DrvStatus.Failed, "", msg);
                }
                row.Progress = -1; row.Checked = false; row.Invalidate();
            }
            SetBusy(false);
            status.Text = "完成：成功 " + ok + " 个，失败 " + fail + " 个。" + (fail > 0 ? "失败项可再次勾选重试。" : "") + (Engine.RebootNeeded ? "需要重启电脑。" : "");
            if (Dialog.Ask(this, "安装结束", "成功 " + ok + " 个，失败 " + fail + " 个。\n\n要现在做收尾清理吗？（删除安装包缓存和被取代的旧版本驱动）", "收尾清理", "以后再说"))
                await Cleanup();
            else if (Engine.RebootNeeded && Dialog.Ask(this, "需要重启", "部分驱动需要重启后才能完全生效，现在重启吗？", "立即重启", "稍后"))
                Reboot();
        }

        async Task Cleanup()
        {
            SetBusy(true); bInstall.Enabled = false;
            status.Text = "正在查找可清理的内容…";
            var olds = await Task.Run(() => DriverCleanup.FindOldDrivers());
            long cache = await Task.Run(() => DriverCleanup.PendingBytes());
            var msg = "将要清理：\n· 驱动安装包缓存和 NVIDIA 安装残留，约 " + (cache / 1048576) + " MB\n· 驱动库里被新版本取代的旧驱动包 " + olds.Count + " 个（正在使用的会自动跳过）";
            if (!Dialog.Ask(this, "收尾清理", msg, "开始清理"))
            { SetBusy(false); bInstall.Enabled = true; status.Text = "已取消清理"; return; }
            status.Text = "正在清理…";
            int del = 0; long freed = 0;
            await Task.Run(() => { del = DriverCleanup.DeleteOldDrivers(olds); freed = DriverCleanup.CleanFiles(); });
            var final = await Task.Run(() => StateFile.Merge(Detector.Detect()));
            foreach (var r in rows) { DriverState s; if (final.TryGetValue(r.Item.Id, out s)) r.State = s; r.Invalidate(); }
            SetBusy(false); bInstall.Enabled = true;
            status.Text = "清理完成：释放 " + (freed / 1048576) + " MB，删除旧驱动包 " + del + " 个" + (Engine.RebootNeeded ? "。部分驱动需要重启后生效" : "");
        }
        static void Reboot()
        {
            try { Process.Start(new ProcessStartInfo("shutdown.exe", "-r -t 5") { CreateNoWindow = true, UseShellExecute = false }); } catch { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (working && !Dialog.Ask(this, "正在安装", "正在安装驱动，中途关闭可能导致驱动不完整。确定要关闭吗？", "关闭", "继续安装"))
            { e.Cancel = true; return; }
            if (cts != null) cts.Cancel();
            base.OnFormClosing(e);
        }
    }
}
