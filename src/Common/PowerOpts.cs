// Windows 电源选项（powrprof API）：电源计划、合盖动作、关闭显示器/睡眠时间。
// 只改这几项，不碰处理器功耗相关的电源设置，避免影响 BIOS 里的功耗配置。
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace P15
{
    public sealed class PowerPlan { public Guid Id; public string Name; public override string ToString() { return Name; } }

    public static class PowerOpts
    {
        [DllImport("powrprof.dll")] static extern uint PowerEnumerate(IntPtr root, IntPtr scheme, IntPtr sub, uint access, uint index, byte[] buf, ref uint size);
        [DllImport("powrprof.dll")] static extern uint PowerReadFriendlyName(IntPtr root, ref Guid scheme, IntPtr sub, IntPtr setting, byte[] buf, ref uint size);
        [DllImport("powrprof.dll")] static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
        [DllImport("powrprof.dll")] static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);
        [DllImport("powrprof.dll")] static extern uint PowerReadACValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, out uint value);
        [DllImport("powrprof.dll")] static extern uint PowerReadDCValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, out uint value);
        [DllImport("powrprof.dll")] static extern uint PowerWriteACValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, uint value);
        [DllImport("powrprof.dll")] static extern uint PowerWriteDCValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, uint value);
        [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr p);

        static Guid SubButtons = new Guid("4f971e89-eebd-4455-a8de-9e59040e7347"), LidAction = new Guid("5ca83367-6e45-459f-a27b-476b1d01c936");
        static Guid SubVideo = new Guid("7516b95f-f776-4464-8c53-06167f40cc99"), VideoIdle = new Guid("3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e");
        static Guid SubSleep = new Guid("238c9fa8-0aad-41ed-83f4-97be242c8f20"), StandbyIdle = new Guid("29f6c1db-86da-48c5-9fdb-f2b67b1f44da");

        public enum Item { Lid, Display, Sleep }

        static void Ids(Item it, out Guid sub, out Guid set)
        {
            switch (it)
            {
                case Item.Lid: sub = SubButtons; set = LidAction; break;
                case Item.Display: sub = SubVideo; set = VideoIdle; break;
                default: sub = SubSleep; set = StandbyIdle; break;
            }
        }

        public static List<PowerPlan> Plans()
        {
            var list = new List<PowerPlan>();
            for (uint i = 0; ; i++)
            {
                var buf = new byte[16]; uint size = 16;
                if (PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 16 /*ACCESS_SCHEME*/, i, buf, ref size) != 0) break;
                var g = new Guid(buf);
                uint n = 0;
                PowerReadFriendlyName(IntPtr.Zero, ref g, IntPtr.Zero, IntPtr.Zero, null, ref n);
                var nb = new byte[Math.Max(n, 2)];
                PowerReadFriendlyName(IntPtr.Zero, ref g, IntPtr.Zero, IntPtr.Zero, nb, ref n);
                var name = System.Text.Encoding.Unicode.GetString(nb).TrimEnd('\0');
                if (name == "Ultimate Performance") name = "卓越性能";
                list.Add(new PowerPlan { Id = g, Name = name });
            }
            return list;
        }

        public static Guid Active()
        {
            IntPtr p;
            if (PowerGetActiveScheme(IntPtr.Zero, out p) != 0) return Guid.Empty;
            var g = (Guid)Marshal.PtrToStructure(p, typeof(Guid));
            LocalFree(p);
            return g;
        }

        public static bool SetActive(Guid g) { return PowerSetActiveScheme(IntPtr.Zero, ref g) == 0; }

        public static uint Read(Item it, bool ac)
        {
            Guid sub, set, s = Active(); Ids(it, out sub, out set);
            uint v;
            uint r = ac ? PowerReadACValueIndex(IntPtr.Zero, ref s, ref sub, ref set, out v) : PowerReadDCValueIndex(IntPtr.Zero, ref s, ref sub, ref set, out v);
            return r == 0 ? v : uint.MaxValue;
        }

        public static bool Write(Item it, bool ac, uint value)
        {
            Guid sub, set, s = Active(); Ids(it, out sub, out set);
            uint r = ac ? PowerWriteACValueIndex(IntPtr.Zero, ref s, ref sub, ref set, value) : PowerWriteDCValueIndex(IntPtr.Zero, ref s, ref sub, ref set, value);
            if (r != 0) return false;
            return PowerSetActiveScheme(IntPtr.Zero, ref s) == 0;   // 重新激活使设置立即生效
        }
    }
}
