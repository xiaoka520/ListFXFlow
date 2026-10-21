$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    nuget restore windows/Drivers/ListFXFlowPtp/packages.config -PackagesDirectory .build/driver-packages -NonInteractive
    if ($LASTEXITCODE) { throw 'WDK restore failed' }
    msbuild windows/Drivers/ListFXFlowPtp/ListFXFlowPtp.vcxproj /m /p:Configuration=Release /p:Platform=x64 /p:SignMode=Off
    if ($LASTEXITCODE) { throw 'PTP observer driver build failed' }
    $out = Join-Path $root 'artifacts/PTP-Driver-Experimental'
    # Keep repeat builds clean without discarding a previous developer package.
    # In particular, never leave an old catalog alongside a rebuilt .sys/INF.
    if(Test-Path -LiteralPath $out) {
        Move-Item -LiteralPath $out -Destination (Join-Path $root ('.build/ptp-artifact-previous-'+[guid]::NewGuid()))
    }
    New-Item -ItemType Directory -Force $out | Out-Null
    Copy-Item .build/ptp-driver/ListFXFlowPtp.sys $out
    Copy-Item windows/Drivers/ListFXFlowPtp/ListFXFlowPtp.inf.in $out
    Copy-Item scripts/Prepare-PtpDriver.ps1 $out
    Copy-Item docs/PTP-DRIVER.md (Join-Path $out 'README.md')
    Copy-Item LICENSE,THIRD_PARTY_NOTICES.txt $out
    # A model-level profile from device diagnostics, with no instance/serial data.
    $profile=Join-Path $out 'Profiles/ASUF1209-Col02'
    & "$PSScriptRoot/Prepare-PtpDriver.ps1" -DiagnosticsFile windows/Drivers/ListFXFlowPtp/Profiles/ASUF1209-Col02.json -PackageDirectory $out -OutputDirectory $profile
    $verifier=Join-Path $root '.build/driver-packages/Microsoft.Windows.WDK.x64.10.0.28000.2526/c/tools/10.0.28000.0/x64/infverif.exe'
    & $verifier /w /v (Join-Path $profile 'ListFXFlowPtp.inf')
    if($LASTEXITCODE) { throw 'ASUF1209 collection-specific INF validation failed' }
    Write-Host 'Built unsigned read-only observer; no device was modified or driver installed.'
} finally { Pop-Location }
