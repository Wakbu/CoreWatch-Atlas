param(
  [Parameter(Mandatory=$true)][string]$SourceDirectory,
  [Parameter(Mandatory=$true)][string]$DestinationDirectory
)
$databasePath = Join-Path $SourceDirectory 'atlas.db'
$keysPath = Join-Path $SourceDirectory 'keys'
if (-not (Test-Path -LiteralPath $databasePath -PathType Leaf)) { throw "SQLite database was not found: $databasePath" }
if (-not (Test-Path -LiteralPath $keysPath -PathType Container)) { throw "Data Protection keys were not found: $keysPath" }
# Stop the Server before running this script, or use a SQLite online-backup tool.
# Copying the -wal and -shm companions keeps a consistent SQLite snapshot when WAL is present.
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$target = Join-Path $DestinationDirectory "corewatch-atlas-$stamp"
New-Item -ItemType Directory -Force -Path $target | Out-Null
foreach ($suffix in @('', '-wal', '-shm')) {
  $source = "$databasePath$suffix"
  if (Test-Path -LiteralPath $source -PathType Leaf) { Copy-Item -LiteralPath $source -Destination $target -Force }
}
Copy-Item -LiteralPath $keysPath -Destination $target -Recurse -Force
$hashManifest = Get-ChildItem -LiteralPath $target -Recurse -File |
  Sort-Object FullName |
  ForEach-Object {
    $hash = Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256
    '{0} *{1}' -f $hash.Hash, $_.FullName.Substring($target.Length + 1)
  }
Set-Content -LiteralPath (Join-Path $target 'SHA256.txt') -Value $hashManifest -Encoding utf8
# CoreWatch Atlas operational script: Backup-CoreWatchAtlas.
