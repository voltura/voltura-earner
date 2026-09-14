#Requires -Version 7.0
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

Add-Type -AssemblyName System.Drawing

$source = [Drawing.Image]::FromFile((Join-Path $root 'installer/Assets/wizard-master.png'))
$bitmap = [Drawing.Bitmap]::new($source.Width, $source.Height, [Drawing.Imaging.PixelFormat]::Format24bppRgb)
$graphics = [Drawing.Graphics]::FromImage($bitmap)

try
{
    # NSIS requires BMP. Keep the master's full resolution and opaque RGB colors.
    $graphics.DrawImageUnscaled($source, 0, 0)
    $bitmap.Save((Join-Path $root 'installer/Assets/wizard.bmp'), [Drawing.Imaging.ImageFormat]::Bmp)
}
finally
{
    $graphics.Dispose()
    $bitmap.Dispose()
    $source.Dispose()
}
