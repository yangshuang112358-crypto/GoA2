param([Parameter(Mandatory)][string]$Archive,[string]$PythonExe)
$ErrorActionPreference='Stop'
$goaRoot=Split-Path -Parent $PSScriptRoot
$goaArchive=(Resolve-Path -LiteralPath $Archive).Path
if (-not $PythonExe) {
    $goaBundled=Join-Path $env:USERPROFILE '.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe'
    $PythonExe=if(Test-Path -LiteralPath $goaBundled){$goaBundled}else{'python'}
}
$goaVerified=& $PythonExe -B (Join-Path $PSScriptRoot 'player_package.py') verify-archive $goaArchive
if($LASTEXITCODE -ne 0){throw 'Archive verification failed; no files were extracted.'}
$goaOutput=Join-Path $goaRoot ('artifacts/release-content-ui/'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $goaOutput | Out-Null
Expand-Archive -LiteralPath $goaArchive -DestinationPath $goaOutput
$goaPlayer=Join-Path $goaOutput 'Goa2V1'
$goaExe=Join-Path $goaPlayer 'Goa2V1.exe'
$goaCards=(Resolve-Path -LiteralPath (Join-Path $goaPlayer 'Goa2V1_Data/StreamingAssets/Goa2/content/canonical/cards.json')).Path
if(-not $goaCards.StartsWith([IO.Path]::GetFullPath($goaOutput)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Fault injection is outside the isolated extracted copy.'}
$goaBackup=Join-Path $goaOutput 'original-cards.json'
$goaSave=Join-Path $goaOutput 'test-save.json'
$goaHash=(Get-FileHash -LiteralPath $goaArchive).Hash
$goaChecks=[Collections.Generic.List[object]]::new()
$goaStarted=[DateTime]::UtcNow
$goaFailure='';$goaProcess=$null
function Check-Content([bool]$Condition,[string]$Description){
    $goaChecks.Add([pscustomobject]@{check=$Description;passed=$Condition})
    if(-not $Condition){throw $Description};Write-Output "PASS $Description"
}
function Close-ContentPlayer {
    if($goaProcess -and -not $goaProcess.HasExited){
        if($goaProcess.Path -ne $goaExe){throw 'The tracked process path changed.'}
        [void]$goaProcess.CloseMainWindow()
        if(-not $goaProcess.WaitForExit(5000)){throw 'The isolated content-test Player did not close.'}
    }
}
function Start-ContentPlayer([string]$Name,[string]$Phase){
    $goaImage=Join-Path $goaOutput ($Name+'.png')
    $goaArguments='-screen-fullscreen 0 -screen-width 1280 -screen-height 800 -goaScreenshot "'+$goaImage+'" -goaSavePath "'+$goaSave+'" -logFile "'+(Join-Path $goaOutput ($Name+'.log'))+'"'
    $script:goaProcess=Start-Process -FilePath $goaExe -ArgumentList $goaArguments -WorkingDirectory $goaOutput -PassThru
    $goaDeadline=[DateTime]::UtcNow.AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 150
        try {$goaUi=Get-Content -LiteralPath ([IO.Path]::ChangeExtension($goaImage,'.ui.json')) -Raw | ConvertFrom-Json;if($goaUi.Phase -eq $Phase){return $goaUi}}catch{}
    }while([DateTime]::UtcNow -lt $goaDeadline -and -not $goaProcess.HasExited)
    throw 'The isolated Player did not display the expected phase.'
}
try {
    Move-Item -LiteralPath $goaCards -Destination $goaBackup
    $goaErrorUi=Start-ContentPlayer 'missing-content' 'StartupError'
    Check-Content ($goaErrorUi.Phase -eq 'StartupError') 'Missing packaged card data displays a startup error page'
    Check-Content (@($goaErrorUi.Buttons|Where-Object {$_.Name -eq 'startup-quit' -and $_.Enabled -and $_.Visible}).Count -eq 1) 'The error page has a visible enabled exit action'
    Check-Content (@($goaErrorUi.Buttons|Where-Object {$_.Name -in @('startup-new','startup-retry')}).Count -eq 0) 'Missing program content does not offer invalid save-recovery actions'
    Check-Content (-not(Test-Path -LiteralPath $goaSave)) 'Failed startup does not create a replacement match save'
    Close-ContentPlayer
    Move-Item -LiteralPath $goaBackup -Destination $goaCards
    $goaRestored=& $PythonExe -B (Join-Path $PSScriptRoot 'player_package.py') verify-build $goaPlayer
    Check-Content ($LASTEXITCODE -eq 0) 'Restoring the original file returns every packaged byte to its verified inventory'
    $goaRestored | Set-Content -LiteralPath (Join-Path $goaOutput 'restored-inventory.json') -Encoding utf8
    $goaReady=Start-ContentPlayer 'restored-content' 'HeroSelection'
    Check-Content (@($goaReady.Buttons|Where-Object {$_.Name -match '^seat-[1-4]$'}).Count -eq 4) 'The repaired independent copy opens all four normal player seats'
    Check-Content ((Get-FileHash -LiteralPath $goaArchive).Hash -ceq $goaHash -and -not(Test-Path -LiteralPath $goaSave)) 'The source archive remains unchanged and no test save was written'
}catch{$goaFailure=$_.Exception.Message;$goaChecks.Add([pscustomobject]@{check='Execution';passed=$false;error=$goaFailure});throw}
finally{
    Close-ContentPlayer
    if((Test-Path -LiteralPath $goaBackup) -and -not(Test-Path -LiteralPath $goaCards)){Move-Item -LiteralPath $goaBackup -Destination $goaCards}
    [pscustomobject]@{startedUtc=$goaStarted.ToString('o');finishedUtc=[DateTime]::UtcNow.ToString('o');archiveSha256=$goaHash.ToLowerInvariant();passed=($goaFailure -eq '');checks=$goaChecks;error=$goaFailure} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $goaOutput 'content-ui.json') -Encoding utf8
    Write-Output "Release content UI evidence: $goaOutput"
}
