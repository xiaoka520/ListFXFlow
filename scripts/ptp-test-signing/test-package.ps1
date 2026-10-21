$ErrorActionPreference='Stop'
. "$PSScriptRoot/Manage-TestCertificate.ps1"
$root=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$source=Join-Path $root 'artifacts/PTP-Driver-TestSigned/ASUF1209-Col02'
$temp=Join-Path ([IO.Path]::GetTempPath()) ('ListFXFlow-PTP-TestSigned-'+[guid]::NewGuid())
New-Item -ItemType Directory -Path $temp | Out-Null
function Reject($script,$label) { $failed=$false;try { & $script | Out-Null } catch { $failed=$true };if(-not $failed) { throw "Unexpected acceptance: $label" } }
$cert=$null
try {
    Copy-Item "$source/*" $temp
    if(@(Get-ChildItem -LiteralPath $source -Recurse -Include '*.pfx','*.p12','*.pvk','*.key').Count) { throw 'Private key material must never be distributed.' }
    $cert=Get-PtpTestCertificate $temp
    if($cert.HasPrivateKey) { throw 'Public certificate unexpectedly has a private key.' }
    foreach($store in @('Root','TrustedPublisher')) {
        if(Test-Path -LiteralPath ("Cert:\LocalMachine\$store\"+$cert.Thumbprint)) { throw 'Test must start without machine trust for this fresh certificate.' }
    }
    Set-PtpTestCertificate $temp Import
    Set-PtpTestCertificate $temp Import # Repeating the explicit import is harmless.
    foreach($store in @('Root','TrustedPublisher')) {
        if(-not(Test-Path -LiteralPath ("Cert:\LocalMachine\$store\"+$cert.Thumbprint))) { throw 'Explicit certificate import did not persist in its intended store.' }
    }
    foreach($file in @('ListFXFlowPtp.sys','ListFXFlowPtp.cat')) {
        $signature=Get-AuthenticodeSignature -LiteralPath (Join-Path $temp $file)
        if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne $cert.Thumbprint) { throw 'Imported certificate did not validate the expected test signature.' }
    }
    foreach($file in @('ListFXFlowPtp.sys','ListFXFlowPtp.inf','ListFXFlowPtp.cat','ListFXFlowPtp-Test.cer')) {
        $path=Join-Path $temp $file;$bytes=[IO.File]::ReadAllBytes($path)
        [IO.File]::AppendAllText($path,'tampered')
        Reject {Get-PtpTestCertificate $temp} ('tampered '+$file)
        [IO.File]::WriteAllBytes($path,$bytes)
    }
    $manifest=Join-Path $temp 'test-signing.json';$original=[IO.File]::ReadAllBytes($manifest)
    $meta=Get-Content -Raw -LiteralPath $manifest | ConvertFrom-Json
    $meta.PrivateKeyIncluded=$true;$meta | ConvertTo-Json | Set-Content -LiteralPath $manifest -Encoding utf8
    Reject {Get-PtpTestCertificate $temp} 'private-key manifest'
    [IO.File]::WriteAllBytes($manifest,$original)
    Set-PtpTestCertificate $temp Remove
    foreach($store in @('Root','TrustedPublisher')) {
        if(Test-Path -LiteralPath ("Cert:\LocalMachine\$store\"+$cert.Thumbprint)) { throw 'Explicit certificate cleanup left trust behind.' }
    }
    Write-Host 'PASS test signatures, public-only certificate, exact trust import/cleanup and tamper rejection. No driver installed or boot policy changed.'
} finally {
    if($cert) {
        foreach($store in @('Root','TrustedPublisher')) {
            $path="Cert:\LocalMachine\$store\"+$cert.Thumbprint
            if(Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
        }
        $cert.Dispose()
    }
    Remove-Item -Recurse -Force -LiteralPath $temp
}
