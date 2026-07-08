param(
    [string]$Output = "artifacts\measure-captures",
    [int]$Iterations = 3,
    [int]$DelayMs = 250
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path "$PSScriptRoot\.."
$project = Join-Path $root "src\Winshots.App\Winshots.App.csproj"
if ([System.IO.Path]::IsPathRooted($Output)) {
    $resolvedOutput = [System.IO.Path]::GetFullPath($Output)
}
else {
    $resolvedOutput = [System.IO.Path]::GetFullPath((Join-Path $root $Output))
}

New-Item -ItemType Directory -Force -Path $resolvedOutput | Out-Null

$rows = for ($i = 1; $i -le $Iterations; $i++) {
    dotnet run --project $project -- capture-once --output $resolvedOutput --delay-ms $DelayMs | Out-Null

    $latest = Get-ChildItem -Path $resolvedOutput -Directory |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if ($null -eq $latest) {
        throw "No capture directory was created."
    }

    $metadata = Get-Content -Raw (Join-Path $latest.FullName "metadata.json") | ConvertFrom-Json
    [pscustomobject]@{
        Iteration = $i
        TotalMs = $metadata.Metrics.TotalMs
        ScreenshotMs = $metadata.Metrics.ScreenshotMs
        TextMs = $metadata.Metrics.TextExtractionMs
        StorageMs = $metadata.Metrics.StorageWriteMs
        Nodes = $metadata.Metrics.AutomationNodeCount
        TimedOut = $metadata.Metrics.AutomationTimedOut
        Bytes = $metadata.Metrics.ScreenshotBytes
        Directory = $latest.FullName
    }
}

$rows | Format-Table -AutoSize
