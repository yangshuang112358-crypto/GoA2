"""Human-readable dictionary from the actually validated entity slot registry."""
import argparse,csv,json
from pathlib import Path

p=argparse.ArgumentParser();p.add_argument('--dictionary',type=Path,required=True);p.add_argument('--output',type=Path,required=True);args=p.parse_args()
d=json.loads(args.dictionary.read_text(encoding='utf-8'))
meaning={
 'Round':'所属轮次','Turn':'该轮中的回合','BlueCrystal':'蓝队当前水晶生命','RedCrystal':'红队当前水晶生命',
 'BlueMarks':'蓝队已得推进标记','RedMarks':'红队已得推进标记','RemainingMinionRemovals':'此次兵战尚需移除数',
 'AttackRange':'当前攻击距离；在bonuses记录中为远程加成','Phase':'规则阶段','Decision':'实际待决策种类',
 'CombatRegion':'当前战斗区域','Coin':'公开决策币朝向','RoundEndStage':'轮末结算步骤',
 'StartingCrystalLife':'本规则初始水晶生命','VictoryMarksRequired':'本规则推进胜利阈值','TurnsPerRound':'每轮回合数','HandSize':'规则手牌规模',
 'X':'轴向六角坐标X','Y':'轴向六角坐标Y','Obstacle':'是否障碍格','Lane':'是否兵线格','Region':'所在地图区域',
 'Base':'基地所属方；攻防记录中是基础数值','Spawn':'地图出生点类型标记',
 'Initiative':'先攻；卡定义是印刷值，行动链是公开有效值，bonuses是加成',
 'PrimaryValue':'主要行动印刷数值','SecondaryMovement':'次要移动值；null表示没有该行动','SecondaryDefense':'次要防御值；null表示没有该行动',
 'Level':'角色等级；definition记录是卡牌级别，基础卡可为null','Exclamation':'是否有感叹号',
 'SubtypeValue':'范围/远程子类型的数值','Id':'稳定卡定义ID，如wasp-05','HeroId':'卡牌所属英雄稳定ID',
 'PrimaryFamily':'攻击/防御/移动/技能/终极能力家族','PrimaryCategory':'区分基础攻击、基础技能等具体类别',
 'Color':'卡牌颜色类别','Subtype':'skill_range或attack_range等数值子类型','Passive':'该牌对应的升级被动类型',
 'Gold':'该角色当前金币','Confirmed':'该角色是否已确认本次暗选，不含所选身份','AwaitingRespawn':'该角色是否等待复活',
 'Poisoned':'该角色是否中毒','PoisonDefense':'中毒是否也影响防御','Petrified':'是否石化',
 'BasicAttackBonus':'仅基础攻击适用的攻击加成','BasicAttackRangeBonus':'仅基础攻击适用的距离加成','Team':'该角色/单位的队伍','Hero':'该角色的稳定英雄ID',
 'Attack':'攻击属性加成','Defense':'防御属性加成','Movement':'移动属性加成','SkillRange':'技能范围属性加成',
 'PlayedRound':'这张装备牌最近一次打出的轮次，null为没有记录','PlayedTurn':'这张装备牌最近一次打出的回合，与PlayedRound配对',
 'Zone':'InHand/Selected/PlayedUnresolved/PlayedResolved/Discarded；他人暗选仍为InHand',
 'Removable':'C#公开规则查询认为该单位可移除；不是策略自行推算','Kind':'由记录类型决定：单位类型、效果种类、公开事件或命令种类',
 'CreatedRound':'效果创建轮次','CreatedTurn':'效果创建回合','Order':'效果创建顺序、行动链顺序或路径点顺序，依记录类型解释',
 'StartRound':'效果开始轮次','StartTurn':'效果开始回合','EndRound':'效果结束轮次','EndTurn':'效果结束回合',
 'BaseRadius':'效果基础半径，实际覆盖格由C#给出','PersistsThroughDefeat':'来源被击败后效果是否保留',
 'Duration':'效果持续类型','AreaKind':'效果区域计算类型','Ordinal':'过滤私有事件后的公开事件序号',
 'Amount':'依事件类型：金币/伤害/数量；HeroLeveled为原等级；兵战为蓝方计数',
 'Amount2':'HeroLeveled为新等级；兵战为红方计数','Amount3':'HeroLeveled为升级费用；兵战为待移除数',
 'Value':'依事件类型的公开结果/颜色/英雄/区域/移动类别，不是内部ID','SecondaryValue':'第二个公开符号，如币翻转后的队伍或换牌第二种颜色',
 'Started':'这张公开行动牌是否已开始','Resolved':'这张公开行动牌是否已结束','Focused':'是否为规则行动链当前公开焦点',
 'Role':'main/defense/discard/ultimate/reaction/recover，表示牌在行动链中的作用',
 'Bonus':'攻防数值修正；upgrade记录中为长期加成种类','Support':'攻击方对本次防御目标的支援数值',
 'Guard':'防御方对本次防御目标的守护数值','Final':'C#最终攻击/防御数值','TextBonus':'攻击牌文修正，已包含在Bonus',
 'UltimateBonus':'终极能力的攻击修正，已包含在Bonus','Ranged':'此次是否为远程攻击','Unblockable':'此次是否无法抵挡',
 'Optional':'当前响应是否可跳过','SourceSymbol':'非卡牌的公开规则来源，如R-INITIATIVE；卡来源通过引用边表示',
 'DraftComplete':'英雄选择是否完成','OpeningComplete':'开局流程是否完成','Purpose':'投币用途','Status':'公开开局状态',
 'FirstTeam':'先选队伍','Result':'公开投币结果','DraftTeam':'当前BP队伍','HeroLevel':'本次升级对应英雄级别',
 'CardLevel':'本次升级对应卡牌级别','SuccessfulDefense':'仅防御候选适用的正式成功判断','ImmediateSkip':'仅BeginPrimary适用的立即无事可做判断',
 'Mode':'仅Move适用：Secondary/Fast','Option':'skip/begin/protect/recover/battle/repeat/finish等稳定分支ID',
 'Primary':'防御使用主要防御属性还是次要属性','Blocked':'正式抵挡判定','IgnoresMinions':'防御是否忽略小兵',
 'AttackCompared':'这张防御牌实际比较的攻击值','Place':'小兵返回是否为放置','RemainingDistance':'此次小兵返回剩余距离',
 'TransferAmount':'本候选转移金币数','PreviewOptional':'主要行动预览后续选择是否可选','PreviewKind':'C#预览显示的后续决策类型',
}
overrides={('upgrade','Amount'):'本次升级获得的永久加成数量',('upgrade','Bonus'):'本次升级获得的永久加成种类',
 ('cell','Base'):'基地所属方，空表示不属于基地',('attack','Base'):'本次攻击的基础攻击值',('defense','Base'):'候选防御牌的基础防御值',
 ('player','Level'):'该角色当前英雄等级',('definition','Level'):'卡牌级别；基础卡为null，与英雄等级不同',
 ('bonuses','AttackRange'):'永久/有效远程属性加成，由连接边区分',('match','AttackRange'):'正式规则查询的当前攻击距离',
 ('attack','Bonus'):'本次总攻击修正，已包含TextBonus和UltimateBonus',('defense','Bonus'):'该候选正式防御修正'}
rows=[]
for record,spec in d['records'].items():
 for field in spec['numeric']:
  key=field['field'];rows.append([record,key,'number',field['slot'],overrides.get((record,key),meaning[key]),f"数字/{field['scale']:g}；布尔0/1",f"presence槽{field['presence_slot']}；null与0不同"])
 for field in spec['categorical']:
  key=field['field'];rows.append([record,key,'category',field['slot'],overrides.get((record,key),meaning[key]),f"{field['domain']}类别Embedding",'0槽号是padding；空类别有独立符号'])
args.output.parent.mkdir(parents=True,exist_ok=True)
with args.output.open('w',encoding='utf-8-sig',newline='') as f:
 w=csv.writer(f);w.writerow(['record','field','type','slot','含义','编码','缺省/存在性']);w.writerows(rows)
print(len(rows),'typed fields;',len(d['relations']),'directed relation types;',args.output)
