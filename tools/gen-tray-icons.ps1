Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = "Stop"
$outDir = "C:\Users\victor\proyectos\VoiceTyper\src\VoiceTyper\Resources"

$pxf = [System.Drawing.Imaging.PixelFormat]::Format32bppArgb

$icons = @(
    @{ Name = "tray-idle.ico";       Color = [System.Drawing.Color]::FromArgb(158, 158, 158) },
    @{ Name = "tray-recording.ico";  Color = [System.Drawing.Color]::FromArgb(229, 57, 53) },
    @{ Name = "tray-processing.ico"; Color = [System.Drawing.Color]::FromArgb(255, 179, 0) },
    @{ Name = "tray-error.ico";      Color = [System.Drawing.Color]::FromArgb(251, 140, 0) }
)

$src = @"
using System;
using System.Runtime.InteropServices;

public class Win32 {
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyIcon(IntPtr hIcon);
}
"@
Add-Type -TypeDefinition $src

function New-IconFile([string] $path, [System.Drawing.Color] $color) {
    $bmp = New-Object System.Drawing.Bitmap 32, 32, $pxf
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $brush = New-Object System.Drawing.SolidBrush $color
    $g.FillEllipse($brush, 1, 1, 30, 30)

    $g.Dispose()
    $brush.Dispose()

    $hicon = $bmp.GetHicon()
    $icon = [System.Drawing.Icon]::FromHandle($hicon)
    $fs = [System.IO.File]::Create($path)
    $icon.Save($fs)
    $fs.Close()
    $icon.Dispose()
    [Win32]::DestroyIcon($hicon) | Out-Null
    $bmp.Dispose()
}

foreach ($i in $icons) {
    $path = Join-Path $outDir $i.Name
    if (Test-Path $path) { Remove-Item $path -Force }
    New-IconFile -path $path -color $i.Color
    $size = (Get-Item $path).Length
    Write-Host "$($i.Name): $size bytes"
}

Write-Host "done"
