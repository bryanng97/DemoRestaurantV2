# Slices the concept asset sheet into individual sprites for Unity.
# Usage (Windows PowerShell 5.1):  powershell -File Tools\SpriteSlicer\slice.ps1 -Sheet <sheet.png|webp> [-OutDir ...]
param(
    [Parameter(Mandatory = $true)][string]$Sheet,
    [string]$Boxes = "",
    [string]$OutDir = "",
    [double]$Tolerance = 34,
    [double]$Feather = 40,
    [string]$Preview = ""
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $Boxes) { $Boxes = "$here\concept_sheet_boxes.txt" }
if (-not $OutDir) { $OutDir = "$here\..\..\Assets\_Game\Resources\Art\Concept" }
Add-Type -AssemblyName System.Drawing, PresentationCore, WindowsBase
Add-Type -Path "$here\SpriteSlicer.cs" -ReferencedAssemblies System.Drawing

$Sheet = (Resolve-Path $Sheet).Path
if ($Sheet -match '\.webp$') {
    # System.Drawing cannot read WebP; decode through WIC and hand over a temporary PNG.
    $png = [IO.Path]::ChangeExtension([IO.Path]::GetTempFileName(), '.png')
    $in = [IO.File]::OpenRead($Sheet)
    $dec = [Windows.Media.Imaging.BitmapDecoder]::Create($in, 'None', 'OnLoad')
    $enc = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add($dec.Frames[0])
    $out = [IO.File]::Create($png); $enc.Save($out); $out.Close(); $in.Close()
    $Sheet = $png
}

New-Item -ItemType Directory -Force $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path
[SpriteSlicer]::Slice($Sheet, (Resolve-Path $Boxes).Path, $OutDir, $Tolerance, $Feather)
if ($Preview) { [SpriteSlicer]::ContactSheet($OutDir, $Preview, 120) }
