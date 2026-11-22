<#
  Builds an MSIX of Crest so it gets package identity (needed only for notification / call mirroring).
  Requirements: .NET 8 SDK and the Windows SDK (makeappx.exe, signtool.exe).

  Usage:
    .\packaging\build-msix.ps1 -Publisher "CN=Your Name" [-PfxPath my.pfx -PfxPassword (Read-Host -AsSecureString)]

  Without -PfxPath the package is built unsigned. Windows only installs signed packages, so either sign
  with your own certificate (and trust it under Local Machine > Trusted People) or use Developer Mode with
  `Add-AppxPackage -Register .\packaging\out\layout\AppxManifest.xml` for a loose-file install.
#>
param(
  [string]$Publisher = "CN=Crest Dev",
  [string]$PfxPath,
  [SecureString]$PfxPassword
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $PSScriptRoot "out"
$layout = Join-Path $out "layout"
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
New-Item $layout -ItemType Directory | Out-Null

dotnet publish (Join-Path $root "src\Crest\Crest.csproj") -c Release -r win-x64 --self-contained true -o $layout

[xml]$csproj = Get-Content (Join-Path $root "src\Crest\Crest.csproj")
$ver = ($csproj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
$manifest = (Get-Content (Join-Path $PSScriptRoot "Package.appxmanifest") -Raw) `
  -replace 'Publisher="CN=Crest Dev"', ('Publisher="' + $Publisher + '"') `
  -replace 'Version="\d+\.\d+\.\d+\.\d+"', ('Version="' + $ver + '.0"')
Set-Content (Join-Path $layout "AppxManifest.xml") $manifest -Encoding UTF8

# Tile images generated from the app icon
Add-Type -AssemblyName System.Drawing
$assets = Join-Path $layout "Assets"
New-Item $assets -ItemType Directory -Force | Out-Null
$icon = New-Object System.Drawing.Icon (Join-Path $root "src\Crest\Assets\crest.ico"), 256, 256
foreach ($a in @(@("StoreLogo", 50), @("Square44x44Logo", 44), @("Square150x150Logo", 150))) {
  $bmp = New-Object System.Drawing.Bitmap $a[1], $a[1]
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.InterpolationMode = "HighQualityBicubic"
  $g.DrawImage($icon.ToBitmap(), 0, 0, $a[1], $a[1])
  $bmp.Save((Join-Path $assets ($a[0] + ".png")), [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
}

$sdk = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\makeappx.exe" | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $sdk) { throw "makeappx.exe not found. Install the Windows 10/11 SDK." }
$msix = Join-Path $out "Crest-$ver-x64.msix"
& $sdk.FullName pack /d $layout /p $msix /o
if ($PfxPath) {
  $signtool = Join-Path $sdk.DirectoryName "signtool.exe"
  $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($PfxPassword))
  & $signtool sign /fd SHA256 /f $PfxPath /p $plain $msix
}
Write-Host "Built $msix"
