"""Create stable metadata only for new Workerborn-owned assets. Never rewrites imports."""
from pathlib import Path
from unity_yaml import ROOT, BASE, guid

def ensure():
    count=0
    for path in [ROOT/BASE,*sorted((ROOT/BASE).rglob('*'))]:
        if path.suffix=='.meta':continue
        meta=Path(str(path)+'.meta')
        if meta.exists():continue
        header='fileFormatVersion: 2\nguid: '+guid(path.relative_to(ROOT))+'\n'
        if path.is_dir():body='folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n'
        elif path.suffix=='.cs':body='MonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n'
        elif path.suffix in ('.asset','.mat','.rendertexture'):body='NativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: '+str({'.mat':2100000,'.rendertexture':8400000}.get(path.suffix,11400000))+'\n'
        elif path.suffix=='.shader':body='ShaderImporter:\n  externalObjects: {}\n  defaultTextures: []\n  nonModifiableTextures: []\n  preprocessorOverride: 0\n'
        elif path.suffix=='.prefab':body='PrefabImporter:\n  externalObjects: {}\n'
        else:body='DefaultImporter:\n  externalObjects: {}\n'
        meta.write_text(header+body+'  userData:\n  assetBundleName:\n  assetBundleVariant:\n');count+=1
    print('Created metadata for',count,'new project-owned assets.')
if __name__=='__main__':ensure()
