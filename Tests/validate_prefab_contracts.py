"""Static prefab field/type contracts, kept separate from Unity import/PlayMode tests."""
from pathlib import Path
from functools import cache
import json
import re
import sys
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'Tools'))
from unity_asset_geometry import documents,GUIDS
from resolve_owned_prefabs import body, expand
CONTENT=ROOT/'Assets/Workerborn'
UI_GUIDS=json.loads((ROOT/'Tools/unity_ui_guids.json').read_text())
CLASSES={}
for path in (CONTENT/'Runtime').rglob('*.cs'):
    text=path.read_text();name=path.stem
    parent=re.search(r'\bclass\s+'+name+r'(?:\s*:\s*([\w.]+))?',text)
    if parent is None:continue
    fields=dict(re.findall(r'\[SerializeField[^\]]*\]\s*(?:\[[^\]]*\]\s*)*(?:private|protected|public)\s+([\w.<>\[\],]+)\s+(\w+)',text))
    # Convert the type/name regex to a name/type mapping.
    fields={field:type for type,field in re.findall(r'\[SerializeField[^\]]*\]\s*(?:\[[^\]]*\]\s*)*(?:private|protected|public)\s+([\w.<>\[\],]+)\s+(\w+)',text)}
    CLASSES[name]=(parent.group(1),fields,path)

def fields_for(name):
    if name not in CLASSES:return {}
    parent,fields,_=CLASSES[name];return {**fields_for(parent),**fields}
def subclass(name,parent):
    while name in CLASSES:
        if name==parent:return True
        name=CLASSES[name][0]
    return name==parent

def kind(doc):return next(k for k in doc if not k.startswith('_'))


@cache
def check_nested_file_ids(path):
    # expand() uses namespaced IDs, so it cannot detect collisions during Unity import.
    rows = documents(path)
    used = {row['_id'] for row in rows}
    assert len(used) == len(rows), f'{path.name}: duplicate serialized fileID'
    for row in rows:
        if 'PrefabInstance' not in row:
            continue
        guid = row['PrefabInstance']['m_SourcePrefab']['guid']
        source = GUIDS.get(guid)
        if source is None or not source.is_relative_to(CONTENT / 'Prefabs'):
            continue

        instance = row['_id']
        aliases = {value['m_CorrespondingSourceObject']['fileID'] for value in map(body, rows)
                   if value.get('m_PrefabInstance', {}).get('fileID') == instance
                   and value.get('m_CorrespondingSourceObject', {}).get('guid') == guid}
        for source_id in check_nested_file_ids(source):
            combined = (instance ^ source_id) & ((1 << 63) - 1)
            assert combined != 0, f'{path.name}: instance {instance} conflicts with a fileID in {source.name}'
            if source_id in aliases:
                continue  # A stripped object already has a local fileID in this asset.
            assert combined not in used, f'{path.name}: duplicate nested fileID {combined} from {source.name}'
            used.add(combined)
    return used


def validate(path):
    docs=expand(path) if path.suffix=='.prefab' else documents(path)
    objects={d['_id']:d for d in docs};checked=0
    for doc in docs:
        mono=doc.get('MonoBehaviour');script=GUIDS.get(mono.get('m_Script',{}).get('guid')) if mono else None
        if script is None or not script.is_relative_to(CONTENT/'Runtime'):continue
        name=script.stem;fields=fields_for(name)
        for field,value in mono.items():
            if field.startswith('m_'):continue
            assert field in fields,f'{path.name}: {name}.{field} is not serialized by its script'
        for field,type in fields.items():
            value=mono.get(field)
            # All UI view object references must be authored; runtime only binds data/listeners.
            optional_chrome=(name=='WorldContextView' and field in {'closeButton','dragHandle','minimizeButton','windowBody'}
                             or name=='ConversationWindowView' and field in {'closeButton','dragHandle','minimizeButton'}
                             or name=='ItemTooltipView' and mono.get('windowId')=='tooltip' and field in {'closeButton','dragHandle','minimizeButton'}
                             or name=='PartTimeScheduleView' and mono.get('upcomingRows',0)>0 and field in {'dateLabel','previousDay','nextDay'})
            if (name.endswith('View') or name=='FrontEndMenu') and not optional_chrome and type in {'TMP_Text','TMP_InputField','Image','Button','Slider','RawImage','RectTransform','CanvasGroup','ScrollRect','GameObject','WindowDragHandle','GridLayoutGroup','UiListRow','UiItemView','VillageMapView','ShopSellTarget','UiActionView'}:
                assert value and value.get('fileID'),f'{path.name}: missing {name}.{field}'
            if not isinstance(value,dict) or not value.get('fileID'):continue
            target=None
            if value.get('guid'):
                asset=GUIDS.get(value['guid'])
                if asset and asset.suffix in ('.prefab','.asset'):
                    target=next((d for d in documents(asset)if d['_id']==value['fileID']),None)
            else:target=objects.get(value['fileID'])
            if target is None:continue # vendor import subobjects are validated by Unity/asset pointer checks
            expected={'RectTransform':'RectTransform','Transform':('Transform','RectTransform'),'GameObject':'GameObject','Camera':'Camera','Light':'Light','BoxCollider':'BoxCollider','Terrain':'Terrain','CanvasGroup':'CanvasGroup'}.get(type)
            if expected:assert kind(target)in ((expected,)if isinstance(expected,str)else expected),f'{path.name}: {name}.{field} expects {type}, got {kind(target)}'
            if type=='TMP_Text':
                assert target.get('MonoBehaviour',{}).get('m_Script',{}).get('guid')=='f4688fdb7df04437aeb418b961361dc5',f'{path.name}: {name}.{field} expects TMP_Text'
            if type in UI_GUIDS:
                assert target.get('MonoBehaviour',{}).get('m_Script',{}).get('guid')==UI_GUIDS[type],f'{path.name}: {name}.{field} expects {type}'
            if type in CLASSES:
                target_script=GUIDS.get(target.get('MonoBehaviour',{}).get('m_Script',{}).get('guid'))
                assert target_script and subclass(target_script.stem,type),f'{path.name}: {name}.{field} expects {type}'
        checked+=1
    return checked

count=0
check_nested_file_ids(CONTENT / 'Prefabs/UI/VillageHUD.prefab')
for path in [*(CONTENT/'Prefabs').rglob('*.prefab'),*(CONTENT/'Data').rglob('*.asset')]:count+=validate(path)
for child in ['VillageInteractable','HarvestableCrop','GroundPickup','MonsterInteractable','AnimalNpc','LoggingTree','VillagePlotSign']:assert subclass(child,'WorldInteractable')
assert subclass('FishingWater','HarvestableCrop')
for name in ['Mira','Noah','Lena','Player']:assert (CONTENT/'Prefabs/Characters'/(name+'.prefab')).is_file()
for name in ['Board','Shrine','Monster','ForestRabbit']:assert (CONTENT/'Prefabs/Interaction'/(name+'.prefab')).is_file()
assert (CONTENT/'Prefabs/Crops/Potato.prefab').is_file()
wardrobe={'field_cap','field_coat','field_trousers','field_boots','field_gloves'}
assert {p.stem for p in (CONTENT/'Prefabs/Items').glob('*.prefab')}=={'GroundPickup','potato','bread','health_potion','hoe','bag','gold','egg','timber','woodcutting_axe','legend_axe','rabbit_meat','branch','fishing_rod','pond_fish'} | wardrobe
manifest=json.loads((ROOT/'Tools/ui-prefab-manifest.json').read_text())
for name in ['InventoryWindow','SkillsWindow','QuestWindow','QuestScrollWindow','CharacterWindow','MapWindow','PartTimeScheduleRow','ShopWindow','QuantityWindow','ConversationWindow','ItemDetailWindow','MainMenu','EraWindow','SystemWindow','SettingsWindow','ServerAdminWindow','ServerAdminRosterRow','VillageChat','PlayerOverhead','PlotWindow']:
    assert name in manifest and (ROOT/manifest[name]['path']).is_file()
print(f'Validated {count} component/content contracts, typed UI bindings, complete window prefabs and shared interaction subclasses.')
