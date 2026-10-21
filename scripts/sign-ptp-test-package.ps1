param(
    [string]$InputDirectory,
    [string]$OutputDirectory
)
$ErrorActionPreference='Stop'
function Invoke-PtpSigningTool([string]$Tool,[string[]]$Arguments) {
    Write-Host ('Running '+[IO.Path]::GetFileName($Tool)+' '+($Arguments -join ' '))
    $info=New-Object Diagnostics.ProcessStartInfo
    $info.FileName=$Tool
    $info.Arguments=(@($Arguments | ForEach-Object {'"'+(($_ -replace '(\\*)"','$1$1\"') -replace '(\\+)$','$1$1')+'"'}) -join ' ')
    $info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
    $process=New-Object Diagnostics.Process;$process.StartInfo=$info
    try {
        if(-not $process.Start()) { throw 'Could not start signing tool' }
        $stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
        if(-not $process.WaitForExit(120000)) { $process.Kill();$process.WaitForExit(5000) | Out-Null;throw ('Tool timed out: '+$Tool) }
        Write-Host $stdout.GetAwaiter().GetResult();Write-Host $stderr.GetAwaiter().GetResult()
        if($process.ExitCode -ne 0) { throw ('Signing tool exit code '+$process.ExitCode+': '+$Tool) }
    } finally { $process.Dispose() }
}
$root=Split-Path -Parent $PSScriptRoot
if(-not $InputDirectory) { $InputDirectory=Join-Path $root 'artifacts/PTP-Driver-Experimental/Profiles/ASUF1209-Col02' }
if(-not $OutputDirectory) { $OutputDirectory=Join-Path $root 'artifacts/PTP-Driver-TestSigned/ASUF1209-Col02' }
$signTool=Join-Path $root '.build/driver-packages/Microsoft.Windows.SDK.CPP.10.0.28000.2526/c/bin/10.0.28000.0/x64/signtool.exe'
$inf2cat=Join-Path $root '.build/driver-packages/Microsoft.Windows.WDK.x64.10.0.28000.2526/c/bin/10.0.28000.0/x86/Inf2Cat.exe'
foreach($tool in @($signTool,$inf2cat)) { if(-not(Test-Path -LiteralPath $tool)) { throw "Missing signing tool: $tool" } }
if(Test-Path -LiteralPath $OutputDirectory) { throw 'Signing output already exists; choose a fresh directory.' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
Copy-Item "$InputDirectory/*" $OutputDirectory
if(Test-Path -LiteralPath (Join-Path $OutputDirectory 'ListFXFlowPtp.cat')) { throw 'Input must be the unsigned package without an old catalog.' }
Copy-Item "$PSScriptRoot/ptp-test-signing/*" $OutputDirectory -Exclude 'test-package.ps1'
Copy-Item "$PSScriptRoot/ptp-test-signing/Test-README.txt" (Join-Path $OutputDirectory 'Trial-README.txt') -Force
. "$OutputDirectory/Manage-PtpTrial.ps1"
$id=Get-PtpTrialTarget $OutputDirectory
if($id -cne 'HID\VEN_ASUF&DEV_1209&Col02') { throw 'Unexpected test-signing target.' }
$sys=Join-Path $OutputDirectory 'ListFXFlowPtp.sys'
$inf=Join-Path $OutputDirectory 'ListFXFlowPtp.inf'
$cat=Join-Path $OutputDirectory 'ListFXFlowPtp.cat'
$cer=Join-Path $OutputDirectory 'ListFXFlowPtp-Test.cer'
$cert=$null
try {
    # The non-exportable private key exists only on this signing machine.
    $subject='CN=ListFXFlow PTP Test Only '+[guid]::NewGuid().ToString('N')
    Write-Host 'Creating short-lived non-exportable signing certificate...'
    $cert=New-SelfSignedCertificate -Type CodeSigningCert -Subject $subject -CertStoreLocation Cert:\CurrentUser\My `
        -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -KeyExportPolicy NonExportable `
        -NotBefore (Get-Date).AddDays(-1) -NotAfter (Get-Date).AddDays(90)
    Write-Host ('Created test certificate '+$cert.Thumbprint)
    Invoke-PtpSigningTool $signTool @('sign','/v','/s','My','/sha1',$cert.Thumbprint,'/fd','SHA256','/ph',$sys)
    Invoke-PtpSigningTool $inf2cat @(("/driver:"+$OutputDirectory),'/os:10_GE_X64,10_25H2_X64','/uselocaltime','/verbose')
    if(-not(Test-Path -LiteralPath $cat)) { throw 'INF catalog generation produced no CAT' }
    Invoke-PtpSigningTool $signTool @('sign','/v','/s','My','/sha1',$cert.Thumbprint,'/fd','SHA256',$cat)
    Export-Certificate -Cert $cert -FilePath $cer | Out-Null
    foreach($store in @('Root','TrustedPublisher')) { Import-Certificate -FilePath $cer -CertStoreLocation ("Cert:\CurrentUser\"+$store) | Out-Null }
    foreach($file in @($sys,$cat)) {
        Invoke-PtpSigningTool $signTool @('verify','/v','/pa',$file)
    }
    foreach($file in @($sys,$inf)) {
        Invoke-PtpSigningTool $signTool @('verify','/v','/pa','/c',$cat,$file)
    }
    $targetPath=Join-Path $OutputDirectory 'target.json'
    $target=Get-Content -Raw -LiteralPath $targetPath | ConvertFrom-Json
    $target.DriverHash=(Get-FileHash -LiteralPath $sys -Algorithm SHA256).Hash
    $target | Add-Member -NotePropertyName SigningMode -NotePropertyValue 'self-signed-test' -Force
    $target | Add-Member -NotePropertyName RequiresTestSigning -NotePropertyValue $true -Force
    $target | ConvertTo-Json | Set-Content -LiteralPath $targetPath -Encoding utf8
    @{Schema=1;SigningMode='self-signed-test';RequiresTestSigning=$true;HardwareId=$id;
      CertificateFile='ListFXFlowPtp-Test.cer';CertificateThumbprint=$cert.Thumbprint;CertificateSubject=$cert.Subject;
      CertificateSHA256=(Get-FileHash -LiteralPath $cer -Algorithm SHA256).Hash;
      ValidFrom=$cert.NotBefore.ToUniversalTime().ToString('o');ValidUntil=$cert.NotAfter.ToUniversalTime().ToString('o');
      DriverSHA256=(Get-FileHash -LiteralPath $sys -Algorithm SHA256).Hash;
      InfSHA256=(Get-FileHash -LiteralPath $inf -Algorithm SHA256).Hash;
      CatalogSHA256=(Get-FileHash -LiteralPath $cat -Algorithm SHA256).Hash;
      SourceRevision=(& git -C $root rev-parse HEAD);PrivateKeyIncluded=$false;Installed=$false} |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'test-signing.json') -Encoding utf8
    Write-Host 'Test-signed SYS/CAT and catalog members verified. Public certificate only; nothing installed.'
} finally {
    if($cert) {
        foreach($store in @('Root','TrustedPublisher')) {
            $path="Cert:\CurrentUser\$store\"+$cert.Thumbprint
            if(Test-Path $path) { Remove-Item -LiteralPath $path -Force }
        }
        $path='Cert:\CurrentUser\My\'+$cert.Thumbprint
        if(Test-Path $path) { Remove-Item -LiteralPath $path -DeleteKey -Force }
        $cert.Dispose()
    }
}
