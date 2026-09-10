param(
    [string]$PythonExe,
    [ValidatePattern('^[A-Za-z0-9_-]+$')][string]$Label = 'local'
)
$ErrorActionPreference='Stop'
$goaRoot=Split-Path -Parent $PSScriptRoot
if (-not $PythonExe) {
    $goaBundled=Join-Path $env:USERPROFILE '.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe'
    if (Test-Path -LiteralPath $goaBundled) { $PythonExe=$goaBundled }
    else { $PythonExe='python' }
}
$goaRelease=Join-Path $goaRoot ('artifacts/releases/Goa2V1-'+$Label+'-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8)+'.zip')
$goaJson=& $PythonExe -B (Join-Path $PSScriptRoot 'player_package.py') create (Join-Path $goaRoot 'artifacts/player') $goaRelease --source-root $goaRoot
if ($LASTEXITCODE -ne 0) { throw 'Player packaging failed. Rebuild the current source if its inventory is missing or stale.' }
$goaReport=$goaJson | ConvertFrom-Json
($goaReport.archive_sha256+'  '+[IO.Path]::GetFileName($goaRelease)) | Set-Content -LiteralPath ($goaRelease+'.sha256') -Encoding ascii
$goaJson | Set-Content -LiteralPath ($goaRelease+'.json') -Encoding utf8
Write-Output $goaJson
