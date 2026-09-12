param([ValidateRange(1152,3840)][int]$Width=1280,[ValidateRange(768,2160)][int]$Height=800)
$ErrorActionPreference='Stop'
$goaRoot=Split-Path -Parent $PSScriptRoot
$goaChecks=[Collections.Generic.List[object]]::new()
$goaStarted=[DateTime]::UtcNow
. "$PSScriptRoot/ui-qa-common.ps1"
function Gallery-Count { [int]((Read-Ui).Labels | Where-Object Name -eq 'gallery-count').Text.Split(' ')[1] }
function Type-Gallery([string]$Query) {
    $goaField=(Read-Ui).TextFields | Where-Object Name -eq 'gallery-query'
    if (-not $goaField) { throw 'The gallery search field is missing.' }
    & "$PSScriptRoot/qa-player.ps1" -Action Click -X ([int]($goaField.Bounds.x+$goaField.Bounds.width-30)) -Y ([int]($goaField.Bounds.y+$goaField.Bounds.height/2)) | Out-Null
    & "$PSScriptRoot/qa-text.ps1" -Text $Query | Out-Null
}
try {
    Close-QaPlayer
    & "$PSScriptRoot/run-player.ps1" -Qa -Width $Width -Height $Height
    $goaDeadline=[DateTime]::UtcNow.AddSeconds(15)
    do { Start-Sleep -Milliseconds 150;try { $goaUi=Read-Ui } catch {continue} } while ($goaUi.Phase -ne 'HeroSelection' -and [DateTime]::UtcNow -lt $goaDeadline)
    Click '^调试$';Click '^自动选英雄与出生$'
    $null=Save-State;$goaBaseline=[IO.File]::ReadAllText((Join-Path $goaRoot 'artifacts/unity/qa-save.json'))
    Click '^图鉴 108$'
    Check (@((Read-Ui).TextFields | Where-Object Name -eq 'gallery-query').Count -eq 1) 'The in-game gallery exposes a search field'
    Check ((Gallery-Count) -eq 18) 'The initial hero tab shows all eighteen cards'
    Click -Element 'gallery-all'
    Check ((Gallery-Count) -eq 108) 'All heroes can be searched together'
    Click -Element 'gallery-supported'
    Check ((Gallery-Count) -eq 23) 'The current engine exposes exactly its twenty-three supported cards'
    Click -Element 'gallery-supported'
    Type-Gallery '飞斧'
    Check ((Gallery-Count) -eq 1 -and @((Read-Ui).Labels | Where-Object { $_.Name -like 'gallery-card-name-*' -and $_.Visible -and $_.Text -eq '投掷飞斧' }).Count -eq 1) 'Real Unicode keyboard input finds the throwing axe without truncating its name'
    Click -Element 'gallery-clear'
    Type-Gallery '13'
    Check ((Read-Ui).Seat -eq 0 -and ((Read-Ui).TextFields | Where-Object Name -eq 'gallery-query').Value -eq '13') 'Typing numbers in search keeps the same controlled character and input focus'
    Check ((Gallery-Count) -eq 6) 'Searching the card ID fragment finds one matching card per hero'
    Click -Element 'gallery-clear'
    Type-Gallery '不存在的牌'
    Check ((Gallery-Count) -eq 0 -and @((Read-Ui).Labels | Where-Object { $_.Name -eq 'gallery-empty' -and $_.Visible }).Count -eq 1) 'An empty search has a clear message'
    Click -Element 'gallery-clear'
    Click -Element 'gallery-hero-brogan'
    Check ((Gallery-Count) -eq 18) 'Hero switching continues to work after clearing search'
    & "$PSScriptRoot/qa-player.ps1" -Action Capture -Name 'gallery-search' | Out-Null
    Click -Element 'gallery-return'
    $null=Save-State
    Check ([IO.File]::ReadAllText((Join-Path $goaRoot 'artifacts/unity/qa-save.json')) -ceq $goaBaseline) 'All gallery operations leave the complete match unchanged'
    Open-Save (Join-Path $goaRoot 'tests/fixtures/legacy-v1-roundend.json') 'RoundEnd' 59
    Click '^图鉴 108$';Click -Element 'gallery-all';Click -Element 'gallery-supported'
    Check ((Gallery-Count) -eq 5) 'An engine-zero save lists only its five original supported cards'
} catch { $goaChecks.Add([pscustomobject]@{check='Execution';passed=$false;error=$_.Exception.Message});throw }
finally {
    [pscustomobject]@{startedUtc=$goaStarted.ToString('o');finishedUtc=[DateTime]::UtcNow.ToString('o');checks=$goaChecks} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $goaRoot "artifacts/unity/gallery-ui-${Width}x${Height}.json") -Encoding utf8
    Write-Output "Gallery UI report: artifacts/unity/gallery-ui-${Width}x${Height}.json"
}
