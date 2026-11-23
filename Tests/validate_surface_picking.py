"""Read-only checks against fully expanded native visual geometry and material bindings."""
import sys
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'Tools'))
from resolve_owned_prefabs import expand
from unity_asset_geometry import documents, GUIDS, trs, multiply

count = 0
for path in sorted((ROOT / 'Assets/Workerborn/Prefabs/World').glob('*.prefab')):
    ds = expand(path, include_vendor_geometry=True)
    transforms = {d['_id']: d['Transform'] for d in ds if 'Transform' in d and 'm_GameObject' in d['Transform']}
    by_go = {v['m_GameObject']['fileID']: k for k, v in transforms.items()}
    gos = {d['_id']: d['GameObject'] for d in ds if 'GameObject' in d}
    proxy = next((k for k, t in transforms.items() if gos[t['m_GameObject']['fileID']]['m_Name'] == 'Pointer geometry'), None)
    if proxy is None:
        assert path.stem in {'ForestTerrain', 'RebirthZone'} or (
            path.stem == 'VillageWeather' and all(go['m_Layer'] == 12 for go in gos.values())), path
        continue
    matrices = {}
    def matrix(identifier):
        if identifier not in matrices:
            t = transforms[identifier]
            value = trs(t['m_LocalPosition'], t['m_LocalRotation'], t['m_LocalScale'])
            parent = t['m_Father']['fileID']
            matrices[identifier] = multiply(matrix(parent), value) if parent in transforms else value
        return matrices[identifier]
    def under_proxy(identifier):
        while identifier in transforms:
            if identifier == proxy: return True
            identifier = transforms[identifier]['m_Father']['fileID']
        return False
    filters = [(d['MeshFilter']['m_Mesh'], matrix(by_go[d['MeshFilter']['m_GameObject']['fileID']])) for d in ds if 'MeshFilter' in d]
    local_count = 0
    for d in ds:
        c = d.get('MeshCollider')
        if c is None or not under_proxy(by_go[c['m_GameObject']['fileID']]): continue
        m = matrix(by_go[c['m_GameObject']['fileID']])
        assert any(c['m_Mesh'] == mesh and max(abs(m[i][j] - other[i][j]) for i in range(4) for j in range(4)) < .0001 for mesh, other in filters), (path.stem, gos[c['m_GameObject']['fileID']]['m_Name'])
        assert not c['m_Convex'] and gos[c['m_GameObject']['fileID']]['m_Layer'] == 11
        local_count += 1
    if path.stem == 'Forge':
        assert any(d.get('MonoBehaviour', {}).get('useImportedMeshColliders') for d in ds)
    else: assert local_count > 0, path
    count += local_count

base = ROOT / 'Assets/Workerborn'
palette = documents(base / 'Data/ToonPalette.asset')[0]['MonoBehaviour']
forest_count = 0
for pair in palette['materials']:
    path = GUIDS[pair['styled']['guid']]
    material = next(d['Material'] for d in documents(path) if 'Material' in d)
    shader = GUIDS[material['m_Shader']['guid']]
    assert shader.parent == base / 'Shaders', path
    floats = {k: v for row in material['m_SavedProperties']['m_Floats'] for k, v in row.items()}
    assert floats['_CameraOpacity'] == 1 and floats.get('_Surface', 0) == 0
    if path.parent.name == 'Forest':
        source = next(d['Material'] for d in documents(GUIDS[pair['source']['guid']]) if 'Material' in d)
        assert source['m_SavedProperties']['m_TexEnvs'] == material['m_SavedProperties']['m_TexEnvs']
        forest_count += 1
    if path.stem.startswith(('M_PotatoLeaf', 'M_Poato')):
        textures = {k: v for row in material['m_SavedProperties']['m_TexEnvs'] for k, v in row.items()}
        assert GUIDS[textures['_BaseMap']['m_Texture']['guid']].name == 'T_Potato_01_AlbedoTransparency.png'
        assert GUIDS[textures['_BumpMap']['m_Texture']['guid']].name == 'T_Potato_01_Normal.png'
        if path.stem.startswith('M_PotatoLeaf'): assert floats['_AlphaClip'] == 1 and floats['_Cull'] == 0
print(f'Validated {count} exact picking meshes against expanded source transforms, {len(palette["materials"])} stable material bindings and {forest_count} preserved forest texture sets.')
