// 后台托盘宿主：后台只保留 托盘图标 + 散热器引擎（如已安装）+ 蓝光滤镜恢复。
// 主窗口只在打开时创建，关到托盘时整个销毁，界面占用的内存和句柄全部释放。
using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace P15
{
    public sealed class TrayHost : ApplicationContext
    {
        readonly Control ui = new Control();   // 用于从其它线程切回界面线程
        readonly NotifyIcon tray;
        readonly ToolStripMenuItem[] modeItems;
        MainForm form;
        System.Threading.Timer trimOnce;
        public ICoolerModule Cooler { get; private set; }
        public bool Exiting { get; private set; }
        public event Action PowerChanged;

        [DllImport("psapi.dll")] static extern bool EmptyWorkingSet(IntPtr h);

        public TrayHost(bool showWindow)
        {
            ui.CreateControl();
            int glassLv; Theme.Level = int.TryParse(Settings.Get("glass", "1"), out glassLv) ? Math.Max(0, Math.Min(2, glassLv)) : 0;

            var menu = new ContextMenuStrip();
            var modes = new[] { PowerMode.Quiet, PowerMode.PowerSaving, PowerMode.Performance, PowerMode.Entertainment };
            string[] names = { "安静模式", "省电模式", "性能模式", "娱乐模式" };
            modeItems = modes.Select((m, i) =>
            {
                var it = new ToolStripMenuItem(names[i]);
                it.Click += (s, e) => SetPowerFromTray(m);
                return it;
            }).ToArray();
            menu.Items.AddRange(modeItems);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("打开 " + Brand.Name, null, (s, e) => ShowMain());
            menu.Items.Add("退出", null, (s, e) => Exit());
            menu.Opening += (s, e) => SyncTray(Dchu.GetPowerMode());
            GlassMenuRenderer.Style(menu);
            tray = new NotifyIcon { Icon = AppIcon.Tray(true), Text = Brand.Name, ContextMenuStrip = menu, Visible = true };
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowMain(); };
            SyncTray(Dchu.GetPowerMode());

            // 蓝光滤镜：启动和显示器变化时恢复（与原厂一致，级别保存在 BIOS APP 数据里）
            ApplyBlueLight();
            SystemEvents.DisplaySettingsChanged += OnDisplayChanged;

            // 散热器插件：之前已经在用的用户视为已安装
            if (!CoolerPlugin.Asked && Settings.Get("bs2On", "") != "") CoolerPlugin.Wanted = true;
            if (CoolerPlugin.Wanted) LoadCooler();

            Task.Run(() => AutoStart.Repair());   // 程序换了位置时修正开机自启路径
            if (showWindow) ShowMain(); else Trim();
            // 开机时散热器连接、蓝牙组件加载都在启动后几秒内完成，20 秒后再回收一次内存（一次性，之后不再运行）
            trimOnce = new System.Threading.Timer(_ => { if (form == null && !Exiting) Trim(); trimOnce.Dispose(); }, null, 20000, System.Threading.Timeout.Infinite);
        }

        void OnDisplayChanged(object s, EventArgs e) { ApplyBlueLight(); }
        static void ApplyBlueLight() { try { int bl = Dchu.GetBlueLight(); if (bl > 0) Dchu.ApplyBlueLight(bl); } catch { } }

        /// <summary>加载散热器插件并启动后台引擎；返回错误信息，成功返回 null</summary>
        public string LoadCooler()
        {
            if (Cooler != null) return null;
            try { Cooler = CoolerPlugin.Load(); }
            catch (Exception ex) { return "加载散热器功能失败：" + ex.Message; }
            if (Cooler == null) return "控制中心里没有散热器插件";
            Cooler.Start();
            return null;
        }

        public void ShowMain()
        {
            if (ui.InvokeRequired) { ui.BeginInvoke(new Action(ShowMain)); return; }
            if (Exiting) return;
            if (form == null || form.IsDisposed)
            {
                form = new MainForm(this);
                form.FormClosed += (s, e) => { form = null; if (!Exiting) Trim(); };
            }
            form.Show();
            if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
            form.Activate();
        }

        public void SyncTray(PowerMode pm)
        {
            for (int i = 0; i < modeItems.Length; i++) modeItems[i].Checked = (int)pm == i;
            // 托盘图标：性能模式为实心闪电，其它模式为描边闪电（单色，跟随任务栏深浅色）
            var ic = AppIcon.Tray(pm == PowerMode.Performance || pm == PowerMode.Unknown);
            if (tray != null && tray.Icon != ic) tray.Icon = ic;
        }

        async void SetPowerFromTray(PowerMode m)
        {
            bool ok = await Task.Run(() => Dchu.SetPowerMode(m));
            SyncTray(Dchu.GetPowerMode());
            var h = PowerChanged; if (h != null) h();
            if (!ok) tray.ShowBalloonTip(3000, Brand.Name, "切换失败：" + Dchu.LastError, ToolTipIcon.Warning);
        }

        public void NotifyPowerChanged(PowerMode pm) { SyncTray(pm); }

        public void Exit()
        {
            if (Exiting) return;
            Exiting = true;
            tray.Visible = false;
            SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            try { if (Cooler != null) Cooler.Shutdown(); } catch { }
            if (form != null && !form.IsDisposed) form.Close();
            tray.Dispose();
            ExitThread();
        }

        public void ExitLater() { ui.BeginInvoke(new Action(Exit)); }

        /// <summary>重启控制中心（卸载插件后用）</summary>
        public void Restart()
        {
            try { Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--restart") { UseShellExecute = true }); } catch { }
            Exit();
        }

        /// <summary>窗口关闭后回收内存，后台只剩托盘</summary>
        static void Trim()
        {
            System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            try { EmptyWorkingSet(Process.GetCurrentProcess().Handle); } catch { }
        }
    }
}
