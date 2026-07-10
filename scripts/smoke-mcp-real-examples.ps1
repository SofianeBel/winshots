param(
    [switch]$OpenWebExamples,
    [switch]$LaunchDesktopApps,
    [string]$Output = "artifacts\mcp-real-examples",
    [int]$BrowserLoadSeconds = 8
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path "$PSScriptRoot\.."
$dll = Join-Path $root "src\Winshots.Mcp\bin\Debug\net8.0-windows\Winshots.Mcp.dll"

if (-not (Test-Path $dll)) {
    dotnet build (Join-Path $root "src\Winshots.Mcp\Winshots.Mcp.csproj") | Out-Host
}

$outputRoot = [System.IO.Path]::GetFullPath((Join-Path $root $Output))
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null

function Start-WebExample {
    param(
        [string]$Name,
        [string]$Url
    )

    foreach ($browser in @("msedge.exe", "chrome.exe", "brave.exe")) {
        try {
            Start-Process -FilePath $browser -ArgumentList @("--new-window", $Url)
            Write-Host "Opened $Name in $browser"
            return
        }
        catch {
        }
    }

    Start-Process $Url
    Write-Host "Opened $Name in the default browser"
}

function Start-DesktopExample {
    param(
        [string]$Name,
        [string]$Uri
    )

    try {
        Start-Process $Uri
        Write-Host "Requested $Name through $Uri"
    }
    catch {
        Write-Host "SKIP $Name launch: $($_.Exception.Message)"
    }
}

function Invoke-McpBatch {
    param(
        [object[]]$Messages
    )

    $initialize = @{
        jsonrpc = "2.0"
        id = 1
        method = "initialize"
        params = @{
            protocolVersion = "2024-11-05"
            capabilities = @{}
            clientInfo = @{
                name = "winshots-real-examples-smoke"
                version = "1.1.0"
            }
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

    $allMessages = @($initialize) + @($Messages)
    foreach ($message in $allMessages) {
        $process.StandardInput.WriteLine(($message | ConvertTo-Json -Depth 12 -Compress))
        $process.StandardInput.Flush()
        Start-Sleep -Milliseconds 700
    }

    Start-Sleep -Milliseconds 4000
    $process.StandardInput.Close()

    if (-not $process.WaitForExit(30000)) {
        $process.Kill()
        throw "MCP real examples smoke timed out."
    }

    $process.WaitForExit()
    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    if ($process.ExitCode -ne 0) {
        throw $stderr
    }

    $responses = @()
    foreach ($line in ($stdout -split "\r?\n")) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        $responses += $line | ConvertFrom-Json
    }

    return $responses
}

function Get-McpText {
    param(
        [object]$Response
    )

    if ($Response.error) {
        throw ($Response.error | ConvertTo-Json -Depth 10)
    }

    if ($Response.result.isError) {
        $errorText = @($Response.result.content) |
            Where-Object { $_.type -eq "text" } |
            Select-Object -First 1
        throw "MCP tool returned an error: $($errorText.text)"
    }

    $text = @($Response.result.content) |
        Where-Object { $_.type -eq "text" } |
        Select-Object -First 1

    if ($null -eq $text) {
        throw "MCP response did not include text content."
    }

    return $text.text
}

$script:nextId = 2
function New-ToolCall {
    param(
        [string]$Name,
        [hashtable]$Arguments
    )

    $message = @{
        jsonrpc = "2.0"
        id = $script:nextId
        method = "tools/call"
        params = @{
            name = $Name
            arguments = $Arguments
        }
    }
    $script:nextId += 1
    return $message
}

if ($OpenWebExamples) {
    Start-WebExample -Name "YouTube" -Url "https://www.youtube.com/"
    Start-WebExample -Name "Twitter/X" -Url "https://x.com/"
    Start-Sleep -Seconds $BrowserLoadSeconds
}

if ($LaunchDesktopApps) {
    Start-DesktopExample -Name "Discord" -Uri "discord:"
    Start-DesktopExample -Name "Steam" -Uri "steam:"
    Start-Sleep -Seconds 10
}

$listResponses = Invoke-McpBatch @(
    (New-ToolCall "list_windows" @{
        maxCount = 100
    })
)
$listText = Get-McpText ($listResponses | Where-Object { $_.id -ne 1 } | Select-Object -Last 1)
$parsedWindows = $listText | ConvertFrom-Json
$windows = @($parsedWindows | ForEach-Object { $_ })

$examples = @(
    [pscustomobject]@{
        Name = "youtube"
        Label = "YouTube"
        Matcher = { param($window) $window.WindowTitle -match "(?i)YouTube" }
    },
    [pscustomobject]@{
        Name = "twitter-x"
        Label = "Twitter/X"
        Matcher = { param($window) $window.WindowTitle -match "(?i)(Twitter|x\.com|\bX\b)" }
        PreferredMatcher = { param($window) $window.ProcessName -match "(?i)^brave$" -and $window.WindowTitle -match "(?i)(Twitter|x\.com|\bX\b)" }
    },
    [pscustomobject]@{
        Name = "discord"
        Label = "Discord"
        Matcher = { param($window) $window.ProcessName -match "(?i)^Discord" -or $window.WindowTitle -match "(?i)Discord" }
    },
    [pscustomobject]@{
        Name = "steam"
        Label = "Steam"
        Matcher = { param($window) $window.ProcessName -match "(?i)^Steam" -or $window.WindowTitle -match "(?i)Steam" }
    }
)

$captures = @()
foreach ($example in $examples) {
    $matches = @($windows |
        Where-Object { & $example.Matcher $_ } |
        ForEach-Object { $_ })

    $match = $null
    if ($example.PSObject.Properties.Name -contains "PreferredMatcher") {
        $match = $matches |
            Where-Object { & $example.PreferredMatcher $_ } |
            Select-Object -First 1
    }

    if ($null -eq $match) {
        $match = $matches | Select-Object -First 1
    }

    if ($null -eq $match) {
        Write-Host "SKIP $($example.Label): no matching capturable window"
        continue
    }

    Write-Host "Capturing $($example.Label): $($match.WindowTitle) [$($match.ProcessName)]"
    $responses = Invoke-McpBatch @(
        (New-ToolCall "capture_window" @{
            outputRoot = $outputRoot
            windowHandle = $match.WindowHandle
            delayMs = 500
            activateWindow = $true
            maxPreviewCharacters = 1000
        })
    )
    $captureText = Get-McpText ($responses | Where-Object { $_.id -ne 1 } | Select-Object -Last 1)
    try {
        $capture = $captureText | ConvertFrom-Json
    }
    catch {
        throw "MCP capture for $($example.Label) did not return JSON. Response: $captureText"
    }

    foreach ($path in @($capture.ScreenshotPath, $capture.TextPath, $capture.MetadataPath)) {
        if (-not (Test-Path $path)) {
            throw "Missing $($example.Label) artifact: $path"
        }
    }

    $captures += $capture
    Write-Host "OK $($example.Label): $($capture.ScreenshotPath)"
}

if ($captures.Count -eq 0) {
    throw "No real example window was captured. Open one of YouTube, Twitter/X, Discord, or Steam and rerun this script."
}

Write-Host "MCP real examples OK: $($captures.Count) capture(s) under $outputRoot"
