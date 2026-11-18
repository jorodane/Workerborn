"""Explicitly author stable opaque fade shaders from supplied Synty graphs.
Never runs during Unity import or Play. Original shading, texture slots and wind
remain intact; only the final alpha is gated by per-camera screen coverage.
"""
import copy
import json
import re
import uuid
from pathlib import Path
from unity_yaml import ROOT, BASE, guid


def graph_docs(path):
    source = (ROOT / path).read_text()
    decoder = json.JSONDecoder()
    docs, index = [], 0
    while index < len(source):
        while index < len(source) and source[index].isspace(): index += 1
        if index < len(source):
            value, index = decoder.raw_decode(source, index)
            docs.append(value)
    return docs


def clone_graph(source, target):
    docs = graph_docs(source)
    templates = graph_docs(Path('Assets/Synty/PolygonNature/Shaders/SyntyStudios_Trees.shadergraph'))
    by = {d.get('m_ObjectId'): d for d in templates + docs}
    root = docs[0]
    def new_id(tag): return uuid.uuid5(uuid.NAMESPACE_URL, 'workerborn-camera-fade/' + str(target) + '/' + tag).hex
    def append(value, tag):
        value = copy.deepcopy(value)
        value['m_ObjectId'] = new_id(tag)
        docs.append(value)
        return value
    def edge(output, output_slot, input, input_slot):
        def endpoint(node, slot): return {'m_Node': {'m_Id': node['m_ObjectId']}, 'm_SlotId': slot}
        root['m_Edges'].append({'m_OutputSlot': endpoint(output, output_slot), 'm_InputSlot': endpoint(input, input_slot)})
    def node(template, tag, slots):
        n = append(template, tag)
        n.update(m_Group={'m_Id': ''}, m_Name=tag, m_Slots=[{'m_Id': s['m_ObjectId']} for s in slots])
        root['m_Nodes'].append({'m_Id': n['m_ObjectId']})
        return n
    def slot(kind, tag, number, name, output=False, value=0):
        template = next(d for d in templates if d.get('m_Type', '').endswith('.' + kind + 'MaterialSlot'))
        s = append(template, tag)
        s.update(m_Id=number, m_DisplayName=name, m_ShaderOutputName=name, m_SlotType=int(output), m_Value=value, m_DefaultValue=value)
        return s
    base_node = next(d for d in templates if d.get('m_Type') == 'UnityEditor.ShaderGraph.PropertyNode')
    scalar = next(d for d in templates if d.get('m_Type') == 'UnityEditor.ShaderGraph.Internal.Vector1ShaderProperty')
    opacity = append(scalar, 'opacity property')
    opacity.update(m_Name='Camera opacity', m_DefaultReferenceName='_CameraOpacity', m_OverrideReferenceName='_CameraOpacity', m_Value=1.0, m_Hidden=True, m_GeneratePropertyBlock=True)
    opacity['m_Guid'] = {'m_GuidSerialized': str(uuid.UUID(new_id('property-guid')))}
    root['m_Properties'].append({'m_Id': opacity['m_ObjectId']})
    pn = node(base_node, 'Camera opacity', [slot('Vector1', 'opacity out', 0, 'Out', True, 1.0)])
    pn['m_Property'] = {'m_Id': opacity['m_ObjectId']}
    screen = node(base_node, 'Camera screen position', [slot('Vector4', 'screen out', 0, 'Out', True, dict(x=0., y=0., z=0., w=0.))])
    screen.pop('m_Property', None)
    screen.update(m_Type='UnityEditor.ShaderGraph.ScreenPositionNode', m_ScreenSpaceType=0)
    cf = node(base_node, 'Camera coverage', [
        slot('Vector1', 'coverage opacity', 0, 'Opacity', value=1.0),
        slot('Vector4', 'coverage screen', 1, 'Screen', value=dict(x=0., y=0., z=0., w=0.)),
        slot('Vector1', 'coverage out', 2, 'Coverage', True, 1.0)])
    cf.pop('m_Property', None)
    cf.update(m_Type='UnityEditor.ShaderGraph.CustomFunctionNode', m_SGVersion=1, m_Precision=1,
              m_SourceType=1, m_FunctionName='WorkerbornCameraCoverage', m_FunctionSource='', m_FunctionSourceUsePragmas=True,
              m_FunctionBody='float2 pixel = floor(Screen.xy * _ScreenParams.xy);\nfloat noise = frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));\nCoverage = step(max(noise, 0.00001), saturate(Opacity));')
    edge(pn, 0, cf, 0)
    edge(screen, 0, cf, 1)
    alpha = next((d for d in docs if d.get('m_Name') == 'SurfaceDescription.Alpha'), None)
    if alpha is None:
        alpha_template = next(d for d in templates if d.get('m_Name') == 'SurfaceDescription.Alpha')
        alpha = append(alpha_template, 'alpha block')
        alpha['m_Slots'] = [{'m_Id': slot('Vector1', 'alpha in', 0, 'Alpha', value=1.0)['m_ObjectId']}]
        root['m_Nodes'].append({'m_Id': alpha['m_ObjectId']})
        root['m_FragmentContext']['m_Blocks'].append({'m_Id': alpha['m_ObjectId']})
    threshold = next((d for d in docs if d.get('m_Name') == 'SurfaceDescription.AlphaClipThreshold'), None)
    if threshold is None:
        template = next(d for d in templates if d.get('m_Name') == 'SurfaceDescription.AlphaClipThreshold')
        threshold = append(template, 'threshold block')
        threshold['m_Slots'] = [{'m_Id': slot('Vector1', 'threshold in', 0, 'AlphaClipThreshold', value=.5)['m_ObjectId']}]
        root['m_Nodes'].append({'m_Id': threshold['m_ObjectId']})
        root['m_FragmentContext']['m_Blocks'].append({'m_Id': threshold['m_ObjectId']})
    # One exposed threshold drives both visible cutouts and the pointer depth/ID mask.
    for reference, label, default, hidden in [('_Cutoff', 'Alpha cutoff', .5, False), ('_AlphaClip', 'Alpha clipping', 1., True)]:
        prop = append(scalar, reference + ' property')
        prop.update(m_Name=label, m_DefaultReferenceName=reference, m_OverrideReferenceName=reference,
                    m_Value=default, m_Hidden=hidden, m_GeneratePropertyBlock=True)
        prop['m_Guid'] = {'m_GuidSerialized': str(uuid.UUID(new_id(reference + '-guid')))}
        root['m_Properties'].append({'m_Id': prop['m_ObjectId']})
        if reference == '_Cutoff':
            cutoff_node = node(base_node, 'Alpha cutoff', [slot('Vector1', 'cutoff out', 0, 'Out', True, default)])
            cutoff_node['m_Property'] = {'m_Id': prop['m_ObjectId']}
            edge(cutoff_node, 0, threshold, 0)
    template = next(d for d in templates if d.get('m_Type') == 'UnityEditor.ShaderGraph.MultiplyNode')
    slots = []
    for old in template['m_Slots']:
        value = append(by[old['m_Id']], 'alpha fade/' + old['m_Id'])
        value['m_Value'] = dict(x=1., y=1., z=1., w=1.)
        slots.append(value)
    multiply = node(template, 'Camera alpha coverage', slots)
    original = next((e for e in root['m_Edges'] if e['m_InputSlot']['m_Node']['m_Id'] == alpha['m_ObjectId']), None)
    if original:
        root['m_Edges'].remove(original)
        root['m_Edges'].append({'m_OutputSlot': original['m_OutputSlot'], 'm_InputSlot': {'m_Node': {'m_Id': multiply['m_ObjectId']}, 'm_SlotId': 0}})
    edge(cf, 2, multiply, 1)
    edge(multiply, 2, alpha, 0)
    root['m_Path'] = 'Workerborn/Camera Fade'
    for d in docs:
        if d.get('m_Type', '').endswith('UniversalTarget'):
            # Fixed opaque state avoids material keyword/state changes during a fade.
            d.update(m_AllowMaterialOverride=False, m_SurfaceType=0, m_AlphaMode=0, m_AlphaClip=True)
    path = ROOT / target
    path.write_text('\n\n'.join(json.dumps(d, indent=4) for d in docs) + '\n')
    meta = (ROOT / (str(source) + '.meta')).read_text()
    meta = re.sub(r'^guid: .*', 'guid: ' + guid(target), meta, flags=re.M)
    Path(str(path) + '.meta').write_text('\n'.join(line.rstrip() for line in meta.splitlines()) + '\n')


if __name__ == '__main__':
    for name in ['Trees', 'Moss', 'LOD']:
        clone_graph(Path('Assets/Synty/PolygonNature/Shaders') / ('SyntyStudios_' + name + '.shadergraph'), BASE / 'Shaders' / ('CameraFade' + name + '.shadergraph'))
    print('Authored stable opaque camera coverage for three supplied forest shaders.')
