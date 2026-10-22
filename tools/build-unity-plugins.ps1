# Baut die gemeinsame Bibliothek und kopiert sie samt Content in das Unity-Projekt (Windows-Fassung von build-unity-plugins.sh).
# Vor dem ersten Öffnen von client/Unity ausführen – sonst fehlen den Client-Skripten die DLLs:
#   powershell -ExecutionPolicy Bypass -File tools\build-unity-plugins.ps1
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "build\unity-plugins"
dotnet publish (Join-Path $root "src\EternalRealms.Shared") -c Release -o $out
if ($LASTEXITCODE -ne 0) { throw "dotnet publish fehlgeschlagen (ist das .NET-8-SDK installiert?)" }

$plugins = Join-Path $root "client\Unity\Assets\Plugins\EternalRealms"
New-Item -ItemType Directory -Force -Path $plugins | Out-Null
# Diese Bibliotheken bringt Unitys .NET-Standard-2.1-Profil bereits mit; doppelt führen sie zu Konflikten.
$skip = @("System.Buffers.dll", "System.Memory.dll", "System.Numerics.Vectors.dll", "System.Threading.Tasks.Extensions.dll")
Get-ChildItem -Path $out -Filter *.dll | Where-Object { $skip -notcontains $_.Name } | Copy-Item -Destination $plugins -Force

$target = Join-Path $root "client\Unity\Assets\StreamingAssets\content"
if (Test-Path $target) { Remove-Item -Recurse -Force $target }
Copy-Item -Recurse -Path (Join-Path $root "content") -Destination $target
$count = (Get-ChildItem -Path $plugins -Filter *.dll).Count
Write-Host "Fertig: $count Bibliotheken, Content nach StreamingAssets kopiert."
