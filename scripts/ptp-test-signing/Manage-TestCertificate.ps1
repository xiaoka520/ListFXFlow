param([ValidateSet('Import','Remove')][string]$Action='Import')
$ErrorActionPreference='Stop'

function Get-PtpTestCertificate([string]$Directory,[switch]$SkipPayload) {
    $meta=Get-Content -Raw -LiteralPath (Join-Path $Directory 'test-signing.json') | ConvertFrom-Json
    if($meta.Schema -ne 1 -or $meta.SigningMode -ne 'self-signed-test' -or -not $meta.RequiresTestSigning -or $meta.PrivateKeyIncluded -or
       $meta.CertificateFile -cne 'ListFXFlowPtp-Test.cer' -or $meta.CertificateThumbprint -notmatch '^[a-fA-F0-9]{40}$') { throw 'Invalid test-signing manifest.' }
    $cer=Join-Path $Directory 'ListFXFlowPtp-Test.cer'
    if((Get-FileHash -LiteralPath $cer -Algorithm SHA256).Hash -ne $meta.CertificateSHA256) { throw 'Test certificate file hash mismatch.' }
    $cert=New-Object Security.Cryptography.X509Certificates.X509Certificate2($cer)
    if($cert.HasPrivateKey -or $cert.Thumbprint -ne $meta.CertificateThumbprint -or $cert.Subject -cne $meta.CertificateSubject -or
       $cert.Issuer -ne $cert.Subject -or $cert.Subject -notmatch '^CN=ListFXFlow PTP Test Only [a-f0-9]{32}$' -or
       ($cert.NotAfter-$cert.NotBefore).TotalDays -gt 100) { throw 'Unexpected test certificate identity.' }
    $eku=@($cert.Extensions | Where-Object {$_.Oid.Value -eq '2.5.29.37'})
    if($eku.Count -ne 1 -or @($eku[0].EnhancedKeyUsages).Count -ne 1 -or $eku[0].EnhancedKeyUsages[0].Value -ne '1.3.6.1.5.5.7.3.3') { throw 'Certificate must be restricted to code signing.' }
    if(-not $SkipPayload) {
        if((Get-Date) -lt $cert.NotBefore -or (Get-Date) -gt $cert.NotAfter) { throw 'Test certificate is outside its validity period.' }
        foreach($entry in @(@('ListFXFlowPtp.sys','DriverSHA256'),@('ListFXFlowPtp.inf','InfSHA256'),@('ListFXFlowPtp.cat','CatalogSHA256'))) {
            if((Get-FileHash -LiteralPath (Join-Path $Directory $entry[0]) -Algorithm SHA256).Hash -ne $meta.($entry[1])) { throw ('Test payload hash mismatch: '+$entry[0]) }
        }
        foreach($name in @('ListFXFlowPtp.sys','ListFXFlowPtp.cat')) {
            $signature=Get-AuthenticodeSignature -LiteralPath (Join-Path $Directory $name)
            if($signature.SignerCertificate.Thumbprint -ne $cert.Thumbprint -or $signature.Status -notin @('Valid','NotTrusted')) { throw ('Unexpected test signature: '+$name+' / '+$signature.Status) }
        }
    }
    return $cert
}
function Set-PtpTestCertificate([string]$Directory,[string]$Mode) {
    $principal=New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if(-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run the certificate launcher as administrator.' }
    $cert=Get-PtpTestCertificate $Directory -SkipPayload:($Mode -eq 'Remove')
    try {
        if($Mode -eq 'Remove') {
            $installed=@(Get-WindowsDriver -Online -All | Where-Object {$_.ProviderName -eq 'ListFXFlow' -and [IO.Path]::GetFileName($_.OriginalFileName) -eq 'ListFXFlowPtp.inf'})
            if($installed.Count) { throw 'Uninstall the ListFXFlow PTP package with Rollback.cmd before removing its trust certificate.' }
        }
        foreach($store in @('Root','TrustedPublisher')) {
            $location='Cert:\LocalMachine\'+$store
            $path=$location+'\'+$cert.Thumbprint
            if($Mode -eq 'Import') {
                if(-not(Test-Path -LiteralPath $path)) { Import-Certificate -FilePath (Join-Path $Directory 'ListFXFlowPtp-Test.cer') -CertStoreLocation $location | Out-Null }
                Write-Host ('Test certificate trusted in LocalMachine/'+$store+': '+$cert.Thumbprint)
            } elseif(Test-Path -LiteralPath $path) {
                $existing=Get-Item -LiteralPath $path
                if($existing.Subject -cne $cert.Subject) { throw 'Certificate identity mismatch; nothing removed from this store.' }
                Remove-Item -LiteralPath $path -Force
                Write-Host ('Removed this package test certificate from LocalMachine/'+$store)
            }
        }
        if($Mode -eq 'Import') { Write-Host 'Certificate imported. Test-signing boot configuration and restart are separate steps; no boot settings changed.' }
    } finally { $cert.Dispose() }
}
if($MyInvocation.InvocationName -ne '.') {
    try { Set-PtpTestCertificate $PSScriptRoot $Action;exit 0 }
    catch { Write-Host ('Certificate operation stopped: '+$_.Exception.Message);exit 1 }
}
