// 驱动收尾清理：控制中心「驱动」页和驱动安装器共用。不生成报告。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace P15
{
    public static class DriverCleanup
    {
        public static Action<string> Log = s => { };

        public sealed class OldDriver { public string Published, Original, Provider, Version; }

        static readonly string[] Dirs =
        {
            StateFile.CacheDir   // 只清理本程序下载的安装包缓存；NVIDIA 驱动由用户自己装，不动它的文件
        };

        static int Pnputil(string args, out string output)
        {
            var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "pnputil.exe"), args)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.Default, StandardErrorEncoding = Encoding.Default
            };
            var sb = new StringBuilder();
            using (var p = new Process { StartInfo = psi })
            {
                p.OutputDataReceived += (s, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                p.Start(); p.BeginOutputReadLine(); p.BeginErrorReadLine();
                if (!p.WaitForExit(60000)) { try { p.Kill(); } catch { } output = sb.ToString(); return -999; }
                p.WaitForExit();
                output = sb.ToString();
                return p.ExitCode;
            }
        }

        /// <summary>找出驱动库中同名驱动的旧版本（每组保留最新的一个）</summary>
        public static List<OldDriver> FindOldDrivers()
        {
            string o;
            Pnputil("/enum-drivers", out o);
            var list = new List<OldDriver>();
            OldDriver cur = null;
            foreach (var raw in o.Split('\n'))
            {
                var line = raw.Trim();
                int i = line.IndexOf(':'); if (i < 0) i = line.IndexOf('：');
                if (i < 0) { if (line.Length == 0 && cur != null) { list.Add(cur); cur = null; } continue; }
                var val = line.Substring(i + 1).Trim();
                if (Regex.IsMatch(val, @"^oem\d+\.inf$", RegexOptions.IgnoreCase)) { if (cur != null) list.Add(cur); cur = new OldDriver { Published = val }; continue; }
                if (cur == null) continue;
                if (cur.Original == null && Regex.IsMatch(val, @"\.inf$", RegexOptions.IgnoreCase)) cur.Original = val.ToLowerInvariant();
                else if (cur.Provider == null && i > 0 && !val.Contains("{") && !Regex.IsMatch(val, @"^\d")) cur.Provider = val;
                var vm = Regex.Match(val, @"(\d+\.\d+\.\d+\.\d+)\s*$");
                if (vm.Success) cur.Version = vm.Groups[1].Value;
            }
            if (cur != null) list.Add(cur);
            var old = new List<OldDriver>();
            // 版本号解析不了的包（个别厂商写法特殊）直接跳过，不能让一个包导致整次清理失败
            Version ignored;
            foreach (var g in list.Where(d => d.Original != null && d.Version != null && Version.TryParse(d.Version, out ignored)).GroupBy(d => d.Original))
                old.AddRange(g.OrderByDescending(d => Version.Parse(d.Version)).Skip(1));
            return old;
        }

        /// <summary>删除旧驱动包（不加 /force：正在被设备使用的包 Windows 会拒绝删除，确保安全）</summary>
        public static int DeleteOldDrivers(IEnumerable<OldDriver> olds)
        {
            int n = 0;
            foreach (var d in olds)
            {
                string o;
                if (Pnputil("/delete-driver " + d.Published, out o) == 0) { n++; Log("已删除旧驱动包 " + d.Published + " (" + d.Original + " " + d.Version + ")"); }
                else Log("跳过 " + d.Published + "（正在使用或系统拒绝）");
            }
            return n;
        }

        /// <summary>可清理的文件大小（驱动安装包缓存）</summary>
        public static long PendingBytes()
        {
            long n = 0;
            foreach (var dir in Dirs)
                try { if (Directory.Exists(dir)) n += Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Sum(f => { try { return new FileInfo(f).Length; } catch { return 0L; } }); }
                catch { }
            return n;
        }

        public static long CleanFiles()
        {
            long freed = 0;
            foreach (var dir in Dirs)
            {
                try
                {
                    if (!Directory.Exists(dir)) continue;
                    freed += Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Sum(f => { try { return new FileInfo(f).Length; } catch { return 0L; } });
                    Directory.Delete(dir, true);
                    Log("已清理 " + dir);
                }
                catch (Exception ex) { Log("清理 " + dir + " 时部分文件被占用：" + ex.Message); }
            }
            return freed;
        }
    }
}
