// 编译时生成 P15 CONTROL 的 exe 图标（与程序内绘制的图标同一套代码）
namespace P15
{
    static class MakeIcon
    {
        static int Main(string[] a)
        {
            Brand.WriteIco(a[0], new[] { 16, 20, 24, 32, 48, 256 });
            return 0;
        }
    }
}
