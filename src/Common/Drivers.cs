// 驱动清单、安装状态检测、两个程序共享的状态文件
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace P15
{
    public enum Tier { Required = 0, Recommended = 1, Optional = 2 }
    public enum DrvStatus { Unknown, Installed, NotInstalled, Failed, Installing, Outdated }
    public enum PkgKind { OemArchive, NvidiaLatest }

    public sealed class DriverItem
    {
        public string Id, Name, Note, Url, Source;
        public Tier Tier;
        public PkgKind Kind = PkgKind.OemArchive;
        public bool Background;       // 是否有常驻后台程序/服务
        public int SizeMB;
        public string[] HwIds = new string[0];   // 设备 ID 正则（任一匹配且驱动非微软通用即算已安装）
        public string ProviderRx;               // 可选：驱动提供商正则
        public string AppRx;                    // 可选：已安装程序名正则（软件类）
        public string StoreInf;                 // 可选：设备被隐藏时（如独显直连下的核显），驱动库里有这个 INF 就算已安装
    }

    public sealed class DriverState
    {
        public DrvStatus Status = DrvStatus.Unknown;
        public string Version = "", Message = "";
        public DateTime Time;
    }

    public static class Catalog
    {
        const string Oem = "https://download.colorful.cn/EnDownload/OneBot/2023/YX/";

        public static readonly DriverItem[] All =
        {
            // ---------- 必装 ----------
            new DriverItem { Id="chipset", Name="芯片组驱动", Tier=Tier.Required, Url=Oem+"01_Chipset.rar", Source="原厂", SizeMB=5,
                Note="主板各控制器识别", HwIds=new[]{@"PCI\\VEN_8086&DEV_5182"}, ProviderRx="(?i)intel" },
            new DriverItem { Id="oem", Name="原厂底层服务 (Fn热键/ACPI)", Tier=Tier.Required, Url=Oem+"07_CC30.rar", Source="原厂", SizeMB=164, Background=true,
                Note="Fn组合键、性能模式、显卡切换都依赖它；会附带原厂控制中心界面，可不用", HwIds=new[]{@"ACPI\\CLV0001", @"ACPI\\CLV0002"}, ProviderRx="(?i)insyde|clevo" },
            new DriverItem { Id="wlan", Name="无线网卡 (AX211)", Tier=Tier.Required, Url=Oem+"01_WLAN.rar", Source="原厂", SizeMB=60,
                Note="Wi-Fi 6E", HwIds=new[]{@"PCI\\VEN_8086&DEV_51F0"}, ProviderRx="(?i)intel" },
            new DriverItem { Id="bt", Name="蓝牙", Tier=Tier.Required, Url=Oem+"02_BT.rar", Source="原厂", SizeMB=23,
                Note="Intel 无线蓝牙", HwIds=new[]{@"USB\\VID_8087&PID_0033"}, ProviderRx="(?i)intel" },
            new DriverItem { Id="lan", Name="有线网卡 (Realtek)", Tier=Tier.Required, Url=Oem+"04_LAN.rar", Source="原厂", SizeMB=11,
                Note="RTL8168 千兆网口", HwIds=new[]{@"PCI\\VEN_10EC&DEV_8168"}, ProviderRx="(?i)realtek" },
            new DriverItem { Id="audio", Name="音频驱动 (Realtek/智音)", Tier=Tier.Required, Url=Oem+"11_Audio.rar", Source="原厂", SizeMB=171,
                Note="扬声器/耳机/麦克风", HwIds=new[]{@"INTELAUDIO\\FUNC_01&VEN_10EC"}, ProviderRx="(?i)realtek|intel" },
            new DriverItem { Id="serialio", Name="英特尔串行 IO", Tier=Tier.Required, Url=Oem+"02_SerialIO.rar", Source="原厂", SizeMB=3,
                Note="触控板 I2C、GPIO", HwIds=new[]{@"PCI\\VEN_8086&DEV_51E8", @"ACPI\\INTC1055"}, ProviderRx="(?i)intel" },
            // ---------- 推荐 ----------
            new DriverItem { Id="nvidia", Name="NVIDIA 显卡驱动 (最新版)", Tier=Tier.Recommended, Kind=PkgKind.NvidiaLatest, Source="NVIDIA 官方最新", SizeMB=650,
                Note="不用原厂 2023 老版本；只装驱动+PhysX，不装 NVIDIA App", HwIds=new[]{@"PCI\\VEN_10DE&DEV_28A1"}, ProviderRx="(?i)nvidia" },
            new DriverItem { Id="igfx", Name="英特尔核显驱动 (UHD)", Tier=Tier.Recommended,
                Url="https://downloadmirror.intel.com/929468/gfx_win_101.7092.exe", Source="Intel 官方", SizeMB=745,
                Note="32.0.101.7092，只装驱动本体，不装 Intel 显卡控制中心；混合模式下才会用到核显", HwIds=new[]{@"PCI\\VEN_8086&DEV_46[A-D]"}, ProviderRx="(?i)intel", StoreInf="iigd_dch.inf" },
            new DriverItem { Id="dtt", Name="英特尔动态调谐 DTT", Tier=Tier.Recommended, Url=Oem+"08_DTT.rar", Source="原厂", SizeMB=33, Background=true,
                Note="参与 CPU 功耗/温度调度，影响性能释放", HwIds=new[]{@"ACPI\\INTC1041"}, ProviderRx="(?i)intel" },
            new DriverItem { Id="hid", Name="英特尔 HID 事件筛选器", Tier=Tier.Recommended, Url=Oem+"05_HIDfilter.rar", Source="原厂", SizeMB=1,
                Note="部分热键（如飞行模式）依赖", HwIds=new[]{@"ACPI\\INTC1051"}, ProviderRx="(?i)intel" },
            // ---------- 可选 ----------
            new DriverItem { Id="csme", Name="英特尔管理引擎 ME", Tier=Tier.Optional, Url=Oem+"06_CSME.rar", Source="原厂", SizeMB=540, Background=true,
                Note="主要是企业管理功能，体积大，不装也能正常用", HwIds=new[]{@"PCI\\VEN_8086&DEV_51E0"}, ProviderRx="(?i)intel" },
            new DriverItem { Id="gna", Name="英特尔 GNA", Tier=Tier.Optional, Url=Oem+"04_GNA.rar", Source="原厂", SizeMB=1,
                Note="AI 降噪加速单元，几乎没有软件使用", HwIds=new[]{@"PCI\\VEN_8086&DEV_464F"}, ProviderRx="(?i)intel" },
            new DriverItem { Id="sbc", Name="SBC 音效软件", Tier=Tier.Optional, Url=Oem+"10_SBC.rar", Source="原厂", SizeMB=10, Background=true,
                Note="Sound Blaster 音效，常驻后台", AppRx="(?i)sound ?blaster|SBC" },
            new DriverItem { Id="creative", Name="Creative 音效 APP", Tier=Tier.Optional,
                Url="https://download.colorful.cn/EnDownload/OneBot/2023/23AT/23AT%20H45/04_Creative_SBC.zip", Source="原厂", SizeMB=50, Background=true,
                Note="Creative 音效控制面板，常驻后台", AppRx="(?i)creative" },
        };

        public static DriverItem Find(string id) { return All.FirstOrDefault(d => d.Id == id); }

        public static string TierName(Tier t) { return t == Tier.Required ? "必装" : t == Tier.Recommended ? "推荐" : "可选（不装也不影响使用）"; }

        public static string StatusName(DrvStatus s)
        {
            switch (s)
            {
                case DrvStatus.Installed: return "已安装";
                case DrvStatus.NotInstalled: return "未安装";
                case DrvStatus.Failed: return "安装失败";
                case DrvStatus.Installing: return "安装中…";
                case DrvStatus.Outdated: return "有新版本";
                default: return "检测中…";
            }
        }
    }

    /// <summary>检测当前系统里每个驱动的安装情况</summary>
    public static class Detector
    {
        sealed class Row { public string Id, Provider, Version; }

        public static Dictionary<string, DriverState> Detect()
        {
            var rows = new List<Row>();
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT DeviceID, DriverProviderName, DriverVersion FROM Win32_PnPSignedDriver"))
                    foreach (ManagementObject o in s.Get())
                        rows.Add(new Row { Id = (o["DeviceID"] as string) ?? "", Provider = (o["DriverProviderName"] as string) ?? "", Version = (o["DriverVersion"] as string) ?? "" });
            }
            catch { }
            var problems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT DeviceID FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0"))
                    foreach (ManagementObject o in s.Get()) problems.Add((o["DeviceID"] as string) ?? "");
            }
            catch { }
            var apps = InstalledApps();

            var result = new Dictionary<string, DriverState>();
            foreach (var d in Catalog.All)
            {
                var st = new DriverState { Time = DateTime.Now };
                if (d.AppRx != null)
                {
                    var hit = apps.FirstOrDefault(a => Regex.IsMatch(a, d.AppRx));
                    st.Status = hit != null ? DrvStatus.Installed : DrvStatus.NotInstalled;
                    st.Version = hit ?? "";
                }
                else
                {
                    bool all = true; string ver = "";
                    foreach (var rx in d.HwIds)
                    {
                        var m = rows.FirstOrDefault(r => Regex.IsMatch(r.Id, "^" + rx, RegexOptions.IgnoreCase)
                                                        && !string.IsNullOrEmpty(r.Provider)
                                                        && !Regex.IsMatch(r.Provider, "^Microsoft")
                                                        && (d.ProviderRx == null || Regex.IsMatch(r.Provider, d.ProviderRx))
                                                        && !problems.Contains(r.Id));
                        if (m == null) { all = false; break; }
                        if (ver == "") ver = m.Version;
                    }
                    // 设备当前不在系统里（独显直连时核显被隐藏）：驱动已在驱动库里就算已安装，切换到混合模式后系统自动使用
                    if (!all && d.StoreInf != null && !rows.Any(r => d.HwIds.Any(rx => Regex.IsMatch(r.Id, "^" + rx, RegexOptions.IgnoreCase))))
                    {
                        var sv = StagedVersion(d.StoreInf);
                        if (sv != null) { all = true; ver = sv; }
                    }
                    st.Status = all ? DrvStatus.Installed : DrvStatus.NotInstalled;
                    st.Version = ver;
                }
                result[d.Id] = st;
            }
            return result;
        }

        /// <summary>驱动库（DriverStore）里某个 INF 的最高版本；没有则返回 null</summary>
        static string StagedVersion(string inf)
        {
            string best = null; Version bv = null;
            try
            {
                var repo = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "DriverStore", "FileRepository");
                foreach (var dir in Directory.GetDirectories(repo, inf + "_amd64_*"))
                {
                    var f = Path.Combine(dir, inf);
                    if (!File.Exists(f)) continue;
                    var line = File.ReadLines(f).FirstOrDefault(l => l.TrimStart().StartsWith("DriverVer", StringComparison.OrdinalIgnoreCase));
                    var m = line == null ? null : Regex.Match(line, @",\s*([\d.]+)");
                    Version v;
                    if (m != null && m.Success && Version.TryParse(m.Groups[1].Value, out v) && (bv == null || v > bv)) { bv = v; best = m.Groups[1].Value; }
                }
            }
            catch { }
            return best;
        }

        public static List<string> InstalledApps()
        {
            var list = new List<string>();
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                try
                {
                    using (var hk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    using (var k = hk.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                        if (k != null)
                            foreach (var n in k.GetSubKeyNames())
                                using (var sk = k.OpenSubKey(n))
                                {
                                    var dn = sk == null ? null : sk.GetValue("DisplayName") as string;
                                    if (!string.IsNullOrEmpty(dn)) list.Add(dn);
                                }
                }
                catch { }
            return list;
        }
    }

    /// <summary>共享状态文件：%ProgramData%\P15Center\drivers.state（制表符分隔，UTF-8）</summary>
    public static class StateFile
    {
        public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "P15Center");
        public static readonly string FilePath = Path.Combine(Dir, "drivers.state");
        public static readonly string CacheDir = Path.Combine(Dir, "cache");

        public static Dictionary<string, DriverState> Load()
        {
            var d = new Dictionary<string, DriverState>();
            try
            {
                if (!File.Exists(FilePath)) return d;
                foreach (var line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    var p = line.Split('\t');
                    if (p.Length < 5) continue;
                    DrvStatus s; DateTime t;
                    Enum.TryParse(p[1], out s); DateTime.TryParse(p[2], out t);
                    d[p[0]] = new DriverState { Status = s, Time = t, Version = p[3], Message = p[4] };
                }
            }
            catch { }
            return d;
        }

        public static void Save(string id, DrvStatus status, string version, string message)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var all = Load();
                all[id] = new DriverState { Status = status, Time = DateTime.Now, Version = version ?? "", Message = (message ?? "").Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ') };
                var sb = new StringBuilder();
                foreach (var kv in all)
                    sb.Append(kv.Key).Append('\t').Append(kv.Value.Status).Append('\t').Append(kv.Value.Time.ToString("s"))
                      .Append('\t').Append(kv.Value.Version).Append('\t').Append(kv.Value.Message).Append('\n');
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, sb.ToString(), Encoding.UTF8);
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null); else File.Move(tmp, FilePath);
            }
            catch { }
        }

        // ---- 上次检测结果快照：打开「驱动」页时直接显示，不用每次都重新检测 ----
        static readonly string SnapPath = Path.Combine(Dir, "detected.state");

        public static void SaveSnapshot(Dictionary<string, DriverState> states)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var sb = new StringBuilder();
                sb.Append("#").Append(DateTime.Now.ToString("s")).Append('\n');
                foreach (var kv in states)
                    sb.Append(kv.Key).Append('\t').Append(kv.Value.Status).Append('\t').Append(kv.Value.Time.ToString("s"))
                      .Append('\t').Append(kv.Value.Version).Append('\t').Append((kv.Value.Message ?? "").Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ')).Append('\n');
                File.WriteAllText(SnapPath, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        /// <summary>读取上次检测结果；没有或驱动清单变了（新增驱动项）返回 null</summary>
        public static Dictionary<string, DriverState> LoadSnapshot(out DateTime at)
        {
            at = DateTime.MinValue;
            try
            {
                if (!File.Exists(SnapPath)) return null;
                var d = new Dictionary<string, DriverState>();
                foreach (var line in File.ReadAllLines(SnapPath, Encoding.UTF8))
                {
                    if (line.StartsWith("#")) { DateTime.TryParse(line.Substring(1), out at); continue; }
                    var p = line.Split('\t');
                    if (p.Length < 5) continue;
                    DrvStatus s; DateTime t;
                    Enum.TryParse(p[1], out s); DateTime.TryParse(p[2], out t);
                    d[p[0]] = new DriverState { Status = s, Time = t, Version = p[3], Message = p[4] };
                }
                return Catalog.All.All(x => d.ContainsKey(x.Id)) ? d : null;
            }
            catch { return null; }
        }

        /// <summary>在上次检测结果上叠加驱动安装器在那之后写下的安装记录（装好 / 失败 / 有新版本），不重新检测</summary>
        public static Dictionary<string, DriverState> ApplyNewer(Dictionary<string, DriverState> snapshot, DateTime since)
        {
            var d = new Dictionary<string, DriverState>(snapshot);
            foreach (var kv in Load())
                if (kv.Value.Time > since && kv.Value.Status != DrvStatus.Installing && kv.Value.Status != DrvStatus.Unknown)
                    d[kv.Key] = kv.Value;
            return d;
        }
        /// <summary>合并：实时检测为准；检测未安装时，若最近记录为失败则显示失败。</summary>
        public static Dictionary<string, DriverState> Merge(Dictionary<string, DriverState> detected)
        {
            var saved = Load();
            foreach (var kv in detected)
            {
                DriverState s;
                if (kv.Value.Status == DrvStatus.NotInstalled && saved.TryGetValue(kv.Key, out s) && s.Status == DrvStatus.Failed)
                { kv.Value.Status = DrvStatus.Failed; kv.Value.Message = s.Message; }
                if (kv.Value.Status == DrvStatus.Installed && saved.TryGetValue(kv.Key, out s) && s.Status == DrvStatus.Outdated && kv.Value.Version == s.Version)
                { kv.Value.Status = DrvStatus.Outdated; kv.Value.Message = s.Message; }
            }
            return detected;
        }
    }
}
