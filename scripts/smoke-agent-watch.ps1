param(
    [string]$Output = "artifacts\agent-watch-real"
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path "$PSScriptRoot\.."
$dll = Join-Path $root "src\Winshots.Mcp\bin\Debug\net8.0-windows\Winshots.Mcp.dll"
if (-not (Test-Path $dll)) {
    dotnet build (Join-Path $root "src\Winshots.Mcp\Winshots.Mcp.csproj") | Out-Host
}

$outputRoot = if ([System.IO.Path]::IsPathRooted($Output)) {
    [System.IO.Path]::GetFullPath($Output)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path $root $Output))
}

$runId = "{0}-{1}" -f (Get-Date -Format "yyyyMMdd-HHmmss-fff"), ([Guid]::NewGuid().ToString("N").Substring(0, 8))
$runRoot = Join-Path $outputRoot $runId
$captureRoot = Join-Path $runRoot "captures"
$reportPath = Join-Path $runRoot "report.json"
New-Item -ItemType Directory -Force -Path $captureRoot | Out-Null

$targetTitle = "Winshots Agent Watch $runId"
$targetSource = @"
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
`$form = [System.Windows.Forms.Form]::new()
`$form.Text = '$targetTitle'
`$form.StartPosition = 'CenterScreen'
`$form.Size = [System.Drawing.Size]::new(640, 320)
`$label = [System.Windows.Forms.Label]::new()
`$label.Text = 'Agent Watch real Windows target'
`$label.AutoSize = `$true
`$label.Location = [System.Drawing.Point]::new(32, 48)
`$form.Controls.Add(`$label)
[System.Windows.Forms.Application]::Run(`$form)
"@
$encodedTarget = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($targetSource))
$targetProcess = $null

try {
    $targetProcess = Start-Process powershell.exe -ArgumentList @(
        "-NoProfile",
        "-EncodedCommand",
        $encodedTarget
    ) -WindowStyle Normal -PassThru

    $messages = @(
        @{
            jsonrpc = "2.0"
            id = 1
            method = "initialize"
            params = @{
                protocolVersion = "2024-11-05"
                capabilities = @{}
                clientInfo = @{
                    name = "winshots-agent-watch-real-smoke"
                    version = "1.3.0"
                }
            }
        },
        @{
            jsonrpc = "2.0"
            id = 2
            method = "tools/call"
            params = @{
                name = "wait_for_window"
                arguments = @{
                    titleContains = $targetTitle
                    timeoutMs = 5000
                    pollIntervalMs = 100
                }
            }
        },
        @{
            jsonrpc = "2.0"
            id = 3
            method = "tools/call"
            params = @{
                name = "capture_window"
                arguments = @{
                    titleContains = $targetTitle
                    outputRoot = $captureRoot
                    delayMs = 0
                    activateWindow = $true
                    maxPreviewCharacters = 1000
                }
            }
        },
        @{
            jsonrpc = "2.0"
            id = 4
            method = "tools/call"
            params = @{
                name = "wait_for_window"
                arguments = @{
                    titleContains = "$targetTitle impossible"
                    timeoutMs = 400
                    pollIntervalMs = 100
                }
            }
        }
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = "dotnet"
    $startInfo.Arguments = "`"$dll`""
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.UseShellExecute = $false

    $mcp = [System.Diagnostics.Process]::Start($startInfo)
    $stderrTask = $mcp.StandardError.ReadToEndAsync()
    $responseLines = [System.Collections.Generic.List[string]]::new()
    foreach ($message in $messages) {
        $mcp.StandardInput.WriteLine(($message | ConvertTo-Json -Depth 12 -Compress))
        $mcp.StandardInput.Flush()
        $responseTask = $mcp.StandardOutput.ReadLineAsync()
        if (-not $responseTask.Wait(10000)) {
            $mcp.Kill()
            throw "Agent Watch MCP response $($message.id) timed out."
        }

        $responseLine = $responseTask.GetAwaiter().GetResult()
        if ([string]::IsNullOrWhiteSpace($responseLine)) {
            throw "Agent Watch MCP response $($message.id) was empty."
        }
        $responseLines.Add($responseLine)
    }
    $mcp.StandardInput.Close()

    if (-not $mcp.WaitForExit(30000)) {
        $mcp.Kill()
        throw "Agent Watch real Windows smoke timed out."
    }

    $mcp.WaitForExit()
    $stdout = $responseLines -join [Environment]::NewLine
    $stderr = $stderrTask.GetAwaiter().GetResult()
    if ($mcp.ExitCode -ne 0) {
        throw $stderr
    }

    $responses = @{}
    foreach ($line in ($stdout -split "\r?\n")) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        $response = $line | ConvertFrom-Json
        if ($null -ne $response.id) {
            $responses[[int]$response.id] = $response
        }
    }

    function Read-ToolResult([int]$id) {
        $response = $responses[$id]
        if ($null -eq $response -or $response.result.isError) {
            throw "MCP tool call $id failed: $($response | ConvertTo-Json -Depth 12 -Compress) Server stderr: $stderr"
        }

        return $response.result.content[0].text | ConvertFrom-Json
    }

    $success = Read-ToolResult 2
    $capture = Read-ToolResult 3
    $timeout = Read-ToolResult 4

    if ($success.Outcome -ne "succeeded") {
        throw "Expected wait_for_window success, got $($success.Outcome)."
    }
    if ($timeout.Outcome -ne "timed_out") {
        throw "Expected wait_for_window timeout, got $($timeout.Outcome)."
    }
    foreach ($path in @($capture.ScreenshotPath, $capture.TextPath, $capture.MetadataPath)) {
        if ([string]::IsNullOrWhiteSpace($path) -or -not (Test-Path $path)) {
            throw "Missing Agent Watch real-smoke artifact: $path"
        }
    }

    $report = [ordered]@{
        Version = "1.3.0"
        RunId = $runId
        TimestampUtc = [DateTime]::UtcNow.ToString("O")
        LocalOnly = $true
        Target = [ordered]@{
            ProcessId = $targetProcess.Id
            WindowTitle = $targetTitle
        }
        Success = $success
        Timeout = $timeout
        Capture = $capture
        Evidence = [ordered]@{
            ReportPath = $reportPath
            CaptureRoot = $captureRoot
            ScreenshotPath = $capture.ScreenshotPath
            TextPath = $capture.TextPath
            MetadataPath = $capture.MetadataPath
        }
    }
    $report | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $reportPath -Encoding UTF8

    Write-Host "Agent Watch real Windows smoke OK"
    Write-Host "Report: $reportPath"
    Write-Host "Screenshot: $($capture.ScreenshotPath)"
    Write-Host "Context: $($capture.TextPath)"
    Write-Host "Metadata: $($capture.MetadataPath)"
}
finally {
    if ($null -ne $targetProcess -and -not $targetProcess.HasExited) {
        Stop-Process -Id $targetProcess.Id -Force
        [void]$targetProcess.WaitForExit(5000)
    }
}
