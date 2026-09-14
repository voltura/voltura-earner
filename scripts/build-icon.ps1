#Requires -Version 7.0
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

Add-Type -AssemblyName System.Drawing

$source = [Drawing.Image]::FromFile((Join-Path $root 'apps/windows/Assets/App-master.png'))
$sizes = @(16, 20, 24, 28, 32, 36, 40, 48, 64, 96, 128, 256)
$frames = [Collections.Generic.List[byte[]]]::new()
$images = Join-Path $root 'docs/images'

New-Item -ItemType Directory -Path $images -Force | Out-Null

try
{
    foreach ($size in $sizes)
    {
        $bitmap = [Drawing.Bitmap]::new($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $stream = [IO.MemoryStream]::new()

        try
        {
            $graphics.Clear([Drawing.Color]::Transparent)

            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality

            $graphics.DrawImage($source, [Drawing.Rectangle]::new(0, 0, $size, $size))
            $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
            $frames.Add($stream.ToArray())

            if ($size -eq 64)
            {
                $bitmap.Save((Join-Path $images 'voltura-earner-tray-icon.png'), [Drawing.Imaging.ImageFormat]::Png)
            }
        }
        finally
        {
            $graphics.Dispose()
            $bitmap.Dispose()
            $stream.Dispose()
        }
    }

    $file = [IO.File]::Create((Join-Path $root 'apps/windows/Assets/App.ico'))
    $writer = [IO.BinaryWriter]::new($file)

    try
    {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$sizes.Count)

        $offset = 6 + 16 * $sizes.Count

        for ($index = 0; $index -lt $sizes.Count; $index++)
        {
            $dimension = if ($sizes[$index] -eq 256)
            {
                0
            }
            else
            {
                $sizes[$index]
            }

            $writer.Write([byte]$dimension)
            $writer.Write([byte]$dimension)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$frames[$index].Length)
            $writer.Write([uint32]$offset)

            $offset += $frames[$index].Length
        }

        foreach ($frame in $frames)
        {
            $writer.Write($frame)
        }
    }
    finally
    {
        $writer.Dispose()
    }
}
finally
{
    $source.Dispose()
}

Write-Output "Built 32-bit alpha icon frames: $($sizes -join ', ') px."
