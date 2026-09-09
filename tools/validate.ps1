param([string]$PythonExe = 'python')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
& $PythonExe -B (Join-Path $PSScriptRoot 'validate.py') --root $projectRoot
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $PythonExe -B -m unittest discover -s (Join-Path $projectRoot 'tests') -p 'test_*.py' -v
exit $LASTEXITCODE
