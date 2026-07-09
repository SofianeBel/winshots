param(
    [string]$InstallRoot = "$env:LOCALAPPDATA\Programs\Winshots",
    [switch]$RemoveCodexPlugin,
    [switch]$SkipCodexPlugin
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath([string]$Path) {
    return [System.IO.Path]::GetFullPath([System.Environment]::ExpandEnvironmentVariables($Path))
}

function Invoke-CodexCommand([string[]]$Arguments) {
    $codex = Get-Command codex -ErrorAction SilentlyContinue
    if ($null -eq $codex) {
        return
    }

    & $codex.Source @Arguments | Out-Null
}

$installRootPath = Resolve-FullPath $InstallRoot
$rootPath = [System.IO.Path]::GetPathRoot($installRootPath)

if ([string]::IsNullOrWhiteSpace($installRootPath) -or $installRootPath -eq $rootPath) {
    throw "InstallRoot is not safe: $installRootPath"
}

if ($SkipCodexPlugin) {
    $RemoveCodexPlugin = $false
}

if ($RemoveCodexPlugin) {
    Invoke-CodexCommand -Arguments @("plugin", "remove", "winshots@winshots-local", "--json")
    Invoke-CodexCommand -Arguments @("plugin", "marketplace", "remove", "winshots-local", "--json")
}
else {
    Write-Host "Codex plugin registration left unchanged. Run uninstall.ps1 -RemoveCodexPlugin after closing Codex if you want to remove it."
}

$programsFolder = [Environment]::GetFolderPath("Programs")
$shortcutFolder = Join-Path $programsFolder "Winshots"
if (Test-Path $shortcutFolder) {
    Remove-Item -LiteralPath $shortcutFolder -Recurse -Force
}

if (Test-Path $installRootPath) {
    Remove-Item -LiteralPath $installRootPath -Recurse -Force
}

Write-Host "Winshots uninstalled from $installRootPath"
