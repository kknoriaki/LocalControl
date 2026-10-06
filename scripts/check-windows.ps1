[CmdletBinding()]
param([string]$Version='0.3.1',[string]$LegacySetup)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
$qa=Join-Path $projectRoot 'artifacts/qa'
New-Item $qa -ItemType Directory -Force | Out-Null
$temporary=Join-Path ([IO.Path]::GetTempPath()) ('LocalControl-acceptance-'+[guid]::NewGuid().ToString('N'))
$install=Join-Path $temporary 'Установка LocalControl'
$received=Join-Path $temporary 'Received'
$setup=Join-Path $projectRoot "artifacts/LocalControl-$Version-Setup.exe"
$data=Join-Path $env:LOCALAPPDATA 'LocalControl'
$results=[Collections.Generic.List[string]]::new()
function Check([bool]$passed,[string]$name){if(-not $passed){throw "FAILED: $name"};$results.Add($name);Write-Host "PASS $name"}
function Run([string]$file,[string]$arguments,[int]$timeout=90000){
    $process=Start-Process $file -ArgumentList $arguments -PassThru
    if(-not $process.WaitForExit($timeout)){$process.Kill();throw "Timed out: $file"}
    if($process.ExitCode -ne 0){throw "Failed exit $($process.ExitCode): $file"}
}
function WaitUntil([scriptblock]$condition,[int]$seconds=30){$end=[DateTime]::UtcNow.AddSeconds($seconds);do{if(& $condition){return $true};Start-Sleep -Milliseconds 200}while([DateTime]::UtcNow -lt $end);return $false}
try {
    & (Join-Path $PSScriptRoot 'Install-WebView2.ps1')
    if($LASTEXITCODE -ne 0){throw 'WebView2 prerequisite failed'}
    New-Item $temporary,$received,$data -ItemType Directory -Force | Out-Null
    # An actual installer wizard is inspected before installing. Unicode text
    # comes from the compiled EXE, not from source-code string assertions.
    Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
    Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class WizardButtons {
 [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr parent,int id);
 [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h,uint m,IntPtr w,IntPtr l);
 delegate bool ChildCallback(IntPtr h,IntPtr l);
 [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr h,ChildCallback callback,IntPtr l);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h,StringBuilder text,int count);
 [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
 [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr OpenProcess(uint access,bool inherit,uint pid);
 [DllImport("kernel32.dll",SetLastError=true)] static extern bool IsWow64Process(IntPtr process,out bool wow64);
 [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr VirtualAllocEx(IntPtr process,IntPtr address,UIntPtr size,uint type,uint protection);
 [DllImport("kernel32.dll")] static extern bool VirtualFreeEx(IntPtr process,IntPtr address,UIntPtr size,uint type);
 [DllImport("kernel32.dll",SetLastError=true)] static extern bool WriteProcessMemory(IntPtr process,IntPtr address,byte[] bytes,UIntPtr size,out UIntPtr count);
 [DllImport("kernel32.dll",SetLastError=true)] static extern bool ReadProcessMemory(IntPtr process,IntPtr address,byte[] bytes,UIntPtr size,out UIntPtr count);
 [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
 static Exception NativeError() { return new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); }
 public static string[] ComponentNames(IntPtr wizard) {
   IntPtr tree=IntPtr.Zero;
   EnumChildWindows(wizard,(h,l)=>{var name=new StringBuilder(128);GetClassName(h,name,name.Capacity);if(name.ToString()=="SysTreeView32"&&IsWindowVisible(h)){tree=h;return false;}return true;},IntPtr.Zero);
   if(tree==IntPtr.Zero) return new string[0];
   uint pid;GetWindowThreadProcessId(tree,out pid);
   var process=OpenProcess(0x1038,false,pid);
   if(process==IntPtr.Zero) throw NativeError();
   IntPtr remote=IntPtr.Zero;
   try {
     bool wow64;if(!IsWow64Process(process,out wow64)) throw NativeError();
     bool target32=IntPtr.Size==4||wow64;
     remote=VirtualAllocEx(process,IntPtr.Zero,(UIntPtr)4096,0x3000,4);
     if(remote==IntPtr.Zero) throw NativeError();
     var textAddress=new IntPtr(remote.ToInt64()+128);
     var names=new List<string>();
     // TVM_GETNEXTITEM (root, next sibling) and TVM_GETITEMW read the
     // actual Unicode tree labels. NSIS's component tree lacks UIA items.
     var item=SendMessage(tree,0x110A,IntPtr.Zero,IntPtr.Zero);
     for(int i=0;i<100&&item!=IntPtr.Zero;i++) {
       var structure=new byte[target32?40:56];
       BitConverter.GetBytes(1u).CopyTo(structure,0); // TVIF_TEXT
       if(target32) {
         BitConverter.GetBytes(unchecked((uint)item.ToInt64())).CopyTo(structure,4);
         BitConverter.GetBytes(unchecked((uint)textAddress.ToInt64())).CopyTo(structure,16);
         BitConverter.GetBytes(1024).CopyTo(structure,20);
       } else {
         BitConverter.GetBytes(item.ToInt64()).CopyTo(structure,8);
         BitConverter.GetBytes(textAddress.ToInt64()).CopyTo(structure,24);
         BitConverter.GetBytes(1024).CopyTo(structure,32);
       }
       UIntPtr count;
       if(!WriteProcessMemory(process,remote,structure,(UIntPtr)structure.Length,out count)) throw NativeError();
       if(SendMessage(tree,0x113E,IntPtr.Zero,remote)==IntPtr.Zero) throw new InvalidOperationException("Cannot read installer tree item");
       var bytes=new byte[2048];
       if(!ReadProcessMemory(process,textAddress,bytes,(UIntPtr)bytes.Length,out count)) throw NativeError();
       names.Add(Encoding.Unicode.GetString(bytes).Split('\0')[0]);
       item=SendMessage(tree,0x110A,(IntPtr)1,item);
     }
     return names.ToArray();
   } finally {if(remote!=IntPtr.Zero)VirtualFreeEx(process,remote,UIntPtr.Zero,0x8000);CloseHandle(process);}
 }
}
'@
    $wizard=Start-Process $setup -ArgumentList '/LANG=1049' -PassThru
    try {
        Check (WaitUntil {$wizard.Refresh();$wizard.MainWindowHandle -ne [IntPtr]::Zero}) 'installer wizard opens'
        for($i=0;$i -lt 2;$i++){
            $next=[WizardButtons]::GetDlgItem($wizard.MainWindowHandle,1)
            [void][WizardButtons]::SendMessage($next,0xF5,[IntPtr]::Zero,[IntPtr]::Zero)
            Start-Sleep -Milliseconds 500
        }
        $names=@()
        $valid=WaitUntil {
            $window=[Windows.Automation.AutomationElement]::FromHandle($wizard.MainWindowHandle)
            $script:optionNames=@($window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition) | ForEach-Object {$_.Current.Name}) + @([WizardButtons]::ComponentNames($wizard.MainWindowHandle))
            ($script:optionNames -contains 'Ярлык на рабочем столе') -and ($script:optionNames -contains 'Запускать с Windows')
        } 10
        $names=$script:optionNames
        Write-Host ('Compiled installer labels: '+($names -join ' | '))
        $names | ConvertTo-Json | Set-Content (Join-Path $qa 'installer-russian.json') -Encoding utf8
        Check $valid 'compiled installer shows readable Russian component names'
    } finally {if(-not $wizard.HasExited){$wizard.Kill();$wizard.WaitForExit()}}
    if($LegacySetup){Run $LegacySetup "/S /D=$install";Check (Test-Path (Join-Path $install 'LocalControl.exe')) 'previous 0.3.0 installer installs at chosen path'}
    else {Run $setup "/S /LANG=1049 /D=$install"}
    $settings=@{ComputerName='QA сохраняется';AutoStart=$false;Port=41017;TransferDirectory=$received;Onboarded=$true}
    $settings|ConvertTo-Json|Set-Content (Join-Path $data 'settings.json') -Encoding utf8
    @(@{Id='qa-device';Label='QA iPhone';Permissions=@('status.read');ApprovedAt=[DateTimeOffset]::UtcNow.ToString('O')})|ConvertTo-Json -AsArray|Set-Content (Join-Path $data 'devices.json') -Encoding utf8
    Set-Content (Join-Path $received 'keep.txt') 'received file' -Encoding utf8
    Set-Content (Join-Path $install 'unrelated-user-file.txt') 'user file' -Encoding utf8
    $settingsHash=(Get-FileHash (Join-Path $data 'settings.json')).Hash
    $devicesHash=(Get-FileHash (Join-Path $data 'devices.json')).Hash
    $old=Start-Process (Join-Path $install 'LocalControl.exe') -PassThru
    Check (WaitUntil {$old.Refresh();$old.MainWindowHandle -ne [IntPtr]::Zero}) 'existing installation is running before update'
    Run $setup "/S /LANG=1049 /D=$install"
    Check $old.HasExited 'setup closes the running previous installation'
    Check (([Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $install 'LocalControl.exe')).FileVersion) -eq "$Version.0") 'setup installs the requested application version'
    Check ((Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\LocalControl').DisplayVersion -eq $Version) 'Windows installed-app version matches release'
    Check ((Get-FileHash (Join-Path $data 'settings.json')).Hash -eq $settingsHash) 'update preserves settings bytes'
    Check ((Get-FileHash (Join-Path $data 'devices.json')).Hash -eq $devicesHash) 'update preserves approved device bytes'
    Check (Test-Path (Join-Path $install 'unrelated-user-file.txt')) 'update preserves unrelated user file in install folder'
    Run (Join-Path $install 'LocalControl.exe') '--health-check' 30000
    Check $true 'installed application serves real HTML, assets and authenticated Windows state'
    $report=Join-Path $qa 'installed-ui.json'
    Run (Join-Path $install 'LocalControl.exe') "--ui-smoke --smoke-report `"$report`"" 60000
    $ui=Get-Content $report -Raw|ConvertFrom-Json
    Check ($ui.success -and $ui.realApiAuthenticated) 'installed WebView2 renders production interface using real API'
    Check $ui.secondInstanceActivated 'second launch activates the existing window'
    $current=Start-Process (Join-Path $install 'LocalControl.exe') -PassThru
    Check (WaitUntil {$current.Refresh();$current.MainWindowHandle -ne [IntPtr]::Zero}) 'application is running before uninstall'
    Run (Join-Path $install 'Uninstall.exe') '/S'
    Check (WaitUntil {-not(Test-Path (Join-Path $install 'LocalControl.exe'))}) 'uninstall closes running application and removes executable'
    Check (-not(Test-Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\LocalControl')) 'uninstall removes installed-app entry'
    Check (Test-Path (Join-Path $received 'keep.txt')) 'uninstall preserves received files'
    Check ((Get-FileHash (Join-Path $data 'settings.json')).Hash -eq $settingsHash) 'uninstall preserves settings'
    Check ((Get-FileHash (Join-Path $data 'devices.json')).Hash -eq $devicesHash) 'uninstall preserves devices'
    Check (Test-Path (Join-Path $install 'unrelated-user-file.txt')) 'uninstall preserves unrelated user file'
    $owned=Get-Content (Join-Path $projectRoot 'artifacts/win-x64/owned-files.json') -Raw|ConvertFrom-Json
    Check (@($owned|Where-Object {Test-Path (Join-Path $install $_)}).Count -eq 0) 'uninstall removes every packaged application file'
    @{version=$Version;passed=$results.Count;checks=$results;success=$true}|ConvertTo-Json -Depth 4|Set-Content (Join-Path $qa 'windows-acceptance.json') -Encoding utf8
    Write-Host "$($results.Count) Windows acceptance checks passed."
} finally {Remove-Item $temporary -Recurse -Force -ErrorAction SilentlyContinue}
