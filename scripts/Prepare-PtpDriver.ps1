param(
    [Parameter(Mandatory=$true)][string]$DiagnosticsFile,
    [int]$DeviceIndex=-1,
    [string]$PackageDirectory=$PSScriptRoot,
    [string]$OutputDirectory=(Join-Path $PWD 'ListFXFlow-PTP-Prepared')
)
$ErrorActionPreference='Stop'
$data=Get-Content -Raw -LiteralPath $DiagnosticsFile | ConvertFrom-Json
if ($data.Schema -ne 1) { throw 'Unsupported HID diagnostic schema' }
$devices=@($data.PhysicalTouchpads)
if ($DeviceIndex -lt 0) {
    if ($devices.Count -ne 1) { throw 'Specify -DeviceIndex for the exact PTP collection in the diagnostic file' }
    $DeviceIndex=0
}
if ($DeviceIndex -ge $devices.Count) { throw 'DeviceIndex is outside the diagnostic device list' }
$device=$devices[$DeviceIndex]
if ($device.UsagePage -ne 13 -or $device.Usage -ne 5) { throw 'The selected device is not a Precision Touchpad collection' }
$id=@($device.HardwareIds | Where-Object {$_ -match '^HID\\[A-Za-z0-9_&.\\-]+$' -and $_ -notmatch '^HID\\HID_DEVICE'}) | Select-Object -First 1
if (-not $id) { throw 'No exact, device-specific HID hardware ID is present; generic class filters are not permitted' }
$sys=Join-Path $PackageDirectory 'ListFXFlowPtp.sys'
$template=Join-Path $PackageDirectory 'ListFXFlowPtp.inf.in'
if (-not (Test-Path -LiteralPath $sys) -or -not (Test-Path -LiteralPath $template)) { throw 'Missing observer .sys or INF template' }
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Output directory already exists; choose a new folder' }
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
Copy-Item -LiteralPath $sys -Destination $OutputDirectory
foreach ($name in @('LICENSE','THIRD_PARTY_NOTICES.txt','README.md')) {
    $notice=Join-Path $PackageDirectory $name
    if(Test-Path -LiteralPath $notice) { Copy-Item -LiteralPath $notice -Destination $OutputDirectory }
}
$inf=(Get-Content -Raw -LiteralPath $template).Replace('@PTP_HARDWARE_ID@',$id)
Set-Content -LiteralPath (Join-Path $OutputDirectory 'ListFXFlowPtp.inf') -Value $inf -Encoding ascii
@{HardwareId=$id;ReportedInputReportLength=$device.InputReportLength;ObserverOnly=$true;DriverHash=(Get-FileHash -LiteralPath $sys -Algorithm SHA256).Hash;Installed=$false;RequiresMicrosoftSigning=$true} |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'target.json') -Encoding utf8
Write-Host "Prepared for exact PTP hardware ID: $id"
Write-Host "Files: $OutputDirectory"
Write-Host 'Nothing installed. The driver and generated catalog still require signing before deployment.'
