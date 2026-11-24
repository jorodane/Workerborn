"""Explicit conversion of the checked-in sample Scene to reusable authored prefabs.
Never invoked by Unity. Refuses to run over an already converted sample Scene.
"""
import copy
import json
import re
import struct
from unity_yaml import *
from author_ui_prefabs import UIBuilder, UI

SCENE=BASE/'Scenes/VillagePrototype.unity'
scene=geo.documents(ROOT/SCENE)
by_id={d['_id']:d for d in scene}
if any(d.get('PrefabInstance',{}).get('m_SourcePrefab',{}).get('guid')==guid(BASE/'Prefabs/UI/VillageHUD.prefab') for d in scene):
    raise SystemExit('This Scene already uses authored prefabs. Edit them in Unity Prefab Mode.')

# Preserve textures, alpha clipping, colors and UV transforms in project-owned FlatKit materials.
def pointers(v):
    if isinstance(v,dict):
        if 'fileID'in v:yield v
        else:
            for value in v.values():yield from pointers(value)
    elif isinstance(v,list):
        for value in v:yield from pointers(value)
visited=set();material_paths=set()
def visit(path):
    path=Path(path)
    if path in visited:return
    visited.add(path)
    if path.suffix=='.mat':material_paths.add(path);return
    if path.suffix.lower()=='.fbx':
        refs=re.findall(r'guid: ([a-f0-9]{32})',Path(str(path)+'.meta').read_text())
        for g in refs:
            target=geo.GUIDS.get(g)
            if target is not None and target.suffix=='.mat':visit(target)
    elif path.suffix in ('.prefab','.asset','.unity'):
        for pointer in pointers(geo.documents(path)):
            target=geo.GUIDS.get(pointer.get('guid'))
            if target is not None and target.suffix in ('.prefab','.fbx','.mat','.asset'):visit(target)
visit(ROOT/SCENE)
for p in (ROOT/BASE/'Data/Characters').rglob('*.asset'):visit(p)
for p in (ROOT/BASE/'Data/Items').glob('*.asset'):visit(p)
flat_shader=Path('Assets/FlatKit/Shaders/StylizedSurface/StylizedSurface.shader')
materials=[]
for path in sorted(material_paths):
    source=next(d['Material'] for d in geo.documents(path) if 'Material'in d)
    if source.get('m_CustomRenderQueue',-1)>=3000:continue
    value=copy.deepcopy(source);saved=value['m_SavedProperties']
    floats={k:v for row in saved.get('m_Floats',[]) for k,v in row.items()}
    colors={k:v for row in saved.get('m_Colors',[]) for k,v in row.items()}
    textures={k:v for row in saved.get('m_TexEnvs',[]) for k,v in row.items()}
    if floats.get('_Surface',0)>0:continue
    if ('_BaseMap' not in textures or textures['_BaseMap']['m_Texture']['fileID']==0) and '_MainTex'in textures:textures['_BaseMap']=textures['_MainTex']
    if '_BaseColor'not in colors:colors['_BaseColor']=colors.get('_Color',Flow(r=1,g=1,b=1,a=1))
    floats.update(_CelPrimaryMode=1,_SelfShadingSize=.58,_ShadowEdgeSize=.035,_LightContribution=1,_UnityShadowMode=1,_UnityShadowPower=.4,_UnityShadowSharpness=1,_OutlineEnabled=1,_OutlineWidth=.45,_OutlineScale=1,_OutlineDepthOffset=0,_OutlineSpace=0,_CameraDistanceImpact=0,_TextureImpact=1,_TextureBlendingMode=0,_CelExtraEnabled=0,_GradientEnabled=0,_SpecularEnabled=0,_RimEnabled=0,_VertexColorsEnabled=0,_SrcBlend=1,_DstBlend=0,_ZWrite=1)
    colors.update(_ColorDim=Flow(r=.66,g=.7,b=.76,a=1),_OutlineColor=Flow(r=.045,g=.04,b=.04,a=1))
    keywords=['_CELPRIMARYMODE_SINGLE','_UNITYSHADOWMODE_MULTIPLY','_TEXTUREBLENDINGMODE_MULTIPLY','_OUTLINESPACE_SCREEN']
    if not re.match(r'^(eye_|eyebrow_|mouth_|cheek_)',path.stem,re.I):keywords.append('DR_OUTLINE_ON')
    else:floats['_OutlineEnabled']=0
    if floats.get('_AlphaClip',0)>.5:keywords.append('_ALPHATEST_ON')
    if textures.get('_BumpMap',{}).get('m_Texture',{}).get('fileID',0):keywords.append('_NORMALMAP')
    saved['m_TexEnvs']=[{k:v} for k,v in textures.items() if k in {'_BaseMap','_BumpMap','_DetailMap','_EmissionMap','_CelStepTexture','_CelCurveTexture'}];saved['m_Floats']=[{k:v} for k,v in floats.items()];saved['m_Colors']=[{k:v} for k,v in colors.items()]
    value.update(m_Shader=assetref(flat_shader,4800000),m_Name='WB_'+path.stem,m_ValidKeywords=keywords,m_InvalidKeywords=[],m_ShaderKeywords=' '.join(keywords),disabledShaderPasses=[],serializedVersion=8)
    target=BASE/'Materials/Toon'/(path.stem+'_'+guid(path.relative_to(ROOT))[:8]+'.mat')
    write(target,[(21,2100000,'Material',value,False)])
    materials.append(dict(source=assetref(path.relative_to(ROOT),2100000),styled=assetref(target,2100000)))
palette=BASE/'Data/ToonPalette.asset'
write(palette,[(114,11400000,'MonoBehaviour',mono('Rendering/ToonMaterialPalette.cs',name='Village toon palette',materials=materials),False)])
for path in (ROOT/BASE/'Data/Characters').glob('*.asset'):
    docs=geo.documents(path);value=docs[0]['MonoBehaviour'];value['toonPalette']=assetref(palette)
    write(path.relative_to(ROOT),[(114,docs[0]['_id'],'MonoBehaviour',value,False)])

# The supplied FlatKit object-outline feature is registered on copied PC and mobile renderers.
feature_script=Path('Assets/FlatKit/[Render Pipeline] URP/RenderFeatures/ObjectOutline/ObjectOutlineRendererFeature.cs')
for platform in ['PC','Mobile']:
    path=Path('Assets/Settings')/(platform+'_Renderer.asset');docs=geo.documents(ROOT/path)
    feature_id=7361452315859170001
    config=docs[0]['MonoBehaviour'];config['m_RendererFeatures'].append(ref(feature_id));config['m_RendererFeatureMap']=str(config.get('m_RendererFeatureMap') or '')+struct.pack('<q',feature_id).hex()
    feature=mono('Rendering/ToonSurface.cs',name='FlatKit object outlines');feature['m_Script']=assetref(feature_script)
    feature.update(m_Active=1,autoReferenceMaterials=0,materials=[],settings=dict(passTag='Workerborn outlines',Event=300,filterSettings=dict(RenderQueueType=0,LayerMask=Flow(serializedVersion=2,m_Bits=4294967295),PassNames=['Outline']),overrideMaterial=ref(),overrideMaterialPassIndex=0,overrideShader=ref(),overrideShaderPassIndex=0,overrideMode=0,overrideDepthState=0,enableWrite=1,depthCompareFunction=4,stencilSettings=dict(overrideStencilState=0,stencilReference=0,stencilCompareFunction=8,passOperation=0,failOperation=0,zFailOperation=0),cameraSettings=dict(overrideCamera=0,restoreCamera=1,offset=vec(0,0,0,0),cameraFieldOfView=60)))
    target=BASE/'Rendering'/(platform+'_Renderer.asset')
    write(target,[(d['_class'],d['_id'],next(k for k in d if not k.startswith('_')),d[next(k for k in d if not k.startswith('_'))],False) for d in docs]+[(114,feature_id,'MonoBehaviour',feature,False)])
    pipeline=Path('Assets/Settings')/(platform+'_RPAsset.asset');text=(ROOT/pipeline).read_text();text=text.replace(guid(path),guid(target)).replace('m_Name: '+platform+'_RPAsset','m_Name: Workerborn_'+platform)
    output=BASE/'Rendering'/(platform+'_Pipeline.asset');(ROOT/output).parent.mkdir(parents=True,exist_ok=True);(ROOT/output).write_text(text)
    for project in ['GraphicsSettings.asset','QualitySettings.asset']:
        p=ROOT/'ProjectSettings'/project;p.write_text(p.read_text().replace(guid(pipeline),guid(output)))

# Native scene tree extraction keeps nested vendor prefab instances intact.
def value(d):return d[next(k for k in d if not k.startswith('_'))]
def subtree(tid):
    ids=set()
    def collect(id):
        if id in ids:return
        ids.add(id);d=by_id[id];v=value(d)
        if 'm_GameObject'in v and v['m_GameObject']['fileID']:
            go=v['m_GameObject']['fileID'];ids.add(go)
            for component in by_id[go]['GameObject']['m_Component']:ids.add(component['component']['fileID'])
        if v.get('m_PrefabInstance',{}).get('fileID'):
            instance=v['m_PrefabInstance']['fileID'];ids.add(instance)
            ids.update(other['_id']for other in scene if value(other).get('m_PrefabInstance',{}).get('fileID')==instance)
        for child in v.get('m_Children',[]):collect(child['fileID'])
    collect(tid);return ids

def find_go(name):return next(d['_id'] for d in scene if d.get('GameObject',{}).get('m_Name')==name)
def transform_of(go):return next(d['_id'] for d in scene if d.get('Transform',d.get('RectTransform',{})).get('m_GameObject',{}).get('fileID')==go)
def serialize(d):return d['_class'],d['_id'],next(k for k in d if not k.startswith('_')),value(d),bool('m_CorrespondingSourceObject'in value(d) and value(d)['m_CorrespondingSourceObject'].get('guid'))

def export_tree(tid,path,root_name=None):
    ids=subtree(tid);docs=copy.deepcopy([d for d in scene if d['_id']in ids]);tr=value(next(d for d in docs if d['_id']==tid))
    tr['m_Father']=ref();tr['m_LocalPosition']=vec(0,0,0);tr['m_LocalRotation']=vec(0,0,0,1)
    if 'm_AnchoredPosition'in tr:tr['m_AnchoredPosition']=vec(0,0)
    if root_name:
        next(d['GameObject'] for d in docs if d['_id']==tr['m_GameObject']['fileID'])['m_Name']=root_name
    write(path,[serialize(d) for d in docs]);return ids,docs

nextid=max(d['_id'] for d in scene if d['_id']<2147483647)+1000
removed=set();added=[]
def allocate():
    global nextid
    nextid+=1;return nextid

def instance_tree(tid,path,source_docs,ids,extra_mods=None):
    source_by={d['_id']:d for d in source_docs};source_t=next(d for d in source_docs if 'Transform'in d and d['Transform'].get('m_Father',{}).get('fileID')==0 or 'RectTransform'in d and d['RectTransform'].get('m_Father',{}).get('fileID')==0)
    source_tid=source_t['_id'];source_go=value(source_t)['m_GameObject']['fileID'];old=value(by_id[tid]);mods=[]
    for key in ['m_LocalPosition','m_LocalRotation','m_LocalScale','m_AnchoredPosition']:
        if key not in old:continue
        for axis,n in old[key].items():mods.append(dict(target=assetref(path,source_tid),propertyPath=key+'.'+axis,value=str(n),objectReference=ref()))
    oldgo=by_id[old['m_GameObject']['fileID']]['GameObject']
    mods.append(dict(target=assetref(path,source_go),propertyPath='m_Name',value=oldgo['m_Name'],objectReference=ref()))
    if extra_mods:mods+=extra_mods
    instance=allocate()
    added.append(dict(_class=1001,_id=instance,PrefabInstance=dict(m_ObjectHideFlags=0,serializedVersion=2,m_Modification=dict(serializedVersion=3,m_TransformParent=old['m_Father'],m_Modifications=mods,m_RemovedComponents=[],m_RemovedGameObjects=[],m_AddedGameObjects=[],m_AddedComponents=[]),m_SourcePrefab=assetref(path,100100000))))
    # Preserve the IDs of objects referenced outside this subtree, including root transforms.
    external={tid}
    for d in scene:
        if d['_id']not in ids:
            for pointer in pointers(value(d)):
                if 'guid'not in pointer and pointer['fileID']in ids:external.add(pointer['fileID'])
    source_ids={d['_id']for d in source_docs}
    for original_id in external:
        # For repeated crops only the root transform is externally referenced.
        source_id=source_tid if original_id==tid else original_id
        if source_id not in source_ids:raise ValueError(('Cannot remap external reference',path,original_id))
        original=source_by[source_id];kind=next(k for k in original if not k.startswith('_'))
        if kind=='PrefabInstance':continue # Internal nested instances are owned by the source prefab, not scene aliases.
        fields=dict(m_CorrespondingSourceObject=assetref(path,source_id),m_PrefabInstance=ref(instance),m_PrefabAsset=ref())
        if kind=='MonoBehaviour':fields.update(m_GameObject=ref(),m_Enabled=1,m_EditorHideFlags=0,m_Script=value(original)['m_Script'],m_Name='',m_EditorClassIdentifier='')
        added.append(dict(_class=original['_class'],_id=original_id,**{kind:fields}))
    removed.update(ids)

# Add a shared toon palette to the authored world; no geometry is created on Play.
map_go=find_go('Village');map_t=transform_of(map_go)
b=Scene();b.n=nextid
map_node=dict(go=map_go,t=map_t,g=by_id[map_go]['GameObject'],tr=value(by_id[map_t]))
b.script(map_node,'Rendering/ToonSurface.cs',palette=assetref(palette))
for c,i,k,v,st in b.docs:added.append(dict(_class=c,_id=i,**{k:v}))
nextid=b.n

# NPCs, landmarks, player and composite building/prop roots are complete reusable prefabs.
for name in ['Mira','Noah','Lena','Board','Shrine','Player']:
    tid=transform_of(find_go(name));path=BASE/'Prefabs'/('Characters' if name in ['Mira','Noah','Lena','Player'] else 'Interaction')/(name+'.prefab')
    ids,docs=export_tree(tid,path);instance_tree(tid,path,docs,ids)

crop_script=guid(BASE/'Runtime/HarvestableCrop.cs')
crop_docs=[d for d in scene if d.get('MonoBehaviour',{}).get('m_Script',{}).get('guid')==crop_script]
first_t=transform_of(crop_docs[0]['MonoBehaviour']['m_GameObject']['fileID']);crop_path=BASE/'Prefabs/Crops/Potato.prefab'
ids,template=export_tree(first_t,crop_path,'Potato')
source_crop=next(d for d in template if d.get('MonoBehaviour',{}).get('m_Script',{}).get('guid')==crop_script)
source_crop['MonoBehaviour']['persistentId']=''
write(crop_path,[serialize(d) for d in template])
for crop in crop_docs:
    tid=transform_of(crop['MonoBehaviour']['m_GameObject']['fileID']);current=subtree(tid)
    mod=dict(target=assetref(crop_path,source_crop['_id']),propertyPath='persistentId',value=crop['MonoBehaviour']['persistentId'],objectReference=ref())
    instance_tree(tid,crop_path,template,current,[mod])

# Composite world props retain the authored colliders, navigation markers and lamps.
map_prop_script=guid(BASE/'Runtime/World/VillageMapProp.cs')
for d in list(scene):
    if d['_id']in removed:continue
    if d.get('MonoBehaviour',{}).get('m_Script',{}).get('guid')!=map_prop_script:continue
    go=d['MonoBehaviour']['m_GameObject']['fileID'];name=by_id[go]['GameObject']['m_Name'];tid=transform_of(go)
    path=BASE/'Prefabs/World'/(name.replace(' ','')+'.prefab');ids,docs=export_tree(tid,path);instance_tree(tid,path,docs,ids)

# All dropped items have their own complete pickup prefab and ItemDefinition link.
item_definitions=list((ROOT/BASE/'Data/Items').glob('*.asset'))
for item_path in item_definitions:
    item_docs=geo.documents(item_path);item=item_docs[0]['MonoBehaviour'];key=item_path.stem
    builder=Scene();root=builder.node(item['displayName']);visual=builder.node('Visual',root)
    model=geo.GUIDS.get(item['worldPrefab'].get('guid'))
    if model is None:model=ROOT/'Assets/Fantastic Village Pack/prefabs/props/P_PROP_food_bread_01.prefab'
    builder.model(visual,model.relative_to(ROOT),.3 if key!='hoe'else .65)
    if key=='gold':
        # Replaced below with a supplied coin mesh, when available.
        pass
    builder.component(root,135,'SphereCollider',dict(m_Material=ref(),m_IncludeLayers=Flow(serializedVersion=2,m_Bits=0),m_ExcludeLayers=Flow(serializedVersion=2,m_Bits=0),m_LayerOverridePriority=0,m_IsTrigger=1,m_ProvidesContacts=0,m_Enabled=1,serializedVersion=3,m_Radius=.38,m_Center=vec(0,.15,0)))
    component=builder.script(root,'GroundPickup.cs',item=assetref(item_path.relative_to(ROOT)),definition=assetref(BASE/'Data/Potato.asset') if key=='potato'else ref(),visual=ref(visual['t']),quantity=1,flightSeconds=.48,persistentId='')
    builder.script(root,'Rendering/ToonSurface.cs',palette=assetref(palette))
    output=BASE/'Prefabs/Items'/(key+'.prefab');write(output,builder.docs)
    item['groundPrefab']=assetref(output,component);write(item_path.relative_to(ROOT),[serialize(d)for d in item_docs])
    if key=='potato':
        p=BASE/'Data/Potato.asset';docs=geo.documents(ROOT/p);docs[0]['MonoBehaviour']['pickupPrefab']=assetref(output,component);write(p,[serialize(d)for d in docs])

# UI root is a prefab as well; all individual windows are in the prefab library.
manifest=json.loads((ROOT/'Tools/ui-prefab-manifest.json').read_text())
ui_go=find_go('Village UI');ui_t=transform_of(ui_go)
ui_node=dict(go=ui_go,t=ui_t,g=by_id[ui_go]['GameObject'],tr=value(by_id[ui_t]))
b=UIBuilder();b.n=nextid
labels=b.ui('World item names',ui_node,(0,0),(0,0),(.5,.5),(.5,.5));labels['tr'].update(m_AnchorMin=vec(0,0),m_AnchorMax=vec(1,1))
pickup_names=b.script(labels,'UI/PickupNames.cs',spacing=3,edgeMargin=8)
link_data=manifest['TargetLink'];link_ids=b.native_instance(ui_node,Path(link_data['path']),[link_data['component']])
link=link_ids[link_data['component']]
# Make a real map view in the persistent HUD using authored RawImage and marker templates.
hud=next(d['MonoBehaviour']for d in scene if d.get('MonoBehaviour',{}).get('m_Script',{}).get('guid')==guid(BASE/'Runtime/Village/VillageHud.cs'))
map_image_id=hud['minimapImage']['fileID'];map_go_id=value(by_id[map_image_id])['m_GameObject']['fileID'];map_node=dict(go=map_go_id,t=map_image_id,g=by_id[map_go_id]['GameObject'],tr=value(by_id[map_image_id]))
_,map_raw=b.raw('Scene map',map_node,(174,132),(0,0),tint=(1,.9,.72,1))
player_marker=b.ui('Player marker',map_node,(5,5),(0,0),(0,0),(.5,.5),(1,1,.85,1))
marker,marker_id=b.text('NPC marker template',map_node,'',(74,22),(0,0),10,(1,1,1,1),4,(0,0),(.5,.5));marker['g']['m_IsActive']=0
b.script(map_node,'Village/VillageMapClick.cs');b.script(map_node,'UI/VillageMapView.cs',map=ref(map_raw),player=ref(player_marker['t']),npcMarker=ref(marker_id))
for c,i,k,v,st in b.docs:
    doc=dict(_class=c,_id=i,**{k:v});scene.append(doc);by_id[i]=doc
nextid=b.n
hud.update(library=assetref(BASE/'Data/VillageUI.asset'),pickupNames=ref(pickup_names),targetLink=ref(link))
ui_path=BASE/'Prefabs/UI/VillageHUD.prefab';ids,docs=export_tree(ui_t,ui_path);instance_tree(ui_t,ui_path,docs,ids)

# A separate NPC portrait camera leaves the player's equipment portrait independent.
b=Scene();b.n=nextid
original_rt=geo.documents(ROOT/BASE/'UI/Portrait.rendertexture')[0];rt=copy.deepcopy(original_rt['RenderTexture']);rt['m_Name']='NPC portrait'
write(BASE/'UI/NpcPortrait.rendertexture',[(84,8400000,'RenderTexture',rt,False)])
n=b.node('NPC portrait camera',pos=(0,.9,3),rot=(0,1,0,0));camera=b.camera(n,'NpcPortrait',1,1.05,1<<10)
next(v for c,i,k,v,st in b.docs if i==camera)['m_Enabled']=0
portrait=b.script(n,'Rendering/NpcPortrait.cs',portraitCamera=ref(camera),distance=3,height=.9)
roots=next(d['SceneRoots']['m_Roots']for d in scene if 'SceneRoots'in d);roots.append(ref(n['t']))
controller=next(d['MonoBehaviour']for d in scene if d.get('MonoBehaviour',{}).get('m_Script',{}).get('guid')==guid(BASE/'Runtime/Village/VillagePrototype.cs'))
controller_node=dict(go=controller['m_GameObject']['fileID'],g=by_id[controller['m_GameObject']['fileID']]['GameObject'])
mask=BASE/'Materials/TargetMask.mat';template=copy.deepcopy(next(d['Material']for d in geo.documents(ROOT/BASE/'Materials/FarmSurface.mat')if'Material'in d));template.update(m_Name='Target silhouette mask',m_Shader=assetref(BASE/'Shaders/TargetMask.shader',4800000));write(mask,[(21,2100000,'Material',template,False)])
targeting=b.script(controller_node,'Interaction/PointerTargeting.cs',maskMaterial=assetref(mask,2100000),maskWidth=768,capturesPerSecond=30,highlightColor=color(1,.88,.36,1),link=ref())
controller.update(targeting=ref(targeting),npcPortrait=ref(portrait),npcPortraitTexture=assetref(BASE/'UI/NpcPortrait.rendertexture',8400000))
for c,i,k,v,st in b.docs:added.append(dict(_class=c,_id=i,**{k:v}))

final=[d for d in scene if d['_id']not in removed]+added
# Make all pointers/vectors canonical flow mappings, so Unity diffs remain compact.
def canonical(v):
    if isinstance(v,dict):
        converted={k:canonical(x)for k,x in v.items()}
        if set(v)<=set(['fileID','guid','type']) or set(v)in [set('xyz'),set('xy'),set('xyzw'),set('rgba')]:return Flow(converted)
        return converted
    if isinstance(v,list):return [canonical(x)for x in v]
    return v
write(SCENE,[(c,i,k,canonical(v),st)for c,i,k,v,st in [serialize(d)for d in final]])
print('Converted the village to editable gameplay/UI prefabs and',len(materials),'FlatKit material variants.')
