param(
  [Parameter(Mandatory=$true)][string]$SourceDirectory,
  [Parameter(Mandatory=$true)][string]$DestinationDirectory
)
$stamp=Get-Date -Format 'yyyyMMdd-HHmmss'; $target=Join-Path $DestinationDirectory "corewatch-atlas-$stamp"; New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item -LiteralPath (Join-Path $SourceDirectory 'atlas.db') -Destination $target -Force
Copy-Item -LiteralPath (Join-Path $SourceDirectory 'keys') -Destination $target -Recurse -Force
Get-FileHash (Join-Path $target 'atlas.db') -Algorithm SHA256 | Format-List | Out-File (Join-Path $target 'SHA256.txt')