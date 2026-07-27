[CmdletBinding()]
param([string]$UpstreamPath = "")

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$version = (Get-Content (Join-Path $root "VERSION") -Raw).Trim()
$commit = (Get-Content (Join-Path $root "UPSTREAM_COMMIT") -Raw).Trim()
$work = Join-Path $root ".work"
$source = Join-Path $work "source"
$stage = Join-Path $work "package"
$dist = Join-Path $root "dist"

foreach ($line in Get-Content (Join-Path $root "SOURCE_SHA256SUMS.txt")) {
    if ([string]::IsNullOrWhiteSpace($line) -or $line.StartsWith('#')) { continue }
    if ($line -notmatch '^([0-9a-fA-F]{64})\s+(.+)$') { throw "Invalid hash line: $line" }
    $path = Join-Path $root $matches[2]
    $actual = (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $matches[1].ToLowerInvariant()) { throw "SHA-256 mismatch: $($matches[2])" }
}

if ([string]::IsNullOrWhiteSpace($UpstreamPath)) { $UpstreamPath = Join-Path $root ".cache/upstream" }
if (-not (Test-Path (Join-Path $UpstreamPath ".git"))) {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $UpstreamPath) | Out-Null
    git clone https://github.com/BOTLANNER/BannerlordPlayerSettlement.git $UpstreamPath
}

function Resolve-Commit([string]$repo, [string]$revision) {
    $previousNativePreference = $PSNativeCommandUseErrorActionPreference
    try {
        $PSNativeCommandUseErrorActionPreference = $false
        $resolved = & git -C $repo rev-parse --verify --quiet "$revision^{commit}" 2>$null
        $exitCode = $LASTEXITCODE
    }
    finally {
        $PSNativeCommandUseErrorActionPreference = $previousNativePreference
    }

    if ($exitCode -ne 0 -or [string]::IsNullOrWhiteSpace($resolved)) { return $null }
    return ([string]$resolved).Trim()
}

if ((Resolve-Commit $UpstreamPath $commit) -ne $commit) {
    git -C $UpstreamPath fetch --no-tags origin $commit
}
if ((Resolve-Commit $UpstreamPath $commit) -ne $commit) { throw "Cannot resolve pinned upstream commit $commit" }

if (Test-Path $source) { Remove-Item -Recurse -Force $source }
New-Item -ItemType Directory -Force -Path $work | Out-Null
git clone --no-checkout $UpstreamPath $source
git -C $source checkout --detach $commit
$patch = Join-Path $root "patches/0001-tor-7.6.11.patch"
git -C $source apply --check --whitespace=nowarn $patch
git -C $source apply --whitespace=nowarn $patch

$overlayRoot = Join-Path $root "overlays/BannerlordPlayerSettlement"
$projectRoot = Join-Path $source "BannerlordPlayerSettlement"
Get-ChildItem $overlayRoot -Recurse -File | ForEach-Object {
    $relative = $_.FullName.Substring($overlayRoot.Length).TrimStart('\','/')
    $target = Join-Path $projectRoot $relative
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
    Copy-Item $_.FullName $target -Force
}
$project = Join-Path $projectRoot "BannerlordPlayerSettlement.csproj"
$text = Get-Content $project -Raw
$text = [regex]::Replace($text, '<Version>[^<]+</Version>', "<Version>$version</Version>", 1)
[IO.File]::WriteAllText($project, $text, [Text.UTF8Encoding]::new($false))

$baseXml = [xml](Get-Content (Join-Path $root "PlayerSettlement/SubModule.xml") -Raw)
$torXml = [xml](Get-Content (Join-Path $root "PlayerSettlement_TOR/SubModule.xml") -Raw)
if ($baseXml.Module.Version.value -ne "v$version") { throw "PlayerSettlement/SubModule.xml version mismatch" }
$dependency = $torXml.Module.DependedModules.DependedModule | Where-Object Id -eq 'PlayerSettlement'
if ($dependency.DependentVersion -ne "v$version") { throw "PlayerSettlement_TOR dependency mismatch" }
Get-ChildItem (Join-Path $root "PlayerSettlement"), (Join-Path $root "PlayerSettlement_TOR") -Recurse -File -Filter *.xml | ForEach-Object { [void][xml](Get-Content $_.FullName -Raw) }

$mainProject = Join-Path $projectRoot "BannerlordPlayerSettlement.csproj"
$raidProject = Join-Path $root "src/PlayerSettlementVillageRaidFix/PlayerSettlementVillageRaidFix.csproj"
$torProject = Join-Path $root "src/PlayerSettlementTORRuntime/PlayerSettlementTORRuntime.csproj"
dotnet restore $mainProject
dotnet build $mainProject -c Beta_Release -p:Platform=x64 --no-restore
dotnet restore $raidProject
dotnet build $raidProject -c Release --no-restore
dotnet restore $torProject
dotnet build $torProject -c Release --no-restore

if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force -Path (Join-Path $stage "Modules") | Out-Null
Copy-Item (Join-Path $root "PlayerSettlement") (Join-Path $stage "Modules/PlayerSettlement") -Recurse
Copy-Item (Join-Path $root "PlayerSettlement_TOR") (Join-Path $stage "Modules/PlayerSettlement_TOR") -Recurse

function Copy-Runtime([string]$file, [string]$module) {
    if (-not (Test-Path $file)) { throw "Missing build output: $file" }
    foreach ($platform in @('Win64_Shipping_Client','Gaming.Desktop.x64_Shipping_Client')) {
        $target = Join-Path $stage "Modules/$module/bin/$platform"
        New-Item -ItemType Directory -Force -Path $target | Out-Null
        Copy-Item $file $target -Force
        $pdb = [IO.Path]::ChangeExtension($file,'.pdb')
        if (Test-Path $pdb) { Copy-Item $pdb $target -Force }
    }
}
Copy-Runtime (Join-Path $projectRoot "bin/x64/Beta_Release/net472/PlayerSettlement.dll") 'PlayerSettlement'
Copy-Runtime (Join-Path $root "src/PlayerSettlementVillageRaidFix/bin/Release/net472/PlayerSettlementVillageRaidFix.dll") 'PlayerSettlement'
Copy-Runtime (Join-Path $root "src/PlayerSettlementTORRuntime/bin/Release/net472/PlayerSettlementTORRuntime.dll") 'PlayerSettlement_TOR'

[IO.File]::WriteAllText((Join-Path $stage 'SOURCE_PROVENANCE.txt'), "Version=$version`nUpstreamCommit=$commit`n", [Text.UTF8Encoding]::new($false))
if (Test-Path $dist) { Remove-Item -Recurse -Force $dist }
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$zip = Join-Path $dist "Player_Settlements_${version}_BL_1.3.15_ToR_1.16.zip"
Add-Type -AssemblyName System.IO.Compression
$stream = [IO.File]::Open($zip,[IO.FileMode]::CreateNew)
try {
    $archive = [IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create,$false)
    try {
        Get-ChildItem $stage -Recurse -File | Sort-Object FullName | ForEach-Object {
            $relative = $_.FullName.Substring($stage.Length).TrimStart('\','/').Replace('\','/')
            $entry = $archive.CreateEntry($relative,[IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = [DateTimeOffset]::new(2020,1,1,0,0,0,[TimeSpan]::Zero)
            $outputStream = $entry.Open(); $sourceStream = [IO.File]::OpenRead($_.FullName)
            try { $sourceStream.CopyTo($outputStream) } finally { $sourceStream.Dispose(); $outputStream.Dispose() }
        }
    } finally { $archive.Dispose() }
} finally { $stream.Dispose() }
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $dist 'SHA256SUMS.txt'), "$hash  $([IO.Path]::GetFileName($zip))`n", [Text.UTF8Encoding]::new($false))
Write-Host "Built and validated $zip"
