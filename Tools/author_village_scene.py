"""Explicit offline authoring utility for the checked-in example assets.
Never called by Unity, on import, or when entering Play mode. Re-running replaces
sample content: use Unity's Scene/Inspector for ordinary level and content edits.
Requires Python, PyYAML and Pillow. Run from the repository root.
"""
from pathlib import Path
import math
import re
import uuid
import yaml
from PIL import Image, ImageDraw
import unity_asset_geometry as geo

ROOT = Path(__file__).resolve().parents[1]
BASE = Path('Assets/Workerborn')
HEADER = '%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n'
class Flow(dict): pass
class Dumper(yaml.SafeDumper): pass
Dumper.add_representer(Flow, lambda d,v:d.represent_mapping('tag:yaml.org,2002:map',v,flow_style=True))
def dump(v): return yaml.dump(v,Dumper=Dumper,allow_unicode=True,sort_keys=False,width=150)
def ref(i=0): return Flow(fileID=i)
def vec(*v): return Flow(zip('xyzw',v))
def color(*v): return Flow(zip('rgba',v))
def guid(path):
    path=Path(path); meta=ROOT/(str(path)+'.meta')
    if meta.exists(): return re.search(r'^guid: ([a-f0-9]{32})$',meta.read_text(),re.M)[1]
    return uuid.uuid5(uuid.NAMESPACE_URL,'workerborn-harvest/'+str(path)).hex

def assetref(path, identifier=None):
    if path is None:return ref()
    path=Path(path); ext=path.suffix.lower()
    if identifier is None:
        identifier={'.cs':11500000,'.asset':11400000,'.anim':7400000,'.mat':2100000,'.otf':12800000,'.png':21300000,'.rendertexture':8400000}.get(ext)
        if identifier is None:identifier=geo.root_ids(ROOT/path)[0]
    return Flow(fileID=identifier,guid=guid(path),type=2 if ext in ('.asset','.anim','.mat','.rendertexture') else 3)

def write(path,docs):
    path=ROOT/path;path.parent.mkdir(parents=True,exist_ok=True)
    path.write_text(HEADER+''.join(f'--- !u!{c} &{i}{" stripped" if strip else ""}\n'+dump({kind:values}) for c,i,kind,values,strip in docs))

def common():return dict(m_ObjectHideFlags=0,m_CorrespondingSourceObject=ref(),m_PrefabInstance=ref(),m_PrefabAsset=ref())
def mono(script,name='',go=0,**fields):
    return dict(**common(),m_GameObject=ref(go),m_Enabled=1,m_EditorHideFlags=0,m_Script=assetref(BASE/'Runtime'/script),m_Name=name,m_EditorClassIdentifier='',**fields)
def so(path,script,**fields):
    write(BASE/'Data'/path,[(114,11400000,'MonoBehaviour',mono('Content/'+script,name=Path(path).stem,**fields),False)])
    return assetref(BASE/'Data'/path)

# Part library uses the supplied modular meshes, materials and bone names.
partrefs={}
for folder,prefix,slot,bone in [('hair','hair_',0,'head_dummy'),('face','eye_',2,'head_dummy'),('face','eyebrow_',3,'head_dummy'),('face','mouth_',4,'head_dummy'),('warrior','warrior_',1,''),('thief','thief_',1,''),('weapon','sword_',6,'weapon_R'),('weapon','knife_',6,'weapon_R'),('weapon','shuriken_',6,'weapon_R'),('weapon','shield_',7,'weapon_L'),('weapon','bow_',7,'weapon_L')]:
    for mesh in sorted((ROOT/'Assets/stylizedLittleHero/Meshes'/folder).glob(prefix+'*.fbx')):
        name=mesh.stem;rel=BASE/'Data/Characters/Parts'/(name+'.asset')
        write(rel,[(114,11400000,'MonoBehaviour',mono('Characters/CharacterPartDefinition.cs',name=name,slot=slot,prefab=assetref(mesh.relative_to(ROOT)),attachmentBone=bone,localPosition=vec(0,0,0),localEulerAngles=vec(0,0,0),localScale=vec(1,1,1),materials=[],tint=color(1,1,1,1)),False)])
        partrefs[name]=assetref(rel)
hoe='Assets/CozyCabin/Upgradeable Tools Pack/Prefabs/Hoe_Tier_1.prefab'
write(BASE/'Data/Characters/Parts/Hoe.asset',[(114,11400000,'MonoBehaviour',mono('Characters/CharacterPartDefinition.cs',name='Hoe',slot=6,prefab=assetref(hoe),attachmentBone='weapon_R',localPosition=vec(0,-0.3,0),localEulerAngles=vec(0,0,90),localScale=vec(0.8,0.8,0.8),materials=[],tint=color(1,1,1,1)),False)])
characters={}
templates={'Player_Default':'Thief_M','Mira':'Warrior_M','Noah':'Archer_M','Lena':'Thief_F'}
for name,template in templates.items():
    rel=BASE/'Data/Characters'/(name+'.asset')
    fields=dict(basePrefab=assetref('Assets/stylizedLittleHero/Prefabs/'+template+'.prefab'),height=1.7,
        **{k:ref() for k in ['hair','outfit','eyes','eyebrows','mouth','headwear','rightHand','leftHand']},hideBaseWeapons=1,hideBaseHair=0,skinTint=color(1,1,1,1),clothingTint=color(1,1,1,1),
        idle=assetref('Assets/stylizedLittleHero/Animation/Idle_ani.anim'),walk=assetref('Assets/stylizedLittleHero/Animation/Walk_ani.anim'),work=assetref('Assets/stylizedLittleHero/Animation/Attack_sword_ani.anim'))
    write(rel,[(114,11400000,'MonoBehaviour',mono('Characters/CharacterDefinition.cs',name=name,**fields),False)])
    characters[name]=assetref(rel)

# Small authored UI icons. ItemDefinition.icon remains replaceable in the Inspector.
icons={}
for item in ['potato','bread','hoe','bag','gold']:
    im=Image.new('RGBA',(96,96));d=ImageDraw.Draw(im)
    edge=(66,49,34,255)
    if item=='potato':
        d.ellipse((14,22,84,77),fill='#b69565',outline=edge,width=3)
        for x,y in [(29,44),(58,37),(68,60),(42,64)]: d.ellipse((x,y,x+5,y+3),fill='#775a40')
    elif item=='bread':
        d.rounded_rectangle((11,28,86,75),radius=22,fill='#c29351',outline=edge,width=3)
        for x in [29,47,65]:d.line((x,35,x-6,51),fill='#efd49a',width=5)
    elif item=='hoe':
        d.line((30,83,65,18),fill='#513b26',width=10);d.line((30,83,65,18),fill='#9a7143',width=6)
        d.polygon([(53,14),(87,24),(79,43),(57,26)],fill='#a1acb2',outline=edge,width=3)
    elif item=='bag':
        d.rounded_rectangle((20,28,76,83),radius=12,fill='#886244',outline=edge,width=3)
        d.arc((34,12,63,47),180,360,fill=edge,width=6);d.rounded_rectangle((18,26,78,54),radius=8,fill='#b0875a',outline=edge,width=3)
        d.rectangle((43,43,52,57),fill='#d7ba6e',outline=edge,width=2)
    else:
        for x,y in [(17,46),(42,48),(29,24)]:
            d.ellipse((x,y,x+38,y+30),fill='#b48632',outline=edge,width=2);d.ellipse((x,y-5,x+38,y+25),fill='#e0c065',outline=edge,width=2);d.ellipse((x+9,y,x+29,y+18),outline='#a38037',width=2)
    rel=BASE/'UI/Icons'/(item+'.png');(ROOT/rel).parent.mkdir(parents=True,exist_ok=True);im.save(ROOT/rel)
    icons[item]=assetref(rel)
items={}
itemdata=[('potato','감자','막 수확한 감자. 납품하거나 상점에 판매할 수 있다.',1,1,20,3,None,'Assets/FarmCrops/Prefabs/Crops/Potato_07.prefab'),('bread','빵','먹으면 에너지가 25 회복된다.',1,1,10,10,None,'Assets/Fantastic Village Pack/prefabs/props/P_PROP_food_bread_01.prefab'),('hoe','농기구','주 무기에 장착하면 수확 속도가 빨라지고 에너지 소모가 줄어든다.',1,3,1,60,5,hoe),('bag','작은 가방','소지품 공간을 두 줄 늘린다.',2,2,1,100,None,'Assets/LowPolyMedievalBlacksmithAndCrafting/Models/LeatherBag_1/LeatherBag_1.fbx'),('gold','골드','마을에서 사용하는 금화. 한 칸에 1,000골드까지 보관한다.',1,1,1000,1,None,None)]
for id,name,description,w,h,stack,price,slot,model in itemdata:
    items[id]=so('Items/'+id+'.asset','ItemDefinition.cs',id={'hoe':'iron_hoe','bag':'small_bag'}.get(id,id),displayName=name,description=description,icon=icons[id],worldPrefab=assetref(model),width=w,height=h,maxStack=stack,price=price,equipmentSlot=slot or 0,equipmentPart=assetref(BASE/'Data/Characters/Parts/Hoe.asset') if slot else ref())
job=so('Jobs/PotatoDelivery.asset','PartTimeJobDefinition.cs',id='potato_delivery',title='감자 납품',description='해가 지기 전에 감자를 모아 농장에 납품한다.',item=items['potato'],startHour=8,endHour=18,tiers=[dict(requiredCompletions=n,quantity=q,gold=g,experience=x) for n,q,g,x in [(0,6,45,35),(3,10,80,60),(8,16,140,100)]],extraQuantity=4,extraGold=30,extraExperience=20)
questbase=dict(requiredCompletions=1,rewardGold=30,rewardAP=2,rewardExperience=15,contributionItem=items['potato'],requiredGold=100,requiredItems=20,goldPerContribution=25,itemsPerContribution=5,resultingWorldStage=2)
intro=so('Quests/FirstWork.asset','QuestDefinition.cs',id='first_work',title='첫 일자리',kind=0,description='미라의 아르바이트를 한 번 완수하고 인사를 전한다.',**questbase)
expansion=so('Quests/FarmExpansion.asset','QuestDefinition.cs',id='farm_expansion',title='함께 넓히는 농장',kind=1,description='마을 농장을 넓히는 데 힘을 보태자. 공사가 준비되면 다음 생에 넓어진 농장을 만날 수 있다.',**questbase)
npcrefs={}
configs=[('Mira','미라','농부',0,[('일상',0,'올해는 땅이 좋아. 감자도 잘 여물었어.'),('아르바이트',1,'밭에서 캐 온 감자를 모아 주겠어? 해 지기 전에 부탁해.'),('첫 일자리',2,'함께 일해 줘서 고마워.'),('마을 농장',3,'모두가 조금씩 힘을 보태면 농장을 넓힐 수 있어.')]),('Noah','노아','상인',1,[('일상',0,'어서 와. 오늘도 쓸 만한 물건을 가져왔어.'),('거래',4,'필요한 물건을 골라 봐. 수확한 감자도 매입하고 있어.')]),('Lena','레나','여관 주인',2,[('일상',0,'바쁜 하루였니? 잠깐 쉬었다 가도 좋아.'),('휴식',5,'따뜻한 방이 준비되어 있어. 내일 아침에 깨워 줄게.')]),('Shrine','기억의 우물','',3,[('기억',0,'잔잔한 물결 위로 오래된 기억이 스쳐 간다.'),('환생',6,'지나온 생의 기술과 경험을 품고 다시 시작한다.')]),('Board','마을 게시판','',4,[('아르바이트',1,'농장에서 일할 사람을 구합니다.')])]
for name,display,occupation,role,topics in configs:
    dialogue=so('Dialogues/'+name+'.asset','DialogueDefinition.cs',topics=[dict(keyword=k,action=a,text=t) for k,a,t in topics])
    npcrefs[name]=so('NPCs/'+name+'.asset','NpcDefinition.cs',id=name.lower(),displayName=display,occupation=occupation,role=role,appearance=characters.get(name,ref()),dialogue=dialogue,job=job if role in (0,4) else ref(),quests=[intro,expansion] if role==0 else [],shopItems=[items[i] for i in ['bread','hoe','bag']] if role==1 else [],interactionDistance=1.8)
settings=so('VillageSettings.asset','VillageSettings.cs',items=list(items.values()),defaultJob=job,introQuest=intro,expansionQuest=expansion,player=characters['Player_Default'],interfaceFont=assetref(BASE/'Fonts/WorkerbornKorean-Regular.otf'),minutesPerSecond=3,wakeHour=6,collapseHour=24,roomPrice=10,freeRestEnergy=70,collapseEnergy=45,rebirthLevel=3,apPerLevel=2,harvestExperience=8,harvestEnergy=5)

# Native materials and render targets.
materials={}
for name,rgb in [('Grass',(0.43,0.55,0.33)),('Path',(0.68,0.59,0.43)),('Soil',(0.31,0.23,0.16))]:
    source=(ROOT/BASE/'Materials/FarmSurface.mat').read_text().replace('m_Name: FarmSurface','m_Name: '+name)
    source=source.replace('_BaseColor: {r: 1, g: 1, b: 1, a: 1}',f'_BaseColor: {{r: {rgb[0]}, g: {rgb[1]}, b: {rgb[2]}, a: 1}}')
    rel=BASE/'Materials'/(name+'.mat');(ROOT/rel).write_text(source);materials[name]=assetref(rel)
for name,w,h in [('Minimap',512,512),('Portrait',256,512)]:
    fields=dict(m_ObjectHideFlags=0,m_CorrespondingSourceObject=ref(),m_PrefabInstance=ref(),m_PrefabAsset=ref(),m_Name=name,m_ImageContentsHash=Flow(serializedVersion=2,Hash='00000000000000000000000000000000'),m_IsAlphaChannelOptional=0,serializedVersion=6,m_Width=w,m_Height=h,m_AntiAliasing=1,m_MipCount=-1,m_DepthStencilFormat=92,m_ColorFormat=8,m_MipMap=0,m_GenerateMips=0,m_SRGB=1,m_UseDynamicScale=0,m_BindMS=0,m_EnableCompatibleFormat=1,m_TextureSettings=dict(serializedVersion=2,m_FilterMode=1,m_Aniso=0,m_MipBias=0,m_WrapU=1,m_WrapV=1,m_WrapW=1),m_Dimension=2,m_VolumeDepth=1)
    write(BASE/'UI'/(name+'.rendertexture'),[(84,8400000,'RenderTexture',fields,False)])

class Scene:
    def __init__(self):self.docs=[];self.n=100000;self.roots=[]
    def id(self):self.n+=1;return self.n
    def add(self,c,kind,values,i=None,strip=False):
        i=i or self.id();self.docs.append((c,i,kind,values,strip));return i
    def node(self,name,parent=None,pos=(0,0,0),rot=(0,0,0,1),scale=(1,1,1),layer=0,active=1,rect=None):
        go=self.id();t=self.id()
        g=dict(**common(),serializedVersion=6,m_Component=[dict(component=ref(t))],m_Layer=layer,m_Name=name,m_TagString='Untagged',m_Icon=ref(),m_NavMeshLayer=0,m_StaticEditorFlags=0,m_IsActive=active)
        self.add(1,'GameObject',g,go)
        tr=dict(**common(),m_GameObject=ref(go),m_LocalRotation=vec(*rot),m_LocalPosition=vec(*pos),m_LocalScale=vec(*scale),m_ConstrainProportionsScale=0,m_Children=[],m_Father=ref(parent['t'] if parent else 0),m_LocalEulerAnglesHint=vec(0,0,0))
        if rect:
            anchor,pivot,size,offset=rect;tr.update(m_AnchorMin=vec(*anchor),m_AnchorMax=vec(*anchor),m_AnchoredPosition=vec(*offset),m_SizeDelta=vec(*size),m_Pivot=vec(*pivot))
        else:tr['serializedVersion']=2
        self.add(224 if rect else 4,'RectTransform' if rect else 'Transform',tr,t)
        node=dict(go=go,t=t,g=g,tr=tr)
        if parent:parent['tr']['m_Children'].append(ref(t))
        else:self.roots.append(ref(t))
        return node
    def component(self,node,c,kind,fields):
        i=self.add(c,kind,dict(**common(),m_GameObject=ref(node['go']),**fields));node['g']['m_Component'].append(dict(component=ref(i)));return i
    def script(self,node,script,**fields):
        v=mono(script,go=node['go'],**fields);i=self.add(114,'MonoBehaviour',v);node['g']['m_Component'].append(dict(component=ref(i)));return i
    def model(self,parent,path,height,character=False):
        path=Path(path);low,high=geo.bounds(ROOT/path,character);factor=height/(high[1]-low[1]);go,t=geo.root_ids(ROOT/path)
        source={d['_id']:d.get('Transform') for d in geo.documents(ROOT/path)}.get(t) if path.suffix=='.prefab' else None
        source=source or {};p0=source.get('m_LocalPosition',{});s0=source.get('m_LocalScale',{})
        centre=[(low[0]+high[0])*.5,low[1],(low[2]+high[2])*.5]
        modifications=[]
        for prop,v in [(f'm_LocalPosition.{axis}',p0.get(axis,0)*factor-centre[j]*factor) for j,axis in enumerate('xyz')]+[(f'm_LocalScale.{axis}',s0.get(axis,1)*factor) for axis in 'xyz']:
            modifications.append(dict(target=assetref(path,t),propertyPath=prop,value=str(round(v,7)),objectReference=ref()))
        modifications.append(dict(target=assetref(path,go),propertyPath='m_Layer',value='2',objectReference=ref()))
        instance=self.add(1001,'PrefabInstance',dict(m_ObjectHideFlags=0,serializedVersion=2,m_Modification=dict(serializedVersion=3,m_TransformParent=ref(parent['t']),m_Modifications=modifications,m_RemovedComponents=[],m_RemovedGameObjects=[],m_AddedGameObjects=[],m_AddedComponents=[]),m_SourcePrefab=assetref(path,100100000)))
        transform=self.add(4,'Transform',dict(m_CorrespondingSourceObject=assetref(path,t),m_PrefabInstance=ref(instance),m_PrefabAsset=ref()),strip=True);parent['tr']['m_Children'].append(ref(transform))
        return transform
    def box(self,node,size=(1,1,1),centre=(0,0,0),trigger=0):
        return self.component(node,65,'BoxCollider',dict(m_Material=ref(),m_IncludeLayers=Flow(serializedVersion=2,m_Bits=0),m_ExcludeLayers=Flow(serializedVersion=2,m_Bits=0),m_LayerOverridePriority=0,m_IsTrigger=trigger,m_ProvidesContacts=0,m_Enabled=1,serializedVersion=3,m_Size=vec(*size),m_Center=vec(*centre)))
    def cube(self,name,parent,pos,scale,material,collide=False):
        node=self.node(name,parent,pos=pos,scale=scale)
        self.component(node,33,'MeshFilter',dict(m_Mesh=Flow(fileID=10202,guid='0000000000000000e000000000000000',type=0)))
        self.component(node,23,'MeshRenderer',dict(m_Enabled=1,m_CastShadows=1,m_ReceiveShadows=1,m_DynamicOccludee=1,m_StaticShadowCaster=0,m_MotionVectors=1,m_LightProbeUsage=1,m_ReflectionProbeUsage=1,m_RayTracingMode=2,m_RayTraceProcedural=0,m_RenderingLayerMask=1,m_RendererPriority=0,m_Materials=[materials[material]],m_StaticBatchInfo=Flow(firstSubMesh=0,subMeshCount=0),m_StaticBatchRoot=ref(),m_ProbeAnchor=ref(),m_LightProbeVolumeOverride=ref(),m_ScaleInLightmap=1,m_ReceiveGI=1,m_PreserveUVs=0,m_IgnoreNormalsForChartDetection=0,m_ImportantGI=0,m_StitchLightmapSeams=1,m_SelectedEditorRenderState=3,m_MinimumChartSize=4,m_AutoUVMaxDistance=.5,m_AutoUVMaxAngle=89,m_LightmapParameters=ref(),m_SortingLayerID=0,m_SortingLayer=0,m_SortingOrder=0,m_AdditionalVertexStreams=ref()))
        return node,self.box(node) if collide else None
    def camera(self,node,texture,ortho,size,mask):
        template=next(d['Camera'] for d in original if 'Camera'in d).copy()
        for key in common():template.pop(key,None)
        template.pop('m_GameObject',None)
        template.update(m_ClearFlags=2,m_BackGroundColor=color(.24,.3,.23,0),orthographic=ortho,**{'orthographic size':size,'near clip plane':.1,'far clip plane':180},m_CullingMask=Flow(serializedVersion=2,m_Bits=mask),m_TargetTexture=assetref(BASE/'UI'/(texture+'.rendertexture')),m_Depth=-2,m_HDR=0,m_AllowMSAA=0,m_OcclusionCulling=0)
        return self.component(node,20,'Camera',template)
    def ui(self,name,parent,size,pos=(0,0),anchor=(0,1),pivot=(0,1),bg=None):
        n=self.node(name,parent,layer=5,rect=(anchor,pivot,size,pos))
        if bg is not None:self.image(n,bg)
        return n
    def graphic(self,n,script,**fields):
        self.component(n,222,'CanvasRenderer',dict(m_CullTransparentMesh=1))
        return self.component(n,114,'MonoBehaviour',dict(m_Enabled=1,m_EditorHideFlags=0,m_Script=Flow(fileID=11500000,guid=script,type=3),m_Name='',m_EditorClassIdentifier='',**fields))
    def image(self,n,tint):
        return self.graphic(n,'fe87c0e1cc204ed48ad3b37840f39efc',m_Material=ref(),m_Color=color(*tint),m_RaycastTarget=1,m_RaycastPadding=vec(0,0,0,0),m_Maskable=1,m_OnCullStateChanged=dict(m_PersistentCalls=dict(m_Calls=[])),m_Sprite=ref(),m_Type=0,m_PreserveAspect=0,m_FillCenter=1,m_FillMethod=4,m_FillAmount=1,m_FillClockwise=1,m_FillOrigin=0,m_UseSpriteMesh=0,m_PixelsPerUnitMultiplier=1)
    def text(self,name,parent,content,size,pos=(0,0),fontSize=13,tint=(.19,.21,.19,1),align=3,anchor=(0,1),pivot=(0,1)):
        n=self.ui(name,parent,size,pos,anchor,pivot)
        i=self.graphic(n,'5f7201a12d95ffc409449d95f23cf332',m_Material=ref(),m_Color=color(*tint),m_RaycastTarget=0,m_RaycastPadding=vec(0,0,0,0),m_Maskable=1,m_OnCullStateChanged=dict(m_PersistentCalls=dict(m_Calls=[])),m_FontData=dict(m_Font=assetref(BASE/'Fonts/WorkerbornKorean-Regular.otf'),m_FontSize=fontSize,m_FontStyle=0,m_BestFit=0,m_MinSize=10,m_MaxSize=40,m_Alignment=align,m_AlignByGeometry=0,m_RichText=1,m_HorizontalOverflow=0,m_VerticalOverflow=1,m_LineSpacing=1),m_Text=content)
        return n,i

def yaw(degrees):return (0,math.sin(math.radians(degrees)/2),0,math.cos(math.radians(degrees)/2))
original=geo.documents(ROOT/BASE/'Scenes/HarvestPrototype.unity')
s=Scene()
legacy_roots={d['MonoBehaviour']['m_GameObject']['fileID'] for d in original if d.get('MonoBehaviour',{}).get('m_Script',{}).get('guid')==guid(BASE/'Runtime/HarvestPrototype.cs')}
# Retain Unity's authored URP camera, sun and volume with their original settings.
for d in original:
    if d['_id'] in (9223372036854775807,1000,1001,1002):continue
    kind=next(k for k in d if not k.startswith('_'));v=d[kind]
    if d['_id'] in legacy_roots or v.get('m_GameObject',{}).get('fileID') in legacy_roots:continue
    if kind=='GameObject' and v.get('m_Name','').startswith('Workerborn'):continue
    if kind=='MonoBehaviour' and v.get('m_Script',{}).get('guid')==guid(BASE/'Runtime/HarvestPrototype.cs'):continue
    if kind=='Transform' and v.get('m_GameObject',{}).get('fileID')==1000:continue
    s.docs.append((d['_class'],d['_id'],kind,v,False))
s.roots=[ref(330585546),ref(410087041),ref(832575519)]
maproot=s.node('Village')
buildings=s.node('Buildings',maproot);nature=s.node('Nature',maproot);npcs=s.node('NPCs',maproot);farm=s.node('Farm',maproot);paths=s.node('Paths',maproot)
ground,groundCollider=s.cube('Ground',maproot,(-3,-.35,5),(55,.7,43),'Grass',True)
for n,p,z in [('Town square',(-1,-.01,4),(17,.035,16)),('Farm lane',(-10,0,2),(18,.035,2.5)),('Inn lane',(8,0,4),(14,.035,3)),('North lane',(-1,0,14),(3,.035,19))]:s.cube(n,paths,p,z,'Path')
F='Assets/Fantastic Village Pack/prefabs/'
def blocker(parent,size,centre=None):
    n=s.node('Navigation and camera collision',parent,layer=8)
    s.box(n,size,centre or (0,size[1]/2,0));s.script(n,'World/VillageNavigationBlocker.cs');return n

def prop(name,path,pos,height,rotation=0,size=None,parent=None):
    n=s.node(name,parent or buildings,pos=pos,rot=yaw(rotation))
    visual=s.node('Visual',n);s.model(visual,path,height)
    s.script(n,'World/VillageMapProp.cs',visual=ref(visual['t']))
    if size:blocker(n,size)
    return n
for name,model,pos,h,angle,size in [
 ('Farmhouse','buildings/P_BLD_house_01.prefab',(-22,0,5.7),4.1,110,(4,4.4,4.4)),
 ('Inn','buildings/P_BLD_house_04.prefab',(10,0,5.8),5.2,180,(4.1,5.4,4.8)),
 ('General store','buildings/P_BLD_house_02.prefab',(3,0,12.7),4.8,180,(4.1,5,4.3)),
 ('Cottage','buildings/P_BLD_house_01.prefab',(-9,0,20),4.1,160,(3.6,4.3,4)),
 ('Workshop','buildings/P_BLD_house_04.prefab',(15,0,14.5),4.2,180,(3.5,4.4,4)),
 ('Market','props/P_PROP_market_v01_01.prefab',(7.8,0,10),2.4,180,(2.5,2.5,2)),
 ('Bench square','props/P_PROP_bench_01.prefab',(4.7,0,-2),.9,15,(1.6,1,.6)),
 ('Bench north','props/P_PROP_bench_01.prefab',(-4,0,15.5),.9,90,(1.6,1,.6)),
 ('Farm crate','props/P_PROP_crate_01.prefab',(-20,0,0),.7,0,(.8,.8,.8)),
 ('Store crate','props/P_PROP_crate_01.prefab',(5.6,0,8.2),.65,10,(.7,.7,.7))]:prop(name,F+model,pos,h,angle,size)
prop('Forge','Assets/LowPolyMedievalBlacksmithAndCrafting/Models/BlacksmithForgeWithHood/BlacksmithForgeWithHood.fbx',(13,0,10.6),1.7,180,(1.8,1.8,1.5))
for index,pos in enumerate([(-25,0,15),(-24,0,-7),(18,0,-5),(18,0,22),(-17,0,22),(8,0,22)]):prop('Tree '+str(index+1),F+'environment/P_ENV_TREE_village.prefab',pos,4.5,index*47,(.65,3,.65),nature)
lamps=[]
for index,pos in enumerate([(-5,0,-2),(6,0,2),(-3,0,11),(12,0,-1)]):
    n=prop('Street lamp '+str(index+1),F+'props/P_PROP_streetlamp_v01_01.prefab',pos,2.5,0,(.3,2.5,.3),nature)
    light=s.node('Warm light',n,pos=(0,2.2,0))
    template=next(d['Light'] for d in original if 'Light'in d).copy()
    for key in [*common(),'m_GameObject']:template.pop(key,None)
    template.update(m_Type=2,m_Color=color(1,.74,.4,1),m_Intensity=2,m_Range=5,m_Enabled=0)
    lamps.append(ref(s.component(light,108,'Light',template)))
for name,pos,angle in [('Mira',(-6.7,0,2.4),180),('Noah',(2.8,0,8.2),180),('Lena',(9.2,0,1.8),210)]:
    n=s.node(name,npcs,pos=pos,rot=yaw(angle));visual=s.node('Appearance',n)
    s.model(visual,'Assets/stylizedLittleHero/Prefabs/'+templates[name]+'.prefab',1.7,True)
    s.script(n,'Characters/CharacterActor.cs',appearance=ref(),npc=npcrefs[name],playerSettings=ref(),visualRoot=ref(visual['t']),renderLayer=2)
    s.script(n,'WorkerAnimation.cs');s.script(n,'Village/VillageInteractable.cs',definition=npcrefs[name]);s.box(n,(.8,1.8,.8),(0,.9,0),1);blocker(n,(.45,2,.45))
for name,path,pos,height in [('Board',F+'props/P_PROP_signpost_01.prefab',(-2.6,0,2),1.8),('Shrine',F+'props/P_PROP_well.prefab',(-1,0,18),1.4)]:
    n=prop(name,path,pos,height,0,(1,1.6,1));s.script(n,'Village/VillageInteractable.cs',definition=npcrefs[name]);s.box(n,(1.2,1.7,1.2),(0,.85,0),1)
for row in range(6):
    if row==3:
        group=s.node('Stage 2 - expanded farm',farm,active=0);s.script(group,'World/VillageWorldStage.cs',minimumStage=2,maximumStage=0)
    parent=farm if row<3 else group
    s.cube('Soil row '+str(row+1),parent,(-13,.025,.4+row*2.8),(11.4,.08,2.2),'Soil')
    for col in range(4):
        num=row*4+col+1;n=s.node('Potato '+str(num).zfill(2),parent,pos=(-13+(col-1.5)*2.6,.065,.4+row*2.8))
        visual=s.node('Plant visual',n);s.model(visual,'Assets/FarmCrops/Prefabs/Crops/Potato_04.prefab',.95)
        s.box(n,(1.5,1,1.4),(0,.45,0),1)
        s.script(n,'HarvestableCrop.cs',definition=assetref(BASE/'Data/Potato.asset'),visual=ref(visual['t']),charges=[],persistentId='farm-'+str(num).zfill(2))
player=s.node('Player',pos=(0,0,-4.4));visual=s.node('Appearance',player)
s.model(visual,'Assets/stylizedLittleHero/Prefabs/Thief_M.prefab',1.7,True)
actor=s.script(player,'Characters/CharacterActor.cs',appearance=ref(),npc=ref(),playerSettings=settings,visualRoot=ref(visual['t']),renderLayer=9)
s.script(player,'WorkerAnimation.cs');worker=s.script(player,'HarvestWorker.cs')
s.component(player,195,'NavMeshAgent',dict(m_Enabled=0,m_AgentTypeID=0,m_Radius=.32,m_Speed=3.7,m_Acceleration=20,avoidancePriority=50,m_AngularSpeed=540,m_StoppingDistance=.08,m_AutoTraverseOffMeshLink=1,m_AutoBraking=1,m_AutoRepath=1,m_Height=1.8,m_BaseOffset=0,m_WalkableMask=4294967295,m_ObstacleAvoidanceType=4))
portrait=s.node('Equipment portrait camera',player,pos=(0,.85,3),rot=yaw(180));portraitCamera=s.camera(portrait,'Portrait',1,1.05,1<<9)
mini=s.node('Minimap camera',pos=(-3,80,5),rot=(math.sqrt(.5),0,0,math.sqrt(.5)));miniCamera=s.camera(mini,'Minimap',1,28,(1<<0)|(1<<2)|(1<<9))
spawn=s.node('Player spawn',pos=(0,0,-4.4));rest=s.node('After rest',pos=(9,0,-1));drops=s.node('Dropped items')
controller=s.node('Village session')
nav=s.script(controller,'World/VillageNavigation.cs',ground=ref(groundCollider),obstacleRoot=ref(maproot['t']),bakedData=ref())
rigfields=dict(fieldOfView=50,pivotHeight=1.1,initialDistance=7.5,minimumDistance=1.8,maximumDistance=22,initialYaw=0,initialPitch=18,pitchLimits=vec(-5,75),orbitSensitivity=.18,zoomSensitivity=.18,followSmoothness=10,collisionMask=Flow(serializedVersion=2,m_Bits=1<<8))
rig=s.add(114,'MonoBehaviour',mono('Village/VillageCameraRig.cs',go=330585543,**rigfields))
for c,i,k,v,st in s.docs:
    if i==330585543:v['m_Component'].append(dict(component=ref(rig)))
    if i==330585546:v.update(m_LocalPosition=vec(0,3.42,-11.53),m_LocalRotation=vec(math.sin(math.radians(9)),0,0,math.cos(math.radians(9))))
    if i==330585545:v.update(**{'field of view':50,'near clip plane':.08,'far clip plane':180})
# Thin native HUD: editable before Play, with gameplay windows opened on demand.
ui=s.ui('Village UI',None,(1600,900),(0,0),(0,0),(0,0))
s.component(ui,223,'Canvas',dict(m_Enabled=1,serializedVersion=3,m_RenderMode=0,m_Camera=ref(),m_PlaneDistance=100,m_PixelPerfect=0,m_ReceivesEvents=1,m_OverrideSorting=0,m_OverridePixelPerfect=0,m_SortingBucketNormalizedSize=0,m_VertexColorAlwaysGammaSpace=0,m_AdditionalShaderChannelsFlag=25,m_UpdateRectTransformForStandalone=0,m_SortingLayerID=0,m_SortingOrder=20,m_TargetDisplay=0))
def uiscript(node,g,**fields):return s.component(node,114,'MonoBehaviour',dict(m_Enabled=1,m_EditorHideFlags=0,m_Script=Flow(fileID=11500000,guid=g,type=3),m_Name='',m_EditorClassIdentifier='',**fields))
uiscript(ui,'0cd44c1031e13a943bb63640046fad76',m_UiScaleMode=1,m_ReferencePixelsPerUnit=100,m_ScaleFactor=1,m_ReferenceResolution=vec(1600,900),m_ScreenMatchMode=0,m_MatchWidthOrHeight=.5,m_PhysicalUnit=3,m_FallbackScreenDPI=96,m_DefaultSpriteDPI=96,m_DynamicPixelsPerUnit=1,m_PresetInfoIsWorld=0)
uiscript(ui,'dc42784cf147c0c48a680349fa168899',m_IgnoreReversedGraphics=1,m_BlockingObjects=0,m_BlockingMask=Flow(serializedVersion=2,m_Bits=4294967295))
bar=s.ui('System bar',ui,(0,28),(0,0),(0,0),(0,0),(.92,.92,.9,.97));bar['tr']['m_AnchorMax']=vec(1,0)
status=s.ui('Character status',ui,(360,52),(0,32),(.5,0),(.5,0),(.1,.13,.17,.55))
_,growth=s.text('Level',status,'Lv.1',(65,25),(8,-3),12,(1,1,1,1));_,energy=s.text('Energy',status,'100/100',(270,25),(80,-3),12,(1,1,1,1),5)
def fill(name,parent,size,pos,tint):
    bg=s.ui(name+' background',parent,size,pos,bg=(.17,.19,.2,1));n=s.ui(name,bg,size,(0,0),bg=tint)
    # Filled images preserve bar shape when changing fillAmount.
    for c,i,k,v,st in s.docs:
        if k=='MonoBehaviour' and v.get('m_GameObject',{}).get('fileID')==n['go']:
            v.update(m_Type=3,m_FillMethod=0,m_FillOrigin=0);return i
energyFill=fill('Stamina bar',status,(270,6),(80,-29),(.55,.72,.4,1));xpFill=fill('Experience bar',status,(342,3),(8,-44),(.85,.75,.45,1))
_,clock=s.text('Clock',ui,'08:00',(180,30),(-16,-12),16,(1,1,1,1),5,(1,1),(1,1))
minipanel=s.ui('Mini map',ui,(190,170),(-12,-49),(1,1),(1,1),(.92,.92,.9,.97));s.text('Village name',minipanel,'솔바람 마을',(174,25),(8,-1),12)
mapimage=s.ui('Map image',minipanel,(174,132),(8,-30),bg=(1,1,1,1))
toast=s.ui('System message',ui,(720,34),(0,104),(.5,0),(.5,0));_,toastText=s.text('Message',toast,'',(720,34),(0,0),14,(1,1,1,1),4)
hud=s.script(ui,'Village/VillageHud.cs',clockText=ref(clock),energyText=ref(energy),growthText=ref(growth),toastText=ref(toastText),energyFill=ref(energyFill),experienceFill=ref(xpFill),toast=ref(toast['t']),minimapPanel=ref(minipanel['t']),minimapImage=ref(mapimage['t']))
for index,(title,id) in enumerate(zip(['메뉴','캐릭터','스킬','소지품','퀘스트','지도'],['system','character','skills','inventory','quests','map'])):
    n=s.ui(title,bar,(68,24),(3+index*72,-2),bg=(.82,.83,.83,1));graphic=n['g']['m_Component'][-1]['component']
    uiscript(n,'4e29b1a8efbd4b44bb3f3716e73f07ff',m_Navigation=dict(m_Mode=0,m_WrapAround=0,m_SelectOnUp=ref(),m_SelectOnDown=ref(),m_SelectOnLeft=ref(),m_SelectOnRight=ref()),m_Transition=1,m_Colors=dict(m_NormalColor=color(1,1,1,1),m_HighlightedColor=color(.9,.94,1,1),m_PressedColor=color(.7,.78,.9,1),m_SelectedColor=color(1,1,1,1),m_DisabledColor=color(.5,.5,.5,.5),m_ColorMultiplier=1,m_FadeDuration=.1),m_SpriteState=dict(m_HighlightedSprite=ref(),m_PressedSprite=ref(),m_SelectedSprite=ref(),m_DisabledSprite=ref()),m_AnimationTriggers=dict(m_NormalTrigger='Normal',m_HighlightedTrigger='Highlighted',m_PressedTrigger='Pressed',m_SelectedTrigger='Selected',m_DisabledTrigger='Disabled'),m_Interactable=1,m_TargetGraphic=graphic,m_OnClick=dict(m_PersistentCalls=dict(m_Calls=[dict(m_Target=ref(hud),m_TargetAssemblyTypeName='Workerborn.VillageHud, Workerborn.Runtime',m_MethodName='ToggleWindow',m_Mode=5,m_Arguments=dict(m_ObjectArgument=ref(),m_ObjectArgumentAssemblyTypeName='UnityEngine.Object, UnityEngine',m_IntArgument=0,m_FloatArgument=0,m_StringArgument=id,m_BoolArgument=0),m_CallState=2)])))
    s.text(title,n,title,(64,22),(2,-1),12,align=4)
events=s.node('EventSystem')
uiscript(events,'76c392e42b5098c458856cdf6ecaaaa1',m_FirstSelected=ref(),m_sendNavigationEvents=1,m_DragThreshold=10)
uiscript(events,'01614664b831546d2ae94a42149d80ac',m_SendPointerHoverToParent=1,m_MoveRepeatDelay=.5,m_MoveRepeatRate=.1,m_XRTrackingOrigin=ref(),m_ActionsAsset=ref(),m_DeselectOnBackgroundClick=1,m_PointerBehavior=0,m_CursorLockBehavior=0,m_ScrollDeltaPerTick=6)
s.script(controller,'Village/VillagePrototype.cs',settings=settings,mapRoot=ref(maproot['t']),navigation=ref(nav),player=ref(actor),worker=ref(worker),worldCamera=ref(330585545),cameraRig=ref(rig),hud=ref(hud),minimapCamera=ref(miniCamera),minimapTexture=assetref(BASE/'UI/Minimap.rendertexture'),portraitCamera=ref(portraitCamera),portraitTexture=assetref(BASE/'UI/Portrait.rendertexture'),spawnPoint=ref(spawn['t']),restPoint=ref(rest['t']),droppedItems=ref(drops['t']),potato=assetref(BASE/'Data/Potato.asset'),sun=ref(410087040),lanterns=lamps,loadSavedGame=1)
s.add(1660057539,'SceneRoots',dict(m_ObjectHideFlags=0,m_Roots=s.roots),9223372036854775807)
write(BASE/'Scenes/VillagePrototype.unity',s.docs)

# Stable importer metadata for all new project-owned assets.
for path in [ROOT/BASE,*sorted((ROOT/BASE).rglob('*'))]:
    if path.suffix=='.meta':continue
    meta=Path(str(path)+'.meta')
    if meta.exists():continue
    rel=path.relative_to(ROOT);head='fileFormatVersion: 2\nguid: '+guid(rel)+'\n'
    if path.is_dir():body='folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n'
    elif path.suffix=='.cs':body='MonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n'
    elif path.suffix in ('.asset','.mat','.rendertexture'):body='NativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: '+str({'.mat':2100000,'.rendertexture':8400000}.get(path.suffix,11400000))+'\n'
    elif path.suffix=='.png':body='TextureImporter:\n  internalIDToNameTable: []\n  externalObjects: {}\n  serializedVersion: 13\n  mipmaps:\n    enableMipMap: 0\n  isReadable: 0\n  textureType: 8\n  spriteMode: 1\n  spritePixelsToUnits: 100\n  spriteMeshType: 1\n  alphaIsTransparency: 1\n  textureSettings:\n    serializedVersion: 2\n    filterMode: 1\n    aniso: 1\n    mipBias: 0\n    wrapU: 1\n    wrapV: 1\n    wrapW: 1\n  spriteSheet:\n    serializedVersion: 2\n    sprites: []\n    outline: []\n    physicsShape: []\n    bones: []\n    spriteID: '+uuid.uuid5(uuid.NAMESPACE_URL,str(rel)).hex+'\n    internalID: 0\n    vertices: []\n    indices:\n    edges: []\n    weights: []\n    secondaryTextures: []\n    nameFileIdTable: {}\n'
    else:body='DefaultImporter:\n  externalObjects: {}\n'
    meta.write_text(head+body+'  userData:\n  assetBundleName:\n  assetBundleVariant:\n')
print('Authored native village Scene, 24 crops, four character presets, modular part library, content assets and HUD.')
