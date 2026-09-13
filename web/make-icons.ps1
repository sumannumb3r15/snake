Add-Type -AssemblyName System.Drawing
$out = Split-Path -Parent $MyInvocation.MyCommand.Path

function RoundRect($x, $y, $w, $h, $r) {
  $p = New-Object System.Drawing.Drawing2D.GraphicsPath
  $d = $r * 2
  $p.AddArc($x, $y, $d, $d, 180, 90)
  $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
  $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
  $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
  $p.CloseFigure()
  $p
}

function DrawIcon([int]$S, [bool]$Maskable, [string]$file) {
  $bmp = New-Object System.Drawing.Bitmap $S, $S
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'
  $g.Clear([System.Drawing.Color]::Transparent)

  # maskable icons get trimmed to a circle, so keep the art well inside
  $pad = if ($Maskable) { [int]($S * 0.20) } else { [int]($S * 0.03) }
  $bg = RoundRect 0 0 $S $S ([int]($S * 0.22))
  $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#0A1220'))), $bg)

  # a few stars
  $rnd = New-Object System.Random 99
  $sb = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(150, 200, 216, 255))
  for ($i = 0; $i -lt 26; $i++) {
    $sz = [Math]::Max(1, $S * 0.012)
    $g.FillRectangle($sb, $rnd.Next($pad, $S - $pad), $rnd.Next($pad, $S - $pad), $sz, $sz)
  }

  # snake: a run of beads curving up to the right, with an apple ahead
  $inner = $S - $pad * 2
  $cells = @(@(0.10,0.72), @(0.26,0.70), @(0.41,0.63), @(0.53,0.52), @(0.60,0.38))
  $tail = [System.Drawing.ColorTranslator]::FromHtml('#0A6B48')
  $head = [System.Drawing.ColorTranslator]::FromHtml('#86EFAC')
  for ($i = 0; $i -lt $cells.Count; $i++) {
    $t = $i / ($cells.Count - 1)
    $c = [System.Drawing.Color]::FromArgb(
           [int]($tail.R + ($head.R - $tail.R) * $t),
           [int]($tail.G + ($head.G - $tail.G) * $t),
           [int]($tail.B + ($head.B - $tail.B) * $t))
    $r = $inner * (0.085 + 0.035 * $t)
    $cx = $pad + $inner * $cells[$i][0]
    $cy = $pad + $inner * $cells[$i][1]
    $g.FillEllipse((New-Object System.Drawing.SolidBrush $c), ($cx - $r), ($cy - $r), ($r*2), ($r*2))
  }

  $ar = $inner * 0.10
  $ax = $pad + $inner * 0.76
  $ay = $pad + $inner * 0.25
  $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#F05252'))),
                 ($ax - $ar), ($ay - $ar), ($ar*2), ($ar*2))

  $g.Dispose()
  $bmp.Save((Join-Path $out $file), [System.Drawing.Imaging.ImageFormat]::Png)
  $bmp.Dispose()
  "  $file"
}

DrawIcon 192 $false 'icon-192.png'
DrawIcon 512 $false 'icon-512.png'
DrawIcon 512 $true  'icon-512-maskable.png'
"icons written to $out"
