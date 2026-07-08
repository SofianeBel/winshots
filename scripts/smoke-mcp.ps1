param(
    [switch]$Capture,
    [switch]$Session,
    [string]$Output = "artifacts\mcp-captures"
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path "$PSScriptRoot\.."
$dll = Join-Path $root "src\Winshots.Mcp\bin\Debug\net8.0-windows\Winshots.Mcp.dll"

if (-not (Test-Path $dll)) {
    dotnet build (Join-Path $root "src\Winshots.Mcp\Winshots.Mcp.csproj") | Out-Host
}

$outputRoot = [System.IO.Path]::GetFullPath((Join-Path $root $Output))
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null

$messages = @(
    @{
        jsonrpc = "2.0"
        id = 1
        method = "initialize"
        params = @{
            protocolVersion = "2024-11-05"
            capabilities = @{}
            clientInfo = @{
                name = "winshots-smoke"
                version = "0.1"
            }
        }
    }
)

if ($Capture) {
    $messages += @{
        jsonrpc = "2.0"
        id = 2
        method = "tools/call"
        params = @{
            name = "capture_active_window"
            arguments = @{
                outputRoot = $outputRoot
                delayMs = 100
                maxPreviewCharacters = 1000
            }
        }
    }
}
elseif ($Session) {
    $messages += @{
        jsonrpc = "2.0"
        id = 2
        method = "tools/call"
        params = @{
            name = "start_visual_session"
            arguments = @{
                outputRoot = $outputRoot
                delayMs = 100
                intervalMs = 500
                maxDurationSeconds = 1
                includeVideo = $false
            }
        }
    }
    $messages += @{
        jsonrpc = "2.0"
        id = 3
        method = "tools/call"
        params = @{
            name = "list_visual_sessions"
            arguments = @{
                outputRoot = $outputRoot
                maxCount = 1
            }
        }
    }
}
else {
    $messages += @{
        jsonrpc = "2.0"
        id = 2
        method = "tools/list"
        params = @{}
    }
}

$psi = [System.Diagnostics.ProcessStartInfo]::new()
$psi.FileName = "dotnet"
$psi.Arguments = "`"$dll`""
$psi.RedirectStandardInput = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.UseShellExecute = $false

$process = [System.Diagnostics.Process]::Start($psi)
$stdoutTask = $process.StandardOutput.ReadToEndAsync()
$stderrTask = $process.StandardError.ReadToEndAsync()

foreach ($message in $messages) {
    $process.StandardInput.WriteLine(($message | ConvertTo-Json -Depth 10 -Compress))
    $process.StandardInput.Flush()
    if ($Session -and $message.params.name -eq "start_visual_session") {
        Start-Sleep -Milliseconds 2500
    }
    else {
        Start-Sleep -Milliseconds 500
    }
}

Start-Sleep -Milliseconds 1000
$process.StandardInput.Close()

if (-not $process.WaitForExit(20000)) {
    $process.Kill()
    throw "MCP smoke timed out."
}

$process.WaitForExit()
$stdout = $stdoutTask.GetAwaiter().GetResult()
$stderr = $stderrTask.GetAwaiter().GetResult()
if ($process.ExitCode -ne 0) {
    throw $stderr
}

if (-not $Capture -and -not $Session -and $stdout -notmatch "capture_active_window") {
    throw "MCP smoke did not expose capture_active_window."
}

if ($Capture) {
    if ($stdout -notmatch "ScreenshotPath") {
        throw "MCP capture response did not include a screenshot path."
    }

    $latest = Get-ChildItem -Path $outputRoot -Directory |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if ($null -eq $latest) {
        throw "No MCP capture directory was created."
    }

    foreach ($name in @("screenshot.png", "context.txt", "metadata.json")) {
        $path = Join-Path $latest.FullName $name
        if (-not (Test-Path $path)) {
            throw "Missing expected artifact: $path"
        }
    }

    Write-Host "MCP capture OK: $($latest.FullName)"
}
elseif ($Session) {
    if ($stdout -notmatch "ContextPath") {
        throw "MCP session response did not include a context path."
    }

    $latest = Get-ChildItem -Path $outputRoot -Directory |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if ($null -eq $latest) {
        throw "No MCP session directory was created."
    }

    foreach ($name in @("session.json", "context.md", "frames.jsonl")) {
        $path = Join-Path $latest.FullName $name
        if (-not (Test-Path $path)) {
            throw "Missing expected session artifact: $path"
        }
    }

    $frame = Get-ChildItem -Path (Join-Path $latest.FullName "frames") -Filter "*.png" |
        Select-Object -First 1
    if ($null -eq $frame) {
        throw "No session frame was captured."
    }

    Write-Host "MCP session OK: $($latest.FullName)"
}
else {
    foreach ($toolName in @("capture_active_window", "list_recent_captures", "read_capture_context", "start_visual_session", "stop_visual_session", "list_visual_sessions", "read_visual_session_context")) {
        if ($stdout -notmatch $toolName) {
            throw "MCP smoke did not expose $toolName."
        }
    }

    Write-Host "MCP tools OK: capture, context, and visual session tools"
}
