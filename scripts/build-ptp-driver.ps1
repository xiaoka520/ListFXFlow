$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    nuget restore windows/Drivers/ListFXFlowPtp/packages.config -PackagesDirectory .build/driver-packages -NonInteractive
    if ($LASTEXITCODE) { throw 'WDK restore failed' }
    msbuild windows/Drivers/ListFXFlowPtp/ListFXFlowPtp.vcxproj /m /p:Configuration=Release /p:Platform=x64 /p:SignMode=Off
    if ($LASTEXITCODE) { throw 'PTP observer driver build failed' }
    $out = Join-Path $root 'artifacts/PTP-Driver-Experimental'
    New-Item -ItemType Directory -Force $out | Out-Null
    Copy-Item .build/ptp-driver/ListFXFlowPtp.sys $out
    Copy-Item windows/Drivers/ListFXFlowPtp/ListFXFlowPtp.inf.in $out
    Copy-Item scripts/Prepare-PtpDriver.ps1 $out
    Copy-Item docs/PTP-DRIVER.md (Join-Path $out 'README.md')
    Copy-Item LICENSE,THIRD_PARTY_NOTICES.txt $out
    Write-Host 'Built unsigned read-only observer; no device was modified or driver installed.'
} finally { Pop-Location }
