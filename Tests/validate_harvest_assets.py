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
for folder in ('Assets', 'Packages'):
    for meta in (ROOT / folder).rglob('*.meta'):
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
                importer=Path(str(target)+'.meta').read_text()
                canonical=identifier in (919132149155446097,-8679921383154817045,100100000) and 'fileIdsGeneration: 2' in importer
                clip=bool(re.search(rf'^\s+(?:74|internalID): {identifier}\s*$',importer,re.M))
                assert witnessed or canonical or clip, f'Unverified model ID {pattern}'
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
opaque_instances={d['_id']:d['PrefabInstance'] for d in scene if 'PrefabInstance' in d}
roots=next(d['SceneRoots']['m_Roots'] for d in scene if 'SceneRoots'in d)
root_ids={p['fileID'] for p in roots}
for id,t in transforms.items():
    if 'm_GameObject' not in t: continue # stripped vendor prefab root
    assert t['m_GameObject']['fileID'] in gos, f'Orphan transform {id}'
    parent=t['m_Father']['fileID']
    if parent:
        parent_transform=transforms[parent]
        if 'm_Children' in parent_transform:
            assert id in [p['fileID'] for p in parent_transform['m_Children']],f'Missing hierarchy child {id}'
        else:
            overrides=opaque_instances[parent_transform['m_PrefabInstance']['fileID']]['m_Modification']
            assert any(a['addedObject']['fileID']==id and a['targetCorrespondingSourceObject']==parent_transform['m_CorrespondingSourceObject']
                       for a in overrides.get('m_AddedGameObjects',[])),f'Missing override adding {id} to a vendor rig'
    else: assert id in root_ids,f'Unregistered scene root {id}'
    assert not gos[t['m_GameObject']['fileID']]['m_Name'].startswith('Workerborn Harvest')
for id,go in gos.items():
    for c in go['m_Component']: assert c['component']['fileID'] in ids,f'Missing component in {go["m_Name"]}'

controller=behaviours(scene,'Village/VillagePrototype.cs');assert len(controller)==1
for field in ['targeting','npcPortrait','npcPortraitTexture','settings','mapRoot','navigation','player','worker','worldCamera','cameraRig','hud','mapRegion','spawnPoint','restPoint','droppedItems','potato','lighting','effects','farm','rebirthZone']:
    assert controller[0][field]['fileID'],f'Missing binding: {field}'
map_region=next(d['MonoBehaviour'] for d in scene if d['_id']==controller[0]['mapRegion']['fileID'])
assert map_region in behaviours(scene,'World/VillageMapRegion.cs')
assert map_region['width']>0 and map_region['depth']>0
assert 'Minimap camera' not in {go['m_Name'] for go in gos.values()}
for prefab in ('VillageHUD', 'MapWindow'):
    ui=docs(CONTENT/f'Prefabs/UI/{prefab}.prefab')
    view=behaviours(ui,'UI/VillageMapView.cs')[0]
    assert all(resolve(view[field]).suffix=='.png' for field in ('npcIcon','plotIcon','facilityIcon'))
    image=next(d['MonoBehaviour'] for d in ui if d['_id']==view['map']['fileID'])
    assert not image['m_Texture']['fileID']
zone=behaviours(scene,'World/RebirthZone.cs')[0]
assert all(zone[field]['fileID'] for field in ('navigation','spawn','guide'))
assert zone['navigation']!=controller[0]['navigation']
cloud_navigation=next(d['MonoBehaviour'] for d in scene if d['_id']==zone['navigation']['fileID'])
cloud_collider=next(d['BoxCollider'] for d in scene if d['_id']==cloud_navigation['ground']['fileID'])
assert cloud_collider['m_Enabled'] and not cloud_collider['m_IsTrigger']
cloud_zone=docs(CONTENT/'Prefabs/World/RebirthZone.prefab')
assert not any('MeshRenderer' in d or 'MeshFilter' in d for d in cloud_zone)
cloud_instances=[d['PrefabInstance'] for d in cloud_zone if 'PrefabInstance' in d
                 and resolve(d['PrefabInstance']['m_SourcePrefab']).stem!='FateGuide']
assert cloud_instances
vendor_clouds=ROOT/'Assets/Synty/PolygonNature/Prefabs/Props'
for instance in cloud_instances:
    source=resolve(instance['m_SourcePrefab'])
    assert source.parent==vendor_clouds and source.stem.startswith('SM_Prop_Cloud_')
    assert not any(m['propertyPath'].startswith(('m_Materials','m_Mesh'))
                   for m in instance['m_Modification']['m_Modifications'])
assert len(behaviours(scene,'World/SeatSlot.cs')) >= 4
lighting=behaviours(scene,'World/VillageLighting.cs')[0]
assert lighting['sun']['fileID'] and lighting['nightIntensity']>.6
assert len(lighting['lanterns'])>=4 and all(lamp['fileID'] for lamp in lighting['lanterns'])
weather=behaviours(scene,'World/VillageWeather.cs')[0]
assert lighting['weather']['fileID'] and weather['sphere']['fileID']
forecast={resolve(v['profile']).stem:v['weight'] for v in weather['forecast']}
assert forecast['Clear']>sum(forecast.values())/2 and forecast['Partly Cloudy']>0 and forecast['Light Rain']>0
cozy=next(d['PrefabInstance'] for d in docs(CONTENT/'Prefabs/World/VillageWeather.prefab') if 'PrefabInstance' in d)
assert resolve(cozy['m_SourcePrefab']).is_relative_to(ROOT/'Packages/com.distantlands.cozy.core')
settings_overrides={m['propertyPath']:m for m in cozy['m_Modification']['m_Modifications']}
clock_profile=docs(resolve(settings_overrides['perennialProfile']['objectReference']))[0]['MonoBehaviour']
assert clock_profile['pauseTime'] and not clock_profile['resetTimeOnStart']
assert int(settings_overrides['ecosystem.weatherSelectionMode']['value'])==0
assert int(settings_overrides['handleSceneLighting']['value'])==0
sky_layers=[m for m in cozy['m_Modification']['m_Modifications'] if m['propertyPath']=='m_Layer']
assert sky_layers and all(int(m['value'])==12 for m in sky_layers)
effects=behaviours(scene,'World/VillageEffects.cs')[0]
effect_library=documents(resolve(effects['library']))[0]['MonoBehaviour']
assert {row['effect'] for row in effect_library['effects']}==set(range(6))
assert len(effect_library['effects'])==6
assert all(resolve(row['prefab']).is_relative_to(CONTENT/'Prefabs/Effects') for row in effect_library['effects'])
assert not behaviours(scene,'HarvestPrototype.cs'),'Village must not generate its map from the harvest demo'
animals=behaviours(scene,'Village/AnimalNpc.cs')
assert len(animals)==2 and all(a['keepVisualWhenDepleted'] for a in animals)
encounters=[a for a in behaviours(scene,'Interaction/MonsterInteractable.cs') if a.get('combat',{}).get('fileID')]
assert len(encounters)==1 and encounters[0]['persistentId']
rabbit_profile=docs(resolve(encounters[0]['combat']))[0]['MonoBehaviour']
assert rabbit_profile['health']>0 and rabbit_profile['counterDelay']>0
assert resolve(rabbit_profile['loot']).stem=='rabbit_meat' and rabbit_profile['lootCount']>0
logging_trees=behaviours(scene,'LoggingTree.cs')
assert len(logging_trees)==3 and all(resolve(tree['branch']).stem=='branch' for tree in logging_trees)
crops=behaviours(scene,'HarvestableCrop.cs')+animals+logging_trees
assert Counter(docs(resolve(c['definition']))[0]['MonoBehaviour']['itemId'] for c in crops)=={'potato':24,'egg':2,'timber':3}
assert len(set(c['persistentId'] for c in crops))==len(crops),'Duplicated crop save IDs'
assert all(c['definition']['fileID'] and c['visual']['fileID'] for c in crops)
work_clips={}
for p in ['Potato.asset','Gathering/GatherEggs.asset','Gathering/CutTimber.asset','Gathering/PondFishing.asset']:
    crop=docs(CONTENT/'Data'/p)[0]['MonoBehaviour']
    work=docs(resolve(crop['workAnimation']))[0]['MonoBehaviour']
    work_clips[crop['itemId']]=[resolve(work[k]) for k in ('begin','loop','finish') if work[k]['fileID']]
assert all(p.is_relative_to(ROOT/'Assets/Kevin Iglesias') for clips in work_clips.values() for p in clips)
assert len(work_clips['timber'])==3 and all('TreeChopping' in p.stem for p in work_clips['timber'])
assert len(work_clips['pond_fish'])==3 and all('Fishing' in p.stem for p in work_clips['pond_fish'])
assert all('Gathering' in work_clips[item][0].stem for item in ('potato','egg'))
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
clock_view=next(d['MonoBehaviour'] for d in scene if d['_id']==hud['clock']['fileID'])
clock=next(d['MonoBehaviour'] for d in scene if d['_id']==clock_view['timeText']['fileID'])
assert clock['m_Script']['guid']=='f4688fdb7df04437aeb418b961361dc5'
assert clock['m_overflowMode']==0 and clock['m_fontSize']>=14
assert not any('F1' in go['m_Name'] or 'F2' in go['m_Name'] for go in gos.values())

settings=docs(resolve(controller[0]['settings']))[0]['MonoBehaviour']
skills=[docs(resolve(pointer))[0]['MonoBehaviour'] for pointer in settings['skills']]
assert {skill['skill'] for skill in skills}==set(range(6))
assert all(0<=row['fromRank']<=row['throughRank']<=100 for skill in skills for row in skill['rankEffects'])
itemdefs=[docs(resolve(p))[0]['MonoBehaviour'] for p in settings['items']]
wardrobe={'field_cap':1,'field_coat':2,'field_trousers':8,'field_boots':4,'field_gloves':3}
assert {i['id'] for i in itemdefs}=={'potato','bread','iron_hoe','small_bag','gold','egg','timber','woodcutting_axe','legend_axe','timber_contract','farm_contract','rabbit_meat','branch','fishing_rod','pond_fish'} | set(wardrobe)
assert all(i['icon']['fileID'] for i in itemdefs)
assert next(i for i in itemdefs if i['id']=='gold')['maxStack']==1000
assert next(i for i in itemdefs if i['id']=='iron_hoe')['equipmentSlot']==5
assert next(i for i in itemdefs if i['id']=='small_bag')['equipmentSlot']==9
plots=behaviours(scene,'World/VillagePlot.cs')
assert len(plots)==5 and len({plot['plotId'] for plot in plots})==len(plots)
assert Counter((p['width'],p['depth'],p['ground']) for p in plots)=={(1,1,0):1,(1,2,0):2,(2,2,0):1,(1,2,1):1}
assert all(p['sign']['fileID'] and p['frontMarker']['fileID'] for p in plots)
facilities=[docs(resolve(p))[0]['MonoBehaviour'] for p in settings['plotFacilities']]
assert {f['id']:(f['width'],f['depth'],f['ground']) for f in facilities}=={
    'potato_field':(1,1,0),'shop':(1,2,0),'inn':(1,2,0),'square':(2,2,0),'fishing_pond':(1,2,1)}
assert [f['id'] for f in facilities if f['fieldCrop']['fileID']]==['potato_field']
for facility in facilities:
    preview=behaviours(docs(resolve(facility['prefab'])),'World/PlotFacilityPreview.cs')[0]
    assert docs(resolve(preview['facility']))[0]['MonoBehaviour']['id']==facility['id']
pond=expand(resolve(next(f for f in facilities if f['id']=='fishing_pond')['prefab']))
water=behaviours(pond,'World/FishingWater.cs')[0]
fishing=docs(resolve(water['definition']))[0]['MonoBehaviour']
assert resolve(fishing['requiredTool']).stem=='fishing_rod' and fishing['itemId']=='pond_fish'
surface=next(d['MeshCollider'] for d in pond if d['_id']==water['surface']['fileID'])
water_object=surface['m_GameObject']
plane=next(d['MeshFilter'] for d in pond if d.get('MeshFilter',{}).get('m_GameObject')==water_object)
renderer=next(d['MeshRenderer'] for d in pond if d.get('MeshRenderer',{}).get('m_GameObject')==water_object)
assert surface['m_Convex']==0 and surface['m_Mesh']==plane['m_Mesh']
water_material=next(d['Material'] for d in docs(resolve(renderer['m_Materials'][0])) if 'Material' in d)
water_properties={k:v for field in water_material['m_SavedProperties']['m_Floats'] for k,v in field.items()}
assert water_properties['_WaveAmplitude']==water_properties['_WaterClearness']==0
# Flat Kit water samples the scene after URP copies opaque color. Its shader writes full coverage.
assert water_material['m_CustomRenderQueue']==3000
assert water_material['stringTagMap']['RenderType']=='Transparent' and water_properties['_ZWrite']==0
assert water_material['stringTagMap']['WorkerbornSelectionCoverage']=='Opaque'
fisher=behaviours(scene,'HarvestWorker.cs')[0]
assert resolve(fisher['fishingBobberPrefab']).stem=='FishingBobber'
for item in itemdefs:
    if item['id'] in wardrobe:
        assert item['equipmentSlot']==wardrobe[item['id']]
        part=docs(resolve(item['equipmentPart']))[0]['MonoBehaviour']
        assert part['prefab']['fileID'] and part['femalePrefab']['fileID'] and item['groundPrefab']['fileID']
for actor in actors:
    definition=docs(resolve(actor['npc']))[0]['MonoBehaviour']['appearance'] if actor['npc']['fileID'] else settings['player']
    appearance=docs(resolve(definition))[0]['MonoBehaviour']
    look=docs(resolve(appearance['basePrefab']))
    sources=[resolve(d['PrefabInstance']['m_SourcePrefab']).stem for d in look if 'PrefabInstance' in d]
    assert sources == ['P09_Human'] and appearance['useP09'] == 1
    assert appearance['rightHandSocket'] == 'Weapon_Target_Hand_R'
    assert appearance['leftHandSocket'] == 'Weapon_Target_Hand_L'
    assert 1 <= appearance['p09Look']['outfit'] <= 12
    assert all(appearance[k]['fileID'] for k in ['idle','walk','run','work'])
parts=list((CONTENT/'Data/Characters/Parts').glob('*.asset'));assert len(parts)>=30
assert {docs(p)[0]['MonoBehaviour']['slot'] for p in parts}>={0,1,2,3,4,6}

build=(ROOT/'ProjectSettings/EditorBuildSettings.asset').read_text()
assert re.search(r'path: (.+)',build)[1]==str((CONTENT/'Scenes/MainMenu.unity').relative_to(ROOT))
for name in ['MainMenu','VillagePrototype','HarvestPrototype']:
    p=CONTENT/'Scenes'/(name+'.unity');assert str(p.relative_to(ROOT)) in build and guid_for(p) in build
assert (CONTENT/'Fonts/WorkerbornKorean-Regular.otf').read_bytes().startswith(b'OTTO')
print(f'Validated {files} project files, {pointers} pointers, authored fields, gatherables, farm plots, modular characters, content presets, HUD and build entries.')

# Input bindings are an authored asset shared by gameplay, camera and the EventSystem.
actions_path=CONTENT/'Input/Workerborn.inputactions'
actions=json.loads(actions_path.read_text());maps={m['name']:m for m in actions['maps']}
assert set(maps)=={'Gameplay','Camera','UI'}
allids=[a['id']for m in maps.values()for a in m['actions']]+[b['id']for m in maps.values()for b in m['bindings']]
assert len(allids)==len(set(allids))
required={'Gameplay':{'Interact':'<Mouse>/leftButton','Target':'<Keyboard>/leftCtrl','Names':'<Keyboard>/leftAlt','Walk':'<Keyboard>/leftShift',
                      'Sit':'<Keyboard>/c','Character':'<Keyboard>/o'},
          'Camera':{'OrbitMiddle':'<Mouse>/middleButton','OrbitRight':'<Mouse>/rightButton','Zoom':'<Mouse>/scroll/y'}}
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
for p in ('VillageHUD','InventoryWindow','InventoryCell','ActionButton','ConversationWindow','ShopWindow','QuestScrollWindow'):
    images=[
        d['MonoBehaviour'] for d in docs(CONTENT/'Prefabs/UI'/(p+'.prefab'))
        if d.get('MonoBehaviour',{}).get('m_Script',{}).get('guid')=='fe87c0e1cc204ed48ad3b37840f39efc']
    assert any(img['m_Sprite']['fileID'] and resolve(img['m_Sprite']).is_relative_to(ROOT/'Assets/Layer Lab') for img in images),p
print('Validated the authored COZY package, daily forecast, Kevin Iglesias work clips and Layer Lab UI art.')

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
