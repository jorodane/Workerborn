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

def expand(path, include_vendor_geometry=False):
    result=documents(path)
    for iteration in range(500):
        chosen=None;source=None
        for d in result:
            instance=d.get('PrefabInstance')
            if instance is None:continue
            candidate=GUIDS.get(instance['m_SourcePrefab']['guid'])
            if candidate is not None and (candidate.is_relative_to(ROOT/'Assets/Workerborn/Prefabs') or include_vendor_geometry and candidate.suffix=='.prefab'):
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
            elif parts[0] in {'m_LocalPosition','m_LocalRotation','m_LocalScale'}:new=float(mod['value'])
            elif isinstance(old,int):
                try:new=int(mod['value'])
                except ValueError:new=float(mod['value'])
            elif isinstance(old,float):new=float(mod['value'])
            else:new=mod['value']
            current[key]=new
        root=next(d for d in cloned if ('Transform'in d or 'RectTransform'in d) and 'm_GameObject'in body(d) and body(d).get('m_Father',{}).get('fileID')==0)
        overrides=chosen['PrefabInstance']['m_Modification']
        body(root)['m_Father']=overrides['m_TransformParent']
        # A scene root can reference the prefab instance handle instead of a stripped Transform.
        for d in result:
            for slot in d.get('SceneRoots',{}).get('m_Roots',[]):
                if slot['fileID']==instance_id:
                    slot['fileID']=root['_id']
        # Unity saves structural overrides separately from property modifications.
        # Follow the added child's actual local transform alias, not the vendor asset.
        for added in overrides.get('m_AddedGameObjects',[]):
            parent=indexed.get(ids.get(added['targetCorrespondingSourceObject']['fileID']))
            if parent and ('Transform' in parent or 'RectTransform' in parent):
                children=body(parent).setdefault('m_Children',[])
                at=added.get('insertIndex',-1)
                children.insert(at if at>=0 else len(children),added['addedObject'])
        removed_gos={ids[p['fileID']] for p in overrides.get('m_RemovedGameObjects',[]) if p['fileID'] in ids}
        removed_ids=set(removed_gos)
        if removed_gos:
            transforms={d['_id']:body(d) for d in cloned if 'Transform' in d or 'RectTransform' in d}
            def remove_branch(tid):
                removed_ids.add(tid)
                transform=transforms.get(tid)
                if transform is None:return
                go=transform.get('m_GameObject',{}).get('fileID')
                if go in indexed:
                    removed_ids.add(go)
                    removed_ids.update(c['component']['fileID'] for c in body(indexed[go]).get('m_Component',[]))
                instance=transform.get('m_PrefabInstance',{}).get('fileID')
                if instance:
                    removed_ids.add(instance)
                for child in transform.get('m_Children',[]):
                    remove_branch(child['fileID'])
            for tid,transform in transforms.items():
                if transform.get('m_GameObject',{}).get('fileID') in removed_gos:
                    remove_branch(tid)
            for transform in transforms.values():
                if 'm_Children' in transform:
                    transform['m_Children']=[c for c in transform['m_Children'] if c['fileID'] not in removed_ids]
            cloned=[d for d in cloned if d['_id'] not in removed_ids and body(d).get('m_PrefabInstance',{}).get('fileID') not in removed_ids]
        deleted={d['_id']for d in aliases}|{instance_id}
        result=[d for d in result if d['_id']not in deleted]+cloned
    raise AssertionError('Recursive or unexpectedly deep project-owned prefab graph')
