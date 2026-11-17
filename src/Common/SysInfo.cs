// 系统占用监控（仅在窗口可见时采样）+ 触控板开关。
// 只用系统原生接口（PDH / powrprof / SetupAPI），不加载 WMI 和 .NET 性能计数器：
// 内存占用小、采样快，窗口关闭后资源全部释放。
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace P15
{
    public sealed class SysUsage
    {
        public int CpuLoad = -1, CpuMHz = -1, GpuLoad = -1, MemPct = -1;
        public double MemUsedGB, MemTotalGB;
    }

    public sealed class SysMonitor : IDisposable
    {
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhOpenQuery(string src, IntPtr user, out IntPtr q);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhAddEnglishCounter(IntPtr q, string path, IntPtr user, out IntPtr c);
        [DllImport("pdh.dll")] static extern int PdhCollectQueryData(IntPtr q);
        [DllImport("pdh.dll")] static extern int PdhGetFormattedCounterValue(IntPtr c, uint fmt, IntPtr type, out FmtValue v);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhGetFormattedCounterArray(IntPtr c, uint fmt, ref uint size, out uint count, IntPtr buf);
        [DllImport("pdh.dll")] static extern int PdhCloseQuery(IntPtr q);
        [StructLayout(LayoutKind.Explicit)] struct FmtValue { [FieldOffset(0)] public uint Status; [FieldOffset(8)] public double Value; }
        const uint FmtDouble = 0x200, NoCap100 = 0x8000;
        const int PdhMoreData = unchecked((int)0x800007D2);

        [StructLayout(LayoutKind.Sequential)] struct MEMSTAT { public uint len, load; public ulong total, avail, tp, ap, tv, av, ave; }
        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MEMSTAT m);
        [StructLayout(LayoutKind.Sequential)] struct PPI { public uint Number, MaxMhz, CurrentMhz, MhzLimit, MaxIdleState, CurrentIdleState; }
        [DllImport("powrprof.dll")] static extern uint CallNtPowerInformation(int level, IntPtr inB, uint inLen, [Out] PPI[] outB, uint outLen);

        IntPtr query, cpu, perf, gpu;
        IntPtr buf; uint bufSize;
        int baseMHz;
        bool ready;
        readonly object gate = new object();

        void Init()
        {
            if (ready) return;
            ready = true;
            // 基准频率（与任务管理器一致，12450H 为 2000MHz）
            try
            {
                int n = Environment.ProcessorCount;
                var a = new PPI[n];
                if (CallNtPowerInformation(11, IntPtr.Zero, 0, a, (uint)(n * Marshal.SizeOf(typeof(PPI)))) == 0) baseMHz = (int)a[0].MaxMhz;
            }
            catch { }
            if (PdhOpenQuery(null, IntPtr.Zero, out query) != 0) { query = IntPtr.Zero; return; }
            if (PdhAddEnglishCounter(query, @"\Processor Information(_Total)\% Processor Utility", IntPtr.Zero, out cpu) != 0) cpu = IntPtr.Zero;
            if (PdhAddEnglishCounter(query, @"\Processor Information(_Total)\% Processor Performance", IntPtr.Zero, out perf) != 0) perf = IntPtr.Zero;
            // 所有进程的 3D 引擎占用（通配实例，每次采集自动更新进程列表）
            if (PdhAddEnglishCounter(query, @"\GPU Engine(*engtype_3D)\Utilization Percentage", IntPtr.Zero, out gpu) != 0) gpu = IntPtr.Zero;
            PdhCollectQueryData(query);   // 速率类计数器需要两次采集，先采一次
        }

        double Value(IntPtr c)
        {
            FmtValue v;
            return c != IntPtr.Zero && PdhGetFormattedCounterValue(c, FmtDouble | NoCap100, IntPtr.Zero, out v) == 0 && v.Status == 0 ? v.Value : -1;
        }

        double GpuSum()
        {
            if (gpu == IntPtr.Zero) return -1;
            uint size = bufSize, count;
            int rc = PdhGetFormattedCounterArray(gpu, FmtDouble | NoCap100, ref size, out count, buf);
            if (rc == PdhMoreData)
            {
                if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf);
                bufSize = size + 4096; buf = Marshal.AllocHGlobal((int)bufSize); size = bufSize;
                rc = PdhGetFormattedCounterArray(gpu, FmtDouble | NoCap100, ref size, out count, buf);
            }
            if (rc != 0) return count == 0 ? 0 : -1;
            // PDH_FMT_COUNTERVALUE_ITEM：名称指针(8) + 值(16)
            double sum = 0;
            for (int i = 0; i < count; i++)
            {
                var item = buf + i * 24;
                if (Marshal.ReadInt32(item, 8) == 0) sum += BitConverter.Int64BitsToDouble(Marshal.ReadInt64(item, 16));
            }
            return sum;
        }

        /// <summary>在后台线程调用</summary>
        public SysUsage Sample()
        {
            var u = new SysUsage();
            lock (gate)
            {
                Init();
                if (query != IntPtr.Zero && PdhCollectQueryData(query) == 0)
                {
                    double c = Value(cpu), p = Value(perf), g = GpuSum();
                    if (c >= 0) u.CpuLoad = (int)Math.Min(100, Math.Round(c));
                    if (p >= 0 && baseMHz > 0) u.CpuMHz = (int)Math.Round(p / 100.0 * baseMHz);
                    if (g >= 0) u.GpuLoad = (int)Math.Min(100, Math.Round(g));
                }
            }
            var m = new MEMSTAT { len = (uint)Marshal.SizeOf(typeof(MEMSTAT)) };
            if (GlobalMemoryStatusEx(ref m))
            {
                u.MemPct = (int)m.load;
                u.MemTotalGB = m.total / 1073741824.0;
                u.MemUsedGB = (m.total - m.avail) / 1073741824.0;
            }
            return u;
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (query != IntPtr.Zero) { PdhCloseQuery(query); query = IntPtr.Zero; }
                if (buf != IntPtr.Zero) { Marshal.FreeHGlobal(buf); buf = IntPtr.Zero; bufSize = 0; }
                cpu = perf = gpu = IntPtr.Zero;
                ready = false;
            }
        }
    }

    /// <summary>触控板开关：启用/禁用"符合 HID 标准的触摸板"设备（立即生效）。用 SetupAPI 查询，不用 WMI</summary>
    public static class Touchpad
    {
        static Guid HidClass = new Guid("745a17a0-74d3-11d0-b6fe-00a0c90f57da");
        [StructLayout(LayoutKind.Sequential)] struct DevInfo { public int cbSize; public Guid ClassGuid; public int DevInst; public IntPtr Reserved; }
        [DllImport("setupapi.dll", SetLastError = true)] static extern IntPtr SetupDiGetClassDevs(ref Guid cls, IntPtr enumerator, IntPtr hwnd, int flags);
        [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiEnumDeviceInfo(IntPtr set, int index, ref DevInfo data);
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool SetupDiGetDeviceRegistryProperty(IntPtr set, ref DevInfo data, int prop, out int type, byte[] buf, int size, out int need);
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool SetupDiGetDeviceInstanceId(IntPtr set, ref DevInfo data, StringBuilder id, int size, out int need);
        [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("cfgmgr32.dll")] static extern int CM_Get_DevNode_Status(out int status, out int problem, int devInst, int flags);

        static string FindId(out bool enabled)
        {
            enabled = true;
            var set = SetupDiGetClassDevs(ref HidClass, IntPtr.Zero, IntPtr.Zero, 2);   // DIGCF_PRESENT（停用的设备也算存在）
            if (set == IntPtr.Zero || set == new IntPtr(-1)) return null;
            try
            {
                var buf = new byte[512];
                for (int i = 0; ; i++)
                {
                    var d = new DevInfo { cbSize = Marshal.SizeOf(typeof(DevInfo)) };
                    if (!SetupDiEnumDeviceInfo(set, i, ref d)) break;
                    int type, need;
                    string name = "";
                    foreach (int prop in new[] { 12, 0 })   // SPDRP_FRIENDLYNAME, SPDRP_DEVICEDESC
                        if (SetupDiGetDeviceRegistryProperty(set, ref d, prop, out type, buf, buf.Length, out need))
                        { name = Encoding.Unicode.GetString(buf, 0, Math.Max(0, need - 2)); break; }
                    if (!(name.Contains("触摸板") || name.Contains("触控板") || name.IndexOf("touch pad", StringComparison.OrdinalIgnoreCase) >= 0
                          || name.IndexOf("touchpad", StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                    var sb = new StringBuilder(260);
                    if (!SetupDiGetDeviceInstanceId(set, ref d, sb, sb.Capacity, out need)) continue;
                    int status, problem;
                    if (CM_Get_DevNode_Status(out status, out problem, d.DevInst, 0) == 0) enabled = problem != 22;   // CM_PROB_DISABLED
                    return sb.ToString();
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return null;
        }

        /// <summary>null = 没找到触控板</summary>
        public static bool? Get()
        {
            try { bool en; return FindId(out en) == null ? (bool?)null : en; } catch { return null; }
        }

        public static bool Set(bool on)
        {
            try
            {
                bool en; var id = FindId(out en);
                if (id == null) return false;
                var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "pnputil.exe"),
                    (on ? "/enable-device \"" : "/disable-device \"") + id + "\"")
                { UseShellExecute = false, CreateNoWindow = true };
                using (var p = Process.Start(psi)) { p.WaitForExit(15000); }
                return Get() == on;
            }
            catch { return false; }
        }
    }
}
