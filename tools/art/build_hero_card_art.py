"""Bake unique per-hero skill reliefs and incised names, keyed to canonical cards."""
import argparse,json,sys,math
from pathlib import Path
import bpy
p=argparse.ArgumentParser();p.add_argument('--root',type=Path,required=True);p.add_argument('--hero',required=True);p.add_argument('--resume',action='store_true')
a=p.parse_args(sys.argv[sys.argv.index('--')+1:]);root=a.root.resolve();sys.path.insert(0,str(root/'tools/art'))
from r2b_runtime_common import materials,studio,render,emission
from r2b02_stones import poly,glyph,subtract,bevel
from sculpt_card_icons import BUILDERS
bpy.ops.wm.open_mainfile(filepath=str(root/'art/production/samples/R2B02-Badges/R2B02_SemanticBadges.blend'))
bpy.context.preferences.filepaths.save_version=0
M=materials();M['energy']=emission('CardArt live pale electric sheath',(.13,.62,1),2.2);M['hot']=emission('CardArt electric hot core',(.72,.92,1),3.8)
cards=[c for c in json.loads((root/'content/canonical/cards.json').read_text(encoding='utf8'))['cards'] if c['hero_id']==a.hero]
out=root/'unity/Assets/Resources/UI3D/CardArt'/a.hero;out.mkdir(parents=True,exist_ok=True)
entries=[];groups={}
for color in ('gold','silver','purple','red','green','blue'):
    subset=sorted((c for c in cards if c['color_key']==color),key=lambda c:c['id'])
    for card in subset:
        lvl=card['level'];same=[c for c in subset if c['level']==lvl]
        key=color if color in ('gold','silver','purple') else color+('-base' if lvl==1 else '-'+('a' if same.index(card)==0 else 'b'))
        groups.setdefault(key,[]).append(card)
        token=card['id'].split('-')[1];entries.append({'CardId':card['id'],'IconKey':a.hero+'/'+key,'NameKey':a.hero+'/name-'+token})
scenes=[]
for key,group in groups.items():
    sc,c=studio(a.hero+' / '+key,512,3.15,48);scenes.append(sc)
    BUILDERS[a.hero](key,c,M)
    sc['cards']=','.join(card['id'] for card in group)
    # Subtle real glow comes from emission and a restrained compositor bloom.
    sc.use_nodes=True;n=sc.node_tree.nodes;n.clear();rl=n.new('CompositorNodeRLayers');gl=n.new('CompositorNodeGlare');gl.glare_type='FOG_GLOW';gl.threshold=1.2;gl.mix=-.65;co=n.new('CompositorNodeComposite');sc.node_tree.links.new(rl.outputs[0],gl.inputs[0]);sc.node_tree.links.new(gl.outputs[0],co.inputs[0])
    if not a.resume or not (out/(key+'.png')).exists():render(sc,out/(key+'.png'))
    print('HERO_ICON '+a.hero+' '+key,flush=True)
font=bpy.data.fonts.load('C:/Windows/Fonts/simsun.ttc')
for card in cards:
    sc,c=studio(a.hero+' / name / '+card['name'],512,3.25,32);scenes.append(sc)
    sc.render.resolution_y=138
    pts=[(-1.57,-.33),(-1.48,-.40),(1.48,-.40),(1.57,-.33),(1.57,.29),(1.46,.36),(-1.46,.36),(-1.57,.29)]
    poly('Incised name / dark fitted seat',pts,.05,.20,c,M['dark'],.025)
    body=poly('Incised name / dressed slate',[(x*.98,y*.95) for x,y in pts],.18,.36,c,M['stone'],.025)
    cut=glyph(card['name'],(0,0,.41),.49,2.86,c,font,depth=.12)
    floor=cut.copy();floor.data=cut.data.copy();c.objects.link(floor);floor.name='Actual incised name floor / '+card['name']
    zs=[v.co.z for v in floor.data.vertices];lo=min(zs);span=max(zs)-lo
    for v in floor.data.vertices:v.co.z=(v.co.z-lo)/span*.006
    floor.location.z=.292;floor.data.materials.clear();floor.data.materials.append(M['cavity'])
    subtract(body,cut,'full card name');bevel(body,.007,False)
    token=card['id'].split('-')[1]
    if not a.resume or not (out/('name-'+token+'.png')).exists():render(sc,out/('name-'+token+'.png'))
    print('HERO_NAME '+card['id'],flush=True)
bpy.context.window.scene=scenes[0]
source=root/'art/production/assets'/('CardArt-'+a.hero+'.blend')
bpy.ops.wm.save_as_mainfile(filepath=str(source),compress=True)
(out/'manifest.json').write_text(json.dumps({'Entries':entries},ensure_ascii=False,indent=2),encoding='utf8',newline='\n')
evidence=root/'art/production/card-art';evidence.mkdir(parents=True,exist_ok=True)
(evidence/(a.hero+'.json')).write_text(json.dumps({'hero':a.hero,'cards':len(cards),'icons':len(groups),'groups':{k:[c['id'] for c in v] for k,v in groups.items()},'source':source.relative_to(root).as_posix(),'method':'Original editable geometric relief; actual incised card names; CPU Cycles RGBA bake'},ensure_ascii=False,indent=2),encoding='utf8',newline='\n')
print('HERO_CARD_ART_COMPLETE '+a.hero,flush=True)
