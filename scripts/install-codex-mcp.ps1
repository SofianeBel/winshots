param(
    [string]$ConfigPath = "$env:USERPROFILE\.codex\config.toml"
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path "$PSScriptRoot\.."
$dll = Join-Path $root "src\Winshots.Mcp\bin\Debug\net8.0-windows10.0.19041.0\Winshots.Mcp.dll"
if (-not (Test-Path $dll)) {
    dotnet build (Join-Path $root "src\Winshots.Mcp\Winshots.Mcp.csproj") | Out-Host
}

$configDirectory = Split-Path -Parent $ConfigPath
if (-not (Test-Path $configDirectory)) {
    New-Item -ItemType Directory -Force -Path $configDirectory | Out-Null
}

$existing = if (Test-Path $ConfigPath) { Get-Content -Raw $ConfigPath } else { "" }
$backupPath = "$ConfigPath.bak-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
if (Test-Path $ConfigPath) {
    Copy-Item -LiteralPath $ConfigPath -Destination $backupPath
}

$dllForToml = ([System.IO.Path]::GetFullPath($dll)).Replace("\", "/")
$block = @"
[mcp_servers.winshots]
command = "dotnet"
args = ["$dllForToml"]
type = "stdio"
startup_timeout_sec = 20.0
"@

$pattern = "(?ms)^\[mcp_servers\.winshots\]\r?\n.*?(?=^\[|\z)"
if ($existing -match $pattern) {
    $updated = [System.Text.RegularExpressions.Regex]::Replace($existing, $pattern, $block.TrimEnd())
}
else {
    $separator = if ([string]::IsNullOrWhiteSpace($existing)) { "" } else { "`r`n`r`n" }
    $updated = $existing.TrimEnd() + $separator + $block.TrimEnd() + "`r`n"
}

Set-Content -Path $ConfigPath -Value $updated -Encoding UTF8

if (Test-Path $backupPath) {
    Write-Host "Updated $ConfigPath"
    Write-Host "Backup: $backupPath"
}
else {
    Write-Host "Created $ConfigPath"
}

Write-Host "Open a new Codex thread for the MCP server list to refresh."
