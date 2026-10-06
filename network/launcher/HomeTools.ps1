# Shared launch planning. No network is started for local play or the tutorial.
Set-StrictMode -Version Latest
function Get-GoaHomeLaunch([string]$Package, [ValidateSet('Local','Tutorial','Network','Help')][string]$Mode) {
    $packagePath=(Resolve-Path -LiteralPath $Package).Path
    if ($Mode -eq 'Help') {
        $manual=Join-Path $packagePath 'README.txt'
        if (-not (Test-Path -LiteralPath $manual -PathType Leaf)) { throw '使用说明缺失，请重新完整解压。' }
        return [pscustomobject]@{File=(Join-Path $env:WINDIR 'System32/notepad.exe');Arguments=('"'+$manual+'"');WorkingDirectory=$packagePath;Hidden=$false}
    }
    if ($Mode -eq 'Network') {
        $launcher=Join-Path $packagePath 'launcher/Launcher.ps1'
        if (-not (Test-Path -LiteralPath $launcher -PathType Leaf)) { throw '联机组件缺失，请重新完整解压。' }
        return [pscustomobject]@{File=(Join-Path $env:WINDIR 'System32/WindowsPowerShell/v1.0/powershell.exe');Arguments=('-NoProfile -STA -File "'+$launcher+'"');WorkingDirectory=$packagePath;Hidden=$true}
    }
    $player=Join-Path $packagePath 'player/Goa2V1.exe'
    if (-not (Test-Path -LiteralPath $player -PathType Leaf)) { throw '游戏程序缺失。请先全部解压，不要只复制入口文件。' }
    $logFolder=Join-Path $env:LOCALAPPDATA 'Goa2V1/Logs'
    [IO.Directory]::CreateDirectory($logFolder) | Out-Null
    $log=Join-Path $logFolder ($Mode.ToLowerInvariant()+'-'+[Guid]::NewGuid().ToString('N')+'.log')
    $arguments='-screen-fullscreen 0 -screen-width 1280 -screen-height 720 -logFile "'+$log+'"'
    if ($Mode -eq 'Tutorial') { $arguments='-goaTutorial '+$arguments }
    return [pscustomobject]@{File=$player;Arguments=$arguments;WorkingDirectory=(Split-Path -Parent $player);Hidden=$false}
}
function Start-GoaHomeMode([string]$Package, [ValidateSet('Local','Tutorial','Network','Help')][string]$Mode) {
    $launch=Get-GoaHomeLaunch $Package $Mode
    $options=@{FilePath=$launch.File;ArgumentList=$launch.Arguments;WorkingDirectory=$launch.WorkingDirectory;PassThru=$true}
    if ($launch.Hidden) { $options.WindowStyle='Hidden' }
    # Local/Tutorial are interactive games explicitly requested with a menu button.
    return Start-Process @options
}
