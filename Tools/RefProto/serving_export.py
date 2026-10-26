"""Re-export native meal timelines while holding mc_plate at each recipe level.

The general sprite export advances nested plate/coaster timelines. In the game,
DishOrder stops that child at recipe.level. Work on private SWF copies, never the
original archive. Requires the existing FFDec CLI and Unity's bundled Java.
Run before import.ps1; all generated art and maps stay private and git-ignored.
"""
import copy
import json
import pathlib
import subprocess
import shutil
import xml.etree.ElementTree as ET
import zipfile


ROOT = pathlib.Path(__file__).resolve().parents[2]
TOOLS = pathlib.Path(__file__).resolve().parent
OUT = ROOT / "Builds/ReferenceAssets/ServingFrames"
JAVA = pathlib.Path("C:/Program Files/Unity/Hub/Editor/6000.6.4f1/Editor/Data/PlaybackEngines/AndroidPlayer/OpenJDK/bin/java.exe")
FFDEC = ROOT / "Builds/Tools/ffdec-26.4.0/app/ffdec-cli.jar"
if not JAVA.exists():
    JAVA = pathlib.Path(shutil.which("java") or "java")
if not FFDEC.exists():
    FFDEC = ROOT / "Builds/Tools/ffdec-26.4.0/ffdec-cli.jar"


def main():
    mapping = json.loads((TOOLS / "art_map.json").read_text(encoding="utf-8"))
    meals = [s for s in mapping["sprites"] if s["key"].startswith("serve_")]
    OUT.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(ROOT / mapping["zip"]) as archive:
        member = next(n for n in archive.namelist() if n.endswith("/originals/game_asset.swf"))
        source = OUT / "source.swf"
        source.write_bytes(archive.read(member))
    with (OUT / "export.log").open("w", encoding="utf-8") as log:
        def run(*args):
            subprocess.run([str(JAVA), "-Djava.awt.headless=true", "-jar", str(FFDEC), *map(str,args)],
                           cwd=ROOT, stdout=log, stderr=subprocess.STDOUT, check=True)

        xml = OUT / "source.xml"
        run("-swf2xml",source,xml)
        tree = ET.parse(xml)
        items = list(tree.getroot().iter("item"))
        plate_ids = {i.get("characterId") for i in items if i.get("name")=="mc_plate" and i.get("characterId")}
        symbols = {}
        for item in items:
            if item.get("type")=="SymbolClassTag":
                symbols.update({n.text:t.text for t,n in zip(item.find("tags"),item.find("names"))})
        selected = ",".join(symbols[s["symbol"]] for s in meals)
        plates = []
        for item in items:
            if item.get("type")!="DefineSpriteTag" or item.get("spriteId") not in plate_ids:
                continue
            frames, frame = [], []
            for tag in item.find("subTags"):
                frame.append(copy.deepcopy(tag))
                if tag.get("type")=="ShowFrameTag": frames.append(frame); frame=[]
            if len(frames)!=10: raise ValueError("Expected ten plate/coaster grades.")
            plates.append((item,frames))
        if not plates: raise ValueError("No mc_plate timelines found.")
        generated = []
        for level in range(1,11):
            for item,frames in plates:
                tags=item.find("subTags")
                for tag in list(tags): tags.remove(tag)
                # Every plate-grade frame replaces the same depth. Start with its placed
                # object, rather than a RemoveObject against an empty display list.
                for tag in frames[level-1]:
                    if not tag.get("type","").startswith("RemoveObject"):
                        tags.append(copy.deepcopy(tag))
                item.set("frameCount","1")
            fixed_xml, fixed_swf = OUT/"fixed.xml", OUT/"fixed.swf"
            tree.write(fixed_xml,encoding="utf-8",xml_declaration=True)
            run("-xml2swf",fixed_xml,fixed_swf)
            directory=OUT/f"level{level:02}"
            # The complete export uses zoom 2; keep that same pixels-per-tile scale.
            run("-selectid",selected,"-zoom","2","-export","sprite",directory,fixed_swf)
            for meal in meals:
                s={k:copy.deepcopy(v) for k,v in meal.items()}
                s["key"]+=f"_level{level:02}"
                folder=directory/f"DefineSprite_{symbols[s['symbol']]}_{s['symbol']}"
                if len(list(folder.glob("*.png")))!=s["frames"][1]:
                    raise ValueError(f"Missing eating frames: {s['key']}")
                s["sourceDir"]=folder.relative_to(ROOT).as_posix()
                generated.append(s)
            print(f"Exported recipe plate grade {level}: {len(meals)} meal sequences",flush=True)
    (TOOLS/"art_map_servings.json").write_text(json.dumps({"sprites":generated},indent=2)+"\n",encoding="utf-8")


if __name__=="__main__": main()
