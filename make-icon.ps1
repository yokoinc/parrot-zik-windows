# Genere ZikTray.ico : carre arrondi orange avec un casque blanc (glyphe des polices d'icones Windows).
Add-Type -AssemblyName System.Drawing
$out = Join-Path $PSScriptRoot "ZikTray.ico"
$sizes = 16, 24, 32, 48, 64, 128, 256

$fonts = (New-Object System.Drawing.Text.InstalledFontCollection).Families | ForEach-Object { $_.Name }
$fontName = if ($fonts -contains "Segoe Fluent Icons") { "Segoe Fluent Icons" } else { "Segoe MDL2 Assets" }
$glyph = [string][char]0xE7F6  # Headphone

$images = foreach ($size in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = "AntiAlias"
    $g.TextRenderingHint = "AntiAliasGridFit"

    $r = [single]($size * 0.22)
    $d = 2 * $r
    $m = [single]($size / 32.0)
    $w = [single]($size - 1 - 2 * $m)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($m, $m, $d, $d, 180, 90)
    $path.AddArc($m + $w - $d, $m, $d, $d, 270, 90)
    $path.AddArc($m + $w - $d, $m + $w - $d, $d, $d, 0, 90)
    $path.AddArc($m, $m + $w - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF 0, $size), ([System.Drawing.Color]::FromArgb(255, 255, 128, 48)), ([System.Drawing.Color]::FromArgb(255, 232, 84, 10))
    $g.FillPath($brush, $path)

    $font = New-Object System.Drawing.Font $fontName, ([single]($size * 0.62)), ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
    $format = New-Object System.Drawing.StringFormat
    $format.Alignment = "Center"
    $format.LineAlignment = "Center"
    $rect = New-Object System.Drawing.RectangleF 0, ([single]($size * 0.03)), $size, $size
    $g.DrawString($glyph, $font, [System.Drawing.Brushes]::White, $rect, $format)
    $g.Dispose()

    # PNG pour 256 px, bitmap classique (DIB 32 bits) en dessous : lisible par tous les lecteurs d'icones
    $ms = New-Object System.IO.MemoryStream
    if ($size -ge 256) {
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    } else {
        $w2 = New-Object System.IO.BinaryWriter $ms
        $w2.Write([uint32]40); $w2.Write([int32]$size); $w2.Write([int32]($size * 2))
        $w2.Write([uint16]1); $w2.Write([uint16]32); $w2.Write([uint32]0)
        $w2.Write([uint32]0); $w2.Write([int32]0); $w2.Write([int32]0); $w2.Write([uint32]0); $w2.Write([uint32]0)
        for ($y = $size - 1; $y -ge 0; $y--) {
            for ($x = 0; $x -lt $size; $x++) {
                $c = $bmp.GetPixel($x, $y)
                $w2.Write([byte]$c.B); $w2.Write([byte]$c.G); $w2.Write([byte]$c.R); $w2.Write([byte]$c.A)
            }
        }
        $maskRow = [int]([math]::Ceiling($size / 32.0) * 4)
        $w2.Write((New-Object byte[] ($maskRow * $size)))
        $w2.Flush()
    }
    $bmp.Dispose()
    ,$ms.ToArray()
}

# Fichier ICO : en-tete, une entree par taille, puis les images
$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $images[$i].Length
    $bw.Write([byte]($s % 256)); $bw.Write([byte]($s % 256)); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$len); $bw.Write([uint32]$offset)
    $offset += $len
}
foreach ($img in $images) { $bw.Write($img) }
$bw.Close()
"Icone : $out ($fontName)"
