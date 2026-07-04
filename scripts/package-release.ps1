param(
    [string]$Version = "",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Resolve-Path (Join-Path $scriptDir "..")
$projectPath = Join-Path $root "DustDesk.csproj"
if (-not (Test-Path -LiteralPath $projectPath)) {
    throw "Project file not found: $projectPath"
}

[xml]$projectXml = Get-Content -LiteralPath $projectPath -Raw
$assemblyName = $projectXml.Project.PropertyGroup.AssemblyName
if ([string]::IsNullOrWhiteSpace($assemblyName)) {
    $assemblyName = [IO.Path]::GetFileNameWithoutExtension($projectPath)
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = $projectXml.Project.PropertyGroup.Version
    if ([string]::IsNullOrWhiteSpace($Version)) {
        throw "Version was not provided and could not be read from DustDesk.csproj."
    }
}

$packageName = "$assemblyName.$Version"
$releaseRoot = Join-Path $root "release"
$rawPublishDir = Join-Path $releaseRoot ".publish-$packageName"
$packageDir = Join-Path $releaseRoot $packageName
$zipPath = Join-Path $root "$packageName.zip"

function Assert-UnderRoot([string]$Path) {
    $fullPath = [IO.Path]::GetFullPath($Path)
    $fullRoot = [IO.Path]::GetFullPath($root)
    if (-not $fullPath.StartsWith($fullRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refuse to operate outside workspace: $fullPath"
    }
}

Assert-UnderRoot $rawPublishDir
Assert-UnderRoot $packageDir
Assert-UnderRoot $zipPath

foreach ($path in @($rawPublishDir, $packageDir, $zipPath)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
}

New-Item -ItemType Directory -Path $rawPublishDir, $packageDir -Force | Out-Null

dotnet publish $projectPath `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $rawPublishDir

$exePath = Join-Path $rawPublishDir "$assemblyName.exe"
if (-not (Test-Path -LiteralPath $exePath)) {
    throw "Publish failed, executable not found: $exePath"
}

Copy-Item -LiteralPath $exePath -Destination $packageDir -Force

$imagesPath = Join-Path $root "images"
if (Test-Path -LiteralPath $imagesPath) {
    Copy-Item -Path (Join-Path $imagesPath "*") -Destination (New-Item -ItemType Directory -Path (Join-Path $packageDir "images") -Force) -Recurse -Force
}

$usageFile = Get-ChildItem -LiteralPath $root -File -Filter "*.txt" | Select-Object -First 1
if ($usageFile) {
    Copy-Item -LiteralPath $usageFile.FullName -Destination $packageDir -Force
}

Compress-Archive -Path (Join-Path $packageDir "*") -DestinationPath $zipPath -Force
Remove-Item -LiteralPath $rawPublishDir -Recurse -Force

$topLevelItems = Get-ChildItem -LiteralPath $packageDir | Select-Object -ExpandProperty Name
$zip = Get-Item -LiteralPath $zipPath

[pscustomobject]@{
    Package = $zip.FullName
    ReleaseDir = $packageDir
    SizeMB = [math]::Round($zip.Length / 1MB, 2)
    TopLevel = ($topLevelItems -join ", ")
}
