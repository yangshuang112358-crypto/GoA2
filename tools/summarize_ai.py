"""Summarize existing immutable runs; never execute a policy or alter evidence."""
import argparse
import datetime as dt
import json
import statistics
from pathlib import Path


def summarize(path):
    path = Path(path)
    manifest = json.loads((path / 'manifest.json').read_text(encoding='utf-8-sig'))
    summary_path = path / 'summary.json'
    summary = json.loads(summary_path.read_text(encoding='utf-8-sig'))
    results = summary['Results']
    completed = [r['Seconds'] for r in results if r['Stop'] == 'terminated']
    start = dt.datetime.fromisoformat(manifest['StartedUtc'].replace('Z', '+00:00')).timestamp()
    # Existing v1 collector times gameplay separately. File mtime provides an explicitly labelled wall-time estimate.
    wall = summary.get('VerifiedPipelineSeconds', summary_path.stat().st_mtime - start)
    decisions = sum(r['Decisions'] for r in results)
    return dict(directory=str(path), source_commit=manifest['SourceCommit'], matchup=manifest['Configuration']['Matchup'],
                games=len(results), completed=len(completed), truncated=sum(r['Stop'] == 'truncated' for r in results),
                failed=sum(r['Stop'] == 'failure' for r in results), decisions=decisions,
                illegal_commands=sum(r['IllegalCommands'] for r in results), exceptions=sum(r['Exceptions'] for r in results),
                gameplay_seconds=sum(r['Seconds'] for r in results), decisions_per_second=summary['DecisionsPerSecond'],
                completed_seconds=completed, completed_median_seconds=statistics.median(completed) if completed else None,
                wall_seconds=wall, wall_measurement='stopwatch' if 'VerifiedPipelineSeconds' in summary else 'artifact_timestamp_estimate',
                verified_pipeline_decisions_per_second_estimate=decisions / wall if wall > 0 else None,
                average_process_cpu_percent=summary['AverageCpuPercent'], peak_working_set_mib=summary['PeakWorkingSetMiB'],
                terminated_rate=summary['CompleteRate'], truncated_rate=summary['TruncationRate'],
                exception_rate=summary['ExceptionRate'], illegal_command_rate=summary['IllegalCommandRate'])


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('runs', nargs='+', type=Path)
    options = parser.parse_args()
    print(json.dumps([summarize(p) for p in options.runs], ensure_ascii=False, indent=2))
