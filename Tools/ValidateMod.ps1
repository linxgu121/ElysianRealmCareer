param(
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
)

$ErrorActionPreference = "Stop"
$rootPath = [System.IO.Path]::GetFullPath($Root)
$failed = $false

function ReportError([string]$Message) {
    Write-Host "ERR $Message" -ForegroundColor Red
    $script:failed = $true
}

function ReportOk([string]$Message) {
    Write-Host "OK  $Message" -ForegroundColor Green
}

$xmlFiles = @(Get-Item -LiteralPath (Join-Path $rootPath "filelist.xml"))
$xmlFiles += Get-ChildItem -Path (Join-Path $rootPath "Content"), (Join-Path $rootPath "Localization") -Filter *.xml -Recurse

foreach ($file in $xmlFiles) {
    $relative = $file.FullName.Substring($rootPath.Length + 1)
    try {
        [xml](Get-Content -LiteralPath $file.FullName -Raw) | Out-Null
        ReportOk "xml $relative"
    } catch {
        ReportError "xml $relative :: $($_.Exception.Message)"
    }
}

foreach ($file in $xmlFiles) {
    $relative = $file.FullName.Substring($rootPath.Length + 1)
    $text = Get-Content -LiteralPath $file.FullName -Raw
    [regex]::Matches($text, "%ModDir%/[^`"'<>]+") | ForEach-Object {
        $relativePath = $_.Value.Replace("%ModDir%/", "").Replace("/", "\")
        $target = Join-Path $rootPath $relativePath
        if (-not (Test-Path -LiteralPath $target)) {
            ReportError "missing resource $relative :: $($_.Value)"
        }
    }
}

$filelist = Get-Content -LiteralPath (Join-Path $rootPath "filelist.xml") -Raw
$disabledContentTypes = @("LocationTypes", "Factions", "NPCSets", "Missions", "OutpostConfig", "RandomEvents")
foreach ($type in $disabledContentTypes) {
    if ($filelist -match "<$type\b") {
        ReportError "disabled recruitment content is still loaded: $type"
    }
}

$activeText = Get-ChildItem -Path (Join-Path $rootPath "Content"), (Join-Path $rootPath "Localization") -Filter *.xml -Recurse |
    ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }

$archivedOnlyIdentifiers = @("Kevingear", "Aponiagear", "Edengear", "V2Vgear", "Flawlesscrystal")
foreach ($identifier in $archivedOnlyIdentifiers) {
    if (($activeText -join "`n") -match [regex]::Escape($identifier)) {
        ReportError "archived identifier leaked into active content: $identifier"
    }
}

if ($failed) {
    exit 1
}

ReportOk "migration checks passed"
