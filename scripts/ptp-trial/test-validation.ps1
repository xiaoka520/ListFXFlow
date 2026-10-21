$ErrorActionPreference='Stop'
. "$PSScriptRoot/Manage-PtpTrial.ps1"
$root=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$source=Join-Path $root 'artifacts/PTP-Driver-Experimental/Profiles/ASUF1209-Col02'
$temp=Join-Path ([IO.Path]::GetTempPath()) ('ListFXFlow-PTP-Trial-Test-'+[guid]::NewGuid())
New-Item -ItemType Directory $temp | Out-Null
function Reject($script,$label) { $failed=$false;try { & $script | Out-Null } catch { $failed=$true };if(-not $failed){throw "Unexpected acceptance: $label"} }
try {
    Copy-Item "$source/*" $temp
    $id=Get-PtpTrialTarget $temp
    if($id -cne 'HID\VEN_ASUF&DEV_1209&Col02') { throw 'Wrong installation target' }
    $inf=Get-Content -Raw -LiteralPath (Join-Path $temp 'ListFXFlowPtp.inf')
    New-Item -ItemType Directory (Join-Path $temp 'INF') | Out-Null
    $inf | Set-Content -LiteralPath (Join-Path $temp 'INF/oem123.inf')
    if((Get-PtpTrialRollbackInf $temp 'oem123.inf' $id) -ne 'oem123.inf') { throw 'Own extension rollback rejected' }
    Reject {Get-PtpTrialRollbackInf $temp '../oem123.inf' $id} 'rollback traversal'
    Reject {Get-PtpTrialRollbackInf $temp 'oem123.inf /force' $id} 'additional rollback flags'
    Reject {Get-PtpTrialRollbackInf $temp 'hid.inf' $id} 'system INF removal'
    $inf.Replace('ListFXFlowPtp','VendorTouchpad') | Set-Content -LiteralPath (Join-Path $temp 'INF/oem124.inf')
    Reject {Get-PtpTrialRollbackInf $temp 'oem124.inf' $id} 'another driver service'
    Reject {Assert-PtpTrialInf $inf 'HID\OTHER_MODEL'} 'another collection'
    [IO.File]::AppendAllText((Join-Path $temp 'ListFXFlowPtp.sys'),'changed')
    Reject {Get-PtpTrialTarget $temp} 'changed driver payload'
    if((Get-PtpTrialTarget $temp -ForRollback) -cne $id) { throw 'A damaged local payload must not block verified installed-package removal' }
    Write-Host 'PTP trial validation checks passed. No PnP command or driver installation was executed.'
} finally { Remove-Item -Recurse -Force -LiteralPath $temp }
