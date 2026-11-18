"""Explicit project-owned material conversion using the supplied FlatKit shader."""
import copy
import re
from unity_yaml import *
flat_shader=Path("Assets/FlatKit/Shaders/StylizedSurface/StylizedSurface.shader")

def style_material(path):
    source=next(d['Material'] for d in geo.documents(path) if 'Material'in d)
    if source.get('m_CustomRenderQueue',-1)>=3000:return None
    value=copy.deepcopy(source);saved=value['m_SavedProperties']
    floats={k:v for row in saved.get('m_Floats',[]) for k,v in row.items()}
    colors={k:v for row in saved.get('m_Colors',[]) for k,v in row.items()}
    textures={k:v for row in saved.get('m_TexEnvs',[]) for k,v in row.items()}
    if floats.get('_Surface',0)>0:return None
    if ('_BaseMap' not in textures or textures['_BaseMap']['m_Texture']['fileID']==0) and '_MainTex'in textures:textures['_BaseMap']=textures['_MainTex']
    if '_BaseColor'not in colors:colors['_BaseColor']=colors.get('_Color',Flow(r=1,g=1,b=1,a=1))
    floats.update(_CelPrimaryMode=1,_SelfShadingSize=.58,_ShadowEdgeSize=.035,_LightContribution=1,_UnityShadowMode=1,_UnityShadowPower=.4,_UnityShadowSharpness=1,_OutlineEnabled=1,_OutlineWidth=.45,_OutlineScale=1,_OutlineDepthOffset=0,_OutlineSpace=0,_CameraDistanceImpact=0,_TextureImpact=1,_TextureBlendingMode=0,_CelExtraEnabled=0,_GradientEnabled=0,_SpecularEnabled=0,_RimEnabled=0,_VertexColorsEnabled=0,_SrcBlend=1,_DstBlend=0,_ZWrite=1)
    colors.update(_ColorDim=Flow(r=.66,g=.7,b=.76,a=1),_OutlineColor=Flow(r=.045,g=.04,b=.04,a=1))
    keywords=['_CELPRIMARYMODE_SINGLE','_UNITYSHADOWMODE_MULTIPLY','_TEXTUREBLENDINGMODE_MULTIPLY','_OUTLINESPACE_SCREEN']
    if not re.match(r'^(eye_|eyebrow_|mouth_|cheek_)',path.stem,re.I):keywords.append('DR_OUTLINE_ON')
    else:floats['_OutlineEnabled']=0
    if floats.get('_AlphaClip',0)>.5:keywords.append('_ALPHATEST_ON')
    if textures.get('_BumpMap',{}).get('m_Texture',{}).get('fileID',0):keywords.append('_NORMALMAP')
    saved['m_TexEnvs']=[{k:v} for k,v in textures.items() if k in {'_BaseMap','_BumpMap','_DetailMap','_EmissionMap','_CelStepTexture','_CelCurveTexture'}];saved['m_Floats']=[{k:v} for k,v in floats.items()];saved['m_Colors']=[{k:v} for k,v in colors.items()]
    value.update(m_Shader=assetref(flat_shader,4800000),m_Name='WB_'+path.stem,m_ValidKeywords=keywords,m_InvalidKeywords=[],m_ShaderKeywords=' '.join(keywords),disabledShaderPasses=[],serializedVersion=8)
    target=BASE/'Materials/Toon'/(path.stem+'_'+guid(path.relative_to(ROOT))[:8]+'.mat')
    write(target,[(21,2100000,'Material',value,False)])
    return dict(source=assetref(path.relative_to(ROOT),2100000),styled=assetref(target,2100000))
