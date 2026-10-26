# Builds the private prototype skin (Art/RefProto) from the asset export zip, using art_map.json.
# Each sprite is cut to its visible pixels and saved under our own art key, plus a skin.json for ArtLibrary.
# An entry with "frames": [first, last] is an animation: every frame is cut with the same box (so they stay
# aligned) and saved as key_00, key_01, ...
# Usage (from the project root, Unity closed or open):  powershell -ExecutionPolicy Bypass -File Tools/RefProto/import.ps1
param([string]$MapFile = "$PSScriptRoot/art_map.json")
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.Drawing

$root = (Resolve-Path "$PSScriptRoot/../..").Path
$map = Get-Content $MapFile -Raw -Encoding UTF8 | ConvertFrom-Json
# "include": more sprite maps next to this one (e.g. the generated catalog from catalog_import.js), if present.
$allSprites = @($map.sprites)
foreach ($inc in @($map.include)) {
    if ([string]::IsNullOrEmpty($inc)) { continue }
    $incPath = Join-Path (Split-Path $MapFile -Parent) $inc
    if (Test-Path $incPath) { $allSprites += @((Get-Content $incPath -Raw -Encoding UTF8 | ConvertFrom-Json).sprites) }
    else { Write-Warning "Included map not found: $incPath (run catalog_import.js first)" }
}
$zipPath = Join-Path $root $map.zip
if (-not (Test-Path $zipPath)) { throw "Asset export not found: $zipPath" }
$out = Join-Path $root $map.output
New-Item -ItemType Directory -Force $out | Out-Null

# Visible-pixel box of a bitmap as @(minX, minY, maxX, maxY), or $null when fully transparent.
function Get-AlphaBounds([System.Drawing.Bitmap]$bmp) {
    $minX = $bmp.Width; $minY = $bmp.Height; $maxX = -1; $maxY = -1
    $rect = New-Object System.Drawing.Rectangle 0, 0, $bmp.Width, $bmp.Height
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bytes = New-Object byte[] ($data.Stride * $bmp.Height)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
    $bmp.UnlockBits($data)
    for ($y = 0; $y -lt $bmp.Height; $y++) {
        $row = $y * $data.Stride
        for ($x = 0; $x -lt $bmp.Width; $x++) {
            if ($bytes[$row + $x * 4 + 3] -gt 8) {
                if ($x -lt $minX) { $minX = $x }; if ($x -gt $maxX) { $maxX = $x }
                if ($y -lt $minY) { $minY = $y }; if ($y -gt $maxY) { $maxY = $y }
            }
        }
    }
    if ($maxX -lt 0) { return $null }
    return @($minX, $minY, $maxX, $maxY)
}

function Save-Cut([System.Drawing.Bitmap]$bmp, $box, [bool]$flip, [string]$path) {
    $rect = New-Object System.Drawing.Rectangle $box[0], $box[1], ($box[2] - $box[0] + 1), ($box[3] - $box[1] + 1)
    $cut = $bmp.Clone($rect, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    if ($flip) { $cut.RotateFlip([System.Drawing.RotateFlipType]::RotateNoneFlipX) }
    $cut.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $size = "$($cut.Width)x$($cut.Height)"
    $cut.Dispose()
    return $size
}

$zip = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $index = @{}
    foreach ($e in $zip.Entries) {
        if ($e.FullName -match '/extracted/([^/]+)/sprites/DefineSprite_\d+_([^/]+)/(\d+)\.png$') {
            $index["$($Matches[1])/$($Matches[2])/$($Matches[3])"] = $e
        }
    }
    function Open-Frame($s, [int]$frame) {
        if ($null -ne $s.sourceDir) {
            $local = Join-Path (Join-Path $root $s.sourceDir) "$frame.png"
            if (-not (Test-Path -LiteralPath $local)) { return $null }
            return New-Object System.Drawing.Bitmap $local
        }
        $entry = $index["$($s.group)/$($s.symbol)/$frame"]
        if ($null -eq $entry) { return $null }
        $stream = $entry.Open()
        try { return New-Object System.Drawing.Bitmap $stream } finally { $stream.Dispose() }
    }

    $hints = @()
    foreach ($s in $allSprites) {
        $range = if ($null -ne $s.frames) { @([int]$s.frames[0], [int]$s.frames[1]) } else { @([int]$s.frame, [int]$s.frame) }
        $bitmaps = @()
        for ($f = $range[0]; $f -le $range[1]; $f++) {
            $bmp = Open-Frame $s $f
            if ($null -eq $bmp) { Write-Warning "Not in export: $($s.group)/$($s.symbol) frame $f (key $($s.key))"; continue }
            $bitmaps += $bmp
        }
        if ($bitmaps.Count -eq 0) { continue }
        # One box for every frame, so animation frames stay aligned.
        $box = $null
        foreach ($bmp in $bitmaps) {
            $b = Get-AlphaBounds $bmp
            if ($null -eq $b) { continue }
            if ($null -eq $box) { $box = $b } else {
                $box = @([math]::Min($box[0], $b[0]), [math]::Min($box[1], $b[1]), [math]::Max($box[2], $b[2]), [math]::Max($box[3], $b[3]))
            }
        }
        if ($null -eq $box) { $box = @(0, 0, ($bitmaps[0].Width - 1), ($bitmaps[0].Height - 1)) }
        # "crop": [x, y, width, height] cuts a fixed region instead (e.g. one repeating piece of a long strip).
        if ($null -ne $s.crop) { $box = @([int]$s.crop[0], [int]$s.crop[1], ([int]$s.crop[0] + [int]$s.crop[2] - 1), ([int]$s.crop[1] + [int]$s.crop[3] - 1)) }
        $isAnim = $null -ne $s.frames
        $first = if ($isAnim) { Get-AlphaBounds $bitmaps[0] } else { $null }
        for ($i = 0; $i -lt $bitmaps.Count; $i++) {
            $name = if ($isAnim) { "{0}_{1:D2}" -f $s.key, $i } else { $s.key }
            $size = Save-Cut $bitmaps[$i] $box ([bool]$s.flipX) (Join-Path $out "$name.png")
            $bitmaps[$i].Dispose()
        }
        Write-Host ("{0,-14} <- {1}/{2} #{3}-{4}  {5}" -f $s.key, $s.group, $s.symbol, $range[0], $range[1], $size)
        $h = [ordered]@{ key = $s.key }
        foreach ($p in 'mirror', 'scale', 'groundCorners', 'surfaceCorners', 'anchor', 'frontCells', 'seatHeight', 'wallLift', 'hand') { if ($null -ne $s.$p) { $h[$p] = $s.$p } }
        if ($null -ne $first -and $null -eq $s.anchor) {
            # Animation frames share one box: record where the first frame (e.g. the closed door) sits in it, so the
            # game can line that frame up and play the rest around it.
            $bw = $box[2] - $box[0] + 1; $bh = $box[3] - $box[1] + 1
            $ax = (($first[0] + $first[2]) / 2.0 - $box[0] + 0.5) / $bw
            if ($s.flipX) { $ax = 1.0 - $ax }
            $h['anchor'] = [ordered]@{ x = [math]::Round($ax, 4); y = [math]::Round(($box[3] - $first[3]) / $bh, 4) }
            $h['firstFrameWidth'] = $first[2] - $first[0] + 1
        }
        if ($h.Count -gt 1) { $hints += [pscustomobject]$h }
    }
    $skin = [ordered]@{}
    foreach ($p in $map.skin.PSObject.Properties) { $skin[$p.Name] = $p.Value }
    $skin['sprites'] = @($hints)
    [System.IO.File]::WriteAllText((Join-Path $out 'skin.json'), ($skin | ConvertTo-Json -Depth 6), (New-Object System.Text.UTF8Encoding $false))
    Write-Host "Skin written to $($map.output). Return to Unity to import it."
} finally { $zip.Dispose() }
