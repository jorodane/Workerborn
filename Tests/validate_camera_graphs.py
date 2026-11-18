"""Read-only structure checks for the supplied graphs' camera opacity variants."""
from pathlib import Path
import json
ROOT=Path(__file__).resolve().parents[1]
for path in sorted((ROOT/'Assets/Workerborn/Shaders').glob('CameraFade*.shadergraph')):
    source=path.read_text();decoder=json.JSONDecoder();objects=[];index=0
    while index<len(source):
        while index<len(source)and source[index].isspace():index+=1
        if index<len(source):value,index=decoder.raw_decode(source,index);objects.append(value)
    ids=[d['m_ObjectId']for d in objects]
    assert len(ids)==len(set(ids)),f'Duplicate graph object IDs: {path.name}'
    by={d['m_ObjectId']:d for d in objects};graph=objects[0]
    def refs(value):
        if isinstance(value,dict):
            if set(value)=={'m_Id'}and value['m_Id']:yield value['m_Id']
            for v in value.values():yield from refs(v)
        elif isinstance(value,list):
            for v in value:yield from refs(v)
    assert all(ref in by for ref in refs(objects)),path.name
    for edge in graph['m_Edges']:
        for side in ['m_InputSlot','m_OutputSlot']:
            endpoint=edge[side];node=by[endpoint['m_Node']['m_Id']]
            assert endpoint['m_SlotId']in{by[p['m_Id']]['m_Id']for p in node['m_Slots']},(path.name,edge)
    prop=next(d for d in objects if d.get('m_OverrideReferenceName')=='_CameraOpacity')
    assert prop['m_Value']==1
    propnode=next(d for d in objects if d.get('m_Property',{}).get('m_Id')==prop['m_ObjectId'])
    alpha=next(d for d in objects if d.get('m_Name')=='SurfaceDescription.Alpha')
    final=next(e for e in graph['m_Edges']if e['m_InputSlot']['m_Node']['m_Id']==alpha['m_ObjectId'])
    multiply=by[final['m_OutputSlot']['m_Node']['m_Id']]
    assert multiply['m_Type']=='UnityEditor.ShaderGraph.MultiplyNode'
    assert any(e['m_OutputSlot']['m_Node']['m_Id']==propnode['m_ObjectId']and e['m_InputSlot']['m_Node']['m_Id']==multiply['m_ObjectId']for e in graph['m_Edges'])
    target=next(d for d in objects if d.get('m_Type','').endswith('UniversalTarget'))
    assert target['m_AllowMaterialOverride']and target['m_SurfaceType']==1 and not target['m_AlphaClip']
    print(f'{path.name}: {len(objects)} unique objects, valid edge slots, transparent alpha multiplied by camera opacity.')
