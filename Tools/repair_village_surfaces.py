"""Explicit repair of checked-in surface bindings; never an import/Play hook.
Keeps the supplied terrain material and original forest shading. Styled copies
use their final opacity-aware shader from the start, with no runtime substitution.
"""
import copy
from unity_yaml import *
from author_camera_fade import graph_docs


def values(rows): return {k: v for row in rows for k, v in row.items()}
def material(path): return next(d['Material'] for d in geo.documents(ROOT / path) if 'Material' in d)
def save_material(path, value):
    value['m_Name'] = path.stem
    write(path, [(21, 2100000, 'Material', value, False)])
def set_floats(value, **changes):
    fs = values(value['m_SavedProperties'].get('m_Floats', []))
    fs.update(changes)
    value['m_SavedProperties']['m_Floats'] = [{k: v} for k, v in fs.items()]


def source_textures(source):
    textures = values(source['m_SavedProperties'].get('m_TexEnvs', []))
    shader = geo.GUIDS.get(source['m_Shader'].get('guid'))
    if shader and shader.suffix == '.shadergraph':
        for prop in graph_docs(shader):
            if not prop.get('m_Type', '').endswith('Texture2DShaderProperty'): continue
            key = prop.get('m_OverrideReferenceName') or prop.get('m_DefaultReferenceName')
            if key not in textures: continue
            display = prop.get('m_Name', '').lower().replace(' ', '')
            if display in {'basecolor', 'albedo', 'diffuse', 'base', 'texturemap'}: textures['_BaseMap'] = textures[key]
            if display in {'normal', 'normalmap'}: textures['_BumpMap'] = textures[key]
    for key in ['_BaseMap', '_BaseTexture', '_MainTex']:
        if textures.get(key, {}).get('m_Texture', {}).get('fileID'):
            textures['_BaseMap'] = textures[key]
            break
    return textures


palette_path = BASE / 'Data/ToonPalette.asset'
palette = geo.documents(ROOT / palette_path)[0]['MonoBehaviour']
for pair in palette['materials']:
    source_path = geo.GUIDS[pair['source']['guid']]
    target = geo.GUIDS[pair['styled']['guid']].relative_to(ROOT)
    # Re-running this explicit repair also preserves the already-authored forest copies.
    if target.parent.name != 'Toon': continue
    source = material(source_path)
    styled = material(target)
    styled['m_Shader'] = assetref(BASE / 'Shaders/CameraFadeSurface.shader', 4800000)
    set_floats(styled, _CameraOpacity=1)
    textures = values(styled['m_SavedProperties']['m_TexEnvs'])
    supplied = source_textures(source)
    for key in ['_BaseMap', '_BumpMap']:
        if supplied.get(key, {}).get('m_Texture', {}).get('fileID'):
            textures[key] = supplied[key]
    styled['m_SavedProperties']['m_TexEnvs'] = [{k: v} for k, v in textures.items()]
    if source_path.stem == 'M_PotatoLeaf_01':
        set_floats(styled, _AlphaClip=1, _Cutoff=.5, _Cull=0)
        styled['m_ValidKeywords'] = sorted(set(styled['m_ValidKeywords']) | {'_ALPHATEST_ON'})
    if textures.get('_BumpMap', {}).get('m_Texture', {}).get('fileID'):
        styled['m_ValidKeywords'] = sorted(set(styled['m_ValidKeywords']) | {'_NORMALMAP'})
    save_material(target, styled)

# Find the supplied forest materials in the authored forest prototypes, including LODs.
forest = set()
for prefab in (ROOT / BASE / 'Prefabs/World').glob('Forest*.prefab'):
    for d in geo.documents(prefab):
        if 'PrefabInstance' not in d: continue
        source = geo.GUIDS[d['PrefabInstance']['m_SourcePrefab']['guid']]
        for doc in geo.documents(source):
            for pointer in doc.get('MeshRenderer', {}).get('m_Materials', []):
                if pointer.get('guid') in geo.GUIDS: forest.add(geo.GUIDS[pointer['guid']])
existing = {p['source']['guid'] for p in palette['materials']}
for source_path in sorted(forest):
    source = material(source_path)
    shader = geo.GUIDS[source['m_Shader']['guid']]
    name = shader.stem.removeprefix('SyntyStudios_')
    assert name in ['Trees', 'Moss', 'LOD'], shader
    target = BASE / 'Materials/Forest' / (source_path.stem + '.mat')
    styled = copy.deepcopy(source)
    styled['m_Shader'] = assetref(BASE / 'Shaders' / ('CameraFade' + name + '.shadergraph'), -6465566751694194690)
    set_floats(styled, _CameraOpacity=1, _Surface=0, _AlphaClip=1, _Cutoff=.5, _SrcBlend=1, _DstBlend=0, _ZWrite=1)
    styled.update(m_CustomRenderQueue=-1, stringTagMap={}, disabledShaderPasses=[])
    save_material(target, styled)
    pointer = assetref(source_path.relative_to(ROOT), 2100000)
    if pointer['guid'] not in existing:
        palette['materials'].append(dict(source=pointer, styled=assetref(target, 2100000)))
write(palette_path, [(114, 11400000, 'MonoBehaviour', palette, False)])

# Keep the provided FlatKit terrain asset; remove Valley-demo location-dependent tinting.
terrain_path = Path('Assets/FlatKit/Demos/[Demo] Valley/Valley-Materials/FlatTerrain-TerrainShader.mat')
terrain = material(terrain_path)
set_floats(terrain, _GradientEnabled=0, _LightContribution=1)
terrain['m_ValidKeywords'] = [k for k in terrain['m_ValidKeywords'] if k != 'DR_GRADIENT_ON']
terrain['m_InvalidKeywords'] = [k for k in terrain.get('m_InvalidKeywords', []) if k != 'DR_GRADIENT_ON']
colors = values(terrain['m_SavedProperties']['m_Colors'])
colors.update(_BaseColor=color(1, 1, 1, 1), _Color=color(1, 1, 1, 1), _ColorDim=color(.72, .77, .8, 1), _EmissionColor=color(0, 0, 0, 1))
terrain['m_SavedProperties']['m_Colors'] = [{k: v} for k, v in colors.items()]
save_material(terrain_path, terrain)
# Match the supplied FlatKit demo's non-instanced terrain path.
p = ROOT / BASE / 'Prefabs/World/ForestTerrain.prefab'
p.write_text(p.read_text().replace('  m_DrawInstanced: 1', '  m_DrawInstanced: 0'))
print(f'Restored surface textures and stable shaders; retained {len(forest)} supplied forest materials and the FlatKit terrain asset.')
