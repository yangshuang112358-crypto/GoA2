param([ValidateRange(1152,3840)][int]$Width=1280,[ValidateRange(768,2160)][int]$Height=800,[switch]$StartupOnly)
$ErrorActionPreference='Stop'
$goaRoot=Split-Path -Parent $PSScriptRoot
$goaChecks=[Collections.Generic.List[object]]::new()
$goaStarted=[DateTime]::UtcNow
$goaOutput=Join-Path $goaRoot ('artifacts/recovery-ui/'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $goaOutput | Out-Null
$goaSave=Join-Path $goaRoot 'artifacts/unity/qa-save.json'
$goaTemporary=$goaSave+'.tmp'
$goaOwnDirectory=$false
. "$PSScriptRoot/ui-qa-common.ps1"
function Wait-RecoveryPhase([string]$Phase,[long]$Revision) {
    $goaDeadline=[DateTime]::UtcNow.AddSeconds(15)
    do {
        Start-Sleep -Milliseconds 100
        try { $goaUi=Read-Ui } catch { continue }
        if ($goaUi.Phase -eq $Phase -and $goaUi.Revision -eq $Revision) { return $goaUi }
    } while ([DateTime]::UtcNow -lt $goaDeadline)
    throw "The Player did not display $Phase at revision $Revision."
}
function Remove-OwnedFaultDirectory {
    if (-not $script:goaOwnDirectory) { return }
    $goaResolved=(Resolve-Path -LiteralPath $goaTemporary).Path
    $goaBoundary=[IO.Path]::GetFullPath((Join-Path $goaRoot 'artifacts/unity'))+[IO.Path]::DirectorySeparatorChar
    if (-not $goaResolved.StartsWith($goaBoundary,[StringComparison]::OrdinalIgnoreCase) -or @(Get-ChildItem -LiteralPath $goaResolved -Force).Count -ne 0) { throw 'The injected empty directory changed; leave it for inspection.' }
    Remove-Item -LiteralPath $goaResolved
    $script:goaOwnDirectory=$false
}
try {
    Close-QaPlayer
    & "$PSScriptRoot/run-player.ps1" -Qa -Width $Width -Height $Height
    $null=Wait-RecoveryPhase 'HeroSelection' 0
    Click '^调试$'; Click '^自动选英雄与出生$'
    $goaState=Save-State; $goaBaseline=[IO.File]::ReadAllText($goaSave)
    Copy-Item -LiteralPath $goaSave -Destination (Join-Path $goaOutput 'baseline.save.json')
    if (-not $StartupOnly) {
        Click '^新对局$'; & "$PSScriptRoot/qa-player.ps1" -Action Key -Key 4 | Out-Null
        Check ((Read-Ui).Seat -eq 0) 'New-match confirmation blocks seat shortcuts behind the dialog'
        Click '^继续当前对局$'; $null=Save-State
        Check ([IO.File]::ReadAllText($goaSave) -ceq $goaBaseline) 'Cancelling a new match preserves the entire current save'
        foreach ($goaFault in @('invalid-json','changed-gold','future-engine','wrong-content')) {
            $goaBad=$goaBaseline | ConvertFrom-Json
            switch ($goaFault) {
                'changed-gold' { $goaBad.Players[0].Gold=999 }
                'future-engine' { $goaBad.EngineVersion=999 }
                'wrong-content' { $goaBad.ContentHash=('0'*64) }
            }
            $goaBadText=if ($goaFault -eq 'invalid-json') { '{broken' } else { $goaBad | ConvertTo-Json -Depth 30 }
            [IO.File]::WriteAllText($goaSave,$goaBadText,[Text.UTF8Encoding]::new($false))
            Copy-Item -LiteralPath $goaSave -Destination (Join-Path $goaOutput ($goaFault+'.json'))
            Click '^读取$'
            Check ((Read-Ui).Revision -eq $goaState.Revision -and [IO.File]::ReadAllText($goaSave) -ceq $goaBadText) ($goaFault+': rejected load does not replace the match or rewrite its source')
            & "$PSScriptRoot/qa-player.ps1" -Action Capture -Name ('recovery-'+$goaFault) | Out-Null
            $null=Save-State
            Check ([IO.File]::ReadAllText($goaSave) -ceq $goaBaseline) ($goaFault+': the previous complete in-memory match remains saveable')
        }
        Move-Item -LiteralPath $goaSave -Destination (Join-Path $goaOutput 'displaced.save.json')
        Click '^读取$'
        Check ((Read-Ui).Revision -eq $goaState.Revision -and -not (Test-Path -LiteralPath $goaSave)) 'A missing save does not reset the current match or create a replacement'
        $null=Save-State
        Check ([IO.File]::ReadAllText($goaSave) -ceq $goaBaseline) 'The current match can be saved again after a missing-file error'
        if (Test-Path -LiteralPath $goaTemporary) { throw 'The temporary save path is already occupied; do not overwrite it.' }
        New-Item -ItemType Directory -Path $goaTemporary | Out-Null; $goaOwnDirectory=$true
        Click '^新对局$'; Click '^保存并开始$'
        Check ((Read-Ui).Revision -eq $goaState.Revision -and [IO.File]::ReadAllText($goaSave) -ceq $goaBaseline) 'Failed saving prevents a new match and preserves the previous file'
        Check (@((Read-Ui).Labels | Where-Object { $_.Name -eq 'new-match-save-error' -and $_.Text -match '保存失败' -and $_.Visible }).Count -eq 1) 'The new-match dialog visibly explains a save failure'
        & "$PSScriptRoot/qa-player.ps1" -Action Capture -Name 'recovery-save-failure' | Out-Null
        Remove-OwnedFaultDirectory
        Click '^保存并开始$'
        $null=Wait-RecoveryPhase 'HeroSelection' 0
        Check ([IO.File]::ReadAllText($goaSave) -ceq $goaBaseline) 'Retrying after the fault saves the previous match before starting a new one'
    }
    Close-QaPlayer
    $goaStartupFile=Join-Path $goaOutput 'startup-broken.json'
    [IO.File]::WriteAllText($goaStartupFile,'{broken',[Text.UTF8Encoding]::new($false))
    & "$PSScriptRoot/run-player.ps1" -Qa -Width $Width -Height $Height -LoadSave $goaStartupFile
    $null=Wait-RecoveryPhase 'StartupError' 0
    Check (@((Read-Ui).Buttons | Where-Object { $_.Name -eq 'startup-retry' -and $_.Enabled -and $_.Visible }).Count -eq 1) 'A bad startup save offers a visible retry action'
    Check ([IO.File]::ReadAllText($goaStartupFile) -ceq '{broken') 'Startup recovery leaves the failed source untouched'
    & "$PSScriptRoot/qa-player.ps1" -Action Capture -Name 'recovery-startup-failure' | Out-Null
    [IO.File]::WriteAllText($goaStartupFile,$goaBaseline,[Text.UTF8Encoding]::new($false))
    Click -Element 'startup-retry'
    $null=Wait-RecoveryPhase 'Planning' $goaState.Revision
    $null=Save-State
    Check ([IO.File]::ReadAllText($goaSave) -ceq $goaBaseline) 'Retry loads the repaired source through complete rule replay'
    Close-QaPlayer
    [IO.File]::WriteAllText($goaStartupFile,'{broken',[Text.UTF8Encoding]::new($false))
    & "$PSScriptRoot/run-player.ps1" -Qa -Width $Width -Height $Height -LoadSave $goaStartupFile
    $null=Wait-RecoveryPhase 'StartupError' 0
    Click -Element 'startup-new'
    $null=Wait-RecoveryPhase 'HeroSelection' 0
    Check ([IO.File]::ReadAllText($goaStartupFile) -ceq '{broken' -and [IO.File]::ReadAllText($goaSave) -ceq $goaBaseline) 'Starting fresh after a failed load preserves the source and prior saved match'
} catch {
    $goaChecks.Add([pscustomobject]@{check='Execution';passed=$false;error=$_.Exception.Message})
    throw
} finally {
    try { & "$PSScriptRoot/qa-player.ps1" -Action Capture -Name 'recovery-final' | Out-Null } catch { }
    Get-ChildItem -LiteralPath (Join-Path $goaRoot 'artifacts/unity') -File | Where-Object { $_.LastWriteTimeUtc -ge $goaStarted -and ($_.Name -like 'recovery-*.png' -or $_.Name -in @('render-latest.ui.json','player.log')) } | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $goaOutput $_.Name) }
    Remove-OwnedFaultDirectory
    $goaReport=[pscustomobject]@{startedUtc=$goaStarted.ToString('o');finishedUtc=[DateTime]::UtcNow.ToString('o');startupOnly=[bool]$StartupOnly;checks=$goaChecks;evidence=$goaOutput}
    $goaReport | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $goaOutput 'report.json') -Encoding utf8
    $goaReport | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $goaRoot "artifacts/unity/recovery-ui-${Width}x${Height}.json") -Encoding utf8
    Write-Output "Recovery UI evidence: $goaOutput"
}
