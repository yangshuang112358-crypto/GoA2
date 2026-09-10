"""Validate the restart package without dependencies or executing legacy code."""
from pathlib import Path
import argparse, collections, hashlib, json, re, sys
from urllib.parse import unquote

ROOT = Path(__file__).resolve().parents[1]

class ValidationError(ValueError):
    pass

def require(condition, message):
    if not condition:
        raise ValidationError(message)

def unique_object(pairs):
    obj = {}
    for key, value in pairs:
        require(key not in obj, 'duplicate JSON key: ' + key)
        obj[key] = value
    return obj

def read(path):
    return json.loads(path.read_text(encoding='utf-8'), object_pairs_hook=unique_object)

def hashfile(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def inside(root, relative):
    require(not Path(relative).is_absolute() and not re.match(r'^[a-zA-Z]:', relative), 'absolute path: '+relative)
    target = (root / relative).resolve()
    require(target.is_relative_to(root.resolve()), 'path escapes repository: '+relative)
    return target

def validate_fixtures(root):
    manifest = read(root / 'tests/fixtures/manifest.json')
    require(type(manifest) is dict and set(manifest) == {'schema_version', 'fixtures'}, 'invalid fixture manifest fields')
    require(manifest['schema_version'] == '1.0.0', 'unsupported fixture manifest version')
    entries = manifest['fixtures']
    require(type(entries) is list and len(entries) > 0, 'empty fixture manifest')
    versions, covered = set(), set()

    def fixture_path(relative, suffix):
        require(type(relative) is str and re.fullmatch(r'tests/fixtures/[^/\\]+\.' + suffix, relative), 'invalid fixture path: ' + str(relative))
        path = inside(root, relative)
        require(path.is_relative_to((root / 'tests/fixtures').resolve()) and path.is_file(), 'missing or outside fixture: ' + relative)
        return path

    def captured_file(relative, digest):
        path = fixture_path(relative, 'json')
        require(type(digest) is str and re.fullmatch(r'[a-f0-9]{64}', digest), 'invalid fixture digest: ' + relative)
        require(relative not in covered, 'duplicate captured file: ' + relative)
        require(hashfile(path) == digest, 'historical fixture hash mismatch: ' + relative)
        covered.add(relative)
        return read(path)

    fields = {'engine_version', 'accepted_commands', 'save', 'save_sha256', 'input', 'input_sha256', 'provenance'}
    for entry in entries:
        require(type(entry) is dict and set(entry) == fields, 'invalid fixture entry fields')
        version, count = entry['engine_version'], entry['accepted_commands']
        require(type(version) is int and version >= 0 and version not in versions, 'invalid or duplicate fixture engine version')
        require(type(count) is int and 0 < count <= 10000, 'invalid fixture command count')
        versions.add(version)
        state = captured_file(entry['save'], entry['save_sha256'])
        require(type(state) is dict, 'fixture state must be an object')
        require(all(type(state.get(key, 0)) is int and state.get(key, 0) == version for key in ('InitialEngineVersion', 'EngineVersion')), 'fixture engine metadata mismatch')
        require(type(state.get('Revision')) is int and state['Revision'] == count and type(state.get('AcceptedCommands')) is list and len(state['AcceptedCommands']) == count, 'fixture command metadata mismatch')
        if entry['input'] is None:
            require(entry['input_sha256'] is None, 'input digest has no captured input')
        else:
            captured_file(entry['input'], entry['input_sha256'])
        fixture_path(entry['provenance'], 'md')
    expected = {p.relative_to(root).as_posix() for p in (root / 'tests/fixtures').glob('engine*.json')}
    expected.add('tests/fixtures/legacy-v1-roundend.json')
    require(covered == expected, 'historical fixture manifest coverage mismatch')
    return {'frozen_fixtures': len(entries), 'frozen_files': len(covered)}

def schema_check(value, schema, path='$'):
    # Deliberately bounded subset used by the schemas shipped in this repository.
    known = {'$schema','title','description','type','const','enum','properties','required',
             'additionalProperties','items','minItems','maxItems','minimum','maximum','minLength','anyOf'}
    require(not (set(schema)-known), path+': unsupported schema keywords '+str(set(schema)-known))
    if 'anyOf' in schema:
        errors=[]
        for child in schema['anyOf']:
            try:
                schema_check(value, child, path)
                return
            except ValidationError as e:
                errors.append(str(e))
        raise ValidationError(path+': no anyOf branch matched')
    types={'object':lambda v:type(v) is dict,'array':lambda v:type(v) is list,
           'integer':lambda v:type(v) is int,'number':lambda v:type(v) in (int,float),
           'boolean':lambda v:type(v) is bool,'null':lambda v:v is None,'string':lambda v:type(v) is str}
    if 'type' in schema:
        names=schema['type'] if isinstance(schema['type'],list) else [schema['type']]
        require(all(n in types for n in names), path+': unsupported type')
        require(any(types[n](value) for n in names),path+': incorrect type')
    if 'const' in schema: require(type(value)==type(schema['const']) and value==schema['const'],path+': incorrect const')
    if 'enum' in schema: require(any(type(value)==type(v) and value==v for v in schema['enum']),path+': invalid enum')
    if type(value) is dict:
        props=schema.get('properties',{})
        require(set(schema.get('required',[]))<=set(value),path+': missing required properties')
        if schema.get('additionalProperties') is False: require(set(value)<=set(props),path+': unknown properties')
        for key,child in value.items():
            if key in props: schema_check(child,props[key],path+'.'+key)
    if type(value) is list:
        require(len(value)>=schema.get('minItems',0),path+': too few items')
        require(len(value)<=schema.get('maxItems',float('inf')),path+': too many items')
        for i,child in enumerate(value):
            if 'items' in schema:schema_check(child,schema['items'],path+'['+str(i)+']')
    if type(value) is str: require(len(value)>=schema.get('minLength',0),path+': string too short')
    if type(value) in (int,float):
        require(value>=schema.get('minimum',-float('inf')),path+': below minimum')
        require(value<=schema.get('maximum',float('inf')),path+': above maximum')

def validate_catalog(heroes, cards, board, statuses):
    require(len(heroes)==6 and len(cards)==108, 'expected 6 heroes and 108 cards')
    hids=[h['hero_id'] for h in heroes]
    ids=[c['id'] for c in cards]
    require(len(set(hids))==6 and len(set(ids))==108,'duplicate hero/card ID')
    for h in heroes:
        hc=[c for c in cards if c['hero_id']==h['hero_id']]
        require(len(hc)==18,'hero must have 18 cards: '+h['hero_id'])
        expected={('gold',None):1,('silver',None):1,('purple',4):1,
                  **{(color,level):(1 if level==1 else 2) for color in ['red','green','blue'] for level in [1,2,3]}}
        require(dict(collections.Counter((c['color_key'],c['level']) for c in hc))==expected,'invalid upgrade graph: '+h['hero_id'])
    for c in cards:
        require(c['hero_id'] in hids and c['id'].startswith(c['hero_id']+'-'),'broken hero reference')
        for name,slot in c['secondary_actions'].items():
            require((slot['has_action'] is False and slot['value'] is None) or
                    (slot['has_action'] is True and type(slot['value']) is int and slot['value']>0),'invalid secondary slot: '+c['id'])
        if c['primary_action']['family']=='movement':
            require(not c['secondary_actions']['movement']['has_action'],'primary move cannot have secondary move')
        family={'基础攻击':'attack','攻击':'attack','基础技能':'skill','技能':'skill','移动':'movement','防御':'defense','终极技能':'ultimate'}
        require(family.get(c['primary_action']['category'])==c['primary_action']['family'],'category-family mismatch')
    require(len(board)==254 and len({(c['x'],c['y']) for c in board})==254,'duplicate/missing map coordinate')
    require(sum(c['obstacle'] for c in board)==44,'obstacle count mismatch')
    regions={'redFountain':19,'redNear':41,'mid':60,'blueNear':41,'blueFountain':19,'topGrass':15,'bottomGrass':15,'terrain':44}
    require(dict(collections.Counter(c['region'] for c in board))==regions,'region distribution mismatch')
    for c in board:
        require(c['obstacle']==(c['state']=='terrain')==(c['region']=='terrain'),'obstacle/state/region mismatch')
        if c['base'] is not None:require(c['region']==c['base']+'Fountain','base mismatch')
        if 'Spawn' in c['state']:require(not c['obstacle'],'spawn on obstacle')
    for region in ['redNear','mid','blueNear']:
        for team in ['red','blue']:
            for kind in ['Melee','Ranged','Heavy']:
                expected=4 if kind=='Melee' else 1
                if kind=='Melee' and ((region=='redNear' and team=='blue') or (region=='blueNear' and team=='red')): expected=3
                require(sum(c['region']==region and c['state']==team+kind+'Spawn' for c in board)==expected,'spawn distribution mismatch')
    for team in ['red','blue']:
        require(sum(c['state']==team+'HeroSpawn' and c['region']==team+'Fountain' for c in board)==3,'hero spawn mismatch')
    require(len(statuses)==108 and {s['card_id'] for s in statuses}==set(ids),'status coverage mismatch')
    for s in statuses:
        stage=s['status']
        require(stage in {'data_only','specified','implemented','behavior_tested','integration_tested'},'unknown card status')
        if stage=='data_only':
            require(s['contract'] is None and s['tests']==[] and s.get('implementation_files',[])==[],'data-only card cannot claim behavior evidence')
        else:
            require(bool(s['contract']) and not s['known_questions'],'specified card needs a resolved contract')
        if stage in {'implemented','behavior_tested','integration_tested'}:
            require(bool(s.get('implementation_files')),'implemented card needs implementation references')
        if stage in {'behavior_tested','integration_tested'}:
            require(bool(s['tests']),'tested card needs behavior evidence')

def validate(root=ROOT):
    root=root.resolve()
    for base in ['content','docs','sources']:
        for p in (root/base).rglob('*.json'): read(p)
    schema_names=['heroes','cards','map','ruleset']
    content={}
    for name in schema_names:
        content[name]=read(root/('content/canonical/'+name+'.json'))
        schema_check(content[name],read(root/('content/schemas/'+name+'.schema.json')))
    status=read(root/'content/status/cards.json')
    schema_check(status,read(root/'content/schemas/status.schema.json'))
    heroes,cards,board=content['heroes']['heroes'],content['cards']['cards'],content['map']['cells']
    validate_catalog(heroes,cards,board,status['cards'])
    for entry in status['cards']:
        refs=entry['tests']+entry['implementation_files']+([entry['contract']] if entry['contract'] else [])
        for ref in refs:
            require(not ref.startswith('sources/'),'legacy evidence cannot establish new card completion')
            require(inside(root,ref).is_file(),'missing card evidence: '+ref)
    # Prove field-for-field migration; legacy status fields are deliberately omitted.
    legacy=read(root/'sources/legacy/goa2_v4/data/cards.json')
    lc=[c for h in legacy['heroes'] for c in h['cards']]
    for old,new in zip(lc,cards):
        stripped={k:v for k,v in old.items() if k not in {'hero','color','implementation','implementation_status','implementation_status_label'}}
        require(stripped==new,'formal card changed during migration: '+old['id'])
    expected_heroes=[{'hero_id':h['hero_id'],'name':h['name'],'legacy_default_team':h['default_team']} for h in legacy['heroes']]
    require(heroes==expected_heroes,'hero data changed during migration')
    require(board==read(root/'sources/legacy/goa2_v4/data/map.json'),'map changed during migration')
    manifest=read(root/'content/manifest.json')
    for entry in manifest['files']:
        require(hashfile(inside(root,entry['path']))==entry['sha256'],'content hash mismatch: '+entry['path'])
    require({e['path'] for e in manifest['files']}=={'content/canonical/'+n+'.json' for n in schema_names},'content manifest incomplete')
    for entry in read(root/'sources/index.json')['entries']:
        require(hashfile(inside(root,entry['archive_path']))==entry['sha256'],'source index mismatch: '+entry['archive_path'])
    sm=read(root/'sources/source-manifest.json')
    expected={p.relative_to(root).as_posix() for p in (root/'sources').rglob('*') if p.is_file() and p.name!='source-manifest.json'}
    require({e['path'] for e in sm['files']}==expected,'source manifest coverage mismatch')
    for e in sm['files']: require(hashfile(inside(root,e['path']))==e['sha256'],'source changed: '+e['path'])
    prov=read(root/'sources/card-provenance.json')
    require({c['card_id'] for c in prov['cards']}=={c['id'] for c in cards} and len(prov['cards'])==108,'provenance coverage mismatch')
    for h in prov['heroes']:
        for rel in h['photos']+[h['hero_description_ocr'],h['card_text_ocr']]:require(inside(root,rel).is_file(),'missing photo/OCR source')
    ruletext=(root/'docs/rules/规则手册.md').read_text(encoding='utf-8')
    ruleids=set(re.findall(r'^## (R-[A-Z-]+)$',ruletext,re.M))
    for rid in content['ruleset']['source_rule_ids']:require(rid in ruleids,'missing rule: '+rid)
    # Confirm historical turn citations resolve to retrieved evidence.
    tids={t['turn_id'] for name in ['guide','rules','audit','tutor'] for t in read(root/('sources/history/'+name+'.json'))['turns']}
    for p in [root/'README.md',root/'AGENTS.md',*(root/'docs').rglob('*.md'),*(root/'tests').rglob('*.md')]:
        text=p.read_text(encoding='utf-8')
        require('\ufffd' not in text,'replacement character in '+str(p.relative_to(root)))
        require(all(line==line.rstrip() for line in text.splitlines()),'trailing whitespace in '+str(p.relative_to(root)))
        for tid in re.findall(r'T-([a-f0-9-]{36})',text):require(tid in tids,'unknown history citation: '+tid)
        for target in re.findall(r'\]\(([^)]+)\)',text):
            target=unquote(target.strip('<>').split('#',1)[0])
            if not target or re.match(r'^[a-zA-Z]+://',target):continue
            require((p.parent/target).resolve().exists(),'broken link in '+str(p.relative_to(root))+': '+target)
    from generate_views import outputs
    for path,text in outputs(root).items():
        require(inside(root,path).read_text(encoding='utf-8')==text.rstrip()+'\n','stale generated view: '+path)
    legacy_tests=read(root/'docs/history/legacy-tests.json')
    require(len(legacy_tests['tests'])==legacy_tests['test_count']==135,'legacy test evidence mismatch')
    fixture_report=validate_fixtures(root)
    return {'result':'PASS','heroes':6,'cards':108,'map_cells':254,'obstacles':44,'card_review_drafts':108,
            'source_files':len(sm['files']),'rules':len(ruleids),'legacy_tests_passed':135,
            **fixture_report,
            'scope':'Restart data/documentation/tools and historical-fixture integrity only; no Goa2V1 game implementation tested.'}

if __name__=='__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('--root',type=Path,default=ROOT)
    parser.add_argument('--report',type=Path)
    args=parser.parse_args()
    try:
        result=validate(args.root)
        text=json.dumps(result,ensure_ascii=False,indent=2)+'\n'
        if args.report:
            args.report.parent.mkdir(parents=True,exist_ok=True)
            args.report.write_text(text,encoding='utf-8',newline='\n')
        print(text,end='')
    except (ValidationError,ValueError,KeyError,OSError) as e:
        print('FAIL: '+str(e),file=sys.stderr)
        sys.exit(1)
