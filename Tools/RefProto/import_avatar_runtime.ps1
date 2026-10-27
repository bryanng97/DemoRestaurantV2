param([string]$ExportRoot = (Join-Path $PSScriptRoot '../../Builds/Exports/GameAssets-0.9.143a'))
$ErrorActionPreference='Stop'
$project=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$source=(Resolve-Path -LiteralPath $ExportRoot).Path
$dest=Join-Path $project 'Assets/_Game/Resources/Art/RefProto/AvatarRuntime'
New-Item -ItemType Directory -Path "$dest/Layers" -Force|Out-Null
Copy-Item -LiteralPath "$source/configuration/model.xml" -Destination "$dest/avatar.dae"
$layers=@()
foreach($dir in (Get-ChildItem "$source/extracted/avatars/sprites" -Directory)) {
    if($dir.Name -notmatch '^DefineSprite_(\d+)_(.+)$'){continue}
    $id=[int]$Matches[1];$name=$Matches[2]
    if($name -match 'Icon$'){continue}
    $image=Join-Path $dir.FullName '1.png'
    $shape=Join-Path $source "extracted/avatars/shapes/$($id-1).svg"
    if(!(Test-Path -LiteralPath $image) -or !(Test-Path -LiteralPath $shape)){continue}
    $matrix=[regex]::Match((Get-Content -LiteralPath $shape -Raw),'matrix\(([^)]*)\)')
    if(!$matrix.Success){continue}
    $numbers=$matrix.Groups[1].Value.Split(',')|ForEach-Object {[double]::Parse($_.Trim(),[cultureinfo]::InvariantCulture)}
    Copy-Item -LiteralPath $image -Destination "$dest/Layers/$name.png"
    $layers+=@{name=$name;x=[int][math]::Round(-$numbers[4]);y=[int][math]::Round(-$numbers[5])}
}
[xml]$catalog=Get-Content -LiteralPath "$source/configuration/avatar.xml"
$parts=@()
function Part($slot,$id,$texture,$mesh='') {
    $item=$catalog.SelectNodes('//group/item')|Where-Object {$_.GetAttribute('texture') -eq $texture -and $texture}|Select-Object -First 1
    if(!$item -and $slot -eq 'HairStyle'){$item=$catalog.SelectNodes("//group[@name='Hair']/item")|Where-Object {$_.GetAttribute('object') -eq $mesh -and $_.GetAttribute('invisible') -ne 'true' -and [int]$_.GetAttribute('cost') -ge 0}|Select-Object -First 1}
    $meshes=@();if($mesh){$meshes=@($mesh.Split(',')|ForEach-Object {$_.Trim()})} elseif($item.object){$meshes=@($item.object.Split(',')|ForEach-Object {$_.Trim()})}
    $script:parts+=@{slot=$slot;id=$id;texture=$texture;meshes=$meshes;name=$(if($item){[string]$item.name}else{$id});sourceId=$(if($item){[string]$item.id}else{''});price=$(if($item){[int]$item.cost}else{0})}
    if($slot -in @('Shirt','Pants') -and $meshes.Count -eq 0){throw "Missing original clothing mesh mapping: $slot/$id ($texture)"}
}
Part Gender female Mouth7;Part Gender male Mouth8
Part Eyes round Eyes5;Part Eyes happy Eyes6;Part Eyes sleepy Eyes1;Part Eyes sparkle Eyes19
Part Eyebrows soft EyeBrow04;Part Eyebrows bold EyeBrow01;Part Eyebrows angled EyeBrow02;Part Eyebrows none ''
Part HairStyle bob '' hair01;Part HairStyle short '' hair05;Part HairStyle spiky '' hair02
Part HairStyle long '' hair09;Part HairStyle ponytail '' hair07;Part HairStyle bun '' hair17
Part Hat none '';Part Hat chef Hat101;Part Hat cap Hat201;Part Hat beanie Hat801;Part Hat headband Hat401
Part Hat songkok Hat901;Part Hat tudung Hat501;Part Hat straw_hat Hat301;Part Hat cat_hood Hat402
Part Shirt vest Shirt13;Part Shirt tee Shirt2;Part Shirt collared Shirt5;Part Shirt apron Shirt3;Part Shirt kurung Shirt15;Part Shirt melayu Shirt8
Part Pants shorts Pants2;Part Pants trousers Pants15;Part Pants skirt Pants19;Part Pants sarong Pants20
$animations=@(
    @{name='walk';from=0.0;count=4;step=.1},@{name='carry';from=1.0;count=4;step=.1},
    @{name='hold';from=1.6;count=1;step=.1},@{name='idle';from=4.4;count=15;step=.2},
    @{name='cook';from=3.0;count=4;step=.1},@{name='sit';from=3.5;count=1;step=.1},
    @{name='eat';from=3.5;count=8;step=.1},@{name='clean';from=14.1;count=4;step=.1;props=@('bucket_PIVOT','brush')},
    @{name='fix';from=13.6;count=4;step=.1;props=@('repair')})
$manifest=@{layers=$layers;parts=$parts;animations=$animations;source='Restaurant City preservation archive 0.9.143a; native model, avatar.xml and extracted avatar texture layers. See Docs/team-assets.md.'}
[IO.File]::WriteAllText("$dest/native-avatar.json",(ConvertTo-Json $manifest -Depth 8),[Text.UTF8Encoding]::new($false))
$keys=@{BananaPeel='trash_banana_peel';PizzaSlice='trash_pizza_slice';SodaCan='trash_soda_can';ChickenLeg='trash_chicken_bone';AppleCore='trash_apple_core'}
$trash=@()
foreach($name in $keys.Keys){
    $dir=Get-ChildItem "$source/extracted/interior/sprites" -Directory|Where-Object Name -Match ("_"+$name+'$')|Select-Object -First 1
    if(!$dir){throw "Missing original trash sprite: $name"}
    $src=Join-Path $dir.FullName '1.png';$target=Join-Path (Split-Path $dest) ($keys[$name]+'.png')
    Copy-Item -LiteralPath $src -Destination $target
    $trash+=@{key=$keys[$name];source=$dir.Name;sha256=(Get-FileHash -LiteralPath $src).Hash.ToLowerInvariant()}
}
[IO.File]::WriteAllText((Join-Path (Split-Path $dest) 'trash-provenance.json'),(ConvertTo-Json @{sprites=$trash;source='Original interior SWF; names confirmed in Dippys/Restaurant-City-client WorldRestaurantPlay.as DEFAULT_TRASH_ITEMS'} -Depth 4),[Text.UTF8Encoding]::new($false))
Write-Output "Imported $($layers.Count) native avatar layers and $($trash.Count) original trash sprites."
