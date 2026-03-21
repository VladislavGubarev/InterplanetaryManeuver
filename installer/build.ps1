param(
  [ValidateSet('Debug','Release')]
  [string]$Configuration = 'Release',
  [switch]$SkipInno
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $root 'InterplanetaryManeuver.App\InterplanetaryManeuver.App.csproj'
$publishDir = Join-Path $root 'dist\publish'
$installerDir = Join-Path $root 'dist\installer'
$iss = Join-Path $PSScriptRoot 'setup.iss'
$brandingDir = Join-Path $root 'assets\branding'
$setupIco = Join-Path $brandingDir 'setup.ico'
$wizardBmp = Join-Path $brandingDir 'wizard.bmp'
$wizardSmallBmp = Join-Path $brandingDir 'wizard_small.bmp'

New-Item -ItemType Directory -Force -Path $publishDir | Out-Null
New-Item -ItemType Directory -Force -Path $installerDir | Out-Null
New-Item -ItemType Directory -Force -Path $brandingDir | Out-Null

function Ensure-Utf8Bom([string]$Path) {
  [byte[]]$bytes = [System.IO.File]::ReadAllBytes($Path)
  if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
    return
  }

  # setup.iss is UTF-8, but Inno detects UTF-8 reliably only with BOM.
  $text = [System.Text.Encoding]::UTF8.GetString($bytes)
  $utf8Bom = New-Object System.Text.UTF8Encoding($true) # BOM = true
  [System.IO.File]::WriteAllText($Path, $text, $utf8Bom)
}

function Write-IcoFromPngBytes([byte[]]$PngBytes, [string]$OutPath) {
  # ICO with a single 256x256 PNG image (width/height = 0 in dir entry means 256)
  $ms = New-Object System.IO.MemoryStream
  try {
    $bw = New-Object System.IO.BinaryWriter($ms)
    try {
      $bw.Write([UInt16]0) # reserved
      $bw.Write([UInt16]1) # type = icon
      $bw.Write([UInt16]1) # count

      $bw.Write([Byte]0)   # width 256
      $bw.Write([Byte]0)   # height 256
      $bw.Write([Byte]0)   # colors
      $bw.Write([Byte]0)   # reserved
      $bw.Write([UInt16]1) # planes
      $bw.Write([UInt16]32) # bpp
      $bw.Write([UInt32]$PngBytes.Length) # bytes in res
      $bw.Write([UInt32](6 + 16)) # image offset

      $bw.Write($PngBytes)
      $bw.Flush()
      [System.IO.File]::WriteAllBytes($OutPath, $ms.ToArray())
    } finally {
      $bw.Dispose()
    }
  } finally {
    $ms.Dispose()
  }
}

function New-BrandingAssets {
  Add-Type -AssemblyName System.Drawing

  $bgA = [System.Drawing.Color]::FromArgb(255, 8, 12, 20)
  $bgB = [System.Drawing.Color]::FromArgb(255, 18, 32, 58)
  $accent = [System.Drawing.Color]::FromArgb(255, 120, 210, 255)
  $muted = [System.Drawing.Color]::FromArgb(255, 110, 140, 190)

  function New-Gradient([int]$w, [int]$h) {
    $bmp = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb
    )
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $rect = New-Object System.Drawing.Rectangle(0, 0, $w, $h)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $bgA, $bgB, 70.0)
    try {
      $g.FillRectangle($brush, $rect)
    } finally {
      $brush.Dispose()
      $g.Dispose()
    }
    return $bmp
  }

  # wizard.bmp (left image, 164x314 recommended by Inno)
  $wb = New-Gradient 164 314
  try {
    $g = [System.Drawing.Graphics]::FromImage($wb)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    try {
      $penOrbit = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(180, $muted), 2.0)
      $penAccent = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(220, $accent), 2.5)
      $penGrid = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(40, 255, 255, 255), 1.0)
      try {
        for ($y = 0; $y -lt 314; $y += 22) { $g.DrawLine($penGrid, 0, $y, 164, $y) }
        for ($x = 0; $x -lt 164; $x += 22) { $g.DrawLine($penGrid, $x, 0, $x, 314) }

        $g.DrawArc($penOrbit, -120, 40, 280, 280, 210, 210)
        $g.DrawArc($penOrbit, -85, 75, 210, 210, 210, 210)
        $g.DrawArc($penAccent, -55, 110, 150, 150, 210, 210)

        $sunBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 252, 210, 90))
        $jupBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 255, 185, 120))
        $satBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 255, 205, 160))
        $ringPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(160, 230, 230, 230), 2.0)
        try {
          $g.FillEllipse($sunBrush, 18, 212, 26, 26)
          $g.FillEllipse($jupBrush, 92, 176, 18, 18)
          $g.FillEllipse($satBrush, 128, 134, 16, 16)
          $g.DrawEllipse($ringPen, 122, 130, 28, 22)
        } finally {
          $sunBrush.Dispose()
          $jupBrush.Dispose()
          $satBrush.Dispose()
          $ringPen.Dispose()
        }

        $fontTitle = New-Object System.Drawing.Font('Segoe UI Semibold', 12.0, [System.Drawing.FontStyle]::Bold)
        $fontSub = New-Object System.Drawing.Font('Segoe UI', 8.8, [System.Drawing.FontStyle]::Regular)
        $textBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(235, 245, 250, 255))
        $subBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(180, 210, 225, 245))
        try {
          # Keep ASCII here to avoid encoding-related parsing issues in Windows PowerShell.
          $g.DrawString('Interplanetary', $fontTitle, $textBrush, 14, 18)
          $g.DrawString('Maneuver', $fontTitle, $textBrush, 14, 38)
          $g.DrawString('RK-45 / gravity / optimization', $fontSub, $subBrush, 14, 66)
        } finally {
          $fontTitle.Dispose()
          $fontSub.Dispose()
          $textBrush.Dispose()
          $subBrush.Dispose()
        }
      } finally {
        $penOrbit.Dispose()
        $penAccent.Dispose()
        $penGrid.Dispose()
      }
    } finally {
      $g.Dispose()
    }

    $wb.Save($wizardBmp, [System.Drawing.Imaging.ImageFormat]::Bmp)
  } finally {
    $wb.Dispose()
  }

  # wizard_small.bmp (55x58 recommended by Inno)
  $ws = New-Gradient 55 58
  try {
    $g = [System.Drawing.Graphics]::FromImage($ws)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    try {
      $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(220, $accent), 2.0)
      $pen2 = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(140, $muted), 2.0)
      $sunBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 252, 210, 90))
      try {
        $g.DrawArc($pen2, -18, 2, 70, 70, 215, 210)
        $g.DrawArc($pen, -6, 14, 46, 46, 215, 210)
        $g.FillEllipse($sunBrush, 8, 32, 10, 10)
      } finally {
        $pen.Dispose()
        $pen2.Dispose()
        $sunBrush.Dispose()
      }
    } finally {
      $g.Dispose()
    }

    $ws.Save($wizardSmallBmp, [System.Drawing.Imaging.ImageFormat]::Bmp)
  } finally {
    $ws.Dispose()
  }

  # setup.ico
  $ib = New-Gradient 256 256
  try {
    $g = [System.Drawing.Graphics]::FromImage($ib)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    try {
      $penOrbit = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(210, $accent), 10.0)
      $penOrbit.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
      $penOrbit.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
      $penMuted = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(120, $muted), 8.0)
      $penMuted.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
      $penMuted.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
      $sunBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 252, 210, 90))
      $dotBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 255, 185, 120))
      try {
        $g.DrawArc($penMuted, -70, 30, 340, 340, 210, 215)
        $g.DrawArc($penOrbit, -30, 70, 260, 260, 210, 215)
        $g.FillEllipse($sunBrush, 44, 168, 44, 44)
        $g.FillEllipse($dotBrush, 178, 116, 22, 22)
      } finally {
        $penOrbit.Dispose()
        $penMuted.Dispose()
        $sunBrush.Dispose()
        $dotBrush.Dispose()
      }
    } finally {
      $g.Dispose()
    }

    $pngMs = New-Object System.IO.MemoryStream
    try {
      $ib.Save($pngMs, [System.Drawing.Imaging.ImageFormat]::Png)
      Write-IcoFromPngBytes -PngBytes $pngMs.ToArray() -OutPath $setupIco
    } finally {
      $pngMs.Dispose()
    }
  } finally {
    $ib.Dispose()
  }
}

Ensure-Utf8Bom $iss
New-BrandingAssets

# Avoid file-lock build failures if the app is running.
Get-Process InterplanetaryManeuver.App -ErrorAction SilentlyContinue | Stop-Process -Force

Write-Host "Publishing to: $publishDir"
dotnet publish $appProject `
  -c $Configuration `
  -r win-x64 `
  --self-contained true `
  /p:PublishSingleFile=true `
  /p:IncludeNativeLibrariesForSelfExtract=true `
  /p:PublishReadyToRun=true `
  /p:PublishTrimmed=false `
  -o $publishDir

$publishDirResolved = (Resolve-Path $publishDir).Path
Write-Host "Publish output: $publishDirResolved"

if ($SkipInno) {
  Write-Host "SkipInno specified: not building Inno Setup installer."
  exit 0
}

$isccPath = $null
$iscc = Get-Command ISCC.exe -ErrorAction SilentlyContinue
if ($iscc) { $isccPath = $iscc.Source }

if (-not $isccPath) {
  $default = "${env:ProgramFiles(x86)}\\Inno Setup 6\\ISCC.exe"
  if (Test-Path $default) { $isccPath = $default }
}

if (-not $isccPath) {
  $default = "${env:ProgramFiles}\\Inno Setup 6\\ISCC.exe"
  if (Test-Path $default) { $isccPath = $default }
}

if (-not $isccPath) {
  Write-Warning "Inno Setup compiler (ISCC.exe) not found. Install Inno Setup 6 (or add ISCC.exe to PATH), then run this script again."
  Write-Host "Script to compile: $iss"
  exit 0
}

Write-Host "Building installer via ISCC..."
Write-Host "Using ISCC: $isccPath"
& $isccPath $iss | Out-Host
Write-Host "Installer output: $installerDir"

