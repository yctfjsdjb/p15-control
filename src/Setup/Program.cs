using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace P15
{
    static class Program
    {
        // 参数：
        //   --install-missing        自动勾选缺失的必装/推荐项并开始安装
        //   --install id1,id2        安装指定项目
        //   --cleanup                直接进入收尾清理
        [STAThread]
        static void Main(string[] args)
        {
            bool created;
            using (new Mutex(true, "Global\\P15DriverSetup_Single", out created))
            {
                if (!created) { MessageBox.Show("驱动安装器已经在运行。", "P15 驱动安装器"); return; }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                string mode = "";
                string[] ids = null;
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "--install-missing") mode = "missing";
                    else if (args[i] == "--cleanup") mode = "cleanup";
                    else if (args[i] == "--install" && i + 1 < args.Length) ids = args[++i].Split(',').Select(s => s.Trim()).ToArray();
                }
                Application.Run(new SetupForm(mode, ids));
            }
        }

    }
}
