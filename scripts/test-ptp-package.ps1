$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$temp=Join-Path ([IO.Path]::GetTempPath()) ('ListFXFlow-PTP-Test-'+[guid]::NewGuid())
New-Item -ItemType Directory $temp | Out-Null
try {
    $package=Join-Path $root 'artifacts/PTP-Driver-Experimental'
    $diagnostic=Join-Path $temp 'diagnostic.json'
    function Write-Diagnostic($ids, $page=13, $usage=5) {
        @{Schema=1;PhysicalTouchpads=@(@{UsagePage=$page;Usage=$usage;HardwareIds=@($ids)})} |
            ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $diagnostic
    }
    function Reject-Preparation($label) {
        $rejected=$false
        try { & "$PSScriptRoot/Prepare-PtpDriver.ps1" -DiagnosticsFile $diagnostic -PackageDirectory $package -OutputDirectory (Join-Path $temp ([guid]::NewGuid())) }
        catch { $rejected=$true }
        if(-not $rejected) { throw "Preparation unexpectedly accepted: $label" }
    }
    $id='HID\VID_1234&PID_5678&COL01'
    Write-Diagnostic @($id,'HID\HID_DEVICE_UP:000D_U:0005')
    $output=Join-Path $temp 'prepared'
    & "$PSScriptRoot/Prepare-PtpDriver.ps1" -DiagnosticsFile $diagnostic -PackageDirectory $package -OutputDirectory $output
    $inf=Get-Content -Raw -LiteralPath (Join-Path $output 'ListFXFlowPtp.inf')
    if(-not $inf.Contains($id) -or $inf.Contains('@PTP_HARDWARE_ID@')) { throw 'Exact hardware ID substitution failed' }
    $target=Get-Content -Raw -LiteralPath (Join-Path $output 'target.json') | ConvertFrom-Json
    if($target.Installed -or -not $target.ObserverOnly -or -not $target.RequiresMicrosoftSigning) { throw 'Incorrect prepared-package state' }
    if((Get-FileHash (Join-Path $output 'ListFXFlowPtp.sys')).Hash -ne (Get-FileHash (Join-Path $package 'ListFXFlowPtp.sys')).Hash) { throw 'Driver bytes changed during preparation' }
    Write-Diagnostic @('HID\HID_DEVICE_UP:000D_U:0005');Reject-Preparation 'generic HID class'
    Write-Diagnostic @('HID\VID_1234&PID_5678') 1 2;Reject-Preparation 'mouse collection'
    Write-Diagnostic @('HID\VID_1234%13%');Reject-Preparation 'INF substitution characters'
    @{Schema=1;PhysicalTouchpads=@(@{UsagePage=13;Usage=5;HardwareIds=@($id)},@{UsagePage=13;Usage=5;HardwareIds=@($id)})} |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $diagnostic
    Reject-Preparation 'ambiguous devices'
    Write-Host 'PTP package preparation checks passed; no driver was installed.'
} finally { Remove-Item -Recurse -Force -LiteralPath $temp }
