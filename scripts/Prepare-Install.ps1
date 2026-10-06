[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$InstallDirectory,[Parameter(Mandatory=$true)][string]$BackupDirectory,[switch]$Restore,[switch]$Diagnostics)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($InstallDirectory).TrimEnd('\')
$backup=[IO.Path]::GetFullPath($BackupDirectory).TrimEnd('\')
function SafePath([string]$base,[string]$relative){
    if([IO.Path]::IsPathRooted($relative) -or $relative.Contains(':') -or $relative.Split([char[]]'/\') -contains '..'){throw 'Unsafe installation path'}
    $path=[IO.Path]::GetFullPath((Join-Path $base $relative))
    if(-not $path.StartsWith($base+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe installation path'}
    $parent=Split-Path $path -Parent
    while($parent.Length -ge $base.Length){
        if((Test-Path $parent) -and ((Get-Item $parent -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Reparse point in installation'}
        if($parent -eq $base){break};$parent=Split-Path $parent -Parent
    }
    return $path
}
function RestoreFiles {
    $listFile=Join-Path $backup 'backup-list.json'
    if(-not(Test-Path $listFile)){return}
    $old=@(Get-Content $listFile -Raw | ConvertFrom-Json)
    $manifest=Join-Path $root 'owned-files.json'
    if(Test-Path $manifest){
        try{$new=@(Get-Content $manifest -Raw | ConvertFrom-Json)}catch{$new=@()}
        if(Test-Path (Join-Path $root 'Uninstall.exe')){$new+='Uninstall.exe'}
        foreach($relative in $new){if($old -notcontains $relative){$file=SafePath $root $relative;if(Test-Path $file){Remove-Item $file -Force}}}
    }
    foreach($relative in $old){$from=SafePath $backup $relative;$to=SafePath $root $relative;if(Test-Path $from){New-Item (Split-Path $to -Parent) -ItemType Directory -Force | Out-Null;Copy-Item $from $to -Force}}
}
try {
    if($Restore){RestoreFiles;exit 0}
    New-Item $backup -ItemType Directory -Force | Out-Null
    $manifest=Join-Path $root 'owned-files.json'
    $files=@()
    if(Test-Path $manifest){
        if((Get-Item $manifest).Length -gt 2MB){throw 'Invalid installed file list'}
        $files=@(Get-Content $manifest -Raw | ConvertFrom-Json)
        if($files.Count -gt 10000){throw 'Invalid installed file count'}
        if(Test-Path (Join-Path $root 'Uninstall.exe')){$files+='Uninstall.exe'}
    }
    $existing=@()
    foreach($relative in $files){
        if($relative -isnot [string]){throw 'Invalid installed filename'}
        $from=SafePath $root $relative;$to=SafePath $backup $relative
        if(Test-Path $from){if((Get-Item $from -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Linked installed file'};New-Item (Split-Path $to -Parent) -ItemType Directory -Force | Out-Null;Copy-Item $from $to -Force;$existing+=$relative}
    }
    ConvertTo-Json -InputObject $existing | Set-Content (Join-Path $backup 'backup-list.json') -Encoding UTF8
    foreach($relative in $existing){Remove-Item (SafePath $root $relative) -Force}
    exit 0
} catch {
    $failure=$_
    if($Diagnostics){Write-Host $failure.Exception.ToString();Write-Host $failure.ScriptStackTrace}
    try {
        $logs=Join-Path $env:LOCALAPPDATA 'LocalControl/logs'
        New-Item $logs -ItemType Directory -Force | Out-Null
        Add-Content (Join-Path $logs 'installer-helper.log') ("prepare {0} {1} line {2}" -f $failure.Exception.GetType().Name,$failure.FullyQualifiedErrorId,$failure.InvocationInfo.ScriptLineNumber)
    } catch { }
    if(-not $Restore){try{RestoreFiles}catch{}}
    Write-Error 'Installation could not proceed; previous application files were restored where available.'
    exit 1
}
