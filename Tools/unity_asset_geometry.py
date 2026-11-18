"""Offline bounds used once while authoring the checked-in sample Scene.
Unity remains the editor; this module is never run by a game or import hook.
"""
from pathlib import Path
import array
import math
import re
import struct
import zlib
import yaml

ROOT = Path(__file__).resolve().parents[1]
GUIDS = {}
for meta in (ROOT / 'Assets').rglob('*.meta'):
    match = re.search(r'^guid: ([a-f0-9]{32})$', meta.read_text(), re.M)
    if match: GUIDS[match[1]] = Path(str(meta)[:-5])
CACHE = {}


def documents(path):
    raw = Path(path).read_bytes()
    if not raw.startswith(b'%YAML'):
        # Supplied TerrainData uses Unity's native binary serialization.
        # UnityPy is an offline validation/authoring dependency, never a game dependency.
        import UnityPy
        env = UnityPy.load(str(path))
        def convert(value, external):
            if isinstance(value, dict):
                if set(value)=={'m_FileID','m_PathID'}:
                    pointer={'fileID':value['m_PathID']}
                    if value['m_FileID'] and value['m_PathID']:
                        item=external[value['m_FileID']-1]
                        pointer.update(guid=''.join(f'{b&15:x}{b>>4:x}' for b in item.guid),type=2)
                    return pointer
                return {k:convert(v,external)for k,v in value.items()}
            if isinstance(value,list):return [convert(v,external)for v in value]
            return value
        return [{'_class':o.type.value,'_id':o.path_id,o.type.name:convert(o.read_typetree(),o.assets_file.externals)}for o in env.objects]
    text = raw.decode()
    text = re.sub(r'^%.*\n', '', text, flags=re.M)
    text = re.sub(r'^--- !u!(\d+) &(-?\d+)(?: stripped)?\s*$', r'---\n_class: \1\n_id: \2', text, flags=re.M)
    return [d for d in yaml.safe_load_all(text) if isinstance(d, dict)]


def identity():
    return [[float(i == j) for j in range(4)] for i in range(4)]


def multiply(a, b):
    return [[sum(a[i][k] * b[k][j] for k in range(4)) for j in range(4)] for i in range(4)]


def transform(point, matrix):
    return [sum(matrix[i][k] * ([*point, 1][k]) for k in range(4)) for i in range(3)]


def trs(position, quaternion, scale):
    x, y, z, w = [quaternion.get(c, 0 if c != 'w' else 1) for c in 'xyzw']
    m = [[1-2*y*y-2*z*z, 2*x*y-2*z*w, 2*x*z+2*y*w, 0],
         [2*x*y+2*z*w, 1-2*x*x-2*z*z, 2*y*z-2*x*w, 0],
         [2*x*z-2*y*w, 2*y*z+2*x*w, 1-2*x*x-2*y*y, 0], [0, 0, 0, 1]]
    for i in range(3):
        for j, c in enumerate('xyz'): m[i][j] *= scale.get(c, 1)
        m[i][3] = position.get('xyz'[i], 0)
    return m


def corners(bounds):
    low, high = bounds
    return [(x, y, z) for x in (low[0], high[0]) for y in (low[1], high[1]) for z in (low[2], high[2])]


def union(points):
    return ([min(p[i] for p in points) for i in range(3)], [max(p[i] for p in points) for i in range(3)])


def fbx_bounds(path):
    data = Path(path).read_bytes()
    if not data.startswith(b'Kaydara FBX Binary'):
        text = data.decode(errors='replace')
        values = [float(x) for x in re.findall(r'[-+]?\d+(?:\.\d*)?(?:[eE][-+]?\d+)?', re.search(r'Vertices:.*?a:([^}]+)', text, re.S)[1])]
        return union(zip(values[0::3], values[1::3], values[2::3]))
    version = struct.unpack_from('<I', data, 23)[0]
    wide = version >= 7500
    fmt, size = ('<QQQB', 25) if wide else ('<IIIB', 13)
    points = []
    units = 1.0
    def node(offset):
        nonlocal units
        end, count, length, name_len = struct.unpack_from(fmt, data, offset)
        if end == 0: return offset + size
        name = data[offset+size:offset+size+name_len].decode(errors='replace')
        cursor = offset + size + name_len
        props = []
        for _ in range(count):
            kind = chr(data[cursor]); cursor += 1
            scalars = {'Y':('<h',2), 'C':('<?',1), 'I':('<i',4), 'F':('<f',4), 'D':('<d',8), 'L':('<q',8)}
            if kind in scalars:
                encoding, width = scalars[kind]; props.append(struct.unpack_from(encoding, data, cursor)[0]); cursor += width
            elif kind in 'SR':
                width = struct.unpack_from('<I', data, cursor)[0]; cursor += 4
                props.append(data[cursor:cursor+width].decode(errors='replace') if kind == 'S' else None); cursor += width
            elif kind in 'fdlibc':
                count_items, compressed, width = struct.unpack_from('<III', data, cursor); cursor += 12
                if name == 'Vertices' and kind in 'fd':
                    raw = data[cursor:cursor+width]
                    if compressed: raw = zlib.decompress(raw)
                    values = array.array(kind); values.frombytes(raw)
                    points.extend(zip(values[0::3], values[1::3], values[2::3]))
                cursor += width; props.append(None)
            else: raise ValueError((path, kind, name))
        if name == 'P' and props and props[0] == 'UnitScaleFactor': units = float(props[-1])
        while cursor + size <= end:
            next_cursor = node(cursor)
            if next_cursor <= cursor: break
            cursor = next_cursor
        return end
    cursor = 27
    while cursor + size < len(data):
        if struct.unpack_from('<Q' if wide else '<I', data, cursor)[0] == 0: break
        cursor = node(cursor)
    factor = units * 0.01
    meta = Path(str(path) + '.meta').read_text()
    match = re.search(r'^    globalScale: ([\d.]+)', meta, re.M)
    if match: factor *= float(match[1])
    return union([(-x*factor, y*factor, z*factor) for x,y,z in points])


def bounds(path, ignore_weapons=False):
    path = Path(path); key = (str(path), ignore_weapons)
    if key in CACHE: return CACHE[key]
    if path.suffix.lower() == '.fbx':
        value = fbx_bounds(path); CACHE[key] = value; return value
    docs = documents(path)
    names = {d['_id']: d['GameObject'].get('m_Name','') for d in docs if 'GameObject' in d}
    transforms = {d['_id']: d['Transform'] for d in docs if 'Transform' in d}
    matrices = {}
    def matrix(identifier):
        if identifier not in transforms: return identity()
        if identifier in matrices: return matrices[identifier]
        t = transforms[identifier]
        m = trs(t.get('m_LocalPosition',{}),t.get('m_LocalRotation',{}),t.get('m_LocalScale',{}))
        father = t.get('m_Father',{}).get('fileID',0)
        matrices[identifier] = multiply(matrix(father),m) if father else m
        return matrices[identifier]
    by_go = {v['m_GameObject']['fileID']: k for k,v in transforms.items() if 'm_GameObject' in v}
    meshes = {d['MeshFilter']['m_GameObject']['fileID']: d['MeshFilter'].get('m_Mesh',{}) for d in docs if 'MeshFilter' in d}
    points = []
    for d in docs:
        if 'MeshRenderer' in d or 'SkinnedMeshRenderer' in d:
            if ignore_weapons and 'SkinnedMeshRenderer' not in d: continue
            renderer = d.get('MeshRenderer', d.get('SkinnedMeshRenderer'))
            if not renderer.get('m_Enabled',1): continue
            go = renderer['m_GameObject']['fileID']; name=names.get(go,'').lower()
            if ignore_weapons and any(word in name for word in ('sword','shield','bow','arrow','knife','shuriken')):continue
            if 'm_AABB' in renderer:
                box=renderer['m_AABB']; c=box['m_Center'];e=box['m_Extent']
                local=([c[a]-e[a] for a in 'xyz'],[c[a]+e[a] for a in 'xyz'])
            else:
                mesh=meshes.get(go,{})
                if mesh.get('guid') not in GUIDS: continue
                local=bounds(GUIDS[mesh['guid']])
            # Unity stores skinned bounds in the root bone's coordinates.
            frame = renderer.get('m_RootBone', {}).get('fileID') if 'SkinnedMeshRenderer' in d else by_go.get(go)
            points.extend(transform(p,matrix(frame)) for p in corners(local))
        if 'PrefabInstance' in d:
            if ignore_weapons: continue
            instance=d['PrefabInstance']; source=GUIDS.get(instance.get('m_SourcePrefab',{}).get('guid'))
            if source is None: continue
            position={};rotation={'w':1};scale={'x':1,'y':1,'z':1}
            root_transform=root_ids(source)[1]
            for mod in instance.get('m_Modification',{}).get('m_Modifications',[]):
                prop=mod.get('propertyPath','')
                if mod.get('target',{}).get('fileID') != root_transform:continue
                for prefix,target in [('m_LocalPosition.',position),('m_LocalRotation.',rotation),('m_LocalScale.',scale)]:
                    if prop.startswith(prefix):target[prop[-1]]=float(mod.get('value',0))
            parent=instance.get('m_Modification',{}).get('m_TransformParent',{}).get('fileID',0)
            m=multiply(matrix(parent),trs(position,rotation,scale))
            try:
                source_bounds = bounds(source,ignore_weapons)
            except ValueError as error:
                if 'No geometry in' in str(error): continue
                raise
            points.extend(transform(p,m) for p in corners(source_bounds))
    if not points: raise ValueError('No geometry in '+str(path))
    value=union(points);CACHE[key]=value;return value


def root_ids(path):
    path=Path(path)
    if path.suffix.lower()=='.fbx':return 919132149155446097,-8679921383154817045
    for d in documents(path):
        t=d.get('Transform',d.get('RectTransform'))
        if t and t.get('m_Father',{}).get('fileID')==0:return t['m_GameObject']['fileID'],d['_id']
    raise ValueError('Prefab has no explicit root: '+str(path))
