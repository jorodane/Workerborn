"""Read-only authored content checks. Does not replace Unity import or PlayMode.
Run with Python 3 and PyYAML: python Tests/validate_harvest_assets.py
"""
from pathlib import Path
from collections import defaultdict, Counter
import json
import re
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'Tools'))
from unity_asset_geometry import documents
CONTENT = ROOT / 'Assets/Workerborn'
GUIDS = defaultdict(list)
for meta in (ROOT / 'Assets').rglob('*.meta'):
    match = re.search(r'^guid: ([a-f0-9]{32})$', meta.read_text(), re.M)
    if match: GUIDS[match[1]].append(Path(str(meta)[:-5]))

# Package-owned scripts/shader retain the GUIDs shipped by URP, UGUI and Input System.
PACKAGE_GUIDS = {
    '933532a4fcc9baf4fa0491de14d08ed7', # URP Lit
    'a79441f348de89743a2939f4d699eac1', # UniversalAdditionalCameraData
    '474bcb49853aa07438625e644c072ee6', # UniversalAdditionalLightData
    '172515602e62fb746b5d573b38a5fe58', # Volume
    '0cd44c1031e13a943bb63640046fad76', # CanvasScaler
    'dc42784cf147c0c48a680349fa168899', # GraphicRaycaster
    'fe87c0e1cc204ed48ad3b37840f39efc', # Image
    '5f7201a12d95ffc409449d95f23cf332', # Text
    '4e29b1a8efbd4b44bb3f3716e73f07ff', # Button
    '76c392e42b5098c458856cdf6ecaaaa1', # EventSystem
    '01614664b831546d2ae94a42149d80ac', # InputSystemUIInputModule
}
PACKAGE_GUIDS.update(json.loads((ROOT/'Tools/unity_ui_guids.json').read_text()).values())
for source_settings in (ROOT/'Assets/Settings').glob('*.asset'):
    PACKAGE_GUIDS.update(re.findall(r'guid: ([a-f0-9]{32})',source_settings.read_text()))
CACHE = {}
def guid_for(path): return re.search(r'^guid: ([a-f0-9]{32})$', Path(str(path)+'.meta').read_text(), re.M)[1]
def docs(path):
    if path not in CACHE: CACHE[path] = documents(path)
    return CACHE[path]
def walk(value):
    if isinstance(value, dict):
        if 'fileID' in value: yield value
        else:
            for child in value.values(): yield from walk(child)
    elif isinstance(value,list):
        for child in value: yield from walk(child)
def script(name): return guid_for(CONTENT/'Runtime'/name)
def behaviours(scene, name):
    return [d['MonoBehaviour'] for d in scene if d.get('MonoBehaviour',{}).get('m_Script',{}).get('guid') == script(name)]
def resolve(pointer):
    paths=GUIDS[pointer['guid']]; assert len(paths)==1, pointer
    return paths[0]

def check_pointers(path):
    parsed=docs(path); local={d['_id'] for d in parsed};count=0
    for pointer in walk(parsed):
        identifier=pointer['fileID'];guid=pointer.get('guid')
        if not identifier: continue
        if not guid:
            assert identifier in local, f'Broken local pointer {identifier} in {path}'
        elif guid.startswith('0000000000000000') or guid in PACKAGE_GUIDS: pass
        else:
            target=resolve(pointer);assert target.is_file(),target
            ext=target.suffix.lower();expected=2 if ext in ('.asset','.anim','.mat','.rendertexture') else 3
            assert pointer['type']==expected, f'Wrong pointer type {pointer} for {target}'
            if ext in ('.prefab','.asset','.anim','.mat','.rendertexture'):
                if ext=='.prefab' and identifier==100100000: pass # Unity's prefab asset handle.
                else: assert identifier in {d['_id'] for d in docs(target)}, f'Missing native ID {identifier} in {target}'
            elif ext=='.fbx':
                pattern=f'fileID: {identifier}, guid: {guid}'
                pack=ROOT/'Assets'/target.relative_to(ROOT/'Assets').parts[0]
                witnessed=any(pattern in p.read_text() for p in pack.rglob('*.prefab'))
                canonical=identifier in (919132149155446097,-8679921383154817045,100100000) and 'fileIdsGeneration: 2' in Path(str(target)+'.meta').read_text()
                assert witnessed or canonical, f'Unverified model ID {pattern}'
        count+=1
    return count

files=0;pointers=0
for path in [CONTENT,*CONTENT.rglob('*')]:
    if path.suffix=='.meta': assert Path(str(path)[:-5]).exists(),path;continue
    assert Path(str(path)+'.meta').is_file(),f'Missing metadata: {path}'
    assert len(GUIDS[guid_for(path)])==1,f'Duplicate GUID: {path}'
    if not path.is_file():continue
    files+=1
    if path.suffix=='.asmdef':json.loads(path.read_text())
    if path.suffix in ('.unity','.prefab','.asset','.mat','.rendertexture'): pointers+=check_pointers(path)

scene_path=CONTENT/'Scenes/VillagePrototype.unity'
from resolve_owned_prefabs import expand
scene=expand(scene_path)
ids=[d['_id'] for d in scene];assert len(ids)==len(set(ids)),'Duplicate scene IDs'
transforms={d['_id']:d.get('Transform',d.get('RectTransform')) for d in scene if 'Transform'in d or 'RectTransform'in d}
gos={d['_id']:d['GameObject'] for d in scene if 'GameObject'in d}
roots=next(d['SceneRoots']['m_Roots'] for d in scene if 'SceneRoots'in d)
root_ids={p['fileID'] for p in roots}
for id,t in transforms.items():
    if 'm_GameObject' not in t: continue # stripped vendor prefab root
    assert t['m_GameObject']['fileID'] in gos, f'Orphan transform {id}'
    parent=t['m_Father']['fileID']
    if parent: assert id in [p['fileID'] for p in transforms[parent]['m_Children']],f'Missing hierarchy child {id}'
    else: assert id in root_ids,f'Unregistered scene root {id}'
    assert not gos[t['m_GameObject']['fileID']]['m_Name'].startswith('Workerborn Harvest')
for id,go in gos.items():
    for c in go['m_Component']: assert c['component']['fileID'] in ids,f'Missing component in {go["m_Name"]}'

controller=behaviours(scene,'Village/VillagePrototype.cs');assert len(controller)==1
for field in ['targeting','npcPortrait','npcPortraitTexture','settings','mapRoot','navigation','player','worker','worldCamera','cameraRig','hud','minimapCamera','minimapTexture','portraitCamera','portraitTexture','spawnPoint','restPoint','droppedItems','potato','sun']:
    assert controller[0][field]['fileID'],f'Missing binding: {field}'
assert not behaviours(scene,'HarvestPrototype.cs'),'Village must not generate its map from the harvest demo'
crops=behaviours(scene,'HarvestableCrop.cs');assert len(crops)==24
assert len(set(c['persistentId'] for c in crops))==24,'Duplicated crop save IDs'
assert all(c['definition']['fileID'] and c['visual']['fileID'] for c in crops)
assert len(behaviours(scene,'Village/VillageInteractable.cs'))==5
actors=behaviours(scene,'Characters/CharacterActor.cs');assert len(actors)==4
assert all(a['visualRoot']['fileID'] for a in actors)
assert len(behaviours(scene,'Interaction/PointerTargeting.cs'))==1
assert len(behaviours(scene,'UI/PickupNames.cs'))==1
assert sum(a['playerSettings']['fileID']!=0 for a in actors)==1
stage=behaviours(scene,'World/VillageWorldStage.cs');assert len(stage)==1 and stage[0]['minimumStage']==2
assert gos[stage[0]['m_GameObject']['fileID']]['m_IsActive']==0
agent=next(d['NavMeshAgent'] for d in scene if 'NavMeshAgent'in d);assert agent['m_Enabled']==0
camera=next(d['Camera'] for d in scene if d['_id']==controller[0]['worldCamera']['fileID']);assert camera['orthographic']==0
rig=behaviours(scene,'Village/VillageCameraRig.cs')[0];assert rig['initialPitch']<=25 and rig['maximumDistance']/rig['minimumDistance']>=10
hud=behaviours(scene,'Village/VillageHud.cs')[0]
clock=next(d['MonoBehaviour'] for d in scene if d['_id']==hud['clockText']['fileID'])
assert clock['m_FontData']['m_VerticalOverflow']==1 and clock['m_FontData']['m_FontSize']>=14
assert not any('F1' in go['m_Name'] or 'F2' in go['m_Name'] for go in gos.values())

settings=docs(resolve(controller[0]['settings']))[0]['MonoBehaviour']
itemdefs=[docs(resolve(p))[0]['MonoBehaviour'] for p in settings['items']]
assert {i['id'] for i in itemdefs}=={'potato','bread','iron_hoe','small_bag','gold'}
assert all(i['icon']['fileID'] for i in itemdefs)
assert next(i for i in itemdefs if i['id']=='gold')['maxStack']==1000
assert next(i for i in itemdefs if i['id']=='iron_hoe')['equipmentSlot']==5
for actor in actors:
    definition=docs(resolve(actor['npc']))[0]['MonoBehaviour']['appearance'] if actor['npc']['fileID'] else settings['player']
    appearance=docs(resolve(definition))[0]['MonoBehaviour']
    assert appearance['basePrefab']['fileID'] and all(appearance[k]['fileID'] for k in ['idle','walk','work'])
parts=list((CONTENT/'Data/Characters/Parts').glob('*.asset'));assert len(parts)>=30
assert {docs(p)[0]['MonoBehaviour']['slot'] for p in parts}>={0,1,2,3,4,6}

build=(ROOT/'ProjectSettings/EditorBuildSettings.asset').read_text()
assert re.search(r'path: (.+)',build)[1]==str(scene_path.relative_to(ROOT))
for name in ['VillagePrototype','HarvestPrototype']:
    p=CONTENT/'Scenes'/(name+'.unity');assert str(p.relative_to(ROOT)) in build and guid_for(p) in build
assert (CONTENT/'Fonts/WorkerbornKorean-Regular.otf').read_bytes().startswith(b'OTTO')
print(f'Validated {files} project files, {pointers} pointers, native Scene hierarchy, 24 stable crops, 4 modular characters, content presets, HUD and build entries.')
