// 可选插件接口与加载：散热器插件只在用户选择安装后才释放到程序目录并加载。
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace P15
{
    public interface ICoolerModule
    {
        /// <summary>启动后台引擎（不建任何界面）</summary>
        void Start();
        /// <summary>为当前打开的窗口创建一张散热器卡片；窗口关闭时卡片随之销毁，引擎继续运行</summary>
        Control CreateCard(int width, Action<string, bool> say);
        void Shutdown();
    }

    public static class CoolerPlugin
    {
        public static string DllPath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "P15Cooler.dll"); } }

        /// <summary>用户是否选择了安装散热器功能</summary>
        public static bool Wanted
        {
            get { return Settings.GetBool("coolerInstalled", false); }
            set { Settings.SetBool("coolerInstalled", value); Settings.SetBool("coolerAsked", true); }
        }

        public static bool Asked { get { return Settings.GetBool("coolerAsked", false); } }

        /// <summary>从控制中心内置的压缩包释放插件</summary>
        public static bool Extract()
        {
            using (var res = typeof(CoolerPlugin).Assembly.GetManifestResourceStream("P15.Cooler.gz"))
            {
                if (res == null) return false;
                var tmp = DllPath + ".tmp";
                using (var z = new System.IO.Compression.GZipStream(res, System.IO.Compression.CompressionMode.Decompress))
                using (var f = File.Create(tmp)) z.CopyTo(f);
                if (File.Exists(DllPath)) File.Delete(DllPath);
                File.Move(tmp, DllPath);
                return true;
            }
        }

        public static ICoolerModule Load()
        {
            // 插件和控制中心版本不一致（控制中心更新过）时重新释放
            if (File.Exists(DllPath) && File.GetLastWriteTimeUtc(DllPath) < File.GetLastWriteTimeUtc(Application.ExecutablePath))
                try { Extract(); } catch { }
            if (!File.Exists(DllPath) && !Extract()) return null;
            var asm = Assembly.LoadFrom(DllPath);
            var t = asm.GetTypes().First(x => typeof(ICoolerModule).IsAssignableFrom(x) && !x.IsInterface);
            return (ICoolerModule)Activator.CreateInstance(t);
        }

        /// <summary>启动时清理：没选安装却留有插件文件（上次卸载时还在使用），删掉它</summary>
        public static void CleanupIfRemoved()
        {
            if (Wanted) return;
            foreach (var f in new[] { DllPath, DllPath + ".tmp" })
                try { if (File.Exists(f)) File.Delete(f); } catch { }
        }
    }
}
