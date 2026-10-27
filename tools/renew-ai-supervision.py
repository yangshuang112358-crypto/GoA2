"""External inspector entry point; never scheduled by the local supervisor.

Uses the frozen supervisor's validation and atomic renewal implementation.
Kept separate so an operator CLI fix does not alter a live training snapshot.
"""
import argparse
import json
from pathlib import Path
import subprocess

from ai_quota_watch import ROOT, renew


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--expected-pid', type=int, required=True)
    parser.add_argument('--expected-created', type=float, required=True)
    args = parser.parse_args()
    branch = subprocess.check_output(['git', 'branch', '--show-current'], cwd=ROOT, text=True).strip()
    if branch != 'feature/ai-training':
        raise ValueError('renew only in the independent AI branch')
    lease = renew(args.output.resolve(), args.expected_pid, args.expected_created)
    print(json.dumps(lease, ensure_ascii=True))


if __name__ == '__main__':
    main()
