param(
    [string]$Version = "",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputRoot = "artifacts\release",
    [switch]$SkipElectronRuntime
)

$ErrorActionPreference = "Stop"

$root = (Resolve-Path "$PSScriptRoot\..").Path
function Invoke-CheckedNative([string]$FilePath, [string[]]$Arguments) {
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $packageJson = Get-Content -Raw (Join-Path $root "package.json") | ConvertFrom-Json
    $Version = [string]$packageJson.version
}

$outputRootPath = [System.IO.Path]::GetFullPath((Join-Path $root $OutputRoot))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $root "artifacts"))
if (-not $outputRootPath.StartsWith($artifactsRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputRoot must stay under $artifactsRoot."
}

$packageName = "winshots-$Version-$Runtime"
$stagingRoot = Join-Path $outputRootPath $packageName
$setupPublishRoot = Join-Path $outputRootPath "$packageName-setup"
$setupExePath = Join-Path $outputRootPath "$packageName-setup.exe"
$setupChecksumPath = "$setupExePath.sha256"
$legacySinglePublishRoot = Join-Path $outputRootPath "$packageName-single"
$legacySingleExePath = Join-Path $outputRootPath "$packageName.exe"
$legacySingleChecksumPath = "$legacySingleExePath.sha256"
$zipPath = Join-Path $outputRootPath "$packageName.zip"
$checksumPath = "$zipPath.sha256"

if (Test-Path $stagingRoot) {
    Remove-Item -LiteralPath $stagingRoot -Recurse -Force
}
if (Test-Path $setupPublishRoot) {
    Remove-Item -LiteralPath $setupPublishRoot -Recurse -Force
}
if (Test-Path $legacySinglePublishRoot) {
    Remove-Item -LiteralPath $legacySinglePublishRoot -Recurse -Force
}
if (Test-Path $legacySingleExePath) {
    Remove-Item -LiteralPath $legacySingleExePath -Force
}
if (Test-Path $legacySingleChecksumPath) {
    Remove-Item -LiteralPath $legacySingleChecksumPath -Force
}

New-Item -ItemType Directory -Force -Path $stagingRoot | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $stagingRoot "app") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $stagingRoot "mcp") | Out-Null
New-Item -ItemType Directory -Force -Path $setupPublishRoot | Out-Null

Invoke-CheckedNative "dotnet" @(
    "publish",
    (Join-Path $root "src\Winshots.App\Winshots.App.csproj"),
    "--configuration",
    $Configuration,
    "--runtime",
    $Runtime,
    "--self-contained",
    "true",
    "--output",
    (Join-Path $stagingRoot "app"),
    "-p:Version=$Version"
)

Invoke-CheckedNative "dotnet" @(
    "publish",
    (Join-Path $root "src\Winshots.Mcp\Winshots.Mcp.csproj"),
    "--configuration",
    $Configuration,
    "--runtime",
    $Runtime,
    "--self-contained",
    "true",
    "--output",
    (Join-Path $stagingRoot "mcp"),
    "-p:Version=$Version"
)

Copy-Item -LiteralPath (Join-Path $root ".codex-plugin") -Destination (Join-Path $stagingRoot ".codex-plugin") -Recurse -Force
Copy-Item -LiteralPath (Join-Path $root ".agents") -Destination (Join-Path $stagingRoot ".agents") -Recurse -Force
Copy-Item -LiteralPath (Join-Path $root "skills") -Destination (Join-Path $stagingRoot "skills") -Recurse -Force
Copy-Item -LiteralPath (Join-Path $root "README.md") -Destination $stagingRoot -Force
Copy-Item -LiteralPath (Join-Path $root "LICENSE") -Destination $stagingRoot -Force

$mcpJson = @{
    mcpServers = @{
        winshots = @{
            command = "./mcp/Winshots.Mcp.exe"
            args = @()
            cwd = "."
            startup_timeout_sec = 60.0
            tool_timeout_sec = 120
        }
    }
} | ConvertTo-Json -Depth 8
Set-Content -Path (Join-Path $stagingRoot ".mcp.json") -Value $mcpJson -Encoding UTF8

$electronSource = Join-Path $root "src\Winshots.Electron"
$electronRuntimeSource = Join-Path $root "node_modules\electron\dist"
if (-not $SkipElectronRuntime -and (Test-Path (Join-Path $electronRuntimeSource "electron.exe"))) {
    Copy-Item -LiteralPath $electronSource -Destination (Join-Path $stagingRoot "electron-ui") -Recurse -Force
    Copy-Item -LiteralPath $electronRuntimeSource -Destination (Join-Path $stagingRoot "electron-runtime") -Recurse -Force

    $electronPackage = @{
        name = "winshots-review-ui"
        version = $Version
        private = $true
        main = "main.cjs"
    } | ConvertTo-Json -Depth 4
    Set-Content -Path (Join-Path $stagingRoot "electron-ui\package.json") -Value $electronPackage -Encoding UTF8
}
else {
    Write-Warning "Electron runtime was not found. Run npm ci first or pass -SkipElectronRuntime intentionally."
}

@"
@echo off
"%~dp0app\Winshots.App.exe" %*
"@ | Set-Content -Path (Join-Path $stagingRoot "Winshots.cmd") -Encoding ASCII

if (Test-Path (Join-Path $stagingRoot "electron-runtime\electron.exe")) {
    @"
@echo off
"%~dp0app\Winshots.App.exe" %*
"@ | Set-Content -Path (Join-Path $stagingRoot "Winshots Review UI.cmd") -Encoding ASCII
}

Copy-Item -LiteralPath (Join-Path $root "scripts\installer\install.ps1") -Destination (Join-Path $stagingRoot "install.ps1") -Force
Copy-Item -LiteralPath (Join-Path $root "scripts\installer\uninstall.ps1") -Destination (Join-Path $stagingRoot "uninstall.ps1") -Force
Copy-Item -LiteralPath (Join-Path $root "scripts\installer\README-INSTALL.md") -Destination (Join-Path $stagingRoot "README-INSTALL.md") -Force

$releaseNotes = @"
# Winshots $Version

- Includes the Winshots Windows app and the Winshots MCP server.
- Includes the Codex plugin files at version $Version.
- Download $packageName-setup.exe to install Winshots like a normal Windows app under %LOCALAPPDATA%\Programs\Winshots.
- Download $packageName.zip for the full portable package with Electron UI, MCP, and Codex plugin files.
- Run Winshots Review UI.cmd from the extracted zip to start the C# host, Electron UI, shortcuts, overlay/session controls, and local MCP files together.
- The setup installs the app, creates Start Menu shortcuts, and registers an Apps & Features uninstaller.
- Codex plugin registration is separate so a locked Codex plugin cache cannot break the Winshots app install.
- Captures and sessions stay local under the user's Documents folder unless the user shares them explicitly.
- Instant Replay keeps a bounded temporary local buffer and saves autonomous sessions without requiring ffmpeg.
"@
Set-Content -Path (Join-Path $outputRootPath "RELEASE_NOTES.md") -Value $releaseNotes -Encoding UTF8

if (Test-Path $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
Get-ChildItem -LiteralPath $stagingRoot -Force | Compress-Archive -DestinationPath $zipPath -Force

Invoke-CheckedNative "dotnet" @(
    "publish",
    (Join-Path $root "src\Winshots.Installer\Winshots.Installer.csproj"),
    "--configuration",
    $Configuration,
    "--runtime",
    $Runtime,
    "--self-contained",
    "true",
    "--output",
    $setupPublishRoot,
    "-p:Version=$Version",
    "-p:PayloadZip=$zipPath",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:EnableCompressionInSingleFile=true"
)

if (Test-Path $setupExePath) {
    Remove-Item -LiteralPath $setupExePath -Force
}
Copy-Item -LiteralPath (Join-Path $setupPublishRoot "Winshots.Setup.exe") -Destination $setupExePath -Force

$hash = Get-FileHash -Path $zipPath -Algorithm SHA256
Set-Content -Path $checksumPath -Value "$($hash.Hash)  $(Split-Path -Leaf $zipPath)" -Encoding ASCII
$setupHash = Get-FileHash -Path $setupExePath -Algorithm SHA256
Set-Content -Path $setupChecksumPath -Value "$($setupHash.Hash)  $(Split-Path -Leaf $setupExePath)" -Encoding ASCII

Write-Host "Windows installer: $setupExePath"
Write-Host "SHA256: $setupChecksumPath"
Write-Host "Release package: $zipPath"
Write-Host "SHA256: $checksumPath"
