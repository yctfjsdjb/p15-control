using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace P15
{
    /// <summary>用户设置：%APPDATA%\P15Center\settings.ini</summary>
    public static class Settings
    {
        static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "P15Center");
        static readonly string FilePath = Path.Combine(Dir, "settings.ini");
        static readonly Dictionary<string, string> Map = new Dictionary<string, string>();

        static Settings()
        {
            try
            {
                if (File.Exists(FilePath))
                    foreach (var l in File.ReadAllLines(FilePath, Encoding.UTF8))
                    {
                        int i = l.IndexOf('=');
                        if (i > 0) Map[l.Substring(0, i)] = l.Substring(i + 1);
                    }
            }
            catch { }
        }

        static readonly object Gate = new object();

        public static string Get(string k, string def = "") { lock (Gate) { string v; return Map.TryGetValue(k, out v) ? v : def; } }
        public static bool GetBool(string k, bool def) { var v = Get(k, null); return v == null ? def : v == "1"; }

        public static void Set(string k, string v)
        {
            lock (Gate)
            {
                string old;
                if (Map.TryGetValue(k, out old) && old == v) return;   // 没变就不写盘
                Map[k] = v;
                Save();
            }
        }

        /// <summary>先写临时文件再替换：写到一半断电也不会把设置文件写坏</summary>
        static void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var sb = new StringBuilder();
                foreach (var kv in Map) sb.Append(kv.Key).Append('=').Append(kv.Value).Append("\r\n");
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, sb.ToString(), Encoding.UTF8);
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null); else File.Move(tmp, FilePath);
            }
            catch { }
        }
        public static void SetBool(string k, bool v) { Set(k, v ? "1" : "0"); }

        /// <summary>删除某一类设置（如卸载散热器时删掉 bs2*）</summary>
        public static void RemovePrefix(string prefix)
        {
            lock (Gate)
            {
                foreach (var k in new List<string>(Map.Keys)) if (k.StartsWith(prefix)) Map.Remove(k);
                Save();
            }
        }
    }

    /// <summary>开机自启：计划任务（最高权限，登录时运行，免 UAC 弹窗）</summary>
    public static class AutoStart
    {
        const string Task = "P15ControlCenter";

        static int Run(string args, out string output)
        {
            var psi = new ProcessStartInfo("schtasks.exe", args)
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
              StandardOutputEncoding = Encoding.Default, StandardErrorEncoding = Encoding.Default };
            using (var p = Process.Start(psi))
            {
                output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit(10000);
                return p.ExitCode;
            }
        }

        public static bool Enabled
        {
            get { try { string o; return Run("/Query /TN " + Task, out o) == 0; } catch { return false; } }
        }

        /// <summary>程序被移动到别的文件夹后，开机自启任务里的路径会失效：发现不一致就按当前位置重建</summary>
        public static void Repair()
        {
            try
            {
                string o;
                if (Run("/Query /TN " + Task + " /XML", out o) != 0) return;
                var exe = Process.GetCurrentProcess().MainModule.FileName;
                if (o.IndexOf(exe, StringComparison.OrdinalIgnoreCase) < 0) Set(true);
            }
            catch { }
        }

        public static bool Set(bool on)
        {
            try
            {
                string o;
                if (!on) return Run("/Delete /TN " + Task + " /F", out o) == 0;
                var exe = Process.GetCurrentProcess().MainModule.FileName;
                return Run("/Create /TN " + Task + " /TR \"\\\"" + exe + "\\\" --tray\" /SC ONLOGON /RL HIGHEST /DELAY 0000:10 /F", out o) == 0;
            }
            catch { return false; }
        }
    }
}
