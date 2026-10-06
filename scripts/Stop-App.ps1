[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$InstallDirectory,[switch]$AllowTerminate)
$ErrorActionPreference='Stop'
$target=[IO.Path]::GetFullPath((Join-Path $InstallDirectory 'LocalControl.exe'))
$session=(Get-Process -Id $PID).SessionId
function OwnedProcesses {
    @(Get-Process -Name LocalControl -ErrorAction SilentlyContinue | Where-Object {
        try { $_.SessionId -eq $session -and $_.Path -and [IO.Path]::GetFullPath($_.Path) -eq $target } catch { $false }
    })
}
try {
    $running=OwnedProcesses
    if($running.Count -eq 0){exit 0}
    $version=[Diagnostics.FileVersionInfo]::GetVersionInfo($target).FileVersion
    if([version]$version -ge [version]'0.3.1.0'){
        $request=Start-Process $target -ArgumentList '--shutdown' -PassThru
        if(-not $request.WaitForExit(5000)){$request.Kill();throw 'Shutdown request timed out'}
        $deadline=[DateTime]::UtcNow.AddSeconds(12)
        do { if((OwnedProcesses).Count -eq 0){exit 0};Start-Sleep -Milliseconds 200 } while([DateTime]::UtcNow -lt $deadline)
    }
    if(-not $AllowTerminate){exit 2}
    # Legacy 0.3.0 did not support graceful exit. Only processes at the exact
    # chosen installation path in this user's session may be terminated.
    foreach($process in (OwnedProcesses)){
        $started=$process.StartTime
        $current=Get-Process -Id $process.Id -ErrorAction SilentlyContinue
        if($current -and $current.StartTime -eq $started -and $current.Path -eq $target -and $current.SessionId -eq $session){Stop-Process -Id $current.Id -Force}
    }
    $deadline=[DateTime]::UtcNow.AddSeconds(8)
    do { if((OwnedProcesses).Count -eq 0){exit 0};Start-Sleep -Milliseconds 200 } while([DateTime]::UtcNow -lt $deadline)
    throw 'LocalControl is still running'
} catch { Write-Error 'Could not stop the selected LocalControl installation.';exit 1 }
