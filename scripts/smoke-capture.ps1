param(
    [string]$Output = "artifacts\smoke-captures",
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
$existingDirectories = @(
    Get-ChildItem -Path $resolvedOutput -Directory | ForEach-Object { $_.FullName }
)
dotnet run --project $project -- capture-once --output $resolvedOutput --delay-ms $DelayMs
if ($LASTEXITCODE -ne 0) {
    throw "Winshots capture command failed with exit code $LASTEXITCODE."
}

$latest = Get-ChildItem -Path $resolvedOutput -Directory |
    Where-Object { $_.FullName -notin $existingDirectories } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if ($null -eq $latest) {
    throw "No capture directory was created."
}

foreach ($name in @("screenshot.png", "context.txt", "metadata.json")) {
    $path = Join-Path $latest.FullName $name
    if (-not (Test-Path $path)) {
        throw "Missing expected artifact: $path"
    }
}

Write-Host "Smoke capture OK: $($latest.FullName)"
