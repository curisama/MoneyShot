# Money Shot 아이콘 만들기
# 원본은 assets/icon-1024.png 하나다. 여기서 16~256px 여러 크기를 PNG로 담은 .ico를 만든다.
# 레포에는 만들어 둔 money-shot.ico도 같이 있으니 보통은 돌릴 필요가 없다 — 그림을 바꿨을 때만.
# 사용: powershell -ExecutionPolicy Bypass -File make-icon.ps1

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$srcPng = Join-Path $dir 'assets\icon-1024.png'
$out = Join-Path $dir 'money-shot.ico'
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)

if (-not (Test-Path $srcPng)) { throw "원본 그림이 없다: $srcPng" }
$src = [System.Drawing.Image]::FromFile($srcPng)

$frames = @()
foreach ($n in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($n, $n, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($src, 0, 0, $n, $n)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $frames += ,@($n, $ms.ToArray())
}
$src.Dispose()

# ICO: 머리(6바이트) + 항목마다 16바이트 + PNG 데이터
$fs = [System.IO.File]::Create($out)
$w = New-Object System.IO.BinaryWriter($fs)
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $n = $f[0]; $data = $f[1]
    $b = if ($n -ge 256) { 0 } else { $n }
    $w.Write([Byte]$b); $w.Write([Byte]$b); $w.Write([Byte]0); $w.Write([Byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$data.Length); $w.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($f in $frames) { $w.Write($f[1]) }
$w.Close()
"만들었다: $out"
