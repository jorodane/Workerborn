"""Explicit authoring of camera fade shader variants; no import or Play-mode generation.
Copies the supplied Shader Graphs and multiplies their Alpha output by _CameraOpacity.
All source texture, color, moss and wind nodes are preserved.
"""
import copy,json,re,uuid,struct
from pathlib import Path
from unity_yaml import *

def graph_docs(path):
    s=(ROOT/path).read_text();decoder=json.JSONDecoder();docs=[];index=0
    while index<len(s):
        while index<len(s)and s[index].isspace():index+=1
        if index<len(s):value,index=decoder.raw_decode(s,index);docs.append(value)
    return docs

def clone_graph(source,target):
    docs=graph_docs(source);templates=graph_docs(Path('Assets/Synty/PolygonNature/Shaders/SyntyStudios_Trees.shadergraph'))
    by={d.get('m_ObjectId'):d for d in templates+docs};root=docs[0]
    def new_id(tag):return uuid.uuid5(uuid.NAMESPACE_URL,'workerborn-camera-fade/'+str(target)+'/'+tag).hex
    original_edge=None
    alpha=next((d for d in docs if d.get('m_Type')=='UnityEditor.ShaderGraph.BlockNode'and d.get('m_Name')=='SurfaceDescription.Alpha'),None)
    if alpha is None:
        alpha=copy.deepcopy(next(d for d in templates if d.get('m_Type')=='UnityEditor.ShaderGraph.BlockNode'and d.get('m_Name')=='SurfaceDescription.Alpha'))
        alpha['m_ObjectId']=new_id('alpha block');slots=[]
        for old in alpha['m_Slots']:
            slot=copy.deepcopy(by[old['m_Id']]);slot['m_ObjectId']=new_id('alpha block/'+old['m_Id']);docs.append(slot);slots.append({'m_Id':slot['m_ObjectId']})
        alpha['m_Slots']=slots;docs.append(alpha);root['m_Nodes'].append({'m_Id':alpha['m_ObjectId']});root['m_FragmentContext']['m_Blocks'].append({'m_Id':alpha['m_ObjectId']})
    original_edge=next((e for e in root['m_Edges']if e['m_InputSlot']['m_Node']['m_Id']==alpha['m_ObjectId']),None)
    scalar=next(d for d in templates if d.get('m_Type')=='UnityEditor.ShaderGraph.Internal.Vector1ShaderProperty')
    propnode=next(d for d in templates if d.get('m_Type')=='UnityEditor.ShaderGraph.PropertyNode'and d.get('m_Property',{}).get('m_Id')==scalar['m_ObjectId'])
    mult=next(d for d in templates if d.get('m_Type')=='UnityEditor.ShaderGraph.MultiplyNode')
    def new_id(tag):return uuid.uuid5(uuid.NAMESPACE_URL,'workerborn-camera-fade/'+str(target)+'/'+tag).hex
    opacity=copy.deepcopy(scalar);opacity.update(m_ObjectId=new_id('opacity property'),m_Name='Camera opacity',m_DefaultReferenceName='_CameraOpacity',m_OverrideReferenceName='_CameraOpacity',m_Value=1.0,m_Hidden=True,m_GeneratePropertyBlock=True)
    opacity['m_Guid']={'m_GuidSerialized':str(uuid.UUID(new_id('property-guid')))}
    docs.append(opacity);root['m_Properties'].append({'m_Id':opacity['m_ObjectId']})
    def clone_node(template,name):
        n=copy.deepcopy(template);n['m_ObjectId']=new_id(name);n['m_Group']={'m_Id':''};n['m_Name']='Camera '+name
        n['m_Slots']=[]
        for slot in template['m_Slots']:
            original=by[slot['m_Id']];value=copy.deepcopy(original);value['m_ObjectId']=new_id(name+'/'+slot['m_Id']);docs.append(value);n['m_Slots'].append({'m_Id':value['m_ObjectId']})
        docs.append(n);root['m_Nodes'].append({'m_Id':n['m_ObjectId']});return n
    pn=clone_node(propnode,'opacity node');pn['m_Property']={'m_Id':opacity['m_ObjectId']}
    mn=clone_node(mult,'alpha fade')
    # Scalar connection types are inferred by Shader Graph, including the original texture alpha.
    for slot in mn['m_Slots']:
        value=next(d for d in docs if d.get('m_ObjectId')==slot['m_Id'])
        if value.get('m_Id')==0:value['m_Value']={'x':1.0,'y':1.0,'z':1.0,'w':1.0}
    def endpoint(node,slot):return {'m_Node':{'m_Id':node},'m_SlotId':slot}
    if original_edge:
        root['m_Edges'].remove(original_edge);root['m_Edges'].append({'m_OutputSlot':original_edge['m_OutputSlot'],'m_InputSlot':endpoint(mn['m_ObjectId'],0)})
    root['m_Edges'] += [{'m_OutputSlot':endpoint(pn['m_ObjectId'],0),'m_InputSlot':endpoint(mn['m_ObjectId'],1)},{'m_OutputSlot':endpoint(mn['m_ObjectId'],2),'m_InputSlot':endpoint(alpha['m_ObjectId'],0)}]
    root['m_Path']='Workerborn/Camera Fade'
    for d in docs:
        if d.get('m_Type','').endswith('UniversalTarget'):
            d.update(m_AllowMaterialOverride=True,m_SurfaceType=1,m_AlphaMode=0,m_AlphaClip=False)
    path=ROOT/target;path.write_text('\n\n'.join(json.dumps(d,indent=4)for d in docs)+'\n')
    meta=(ROOT/(str(source)+'.meta')).read_text();meta=re.sub(r'^guid: .*','guid: '+guid(target),meta,flags=re.M);Path(str(path)+'.meta').write_text('\n'.join(line.rstrip()for line in meta.splitlines())+'\n')

def read(path):
    text=(ROOT/path).read_text();ids={int(i)for i in re.findall(r'^--- !u!\d+ &(-?\d+) stripped$',text,re.M)}
    return [(d['_class'],d['_id'],next(k for k in d if not k.startswith('_')),next(v for k,v in d.items()if not k.startswith('_')),d['_id']in ids)for d in geo.documents(ROOT/path)]

pairs=[]
for name in ['Trees','Moss','LOD']:
    source=Path('Assets/Synty/PolygonNature/Shaders')/('SyntyStudios_'+name+'.shadergraph');target=BASE/'Shaders'/('CameraFade'+name+'.shadergraph');clone_graph(source,target)
    pairs.append(dict(source=assetref(source,-6465566751694194690),fading=assetref(target,-6465566751694194690)))
shader=BASE/'Shaders/CameraFadeSurface.shader';keywords=set()
for mat in (ROOT/BASE/'Materials/Toon').glob('*.mat'):
    value=next(d['Material']for d in geo.documents(mat)if'Material'in d);ks=set(value['m_ValidKeywords'])-{'_ALPHATEST_ON','_ALPHAPREMULTIPLY_ON'}
    for fog in ['', 'FOG_EXP2']:keywords.add(' '.join(sorted(ks | ({fog}if fog else set()))))
shaders=[dict(first=assetref(shader,4800000),second=dict(variants=[dict(keywords=k,passType=13)for k in sorted(keywords)]))]
for pair in pairs:
    combinations={'_SURFACE_TYPE_TRANSPARENT','FOG_EXP2 _SURFACE_TYPE_TRANSPARENT'}
    for mat in (ROOT/'Assets/Synty/PolygonNature/Materials').rglob('*.mat'):
        material=next(d['Material']for d in geo.documents(mat)if'Material'in d)
        if material['m_Shader'].get('guid')!=pair['source']['guid']:continue
        ks=set(material.get('m_ValidKeywords',[]))-{'_ALPHATEST_ON','_ALPHAPREMULTIPLY_ON'}
        ks.add('_SURFACE_TYPE_TRANSPARENT')
        for fog in ['', 'FOG_EXP2']:combinations.add(' '.join(sorted(ks|({fog}if fog else set()))))
    shaders.append(dict(first=pair['fading'],second=dict(variants=[dict(keywords=k,passType=13)for k in sorted(combinations)])))
path=BASE/'Rendering/CameraFade.shadervariants';write(path,[(200,20000000,'ShaderVariantCollection',dict(**common(),m_Name='Camera Fade',m_Shaders=shaders),False)])
# The scene stores the library explicitly so player builds retain these variants.
ds=read(BASE/'Scenes/VillagePrototype.unity')
for c,i,k,v,st in ds:
    if v.get('m_Script',{}).get('guid')==guid(BASE/'Runtime/Rendering/CameraVisibility.cs'):
        v['fadeVariants']=assetref(path,20000000);v['fadeShaders']=pairs
write(BASE/'Scenes/VillagePrototype.unity',ds)
print('Authored three opacity-aware Shader Graphs with the supplied forest shading and wind.')
