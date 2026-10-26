# Builds the private prototype street-front catalog from the user's asset export (never committed, never shipped).
# Reads the export's shop-front catalog and sprites and writes, under our own neutral names (ext_<kind>_NN):
#   Assets/_Game/Resources/Art/ExteriorAssets/RefProto/ext_*.png         sprites (git-ignored)
#   Assets/_Game/Resources/Content/malaysia/RefProto/exterior.json       catalog items (merged by ContentLoader)
#   Assets/_Game/Resources/Localization/RefProto/exterior_{en,ms,zh}.json  generic names ("Window 3")
# Walls are repeating textures. Roofs use clean vectors: mc_rect is an authoring marker, not roof or wall art.
# Usage (project root):  powershell -ExecutionPolicy Bypass -File Tools/RefProto/exterior_import.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = (Resolve-Path "$PSScriptRoot/../..").Path
$export = Join-Path $root 'Builds/Exports/GameAssets-0.9.143a'
$catalogXml = Join-Path $export 'configuration/front.xml'
$spriteDir = Join-Path $export 'extracted/exterior/sprites'
if (-not (Test-Path $catalogXml)) { throw "Export not found: $catalogXml" }
$cleanExport = Join-Path $root 'Builds/ReferenceAssets/ExteriorClean'
$java = Join-Path $env:ProgramFiles 'Unity/Hub/Editor/6000.6.4f1/Editor/Data/PlaybackEngines/AndroidPlayer/OpenJDK/bin/java.exe'
$ffdec = Join-Path $root 'Builds/Tools/ffdec-26.4.0/app/ffdec-cli.jar'
if (-not (Test-Path $java)) { $java = (Get-Command java -ErrorAction Stop).Source }
if (-not (Test-Path $ffdec)) { $ffdec = Join-Path $root 'Builds/Tools/ffdec-26.4.0/ffdec-cli.jar' }
if (-not (Test-Path (Join-Path $cleanExport 'outdoor-clean.swf')) -or
    -not (Test-Path (Join-Path $cleanExport 'outdoor-source.xml')) -or
    -not (Test-Path (Join-Path $cleanExport 'sprites'))) {
    & (Join-Path $root 'Tools/ExteriorAssets/prepare_exterior_export.ps1') -Java $java -FfdecJar $ffdec `
        -SourceSwf (Join-Path $export 'originals/outdoor_asset.swf') -OutputFolder $cleanExport
}
$artOut = Join-Path $root 'Assets/_Game/Resources/Art/ExteriorAssets/RefProto'
$contentOut = Join-Path $root 'Assets/_Game/Resources/Content/malaysia/RefProto'
$locOut = Join-Path $root 'Assets/_Game/Resources/Localization/RefProto'
foreach ($d in $artOut, $contentOut, $locOut) { New-Item -ItemType Directory -Force $d | Out-Null }
Get-ChildItem $artOut -Filter 'ext_*.png' | Remove-Item -Force

# Source sprite folder per symbol name.
$symbols = @{}
foreach ($dir in Get-ChildItem $spriteDir -Directory) {
    if ($dir.Name -match '^DefineSprite_\d+_(.+)$') { $symbols[$Matches[1]] = $dir.FullName }
}

$cleanSymbols = @{}
foreach ($dir in Get-ChildItem (Join-Path $cleanExport 'sprites') -Directory) {
    if ($dir.Name -match '^DefineSprite_\d+_(.+)$') { $cleanSymbols[$Matches[1]] = $dir.FullName }
}
# Measure the original vector attachment marker instead of guessing from black pixels.
[xml]$source = Get-Content (Join-Path $cleanExport 'outdoor-source.xml') -Raw -Encoding UTF8
$characters = @{}; $symbolIds = @{}
foreach ($node in $source.SelectNodes('//*[@spriteId or @shapeId]')) {
    $id = if ($node.HasAttribute('spriteId')) { $node.spriteId } else { $node.shapeId }
    $characters[[string]$id] = $node
}
foreach ($node in $source.SelectNodes('//*[@type="SymbolClassTag"]')) {
    for ($i = 0; $i -lt $node.names.item.Count; $i++) { $symbolIds[[string]$node.names.item[$i]] = [string]$node.tags.item[$i] }
}
function Marker-Width($character) {
    if ($null -ne $character.shapeBounds) { return ([double]$character.shapeBounds.Xmax - [double]$character.shapeBounds.Xmin) / 20.0 }
    $placed = @($character.subTags.item | Where-Object { $_.type -like 'PlaceObject*' -and $_.characterId })
    if ($placed.Count -ne 1) { throw 'Unexpected roof attachment marker.' }
    $sx = if ($placed[0].matrix.hasScale -eq 'true') { [double]$placed[0].matrix.scaleX } else { 1.0 }
    return (Marker-Width $characters[[string]$placed[0].characterId]) * [math]::Abs($sx)
}
function Roof-Width([string]$symbol) {
    $marker = $characters[$symbolIds[$symbol]].subTags.item | Where-Object { $_.name -eq 'mc_rect' } | Select-Object -First 1
    if ($null -eq $marker) { throw "Roof marker missing: $symbol" }
    $sx = if ($marker.matrix.hasScale -eq 'true') { [double]$marker.matrix.scaleX } else { 1.0 }
    return (Marker-Width $characters[[string]$marker.characterId]) * [math]::Abs($sx) * 2.0 # PNG export zoom
}

# Source group -> our kind, short id prefix and generic names. Scale: the source building is 418 px wide, ours 800.
$scale = 800.0 / 418.0 # the source's starter building (418 px) is our level-1 front (800 wide): one scale for every piece
$groups = [ordered]@{
    'Tile'              = @{ kind = 'Wall';     prefix = 'wall';   en = 'Wall';          ms = 'Dinding';        zh = '墙面' }
    'Roof'              = @{ kind = 'Roof';     prefix = 'roof';   en = 'Roof';          ms = 'Bumbung';        zh = '屋顶' }
    'Door'              = @{ kind = 'Door';     prefix = 'door';   en = 'Door';          ms = 'Pintu';          zh = '大门' }
    'Window'            = @{ kind = 'Window';   prefix = 'window'; en = 'Window';        ms = 'Tingkap';        zh = '窗户' }
    'Banner'            = @{ kind = 'Sign';     prefix = 'sign';   en = 'Sign';          ms = 'Papan tanda';    zh = '招牌' }
    'Wall Decoration'   = @{ kind = 'WallDecor'; prefix = 'walldecor'; en = 'Wall decor'; ms = 'Hiasan dinding'; zh = '墙饰' }
    'Street Decoration' = @{ kind = 'Ground';   prefix = 'street'; en = 'Street decor';  ms = 'Hiasan jalan';   zh = '街边装饰' }
    'Functional'        = @{ kind = 'Ground';   prefix = 'street'; en = 'Street decor';  ms = 'Hiasan jalan';   zh = '街边装饰' }
}

function Save-Png([System.Drawing.Bitmap]$bmp, [string]$path) { $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png) }

# Copy native pixels unchanged. Clean roof vectors already have the editor's marker removed.
function Copy-Sprite([string]$src, [string]$outPath, [bool]$roof) {
    $bmp = New-Object System.Drawing.Bitmap $src
    $copy = New-Object System.Drawing.Bitmap $bmp.Width, $bmp.Height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($copy); $g.DrawImage($bmp, 0, 0, $bmp.Width, $bmp.Height); $g.Dispose()
    $size = @($bmp.Width, $bmp.Height)
    $bmp.Dispose()
    Save-Png $copy $outPath; $copy.Dispose()
    return $size
}

# Signs carry a "NAME" placeholder in the source art (the game writes the real name over it): paint the dark text in
# the middle of the board with the board's own colour.
function Clear-SignText([string]$path) {
    $src = New-Object System.Drawing.Bitmap $path
    $bmp = New-Object System.Drawing.Bitmap $src; $src.Dispose()
    $x0 = [int]($bmp.Width * 0.27); $x1 = [int]($bmp.Width * 0.73); $y0 = [int]($bmp.Height * 0.24); $y1 = [int]($bmp.Height * 0.80)
    # The board colour = the most common (coarsely quantised) colour in the text box; anything clearly different
    # there (the letters and their soft edges) is painted over with it.
    $counts = @{}
    for ($y = $y0; $y -lt $y1; $y++) { for ($x = $x0; $x -lt $x1; $x++) {
        $c = $bmp.GetPixel($x, $y); if ($c.A -lt 200) { continue }
        $k = ([int][math]::Floor($c.R / 8) * 1024) + ([int][math]::Floor($c.G / 8) * 32) + [int][math]::Floor($c.B / 8)
        $counts[$k] = 1 + [int]$counts[$k] } }
    if ($counts.Count -gt 0) {
        $best = ($counts.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 1).Key
        $fill = [System.Drawing.Color]::FromArgb(255, [math]::Floor($best / 1024) * 8 + 4, ([math]::Floor($best / 32) % 32) * 8 + 4, ($best % 32) * 8 + 4)
        for ($y = $y0; $y -lt $y1; $y++) { for ($x = $x0; $x -lt $x1; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $d = [math]::Abs($c.R - $fill.R) + [math]::Abs($c.G - $fill.G) + [math]::Abs($c.B - $fill.B)
            if ($c.A -gt 60 -and $d -gt 30) { $bmp.SetPixel($x, $y, $fill) } } }
    }
    Save-Png $bmp $path; $bmp.Dispose()
}

[xml]$xml = Get-Content $catalogXml -Raw -Encoding UTF8
$roofIds = @($xml.database.group | Where-Object name -eq 'Roof' | ForEach-Object {
    $_.item | Where-Object { $_.invisible -ne 'true' -and $symbolIds[[string]$_.className] } | ForEach-Object { $symbolIds[[string]$_.className] }
})
$metricsText = & $java '-Djava.awt.headless=true' -cp $ffdec (Join-Path $root 'Tools/ExteriorAssets/ExportRoofMetrics.java') `
    (Join-Path $cleanExport 'outdoor-clean.swf') @roofIds
if ($LASTEXITCODE -ne 0) { throw 'Roof registration metrics export failed.' }
$roofMetrics = $metricsText | ConvertFrom-Json
$items = New-Object System.Collections.ArrayList
$names = @{ en = New-Object System.Collections.ArrayList; ms = New-Object System.Collections.ArrayList; zh = New-Object System.Collections.ArrayList }
$counters = @{}
foreach ($group in $xml.database.group) {
    $map = $groups[$group.name]
    if ($null -eq $map) { continue }
    foreach ($item in $group.item) {
        if ($item.invisible -eq 'true') { continue }
        $dir = $symbols[$item.className]
        if ($null -eq $dir) { continue }
        $frame = Join-Path $dir '1.png'
        if ($map.kind -eq 'Roof') { $frame = Join-Path $cleanSymbols[[string]$item.className] '1.png' }
        if (-not (Test-Path $frame)) { continue }
        $counters[$map.prefix] = 1 + [int]$counters[$map.prefix]
        $n = $counters[$map.prefix]
        $id = '{0}_{1:D2}' -f ("ext_" + $map.prefix), $n
        $png = Join-Path $artOut "$id.png"
        if ($map.kind -eq 'Wall') {
            # Wall tiles keep the source's size relative to the building (the street view repeats them).
            $size = Copy-Sprite $frame $png $false
            $width = [math]::Round($size[0] * $scale); $height = [math]::Round($size[1] * $scale)
        } else {
            $size = Copy-Sprite $frame $png ($map.kind -eq 'Roof')
            if ($map.kind -eq 'Sign') { Clear-SignText $png }
            $width = [math]::Round($size[0] * $scale); $height = [math]::Round($size[1] * $scale)
            # A roof is sized so its upper wall is exactly as wide as the level-1 front; it grows with the front.
            if ($map.kind -eq 'Roof') { $k = 800.0 / (Roof-Width ([string]$item.className)); $width = [math]::Round($size[0] * $k); $height = [math]::Round($size[1] * $k) }
        }
        $price = if ($item.cost) { [int]$item.cost } else { 0 }
        $anchorX = 0.5; $anchorY = 1.0
        if ($map.kind -eq 'Roof') {
            $metric = $roofMetrics.($symbolIds[[string]$item.className])
            # RECT uses 20 twips per Flash pixel; PNG export is 2x. The registration origin
            # can be inside the picture (e.g. nest twigs extend below the wall attachment).
            $anchorX = -[double]$metric.xMin / 10.0 / $size[0]
            $anchorY = -[double]$metric.yMin / 10.0 / $size[1]
        }
        [void]$items.Add([ordered]@{ id = $id; nameKey = "exterior.item.$id"; kind = $map.kind; price = [math]::Max(0, $price);
            width = $width; height = $height; roofAnchorX = $anchorX; roofAnchorY = $anchorY;
            unlockLevel = 1; starterOwned = 0; sprite = "Art/ExteriorAssets/RefProto/$id" })
        foreach ($lang in 'en', 'ms', 'zh') { [void]$names[$lang].Add([ordered]@{ key = "exterior.item.$id"; value = "$($map[$lang]) $n" }) }
    }
}

$utf8 = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText((Join-Path $contentOut 'exterior.json'), (@{ items = $items } | ConvertTo-Json -Depth 5), $utf8)
foreach ($lang in 'en', 'ms', 'zh') {
    [System.IO.File]::WriteAllText((Join-Path $locOut "exterior_$lang.json"), (@{ entries = $names[$lang] } | ConvertTo-Json -Depth 5), $utf8)
}
$byKind = $items | Group-Object { $_.kind } | ForEach-Object { "$($_.Name) $($_.Count)" }
Write-Host "Exterior items $($items.Count): $($byKind -join ', '). Return to Unity to import them."
