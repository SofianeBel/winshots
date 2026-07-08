param(
    [string]$InstallRoot = "$env:LOCALAPPDATA\Programs\Winshots",
    [switch]$SkipCodexPlugin,
    [switch]$NoStartMenuShortcut
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath([string]$Path) {
    return [System.IO.Path]::GetFullPath([System.Environment]::ExpandEnvironmentVariables($Path))
}

function Invoke-CodexCommand([string[]]$Arguments, [switch]$IgnoreFailure) {
    $codex = Get-Command codex -ErrorAction SilentlyContinue
    if ($null -eq $codex) {
        throw "Codex CLI was not found on PATH."
    }

    & $codex.Source @Arguments
    if ($LASTEXITCODE -ne 0) {
        if ($IgnoreFailure) {
            return $false
        }

        throw "codex $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }

    return $true
}

function New-Shortcut([string]$ShortcutPath, [string]$TargetPath, [string]$Arguments, [string]$WorkingDirectory) {
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = $TargetPath
    $shortcut.Arguments = $Arguments
    $shortcut.WorkingDirectory = $WorkingDirectory
    $shortcut.Save()
}

$sourceRoot = (Resolve-Path $PSScriptRoot).Path
$installRootPath = Resolve-FullPath $InstallRoot
$rootPath = [System.IO.Path]::GetPathRoot($installRootPath)

if ([string]::IsNullOrWhiteSpace($installRootPath) -or $installRootPath -eq $rootPath) {
    throw "InstallRoot is not safe: $installRootPath"
}

if ($installRootPath.StartsWith($sourceRoot.TrimEnd('\') + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "InstallRoot cannot be inside the extracted installer folder."
}

New-Item -ItemType Directory -Force -Path $installRootPath | Out-Null

if (-not ($sourceRoot.Equals($installRootPath, [System.StringComparison]::OrdinalIgnoreCase))) {
    foreach ($item in Get-ChildItem -LiteralPath $sourceRoot -Force) {
        Copy-Item -LiteralPath $item.FullName -Destination $installRootPath -Recurse -Force
    }
}

$mcpExe = Join-Path $installRootPath "mcp\Winshots.Mcp.exe"
if (-not (Test-Path $mcpExe)) {
    throw "Missing MCP executable: $mcpExe"
}

$mcpJson = @{
    mcpServers = @{
        winshots = @{
            command = $mcpExe
            args = @()
            cwd = $installRootPath
            startup_timeout_sec = 60.0
            tool_timeout_sec = 120
        }
    }
} | ConvertTo-Json -Depth 8
Set-Content -Path (Join-Path $installRootPath ".mcp.json") -Value $mcpJson -Encoding UTF8

if (-not $NoStartMenuShortcut) {
    $programsFolder = [Environment]::GetFolderPath("Programs")
    $shortcutFolder = Join-Path $programsFolder "Winshots"
    New-Item -ItemType Directory -Force -Path $shortcutFolder | Out-Null

    New-Shortcut `
        -ShortcutPath (Join-Path $shortcutFolder "Winshots.lnk") `
        -TargetPath (Join-Path $installRootPath "app\Winshots.App.exe") `
        -Arguments "" `
        -WorkingDirectory (Join-Path $installRootPath "app")

    $reviewLauncher = Join-Path $installRootPath "Winshots Review UI.cmd"
    if (Test-Path $reviewLauncher) {
        New-Shortcut `
            -ShortcutPath (Join-Path $shortcutFolder "Winshots Review UI.lnk") `
            -TargetPath $reviewLauncher `
            -Arguments "" `
            -WorkingDirectory $installRootPath
    }

    $powershellExe = Join-Path $env:SystemRoot "System32\WindowsPowerShell\v1.0\powershell.exe"
    New-Shortcut `
        -ShortcutPath (Join-Path $shortcutFolder "Uninstall Winshots.lnk") `
        -TargetPath $powershellExe `
        -Arguments "-NoProfile -ExecutionPolicy Bypass -File `"$installRootPath\uninstall.ps1`"" `
        -WorkingDirectory $installRootPath
}

if (-not $SkipCodexPlugin) {
    if ($null -eq (Get-Command codex -ErrorAction SilentlyContinue)) {
        Write-Warning "Codex CLI was not found on PATH. Skipping Codex plugin registration."
    }
    else {
        Invoke-CodexCommand -Arguments @("plugin", "remove", "winshots@winshots-local", "--json") -IgnoreFailure | Out-Null
        Invoke-CodexCommand -Arguments @("plugin", "marketplace", "remove", "winshots-local", "--json") -IgnoreFailure | Out-Null
        Invoke-CodexCommand -Arguments @("plugin", "marketplace", "add", $installRootPath, "--json") | Out-Null
        Invoke-CodexCommand -Arguments @("plugin", "add", "winshots@winshots-local", "--json") | Out-Null
        Write-Host "Codex plugin installed. Open a new Codex thread for the plugin and MCP tools to load."
    }
}

Write-Host "Winshots installed to $installRootPath"
