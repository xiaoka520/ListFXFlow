param(
    [ValidateSet('Install','Rollback')][string]$Action='Install',
    [string]$PublishedName
)
$ErrorActionPreference='Stop'

function Get-PtpTrialTarget([string]$Directory,[switch]$ForRollback) {
    $target=Get-Content -Raw -LiteralPath (Join-Path $Directory 'target.json') | ConvertFrom-Json
    $id=[string]$target.HardwareId
    if($id -notmatch '^HID\\[A-Za-z0-9_&.\\-]+$' -or $id -match '^HID\\HID_DEVICE' -or -not $target.ObserverOnly) { throw 'Not an exact, observe-only PTP package.' }
    $sys=Join-Path $Directory 'ListFXFlowPtp.sys'
    if(-not $ForRollback -and (Get-FileHash -LiteralPath $sys -Algorithm SHA256).Hash -ne $target.DriverHash) { throw 'Driver hash mismatch. Extract the original package again.' }
    $inf=Get-Content -Raw -LiteralPath (Join-Path $Directory 'ListFXFlowPtp.inf')
    Assert-PtpTrialInf $inf $id
    return $id
}
function Assert-PtpTrialInf([string]$Text,[string]$HardwareId) {
    if(-not $Text.Contains('Observe,'+$HardwareId) -or
       -not $Text.Contains('ExtensionId={67205833-e52c-4a97-97af-726c09f8ed1c}') -or
       -not $Text.Contains('AddService=ListFXFlowPtp,0x00000000,Service')) { throw 'INF is not the expected ListFXFlow collection-specific extension.' }
}
function Get-PtpTrialRollbackInf([string]$WindowsDirectory,[string]$Name,[string]$HardwareId) {
    if($Name -notmatch '^oem[0-9]+\.inf$') { throw 'Enter only the published name shown by PnPUtil, for example oem123.inf.' }
    $path=Join-Path $WindowsDirectory ('INF/'+$Name)
    Assert-PtpTrialInf (Get-Content -Raw -LiteralPath $path) $HardwareId
    return $Name
}
function Invoke-PtpTrialPnP([string[]]$Arguments) {
    $previous=$ErrorActionPreference;$ErrorActionPreference='Continue'
    try {
        $output=& "$env:windir/System32/pnputil.exe" @Arguments 2>&1 | Out-String
        $code=$LASTEXITCODE
    } finally { $ErrorActionPreference=$previous }
    return @{Output=$output;ExitCode=$code}
}
function Invoke-PtpTrial([string]$Directory,[string]$Mode,[string]$Name) {
    $identity=[Security.Principal.WindowsIdentity]::GetCurrent()
    $principal=New-Object Security.Principal.WindowsPrincipal($identity)
    if(-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Right-click Try-Install.cmd or Rollback.cmd and choose Run as administrator.' }
    $id=Get-PtpTrialTarget $Directory -ForRollback:($Mode -eq 'Rollback')
    $log=Join-Path $Directory ('ptp-'+$Mode.ToLowerInvariant()+'-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'.log')
    if($Mode -eq 'Install') {
        Write-Host 'Trying the exact PTP extension under the CURRENT Windows signing policy.'
        Write-Host 'An unsigned or missing catalog may be rejected. No boot/security setting will be changed.'
        $result=Invoke-PtpTrialPnP @('/add-driver',(Join-Path $Directory 'ListFXFlowPtp.inf'),'/install')
    } else {
        if(-not $Name) { $Name=Read-Host 'Published name from the install log (oem123.inf), or Enter to cancel' }
        if(-not $Name) { Write-Host 'Cancelled. Nothing removed.';return 0 }
        $approved=Get-PtpTrialRollbackInf $env:windir $Name $id
        $result=Invoke-PtpTrialPnP @('/delete-driver',$approved,'/uninstall')
    }
    $result.Output | Set-Content -LiteralPath $log -Encoding utf8
    Add-Content -LiteralPath $log -Value ('PnPUtil exit code: '+$result.ExitCode)
    Write-Host $result.Output
    Write-Host ('PnPUtil exit code: '+$result.ExitCode)
    $inspection=Invoke-PtpTrialPnP @('/enum-devices','/connected','/deviceid',$id,'/stack','/drivers')
    Add-Content -LiteralPath $log -Value $inspection.Output
    Write-Host $inspection.Output
    Write-Host ('Log: '+$log)
    if($result.ExitCode -eq 3010) { Write-Host 'Windows requests a restart. This script will not restart the computer.' }
    if($Mode -eq 'Install' -and $result.ExitCode -in @(0,3010)) { Write-Host 'PnPUtil accepted the operation. Driver loading and touch reports are NOT yet verified.' }
    return $result.ExitCode
}
# Dot-sourcing exposes only the validation functions for isolated CI tests.
if($MyInvocation.InvocationName -ne '.') {
    try { $code=Invoke-PtpTrial $PSScriptRoot $Action $PublishedName;exit $code }
    catch { Write-Host ('Trial stopped: '+$_.Exception.Message);exit 1 }
}
