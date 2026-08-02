param(
  [Parameter(Mandatory=$true)][string]$OutputDirectory,
  [string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
function New-PortableZip([string]$Source, [string]$Archive) {
  $stream = [IO.File]::Open($Archive, [IO.FileMode]::Create)
  try {
    $zip = [System.IO.Compression.ZipArchive]::new($stream, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
      Get-ChildItem -LiteralPath $Source -Recurse -File | ForEach-Object {
        $entryName = $_.FullName.Substring($Source.Length).TrimStart('\', '/') -replace '\\', '/'
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $_.FullName, $entryName, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
      }
    } finally { $zip.Dispose() }
  } finally { $stream.Dispose() }
}
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$stagingRoot = Join-Path $outputRoot 'staging'
$serverOutput = Join-Path $stagingRoot 'server'
$agentOutput = Join-Path $stagingRoot 'agent'
Remove-Item -LiteralPath $stagingRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $serverOutput, $agentOutput | Out-Null
dotnet publish (Join-Path $repositoryRoot 'src/CoreWatch.Atlas.Server/CoreWatch.Atlas.Server.csproj') -c $Configuration --no-restore -o $serverOutput
if ($LASTEXITCODE -ne 0) { throw 'Server publish failed.' }
dotnet publish (Join-Path $repositoryRoot 'src/CoreWatch.Atlas.Agent/CoreWatch.Atlas.Agent.csproj') -c $Configuration --no-restore -o $agentOutput
if ($LASTEXITCODE -ne 0) { throw 'Agent publish failed.' }
$agentArchive = Join-Path $outputRoot 'corewatch-atlas-agent.zip'
Remove-Item -LiteralPath $agentArchive -Force -ErrorAction SilentlyContinue
New-PortableZip $agentOutput $agentArchive
$downloadDirectory = Join-Path $serverOutput 'wwwroot/downloads'
New-Item -ItemType Directory -Force -Path $downloadDirectory | Out-Null
Copy-Item -LiteralPath $agentArchive -Destination (Join-Path $downloadDirectory 'corewatch-atlas-agent.zip') -Force
foreach ($name in @('server', 'agent')) {
  $source = Join-Path $stagingRoot $name
  $archive = Join-Path $outputRoot "corewatch-atlas-$name.zip"
  Remove-Item -LiteralPath $archive -Force -ErrorAction SilentlyContinue
  New-PortableZip $source $archive
  $hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
  Set-Content -LiteralPath "$archive.sha256.txt" -Value "$hash  $(Split-Path -Leaf $archive)" -Encoding utf8
}
Write-Host "Published packages: $outputRoot"
# CoreWatch Atlas operational script: Publish-CoreWatchAtlas.
