param([string]$OutputPath = (Join-Path $PSScriptRoot 'neon.ico'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$frames = @()
foreach ($size in @(16, 20, 24, 32, 48, 64, 128, 256)) {
    $bitmap = New-Object System.Drawing.Bitmap($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.ScaleTransform(($size / 32.0), ($size / 32.0))
    $outline = New-Object System.Drawing.Drawing2D.GraphicsPath
    $outline.AddArc(1, 1, 14, 14, 180, 90)
    $outline.AddArc(17, 1, 14, 14, 270, 90)
    $outline.AddArc(17, 17, 14, 14, 0, 90)
    $outline.AddArc(1, 17, 14, 14, 90, 90)
    $outline.CloseFigure()
    $background = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml('#0B1813'))
    $border = New-Object System.Drawing.Pen([System.Drawing.ColorTranslator]::FromHtml('#41634E'), 1)
    $neon = New-Object System.Drawing.Pen([System.Drawing.ColorTranslator]::FromHtml('#70FFA7'), 3.2)
    $neon.StartCap = $neon.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $neon.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $cyan = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml('#73DDEB'))
    $graphics.FillPath($background, $outline)
    $graphics.DrawPath($border, $outline)
    $points = [System.Drawing.PointF[]]@((New-Object System.Drawing.PointF(9,24)), (New-Object System.Drawing.PointF(9,8)), (New-Object System.Drawing.PointF(23,24)), (New-Object System.Drawing.PointF(23,8)))
    $graphics.DrawLines($neon, $points)
    $graphics.FillEllipse($cyan, 24, 4, 4, 4)
    $buffer = New-Object System.IO.MemoryStream
    $bitmap.Save($buffer, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames += [pscustomobject]@{ Size = $size; Bytes = $buffer.ToArray() }
    $buffer.Dispose(); $cyan.Dispose(); $neon.Dispose(); $border.Dispose()
    $background.Dispose(); $outline.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
$stream = [System.IO.File]::Create($OutputPath)
$writer = New-Object System.IO.BinaryWriter($stream)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$offset)
        $offset += $frame.Bytes.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
} finally { $writer.Dispose(); $stream.Dispose() }
