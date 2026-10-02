# Renders assets/social-preview.png - the 1280x640 card GitHub unfurls for the repo.
#
# RUN BY HAND, never automatically: nothing in release.ps1 or CI calls this, and it
# captures NOTHING. It only composes a card out of the staged demo screenshot already
# committed at assets/screenshots/deck.png, whose workspaces and sessions are invented.
# No real screen, session or project name can reach the card through this script.
#
# It exists as a script rather than a hand-made image only so that replacing that demo
# screenshot after a UI change is a re-run instead of redoing the layout. GitHub has no
# API for the social preview, so uploading the result stays manual as well
# (Settings -> General -> Social preview).
#
# Deliberately ASCII-only source: PowerShell 5.1 reads a BOM-less .ps1 as ANSI, so the
# separator is built from [char]0xB7 instead of being typed literally.
#
# Palette matches assets/make-icon.ps1 (same product, same card).

param(
    [string]$Screenshot = (Join-Path $PSScriptRoot 'screenshots\deck.png'),
    [string]$Out        = (Join-Path $PSScriptRoot 'social-preview.png')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$W = 1280
$H = 640

$bg      = [System.Drawing.Color]::FromArgb(255, 0x18, 0x18, 0x18)
$accent  = [System.Drawing.Color]::FromArgb(255, 0x3A, 0x6E, 0xA5)
$white   = [System.Drawing.Color]::FromArgb(255, 0xF2, 0xF2, 0xF2)
$body    = [System.Drawing.Color]::FromArgb(255, 0xC0, 0xC0, 0xC0)
$muted   = [System.Drawing.Color]::FromArgb(255, 0x8A, 0x8A, 0x8A)
$edge    = [System.Drawing.Color]::FromArgb(255, 0x3A, 0x3A, 0x3A)

if (-not (Test-Path $Screenshot)) { throw "screenshot not found: $Screenshot" }
$shot = [System.Drawing.Image]::FromFile((Resolve-Path $Screenshot))

$bmp = New-Object System.Drawing.Bitmap($W, $H)
$g   = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode      = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.InterpolationMode  = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.TextRenderingHint  = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$g.Clear($bg)

# --- the screenshot, whole, inside its own pane -------------------------------------
# Fitted rather than cropped or bled off the edge: a half-cut window control reads as a
# rendering accident, and the point of the shot is that it is one window holding several
# projects - cropping it argues against the line next to it.
$paneX = 468; $paneY = 44; $paneW = 776; $paneH = 552
$scale = [Math]::Min($paneW / $shot.Width, $paneH / $shot.Height)
$shotW = [int]($shot.Width * $scale)
$shotH = [int]($shot.Height * $scale)
$shotX = [int]($paneX + ($paneW - $shotW) / 2)
$shotY = [int](($H - $shotH) / 2)
$g.DrawImage($shot, $shotX, $shotY, $shotW, $shotH)
$pen = New-Object System.Drawing.Pen($edge, 1)
$g.DrawRectangle($pen, $shotX, $shotY, $shotW, $shotH)
$pen.Dispose()

# --- text panel ---------------------------------------------------------------------
$fTitle = New-Object System.Drawing.Font('Segoe UI', 46, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$fLead  = New-Object System.Drawing.Font('Segoe UI', 25, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
$fFoot  = New-Object System.Drawing.Font('Segoe UI', 19, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)

$bTitle = New-Object System.Drawing.SolidBrush($white)
$bBody  = New-Object System.Drawing.SolidBrush($body)
$bMuted = New-Object System.Drawing.SolidBrush($muted)
$bAcc   = New-Object System.Drawing.SolidBrush($accent)

$x = 64
$y = 192
$g.DrawString('TabTower', $fTitle, $bTitle, [float]$x, [float]$y)
$y += 78

foreach ($line in @('Several Claude Code sessions.', 'Several projects.', 'One deck.')) {
    $g.DrawString($line, $fLead, $bBody, [float]$x, [float]$y)
    $y += 36
}

$y += 26
$g.FillRectangle($bAcc, [float]$x, [float]$y, [float]104, [float]3)
$y += 24

$dot = [string][char]0x00B7
$g.DrawString("Windows  $dot  VS Code  $dot  Claude Code", $fFoot, $bMuted, [float]$x, [float]$y)

$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)

foreach ($d in @($fTitle, $fLead, $fFoot, $bTitle, $bBody, $bMuted, $bAcc, $g, $bmp, $shot)) { $d.Dispose() }
Write-Host "wrote $Out (${W}x${H})"
