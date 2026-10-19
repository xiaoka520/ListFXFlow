$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$version = (Get-Content (Join-Path $root 'VERSION')).Trim()
$build = (Get-Content (Join-Path $root 'BUILD')).Trim()
$packageTag = "$version-b$build"
$appProject = Join-Path $root 'windows/ListFXFlow/ListFXFlow.csproj'
$setupProject = Join-Path $root 'windows/ListFXFlow.Setup/ListFXFlow.Setup.csproj'
$output = Join-Path $root 'artifacts/Windows-x64'
$packaging = Join-Path $root 'artifacts/Packaging'
$setupOutput = Join-Path $root 'artifacts/Windows-Setup'
$portable = Join-Path $root "artifacts/ListFXFlow-Windows-x64-$packageTag.zip"
$installer = Join-Path $root "artifacts/ListFXFlow-Setup-x64-$packageTag.exe"

foreach ($path in @($output, $packaging, $setupOutput)) {
    if (Test-Path $path) { Remove-Item $path -Recurse -Force }
    New-Item -ItemType Directory -Path $path | Out-Null
}

dotnet publish $appProject -c Release -r win-x64 --self-contained true `
    "-p:PublishSingleFile=true" "-p:IncludeNativeLibrariesForSelfExtract=true" `
    "-p:Version=$version" "-p:FileVersion=$version.$build" "-p:AssemblyVersion=$version.0" `
    -o $output
if ($LASTEXITCODE -ne 0) { throw 'Windows app build failed' }

Remove-Item (Join-Path $output '*.pdb') -Force -ErrorAction SilentlyContinue
Copy-Item (Join-Path $root 'docs/Windows-QuickStart.txt') (Join-Path $output 'Windows-QuickStart.txt')
Copy-Item (Join-Path $root 'THIRD_PARTY_NOTICES.txt') (Join-Path $output 'THIRD_PARTY_NOTICES.txt')
if (Test-Path $portable) { Remove-Item $portable -Force }
Compress-Archive -Path "$output/*" -DestinationPath $portable -CompressionLevel Optimal

# The installer embeds exactly one self-contained executable and verifies its size
# and SHA-256 before replacing an existing per-user installation.
$exe = Join-Path $output 'ListFXFlow.exe'
if (-not (Test-Path $exe)) { throw 'Publish did not produce ListFXFlow.exe' }
$payloadZip = Join-Path $packaging 'payload.zip'
$payloadJson = Join-Path $packaging 'payload.json'
Compress-Archive -Path $exe -DestinationPath $payloadZip -CompressionLevel Optimal
$size = (Get-Item $exe).Length
$hash = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLowerInvariant()
$manifestJson = @{ version = "$version.$build"; size = $size; sha256 = $hash } | ConvertTo-Json -Compress
[System.IO.File]::WriteAllText($payloadJson, $manifestJson, (New-Object System.Text.UTF8Encoding($false)))

dotnet publish $setupProject -c Release -r win-x64 --self-contained true `
    "-p:PublishSingleFile=true" "-p:IncludeNativeLibrariesForSelfExtract=true" `
    "-p:EnableCompressionInSingleFile=true" `
    "-p:PayloadPath=$payloadZip" "-p:PayloadManifestPath=$payloadJson" `
    "-p:Version=$version" "-p:FileVersion=$version.$build" "-p:AssemblyVersion=$version.0" `
    -o $setupOutput
if ($LASTEXITCODE -ne 0) { throw 'Windows installer build failed' }

$setupExe = Join-Path $setupOutput 'ListFXFlow.Setup.exe'
if (-not (Test-Path $setupExe)) { throw 'Publish did not produce ListFXFlow.Setup.exe' }
if (Test-Path $installer) { Remove-Item $installer -Force }
Copy-Item $setupExe $installer
Copy-Item (Join-Path $root 'docs/Windows-QuickStart.txt') (Join-Path $root "artifacts/Windows-Setup-README.txt") -Force

$sums = @()
$sums += (Get-FileHash $installer -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + (Split-Path $installer -Leaf)
$sums += (Get-FileHash $portable -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + (Split-Path $portable -Leaf)
$sums | Set-Content -Path (Join-Path $root 'artifacts/SHA256SUMS-windows.txt') -Encoding ascii
Write-Host "Built $installer"
Write-Host "Built $portable"
