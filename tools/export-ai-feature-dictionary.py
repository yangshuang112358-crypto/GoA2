"""Document every encoder-v2 coordinate, with values from an archived public input.

This exports an explanation, not a replacement encoder or a completeness test.
"""
import argparse
import csv
import hashlib
import json
from pathlib import Path
import subprocess
import sys
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "ai/trainer"))
sys.stdout.reconfigure(encoding="utf-8")
from policy import Encoder, PHASES, ZONES, KINDS, ENCODER_VERSION

p = argparse.ArgumentParser()
p.add_argument("--output", type=Path, required=True)
args = p.parse_args()
root = Path(__file__).resolve().parents[1]
catalog_path = root / "docs/verification/ai-stage4-20261007/defense-finetune-01/public-catalog.json"
input_path = root / "docs/verification/ai-observation-audit-20261007/input.json"
enc = Encoder(json.loads(catalog_path.read_text(encoding="utf-8")))
d = json.loads(input_path.read_text(encoding="utf-8"))
s, candidates = enc.encode(d)
rows, part, index = [], "state", 0

def add(group, name, meaning, encoding, missing="不适用"):
    global index
    example = s[index] if part == "state" else candidates[0,index]
    rows.append(dict(part=part, index=index, actor_concat_index=index if part == "state" else len(s)+index,
                     group=group, name=name, meaning=meaning, encoding=encoding, missing=missing,
                     example_value=float(example)))
    index += 1

for phase in PHASES:
    add("阶段", "phase."+phase, "当前Phase是否为"+phase, "相等为1，否则0")
for name, meaning, formula in [
    ("round","当前轮数","Round/20"),("turn","轮内回合数","Turn/TurnsPerRound"),
    ("starting_life","规则配置的初始水晶生命","StartingCrystalLife/10"),
    ("marks_required","规则配置的获胜标记目标","VictoryMarksRequired/5"),
    ("own_life_relative","己方当前水晶生命相对初始值","己方Crystal/StartingCrystalLife"),
    ("enemy_life_relative","敌方当前水晶生命相对初始值","敌方Crystal/StartingCrystalLife"),
    ("own_marks_relative","己方标记相对目标","己方Marks/VictoryMarksRequired"),
    ("enemy_marks_relative","敌方标记相对目标","敌方Marks/VictoryMarksRequired"),
    ("own_life_absolute","己方当前水晶生命保留固定尺度","己方Crystal/10"),
    ("enemy_life_absolute","敌方当前水晶生命保留固定尺度","敌方Crystal/10"),
    ("own_marks_absolute","己方当前标记保留固定尺度","己方Marks/5"),
    ("enemy_marks_absolute","敌方当前标记保留固定尺度","敌方Marks/5"),
    ("coin_is_own","决策币是否为己方","相等为1，否则0"),
    ("hand_size","规则配置手牌数","HandSize/5")]:
    add("回合与目标",name,meaning,formula)
for side in ("friendly","enemy"):
    label="己方全队（含自己）" if side=="friendly" else "敌方全队"
    for name, meaning, scale in [("Level","等级和",16),("Gold","金币和",40),("HandCount","手牌数量和",10),
                                ("AwaitingRespawn","等待复活人数",2),("Poisoned","中毒人数",2),("Petrified","石化人数",2)]:
        add("队伍统计",side+"."+name,label+meaning,f"该队所有玩家{name}求和/{scale}")
    for kind in KINDS:
        for name,meaning,formula in [("count","数量","数量/10"),("mean_x","X坐标均值","X坐标和/max(1,数量)/20"),("mean_y","Y坐标均值","Y坐标和/max(1,数量)/20")]:
            add("单位聚合",f"{side}.{kind}.{name}",label+kind+meaning,formula,"该类单位不存在则为0")
for name,meaning,formula,missing in [("self_exists","本人英雄是否在场","存在为1，否则0","不存在为0"),
    ("self_x","本人英雄X坐标","X/20","本人单位不存在则0"),("self_y","本人英雄Y坐标","Y/20","本人单位不存在则0"),
    ("self_level","本人等级","Level/8","不适用"),("self_gold","本人金币","Gold/20","不适用")]:
    add("本人",name,meaning,formula,missing)
zone_labels=dict(InHand="在手",Selected="本人已暗选",PlayedUnresolved="已揭示尚未结算",PlayedResolved="已结算",Discarded="已弃置")
for cid in enc.card_ids:
    for z in ZONES:
        add("本人牌区",f"own_card.{cid}.{z}",f"本人{cid}是否处于{zone_labels[z]}","相等为1，否则0","未装备此牌则该牌5项全0；不表示被弃置")
for cid in enc.card_ids:
    add("当前公开牌",f"current_card.{cid}","当前公开焦点牌是否为"+cid,"相等为1，否则0","CurrentCard空则108项全0")
for side in ("teammate","enemy"):
    label="己方其他玩家" if side=="teammate" else "敌方玩家"
    for cid in enc.card_ids:
        for z in ZONES:
            add("他人牌区",f"{side}.card.{cid}.{z}",label+f"公开的{cid}处于{zone_labels[z]}的数量", "该组匹配数量/全场玩家数（当前为4）","没有则0；他人Selected被投影为InHand")
for name,meaning,formula in [("present","当前是否有攻击算式","Attack非空为1"),("attacker_self","攻击者是否本人","Attacker==Seat"),("defender_self","防御者是否本人","Defender==Seat")]:
    add("攻击", "attack."+name,meaning,formula,"无Attack时为0")
for name,meaning in [("Base","牌面基础攻击"),("Bonus","本次合计攻击加成（已包含TextBonus/UltimateBonus）"),
                    ("Support","从被攻击者视角的敌方小兵支援点数（增加攻击）"),("Guard","从被攻击者视角的友方守卫点数（降低攻击）"),
                    ("Final","规则算出的最终攻击"),("TextBonus","合计加成内的牌文部分"),("UltimateBonus","合计加成内的紫卡部分")]:
    add("攻击","attack."+name,meaning,name+"/10","无Attack或空值则0")
for name,meaning in [("Ranged","本次攻击是否远程"),("Unblockable","本次攻击的不可抵挡标志")]:
    add("攻击","attack."+name,meaning,"真1假0","无Attack则0")
add("距离","nearest_enemy","本人到最近敌方单位的六边形直线距离","最小hex距离/20","没有敌人则40/20=2；本人不在场时从(0,0)计算")
add("距离","nearest_enemy_nonhero","本人到最近敌方非英雄单位的六边形直线距离","若无敌方非英雄则改为所有敌方单位；最小距离/20","没有任何敌人则2；本人不在场时从(0,0)计算")
assert index == len(s) == 1805
part, index = "candidate", 0
command_labels = dict(BeginPrimary="启动主要行动",CancelCardSelection="撤销本地选牌流程中的已选牌",
    ChooseAttackTarget="选择攻击目标",ChooseCardSwap="选择交换卡牌",ChooseDiscardAttack="选择弃牌攻击",
    ChooseEffectMove="选择效果移动落点",ChooseEffectTarget="选择效果目标",ChooseGoldTransfer="选择金币转移",
    ChooseHero="选择英雄",ChooseInitiative="选择先攻行动者",ChooseMinionProtection="选择小兵保护牌",
    ChooseMinionReturn="选择小兵返回",ChooseMinionSpawn="选择小兵生成",ChooseOptionalDiscard="选择可选弃牌",
    ChoosePlacement="选择放置位置",ChoosePrimaryOption="选择主要行动分支",ChooseRecoveredCard="选择取回卡牌",
    ChooseRoundMinionRemoval="选择兵战移除小兵",ChooseUpgrade="选择升级卡牌",CommitPrimaryAttack="提交预选主要攻击",
    CommitPrimaryMove="提交预选主要移动",CommitPrimaryPlacement="提交预选主要放置",ConfirmCard="确认暗选",
    DeclineDefense="放弃防御",DeclineRetaliationDiscard="放弃反击相关弃牌支付",Defend="使用卡牌防御",
    DeployHero="部署英雄",ForcedDiscard="提交强制弃牌",MarkCoinStuck="报告物理硬币卡住（环境命令）",
    Move="移动或启动移动前置效果",Pass="结束本次行动",ReportCoinToss="报告物理投币结果（环境命令）",
    ResolveRoundEnd="推进轮末结算",RespawnHero="选择英雄复活位置",SelectCard="选择手牌",
    SetQuickSelection="设置快速选牌",StartDraft="开始选英雄",UpgradeEngine="升级存档规则引擎版本",
    VoteCoinReroll="投票重新投币（环境流程）")
if set(enc.kinds) != set(command_labels):
    raise ValueError("action vocabulary changed; update documented meanings explicitly")
for kind in enc.kinds:
    add("命令类型","kind."+kind,"此候选是否为："+command_labels[kind],"相等为1，否则0","词表有该命令不代表策略在当前局面可选择")
for cid in enc.card_ids:
    add("候选卡牌","card."+cid,"此候选Value是否为"+cid,"相等为1，否则0","Value不是卡牌ID则108项全0")
for b in range(64):
    add("稳定值字节",f"value_byte.{b}",f"Value UTF-8字节序列第{b}位（0起）","字节值/255","不足64字节补0，超过64直接报错，不截断")
for family in enc.families:
    add("牌面主类型","family."+family,"Value指向的卡牌PrimaryFamily是否为"+family,"相等为1，否则0","非卡牌动作全0；不代表理解牌文")
for name,meaning,formula,missing in [
    ("destination_nearest_enemy","落点到最近敌方单位","最小hex距离/20","无落点则0；无敌人则2"),
    ("destination_nearest_nonhero","落点到最近敌方非英雄单位","无非英雄则回退所有敌方；最小hex距离/20","无落点则0；无敌人则2"),
    ("move_distance","落点到本人当前位置","hex距离/20","无落点或本人不在场则0"),
    ("target_distance","Value指定的目标单位到本人","hex距离/20","非单位目标或本人不在场则0")]:
    add("候选距离",name,meaning,formula,missing)
for name,meaning,formula,missing in [
    ("successful_defense","核心公开查询确认此防御可成功","SuccessfulDefense真1假0","非防御候选通常为0"),
    ("immediate_skip","核心预览：启动主要行动会立即跳过","ImmediateSkip真1假0","非BeginPrimary通常为0"),
    ("has_destination","候选是否包含落点","HasDestination真1假0","不适用"),
    ("destination_x","落点X","Destination.X/20","没有落点则0"),
    ("destination_y","落点Y","Destination.Y/20","没有落点则0"),
    ("fast_mode","是否快速移动模式","Mode==Fast为1，否则0","没有此模式也为0"),
    ("skip_value","是否可选窗口的skip分支","Value==skip为1，否则0","不是Pass命令判断"),
    ("target_friendly","Value指定的目标单位是否己方","单位存在且Team==己方为1","非单位目标则0"),
    ("target_exists","Value是否匹配在场单位ID","匹配为1，否则0","不适用"),
    ("destination_combat_region","落点是否属于当前交战区","落点格存在且Region==CombatRegion为1","无落点/未找到格子则0"),
    ("primary_value","Value指向卡牌的静态主要数值","PrimaryValue/10","非卡牌、缺失或0均编码0"),
    ("secondary_defense","卡牌静态次要防御","SecondaryDefense/10","非卡牌、null与数值0均编码0"),
    ("secondary_movement","卡牌静态次要移动","SecondaryMovement/10","非卡牌、null与数值0均编码0"),
    ("initiative","卡牌静态先攻值","Initiative/10","非卡牌/缺失则0")]:
    add("候选标志与数值",name,meaning,formula,missing)
for kind in KINDS:
    add("目标兵种","target_kind."+kind,"Value指定目标单位的Kind是否为"+kind,"相等为1，否则0","非单位目标则全0")
for name,meaning,formula,missing in [
    ("target_dx","目标单位相对本人X坐标差","(target.X-self.X)/20","非单位目标则0；本人不在场时self=(0,0)"),
    ("target_dy","目标单位相对本人Y坐标差","(target.Y-self.Y)/20","非单位目标则0；本人不在场时self=(0,0)"),
    ("target_seat_self","TargetSeat是否本人","TargetSeat==Seat为1","未指定席位为0"),
    ("target_seat_friendly","TargetSeat是否属于己方（包含本人）","存在该玩家且其Team==己方为1","未指定/非法席位为0")]:
    add("目标关系",name,meaning,formula,missing)
assert index == candidates.shape[1] == 242
assert len({(r['part'],r['name']) for r in rows}) == len(rows) == 2047
args.output.mkdir(parents=True, exist_ok=False)
with (args.output / "features.csv").open("w",encoding="utf-8-sig",newline="") as f:
    writer=csv.DictWriter(f,fieldnames=list(rows[0]));writer.writeheader();writer.writerows(rows)
(args.output / "features.json").write_text(json.dumps(rows,ensure_ascii=False,indent=2),encoding="utf-8")
blocks=[]
for row in rows:
    if not blocks or (blocks[-1]['part'],blocks[-1]['group']) != (row['part'],row['group']):
        blocks.append(dict(part=row['part'],group=row['group'],first=row['index'],last=row['index'],count=1))
    else:blocks[-1]['last']=row['index'];blocks[-1]['count']+=1
manifest=dict(source_commit=subprocess.check_output(['git','rev-parse','HEAD'],cwd=root,text=True).strip(),
              encoder_version=ENCODER_VERSION,observation_version=3,state_dimensions=len(s),candidate_dimensions=candidates.shape[1],
              candidate_example_id=d['Actions'][0]['Id'],blocks=blocks,
              meaning="Per-coordinate explanation and real encoded example; not an observation-completeness claim.",
              sources={p.relative_to(root).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in
                       (catalog_path,input_path,root/'ai/trainer/policy.py',Path(__file__))})
(args.output/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(dict(state=len(s),candidate=candidates.shape[1],rows=len(rows),blocks=blocks),ensure_ascii=False,indent=2))
