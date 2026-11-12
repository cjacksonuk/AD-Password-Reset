# Builds ADUserManager.exe with the C# compiler that ships with Windows (.NET Framework 4.8).
# No Visual Studio, SDK or NuGet needed.  Output: .\dist\ADUserManager.exe
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src  = Join-Path $root 'src'
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }

# ---- Generate the app icon (PNG-compressed .ico) -----------------------------
$ico = Join-Path $src 'app.ico'
if (-not (Test-Path $ico)) {
    Add-Type -AssemblyName System.Drawing
    $sizes = 16, 24, 32, 48, 64, 256
    $pngs = foreach ($s in $sizes) {
        $bmp = New-Object System.Drawing.Bitmap $s, $s
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'
        $r = [Math]::Max(2, [int]($s * 0.22)); $d = $r * 2
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $path.AddArc(0, 0, $d, $d, 180, 90); $path.AddArc($s - $d - 1, 0, $d, $d, 270, 90)
        $path.AddArc($s - $d - 1, $s - $d - 1, $d, $d, 0, 90); $path.AddArc(0, $s - $d - 1, $d, $d, 90, 90); $path.CloseFigure()
        $rect = New-Object System.Drawing.Rectangle 0, 0, $s, $s
        $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(19, 41, 75)), ([System.Drawing.Color]::FromArgb(0, 103, 192)), 45
        $g.FillPath($brush, $path)
        $font = New-Object System.Drawing.Font 'Segoe MDL2 Assets', ([single]($s * 0.52)), ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
        $sf = New-Object System.Drawing.StringFormat; $sf.Alignment = 'Center'; $sf.LineAlignment = 'Center'
        $g.DrawString([string][char]0xE8D7, $font, [System.Drawing.Brushes]::White, (New-Object System.Drawing.RectangleF 0, ([single]($s * 0.03)), $s, $s), $sf)
        $g.Dispose()
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
        , $ms.ToArray()
    }
    $fs = [System.IO.File]::Create($ico); $bw = New-Object System.IO.BinaryWriter $fs
    $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $s = $sizes[$i]; $len = $pngs[$i].Length
        $bw.Write([byte]($s % 256)); $bw.Write([byte]($s % 256)); $bw.Write([byte]0); $bw.Write([byte]0)
        $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$len); $bw.Write([uint32]$offset)
        $offset += $len
    }
    foreach ($p in $pngs) { $bw.Write($p) }
    $bw.Close()
}

# ---- Compile ------------------------------------------------------------------
$out = Join-Path $dist 'ADUserManager.exe'
$files = Get-ChildItem $src -Filter *.cs | ForEach-Object { $_.FullName }
& $csc /nologo /target:winexe /platform:anycpu /optimize+ /utf8output `
    "/out:$out" "/win32icon:$ico" "/win32manifest:$(Join-Path $src 'app.manifest')" `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    /r:System.DirectoryServices.dll $files
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
Write-Host "Built $out ($([int]((Get-Item $out).Length / 1KB)) KB)" -ForegroundColor Green
