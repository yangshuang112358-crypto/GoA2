"""Build a self-contained, read-only browser from the verified project sources."""
from pathlib import Path
import argparse
import datetime
import json

from validate import ROOT, hashfile, inside, read, validate


DOCUMENTS = [
    ('rules', '规则手册', 'docs/rules/规则手册.md'),
    ('decisions', '裁定记录', 'docs/rules/裁定记录.md'),
    ('questions', '待确认规则', 'docs/rules/待确认规则.md'),
    ('lessons', '项目复盘', 'docs/history/项目复盘.md'),
    ('controls', '启动与操作', 'docs/development/启动与操作指南.md'),
]


def build(root, output):
    root = root.resolve()
    validation = validate(root)
    paths = [
        'content/manifest.json', 'content/canonical/cards.json',
        'content/canonical/heroes.json', 'content/canonical/map.json',
        'content/canonical/ruleset.json', 'content/status/cards.json',
        'tools/data-browser.html', 'tools/build-data-browser.py',
    ]
    statuses = read(root / 'content/status/cards.json')['cards']
    contracts = {}
    for status in statuses:
        if status['contract']:
            path = status['contract']
            contracts[status['card_id']] = inside(root, path).read_text(encoding='utf-8')
            paths.append(path)
    documents = []
    for key, title, path in DOCUMENTS:
        documents.append(dict(id=key, title=title, path=path, text=inside(root, path).read_text(encoding='utf-8')))
        paths.append(path)
    data = dict(
        schema_version=1,
        built_utc=datetime.datetime.now(datetime.timezone.utc).isoformat(),
        manifest=read(root / 'content/manifest.json'),
        heroes=read(root / 'content/canonical/heroes.json')['heroes'],
        cards=read(root / 'content/canonical/cards.json')['cards'],
        cells=read(root / 'content/canonical/map.json')['cells'],
        statuses=statuses, contracts=contracts, documents=documents,
        sources=[dict(path=path, sha256=hashfile(inside(root, path))) for path in sorted(set(paths))],
    )
    # The JSON script is inert data. Escape all '<' to prevent a source string
    # from terminating it; user-visible text is inserted with textContent.
    payload = json.dumps(data, ensure_ascii=False, separators=(',', ':')).replace('<', '\\u003c')
    template = (root / 'tools/data-browser.html').read_text(encoding='utf-8')
    if template.count('__GOA2_DATA__') != 1:
        raise ValueError('The data browser template must contain one data placeholder.')
    output = output.resolve()
    if output.is_relative_to(root) and not output.is_relative_to(root / 'artifacts'):
        raise ValueError('Generated output inside the repository must stay under artifacts.')
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(template.replace('__GOA2_DATA__', payload), encoding='utf-8', newline='\n')
    report = dict(result='PASS', output=str(output), sha256=hashfile(output),
                  heroes=len(data['heroes']), cards=len(data['cards']), cells=len(data['cells']),
                  implemented=sum(s['status'] == 'implemented' for s in statuses),
                  source_files=len(data['sources']), validation=validation['result'])
    output.with_suffix('.json').write_text(json.dumps(report, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    return report


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=ROOT)
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    destination = args.output or args.root / 'artifacts/reference/Goa2V1-资料浏览器.html'
    print(json.dumps(build(args.root, destination), ensure_ascii=False, indent=2))
