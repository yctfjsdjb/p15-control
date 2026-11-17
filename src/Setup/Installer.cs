// 每个驱动包的静默安装方式（根据官网包的实际结构确定）
using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace P15
{
    public static class Installer
    {
        static string FindFile(string dir, string name)
        {
            var f = Directory.GetFiles(dir, name, SearchOption.AllDirectories).OrderBy(p => p.Length).FirstOrDefault();
            if (f == null) throw new Exception("安装包里没有找到 " + name);
            return f;
        }

        static string FindDir(string dir, string relative)
        {
            var hit = Directory.GetDirectories(dir, "*", SearchOption.AllDirectories)
                .FirstOrDefault(d => d.Replace('\\', '/').EndsWith(relative, StringComparison.OrdinalIgnoreCase));
            if (hit == null) throw new Exception("安装包里没有找到目录 " + relative);
            return hit;
        }

        static void Exe(string exe, string args)
        {
            int code = Engine.RunInstaller(exe, args);
            if (code == -999) throw new Exception("安装超时");
            if (!Engine.Ok(code)) throw new Exception("安装程序返回错误码 " + code);
        }

        /// <summary>只装一个 INF（不连带同目录的其它组件 INF）</summary>
        static void InfFile(string inf)
        {
            if (!Engine.InstallInfFile(inf)) throw new Exception("pnputil 安装驱动失败");
        }

        static void Inf(string dir)
        {
            if (!Engine.InstallInf(dir)) throw new Exception("pnputil 安装驱动失败");
        }

        public static void Install(DriverItem d, Action<string, int> progress, CancellationToken ct)
        {
            if (d.Kind == PkgKind.NvidiaLatest) { InstallNvidia(progress, ct); return; }

            var file = Path.GetFileName(new Uri(d.Url).LocalPath);
            progress("下载中", 0);
            var archive = Engine.Download(d.Url, file, p => progress("下载 " + p + "%", p), ct);
            ct.ThrowIfCancellationRequested();
            progress("解压中", 100);
            // Intel 核显包只需要 Graphics 目录（驱动本体），不解出 Intel 安装程序
            var dir = Engine.Extract(archive, d.Id, d.Id == "igfx" ? @"Graphics\*" : null);
            ct.ThrowIfCancellationRequested();
            progress("安装中", 100);

            switch (d.Id)
            {
                case "chipset": Exe(FindFile(dir, "SetupChipset.exe"), "-s -norestart"); break;
                case "serialio": Exe(FindFile(dir, "SetupSerialIO.exe"), "-s -norestart"); break;
                case "csme": Exe(FindFile(dir, "SetupME.exe"), "-s -norestart"); break;
                case "wlan": Inf(FindDir(dir, "Win64/Drivers")); break;
                case "bt": Inf(FindDir(dir, "INF_INSTALL/Win10_UWD/x64")); break;
                case "gna": Inf(Path.GetDirectoryName(FindFile(dir, "gna.inf"))); break;
                case "hid": Inf(FindDir(dir, "drivers/x64")); break;
                // Intel 核显：只装显示驱动本体 iigd_dch.inf，不运行 Intel 安装程序，所以不会装 Intel 显卡控制中心 / Graphics Software。
                // 独显直连时核显被隐藏，驱动先放进驱动库，切换到混合模式后系统自动使用。
                case "igfx": InfFile(FindFile(dir, "iigd_dch.inf")); break;
                // InstallShield 包：自带 setup.iss 应答文件，-s 静默
                case "lan": Exe(FindFile(dir, "setup.exe"), "-s"); break;
                case "audio": Exe(FindFile(dir, "Setup.exe"), "-s"); break;
                case "oem": Exe(FindFile(dir, "setup.exe"), "-s"); break;
                case "dtt":
                    Exe(FindFile(dir, "Ipf_*_Install.exe"), "-s");
                    Exe(FindFile(dir, "IpfDtt_*_Install.exe"), "-s");
                    break;
                case "sbc":
                    // 原厂批处理会强制重启，这里只运行其中的两个安装程序
                    var v = FindDir(dir, "V26");
                    Exe(Directory.GetFiles(v, "SpeakerEQClevo_*.exe").First(), "");
                    Exe(Directory.GetFiles(v, "CreativeAudioEffectsComponentInstaller_*.exe").First(), "/silent");
                    Engine.RebootNeeded = true;
                    break;
                case "creative":
                    // 原厂软件不支持静默安装，弹出安装界面由用户操作
                    Exe(FindFile(dir, "CreativeAppSetup_*.exe"), "");
                    break;
                default: throw new Exception("未定义安装方式");
            }
        }

        static void InstallNvidia(Action<string, int> progress, CancellationToken ct)
        {
            progress("查询最新版", 0);
            var rel = Engine.NvidiaLatest();
            Engine.Log("NVIDIA 最新驱动 " + rel.Version);
            var file = Path.GetFileName(new Uri(rel.Url).LocalPath);
            var exe = Engine.Download(rel.Url, file, p => progress("下载 " + p + "%", p), ct);
            ct.ThrowIfCancellationRequested();
            progress("解压中", 100);
            string dir = null;
            try { dir = Engine.Extract(exe, "nvidia"); } catch (Exception ex) { Engine.Log("解压 NVIDIA 安装包失败，改用整包静默安装：" + ex.Message); }
            progress("安装中（屏幕会闪烁）", 100);
            if (dir != null && File.Exists(Path.Combine(dir, "setup.exe")))
            {
                // 只装 显示驱动 + HD 音频 + PhysX，不装 NVIDIA App / GeForce Experience
                int code = Engine.RunInstaller(Path.Combine(dir, "setup.exe"), "-s -noreboot -noeula -nofinish -passive Display.Driver HDAudio.Driver PhysX");
                if (Engine.Ok(code)) return;
                Engine.Log("按组件安装返回 " + code + "，改用默认组件安装");
                code = Engine.RunInstaller(Path.Combine(dir, "setup.exe"), "-s -noreboot -noeula");
                if (!Engine.Ok(code)) throw new Exception("NVIDIA 安装程序返回错误码 " + code);
                return;
            }
            int c2 = Engine.RunInstaller(exe, "-s -noreboot -noeula");
            if (!Engine.Ok(c2)) throw new Exception("NVIDIA 安装程序返回错误码 " + c2);
        }
    }
}
