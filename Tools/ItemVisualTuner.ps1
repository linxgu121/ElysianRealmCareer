param(
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path,
    [string]$Identifier,

    [string]$IconTexture,
    [string]$IconSourceRect,
    [string]$IconOrigin,

    [string]$SpriteTexture,
    [string]$SpriteSourceRect,
    [string]$SpriteOrigin,
    [string]$SpriteDepth,

    [string]$ItemScale,
    [string]$BodyWidth,
    [string]$BodyHeight,

    [string]$Handle1,
    [string]$Handle2,
    [string]$AimPos,
    [string]$HoldPos,
    [string]$HoldAngle,

    [string]$BarrelPos,

    [string]$WearableSpriteName,
    [string]$WearableTexture,
    [string]$WearableSourceRect,
    [string]$WearableOrigin,

    [switch]$NoBackup,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$rootPath = [System.IO.Path]::GetFullPath($Root)
$itemsPath = Join-Path $rootPath "Content\Items"

function Load-XmlDocument([string]$Path) {
    $doc = New-Object System.Xml.XmlDocument
    $doc.PreserveWhitespace = $true
    $doc.Load($Path)
    return $doc
}

function Get-ExistingAttrName($Node, [string]$Name) {
    if ($null -eq $Node) { return "" }
    foreach ($attr in $Node.Attributes) {
        if ($attr.Name -ieq $Name) {
            return $attr.Name
        }
    }
    return ""
}

function Get-Attr($Node, [string]$Name) {
    if ($null -eq $Node) { return "" }
    $actualName = Get-ExistingAttrName $Node $Name
    if ([string]::IsNullOrWhiteSpace($actualName)) { return "" }
    return $Node.GetAttribute($actualName)
}

function Set-AttrIfValue($Node, [string]$Name, [string]$Value, [string]$Label) {
    if ([string]::IsNullOrWhiteSpace($Value)) { return $false }
    if ($null -eq $Node) {
        throw "XML node not found for $Label."
    }

    $oldValue = Get-Attr $Node $Name
    if ($oldValue -eq $Value) { return $false }
    $actualName = Get-ExistingAttrName $Node $Name
    if ([string]::IsNullOrWhiteSpace($actualName)) {
        $actualName = $Name
    }
    $Node.SetAttribute($actualName, $Value)
    Write-Host "SET $Label : '$oldValue' -> '$Value'" -ForegroundColor Cyan
    return $true
}

function Read-Keep([string]$Label, [string]$CurrentValue) {
    if ([string]::IsNullOrWhiteSpace($CurrentValue)) {
        $CurrentValue = "<empty>"
    }
    $value = Read-Host "$Label [$CurrentValue], press Enter to keep"
    if ([string]::IsNullOrWhiteSpace($value)) { return $null }
    return $value
}

function Find-DirectChild($ItemNode, [string]$Name) {
    foreach ($child in $ItemNode.ChildNodes) {
        if ($child.NodeType -eq [System.Xml.XmlNodeType]::Element -and $child.LocalName -ieq $Name) {
            return $child
        }
    }
    return $null
}

function Get-WearableSprites($WearableNode) {
    $result = @()
    if ($null -eq $WearableNode) { return $result }
    foreach ($child in $WearableNode.ChildNodes) {
        if ($child.NodeType -eq [System.Xml.XmlNodeType]::Element -and $child.LocalName -ieq "sprite") {
            $result += $child
        }
    }
    return $result
}

function Find-WearableSprite($WearableNode, [string]$NameOrLimb) {
    if ([string]::IsNullOrWhiteSpace($NameOrLimb)) { return $null }
    foreach ($spriteNode in Get-WearableSprites $WearableNode) {
        $spriteName = Get-Attr $spriteNode "name"
        $limb = Get-Attr $spriteNode "limb"
        if ($spriteName -ieq $NameOrLimb -or $limb -ieq $NameOrLimb) {
            return $spriteNode
        }
    }
    return $null
}

if (-not (Test-Path -LiteralPath $itemsPath)) {
    throw "Content\Items folder not found: $itemsPath"
}

$allItems = @()
foreach ($file in Get-ChildItem -Path $itemsPath -Filter *.xml -Recurse) {
    $doc = Load-XmlDocument $file.FullName
    foreach ($itemNode in $doc.SelectNodes("//Item[@identifier]")) {
        $id = $itemNode.GetAttribute("identifier")
        if ([string]::IsNullOrWhiteSpace($id)) { continue }
        $allItems += [pscustomobject]@{
            Identifier = $id
            File = $file
            Doc = $doc
            Node = $itemNode
        }
    }
}

if ($allItems.Count -eq 0) {
    throw "No Item nodes were found under Content\Items."
}

if ([string]::IsNullOrWhiteSpace($Identifier)) {
    Write-Host ""
    Write-Host "Choose an item to tune:" -ForegroundColor Yellow
    for ($i = 0; $i -lt $allItems.Count; $i++) {
        $relative = $allItems[$i].File.FullName.Substring($rootPath.Length + 1)
        Write-Host ("[{0}] {1} ({2})" -f ($i + 1), $allItems[$i].Identifier, $relative)
    }
    $selection = Read-Host "Enter number or identifier"
    $number = 0
    if ([int]::TryParse($selection, [ref]$number) -and $number -ge 1 -and $number -le $allItems.Count) {
        $Identifier = $allItems[$number - 1].Identifier
    } else {
        $Identifier = $selection
    }
}

$matches = @($allItems | Where-Object { $_.Identifier -ieq $Identifier })
if ($matches.Count -eq 0) {
    throw "Item identifier '$Identifier' was not found."
}
if ($matches.Count -gt 1) {
    throw "Multiple items use identifier '$Identifier'. Please remove duplicates first."
}

$target = $matches[0]
$item = $target.Node
$icon = Find-DirectChild $item "InventoryIcon"
$sprite = Find-DirectChild $item "Sprite"
$body = Find-DirectChild $item "Body"
$holdable = Find-DirectChild $item "Holdable"
$rangedWeapon = Find-DirectChild $item "RangedWeapon"
$wearable = Find-DirectChild $item "Wearable"
$wearableSprite = $null

$editParamNames = @(
    "IconTexture", "IconSourceRect", "IconOrigin",
    "SpriteTexture", "SpriteSourceRect", "SpriteOrigin", "SpriteDepth",
    "ItemScale", "BodyWidth", "BodyHeight",
    "Handle1", "Handle2", "AimPos", "HoldPos", "HoldAngle",
    "BarrelPos",
    "WearableSpriteName", "WearableTexture", "WearableSourceRect", "WearableOrigin"
)
$hasExplicitEdits = $false
foreach ($name in $editParamNames) {
    if ($PSBoundParameters.ContainsKey($name)) {
        $hasExplicitEdits = $true
        break
    }
}

$relativeTargetFile = $target.File.FullName.Substring($rootPath.Length + 1)
Write-Host ""
Write-Host "Tuning item: $($target.Identifier)" -ForegroundColor Yellow
Write-Host "File: $relativeTargetFile"
Write-Host ""
Write-Host "Formats:" -ForegroundColor DarkYellow
Write-Host "  texture: %ModDir%/Assets/Items/example.png"
Write-Host "  sourcerect: x,y,width,height  example: 0,0,1500,1500"
Write-Host "  position: x,y  example: -35,0"
Write-Host ""

if (-not $hasExplicitEdits) {
    $ItemScale = Read-Keep "Item.scale" (Get-Attr $item "scale")

    $IconTexture = Read-Keep "InventoryIcon.texture" (Get-Attr $icon "texture")
    $IconSourceRect = Read-Keep "InventoryIcon.sourcerect" (Get-Attr $icon "sourcerect")
    $IconOrigin = Read-Keep "InventoryIcon.origin" (Get-Attr $icon "origin")

    $SpriteTexture = Read-Keep "Sprite.texture" (Get-Attr $sprite "texture")
    $SpriteSourceRect = Read-Keep "Sprite.sourcerect" (Get-Attr $sprite "sourcerect")
    $SpriteOrigin = Read-Keep "Sprite.origin" (Get-Attr $sprite "origin")
    $SpriteDepth = Read-Keep "Sprite.depth" (Get-Attr $sprite "depth")

    $BodyWidth = Read-Keep "Body.width" (Get-Attr $body "width")
    $BodyHeight = Read-Keep "Body.height" (Get-Attr $body "height")

    if ($null -ne $holdable) {
        $Handle1 = Read-Keep "Holdable.handle1" (Get-Attr $holdable "handle1")
        $Handle2 = Read-Keep "Holdable.handle2" (Get-Attr $holdable "handle2")
        $AimPos = Read-Keep "Holdable.aimpos" (Get-Attr $holdable "aimpos")
        $HoldPos = Read-Keep "Holdable.holdpos" (Get-Attr $holdable "holdpos")
        $HoldAngle = Read-Keep "Holdable.holdangle" (Get-Attr $holdable "holdangle")
    } else {
        Write-Host "No Holdable node found; grip fields skipped." -ForegroundColor DarkYellow
    }

    if ($null -ne $rangedWeapon) {
        $BarrelPos = Read-Keep "RangedWeapon.barrelpos" (Get-Attr $rangedWeapon "barrelpos")
    } else {
        Write-Host "No RangedWeapon node found; barrelpos skipped." -ForegroundColor DarkYellow
    }

    $wearableSprites = @(Get-WearableSprites $wearable)
    if ($wearableSprites.Count -gt 0) {
        Write-Host ""
        Write-Host "Wearable sprites were found. Enter a number/name to edit one, or press Enter to skip." -ForegroundColor Yellow
        for ($i = 0; $i -lt $wearableSprites.Count; $i++) {
            $node = $wearableSprites[$i]
            Write-Host ("[{0}] name={1}, limb={2}, texture={3}" -f ($i + 1), (Get-Attr $node "name"), (Get-Attr $node "limb"), (Get-Attr $node "texture"))
        }
        $selection = Read-Host "Wearable sprite number or name"
        if (-not [string]::IsNullOrWhiteSpace($selection)) {
            $number = 0
            if ([int]::TryParse($selection, [ref]$number) -and $number -ge 1 -and $number -le $wearableSprites.Count) {
                $wearableSprite = $wearableSprites[$number - 1]
                $WearableSpriteName = Get-Attr $wearableSprite "name"
            } else {
                $WearableSpriteName = $selection
                $wearableSprite = Find-WearableSprite $wearable $WearableSpriteName
            }

            if ($null -eq $wearableSprite) {
                throw "Wearable sprite '$WearableSpriteName' was not found."
            }

            $WearableTexture = Read-Keep "Wearable sprite.texture" (Get-Attr $wearableSprite "texture")
            $WearableSourceRect = Read-Keep "Wearable sprite.sourcerect" (Get-Attr $wearableSprite "sourcerect")
            $WearableOrigin = Read-Keep "Wearable sprite.origin" (Get-Attr $wearableSprite "origin")
        }
    }
} else {
    if (-not [string]::IsNullOrWhiteSpace($WearableSpriteName)) {
        $wearableSprite = Find-WearableSprite $wearable $WearableSpriteName
        if ($null -eq $wearableSprite) {
            throw "Wearable sprite '$WearableSpriteName' was not found."
        }
    } elseif (-not [string]::IsNullOrWhiteSpace($WearableTexture) -or
              -not [string]::IsNullOrWhiteSpace($WearableSourceRect) -or
              -not [string]::IsNullOrWhiteSpace($WearableOrigin)) {
        throw "Use -WearableSpriteName together with wearable sprite fields."
    }
}

$changed = $false
$changed = (Set-AttrIfValue $item "scale" $ItemScale "Item.scale") -or $changed

$changed = (Set-AttrIfValue $icon "texture" $IconTexture "InventoryIcon.texture") -or $changed
$changed = (Set-AttrIfValue $icon "sourcerect" $IconSourceRect "InventoryIcon.sourcerect") -or $changed
$changed = (Set-AttrIfValue $icon "origin" $IconOrigin "InventoryIcon.origin") -or $changed

$changed = (Set-AttrIfValue $sprite "texture" $SpriteTexture "Sprite.texture") -or $changed
$changed = (Set-AttrIfValue $sprite "sourcerect" $SpriteSourceRect "Sprite.sourcerect") -or $changed
$changed = (Set-AttrIfValue $sprite "origin" $SpriteOrigin "Sprite.origin") -or $changed
$changed = (Set-AttrIfValue $sprite "depth" $SpriteDepth "Sprite.depth") -or $changed

$changed = (Set-AttrIfValue $body "width" $BodyWidth "Body.width") -or $changed
$changed = (Set-AttrIfValue $body "height" $BodyHeight "Body.height") -or $changed

$changed = (Set-AttrIfValue $holdable "handle1" $Handle1 "Holdable.handle1") -or $changed
$changed = (Set-AttrIfValue $holdable "handle2" $Handle2 "Holdable.handle2") -or $changed
$changed = (Set-AttrIfValue $holdable "aimpos" $AimPos "Holdable.aimpos") -or $changed
$changed = (Set-AttrIfValue $holdable "holdpos" $HoldPos "Holdable.holdpos") -or $changed
$changed = (Set-AttrIfValue $holdable "holdangle" $HoldAngle "Holdable.holdangle") -or $changed

$changed = (Set-AttrIfValue $rangedWeapon "barrelpos" $BarrelPos "RangedWeapon.barrelpos") -or $changed

$changed = (Set-AttrIfValue $wearableSprite "texture" $WearableTexture "Wearable.sprite[$WearableSpriteName].texture") -or $changed
$changed = (Set-AttrIfValue $wearableSprite "sourcerect" $WearableSourceRect "Wearable.sprite[$WearableSpriteName].sourcerect") -or $changed
$changed = (Set-AttrIfValue $wearableSprite "origin" $WearableOrigin "Wearable.sprite[$WearableSpriteName].origin") -or $changed

if (-not $changed) {
    Write-Host "No changes." -ForegroundColor Yellow
    exit 0
}

if ($DryRun) {
    Write-Host "DryRun is enabled; no file was saved." -ForegroundColor Yellow
    exit 0
}

if (-not $NoBackup) {
    $backupPath = $target.File.FullName + ".bak-" + (Get-Date -Format "yyyyMMdd-HHmmss")
    Copy-Item -LiteralPath $target.File.FullName -Destination $backupPath
    Write-Host "Backup created: $backupPath" -ForegroundColor Green
}

$target.Doc.Save($target.File.FullName)
Write-Host "Saved: $relativeTargetFile" -ForegroundColor Green
Write-Host "Recommended next check: powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\ValidateMod.ps1" -ForegroundColor Yellow
