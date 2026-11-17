// P15 CONTROL 安装 / 卸载程序（单个 exe）。
//   直接运行            → 安装或更新
//   --uninstall          → 卸载（「设置 → 应用」里点卸载时调用）
// 不依赖任何第三方安装工具；程序本体以压缩资源的形式内置。
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace P15
{
    static class SetupApp
    {
        public const string AppExe = "P15控制中心.exe";
        public const string UninstallExe = "Uninstall.exe";
        public const string RegKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\P15CONTROL";
        public const string TaskName = "P15ControlCenter";
        public static readonly string DefaultDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "P15 CONTROL");
        public static readonly string StartMenuLink = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), Brand.Name + ".lnk");
        public static readonly string DesktopLink = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), Brand.Name + ".lnk");

        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Theme.Level = 1;

            // 卸载：先把自己复制到临时目录再运行，这样安装目录（包括 Uninstall.exe 自己）才能被删掉
            int ui = Array.IndexOf(args, "--uninstall");
            if (ui >= 0)
            {
                var dir = InstalledDir() ?? Path.GetDirectoryName(Application.ExecutablePath);
                var self = Application.ExecutablePath;
                if (self.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
                {
                    var tmp = Path.Combine(Path.GetTempPath(), "P15CONTROL-uninstall.exe");
                    try
                    {
                        File.Copy(self, tmp, true);
                        Process.Start(new ProcessStartInfo(tmp, "--uninstall-from \"" + dir + "\"") { UseShellExecute = false });
                        return 0;
                    }
                    catch { }
                }
                Application.Run(new SetupForm(true, dir));
                return 0;
            }
            int uf = Array.IndexOf(args, "--uninstall-from");
            if (uf >= 0 && uf + 1 < args.Length) { Application.Run(new SetupForm(true, args[uf + 1])); return 0; }

            Application.Run(new SetupForm(false, InstalledDir() ?? DefaultDir));
            return 0;
        }

        static RegistryKey Hklm() { return RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64); }

        /// <summary>已安装的位置；没装过返回 null</summary>
        public static string InstalledDir()
        {
            try
            {
                using (var b = Hklm()) using (var k = b.OpenSubKey(RegKey))
                {
                    var d = k == null ? null : k.GetValue("InstallLocation") as string;
                    return !string.IsNullOrEmpty(d) && Directory.Exists(d) ? d : null;
                }
            }
            catch { return null; }
        }

        public static string InstalledVersion()
        {
            try { using (var b = Hklm()) using (var k = b.OpenSubKey(RegKey)) return k == null ? null : k.GetValue("DisplayVersion") as string; }
            catch { return null; }
        }

        /// <summary>原厂 Fn 热键服务（提供硬件接口）是否已安装</summary>
        public static bool OemPresent()
        {
            try
            {
                var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                if (File.Exists(Path.Combine(pf86, @"ControlCenter\DCHU\InsydeDCHU.dll"))) return true;
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages"))
                    return k != null && k.GetSubKeyNames().Any(n => n.StartsWith("CLEVOCO.Fnhotkeys", StringComparison.OrdinalIgnoreCase));
            }
            catch { return false; }
        }

        public static void Run(string exe, string args, int timeout = 20000)
        {
            try
            {
                using (var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true }))
                    p.WaitForExit(timeout);
            }
            catch { }
        }

        /// <summary>关掉正在运行的控制中心和驱动安装器（覆盖或删除文件前）</summary>
        public static void StopApp()
        {
            foreach (var n in new[] { "P15控制中心", "P15驱动安装器" })
                foreach (var p in Process.GetProcessesByName(n))
                    try { p.Kill(); p.WaitForExit(5000); } catch { }
            Thread.Sleep(300);
        }

        // ---------------- 安装 ----------------
        public static void Install(string dir, bool desktop, bool autostart, Action<string> say)
        {
            say("正在关闭正在运行的控制中心…");
            StopApp();
            Directory.CreateDirectory(dir);

            say("正在复制文件…");
            var exe = Path.Combine(dir, AppExe);
            using (var res = Assembly.GetExecutingAssembly().GetManifestResourceStream("P15.App.gz"))
            {
                if (res == null) throw new Exception("安装包不完整（缺少程序文件）");
                var tmp = exe + ".new";
                using (var z = new System.IO.Compression.GZipStream(res, System.IO.Compression.CompressionMode.Decompress))
                using (var f = File.Create(tmp)) z.CopyTo(f);
                if (File.Exists(exe)) File.Delete(exe);
                File.Move(tmp, exe);
            }
            // 硬件接口 InsydeDCHU.dll（原厂文件，安装包自带）
            using (var res = Assembly.GetExecutingAssembly().GetManifestResourceStream("P15.Dchu.gz"))
                if (res != null)
                {
                    var dll = Path.Combine(dir, "InsydeDCHU.dll");
                    using (var z = new System.IO.Compression.GZipStream(res, System.IO.Compression.CompressionMode.Decompress))
                    using (var f = File.Create(dll + ".new")) z.CopyTo(f);
                    if (File.Exists(dll)) File.Delete(dll);
                    File.Move(dll + ".new", dll);
                }
            // 旧版本释放出的组件和程序本体版本要一致：删掉，用到时控制中心会重新释放
            foreach (var old in new[] { "P15驱动安装器.exe", "P15Cooler.dll" })
                try { var p = Path.Combine(dir, old); if (File.Exists(p)) File.Delete(p); } catch { }
            var un = Path.Combine(dir, UninstallExe);
            if (!string.Equals(Application.ExecutablePath, un, StringComparison.OrdinalIgnoreCase)) File.Copy(Application.ExecutablePath, un, true);

            say("正在创建快捷方式…");
            Shortcut(StartMenuLink, exe, dir);
            if (desktop) Shortcut(DesktopLink, exe, dir); else TryDelete(DesktopLink);

            say("正在写入卸载信息…");
            long kb = 0;
            try { kb = new DirectoryInfo(dir).GetFiles().Sum(f => f.Length) / 1024; } catch { }
            using (var b = Hklm())
            using (var k = b.CreateSubKey(RegKey))
            {
                k.SetValue("DisplayName", Brand.Name);
                k.SetValue("DisplayVersion", Brand.Version);
                k.SetValue("Publisher", Brand.Name);
                k.SetValue("DisplayIcon", exe + ",0");
                k.SetValue("InstallLocation", dir);
                k.SetValue("UninstallString", "\"" + un + "\" --uninstall");
                k.SetValue("QuietUninstallString", "\"" + un + "\" --uninstall");
                k.SetValue("URLInfoAbout", Brand.Url);
                k.SetValue("EstimatedSize", (int)Math.Max(1, kb), RegistryValueKind.DWord);
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }

            say("正在设置开机自启…");
            if (autostart)
                Run("schtasks.exe", "/Create /TN " + TaskName + " /TR \"\\\"" + exe + "\\\" --tray\" /SC ONLOGON /RL HIGHEST /DELAY 0000:10 /F");
            else
                Run("schtasks.exe", "/Delete /TN " + TaskName + " /F");
        }

        // ---------------- 卸载 ----------------
        public static void Uninstall(string dir, bool removeData, Action<string> say)
        {
            say("正在关闭控制中心…");
            StopApp();
            say("正在删除开机自启…");
            Run("schtasks.exe", "/Delete /TN " + TaskName + " /F");
            say("正在删除快捷方式…");
            TryDelete(StartMenuLink); TryDelete(DesktopLink);
            TryDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Brand.Name + ".lnk"));
            say("正在删除程序文件…");
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                // 安全起见：只有安装器建的「P15 CONTROL」文件夹才整个删除；否则只删本程序的文件
                if (string.Equals(Path.GetFileName(dir.TrimEnd('\\')), "P15 CONTROL", StringComparison.OrdinalIgnoreCase))
                {
                    for (int i = 0; i < 5; i++) { try { Directory.Delete(dir, true); break; } catch { Thread.Sleep(600); } }
                }
                else
                {
                    foreach (var n in new[] { AppExe, UninstallExe, "P15驱动安装器.exe", "P15Cooler.dll", "InsydeDCHU.dll" }) TryDelete(Path.Combine(dir, n));
                    try { if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); } catch { }
                }
            }
            if (removeData)
            {
                say("正在删除设置和驱动缓存…");
                TryDeleteDir(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "P15Center"));
                TryDeleteDir(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "P15Center"));
                foreach (var desk in new[] { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory) })
                    TryDelete(Path.Combine(desk, "P15驱动安装报告.txt"));
            }
            say("正在删除卸载信息…");
            try { using (var b = Hklm()) b.DeleteSubKeyTree(RegKey, false); } catch { }
        }

        /// <summary>卸载程序从临时目录运行，结束后把自己也删掉</summary>
        public static void DeleteSelfLater()
        {
            var self = Application.ExecutablePath;
            if (!self.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)) return;
            try
            {
                Process.Start(new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 3 > nul & del /f /q \"" + self + "\"")
                { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
            }
            catch { }
        }

        static void TryDelete(string f) { try { if (File.Exists(f)) File.Delete(f); } catch { } }
        static void TryDeleteDir(string d) { try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch { } }

        /// <summary>用系统自带的 WScript.Shell 创建快捷方式</summary>
        static void Shortcut(string lnk, string target, string workDir)
        {
            object sh = null, link = null;
            try
            {
                var t = Type.GetTypeFromProgID("WScript.Shell");
                sh = Activator.CreateInstance(t);
                link = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { lnk });
                var lt = link.GetType();
                lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { target });
                lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, link, new object[] { workDir });
                lt.InvokeMember("IconLocation", BindingFlags.SetProperty, null, link, new object[] { target + ",0" });
                lt.InvokeMember("Description", BindingFlags.SetProperty, null, link, new object[] { Brand.Name });
                lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
            }
            catch { }
            finally
            {
                if (link != null) Marshal.FinalReleaseComObject(link);
                if (sh != null) Marshal.FinalReleaseComObject(sh);
            }
        }
    }

    /// <summary>安装 / 卸载窗口（和控制中心同样的黑色磨砂玻璃）</summary>
    sealed class SetupForm : GlassForm
    {
        const int W = 380;
        readonly bool uninstall;
        string dir;
        readonly Label2 status = new Label2("", true) { Align = ContentAlignment.TopLeft };
        readonly Btn go, cancel;
        Toggle tDesktop, tAuto, tRun, tData;
        Label2 pathLabel;
        bool busy, done;

        public SetupForm(bool uninstall, string dir)
        {
            this.uninstall = uninstall; this.dir = dir;
            Text = uninstall ? "卸载 " + Brand.Name : "安装 " + Brand.Name;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(W, uninstall ? 300 : 404);
            Icon = AppIcon.Get(0);
            ShowInTaskbar = true;
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape && !busy) Close(); };

            var mark = new MarkLabel { Bounds = new Rectangle(24, 22, W - 48, 32) };
            mark.MouseDown += (s, e) => DragMove();
            MouseDown += (s, e) => DragMove();
            Controls.Add(mark);
            var old = SetupApp.InstalledVersion();
            string sub = uninstall ? "将从这台电脑上删除 " + Brand.Name
                       : old == null ? "版本 " + Brand.Version + "，适用于七彩虹 隐星 P15（蓝天模具）"
                       : "已安装 " + old + "，将更新到 " + Brand.Version;
            Controls.Add(new Label2(sub, true) { Bounds = new Rectangle(24, 58, W - 48, 20) });

            int y = 96;
            if (!uninstall)
            {
                Controls.Add(new Label2("安装位置") { Bounds = new Rectangle(24, y, 120, 22) });
                pathLabel = new Label2(dir, true) { Bounds = new Rectangle(24, y + 22, W - 48 - 70, 20) };
                var change = new Btn("更改") { Bounds = new Rectangle(W - 24 - 60, y + 2, 60, 30) };
                change.Click += (s, e) =>
                {
                    using (var d = new FolderBrowserDialog { Description = "选择安装位置（会在里面建立 P15 CONTROL 文件夹）", SelectedPath = Path.GetDirectoryName(dir) })
                        if (d.ShowDialog(this) == DialogResult.OK) { dir = Path.Combine(d.SelectedPath, "P15 CONTROL"); pathLabel.Text = dir; }
                };
                Controls.Add(pathLabel); Controls.Add(change);
                y += 58;
                tDesktop = new Toggle { On = true }; tAuto = new Toggle { On = true }; tRun = new Toggle { On = true };
                Controls.Add(new SettingRow("桌面快捷方式", "", tDesktop, 34) { Bounds = new Rectangle(24, y, W - 48, 34) }); y += 40;
                Controls.Add(new SettingRow("开机自动启动", "在托盘运行，不弹权限确认", tAuto, 44) { Bounds = new Rectangle(24, y, W - 48, 44) }); y += 50;
                Controls.Add(new SettingRow("安装完成后运行", "", tRun, 34) { Bounds = new Rectangle(24, y, W - 48, 34) }); y += 44;
                if (!SetupApp.OemPresent())
                {
                    status.Text = "没检测到原厂底层服务：硬件功能需要它，装好后可在控制中心「驱动」页安装「原厂底层服务」";
                    status.Fore = Theme.P.Warn;
                }
            }
            else
            {
                tData = new Toggle { On = true };
                Controls.Add(new SettingRow("同时删除设置和驱动缓存", "包括散热器设置、驱动检测记录、下载的驱动安装包", tData, 48) { Bounds = new Rectangle(24, y, W - 48, 48) });
                y += 60;
                status.Text = "已经安装的驱动、BIOS 设置和原厂软件不受影响";
            }
            status.Bounds = new Rectangle(24, y, W - 48, 40);
            Controls.Add(status);

            int by = ClientSize.Height - 24 - 38;
            cancel = new Btn("取消") { Bounds = new Rectangle(24, by, 120, 38) };
            go = new Btn(uninstall ? "卸载" : (old == null ? "安装" : "更新"), true) { Bounds = new Rectangle(24 + 128, by, W - 48 - 128, 38) };
            cancel.Click += (s, e) => { if (!busy) Close(); };
            go.Click += async (s, e) => await Go();
            Controls.Add(cancel); Controls.Add(go);
        }

        void Say(string s, bool bad = false)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke(new Action(() => Say(s, bad))); return; }
            status.Fore = bad ? (Color?)Theme.P.Warn : null;
            status.Text = s;
        }

        async Task Go()
        {
            if (done) { Close(); return; }
            if (busy) return;
            busy = true; go.Enabled = cancel.Enabled = false;
            try
            {
                if (uninstall)
                {
                    bool data = tData.On;
                    await Task.Run(() => SetupApp.Uninstall(dir, data, m => Say(m)));
                    Say(Brand.Name + " 已卸载");
                }
                else
                {
                    bool desk = tDesktop.On, auto = tAuto.On;
                    await Task.Run(() => SetupApp.Install(dir, desk, auto, m => Say(m)));
                    Say("安装完成");
                    if (tRun.On)
                        try { Process.Start(new ProcessStartInfo(Path.Combine(dir, SetupApp.AppExe)) { UseShellExecute = true, WorkingDirectory = dir }); } catch { }
                }
                done = true;
                go.Text = "完成"; go.Enabled = true; cancel.Visible = false;
            }
            catch (Exception ex)
            {
                Say((uninstall ? "卸载失败：" : "安装失败：") + ex.Message, true);
                go.Enabled = cancel.Enabled = true;
            }
            finally { busy = false; }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (busy) { e.Cancel = true; return; }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            if (uninstall) SetupApp.DeleteSelfLater();
        }

        /// <summary>闪电 + 字标</summary>
        sealed class MarkLabel : Owner
        {
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics; var p = Theme.P;
                Back(g);
                Brand.DrawBolt(g, new RectangleF(0, 4, 15, 23), p.Text);
                Theme.Text(g, Brand.Name, Theme.Title, p.Text, new RectangleF(24, 0, Width - 24, Height));
            }
        }
    }
}
