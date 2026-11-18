"""Native Unity YAML helpers for explicit offline authoring tools; never executed by Unity."""
from pathlib import Path
import math
import re
import uuid
import yaml
import unity_asset_geometry as geo
ROOT=Path(__file__).resolve().parents[1]
BASE=Path('Assets/Workerborn')
HEADER='%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n'
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
    return Flow(fileID=identifier,guid=guid(path),type=2 if ext in ('.asset','.anim','.mat','.rendertexture','.terrainlayer','.shadervariants') else 3)

def write(path,docs):
    path=ROOT/path;path.parent.mkdir(parents=True,exist_ok=True)
    path.write_text(HEADER+''.join(f'--- !u!{c} &{i}{" stripped" if strip else ""}\n'+dump({kind:values}) for c,i,kind,values,strip in docs))

def common():return dict(m_ObjectHideFlags=0,m_CorrespondingSourceObject=ref(),m_PrefabInstance=ref(),m_PrefabAsset=ref())

def mono(script,name='',go=0,**fields):
    return dict(**common(),m_GameObject=ref(go),m_Enabled=1,m_EditorHideFlags=0,m_Script=assetref(BASE/'Runtime'/script),m_Name=name,m_EditorClassIdentifier='',**fields)

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
        from tmp_ui import convert_text,TMP_GUID
        fields=dict(m_Material=ref(),m_Color=color(*tint),m_RaycastTarget=0,m_RaycastPadding=vec(0,0,0,0),m_Maskable=1,m_OnCullStateChanged=dict(m_PersistentCalls=dict(m_Calls=[])),m_FontData=dict(m_Font=assetref(BASE/'Fonts/WorkerbornKorean-Regular.otf'),m_FontSize=fontSize,m_FontStyle=0,m_BestFit=0,m_MinSize=10,m_MaxSize=40,m_Alignment=align,m_AlignByGeometry=0,m_RichText=1,m_HorizontalOverflow=0,m_VerticalOverflow=1,m_LineSpacing=1),m_Text=content)
        convert_text(fields,name in ['NpcName','Name','FloatingText','Object name'])
        fields.pop('m_Script')
        i=self.graphic(n,TMP_GUID,**fields)
        return n,i
original=geo.documents(ROOT/BASE/'Scenes/VillagePrototype.unity')
materials={}
