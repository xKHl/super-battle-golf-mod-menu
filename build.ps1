<#
.SYNOPSIS
  Builds SbgMod.dll against a local Super Battle Golf + BepInEx 5 installation.
.EXAMPLE
  .\build.ps1
  .\build.ps1 -GameDir "D:\SteamLibrary\steamapps\common\Super Battle Golf" -Install
  .\build.ps1 -Package
#>
param(
    [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\Super Battle Golf",
    [switch]$Install,   # copy the DLL into <GameDir>\BepInEx\plugins\SbgMod (game must be closed)
    [switch]$Package    # create release\v<version>\SbgModMenu-v<version>.zip and its SHA-256 file
)
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

if (-not (Test-Path "$GameDir\Super Battle Golf_Data\Managed\GameAssembly.dll")) { throw "Game not found at '$GameDir'. Pass -GameDir." }
if (-not (Test-Path "$GameDir\BepInEx\core\BepInEx.dll")) { throw "BepInEx 5 not found in '$GameDir'. Install it first." }

dotnet build "$root\SbgMod.csproj" -c Release -nologo "-p:GameDir=$GameDir"
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

$dll = "$root\bin\SbgMod.dll"
$version = (Select-String -Path "$root\src\Plugin.cs" -Pattern 'Version = "([^"]+)"').Matches[0].Groups[1].Value
"Built $dll (v$version)  SHA-256 " + (Get-FileHash $dll -Algorithm SHA256).Hash.ToLower()

if ($Install) {
    if (Get-Process -Name "Super Battle Golf" -ErrorAction SilentlyContinue) { throw "Close the game before installing." }
    $target = "$GameDir\BepInEx\plugins\SbgMod"
    New-Item -ItemType Directory -Force $target | Out-Null
    Copy-Item $dll $target -Force
    "Installed to $target"
}

if ($Package) {
    $out = "$root\release\v$version"
    $stage = "$out\stage"
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    New-Item -ItemType Directory -Force "$stage\BepInEx\plugins\SbgMod" | Out-Null
    Copy-Item $dll "$stage\BepInEx\plugins\SbgMod\"
    Copy-Item "$root\packaging\INSTALL.txt" "$stage\SbgModMenu-INSTALL.txt"
    $zip = "$out\SbgModMenu-v$version.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path "$stage\*" -DestinationPath $zip
    Remove-Item $stage -Recurse -Force
    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
    "$hash  SbgModMenu-v$version.zip" | Out-File "$zip.sha256" -Encoding ascii
    "Packaged $zip"
    "SHA-256 $hash"
}
