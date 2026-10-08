"""Export documentation from the C# public catalog; contains no executable card rules."""
import argparse
import csv
import json
from pathlib import Path

PARAMETERS = {
    'AttackSubtype':'本卡攻击步骤的近战/远程类型',
    'GoldMaximum':'一次转移金币的上限，实际仍受目标金币数限制',
    'RearTargetDistance':'相对原攻击目标检查后方英雄的距离',
    'IgnoreHeavyImmunity':'该小兵选择步骤是否忽略重型小兵免疫',
    'UnitSwapTarget':'换位目标的阵营、单位类型和范围条件',
    'PlacementTarget':'自身放置的目的格规则',
    'PushTarget':'选择推动目标时允许的单位类型',
    'OptionalPushTarget':'该推动选择是否允许不选目标',
    'FixedPushDistance':'推动距离固定还是可选择上限以内距离',
    'TargetMoveKeepsDistance':'移动目标时是否保持与来源的距离',
    'MinimumDistance':'攻击/主要防御适用的最小双方距离',
    'Effect':'施加的持续效果类型，连接效果定义',
    'Duration':'持续期类型；具体起止轮次在效果实例中',
    'AreaKind':'效果区域的计算方式，实际覆盖格由规则查询产生',
    'AdjacentAttack':'攻击是否限定相邻目标',
    'OnlyHeroes':'攻击目标是否只允许英雄',
    'AttackBonusKind':'牌文攻击加成的条件类型',
    'AttackBonusValue':'该牌文攻击加成的参数值',
    'RangeBonusKind':'牌文攻击距离加成的条件类型',
    'RangeBonusValue':'该牌文距离加成的参数值',
    'TextMoveDistance':'牌文移动的距离参数；实际预算由对应步骤决定',
    'TextMoveMinimum':'需要按牌文最短距离筛选路线时的下限',
    'RecoveryRequiresAdjacentMinion':'取回步骤是否要求与小兵相邻',
    'HeroTarget':'英雄目标选择条件；None表示无需另选英雄',
    'RecoverResolved':'取回是否还允许已结算区，而非仅弃牌区',
    'SupportMakesUnblockable':'满足友方支援条件是否使攻击无法抵挡',
    'ExcludeStraightLine':'攻击目标是否排除六角直线上的单位',
    'ExtraRemovalUsesAttackRange':'追加小兵移除使用攻击距离还是相邻范围',
    'TextPushDistance':'牌文推动的距离参数',
    'Block':'主要防御是否使用抵挡规则',
    'IgnoresMinions':'该主要防御是否忽略小兵计算',
    'RequiresAdjacentFriendlyMinion':'主要防御是否要求相邻友方小兵',
    'SwapAfterMove':'主要防御是否带后续换牌步骤',
    'ProtectFromOtherEnemies':'防御是否带对其他敌方行动的免疫',
    'ProtectFromOtherAttacks':'防御是否带对其他攻击的免疫',
    'PersistImmunityThroughDefeat':'该防御免疫是否跨过来源被击败保留',
    'AttackKind':'该主要防御适用的来袭攻击类型',
    'Followup':'防御后的弃牌、击败或可选直线移动流程',
    'Trigger':'紫卡触发时点和流程类别',
    'BasicAttackBonus':'紫卡对基础攻击的常驻加成',
    'BasicAttackRangeBonus':'紫卡对基础攻击距离的常驻加成',
    'TraverseObstacles':'紫卡是否允许穿越障碍，终点仍须合法',
    'BattleMinionCount':'紫卡在兵战中计作的小兵数量',
}


def main():
    p=argparse.ArgumentParser()
    p.add_argument('--catalog',type=Path,required=True)
    p.add_argument('--output',type=Path,required=True)
    args=p.parse_args()
    root=Path(__file__).resolve().parents[1]
    d=json.loads(args.catalog.read_text(encoding='utf-8-sig'))
    source=json.loads((root/'content/canonical/cards.json').read_text(encoding='utf-8'))['cards']
    names={'-'.join(c['id'].split('-')[:2]):c for c in source}
    keys={p['Key'] for c in d['Cards'] for p in c['Mechanics']['Parameters']}
    if keys-set(PARAMETERS):raise ValueError('undocumented parameters:'+str(keys-set(PARAMETERS)))
    args.output.mkdir(parents=True,exist_ok=False)
    def csv_file(name,rows):
        with (args.output/name).open('w',encoding='utf-8-sig',newline='') as f:csv.writer(f).writerows(rows)
    csv_file('parameters.csv',[['参数','中文含义']]+[[k,PARAMETERS[k]] for k in sorted(keys)])
    for kind,name in [('StepDefinitions','steps'),('EffectDefinitions','effects')]:
        fields=list(d[kind][0]);csv_file(name+'.csv',[fields]+[[row[k] for k in fields] for row in d[kind]])
    index=['# 当前卡牌语义目录','',
        '由C#公开查询导出，供人工核对。中文名称和牌文只用于阅读，不进入模型。模型使用稳定ID、类型化参数、有序步骤、效果定义及约束代码。',
        '',f"卡牌 {len(d['Cards'])}；步骤定义 {len(d['StepDefinitions'])}；效果定义 {len(d['EffectDefinitions'])}。",'',
        '[参数中文含义](parameters.csv) · [步骤表](steps.csv) · [持续效果表](effects.csv)','',
        '参数只在对应步骤/分支执行时适用；不存在的分支不代表拥有该能力。false、0与未提供参数不同。',
        '步骤是描述，不在Python执行。未知牌文、机制字段、事件或词表项必须报错；卡牌名称相近不代表规则可复用。','']
    for hero in sorted(d['Heroes']):
        lines=[f'# {hero} 的公开卡牌语义','', '[返回目录](README.md)','']
        for c in sorted((c for c in d['Cards'] if c['HeroId']==hero),key=lambda c:c['Id']):
            original=names[c['Id']];m=c['Mechanics']
            lines += [f"## {c['Id']} · {original['name']}",'',original['primary_action']['text'],'',
                '**有序步骤**：'+' → '.join(m['Steps']),'','| 参数 | 值 | 含义 |','|---|---|---|']
            for param in sorted(m['Parameters'],key=lambda p:p['Key']):
                value=param['Number'] if param['Number'] is not None else param['Flag'] if param['Flag'] is not None else param['Symbol'] or '空'
                lines.append(f"| {param['Key']} | {value} | {PARAMETERS[param['Key']]} |")
            lines+=['','**规则约束**：'+'；'.join(m['Constraints']),'']
            if m['Limitations']:lines+=['**现有未裁定/未扩展边界**：'+'；'.join(m['Limitations']),'']
        (args.output/(hero+'.md')).write_text('\n'.join(lines),encoding='utf-8')
        index.append(f'- [{hero} 的18张卡牌]({hero}.md)')
    (args.output/'README.md').write_text('\n'.join(index)+'\n',encoding='utf-8')
    print(f'{len(keys)} parameter meanings; {len(d["Cards"])} card records; {args.output}')


if __name__=='__main__':main()
