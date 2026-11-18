"""Read-only expansion of project-owned prefab instances for static checks.
Vendor model prefabs remain opaque imported geometry. Unity is the final importer.
"""
import copy
from pathlib import Path
from unity_asset_geometry import documents, GUIDS, ROOT

def body(doc):return doc[next(k for k in doc if not k.startswith('_'))]
def remap(value,ids):
    if isinstance(value,dict):
        if 'fileID'in value and 'guid'not in value:return {'fileID':ids.get(value['fileID'],value['fileID'])}
        return {k:remap(v,ids)for k,v in value.items()}
    if isinstance(value,list):return [remap(v,ids)for v in value]
    return value

def expand(path):
    result=documents(path)
    for iteration in range(500):
        chosen=None;source=None
        for d in result:
            instance=d.get('PrefabInstance')
            if instance is None:continue
            candidate=GUIDS.get(instance['m_SourcePrefab']['guid'])
            if candidate is not None and candidate.is_relative_to(ROOT/'Assets/Workerborn/Prefabs'):
                chosen=d;source=candidate;break
        if chosen is None:return result
        original=documents(source);instance_id=chosen['_id'];guid=chosen['PrefabInstance']['m_SourcePrefab']['guid']
        aliases=[d for d in result if body(d).get('m_PrefabInstance',{}).get('fileID')==instance_id and body(d).get('m_CorrespondingSourceObject',{}).get('guid')==guid]
        ids={d['_id']:f'{instance_id}:{d["_id"]}'for d in original}
        for d in aliases:ids[body(d)['m_CorrespondingSourceObject']['fileID']]=d['_id']
        cloned=[]
        for d in original:
            new=copy.deepcopy(d);new['_id']=ids[d['_id']]
            kind=next(k for k in new if not k.startswith('_'));new[kind]=remap(new[kind],ids);cloned.append(new)
        indexed={d['_id']:d for d in cloned}
        for mod in chosen['PrefabInstance']['m_Modification'].get('m_Modifications',[]):
            if mod['target'].get('guid')!=guid:continue
            target=ids.get(mod['target']['fileID'])
            if target not in indexed:continue
            current=body(indexed[target]);parts=mod['propertyPath'].split('.')
            if any(p in ('Array',)for p in parts):continue
            for part in parts[:-1]:current=current.setdefault(part,{})
            key=parts[-1];old=current.get(key)
            if mod.get('objectReference',{}).get('fileID'):new=mod['objectReference']
            elif isinstance(old,int):
                try:new=int(mod['value'])
                except ValueError:new=float(mod['value'])
            elif isinstance(old,float):new=float(mod['value'])
            else:new=mod['value']
            current[key]=new
        root=next(d for d in cloned if ('Transform'in d or 'RectTransform'in d) and 'm_GameObject'in body(d) and body(d).get('m_Father',{}).get('fileID')==0)
        body(root)['m_Father']=chosen['PrefabInstance']['m_Modification']['m_TransformParent']
        deleted={d['_id']for d in aliases}|{instance_id}
        result=[d for d in result if d['_id']not in deleted]+cloned
    raise AssertionError('Recursive or unexpectedly deep project-owned prefab graph')
