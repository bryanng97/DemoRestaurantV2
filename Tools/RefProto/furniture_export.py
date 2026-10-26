"""Bake the private catalog's nested furniture loops without executing SWF code.

Directions and named kitchen states are selectors, not animation frames. Exported
pixels stay under ignored RefProto/; packed sheets keep Android imports and memory
practical. Run after catalog_import.js, or use --install-only after a completed bake.
"""
import argparse
import json
import pathlib
import subprocess
import shutil
from PIL import Image

ROOT = pathlib.Path(__file__).resolve().parents[2]
TOOLS = ROOT / "Tools/RefProto"
OUT = ROOT / "Builds/ReferenceAssets/FurnitureFrames"
ART = ROOT / "Assets/_Game/Resources/Art/RefProto"
JDK = pathlib.Path("C:/Program Files/Unity/Hub/Editor/6000.6.4f1/Editor/Data/PlaybackEngines/AndroidPlayer/OpenJDK/bin")
JAR = ROOT / "Builds/Tools/ffdec-26.4.0/app/ffdec.jar"
if not (JDK / "java.exe").exists():
    JDK = pathlib.Path(shutil.which("java") or "java").parent
if not JAR.exists():
    JAR = ROOT / "Builds/Tools/ffdec-26.4.0/ffdec.jar"


def install(source=None):
    source = source or OUT / "export"
    manifest = json.loads((source / "furniture-loops.json").read_text())
    ART.mkdir(parents=True, exist_ok=True)
    pages = 0
    for hint in manifest["sprites"]:
        key = hint["key"]
        first = Image.open(source / f"{key}_000.png").convert("RGBA")
        w, h = first.size
        cw, ch = w + 4, h + 4
        columns, rows = max(1, 2048 // cw), max(1, 2048 // ch)
        if cw > 2048 or ch > 2048:
            raise ValueError(f"Native animation frame exceeds the sheet limit: {key}")
        capacity = columns * rows
        count = hint["animationFrames"]
        for start in range(0, count, capacity):
            amount = min(capacity, count - start)
            actual_columns = min(columns, amount)
            actual_rows = (amount + columns - 1) // columns
            sheet = Image.new("RGBA", (actual_columns * cw, actual_rows * ch))
            for offset in range(amount):
                with Image.open(source / f"{key}_{start + offset:03}.png") as frame:
                    sheet.paste(frame, ((offset % columns) * cw + 2, (offset // columns) * ch + 2))
            sheet.save(ART / f"{key}_sheet_{start // capacity:02}.png")
            pages += 1
        hint.update(animationColumns=columns, animationRows=rows, animationWidth=w, animationHeight=h)
    existing_path = ART / "furniture-loops.json"
    existing = json.loads(existing_path.read_text()) if existing_path.exists() else {"sprites": []}
    merged = {h["key"]: h for h in existing["sprites"]}
    merged.update({h["key"]: h for h in manifest["sprites"]})
    existing_path.write_text(json.dumps({"sprites": list(merged.values())}, separators=(",", ":")), encoding="utf-8")
    print(f"Installed {len(manifest['sprites'])} animated views as {pages} private sprite sheets.")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--install-only", action="store_true")
    parser.add_argument("--kitchen-states", action="store_true")
    parser.add_argument("--entertainment-states", action="store_true")
    parser.add_argument("--keys", help="Bake only these comma-separated art keys; keep other installed loops.")
    args = parser.parse_args()
    selected_keys = set(args.keys.split(",")) if args.keys else None
    OUT.mkdir(parents=True, exist_ok=True)
    if args.kitchen_states or args.entertainment_states:
        category = "Entertainment" if args.entertainment_states else "Stove"
        plan_name = "entertainment.tsv" if args.entertainment_states else "kitchen.tsv"
        if not args.install_only:
            catalog=[]
            for relative in ("furniture.json", "RefProto/furniture.json"):
                catalog+=json.loads((ROOT / "Assets/_Game/Resources/Content/malaysia" / relative).read_text())["furniture"]
            wanted={f.get(field) for f in catalog if f["kind"]==category for field in ("sprite","spriteBack")}
            if selected_keys is not None: wanted &= selected_keys
            mappings=json.loads((TOOLS/"art_map.json").read_text())["sprites"]+json.loads((TOOLS/"art_map_catalog.json").read_text())["sprites"]
            seen,plan=set(),[]
            for item in mappings:
                if item["key"] in seen or item["key"] not in wanted or item.get("group")!="interior" or "frame" not in item:continue
                seen.add(item["key"]);plan.append(f"{item['key']}\t{item['symbol']}\t{item['frame']}")
            (OUT/plan_name).write_text("\n".join(plan),encoding="utf-8")
        subprocess.run([str(JDK / "javac.exe"), "-cp", str(JAR), "-d", str(OUT), str(TOOLS / "FurnitureTimelineExporter.java")], check=True)
        for state in (("on", "off", "broken") if args.entertainment_states else ("cooking", "done")):
            source = OUT / f"export-{state}"
            if not args.install_only:
                subprocess.run([str(JDK / "java.exe"), "-Xmx3g", "-Djava.awt.headless=true", "-cp", f"{OUT};{JAR}",
                    "FurnitureTimelineExporter", str(ROOT / "Builds/Exports/GameAssets-0.9.143a/originals/indoor_asset.swf"),
                    str(OUT / plan_name), str(source), "export", state], check=True)
            install(source)
        return
    if not args.install_only:
        catalog = []
        for relative in ("furniture.json", "RefProto/furniture.json"):
            catalog += json.loads((ROOT / "Assets/_Game/Resources/Content/malaysia" / relative).read_text())["furniture"]
        wanted = {f.get(field) for f in catalog for field in ("sprite", "spriteBack")}
        if selected_keys is not None: wanted &= selected_keys
        mappings = json.loads((TOOLS / "art_map.json").read_text())["sprites"]
        mappings += json.loads((TOOLS / "art_map_catalog.json").read_text())["sprites"]
        seen, plan = set(), []
        for item in mappings:
            key = item["key"]
            if key in seen or key not in wanted or item.get("group") != "interior" or "frame" not in item:
                continue
            if item.get("flipX"):
                raise ValueError(f"Add explicit registration support before baking mirrored source pixels: {key}")
            seen.add(key)
            plan.append(f"{key}\t{item['symbol']}\t{item['frame']}")
        (OUT / "plan.tsv").write_text("\n".join(plan), encoding="utf-8")
        subprocess.run([str(JDK / "javac.exe"), "-cp", str(JAR), "-d", str(OUT), str(TOOLS / "FurnitureTimelineExporter.java")], check=True)
        subprocess.run([str(JDK / "java.exe"), "-Xmx3g", "-Djava.awt.headless=true", "-cp", f"{OUT};{JAR}",
                        "FurnitureTimelineExporter", str(ROOT / "Builds/Exports/GameAssets-0.9.143a/originals/indoor_asset.swf"),
                        str(OUT / "plan.tsv"), str(OUT / "export")], check=True)
    install()


if __name__ == "__main__":
    main()
