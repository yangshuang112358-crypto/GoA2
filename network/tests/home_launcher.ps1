param([Parameter(Mandatory=$true)][string]$Package,[Parameter(Mandatory=$true)][string]$Report)
$ErrorActionPreference='Stop'
. (Join-Path $Package 'launcher/HomeTools.ps1')
$checks=New-Object 'Collections.Generic.List[string]'
function Check([string]$Name,[bool]$Condition) { if(-not $Condition){throw $Name}; $checks.Add($Name) }
$localSpec=Get-GoaHomeLaunch $Package 'Local'
$tutorialSpec=Get-GoaHomeLaunch $Package 'Tutorial'
$networkSpec=Get-GoaHomeLaunch $Package 'Network'
$helpSpec=Get-GoaHomeLaunch $Package 'Help'
Check 'Tutorial and local use the identical packaged Player' ($localSpec.File -eq $tutorialSpec.File -and (Test-Path -LiteralPath $localSpec.File))
Check 'Only tutorial mode selects the tutorial entry' ($tutorialSpec.Arguments.StartsWith('-goaTutorial ') -and -not $localSpec.Arguments.Contains('-goaTutorial'))
Check 'Offline modes contain no network ticket or network bootstrap' (-not (($localSpec.Arguments+$tutorialSpec.Arguments) -match 'goaNetwork|BootstrapWorker|easytier'))
Check 'Relative package resolves after relocation with spaces' ($localSpec.WorkingDirectory -eq (Join-Path (Resolve-Path -LiteralPath $Package).Path 'player'))
Check 'Network invokes bundled automatic launcher' ($networkSpec.Arguments.Contains((Join-Path (Resolve-Path -LiteralPath $Package).Path 'launcher/Launcher.ps1')) -and $networkSpec.Hidden)
Check 'Help opens packaged manual' ($helpSpec.Arguments.Contains((Join-Path (Resolve-Path -LiteralPath $Package).Path 'README.txt')))
Check 'No offline data writes into distribution' ($localSpec.Arguments.Contains((Join-Path $env:LOCALAPPDATA 'Goa2V1/Logs')) -and -not $localSpec.Arguments.Contains((Resolve-Path -LiteralPath $Package).Path))
Check 'Launch targets exist without an SDK path' ((Test-Path $networkSpec.File) -and (Test-Path $helpSpec.File) -and -not $networkSpec.File.Contains('Goa2V1Toolchain'))
[pscustomobject]@{passed=$true;checks=$checks;method='Launch specifications from relocated distribution; actual button clicks verified separately'} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $Report -Encoding UTF8
Write-Output ('PASS '+$checks.Count+' unified-entry checks')
