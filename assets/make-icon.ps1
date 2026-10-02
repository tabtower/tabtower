# Generates TabTower.ico — the app icon: a dark deck with a 2x2 grid of workspace
# cards, each carrying a status dot (blue/orange/green/gray = working/waiting/done/idle);
# the first card has the accent-blue border (the "active" card).
# Outputs:
#   assets\TabTower.ico                    — 256/64/48/32/16 px PNG-compressed entries
#   vscode-extension\assets\logo128.png       — the Connector's Marketplace icon (VSCode wants 128x128)
# Both come from the same Draw-Icon, so the app and the extension can never drift apart.
# PowerShell 5.1 compatible. Re-run after tweaking to regenerate both.

Add-Type -AssemblyName System.Drawing

$outPath = Join-Path $PSScriptRoot 'TabTower.ico'
$sizes = 256, 64, 48, 32, 16

function New-RoundedRectPath([double]$x, [double]$y, [double]$w, [double]$h, [double]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    if ($r -le 0.5) {
        $p.AddRectangle((New-Object System.Drawing.RectangleF($x, $y, $w, $h)))
        return $p
    }
    $d = 2 * $r
    $p.AddArc([float]$x, [float]$y, [float]$d, [float]$d, 180, 90)
    $p.AddArc([float]($x + $w - $d), [float]$y, [float]$d, [float]$d, 270, 90)
    $p.AddArc([float]($x + $w - $d), [float]($y + $h - $d), [float]$d, [float]$d, 0, 90)
    $p.AddArc([float]$x, [float]($y + $h - $d), [float]$d, [float]$d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Draw-Icon([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $s = $size / 256.0

    # background: the dark deck
    $bg = New-RoundedRectPath 0 0 $size $size (44 * $s)
    $g.FillPath((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 0x18, 0x18, 0x18))), $bg)

    # 2x2 workspace cards
    $margin = 30 * $s
    $gap = 16 * $s
    $card = ($size - 2 * $margin - $gap) / 2
    $cardBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 0x2E, 0x2E, 0x2E))
    $stripBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 0x58, 0x58, 0x58))
    $strip2Brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 0x44, 0x44, 0x44))
    $accentPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 0x3A, 0x6E, 0xA5), [float][Math]::Max(2.0, 6 * $s))
    $dotColors = @(
        [System.Drawing.Color]::FromArgb(255, 0x2D, 0x7D, 0xD2),   # working — blue
        [System.Drawing.Color]::FromArgb(255, 0xF3, 0x9C, 0x12),   # waiting — orange
        [System.Drawing.Color]::FromArgb(255, 0x2E, 0xCC, 0x71),   # done — green
        [System.Drawing.Color]::FromArgb(255, 0x86, 0x86, 0x86)    # idle — gray
    )

    $i = 0
    foreach ($row in 0, 1) {
        foreach ($col in 0, 1) {
            $x = $margin + $col * ($card + $gap)
            $y = $margin + $row * ($card + $gap)
            $cp = New-RoundedRectPath $x $y $card $card (14 * $s)
            $g.FillPath($cardBrush, $cp)
            if ($i -eq 0 -and $size -ge 32) { $g.DrawPath($accentPen, $cp) }

            # title strips — only when there's room for them to read as lines
            $pad = 12 * $s
            if ($size -ge 48) {
                $g.FillRectangle($stripBrush, [float]($x + $pad), [float]($y + $pad + 2 * $s), [float]($card * 0.45), [float][Math]::Max(2.0, 9 * $s))
                $g.FillRectangle($strip2Brush, [float]($x + $pad), [float]($y + $pad + 18 * $s), [float]($card * 0.30), [float][Math]::Max(1.5, 7 * $s))
            }
            elseif ($size -ge 32) {
                $g.FillRectangle($stripBrush, [float]($x + $pad), [float]($y + $pad), [float]($card * 0.45), [float]2)
            }

            # status dot — bottom-right of the card
            $dotR = [Math]::Max(1.6, 13 * $s)
            $cx = $x + $card - $pad - $dotR
            $cy = $y + $card - $pad - $dotR
            if ($size -le 16) { $cx = $x + $card / 2; $cy = $y + $card / 2; $dotR = 1.8 }
            $dotBrush = New-Object System.Drawing.SolidBrush($dotColors[$i])
            $g.FillEllipse($dotBrush, [float]($cx - $dotR), [float]($cy - $dotR), [float](2 * $dotR), [float](2 * $dotR))
            $dotBrush.Dispose()
            $i++
        }
    }

    $g.Dispose()
    return $bmp
}

# render every size to an in-memory PNG
$entries = @()
foreach ($size in $sizes) {
    $bmp = Draw-Icon $size
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $entries += , @{ Size = $size; Bytes = $ms.ToArray() }
    $ms.Dispose()
}

# pack as .ico (PNG-compressed entries are supported since Vista)
$fs = [System.IO.File]::Create($outPath)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([uint16]0)                # reserved
$bw.Write([uint16]1)                # type: icon
$bw.Write([uint16]$entries.Count)
$offset = 6 + 16 * $entries.Count
foreach ($e in $entries) {
    $dim = 0; if ($e.Size -lt 256) { $dim = $e.Size }   # 0 means 256
    $bw.Write([byte]$dim)           # width
    $bw.Write([byte]$dim)           # height
    $bw.Write([byte]0)              # palette
    $bw.Write([byte]0)              # reserved
    $bw.Write([uint16]1)            # planes
    $bw.Write([uint16]32)           # bpp
    $bw.Write([uint32]$e.Bytes.Length)
    $bw.Write([uint32]$offset)
    $offset += $e.Bytes.Length
}
foreach ($e in $entries) { $bw.Write($e.Bytes) }
$bw.Close()

Write-Host "written: $outPath ($((Get-Item $outPath).Length) bytes, sizes: $($sizes -join ','))"

# the Connector's extension icon - same artwork, plain 128x128 PNG (vsce only packs
# files under vscode-extension\, so it gets its own copy rather than a link)
$pngPath = Join-Path (Split-Path $PSScriptRoot -Parent) 'vscode-extension\assets\logo128.png'
$pngDir = Split-Path $pngPath -Parent
if (-not (Test-Path $pngDir)) { New-Item -ItemType Directory -Path $pngDir | Out-Null }
$png = Draw-Icon 128
$png.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
$png.Dispose()

Write-Host "written: $pngPath ($((Get-Item $pngPath).Length) bytes, 128x128)"
