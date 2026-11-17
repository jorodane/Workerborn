"""Read-only checks for prototype asset wiring; does not replace a Unity import/play test."""
from pathlib import Path
from collections import defaultdict
import json
import re


ROOT = Path(__file__).resolve().parents[1]
CONTENT = ROOT / "Assets/Workerborn"
SCENE = CONTENT / "Scenes/HarvestPrototype.unity"
guid_paths = defaultdict(list)

for meta in (ROOT / "Assets").rglob("*.meta"):
    match = re.search(r"^guid: ([a-f0-9]{32})$", meta.read_text(), re.M)
    if match:
        guid_paths[match[1]].append(Path(str(meta)[:-5]))


def guid_for(path):
    return re.search(r"^guid: ([a-f0-9]{32})$", Path(str(path) + ".meta").read_text(), re.M)[1]


def validate_pointers(text):
    count = 0
    for file_id, guid, asset_type in re.findall(
        r"\{fileID: (-?\d+), guid: ([a-f0-9]{32}), type: (\d+)\}", text
    ):
        assert len(guid_paths[guid]) == 1, f"Unresolved or ambiguous GUID: {guid}"
        asset = guid_paths[guid][0]
        assert asset.is_file(), f"Missing referenced file: {asset}"
        expected_type = 2 if asset.suffix in (".asset", ".anim", ".mat") else 3
        assert int(asset_type) == expected_type, f"Incorrect pointer type for {asset}"
        if asset.suffix in (".asset", ".anim", ".mat", ".prefab"):
            anchors = re.findall(r"^--- !u!\d+ &(-?\d+)", asset.read_text(), re.M)
            assert file_id in anchors, f"Unresolved native object {file_id} in {asset}"
        elif asset.suffix == ".fbx":
            # Unity assigns these model IDs during import. Check an existing shipped prefab
            # already references the exact same model object ID.
            pattern = f"fileID: {file_id}, guid: {guid}"
            assert any(pattern in prefab.read_text() for prefab in (ROOT / "Assets/stylizedLittleHero/Prefabs").glob("*.prefab")), \
                f"No existing prefab verifies the model root ID: {asset}"
        count += 1
    return count


files = 0
for path in [CONTENT, *CONTENT.rglob("*")]:
    if path.suffix == ".meta":
        assert Path(str(path)[:-5]).exists(), f"Orphan metadata: {path}"
        continue
    assert Path(str(path) + ".meta").is_file(), f"Missing metadata: {path}"
    assert len(guid_paths[guid_for(path)]) == 1, f"Duplicate GUID: {path}"
    if path.is_file():
        files += 1
        if path.suffix == ".asmdef":
            json.loads(path.read_text())

scene_text = SCENE.read_text()
bootstrap_guid = guid_for(CONTENT / "Runtime/HarvestPrototype.cs")
blocks = re.split(r"(?=^--- !u!)", scene_text, flags=re.M)
bootstrap = next(block for block in blocks if f"guid: {bootstrap_guid}" in block)
pointers = validate_pointers(bootstrap)
pointers += validate_pointers((CONTENT / "Data/Potato.asset").read_text())

scene_ids = re.findall(r"^--- !u!\d+ &(-?\d+)", scene_text, re.M)
assert len(scene_ids) == len(set(scene_ids)), "Duplicate scene object IDs"
roots = next(block for block in blocks if "\nSceneRoots:" in block)
for root_id in re.findall(r"fileID: (-?\d+)", roots):
    assert root_id in scene_ids, f"Missing scene root: {root_id}"

build_settings = (ROOT / "ProjectSettings/EditorBuildSettings.asset").read_text()
first_scene = re.search(r"path: (.+)", build_settings)[1]
assert first_scene == str(SCENE.relative_to(ROOT)), "Prototype must be the first build scene"
assert guid_for(SCENE) in build_settings, "Build scene GUID is stale"
assert (CONTENT / "Fonts/WorkerbornKorean-Regular.otf").read_bytes().startswith(b"OTTO"), "Invalid OpenType font"

print(f"Validated {files} prototype files, unique metadata, {pointers} serialized asset pointers, scene roots, build entry and Korean font.")
