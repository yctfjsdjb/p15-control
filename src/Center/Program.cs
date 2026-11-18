using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace P15
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool created = false;
            Mutex mutex = null;
            // --restart：等旧进程退出（卸载插件后自动重启控制中心时用）
            for (int i = 0; i < (args.Contains("--restart") ? 40 : 1); i++)
            {
                if (mutex != null) mutex.Dispose();
                mutex = new Mutex(true, "Global\\P15ControlCenter_Single", out created);
                if (created) break;
                Thread.Sleep(200);
            }
            CoolerPlugin.CleanupIfRemoved();   // 散热器插件已卸载但上次文件被占用：现在删掉
            using (mutex)
            using (var show = new EventWaitHandle(false, EventResetMode.AutoReset, "Global\\P15ControlCenter_Show"))
            using (var quit = new EventWaitHandle(false, EventResetMode.AutoReset, "Global\\P15ControlCenter_Exit"))
            {
                if (!created) { show.Set(); return; }   // 已在运行：唤出已有窗口

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += (s, e) => Log(e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Log(e.ExceptionObject as Exception);

                // 后台只运行托盘宿主；--tray（开机自启）时不建窗口
                var host = new TrayHost(!args.Contains("--tray"));
                // 等待「唤出窗口」信号：用线程池等待，不单独占一个线程
                ThreadPool.RegisterWaitForSingleObject(show, (o, timedOut) => host.ShowMain(), null, -1, false);
                // 安装包更新 / 卸载前请程序自己退出：散热器先交还按键控制，托盘图标也会正常消失（不用强制结束）
                ThreadPool.RegisterWaitForSingleObject(quit, (o, timedOut) => host.ExitLater(), null, -1, true);
                Application.Run(host);
            }
        }

        static void Log(Exception ex)
        {
            try
            {
                var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "P15Center");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "error.log"), DateTime.Now + " " + ex + "\r\n");
            }
            catch { }
        }
    }
}
