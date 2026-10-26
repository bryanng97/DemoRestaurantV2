"""Check the shared skin and LFS checkout without running Unity."""
import json
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
ART = ROOT / "Assets/_Game/Resources/Art/RefProto"
errors = []
count = 0
for mapping in (ROOT / "Tools/RefProto").glob("art_map*.json"):
    for sprite in json.loads(mapping.read_text(encoding="utf-8-sig")).get("sprites", []):
        names = [sprite["key"]]
        if "frames" in sprite:
            first, last = sprite["frames"]
            names = [f'{sprite["key"]}_{i:02d}' for i in range(last - first + 1)]
        for name in names:
            if not (ART / f"{name}.png").is_file():
                errors.append(f"Missing mapped sprite: {name}")
for folder in ROOT.glob("Assets/_Game/Resources/**/RefProto"):
    for path in folder.rglob("*.png"):
        count += 1
        with path.open("rb") as stream:
            if stream.read(8) != b"\x89PNG\r\n\x1a\n":
                errors.append(f"Invalid PNG or unresolved Git LFS pointer: {path.relative_to(ROOT)}")
        if not pathlib.Path(str(path) + ".meta").is_file():
            errors.append(f"Missing Unity metadata: {path.relative_to(ROOT)}")
if not (ART / "skin.json").is_file():
    errors.append("Missing shared skin.json")
loops = ART / "furniture-loops.json"
if not loops.is_file():
    errors.append("Missing furniture animation manifest")
else:
    for hint in json.loads(loops.read_text(encoding="utf-8-sig"))["sprites"]:
        capacity = hint["animationColumns"] * hint["animationRows"]
        for page in range((hint["animationFrames"] + capacity - 1) // capacity):
            if not (ART / f'{hint["key"]}_sheet_{page:02d}.png').is_file():
                errors.append(f'Missing animation sheet: {hint["key"]} page {page}')
print(f"Checked {count} PNG files; {len(errors)} errors.")
for error in errors[:40]:
    print(error)
sys.exit(bool(errors))
