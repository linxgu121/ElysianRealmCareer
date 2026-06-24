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
$modConfigPath = Join-Path $rootPath "ModConfig.xml"
if (Test-Path -LiteralPath $modConfigPath) {
    $xmlFiles += Get-Item -LiteralPath $modConfigPath
}
$xmlFiles += Get-ChildItem -Path (Join-Path $rootPath "Content"), (Join-Path $rootPath "Localization") -Filter *.xml -Recurse

foreach ($file in $xmlFiles) {
    $relative = $file.FullName.Substring($rootPath.Length + 1)
    try {
        [xml](Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8) | Out-Null
        ReportOk "xml $relative"
    } catch {
        ReportError "xml $relative :: $($_.Exception.Message)"
    }
}

foreach ($file in $xmlFiles) {
    $relative = $file.FullName.Substring($rootPath.Length + 1)
    $text = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8
    [regex]::Matches($text, "%ModDir%/[^`"'<>]+") | ForEach-Object {
        $relativePath = $_.Value.Replace("%ModDir%/", "").Replace("/", "\")
        $target = Join-Path $rootPath $relativePath
        if (-not (Test-Path -LiteralPath $target)) {
            ReportError "missing resource $relative :: $($_.Value)"
        }
    }
}

$filelist = Get-Content -LiteralPath (Join-Path $rootPath "filelist.xml") -Raw -Encoding UTF8
$disabledContentTypes = @("Missions", "RandomEvents")
foreach ($type in $disabledContentTypes) {
    if ($filelist -match "<$type\b") {
        ReportError "disabled recruitment content is still loaded: $type"
    }
}

$activeText = Get-ChildItem -Path (Join-Path $rootPath "Content"), (Join-Path $rootPath "Localization") -Filter *.xml -Recurse |
    ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8 }

$archivedOnlyIdentifiers = @("Kevingear", "Aponiagear", "Edengear", "V2Vgear", "Flawlesscrystal")
foreach ($identifier in $archivedOnlyIdentifiers) {
    if (($activeText -join "`n") -match [regex]::Escape($identifier)) {
        ReportError "archived identifier leaked into active content: $identifier"
    }
}

$npcSets = @{}
Get-ChildItem -Path (Join-Path $rootPath "Content") -Filter *.xml -Recurse | ForEach-Object {
    [xml]$doc = Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8
    $doc.SelectNodes("//npcset") | ForEach-Object {
        $setIdentifier = $_.identifier
        if (-not [string]::IsNullOrWhiteSpace($setIdentifier)) {
            $npcSets[$setIdentifier] = @{}
            $_.SelectNodes("./npc") | ForEach-Object {
                if (-not [string]::IsNullOrWhiteSpace($_.identifier)) {
                    $npcSets[$setIdentifier][$_.identifier] = $true
                }
            }
        }
    }
}

Get-ChildItem -Path (Join-Path $rootPath "Content") -Filter *.xml -Recurse | ForEach-Object {
    $relative = $_.FullName.Substring($rootPath.Length + 1)
    [xml]$doc = Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8
    $doc.SelectNodes("//npc[@from='ElysianRealmNpc']") | ForEach-Object {
        if (-not $npcSets.ContainsKey($_.from) -or -not $npcSets[$_.from].ContainsKey($_.identifier)) {
            ReportError "missing ElysianRealmNpc reference $relative :: $($_.identifier)"
        }
    }
}

$luaCsPath = Join-Path $rootPath "OptionalLuaCs"
if (Test-Path -LiteralPath $luaCsPath) {
    Get-ChildItem -Path $luaCsPath -Filter *.cs -Recurse | ForEach-Object {
        $relative = $_.FullName.Substring($rootPath.Length + 1)
        $text = Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8
        if ($text -match "[^\x00-\x7F]") {
            ReportError "LuaCs C# script should stay ASCII because LuaCs script compilation uses the local default encoding: $relative"
        }
    }
}

if ($failed) {
    exit 1
}

ReportOk "migration checks passed"
