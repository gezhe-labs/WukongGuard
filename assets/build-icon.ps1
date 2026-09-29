$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$images = New-Object 'System.Collections.Generic.List[byte[]]'
foreach ($size in @(16, 32, 48, 256)) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size)
    $graphic = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphic.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $scale = $size / 256.0
    $graphic.ScaleTransform($scale, $scale)
    $background = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(22, 25, 31))
    $gold = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(249, 194, 99))
    $pen = [System.Drawing.Pen]::new($gold.Color, 17)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddArc(0, 0, 108, 108, 180, 90)
    $path.AddArc(148, 0, 108, 108, 270, 90)
    $path.AddArc(148, 148, 108, 108, 0, 90)
    $path.AddArc(0, 148, 108, 108, 90, 90)
    $path.CloseFigure()
    $graphic.FillPath($background, $path)
    $graphic.DrawArc($pen, 49, 49, 158, 158, 2, 300)
    $graphic.FillEllipse($gold, 180, 57, 24, 24)
    $graphic.FillEllipse($gold, 94, 94, 68, 68)
    $darkPen = [System.Drawing.Pen]::new($background.Color, 7)
    $graphic.DrawLine($darkPen, 128, 95, 128, 161)
    $stream = [System.IO.MemoryStream]::new()
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    if ($size -eq 256) { $bitmap.Save((Join-Path $PSScriptRoot 'logo.png'),
        [System.Drawing.Imaging.ImageFormat]::Png) }
    $images.Add($stream.ToArray())
    $stream.Dispose(); $darkPen.Dispose(); $path.Dispose(); $pen.Dispose()
    $background.Dispose(); $gold.Dispose(); $graphic.Dispose(); $bitmap.Dispose()
}

$target = Join-Path $PSScriptRoot 'regretpill.ico'
$file = [System.IO.File]::Create($target)
$writer = [System.IO.BinaryWriter]::new($file)
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$images.Count)
$offset = 6 + 16 * $images.Count
for ($index = 0; $index -lt $images.Count; $index++) {
    $size = @(16, 32, 48, 256)[$index]
    $writer.Write([byte]($size % 256)); $writer.Write([byte]($size % 256))
    $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$images[$index].Length)
    $writer.Write([uint32]$offset)
    $offset += $images[$index].Length
}
foreach ($image in $images) { $writer.Write($image) }
$writer.Dispose()
Write-Output $target
