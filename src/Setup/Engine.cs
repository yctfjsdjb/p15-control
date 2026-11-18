// 驱动下载 / 解压 / 静默安装 / 收尾清理
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace P15
{
    public static class Engine
    {
        public static Action<string> Log = s => { };
        static readonly StringBuilder report = new StringBuilder();
        public static string Report { get { return report.ToString(); } }
        static void L(string s) { report.AppendLine(DateTime.Now.ToString("HH:mm:ss ") + s); Log(s); }

        static Engine()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | (SecurityProtocolType)12288; // TLS1.2 / 1.3
            DriverCleanup.Log = L;   // 收尾清理的过程写进同一份日志
        }

        // ---------------- 下载（断点续传） ----------------
        public static string Download(string url, string fileName, Action<int> progress, CancellationToken ct)
        {
            Directory.CreateDirectory(StateFile.CacheDir);
            var path = Path.Combine(StateFile.CacheDir, fileName);
            string md5;
            long total = RemoteSize(url, out md5);
            if (File.Exists(path) && total > 0 && new FileInfo(path).Length == total && Md5Ok(path, md5)) { L("已有下载缓存：" + fileName); progress(100); return path; }
            var part = path + ".part";
            for (int attempt = 1; attempt <= 4; attempt++)
            {
                try
                {
                    long have = File.Exists(part) ? new FileInfo(part).Length : 0;
                    if (total > 0 && have > total) { File.Delete(part); have = 0; }
                    var req = NewRequest(url);
                    req.Timeout = 30000; req.ReadWriteTimeout = 60000;
                    if (have > 0) req.AddRange(have);
                    using (var resp = (HttpWebResponse)req.GetResponse())
                    {
                        if (have > 0 && resp.StatusCode != HttpStatusCode.PartialContent) have = 0;
                        if (total <= 0) total = have + resp.ContentLength;
                        using (var src = resp.GetResponseStream())
                        using (var dst = new FileStream(part, have > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write))
                        {
                            var buf = new byte[1 << 16]; int n; long done = have; int last = -1;
                            while ((n = src.Read(buf, 0, buf.Length)) > 0)
                            {
                                ct.ThrowIfCancellationRequested();
                                dst.Write(buf, 0, n); done += n;
                                int pct = total > 0 ? (int)(done * 100 / total) : 0;
                                if (pct != last) { last = pct; progress(pct); }
                            }
                        }
                    }
                    if (total > 0 && new FileInfo(part).Length != total) throw new IOException("文件大小不对，重新下载");
                    if (!Md5Ok(part, md5)) { File.Delete(part); throw new IOException("文件校验(MD5)不通过，重新下载"); }
                    if (File.Exists(path)) File.Delete(path);
                    File.Move(part, path);
                    L("下载完成：" + fileName);
                    return path;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    L("下载出错（第" + attempt + "次）：" + ex.Message);
                    if (attempt == 4) throw new Exception("下载失败：" + ex.Message);
                    Thread.Sleep(2000 * attempt);
                }
            }
            return path;
        }

        /// <summary>七彩虹下载服务器需要浏览器 UA 和来源页，否则会无限重定向</summary>
        static HttpWebRequest NewRequest(string url)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36";
            if (url.Contains("colorful.cn")) req.Referer = "https://www.colorful.cn/";
            req.AllowAutoRedirect = true; req.MaximumAutomaticRedirections = 10;
            return req;
        }

        static long RemoteSize(string url, out string md5)
        {
            md5 = null;
            try
            {
                var req = NewRequest(url);
                req.Method = "HEAD"; req.Timeout = 15000;
                using (var r = (HttpWebResponse)req.GetResponse()) { md5 = r.Headers["Content-MD5"]; return r.ContentLength; }
            }
            catch { return -1; }
        }

        static bool Md5Ok(string file, string md5Base64)
        {
            if (string.IsNullOrEmpty(md5Base64)) return true;
            using (var m = System.Security.Cryptography.MD5.Create())
            using (var f = File.OpenRead(file))
                return Convert.ToBase64String(m.ComputeHash(f)) == md5Base64;
        }

        // ---------------- 解压 ----------------
        /// <param name="only">只解出这部分（7-Zip 通配，例如 Graphics\*），null 表示全部</param>
        public static string Extract(string archive, string id, string only = null)
        {
            var dir = Path.Combine(StateFile.CacheDir, "x", id);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            // 1) 系统自带 tar（libarchive，支持 rar/zip/7z）
            int code = Run(Path.Combine(Environment.SystemDirectory, "tar.exe"), "-xf \"" + archive + "\" -C \"" + dir + "\"", 600000);
            if (code == 0 && Directory.EnumerateFileSystemEntries(dir).Any()) return dir;
            // 2) 7-Zip：tar 解不开的包（例如 Intel 核显驱动用了 256MB 字典的 RAR 自解压包）
            string z = SevenZip();
            Directory.Delete(dir, true); Directory.CreateDirectory(dir);
            code = Run(z, "x -y \"-o" + dir + "\" \"" + archive + "\"" + (only != null ? " \"" + only + "\"" : ""), 900000);
            if (code == 0 && Directory.EnumerateFileSystemEntries(dir).Any()) return dir;
            throw new Exception("解压失败（7-Zip 返回 " + code + "）");
        }

        // 7-Zip 官方安装包本身是 7z 自解压包：用系统 tar 直接取出 7z.exe 和 7z.dll，不运行安装程序、不装进系统
        const string SevenZipSetup = "https://www.7-zip.org/a/7z2606-x64.exe";

        /// <summary>已安装的 7-Zip；没有就临时取一份放在缓存目录（收尾清理时一并删除）</summary>
        static string SevenZip()
        {
            foreach (var z in new[] { @"C:\Program Files\7-Zip\7z.exe", @"C:\Program Files (x86)\7-Zip\7z.exe" })
                if (File.Exists(z)) return z;
            var dir = Path.Combine(StateFile.CacheDir, "7zip");
            var exe = Path.Combine(dir, "7z.exe");
            if (File.Exists(exe) && File.Exists(Path.Combine(dir, "7z.dll"))) return exe;
            L("系统自带的解压工具解不开这个包，临时获取 7-Zip 解压组件（不安装）");
            var setup = Download(SevenZipSetup, "7z-x64.exe", p => { }, CancellationToken.None);
            Directory.CreateDirectory(dir);
            int code = Run(Path.Combine(Environment.SystemDirectory, "tar.exe"), "-xf \"" + setup + "\" -C \"" + dir + "\" 7z.exe 7z.dll", 120000);
            if (!File.Exists(exe)) throw new Exception("获取 7-Zip 失败（tar 返回 " + code + "）");
            return exe;
        }

        // ---------------- 运行外部程序 ----------------
        public static int Run(string exe, string args, int timeoutMs, string workDir = null)
        {
            string ignored;
            return Run(exe, args, timeoutMs, out ignored, workDir);
        }

        public static int Run(string exe, string args, int timeoutMs, out string output, string workDir = null)
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = workDir ?? Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory,
                StandardOutputEncoding = Encoding.Default, StandardErrorEncoding = Encoding.Default
            };
            var sb = new StringBuilder();
            using (var p = new Process { StartInfo = psi })
            {
                p.OutputDataReceived += (s, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                p.Start(); p.BeginOutputReadLine(); p.BeginErrorReadLine();
                if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } output = sb.ToString(); return -999; }
                p.WaitForExit();
                output = sb.ToString();
                return p.ExitCode;
            }
        }

        // ---------------- 安装 ----------------
        /// <summary>INF 驱动：pnputil 添加并安装到匹配的设备</summary>
        public static bool InstallInf(string dir)
        {
            var infs = Directory.GetFiles(dir, "*.inf", SearchOption.AllDirectories);
            if (infs.Length == 0) throw new Exception("压缩包里没有找到驱动 INF 文件");
            string o;
            int code = Run(Path.Combine(Environment.SystemDirectory, "pnputil.exe"), "/add-driver \"" + Path.Combine(dir, "*.inf") + "\" /subdirs /install", 900000, out o);
            L("pnputil 返回 " + code);
            // 0 成功；3010 需要重启；259 = 没有更合适的设备需要更新（已是最新）
            return code == 0 || code == 3010 || code == 259;
        }

        public static bool InstallInfFile(string inf)
        {
            string o;
            int code = Run(Path.Combine(Environment.SystemDirectory, "pnputil.exe"), "/add-driver \"" + inf + "\" /install", 900000, out o);
            L("pnputil 返回 " + code + "：" + Path.GetFileName(inf));
            // 0 成功；3010 需要重启；259 = 没有需要更新的设备（例如核显当前被隐藏，驱动已放进驱动库）
            if (code == 3010) RebootNeeded = true;
            return code == 0 || code == 3010 || code == 259;
        }

        public static int RunInstaller(string exe, string args, int timeoutMs = 1800000)
        {
            L("运行：" + Path.GetFileName(exe) + " " + args);
            int code = Run(exe, args, timeoutMs, Path.GetDirectoryName(exe));
            L("返回码 " + code);
            return code;
        }

        public static bool RebootNeeded;

        public static bool Ok(int code)
        {
            if (code == 3010 || code == 1641) { RebootNeeded = true; return true; }
            return code == 0;
        }
    }
}
