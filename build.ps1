# 编译到 out\ 。需要 .NET SDK（只用其中的 Roslyn 编译器），生成的 exe 运行在系统自带的 .NET Framework 4.8 上。
# out\ 里只有控制中心本体；驱动安装器和散热器插件都压缩内置在控制中心里，用户需要时才释放：
#   驱动安装器 → 「驱动」页「获取驱动组件」
#   散热器插件 → 首次启动询问 /「设置 → 扩展功能」
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$out = Join-Path $root 'out'
$obj = Join-Path $root 'obj'
$stage = Join-Path $obj 'stage'
New-Item -ItemType Directory -Force $out, $obj, $stage | Out-Null

$sdk = Get-ChildItem 'C:\Program Files\dotnet\sdk' -Directory | Sort-Object Name -Descending | Select-Object -First 1
$csc = Join-Path $sdk.FullName 'Roslyn\bincore\csc.dll'
$fw = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$refs = 'mscorlib.dll','System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Management.dll','System.ServiceProcess.dll','System.Net.Http.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll' |
    ForEach-Object { "/r:$fw\$_" }
# 散热器插件额外引用：WinRT（蓝牙 LE）
# WinRT 元数据（散热器插件的蓝牙接口用）。仓库里不附带这个微软文件，按顺序找：
#   1) lib\Windows.winmd（自己放的）  2) 原厂控制中心目录里的 Windows.winmd（装了原厂 Control Center 就有）
#   都没有时跳过散热器插件，控制中心其它功能照常编译
$winmd = @((Join-Path $root 'lib\Windows.winmd'), 'C:\Program Files (x86)\ControlCenter\Windows.winmd') | Where-Object { Test-Path $_ } | Select-Object -First 1
$winrt = @("/r:$fw\System.Runtime.dll", "/r:$fw\System.Runtime.WindowsRuntime.dll", "/r:$fw\System.Runtime.InteropServices.WindowsRuntime.dll", "/r:$fw\System.Threading.Tasks.dll", "/r:$winmd")

function Build($target, $dirs, $extra = @(), $kind = 'winexe') {
    $files = $dirs | ForEach-Object { Get-ChildItem (Join-Path $root "src\$_") -Filter *.cs } | ForEach-Object { $_.FullName }
    [string[]]$man = @(); if ($kind -eq 'winexe') { $man = @("/win32manifest:$root\src\app.manifest", "/win32icon:$ico") }
    & dotnet $csc /nologo /noconfig /nostdlib+ "/target:$kind" /platform:x64 /optimize+ /langversion:latest /utf8output `
        @man "/out:$target" @refs @extra @files
    if ($LASTEXITCODE -ne 0) { throw "编译失败：$target" }
    Write-Host "OK  $(Split-Path $target -Leaf)  $([math]::Round((Get-Item $target).Length/1KB)) KB"
}

function Gz($src, $dst) {
    $in = [IO.File]::OpenRead($src); $fs = [IO.File]::Create($dst)
    $z = New-Object IO.Compression.GZipStream($fs, [IO.Compression.CompressionLevel]::Optimal)
    $in.CopyTo($z); $z.Dispose(); $fs.Dispose(); $in.Dispose()
    Write-Host "    → 压缩 $([math]::Round((Get-Item $dst).Length/1KB)) KB"
}

# 0) P15 CONTROL 图标（和程序内绘制的是同一套代码）
$ico = Join-Path $obj 'p15control.ico'
$mk = Join-Path $obj 'MakeIcon.exe'
& dotnet $csc /nologo /noconfig /nostdlib+ /target:exe /platform:x64 /optimize+ /langversion:latest "/out:$mk" @refs (Join-Path $root 'tools\MakeIcon.cs') (Join-Path $root 'src\Common\Brand.cs')
if ($LASTEXITCODE -ne 0) { throw '编译失败：MakeIcon' }
& $mk $ico
if (-not (Test-Path $ico)) { throw '生成图标失败' }
Write-Host "OK  p15control.ico  $([math]::Round((Get-Item $ico).Length/1KB)) KB"

# 1) 驱动安装器
$setup = Join-Path $obj 'P15驱动安装器.exe'
Build $setup @('Common','Setup')
Gz $setup (Join-Path $obj 'DriverSetup.gz')

# 2) 控制中心第一遍（供插件引用其中的界面控件和设置）
$stageExe = Join-Path $stage 'P15控制中心.exe'
Build $stageExe @('Common','Center')

# 3) 散热器插件（引用控制中心）
$res = @("/resource:$obj\DriverSetup.gz,P15.DriverSetup.gz")
if ($winmd) {
    $cooler = Join-Path $obj 'P15Cooler.dll'
    Build $cooler @('Cooler') ($winrt + @("/r:$stageExe")) 'library'
    Gz $cooler (Join-Path $obj 'Cooler.gz')
    $res += "/resource:$obj\Cooler.gz,P15.Cooler.gz"
} else { Write-Warning '没找到 Windows.winmd（lib\ 或原厂控制中心目录），本次不编译散热器插件' }

# 4) 控制中心最终版（内置压缩组件）
Build (Join-Path $out 'P15控制中心.exe') @('Common','Center') $res
# 硬件接口 InsydeDCHU.dll（原厂文件，放在 lib\）：和控制中心放在一起，安装包也会带上
$dchu = Join-Path $root 'lib\InsydeDCHU.dll'
if (Test-Path $dchu) { Copy-Item $dchu $out -Force } else { Write-Warning '没找到 lib\InsydeDCHU.dll：安装包不带硬件接口，运行时会去原厂软件里找' }
Remove-Item (Join-Path $out 'P15驱动安装器.exe'), (Join-Path $out 'P15Cooler.dll') -Force -ErrorAction SilentlyContinue

# 5) 安装包（内置压缩的控制中心本体；同一个 exe 也是卸载程序）
$ver = ([regex]::Match((Get-Content (Join-Path $root 'src\Common\Brand.cs') -Raw -Encoding UTF8), 'Version = "([^"]+)"')).Groups[1].Value
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
Gz (Join-Path $out 'P15控制中心.exe') (Join-Path $obj 'App.gz')
$instRes = @("/resource:$obj\App.gz,P15.App.gz")
if (Test-Path $dchu) { Gz $dchu (Join-Path $obj 'Dchu.gz'); $instRes += "/resource:$obj\Dchu.gz,P15.Dchu.gz" }
# 原厂硬件驱动 AcpiBridge（微软签名）：重装系统后设备没有驱动时，安装包自动装上
foreach ($n in 'AcpiBridge.inf','AcpiBridge.sys','AcpiBridge.cat') {
    $p = Join-Path $root "lib\acpibridge\$n"
    if (Test-Path $p) { Gz $p (Join-Path $obj "Acpi.$n.gz"); $instRes += "/resource:$obj\Acpi.$n.gz,P15.Acpi.$n.gz" }
}
Build (Join-Path $dist "P15CONTROL-Setup-$ver.exe") @('Common','Installer') $instRes