"""Generate read-only card views and review drafts from canonical content."""
from pathlib import Path
import json

ROOT = Path(__file__).resolve().parents[1]

def outputs(root=ROOT):
    cards = json.loads((root / 'content/canonical/cards.json').read_text(encoding='utf-8'))['cards']
    heroes = json.loads((root / 'content/canonical/heroes.json').read_text(encoding='utf-8'))['heroes']
    statuses = {s['card_id']: s for s in json.loads((root / 'content/status/cards.json').read_text(encoding='utf-8'))['cards']}
    out = {}
    index = []
    keywords = {
        'move': ['移动'], 'push': ['推动'], 'place': ['放置'], 'swap': ['换位', '交换'],
        'discard': ['丢弃', '弃置'], 'retrieve': ['取回', '拿回'], 'gold': ['金币'],
        'repeat': ['重复'], 'conditional': ['如果', '否则'], 'choice_wording': ['最多', '可以', '如果可能'],
        'immunity': ['免疫'], 'continuous': ['此回合', '本轮', '下回合', '持续', '激活'],
        'trigger': ['攻击前', '攻击后', '防御后', '执行', '每当'], 'marker': ['标记', '标志物'],
    }
    for hero in heroes:
        hc = [c for c in cards if c['hero_id'] == hero['hero_id']]
        lines = ['# ' + hero['name'] + '：卡牌资料', '', '自动生成的正式数据阅读视图。勿手工编辑；实现证据统一记录在content/status/cards.json。', '']
        for c in hc:
            status = statuses[c['id']]
            a, s = c['primary_action'], c['secondary_actions']
            facts = [f"- ID：{c['id']}", f"- 颜色 / 卡牌等级 / 先攻：{c['color_key']} / {c['level']} / {c['initiative']}",
                     f"- 主要行动：{a['category']} {a['value']}；感叹号：{a['exclamation']}",
                     f"- 次要移动：{s['movement']['value']}；次要防御：{s['defense']['value']}",
                     f"- 范围 / 远程：{json.dumps(a['subtype'], ensure_ascii=False)}",
                     f"- 底部被动图标：{c['passive_bonus']['type']}"]
            lines += ['## ' + c['name'], ''] + facts + ['', a['text'], '']
            tags = [tag for tag, words in keywords.items() if any(w in a['text'] for w in words)]
            draft = 'docs/cards/drafts/' + c['id'] + '.md'
            questions = ['U-011']
            if 'choice_wording' in tags: questions.append('U-007')
            if 'marker' in tags: questions.append('U-004')
            if 'repeat' in tags and c['color_key'] == 'purple': questions.append('U-008')
            index.append({'card_id':c['id'],'hero_id':c['hero_id'],'family':a['family'],'keyword_hints':tags,
                          'analysis_status':'not_specified' if status['contract'] is None else 'specified','draft':draft,'question_candidates':questions})
            out[draft] = '\n'.join(['# ' + c['name'] + '：规格准备稿', '',
                '自动生成。仅保留输入与分析检查点，不代表合同已确定；请在独立正式合同中完成分析。', ''] + facts + [
                '', '## 正式原文', '', a['text'], '', '## 分析检查点', '',
                '- 关键词候选：' + ('、'.join(tags) or '无关键词提示，仍需完整阅读'),
                '- 相关问题候选：' + '、'.join(questions) + '（逐张判断是否适用）',
                '- 需拆分主要行动、防御响应、持续/紫卡与次要行动；目前未拆成权威元动作。',
                '- 每步填写时机、强制性、选择者、单位种类/阵营/距离、排除/免疫、无目标分支、事件、来源、持续期。',
                '- 不依据关键字直接判定语义，不凭旧版实现跳过用户裁定。',
                '', '## 验收准备', '',
                '- 建立正常、边界、无目标、强制/可选、选择者、空资源、免疫/占位、重复/联动、保存恢复、非法命令与私有投影矩阵。',
                '- 每项必须有初态、命令、事件、终态与禁止副作用；不适用写理由。',
                '- 下一小任务：完成该牌合同及实质疑问，再实现一个最小必要原语。',
                '- 当前状态：' + status['status'] + '；合同与实际测试证据见content/status/cards.json。', ''])
        out['docs/data/cards/' + hero['hero_id'] + '.md'] = '\n'.join(lines)
    out['docs/data/卡牌机制索引.json'] = json.dumps({'notice':'Keyword hints only; not effect semantics or completion evidence.','cards':index},ensure_ascii=False,indent=2)+'\n'
    return out

if __name__ == '__main__':
    for name, text in outputs().items():
        p = ROOT / name
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(text.rstrip() + '\n', encoding='utf-8', newline='\n')
    print('Generated 6 hero catalogs, 108 review drafts and the mechanism index.')
