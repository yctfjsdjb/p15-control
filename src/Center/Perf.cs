// 性能优化项与 CPU 跑分。
// 原则：不改任何功耗限制（PL1/PL2、电压、BIOS 功耗配置），只调整 Windows 层面会拖慢性能的设置；
// 每一项都能随时恢复，需要重启的会明确提示。
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace P15
{
    public static class PerfTweaks
    {
        // ---------- 1. 唤醒全部核心（处理器核心休眠：最少不休眠核心 = 100%，仅插电） ----------
        static Guid SubProc = new Guid("54533251-82be-4824-96c1-47b60b740d00");
        static Guid CpMinCores = new Guid("0cc5b647-c1df-4637-891a-dec35c318583");
        [DllImport("powrprof.dll")] static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
        [DllImport("powrprof.dll")] static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);
        [DllImport("powrprof.dll")] static extern uint PowerReadACValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, out uint value);
        [DllImport("powrprof.dll")] static extern uint PowerWriteACValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, uint value);
        [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr p);

        static Guid Active()
        {
            IntPtr p; PowerGetActiveScheme(IntPtr.Zero, out p);
            var g = (Guid)Marshal.PtrToStructure(p, typeof(Guid)); LocalFree(p); return g;
        }

        /// <summary>当前插电时最少不休眠核心百分比</summary>
        public static int CoreParkingMin()
        {
            var s = Active(); uint v;
            return PowerReadACValueIndex(IntPtr.Zero, ref s, ref SubProc, ref CpMinCores, out v) == 0 ? (int)v : -1;
        }

        /// <summary>on=100%（全部唤醒）；off=恢复 Windows 默认 4%</summary>
        public static bool SetAllCoresAwake(bool on)
        {
            var s = Active();
            if (PowerWriteACValueIndex(IntPtr.Zero, ref s, ref SubProc, ref CpMinCores, on ? 100u : 4u) != 0) return false;
            return PowerSetActiveScheme(IntPtr.Zero, ref s) == 0;
        }

        /// <summary>当前被休眠（停放）的逻辑处理器数量</summary>
        public static int ParkedCores()
        {
            try
            {
                var cat = new PerformanceCounterCategory("Processor Information");
                int parked = 0;
                foreach (var inst in cat.GetInstanceNames().Where(n => Regex.IsMatch(n, @"^\d+,\d+$")))
                    using (var c = new PerformanceCounter("Processor Information", "Parking Status", inst))
                        if (c.NextValue() > 0) parked++;
                return parked;
            }
            catch { return -1; }
        }

        // ---------- 2. 硬件加速 GPU 计划（HAGS，DLSS 3 帧生成需要） ----------
        const string GfxKey = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";

        [StructLayout(LayoutKind.Sequential)] struct ADAPTERINFO { public uint hAdapter; public uint LuidLow; public int LuidHigh; public uint NumOfSources; public int Precise; }
        [StructLayout(LayoutKind.Sequential)] struct ENUM2 { public uint Num; public IntPtr pAdapters; }
        [StructLayout(LayoutKind.Sequential)] struct QUERY { public uint hAdapter; public int Type; public IntPtr pData; public uint Size; }
        [StructLayout(LayoutKind.Sequential)] struct CLOSE { public uint hAdapter; }
        [DllImport("gdi32.dll")] static extern int D3DKMTEnumAdapters2(ref ENUM2 e);
        [DllImport("gdi32.dll")] static extern int D3DKMTQueryAdapterInfo(ref QUERY q);
        [DllImport("gdi32.dll")] static extern int D3DKMTCloseAdapter(ref CLOSE c);

        /// <summary>系统实际状态：null=查询失败；读取显卡驱动报告的 WDDM 2.7 能力（HwSchEnabled 位）</summary>
        public static bool? HagsRunning()
        {
            try
            {
                var e = new ENUM2();
                D3DKMTEnumAdapters2(ref e);
                if (e.Num == 0) return null;
                int sz = Marshal.SizeOf(typeof(ADAPTERINFO));
                e.pAdapters = Marshal.AllocHGlobal(sz * (int)e.Num);
                bool any = false, found = false;
                try
                {
                    if (D3DKMTEnumAdapters2(ref e) != 0) return null;
                    for (int i = 0; i < e.Num; i++)
                    {
                        var a = (ADAPTERINFO)Marshal.PtrToStructure(e.pAdapters + i * sz, typeof(ADAPTERINFO));
                        var buf = Marshal.AllocHGlobal(4);
                        try
                        {
                            Marshal.WriteInt32(buf, 0);
                            var q = new QUERY { hAdapter = a.hAdapter, Type = 70 /* KMTQAITYPE_WDDM_2_7_CAPS */, pData = buf, Size = 4 };
                            if (D3DKMTQueryAdapterInfo(ref q) == 0)
                            {
                                int flags = Marshal.ReadInt32(buf);
                                if ((flags & 1) != 0) { found = true; if ((flags & 2) != 0) any = true; }   // bit0 支持, bit1 已启用
                            }
                        }
                        finally { Marshal.FreeHGlobal(buf); var c = new CLOSE { hAdapter = a.hAdapter }; D3DKMTCloseAdapter(ref c); }
                    }
                }
                finally { Marshal.FreeHGlobal(e.pAdapters); }
                return found ? (bool?)any : null;
            }
            catch { return null; }
        }

        /// <summary>注册表里设定的值：null=系统默认 true=开 false=关</summary>
        public static bool? HagsConfigured()
        {
            using (var k = Registry.LocalMachine.OpenSubKey(GfxKey))
            {
                var v = k == null ? null : k.GetValue("HwSchMode");
                return v == null ? (bool?)null : Convert.ToInt32(v) == 2;
            }
        }

        public static void SetHags(bool on)
        {
            using (var k = Registry.LocalMachine.CreateSubKey(GfxKey)) k.SetValue("HwSchMode", on ? 2 : 1, RegistryValueKind.DWord);
        }

        // ---------- 3. 关闭虚拟化平台（Hypervisor，游戏有额外开销） ----------
        public static bool HypervisorRunning()
        {
            try { using (var s = new ManagementObjectSearcher("SELECT HypervisorPresent FROM Win32_ComputerSystem")) foreach (ManagementObject o in s.Get()) return (bool)o["HypervisorPresent"]; }
            catch { }
            return false;
        }

        static string Bcd(string args)
        {
            var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "bcdedit.exe"), args)
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            using (var p = Process.Start(psi)) { var o = p.StandardOutput.ReadToEnd(); p.WaitForExit(10000); return o; }
        }

        /// <summary>下次启动是否加载 Hypervisor（bcdedit hypervisorlaunchtype）</summary>
        public static bool HypervisorConfigured()
        {
            var m = Regex.Match(Bcd("/enum {current}"), @"hypervisorlaunchtype\s+(\w+)", RegexOptions.IgnoreCase);
            return !m.Success || !m.Groups[1].Value.Equals("Off", StringComparison.OrdinalIgnoreCase);
        }

        public static bool SetHypervisor(bool on)
        {
            Bcd("/set {current} hypervisorlaunchtype " + (on ? "auto" : "off"));
            return HypervisorConfigured() == on;
        }

    }
}
