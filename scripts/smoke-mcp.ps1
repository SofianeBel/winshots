param(
    [switch]$Capture,
    [switch]$Session,
    [switch]$Replay,
    [switch]$AgentWatch,
    [string]$Output = "artifacts\mcp-captures"
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path "$PSScriptRoot\.."
$dll = Join-Path $root "src\Winshots.Mcp\bin\Debug\net8.0-windows10.0.19041.0\Winshots.Mcp.dll"

if (-not (Test-Path $dll)) {
    dotnet build (Join-Path $root "src\Winshots.Mcp\Winshots.Mcp.csproj") | Out-Host
}

if ([System.IO.Path]::IsPathRooted($Output)) {
    $outputRoot = [System.IO.Path]::GetFullPath($Output)
}
else {
    $outputRoot = [System.IO.Path]::GetFullPath((Join-Path $root $Output))
}
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
                version = "1.3.2"
            }
        }
    }
)

if ($AgentWatch) {
    $messages += @{
        jsonrpc = "2.0"
        id = 2
        method = "tools/call"
        params = @{
            name = "wait_for_window"
            arguments = @{
                titleContains = "Winshots Agent Watch smoke target that cannot exist"
                timeoutMs = 300
                pollIntervalMs = 100
            }
        }
    }
}
elseif ($Capture) {
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
elseif ($Replay) {
    $messages += @{
        jsonrpc = "2.0"
        id = 2
        method = "tools/call"
        params = @{
            name = "get_instant_replay_status"
            arguments = @{}
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
    if (($Session -and $message.params.name -eq "start_visual_session") -or
        ($Capture -and $message.params.name -eq "capture_active_window")) {
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

if (-not $Capture -and -not $Session -and -not $Replay -and -not $AgentWatch -and $stdout -notmatch "capture_active_window") {
    throw "MCP smoke did not expose capture_active_window."
}

if ($AgentWatch) {
    $callResponse = $null
    foreach ($line in ($stdout -split "\r?\n")) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        try {
            $message = $line | ConvertFrom-Json
            if ($message.id -eq 2) {
                $callResponse = $message
            }
        }
        catch {
        }
    }

    $watchResult = if ($null -ne $callResponse) {
        $callResponse.result.content[0].text | ConvertFrom-Json
    }
    else {
        $null
    }

    if ($null -eq $watchResult -or $watchResult.Outcome -ne "timed_out" -or $null -eq $watchResult.AppliedBounds) {
        throw "MCP Agent Watch response did not return bounded timeout diagnostics. Output: $stdout"
    }

    Write-Host "MCP Agent Watch bounded timeout OK"
}
elseif ($Capture) {
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
elseif ($Replay) {
    if ($stdout -notmatch "FrameCount") {
        throw "MCP Instant Replay status did not include buffer counters."
    }

    Write-Host "MCP Instant Replay host bridge OK"
}
else {
    foreach ($toolName in @("list_windows", "capture_window", "capture_active_window", "list_recent_captures", "read_capture_context", "start_visual_session", "stop_visual_session", "list_visual_sessions", "read_visual_session_context", "get_instant_replay_status", "start_instant_replay", "stop_instant_replay", "save_instant_replay", "wait_for_window", "wait_for_text", "wait_for_change", "wait_for_disappear", "wait_for_stable")) {
        if ($stdout -notmatch $toolName) {
            throw "MCP smoke did not expose $toolName."
        }
    }

    Write-Host "MCP tools OK: window targeting, capture, context, visual session, Instant Replay, and Agent Watch waits"
}
