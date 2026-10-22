# Builds the .mcpb: publishes the single-file exe, puts it next to a manifest and packs both.
#   .\build\empacotar.ps1                          generic bundle (keys typed in Claude Desktop's settings)
#   .\build\empacotar.ps1 -Manifest <path> -Saida <file.mcpb>
# A client bundle (keys built in) lives outside this repo and calls this script with its own manifest.
param(
    [string]$Manifest = (Join-Path $PSScriptRoot "..\bundle\manifest.json"),
    [string]$Saida = (Join-Path $PSScriptRoot "..\artifacts\omie-mcp-readonly.mcpb")
)
$ErrorActionPreference = "Stop"
$raiz = Resolve-Path (Join-Path $PSScriptRoot "..")
$palco = Join-Path $raiz "artifacts\bundle"

if (Test-Path $palco) { Remove-Item $palco -Recurse -Force }
New-Item -ItemType Directory -Force (Join-Path $palco "server") | Out-Null

dotnet publish (Join-Path $raiz "src\OmieMcpReadonly") -c Release -o (Join-Path $raiz "artifacts\server") --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou" }

Copy-Item (Join-Path $raiz "artifacts\server\omie-mcp-readonly.exe") (Join-Path $palco "server")
Copy-Item $Manifest (Join-Path $palco "manifest.json")
# the license and the notices the bundled runtime and packages require go with every bundle
Copy-Item (Join-Path $raiz "LICENSE") $palco
Copy-Item (Join-Path $raiz "notices") (Join-Path $palco "notices") -Recurse
$icone = Join-Path (Split-Path $Manifest) "icon.png"
if (Test-Path $icone) { Copy-Item $icone $palco }

New-Item -ItemType Directory -Force (Split-Path $Saida) | Out-Null
# the official CLI validates the manifest before packing (a .mcpb is a zip with the manifest at the root)
npx -y @anthropic-ai/mcpb@2.1.2 pack $palco $Saida
if ($LASTEXITCODE -ne 0) { throw "mcpb pack falhou" }
Remove-Item $palco -Recurse -Force
Write-Host "Pronto: $Saida"
