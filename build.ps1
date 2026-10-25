param([switch]$Test)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) { throw '需要 Windows 的 .NET Framework 4.8。' }
Push-Location -LiteralPath $projectRoot
try {
    New-Item -ItemType Directory -Path 'build','dist','assets' -Force | Out-Null
    Add-Type -AssemblyName System.Drawing
    $bitmap = New-Object System.Drawing.Bitmap 128,128
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $cbIconShape = New-Object System.Drawing.Drawing2D.GraphicsPath
        $cbIconShape.AddArc(3,3,60,60,180,90)
        $cbIconShape.AddArc(65,3,60,60,270,90)
        $cbIconShape.AddArc(65,65,60,60,0,90)
        $cbIconShape.AddArc(3,65,60,60,90,90)
        $cbIconShape.CloseFigure()
        $cbGradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush ((New-Object System.Drawing.Point 0,0)),((New-Object System.Drawing.Point 128,128)),([System.Drawing.Color]::FromArgb(65,158,255)),([System.Drawing.Color]::FromArgb(0,97,216))
        $cbBridgePen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White),7
        try {
            $graphics.FillPath($cbGradient,$cbIconShape)
            $cbBridgePen.StartCap = $cbBridgePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
            $graphics.DrawLine($cbBridgePen,28,84,100,84)
            $graphics.DrawLine($cbBridgePen,37,88,37,47)
            $graphics.DrawLine($cbBridgePen,91,88,91,47)
            $graphics.DrawBezier($cbBridgePen,37,49,47,79,81,79,91,49)
            $graphics.DrawLine($cbBridgePen,54,69,54,84)
            $graphics.DrawLine($cbBridgePen,74,69,74,84)
        } finally { $cbBridgePen.Dispose(); $cbGradient.Dispose(); $cbIconShape.Dispose() }
    } finally { $graphics.Dispose() }
    try {
        $memory = New-Object System.IO.MemoryStream
        $bitmap.Save($memory, [System.Drawing.Imaging.ImageFormat]::Png)
        $png = $memory.ToArray()
        [System.IO.File]::WriteAllBytes((Join-Path $projectRoot 'assets\icon.png'), $png)
        $iconStream = [System.IO.File]::Create((Join-Path $projectRoot 'assets\icon.ico'))
        $writer = New-Object System.IO.BinaryWriter $iconStream
        try {
            $writer.Write([UInt16]0); $writer.Write([UInt16]1); $writer.Write([UInt16]1)
            $writer.Write([Byte]128); $writer.Write([Byte]128); $writer.Write([Byte]0); $writer.Write([Byte]0)
            $writer.Write([UInt16]1); $writer.Write([UInt16]32); $writer.Write([UInt32]$png.Length); $writer.Write([UInt32]22)
            $writer.Write($png)
        } finally { $writer.Dispose(); $iconStream.Dispose(); $memory.Dispose() }
    } finally { $bitmap.Dispose() }

    $sources = @(Get-ChildItem -LiteralPath 'src' -Filter '*.cs' | ForEach-Object { $_.FullName })
    $references = @('/reference:System.Web.Extensions.dll','/reference:System.IO.Compression.dll','/reference:System.IO.Compression.FileSystem.dll','/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll')
    & $compiler /nologo /codepage:65001 /optimize+ /target:winexe /platform:anycpu '/out:dist\CraftBridge.exe' '/win32manifest:src\app.manifest' '/win32icon:assets\icon.ico' $references $sources
    if ($LASTEXITCODE -ne 0) { throw '程序编译失败。' }
    Copy-Item -LiteralPath 'src\App.config' -Destination 'dist\CraftBridge.exe.config' -Force
    Copy-Item -LiteralPath 'LICENSE' -Destination 'dist\LICENSE' -Force
    Copy-Item -LiteralPath 'README.md' -Destination 'dist\使用说明.md' -Force
    Copy-Item -LiteralPath 'docs\FORMAT.md' -Destination 'dist\迁移包格式.md' -Force
    Write-Output '已生成 dist\CraftBridge.exe'
    if ($Test) {
        $coreSources = @('src\Models.cs','src\FileSafety.cs','src\Discovery.cs','src\RuntimeCollector.cs','src\PackageEngine.cs')
        $testSources = @(Get-ChildItem -LiteralPath 'tests' -Filter '*.cs' | Where-Object { $_.Name -ne 'UiTests.cs' } | ForEach-Object { $_.FullName })
        & $compiler /nologo /codepage:65001 /optimize+ /target:exe '/out:build\CraftBridge.Tests.exe' $references $coreSources $testSources
        if ($LASTEXITCODE -ne 0) { throw '测试程序编译失败。' }
        Copy-Item -LiteralPath 'src\App.config' -Destination 'build\CraftBridge.Tests.exe.config' -Force
        & '.\build\CraftBridge.Tests.exe'
        if ($LASTEXITCODE -ne 0) { throw '回归测试失败。' }
        & $compiler /nologo /codepage:65001 /optimize+ /target:exe '/main:CraftBridge.Tests.UiTests' '/out:build\CraftBridge.UiTests.exe' $references $sources 'tests\UiTests.cs'
        if ($LASTEXITCODE -ne 0) { throw '界面状态测试编译失败。' }
        Copy-Item -LiteralPath 'src\App.config' -Destination 'build\CraftBridge.UiTests.exe.config' -Force
        & '.\build\CraftBridge.UiTests.exe'
        if ($LASTEXITCODE -ne 0) { throw '界面状态回归测试失败。' }
    }
} finally { Pop-Location }
