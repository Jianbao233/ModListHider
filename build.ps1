param(
    [string]$GodotExe = "K:\杀戮尖塔mod制作\Godot_v4.5.1\Godot_v4.5.1\Godot_v4.5.1-stable_mono_win64.exe"
)
$ErrorActionPreference = "Stop"
$ProjectRoot = $PSScriptRoot
$GameModsDir = "K:\SteamLibrary\steamapps\common\Slay the Spire 2\mods\ModListHider"
$GameModsDirAlt = "C:\Users\Administrator\AppData\Roaming\SlayTheSpire2\mods\ModListHider"
Write-Host "=== ModListHider Mod Build ===" -ForegroundColor Cyan
Write-Host "Project: $ProjectRoot"

# Determine target directory
if (Test-Path $GameModsDir) {
    $TargetDir = $GameModsDir
}
elseif (Test-Path $GameModsDirAlt) {
    $TargetDir = $GameModsDirAlt
}
else {
    if (Test-Path "K:\SteamLibrary\steamapps\common\Slay the Spire 2\mods") {
        $TargetDir = $GameModsDir
        New-Item -ItemType Directory -Force -Path $TargetDir | Out-Null
    }
    else {
        $TargetDir = $GameModsDirAlt
        New-Item -ItemType Directory -Force -Path $TargetDir | Out-Null
    }
}
Write-Host "Target: $TargetDir" -ForegroundColor Cyan

# [1/5] dotnet build
Write-Host "[1/5] dotnet build..." -ForegroundColor Yellow
dotnet build -c Debug --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }
Write-Host "  dotnet build OK" -ForegroundColor Green

# [2/5] Godot export pck
Write-Host "[2/5] Godot export pck..." -ForegroundColor Yellow
$env:GODOT = $GodotExe
& $env:GODOT --headless --path $ProjectRoot --export-pack "Windows Desktop" "$ProjectRoot\build\ModListHider.pck"
if ($LASTEXITCODE -ne 0) { Write-Host "  Warning: Godot export failed (pck may be optional)" -ForegroundColor Yellow }
else { Write-Host "  Godot export OK" -ForegroundColor Green }

# [3/5] Copy DLL
$DllPath = "$ProjectRoot\.godot\mono\temp\bin\Debug\ModListHider.dll"
if (Test-Path $DllPath) {
    Copy-Item -Path $DllPath -Destination "$TargetDir\ModListHider.dll" -Force
    Write-Host "[3/5] Copied ModListHider.dll OK" -ForegroundColor Green
}
else {
    Write-Host "[3/5] Warning: $DllPath not found" -ForegroundColor Yellow
}

# [4/5] Copy PCK and assets
$PckPath = "$ProjectRoot\build\ModListHider.pck"
if (Test-Path $PckPath) {
    Copy-Item -Path $PckPath -Destination "$TargetDir\ModListHider.pck" -Force
    Write-Host "[4/5] Copied ModListHider.pck OK" -ForegroundColor Green
}
else {
    Write-Host "[4/5] Warning: $PckPath not found (icons may not be bundled)" -ForegroundColor Yellow
}

# [5/5] Copy manifest + regenerate with Python
if (Test-Path "$ProjectRoot\mod_manifest.json") {
    Copy-Item -Path "$ProjectRoot\mod_manifest.json" -Destination "$TargetDir\mod_manifest.json" -Force
    python -c "
import json, os
src = r'$TargetDir\mod_manifest.json'
with open(src, 'r', encoding='utf-8') as f:
    data = json.load(f)
with open(src, 'w', encoding='utf-8') as f:
    json.dump(data, f, ensure_ascii=False, indent=2)
"
    Write-Host "[5/5] Copied + regenerated mod_manifest.json OK" -ForegroundColor Green
}

Write-Host ""
Write-Host "Build complete. Target: $TargetDir" -ForegroundColor Green
Write-Host "Please test in game." -ForegroundColor Yellow
