"""Observation 4 -> relational records. No game-rule simulation, hashing or ID bytes.

Numeric slots are typed by record kind and have explicit presence masks. Foreign
keys become edges, never learned ordinal numbers. All collections are variable
length; exceeding a declared resource bound raises before any action is sampled.
"""
from dataclasses import dataclass
import hashlib
import json
from pathlib import Path
import torch

ENCODER_VERSION = 3
SYMBOLS = json.loads(Path(__file__).with_name('public-symbols-v1.json').read_text(encoding='utf-8'))
PHASES = 'HeroSelection Deployment Planning InitiativeChoice Action RoundEnd EffectChoice Finished'.split()
WINDOWS = ('initiative hero_respawn attack_target minion_protection primary_option effect_target effect_minion '
           'defense forced_discard optional_discard effect_move placement minion_return gold_transfer card_swap '
           'recover_discard discard_attack round_minion_removal action_minion_removal minion_spawn').split()
ZONES = 'InHand Selected PlayedUnresolved PlayedResolved Discarded'.split()
KINDS = 'hero melee ranged heavy'.split()
BONUSES = 'attack defense movement initiative skill_range attack_range'.split()
# Schema version fixes slot semantics; changes require a new encoder/checkpoint.
# Each spec is (numeric fields, categorical field -> vocabulary domain).
SPEC = {
    'match': ('Round Turn BlueCrystal RedCrystal BlueMarks RedMarks RemainingMinionRemovals AttackRange',
              {'Phase':'phase','Decision':'window','CombatRegion':'region','Coin':'team','RoundEndStage':'round_stage'}),
    'rules': ('StartingCrystalLife VictoryMarksRequired TurnsPerRound HandSize', {}),
    'cell': ('X Y Obstacle Lane', {'Region':'region','Base':'base','Spawn':'spawn'}),
    'definition': ('Initiative PrimaryValue SecondaryMovement SecondaryDefense Level Exclamation SubtypeValue',
                   {'Id':'card','HeroId':'hero','PrimaryFamily':'family','PrimaryCategory':'category','Color':'color','Subtype':'subtype','Passive':'bonus'}),
    'player': ('Level Gold Confirmed AwaitingRespawn Poisoned PoisonDefense Petrified BasicAttackBonus BasicAttackRangeBonus', {'Team':'team','Hero':'hero'}),
    'bonuses': ('Attack Defense Movement Initiative SkillRange AttackRange', {}),
    'card': ('PlayedRound PlayedTurn', {'Zone':'zone'}),
    'unit': ('Removable', {'Kind':'unit','Team':'team'}),
    'absent_unit': ('', {}),
    'effect': ('CreatedRound CreatedTurn Order StartRound StartTurn EndRound EndTurn BaseRadius PersistsThroughDefeat',
               {'Kind':'effect','Duration':'duration','AreaKind':'area'}),
    'event': ('Ordinal Round Turn Amount Amount2 Amount3', {'Kind':'event','Value':'event_value','SecondaryValue':'event_value'}),
    'sequence': ('Round Turn', {}),
    'action_card': ('Order Initiative Started Resolved Focused', {'Role':'role'}),
    'attack': ('Base Bonus Support Guard Final TextBonus UltimateBonus Ranged Unblockable', {}),
    'response': ('Optional', {'SourceSymbol':'source'}),
    'opening': ('DraftComplete OpeningComplete', {'Purpose':'purpose','Status':'opening_status','FirstTeam':'team','Result':'team','DraftTeam':'team'}),
    'upgrade': ('Round HeroLevel CardLevel Amount', {'Bonus':'bonus'}),
    'action': ('SuccessfulDefense ImmediateSkip', {'Kind':'action','Mode':'mode','Option':'option'}),
    'defense': ('Primary Blocked IgnoresMinions Base Bonus Final AttackCompared', {}),
    'facts': ('Place RemainingDistance TransferAmount PreviewOptional', {'PreviewKind':'window'}),
    'path_point': ('Order', {}),
}
SPEC = {k:(n.split(),c) for k,(n,c) in SPEC.items()}
MAX_NUM = max(len(n) for n,c in SPEC.values())
MAX_CAT = 1 + max(len(c) for n,c in SPEC.values())
RELATIONS = ('self active blue_captain red_captain upgrading rules permanent effective owner definition purple '
             'position pending_spawn source protected controller exempt area actor card from to path point '
             'sequence parent attacker defender target text_source support_source guard_source own_upgrade '
             'previous selected rejected response opening attack effect history destination value facts defense '
             'upgrade preview_target preview_cell next_cell').split()
RELATIONS = RELATIONS + ['reverse:'+r for r in RELATIONS]


def strict(obj, kind, extra=()):
    expected = set(SPEC[kind][0]) | set(SPEC[kind][1]) | set(extra)
    if set(obj) != expected:
        raise ValueError(f'unconsumed_or_missing_fields:{kind}:extra={sorted(set(obj)-expected)}:missing={sorted(expected-set(obj))}')


def scale(field):
    if field in ('X','Y'): return 20.
    if field in ('Order','Ordinal'): return 1000.
    if 'Round' in field: return 20.
    if 'Turn' in field: return 4.
    return 10.  # Exact linear rescaling, no clipping and no data-dependent normalization.


@dataclass
class Graph:
    numbers: torch.Tensor
    categories: torch.Tensor
    edges: torch.Tensor  # [source, destination, relation]; local indexes are pointers only.

    def to(self, device):
        return Graph(self.numbers.to(device), self.categories.to(device), self.edges.to(device))


class Builder:
    def __init__(self, encoder):
        self.encoder = encoder
        self.numbers, self.categories, self.edges, self.labels = [], [], [], []

    def node(self, kind, obj, extra=()):
        strict(obj, kind, extra)
        nums, cats = SPEC[kind]
        values = [0.] * (MAX_NUM * 2)
        for i, key in enumerate(nums):
            v = obj[key]
            if v is not None:
                if not isinstance(v, (int, float, bool)):
                    raise ValueError('nonnumeric:' + key)
                values[i] = float(v) if isinstance(v, bool) else float(v) / scale(key)
                values[MAX_NUM+i] = 1.
        categories = [self.encoder.vocab['type='+kind]]
        for key, domain in cats.items():
            value = obj[key] or ''
            token = domain + '=' + value
            if token not in self.encoder.vocab:
                raise ValueError(f'unknown_symbol:{kind}.{key}={value}')
            categories.append(self.encoder.vocab[token])
        categories += [0] * (MAX_CAT - len(categories))
        self.numbers.append(values); self.categories.append(categories); self.labels.append(kind)
        if len(self.numbers) > self.encoder.max_nodes:
            raise ValueError('entity_capacity_exceeded: no data was truncated')
        return len(self.numbers)-1

    def edge(self, source, target, kind):
        if target is None: return
        self.edges.append([source, target, self.encoder.relations[kind]])
        self.edges.append([target, source, self.encoder.relations['reverse:'+kind]])
        if len(self.edges) > self.encoder.max_edges:
            raise ValueError('relation_capacity_exceeded: no data was truncated')


class Encoder:
    def __init__(self, description):
        self.contract = description['Contract']
        if self.contract['ObservationVersion'] != 4 or self.contract['ActionVersion'] != 2:
            raise ValueError('unsupported observation/action format; regenerate public decisions, do not relabel old vectors')
        self.description = description
        self.cards = {c['Id']:c for c in description['Cards']}
        if len(self.cards) != len(description['Cards']): raise ValueError('duplicate card identity')
        self.card_ids = sorted(self.cards)
        self.kinds = description['ActionKinds']
        self.max_nodes, self.max_edges = 16384, 262144
        self.max_attention_pairs = 2000000
        self.relations = {s:i for i,s in enumerate(RELATIONS)}
        cells = description['Cells']
        domains = dict(phase=PHASES, window=PHASES+WINDOWS, team=['Blue','Red'],
            region=[c['Region'] for c in cells], base=[c['Base'] for c in cells], spawn=[c['Spawn'] for c in cells],
            card=self.card_ids, hero=description['Heroes'], family=[c['PrimaryFamily'] for c in self.cards.values()],
            category=[c['PrimaryCategory'] for c in self.cards.values()],
            color=[c['Color'] for c in self.cards.values()], subtype=[c['Subtype'] for c in self.cards.values()],
            bonus=BONUSES, zone=ZONES, unit=KINDS, effect=SYMBOLS['EffectKind'], duration=SYMBOLS['EffectDuration'],
            area=SYMBOLS['EffectAreaKind'], event=SYMBOLS['events'], role=['main','defense','discard','ultimate','reaction','recover'],
            source=['round_end','action','minion_battle','hero_defeat','minion_defeat','R-INITIATIVE','R-DEFEAT','R-ROUND-END'], purpose=['draft','opening'],
            opening_status=['throwing','settled','stuck','complete','resolved'], action=self.kinds, mode=['Secondary','Fast'],
            option=['skip','begin','protect','recover','battle','repeat','finish'], round_stage=['minion_battle','upgrades','frontline','complete'])
        domains['event_value'] = (['Blue','Red','skip','ultimate','Secondary','Fast','Primary','CardText','BeforeAction','DefenseResponse',
            'success','failure','defended','hit','defeated','removed','immune','minion_defeated','hero_defeated','minion_saved','protect','recover','battle','repeat','finish',
            'initiative_attack','initiative_attack_defense'] + description['Heroes'] + domains['region'] + domains['color'])
        tokens = {'type='+k for k in SPEC}
        for name, values in domains.items():
            tokens.update(name+'='+str(v or '') for v in list(values)+[''])
        self.vocab = {s:i+1 for i,s in enumerate(sorted(tokens))}
        self.signature = hashlib.sha256(json.dumps(dict(description=description,spec=SPEC,symbols=SYMBOLS,
            relations=RELATIONS,limits=[self.max_nodes,self.max_edges,self.max_attention_pairs]),sort_keys=True).encode()).hexdigest()
        self.model_kwargs = dict(numeric_dim=2*MAX_NUM, category_dim=MAX_CAT, vocabulary_size=len(self.vocab)+1,
                                 relation_count=len(RELATIONS), hidden=64)

    def encode(self, decision):
        if set(decision) != {'Revision','Observation','Actions'}: raise ValueError('unknown decision fields')
        o, actions = decision['Observation'], decision['Actions']
        if o['Schema'] != 4 or o['Rules'] != self.contract['RuleProfile']:
            raise ValueError('unvalidated rule profile or observation version')
        if not actions or len({a['Id'] for a in actions}) != len(actions): raise ValueError('empty/duplicate candidates')
        b = Builder(self)
        match_extras = 'Schema Rules Seat ActiveSeat BlueCaptain RedCaptain UpgradingSeats Response Sequence Opening Attack Players Units PendingSpawns OwnUpgrades PublicHistory Effects'.split()
        match = b.node('match', o, match_extras)
        rules = b.node('rules', o['Rules'], ['Id']); b.edge(match,rules,'rules')
        cells = {}
        for c in sorted(self.description['Cells'],key=lambda c:(c['Position']['X'],c['Position']['Y'])):
            if set(c) != {'Position','Region','Obstacle','Lane','Base','Spawn'}: raise ValueError('unknown map field')
            key = tuple(c['Position'][k] for k in ('X','Y'))
            if key in cells: raise ValueError('duplicate cell')
            cells[key] = b.node('cell',dict(c['Position'],**{k:v for k,v in c.items() if k!='Position'}))
        def cell(at):
            if at is None: return None
            if set(at) != {'X','Y'}: raise ValueError('unknown coordinate field')
            key = (at['X'],at['Y'])
            if key not in cells: raise ValueError('unknown map cell:'+str(key))
            return cells[key]
        # Topology is supplied once by the map; no Python movement/attack rule is implemented.
        for (x,y), n in cells.items():
            for dx,dy in ((1,0),(0,1),(-1,1)):
                if (x+dx,y+dy) in cells: b.edge(n,cells[x+dx,y+dy],'next_cell')
        defs = {cid:b.node('definition',self.cards[cid]) for cid in self.card_ids}
        def card(cid):
            if not cid: return None
            if cid not in defs: raise ValueError('unknown card:'+cid)
            return defs[cid]
        players = {}
        for p in sorted(o['Players'],key=lambda p:p['Seat']):
            if p['Seat'] in players: raise ValueError('duplicate player')
            n = b.node('player',p,['Seat','Purple','Permanent','Effective','Cards']); players[p['Seat']]=n
            b.edge(n,card(p['Purple']),'purple')
            for key in ('Permanent','Effective'): b.edge(n,b.node('bonuses',p[key]),key.lower())
            seen = set()
            for c in sorted(p['Cards'],key=lambda c:c['Id']):
                if c['Id'] in seen: raise ValueError('duplicate equipped card')
                seen.add(c['Id'])
                if p['Seat'] != o['Seat'] and c['Zone']=='Selected': raise ValueError('foreign hidden selection')
                cn = b.node('card',c,['Id']); b.edge(cn,n,'owner'); b.edge(cn,card(c['Id']),'definition')
        def player(seat):
            if seat is None or seat == -1: return None
            if seat not in players: raise ValueError('unknown player reference')
            return players[seat]
        for key,rel in [('Seat','self'),('ActiveSeat','active'),('BlueCaptain','blue_captain'),('RedCaptain','red_captain')]: b.edge(match,player(o[key]),rel)
        for s in sorted(o['UpgradingSeats']): b.edge(match,player(s),'upgrading')
        units = {}
        for group in ('Units','PendingSpawns'):
            for u in sorted(o[group],key=lambda u:u['Id']):
                if u['Id'] in units: raise ValueError('duplicate unit')
                n=b.node('unit',u,['Id','Seat','Position']);units[u['Id']]=n
                b.edge(n,player(u['Seat']),'controller'); b.edge(n,cell(u['Position']),'position')
                if group=='PendingSpawns': b.edge(match,n,'pending_spawn')
        def unit(uid):
            if not uid: return None
            if uid not in units: units[uid]=b.node('absent_unit',{})
            return units[uid]
        def path(parent, points):
            for i, at in enumerate(points):
                pn=b.node('path_point',dict(Order=i));b.edge(parent,pn,'path');b.edge(pn,cell(at),'point')
        if o['Response'] is not None:
            r=o['Response'];
            if set(r)!={'Source','Unit','Optional'}: raise ValueError('unknown response field')
            rn=b.node('response',dict(Optional=r['Optional'],SourceSymbol='' if r['Source'] in defs else r['Source']))
            b.edge(match,rn,'response'); b.edge(rn,card(r['Source']) if r['Source'] in defs else None,'card'); b.edge(rn,unit(r['Unit']),'target')
        if o['Opening'] is not None: b.edge(match,b.node('opening',o['Opening']),'opening')
        seq=o['Sequence']; sn=b.node('sequence',seq,['Cards']); b.edge(match,sn,'sequence')
        queue={}
        for q in sorted(seq['Cards'],key=lambda q:q['Key']):
            if q['Key'] in queue: raise ValueError('duplicate action-sequence key')
            n=b.node('action_card',q,['Key','Parent','Seat','Card']);queue[q['Key']]=n
            b.edge(n,sn,'sequence');b.edge(n,player(q['Seat']),'actor');b.edge(n,card(q['Card']),'card')
        for q in seq['Cards']:
            if q['Parent'] is not None:
                if q['Parent'] not in queue: raise ValueError('dangling action parent')
                b.edge(queue[q['Key']],queue[q['Parent']],'parent')
        for e in sorted(o['Effects'],key=lambda e:e['Order']):
            n=b.node('effect',e,['Card','SourceUnit','ProtectedUnit','Controller','ExemptSeat','Area']);b.edge(match,n,'effect')
            for key,relation,lookup in [('Card','card',card),('SourceUnit','source',unit),('ProtectedUnit','protected',unit),('Controller','controller',player),('ExemptSeat','exempt',player)]: b.edge(n,lookup(e[key]),relation)
            for at in sorted(e['Area'],key=lambda a:(a['X'],a['Y'])): b.edge(n,cell(at),'area')
        for e in sorted(o['PublicHistory'],key=lambda e:e['Ordinal']):
            n=b.node('event',e,['Card','Seat','From','To','Path','OtherSeat','Unit']);b.edge(match,n,'history')
            b.edge(n,card(e['Card']),'card');b.edge(n,player(e['Seat']),'actor');b.edge(n,player(e['OtherSeat']),'target');b.edge(n,unit(e['Unit']),'value');b.edge(n,cell(e['From']),'from');b.edge(n,cell(e['To']),'to');path(n,e['Path'])
        if o['Attack'] is not None:
            a=o['Attack'];n=b.node('attack',a,['Card','Target','Attacker','Defender','TextSources','SupportSources','GuardSources']);b.edge(match,n,'attack')
            b.edge(n,card(a['Card']),'card');b.edge(n,unit(a['Target']),'target');b.edge(n,player(a['Attacker']),'attacker');b.edge(n,player(a['Defender']),'defender')
            # Repeated support IDs are repeated edges: one entry per contribution, never deduplicated.
            for key,rel in [('TextSources','text_source'),('SupportSources','support_source'),('GuardSources','guard_source')]:
                for uid in a[key]: b.edge(n,unit(uid),rel)
        def upgrade(u):
            n=b.node('upgrade',u,['Previous','Selected','Rejected'])
            for k in ('Previous','Selected','Rejected'): b.edge(n,card(u[k]),k.lower())
            return n
        for u in o['OwnUpgrades']: b.edge(player(o['Seat']),upgrade(u),'own_upgrade')
        action_nodes={}
        for a in sorted(actions,key=lambda a:a['Id']):
            if set(a)!={'Id','Kind','Value','Mode','TargetSeat','Destination','HasDestination','SuccessfulDefense','ImmediateSkip','Facts'}: raise ValueError('unknown action field')
            value=a['Value']; target=None; option=''
            if value in defs: target=card(value)
            elif value in units: target=unit(value)
            elif value and value!='skip' and a['Kind'] in ('ChooseAttackTarget','ChooseEffectTarget','ChooseMinionReturn','ChooseMinionSpawn','ChooseRoundMinionRemoval'):
                target=unit(value)  # Displaced/removed units may currently have no board cell.
            elif a['Kind']=='ChooseHero':
                # Hero identity is a categorical property, represented by its catalog card definitions.
                if value not in self.description['Heroes']: raise ValueError('unknown hero')
            elif a['Kind']=='ChooseGoldTransfer':
                if int(value)!=a['Facts']['TransferAmount']: raise ValueError('transfer amount mismatch')
            else: option=value
            if a['Kind']!='Move' and a['Mode']!='Secondary': raise ValueError('unexpected non-movement mode')
            if a['Kind']!='Defend' and a['SuccessfulDefense']: raise ValueError('defense fact on unrelated action')
            if a['Kind']!='BeginPrimary' and a['ImmediateSkip']: raise ValueError('skip fact on unrelated action')
            n=b.node('action',dict(Kind=a['Kind'],Mode=a['Mode'] if a['Kind']=='Move' else '',Option=option,
                SuccessfulDefense=a['SuccessfulDefense'] if a['Kind']=='Defend' else None,
                ImmediateSkip=a['ImmediateSkip'] if a['Kind']=='BeginPrimary' else None));action_nodes[a['Id']]=n
            if a['Kind']=='ChooseHero':
                for cid,c in self.cards.items():
                    if c['HeroId']==value: b.edge(n,defs[cid],'value')
            b.edge(n,target,'value');b.edge(n,player(a['TargetSeat']),'target')
            if a['HasDestination']: b.edge(n,cell(a['Destination']),'destination')
            f=a['Facts'];fn=b.node('facts',f,['Path','Defense','Upgrade','PreviewTargets','PreviewCells']);b.edge(n,fn,'facts');path(n,f['Path'])
            if f['Defense'] is not None: b.edge(n,b.node('defense',f['Defense']),'defense')
            if f['Upgrade'] is not None: b.edge(n,upgrade(f['Upgrade']),'upgrade')
            for uid in f['PreviewTargets']: b.edge(n,unit(uid),'preview_target')
            for at in sorted(f['PreviewCells'],key=lambda a:(a['X'],a['Y'])): b.edge(n,cell(at),'preview_cell')
        if len(b.numbers)*len(actions)>self.max_attention_pairs:
            raise ValueError('attention_capacity_exceeded: no candidate was truncated')
        numbers=torch.tensor(b.numbers,dtype=torch.float32)
        if not torch.isfinite(numbers).all(): raise ValueError('nonfinite graph features')
        self.last_inventory={k:b.labels.count(k) for k in sorted(set(b.labels))}
        return Graph(numbers,torch.tensor(b.categories,dtype=torch.long),torch.tensor(b.edges,dtype=torch.long).T.contiguous()), torch.tensor([action_nodes[a['Id']] for a in actions],dtype=torch.long)
