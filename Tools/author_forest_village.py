"""Explicit, one-time forest Scene authoring; never runs during Unity import or Play.
Uses supplied FarmCrops TerrainData/layers, FlatKit terrain material and Synty prefabs.
Ordinary level work belongs in Unity's Terrain editor, Scene and Prefab Mode.
Requires numpy, Pillow and UnityPy (in addition to the shared YAML tools).
"""
from pathlib import Path
import copy, json, math, random, re, struct
import numpy as np
from PIL import Image
import UnityPy
from unity_yaml import *

SCENE=BASE/'Scenes/VillagePrototype.unity'
TERRAIN=BASE/'World/ForestVillageTerrain.asset'
TERRAIN_PREFAB=BASE/'Prefabs/World/ForestTerrain.prefab'
TERRAIN_MATERIAL=Path('Assets/FlatKit/Demos/[Demo] Valley/Valley-Materials/FlatTerrain-TerrainShader.mat')
ORIGIN=(-63.,-45.);SIZE=(120.,100.);HEIGHT=18.

def load(path):
    text=(ROOT/path).read_text();stripped={int(i)for i in re.findall(r'^--- !u!\d+ &(-?\d+) stripped$',text,re.M)}
    return [(d['_class'],d['_id'],next(k for k in d if not k.startswith('_')),next(v for k,v in d.items()if not k.startswith('_')),d['_id'] in stripped) for d in geo.documents(ROOT/path)]
def flow(value):
    if isinstance(value,dict):
        value={k:flow(v)for k,v in value.items()}
        if 'fileID'in value or set(value)in[set('xy'),set('xyz'),set('xyzw'),set('rgba')]:return Flow(value)
    elif isinstance(value,list):return [flow(v)for v in value]
    return value
def save(path,docs):write(path,[(c,i,k,flow(v),st)for c,i,k,v,st in docs])
def native_meta(path,main):
    path=ROOT/path;Path(str(path)+'.meta').write_text('fileFormatVersion: 2\nguid: '+guid(path.relative_to(ROOT))+'\nNativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: '+str(main)+'\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n')

scene=load(SCENE);objects={i:(k,v)for c,i,k,v,st in scene}
if any(v.get('m_Name')=='Forest canopy'for c,i,k,v,st in scene):raise SystemExit('Forest already authored. Edit the Scene and Terrain directly in Unity.')
root=lambda docs:next((i,v)for c,i,k,v,st in docs if k=='Transform'and v.get('m_Father',{}).get('fileID')==0)
instances=[];pads=[]
for c,i,k,v,st in scene:
    if k!='PrefabInstance' or 'm_SourcePrefab'not in v:continue
    p=geo.GUIDS.get(v['m_SourcePrefab'].get('guid'))
    if p is None or not p.is_relative_to(ROOT/BASE/'Prefabs') or p.parent.name=='UI':continue
    source=load(p);rt,rv=root(source);go=next(v for c,i,k,v,st in source if i==rv['m_GameObject']['fileID']);name=go['m_Name']
    mods=v['m_Modification']['m_Modifications'];pos=dict(x=0.,y=0.,z=0.)
    for m in mods:
        if m['target']['fileID']==rt and m['propertyPath'].startswith('m_LocalPosition.'):pos[m['propertyPath'][-1]]=float(m['value'])
    instances.append((p,rt,mods,pos,name))
    if p.parent.name=='World'and any(w in name.lower()for w in ['house','inn','shop','bakery','smith','barn']):pads.append((pos['x'],pos['z']))
def base_height(x,z):
    r=np.sqrt(((x+3)/32)**2+((z-5)/25)**2)
    return np.maximum(.12,.48+.20*np.sin(x*.12)*np.cos(z*.09)+.018*(z+4)+3.0*np.maximum(0,r-.72)**2+2.1*np.exp(-((x+34)**2+(z-23)**2)/200)+1.8*np.exp(-((x-32)**2+(z-24)**2)/150))
def smooth(a):return np.clip(a,0,1)**2*(3-2*np.clip(a,0,1))
def height(x,z):
    out=base_height(x,z)
    for px,pz in pads:
        influence=1-smooth((np.sqrt((x-px)**2+(z-pz)**2)-3.5)/2.5)
        out=out*(1-influence)+base_height(px,pz)*influence
    field=1-smooth(np.maximum(np.maximum(np.abs(x+13)-7.8,np.abs(z-7.5)-10.8),0)/2.2)
    return np.minimum(16,out*(1-field)+.55*field)

# Clone supplied terrain data, keeping the actual supplied TerrainLayer references.
env=UnityPy.load(str(ROOT/'Assets/FarmCrops/Terrain/DemoTerrain.asset'))
terrain_obj=next(o for o in env.objects if o.type.name=='TerrainData');data=terrain_obj.read_typetree();h=data['m_Heightmap'];resolution=h['m_Resolution']
x,z=np.meshgrid(np.linspace(ORIGIN[0],ORIGIN[0]+SIZE[0],resolution),np.linspace(ORIGIN[1],ORIGIN[1]+SIZE[1],resolution));world=height(x,z);normalized=world/HEIGHT
h['m_Scale']=dict(x=SIZE[0]/(resolution-1),y=HEIGHT,z=SIZE[1]/(resolution-1))
h['m_Heights']=np.rint(normalized*32767).astype(np.uint16).ravel().tolist()
# Conservative error/culling bounds stay valid until Unity recomputes individual patches after sculpting.
h['m_PrecomputedError']=[1.]*len(h['m_PrecomputedError']);h['m_MinMaxPatchHeights']=[v for _ in range(len(h['m_MinMaxPatchHeights'])//2)for v in [float(normalized.min()),float(normalized.max())]]
data['m_Name']='Forest Village Terrain';detail=data['m_DetailDatabase'];detail['m_DetailPrototypes']=[]
for patch in detail['m_Patches']:
    for key,value in patch.items():
        if isinstance(value,list):patch[key]=[]
        elif isinstance(value,(int,float)):patch[key]=0
for key in ['m_DetailBillboardShader','m_DetailMeshLitShader','m_DetailMeshGrassShader']:detail[key]=dict(m_FileID=0,m_PathID=0)
terrain_obj.save_typetree(data)
alpha_obj=next(o for o in env.objects if o.type.name=='Texture2D');texture=alpha_obj.read();n=texture.m_Width
x,z=np.meshgrid(np.linspace(ORIGIN[0],ORIGIN[0]+SIZE[0],n),np.linspace(ORIGIN[1],ORIGIN[1]+SIZE[1],n))
# Curving village footpaths and a hand-worked farm, painted into the supplied grass/soil/gravel layers.
paths=[[(0,-5),(-2,-1),(-5,2),(-9,3),(-16,3),(-22,5)],[(0,-5),(3,-2),(7,0),(10,4),(11,9)],[(0,-1),(-.5,4),(1,9),(0,15),(-4,21),(-9,30)],[(-4,2),(-7,-3),(-13,-6),(-21,-8)]]
road=np.zeros_like(x)
for path in paths:
    for (ax,az),(bx,bz)in zip(path,path[1:]):
        t=np.clip(((x-ax)*(bx-ax)+(z-az)*(bz-az))/((bx-ax)**2+(bz-az)**2),0,1)
        distance=np.sqrt((x-ax-t*(bx-ax))**2+(z-az-t*(bz-az))**2)
        road=np.maximum(road,1-smooth((distance-.65)/.85))
soil=(1-smooth(np.maximum(np.abs(x+13)-5.9,np.abs(z-7.4)-8.6)/.8))*.92
road*=1-soil
rgba=np.zeros((n,n,4),dtype=np.uint8);rgba[:,:,0]=np.rint((1-soil-road)*255).astype(np.uint8);rgba[:,:,1]=np.rint(soil*255).astype(np.uint8);rgba[:,:,2]=255-rgba[:,:,0]-rgba[:,:,1]
texture.image=Image.fromarray(rgba);texture.save();out=ROOT/TERRAIN;out.parent.mkdir(exist_ok=True);out.write_bytes(env.file.save());native_meta(TERRAIN,15600000)

# A real Unity Terrain prefab, not a generated Play-mode mesh or custom ground material.
source=geo.documents(ROOT/'Assets/FlatKit/Demos/[Demo] Valley/Valley.unity')
b=Scene();node=b.node('Forest terrain')
for kind,cls in [('TerrainCollider',154),('Terrain',218)]:
    value=copy.deepcopy(next(d[kind]for d in source if kind in d));value.pop('m_GameObject');[value.pop(k,None)for k in common()]
    value['m_TerrainData']=assetref(TERRAIN,15600000)
    if kind=='Terrain':value.update(m_MaterialTemplate=assetref(TERRAIN_MATERIAL),m_DetailObjectDensity=0,m_DrawTreesAndFoliage=0,m_DrawInstanced=1,m_TreeDistance=120,m_DetailObjectDistance=40,m_HeightmapPixelError=6,m_ScaleInLightmap=1)
    component=b.component(node,cls,kind,value)
    if kind=='Terrain':terrain_component=component
save(TERRAIN_PREFAB,b.docs)

# Fully assembled editable forest props; original meshes and materials stay linked to supplied prefabs.
props={}
sources=[('ForestPine','Trees/SM_Tree_Pine_01.prefab',9.,.42),('ForestBirch','Trees/SM_Tree_Birch_02.prefab',7.6,.32),('ForestBoulder','Rocks/SM_Rock_Boulder_01.prefab',1.9,.8),('ForestShrub','Plants/SM_Plant_Hedge_Bush_01.prefab',.85,0)]
for name,source,visual_height,radius in sources:
    b=Scene();node=b.node(name);visual=b.node('Visual',node);b.model(visual,Path('Assets/Synty/PolygonNature/Prefabs')/source,visual_height)
    b.script(node,'World/VillageMapProp.cs',visual=ref(visual['t']))
    if radius:
        block=b.node('Navigation volume',node,layer=8);collider=b.box(block,(radius*2,visual_height,radius*2),(0,visual_height*.5,0));b.script(block,'World/VillageNavigationBlocker.cs')
    path=BASE/'Prefabs/World'/(name+'.prefab');save(path,b.docs);props[name]=path

# Reuse the authored Scene, preserving IDs, quests, crop IDs, NPCs and unrelated hierarchy.
controller=next(v for c,i,k,v,st in scene if v.get('m_Script',{}).get('guid')==guid(BASE/'Runtime/Village/VillagePrototype.cs'))
map_id=controller['mapRoot']['fileID'];map_transform=objects[map_id][1];map_go=objects[map_transform['m_GameObject']['fileID']][1]
map_node=dict(t=map_id,tr=map_transform,go=map_transform['m_GameObject']['fileID'],g=map_go)
b=Scene();b.n=950000000

def instantiate(path,parent,position=(0,0,0),yaw=0,scale=1,components=()):
    source=load(path);rt,rv=root(source);mod=[]
    rotation=(0,math.sin(math.radians(yaw)*.5),0,math.cos(math.radians(yaw)*.5))
    for prefix,values in [('m_LocalPosition',position),('m_LocalRotation',rotation),('m_LocalScale',(scale,)*3)]:
        for axis,value in zip('xyzw',values):mod.append(dict(target=assetref(path,rt),propertyPath=prefix+'.'+axis,value=str(round(float(value),6)),objectReference=ref()))
    instance=b.add(1001,'PrefabInstance',dict(m_ObjectHideFlags=0,serializedVersion=2,m_Modification=dict(serializedVersion=3,m_TransformParent=ref(parent['t']),m_Modifications=mod,m_RemovedComponents=[],m_RemovedGameObjects=[],m_AddedGameObjects=[],m_AddedComponents=[]),m_SourcePrefab=assetref(path,100100000)))
    transform=b.add(4,'Transform',dict(m_CorrespondingSourceObject=assetref(path,rt),m_PrefabInstance=ref(instance),m_PrefabAsset=ref()),strip=True);parent['tr']['m_Children'].append(ref(transform));aliases={}
    for component in components:
        kind=next(k for c,i,k,v,st in source if i==component);cls=next(c for c,i,k,v,st in source if i==component)
        aliases[component]=b.add(cls,kind,dict(m_CorrespondingSourceObject=assetref(path,component),m_PrefabInstance=ref(instance),m_PrefabAsset=ref()),strip=True)
    return aliases
terrain_alias=instantiate(TERRAIN_PREFAB,map_node,(ORIGIN[0],0,ORIGIN[1]),components=[terrain_component])[terrain_component]
# Remove the old flat plates from the active layout.
for c,i,k,v,st in scene:
    if k=='GameObject'and(v['m_Name']in ['Ground','Paths']or v['m_Name'].startswith('Soil row')):v['m_IsActive']=0
    if v.get('m_Script',{}).get('guid')==guid(BASE/'Runtime/World/VillageNavigation.cs'):v['terrain']=ref(terrain_alias);v['bakedData']=ref()
for path,rt,mods,pos,name in instances:
    if path.parent.name=='UI':continue
    y=float(height(pos['x'],pos['z']))+pos['y']
    existing=next((m for m in mods if m['target']['fileID']==rt and m['propertyPath']=='m_LocalPosition.y'),None)
    if existing:existing['value']=str(round(y,6))
    else:mods.append(dict(target=assetref(path,rt),propertyPath='m_LocalPosition.y',value=str(round(y,6)),objectReference=ref()))
# Spawn and rest markers are native scene transforms.
for c,i,k,v,st in scene:
    if k=='Transform'and'm_GameObject'in v:
        go=objects.get(v['m_GameObject']['fileID'],('',{}))[1]
        if go.get('m_Name')in ['Player spawn','After rest']:p=v['m_LocalPosition'];p['y']=float(height(p['x'],p['z']))
canopy=b.node('Forest canopy',map_node);understory=b.node('Forest understory',map_node);rocks=b.node('Forest outcrops',map_node)
rng=random.Random(5618);trees=[]
for z in np.arange(-36,48,6.3):
    for x in np.arange(-55,50,6.3):
        x=float(x+rng.uniform(-1.5,1.5));z1=float(z+rng.uniform(-1.5,1.5))
        if -29<x<23 and -13<z1<25:continue
        if rng.random()>.65:continue
        species='ForestPine'if rng.random()<.56 else'ForestBirch';trees.append((x,z1))
        instantiate(props[species],canopy,(x,float(height(x,z1)),z1),rng.uniform(0,360),rng.uniform(.78,1.22))
for _ in range(36):
    x,z=trees[rng.randrange(len(trees))];x+=rng.uniform(-2,2);z+=rng.uniform(-2,2)
    instantiate(props['ForestShrub'],understory,(x,float(height(x,z)),z),rng.uniform(0,360),rng.uniform(.7,1.3))
for x,z in [(-29,19),(-34,5),(-25,-14),(23,-8),(27,18),(15,28),(-16,29),(-39,29),(34,34),(-42,-23)]:
    instantiate(props['ForestBoulder'],rocks,(x,float(height(x,z))-.13,z),rng.uniform(0,360),rng.uniform(.8,1.8))
scene+=b.docs
save(SCENE,scene)
# Native height authoring audit also helps designers find the source assets and edit the resulting terrain.
print('Authored forest Terrain and prefab instances:',len(trees),'trees, 36 shrubs, 10 outcrops. Height range',round(float(world.min()),2),round(float(world.max()),2))
