param(
    [Parameter(Mandatory=$true)][string]$Java,
    [Parameter(Mandatory=$true)][string]$FfdecJar,
    [Parameter(Mandatory=$true)][string]$SourceSwf,
    [Parameter(Mandatory=$true)][string]$OutputFolder
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $OutputFolder | Out-Null
$sourceXml = Join-Path $OutputFolder 'outdoor-source.xml'
$cleanXml = Join-Path $OutputFolder 'outdoor-clean.xml'
$cleanSwf = Join-Path $OutputFolder 'outdoor-clean.swf'
& $Java '-Djava.awt.headless=true' -jar $FfdecJar -swf2xml $SourceSwf $sourceXml
if ($LASTEXITCODE -ne 0) { throw 'FFDec XML export failed.' }
[xml]$document = Get-Content -LiteralPath $sourceXml -Raw
# StreetBuilding/BuildingItem remove these authoring bounds at runtime; tf_name is replaced with Unity text.
# Edit native vector display-list tags, never raster pixels; no SWF code is executed.
$removed = 0
foreach ($node in @($document.SelectNodes('//*[@name="mc_rect" or @name="tf_name"]'))) {
    if ($node.type -like 'PlaceObject*') {
        [void]$node.ParentNode.RemoveChild($node)
        $removed++
    }
}
$settings = [System.Xml.XmlWriterSettings]::new()
$settings.Encoding = [System.Text.UTF8Encoding]::new($false)
$writer = [System.Xml.XmlWriter]::Create([IO.Path]::GetFullPath($cleanXml), $settings)
try { $document.Save($writer) } finally { $writer.Dispose() }
& $Java '-Djava.awt.headless=true' -jar $FfdecJar -xml2swf $cleanXml $cleanSwf
if ($LASTEXITCODE -ne 0) { throw 'FFDec native vector rebuild failed.' }
if ((Get-Item -LiteralPath $cleanSwf).Length -lt 1024) { throw 'FFDec produced an empty SWF; inspect XML encoding.' }
& $Java '-Djava.awt.headless=true' -jar $FfdecJar -onerror ignore -zoom 2 -export 'sprite,symbolClass' $OutputFolder $cleanSwf
if ($LASTEXITCODE -ne 0) { throw 'FFDec sprite export failed.' }
Write-Output "Removed $removed authoring/name placeholders; sprite exports: $OutputFolder/sprites"
