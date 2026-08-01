param(
  [Parameter(Mandatory=$true)][string]$OutputDirectory,
  [string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
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
Compress-Archive -Path (Join-Path $agentOutput '*') -DestinationPath $agentArchive -CompressionLevel Optimal
$downloadDirectory = Join-Path $serverOutput 'wwwroot/downloads'
New-Item -ItemType Directory -Force -Path $downloadDirectory | Out-Null
Copy-Item -LiteralPath $agentArchive -Destination (Join-Path $downloadDirectory 'corewatch-atlas-agent.zip') -Force
foreach ($name in @('server', 'agent')) {
  $source = Join-Path $stagingRoot $name
  $archive = Join-Path $outputRoot "corewatch-atlas-$name.zip"
  Remove-Item -LiteralPath $archive -Force -ErrorAction SilentlyContinue
  Compress-Archive -Path (Join-Path $source '*') -DestinationPath $archive -CompressionLevel Optimal
  $hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
  Set-Content -LiteralPath "$archive.sha256.txt" -Value "$hash  $(Split-Path -Leaf $archive)" -Encoding utf8
}
Write-Host "Published packages: $outputRoot"
# CoreWatch Atlas operational script: Publish-CoreWatchAtlas.
