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
    'd0353a89b1f911e48b9e16bdc9f2e058', # URP Editor/AssetVersion.cs (material import stamp)
    '639247ca83abc874e893eb93af2b5e44', # Shader Graph BuiltIn/Editor/AssetVersion.cs (multi-target graph import stamp)
    'a79441f348de89743a2939f4d699eac1', # UniversalAdditionalCameraData
    '474bcb49853aa07438625e644c072ee6', # UniversalAdditionalLightData
    '172515602e62fb746b5d573b38a5fe58', # Volume
    '0cd44c1031e13a943bb63640046fad76', # CanvasScaler
    'dc42784cf147c0c48a680349fa168899', # GraphicRaycaster
    'fe87c0e1cc204ed48ad3b37840f39efc', # Image
    'f4688fdb7df04437aeb418b961361dc5', # TextMeshProUGUI
    '71c1514a6bd24e1e882cebbe1904ce04', # TMP_FontAsset
    '2705215ac5b84b70bacc50632be6e391', # TMP_Settings
    '4e29b1a8efbd4b44bb3f3716e73f07ff', # Button
    '76c392e42b5098c458856cdf6ecaaaa1', # EventSystem
    '01614664b831546d2ae94a42149d80ac', # InputSystemUIInputModule
    'fc1515ab76e54f068e2f2207940fab32', # InputActionReference
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
    override_targets=set()
    for d in parsed:
        if 'PrefabInstance'in d:
            instance=d['PrefabInstance']
            assert instance.get('m_SourcePrefab',{}).get('fileID'), f'Broken prefab instance without a source: {path}:{d["_id"]}'
            override_targets.update(id(mod['target']) for mod in instance['m_Modification'].get('m_Modifications',[])
                                    if mod['target'].get('guid')==instance['m_SourcePrefab'].get('guid'))
    raw=path.read_bytes()
    if raw.startswith(b'%YAML'):
        assert not re.search(rb'^\s*m_EditorClassIdentifier: null\s*$',raw,re.M),f'Literal null type identifier: {path}'
    meta=Path(str(path)+'.meta')
    main=re.search(r'mainObjectFileID: (-?\d+)',meta.read_text())if meta.exists()else None
    if main and path.suffix!='.prefab':
        obj=next((d for d in parsed if d['_id']==int(main[1])),None)
        if obj is not None:
            value=next(v for k,v in obj.items()if not k.startswith('_'))
            assert value.get('m_Name')==path.stem,f'Main object name differs from filename: {path}'

    assert all(-(2**63)<=i<2**63 for i in local), f'Object ID outside Unity int64 range: {path}'
    for pointer in walk(parsed):
        identifier=pointer['fileID'];guid=pointer.get('guid')
        assert -(2**63)<=identifier<2**63, f'Pointer ID outside Unity int64 range: {path}'
        if not identifier: continue
        if not guid:
            assert identifier in local, f'Broken local pointer {identifier} in {path}'
        elif guid.startswith('0000000000000000') or guid in PACKAGE_GUIDS: pass
        else:
            target=resolve(pointer);assert target.is_file(),target
            ext=target.suffix.lower();expected=2 if ext in ('.asset','.anim','.mat','.rendertexture','.terrainlayer','.shadervariants') else 3
            # Native binary PPtrs have GUID/path IDs, not Unity YAML's reference type field.
            if raw.startswith(b'%YAML'):
                assert pointer['type']==expected, f'Wrong pointer type {pointer} for {target}'
            if ext in ('.prefab','.asset','.anim','.mat','.rendertexture','.terrainlayer','.shadervariants'):
                if ext=='.prefab' and identifier==100100000: pass # Unity's prefab asset handle.
                elif identifier not in {d['_id'] for d in docs(target)}:
                    # Unity can override children inside a nested vendor prefab using a composed ID
                    # that doesn't appear as a local YAML document in the immediate source.
                    assert ext=='.prefab' and (id(pointer) in override_targets or not raw.startswith(b'%YAML')) and any('PrefabInstance' in d for d in docs(target)), f'Missing native ID {identifier} in {target}'
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
    if path.suffix in ('.unity','.prefab','.asset','.mat','.rendertexture','.shadervariants'): pointers+=check_pointers(path)

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
for field in ['targeting','npcPortrait','npcPortraitTexture','settings','mapRoot','navigation','player','worker','worldCamera','cameraRig','hud','minimapCamera','minimapTexture','spawnPoint','restPoint','droppedItems','potato','sun','farm','guideStage','fateGuide','guideCameraPose']:
    assert controller[0][field]['fileID'],f'Missing binding: {field}'
assert not behaviours(scene,'HarvestPrototype.cs'),'Village must not generate its map from the harvest demo'
crops=behaviours(scene,'HarvestableCrop.cs')
assert Counter(docs(resolve(c['definition']))[0]['MonoBehaviour']['itemId'] for c in crops)=={'potato':24,'egg':2,'timber':3}
assert len(set(c['persistentId'] for c in crops))==len(crops),'Duplicated crop save IDs'
assert all(c['definition']['fileID'] and c['visual']['fileID'] for c in crops)
assert len(behaviours(scene,'Village/VillageInteractable.cs'))==6
assert {s['siteId'] for s in behaviours(scene,'World/VillageFarmSite.cs')}=={'meadow','south'}
actors=behaviours(scene,'Characters/CharacterActor.cs');assert len(actors)==5
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
assert clock['m_Script']['guid']=='f4688fdb7df04437aeb418b961361dc5'
assert clock['m_overflowMode']==0 and clock['m_fontSize']>=14
assert not any('F1' in go['m_Name'] or 'F2' in go['m_Name'] for go in gos.values())

settings=docs(resolve(controller[0]['settings']))[0]['MonoBehaviour']
itemdefs=[docs(resolve(p))[0]['MonoBehaviour'] for p in settings['items']]
assert {i['id'] for i in itemdefs}=={'potato','bread','iron_hoe','small_bag','gold','egg','timber','woodcutting_axe','timber_contract','farm_contract'}
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
print(f'Validated {files} project files, {pointers} pointers, authored fields, gatherables, farm plots, modular characters, content presets, HUD and build entries.')

# Input bindings are an authored asset shared by gameplay, camera and the EventSystem.
actions_path=CONTENT/'Input/Workerborn.inputactions'
actions=json.loads(actions_path.read_text());maps={m['name']:m for m in actions['maps']}
assert set(maps)=={'Gameplay','Camera','UI'}
allids=[a['id']for m in maps.values()for a in m['actions']]+[b['id']for m in maps.values()for b in m['bindings']]
assert len(allids)==len(set(allids))
required={'Gameplay':{'Interact':'<Mouse>/leftButton','Target':'<Keyboard>/leftCtrl','Names':'<Keyboard>/leftAlt'},'Camera':{'OrbitMiddle':'<Mouse>/middleButton','OrbitRight':'<Mouse>/rightButton','Zoom':'<Mouse>/scroll/y'}}
for mapname,bindings in required.items():
    for name,binding in bindings.items():assert any(b['action']==name and b['path']==binding for b in maps[mapname]['bindings'])
assert len(behaviours(scene,'Interaction/WorkerbornInput.cs'))==1
module=next(d['MonoBehaviour']for d in scene if d.get('MonoBehaviour',{}).get('m_Script',{}).get('guid')=='01614664b831546d2ae94a42149d80ac')
assert module['m_ActionsAsset']['guid']==guid_for(actions_path)
for field in ['m_PointAction','m_LeftClickAction','m_RightClickAction','m_MiddleClickAction','m_ScrollWheelAction','m_MoveAction','m_SubmitAction','m_CancelAction']:
    reference=docs(resolve(module[field]))[0]['MonoBehaviour'];assert reference['m_Asset']['guid']==guid_for(actions_path)
    assert reference['m_ActionId'] in {a['id']for a in maps['UI']['actions']}
assert 'activeInputHandler: 1' in (ROOT/'ProjectSettings/ProjectSettings.asset').read_text()
assert len(behaviours(scene,'Rendering/CameraVisibility.cs'))==1
assert 'followSmoothness'not in rig and 'collisionMask'not in rig
for actor in actors:
    go=actor['m_GameObject']['fileID'];assert any(d.get('CapsuleCollider',{}).get('m_GameObject',{}).get('fileID')==go for d in scene)
# Every owned in-game text component uses TMP, with a valid Korean font and SDF material.
texts=0
for p in (CONTENT/'Prefabs/UI').glob('*.prefab'):
    assert '5f7201a12d95ffc409449d95f23cf332'not in p.read_text(),p
    for d in docs(p):
        v=d.get('MonoBehaviour',{})
        if v.get('m_Script',{}).get('guid')!='f4688fdb7df04437aeb418b961361dc5':continue
        assert v['m_fontAsset']['guid']==guid_for(CONTENT/'Fonts/WorkerbornKorean-SDF.asset')
        assert v['m_sharedMaterial']['fileID'] and not v['m_RaycastTarget'];texts+=1
font=next(d['MonoBehaviour']for d in docs(CONTENT/'Fonts/WorkerbornKorean-SDF.asset')if 'MonoBehaviour'in d)
assert font['m_AtlasPopulationMode']==1 and font['m_IsMultiAtlasTexturesEnabled']==1 and font['m_SourceFontFile']['fileID']==12800000
outline=docs(CONTENT/'Fonts/WorkerbornKorean-NameOutline.mat')[0]['Material'];assert 'OUTLINE_ON'in outline['m_ValidKeywords']
assert any(p.get('_OutlineWidth',0)>0 for p in outline['m_SavedProperties']['m_Floats'])
hint=docs(CONTENT/'Prefabs/UI/WorldTooltip.prefab')
assert not any(d.get('MonoBehaviour',{}).get('m_Script',{}).get('guid')=='fe87c0e1cc204ed48ad3b37840f39efc'and d['MonoBehaviour'].get('m_Enabled')for d in hint)
for p in (CONTENT/'Runtime').rglob('*.cs'):
    assert not re.search(r'UnityEngine\.Input(?!System)\b|Keyboard\.current|Mouse\.current|Pointer\.current',p.read_text()),p
print(f'Validated {texts} TMP labels, Korean outlined SDF assets, authored input actions/UI references, camera visibility and interaction capsules.')

# The starter world is an editable Unity Terrain with supplied materials/layers and authored forest instances.
terrains=[d['Terrain']for d in scene if 'Terrain'in d]
assert len(terrains)==1
terrain=terrains[0]
assert resolve(terrain['m_MaterialTemplate']).is_relative_to(ROOT/'Assets/BK')
data=next(d['TerrainData']for d in docs(resolve(terrain['m_TerrainData']))if'TerrainData'in d)
h=data['m_Heightmap'];resolution=h['m_Resolution'];heights=h['m_Heights']
assert len(heights)==resolution*resolution
assert resolve(terrain['m_TerrainData'])==CONTENT/'Terrains/TerrainMeadows.asset'
assert all(resolve(p).is_relative_to(ROOT/'Assets/BK')for p in data['m_SplatDatabase']['m_TerrainLayers'])
assert all(a['m_IsActive']==0 for a in gos.values()if a['m_Name']in ['Ground','Paths']or a['m_Name'].startswith('Soil row'))
nav=behaviours(scene,'World/VillageNavigation.cs')[0]
assert nav['terrain']['fileID'] and nav['buildBounds']['m_Extent']['x']>0
balloon=docs(CONTENT/'Prefabs/UI/SkillBalloon.prefab')
assert not any(d.get('MonoBehaviour',{}).get('m_Script',{}).get('guid')=='f4688fdb7df04437aeb418b961361dc5'for d in balloon)
progress=next(d['MonoBehaviour']for d in balloon if d.get('MonoBehaviour',{}).get('m_Type')==3)
assert progress['m_FillMethod']==4 and progress['m_FillOrigin']==2 and progress['m_FillClockwise']==1
visibility=behaviours(scene,'Rendering/CameraVisibility.cs')[0]
assert visibility['fadeOutSeconds']>0 and visibility['fadeInSeconds']>0 and visibility['containmentPadding']>=.25
assert not any(k in visibility for k in ['fadeShader','fadeVariants','fadeShaders'])
visibility_code=(CONTENT/'Runtime/Rendering/CameraVisibility.cs').read_text()
assert 'new Material(' not in visibility_code and '.sharedMaterials =' not in visibility_code and '.shader =' not in visibility_code
for renderer in ['PC_Renderer.asset','Mobile_Renderer.asset']:
    features=[d['MonoBehaviour']for d in docs(CONTENT/'Rendering'/renderer)if d.get('MonoBehaviour',{}).get('m_Script',{}).get('guid')=='576f949be0794f548b825c9ce6f556db']
    assert {f['settings']['filterSettings']['RenderQueueType']for f in features}=={0}
print('Validated the supplied Meadows Terrain, localized navigation, radial skill icon, stable material opacity and opaque outlines.')

# Validate the active render-pipeline assets, their typed renderer data and the graphics/quality bindings.
for platform in ['PC','Mobile']:
    pipeline=docs(CONTENT/'Rendering'/(platform+'_Pipeline.asset'))[0]['MonoBehaviour']
    index=pipeline['m_DefaultRendererIndex'];renderers=pipeline['m_RendererDataList']
    assert 0<=index<len(renderers) and renderers[index]['fileID']
    renderer_path=resolve(renderers[index]);assert renderer_path==CONTENT/'Rendering'/(platform+'_Renderer.asset')
    renderer=next(d['MonoBehaviour']for d in docs(renderer_path)if d['_id']==renderers[index]['fileID'])
    assert renderer['m_Script']['guid']=='de640fe3d0db1804a85f9fc8f5cadab6'
    assert renderer.get('m_EditorClassIdentifier')in [None,'']
assert guid_for(CONTENT/'Rendering/PC_Pipeline.asset')in(ROOT/'ProjectSettings/GraphicsSettings.asset').read_text()
print('Validated native asset names, empty type identifiers, complete prefab instances and default URP renderer references.')
