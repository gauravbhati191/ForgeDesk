[CmdletBinding()]
param(
    [ValidateSet("Release")]
    [string] $Configuration = "Release",

    [ValidateSet("win-x64")]
    [string] $Runtime = "win-x64"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path -LiteralPath (Join-Path -Path $PSScriptRoot -ChildPath "..")).Path
$solutionPath = [System.IO.Path]::Combine($repoRoot, "ForgeDesk.sln")
$projectPath = [System.IO.Path]::Combine($repoRoot, "ForgeDesk.App", "ForgeDesk.App.csproj")
$artifactRoot = [System.IO.Path]::Combine($repoRoot, "artifacts")
$publishPath = [System.IO.Path]::Combine($artifactRoot, "publish", $Runtime)
$archivePath = [System.IO.Path]::Combine($artifactRoot, "ForgeDesk-$Runtime.zip")

foreach ($requiredPath in @($solutionPath, $projectPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required packaging input was not found: $requiredPath"
    }
}

$dotnet = $null
foreach ($candidate in @(
        [System.IO.Path]::Combine($repoRoot, ".dotnet", "dotnet.exe"),
        [System.IO.Path]::Combine($repoRoot, ".dotnet", "dotnet")
    )) {
    if (Test-Path -LiteralPath $candidate -PathType Leaf) {
        $dotnet = $candidate
        break
    }
}

if ($null -eq $dotnet) {
    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $dotnetCommand) {
        throw "The dotnet command was not found. Install the SDK pinned by global.json."
    }

    $dotnet = $dotnetCommand.Source
}

function Invoke-DotNet {
    param(
        [Parameter(Mandatory = $true)]
        [string[]] $Arguments
    )

    Write-Host ("> {0} {1}" -f $dotnet, ($Arguments -join " "))
    & $dotnet @Arguments
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "dotnet exited with code $exitCode."
    }
}

Push-Location -LiteralPath $repoRoot
try {
    # Only packaging output is cleaned; user data remains under %LOCALAPPDATA%\ForgeDesk.
    if (Test-Path -LiteralPath $publishPath) {
        Remove-Item -LiteralPath $publishPath -Recurse -Force
    }
    if (Test-Path -LiteralPath $archivePath) {
        Remove-Item -LiteralPath $archivePath -Force
    }

    New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null

    Invoke-DotNet -Arguments @(
        "restore",
        $solutionPath,
        "--locked-mode"
    )

    Invoke-DotNet -Arguments @(
        "publish",
        $projectPath,
        "--configuration",
        $Configuration,
        "--runtime",
        $Runtime,
        "--self-contained",
        "true",
        "--output",
        $publishPath,
        "--no-restore",
        "-p:PublishSingleFile=false",
        "-p:PublishTrimmed=false",
        "-p:PublishAot=false"
    )

    $publishedFiles = @(Get-ChildItem -LiteralPath $publishPath -File -Recurse -ErrorAction Stop)
    if ($publishedFiles.Count -eq 0) {
        throw "The publish directory is empty: $publishPath"
    }

    foreach ($requiredFile in @(
            "ForgeDesk.App.exe",
            "ForgeDesk.App.deps.json",
            "ForgeDesk.App.runtimeconfig.json",
            "e_sqlite3.dll"
        )) {
        $requiredFilePath = [System.IO.Path]::Combine($publishPath, $requiredFile)
        if (-not (Test-Path -LiteralPath $requiredFilePath -PathType Leaf)) {
            throw "The publish output is missing required file '$requiredFile'."
        }
    }

    $forbiddenPattern = '(?i)(\.db(?:-(?:shm|wal))?$|\.log(?:\.\d+)?$|^\.env(?:\..*)?$|^appsettings\.(?:Development|Local)\.json$|(?:^|[._-])secrets?(?:[._-][^/]*)?\.(?:json|xml|config|yaml|yml|txt)$|\.pfx$|\.snk$)'
    $forbiddenFiles = @($publishedFiles | Where-Object { $_.Name -match $forbiddenPattern })
    if ($forbiddenFiles.Count -gt 0) {
        $forbiddenNames = @($forbiddenFiles | ForEach-Object {
                $_.FullName.Substring($publishPath.Length + 1)
            })
        throw "The publish output contains prohibited data files: $($forbiddenNames -join ', ')"
    }

    $archiveSource = [System.IO.Path]::Combine($publishPath, "*")
    Compress-Archive -Path $archiveSource -DestinationPath $archivePath -CompressionLevel Optimal -Force

    $archiveInfo = Get-Item -LiteralPath $archivePath -ErrorAction Stop
    if ($archiveInfo.Length -le 0) {
        throw "The release archive is empty: $archivePath"
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        $archiveEntries = @($archive.Entries)
        if ($archiveEntries.Count -eq 0) {
            throw "The release archive contains no files: $archivePath"
        }

        $archiveForbiddenEntries = @($archiveEntries | Where-Object {
                $_.FullName -match $forbiddenPattern
            })
        if ($archiveForbiddenEntries.Count -gt 0) {
            throw "The release archive contains prohibited data files: $($archiveForbiddenEntries.FullName -join ', ')"
        }
    }
    finally {
        $archive.Dispose()
    }

    Write-Host "Self-contained multi-file publish: $publishPath"
    Write-Host "Release archive: $archivePath"
}
finally {
    Pop-Location
}
