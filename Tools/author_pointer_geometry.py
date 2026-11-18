"""Bake exact static picking colliders into the editable world prefabs, offline.
Copies source transform chains and references the original rendered mesh assets.
Navigation blockers remain independent. Never called during import or Play.
"""
import copy
from unity_yaml import *

LAYER = 11

def body(doc): return doc[next(k for k in doc if not k.startswith('_'))]
def serialize(doc):
    v = body(doc)
    return doc['_class'], doc['_id'], next(k for k in doc if not k.startswith('_')), v, bool(v.get('m_CorrespondingSourceObject', {}).get('guid'))


def bake(path):
    docs = geo.documents(ROOT / path)
    marker = next((d['MonoBehaviour'] for d in docs if d.get('MonoBehaviour', {}).get('m_Script', {}).get('guid') == guid(BASE / 'Runtime/World/VillageMapProp.cs')), None)
    if marker is None: return 0
    if any(d.get('GameObject', {}).get('m_Name') == 'Pointer geometry' for d in docs): return 0
    by = {d['_id']: d for d in docs}
    root_go = marker['m_GameObject']['fileID']
    root_t = next(d for d in docs if d.get('Transform', {}).get('m_GameObject', {}).get('fileID') == root_go)
    root = dict(go=root_go, t=root_t['_id'], g=by[root_go]['GameObject'], tr=root_t['Transform'])
    builder = Scene()
    builder.n = max(d['_id'] for d in docs) + 100
    proxy = builder.node('Pointer geometry', root, layer=LAYER)
    count, imported = 0, False

    def append_geometry(source_path, parent, modifications=()):
        nonlocal count, imported
        if source_path.suffix.lower() == '.fbx':
            # This single supplied forge model has no native prefab mesh references.
            # Its ModelImporter authors exact mesh colliders, visible in the imported prefab.
            imported = True
            meta = Path(str(source_path) + '.meta')
            meta.write_text(meta.read_text().replace('    addColliders: 0', '    addColliders: 1'))
            return lambda _: parent
        local_docs = copy.deepcopy(geo.documents(source_path))
        local = {d['_id']: d for d in local_docs}
        for mod in modifications:
            if mod['target'].get('guid') != guid(source_path.relative_to(ROOT)): continue
            identifier = mod['target']['fileID']
            if identifier not in local: continue
            parts = mod['propertyPath'].split('.')
            if 'Array' in parts: continue
            v = body(local[identifier])
            for part in parts[:-1]: v = v.setdefault(part, {})
            old = v.get(parts[-1]); raw = mod.get('value')
            v[parts[-1]] = mod.get('objectReference') if mod.get('objectReference', {}).get('fileID') else float(raw) if isinstance(old, float) or parts[0] in {'m_LocalPosition', 'm_LocalRotation', 'm_LocalScale'} else int(raw) if isinstance(old, int) else raw
        transforms = {d['_id']: d['Transform'] for d in local_docs if 'Transform' in d and 'm_GameObject' in d['Transform']}
        by_go = {v['m_GameObject']['fileID']: k for k, v in transforms.items()}
        built = {}
        instances = {}
        def ensure_node(tid):
            if tid in built: return built[tid]
            if tid not in transforms:
                alias = body(local[tid])
                resolve = materialize_instance(alias['m_PrefabInstance']['fileID'])
                built[tid] = resolve(alias['m_CorrespondingSourceObject']['fileID'])
                return built[tid]
            t = transforms[tid]
            go = local[t['m_GameObject']['fileID']]['GameObject']
            father = t['m_Father']['fileID']
            target_parent = ensure_node(father) if father in local and 'Transform' in local[father] else parent
            args = lambda field, axes, fallback: tuple(t.get(field, {}).get(a, fallback[i]) for i, a in enumerate(axes))
            n = builder.node(go['m_Name'], target_parent,
                pos=args('m_LocalPosition', 'xyz', (0, 0, 0)), rot=args('m_LocalRotation', 'xyzw', (0, 0, 0, 1)),
                scale=args('m_LocalScale', 'xyz', (1, 1, 1)), active=go.get('m_IsActive', 1), layer=LAYER)
            built[tid] = n
            return n
        def materialize_instance(identifier):
            if identifier not in instances:
                instance = local[identifier]['PrefabInstance']
                source = geo.GUIDS[instance['m_SourcePrefab']['guid']]
                m = instance['m_Modification']
                target_parent = ensure_node(m['m_TransformParent']['fileID'])
                instances[identifier] = append_geometry(source, target_parent, m.get('m_Modifications', []))
            return instances[identifier]

        # Only the highest-detail geometry participates in picking, never crossed billboard LODs.
        excluded = set()
        for d in local_docs:
            lods = d.get('LODGroup', {}).get('m_LODs', [])
            keep = {r['renderer']['fileID'] for r in lods[0].get('renderers', [])} if lods else set()
            for lod in lods[1:]:
                excluded.update(r['renderer']['fileID'] for r in lod.get('renderers', []) if r['renderer']['fileID'] not in keep)
        filters = {d['MeshFilter']['m_GameObject']['fileID']: d['MeshFilter']['m_Mesh'] for d in local_docs if 'MeshFilter' in d}
        for d in local_docs:
            renderer = d.get('MeshRenderer')
            if renderer is None or not renderer.get('m_Enabled', 1) or d['_id'] in excluded: continue
            mesh = filters.get(renderer['m_GameObject']['fileID'])
            if not mesh or not mesh.get('fileID'): continue
            n = ensure_node(by_go[renderer['m_GameObject']['fileID']])
            builder.component(n, 64, 'MeshCollider', dict(m_Material=ref(),
                m_IncludeLayers=Flow(serializedVersion=2, m_Bits=0), m_ExcludeLayers=Flow(serializedVersion=2, m_Bits=0),
                m_LayerOverridePriority=0, m_IsTrigger=0, m_ProvidesContacts=0, m_Enabled=1, serializedVersion=5,
                m_Convex=0, m_CookingOptions=30, m_Mesh=mesh))
            count += 1
        for d in local_docs:
            instance = d.get('PrefabInstance')
            if instance is None: continue
            materialize_instance(d['_id'])
        return ensure_node

    # Begin with the wrapper's authored visual placement, including its normalized model scale.
    visual = marker['visual']['fileID']
    t = by[visual]['Transform']
    visual_proxy = builder.node('Visual', proxy,
        pos=tuple(t['m_LocalPosition'][a] for a in 'xyz'),
        rot=tuple(t['m_LocalRotation'][a] for a in 'xyzw'), scale=tuple(t['m_LocalScale'][a] for a in 'xyz'), layer=LAYER)
    for d in docs:
        instance = d.get('PrefabInstance')
        if instance is None or instance['m_Modification']['m_TransformParent']['fileID'] != visual: continue
        m = instance['m_Modification']
        append_geometry(geo.GUIDS[instance['m_SourcePrefab']['guid']], visual_proxy, m.get('m_Modifications', []))
    marker['useImportedMeshColliders'] = int(imported)
    write(path, [serialize(d) for d in docs] + builder.docs)
    return count


if __name__ == '__main__':
    total = sum(bake(p.relative_to(ROOT)) for p in sorted((ROOT / BASE / 'Prefabs/World').glob('*.prefab')))
    print(f'Authored {total} exact mesh colliders in the existing world prefabs.')
