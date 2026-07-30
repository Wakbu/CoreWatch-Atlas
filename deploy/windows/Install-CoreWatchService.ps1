param(
  [Parameter(Mandatory=$true)][string]$Name,
  [Parameter(Mandatory=$true)][string]$BinaryPath,
  [string]$Arguments = "",
  [string]$WorkingDirectory = ""
)
$command = '"{0}" {1}' -f $BinaryPath, $Arguments
New-Service -Name $Name -BinaryPathName $command -DisplayName $Name -StartupType Automatic
if ($WorkingDirectory) { Set-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Services\$Name" -Name WorkingDirectory -Value $WorkingDirectory }
Start-Service $Name