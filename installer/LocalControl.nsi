Unicode true
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"
!ifndef PUBLISH_DIR
 !error "Pass /DPUBLISH_DIR=<self-contained publish directory>"
!endif
!ifndef OUT_FILE
 !define OUT_FILE "..\artifacts\LocalControl-0.3.0-dev-Setup.exe"
!endif
Name "LocalControl"
OutFile "${OUT_FILE}"
InstallDir "$LOCALAPPDATA\Programs\LocalControl"
RequestExecutionLevel user
SetCompressor /SOLID lzma
!define MUI_ABORTWARNING
!define MUI_LANGDLL_REGISTRY_ROOT "HKCU"
!define MUI_LANGDLL_REGISTRY_KEY "Software\LocalControl\Installer"
!define MUI_LANGDLL_REGISTRY_VALUENAME "Language"
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "Russian"
!insertmacro MUI_LANGUAGE "English"
LangString CloseApp ${LANG_RUSSIAN} "Завершите LocalControl через меню tray перед установкой."
LangString CloseApp ${LANG_ENGLISH} "Exit LocalControl from its tray menu before installing."
LangString NeedRuntime ${LANG_RUSSIAN} "Для LocalControl нужен Microsoft Edge WebView2 Evergreen Runtime. Установите Runtime с сайта Microsoft и запустите Setup снова. Открыть сайт?"
LangString NeedRuntime ${LANG_ENGLISH} "LocalControl requires Microsoft Edge WebView2 Evergreen Runtime. Install it from Microsoft, then run Setup again. Open the website?"
LangString InstallRuntime ${LANG_RUSSIAN} "WebView2 Runtime не найден. Скачать и установить подписанный Microsoft Runtime? Для этого нужен интернет."
LangString InstallRuntime ${LANG_ENGLISH} "WebView2 Runtime is missing. Download and install the signed Microsoft Runtime? This requires internet."
LangString DesktopShortcut ${LANG_RUSSIAN} "Ярлык на рабочем столе"
LangString DesktopShortcut ${LANG_ENGLISH} "Desktop shortcut"
LangString StartWindows ${LANG_RUSSIAN} "Запускать с Windows"
LangString StartWindows ${LANG_ENGLISH} "Start with Windows"
Function .onInit
 ${IfNot} ${RunningX64}
   MessageBox MB_ICONSTOP "LocalControl requires Windows x64."
   Abort
 ${EndIf}
 SetShellVarContext current
 !insertmacro MUI_LANGDLL_DISPLAY
 FindWindow $0 "" "LocalControl"
 ${If} $0 != 0
   MessageBox MB_ICONSTOP "$(CloseApp)"
   Abort
 ${EndIf}
 SetRegView 32
 ReadRegStr $0 HKLM "SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}" "pv"
 ${If} $0 == ""
 ${OrIf} $0 == "0.0.0.0"
   ReadRegStr $0 HKCU "SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}" "pv"
 ${EndIf}
 ${If} $0 == ""
 ${OrIf} $0 == "0.0.0.0"
   SetRegView 64
   ReadRegStr $0 HKLM "SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}" "pv"
 ${EndIf}
 ${If} $0 == ""
 ${OrIf} $0 == "0.0.0.0"
   ReadRegStr $0 HKCU "SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}" "pv"
 ${EndIf}
 ${If} $0 == ""
 ${OrIf} $0 == "0.0.0.0"
   MessageBox MB_YESNO|MB_ICONQUESTION "$(InstallRuntime)" IDNO runtime_manual
   InitPluginsDir
   SetOutPath "$PLUGINSDIR"
   File /oname=Install-WebView2.ps1 "${PUBLISH_DIR}\Install-WebView2.ps1"
   nsExec::ExecToLog '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "$PLUGINSDIR\Install-WebView2.ps1"'
   Pop $1
   ${If} $1 == 0
     Goto runtime_ready
   ${EndIf}
   runtime_manual:
   MessageBox MB_YESNO|MB_ICONEXCLAMATION "$(NeedRuntime)" IDNO no_runtime_site
   ExecShell "open" "https://developer.microsoft.com/microsoft-edge/webview2/"
   no_runtime_site:
   Abort
 ${EndIf}
 runtime_ready:
 SetRegView 64
FunctionEnd
Section "LocalControl" SEC_MAIN
 SectionIn RO
 SetOutPath "$INSTDIR"
 File /r /x "uninstall-files.nsh" "${PUBLISH_DIR}\*.*"
 WriteUninstaller "$INSTDIR\Uninstall.exe"
 CreateDirectory "$SMPROGRAMS\LocalControl"
 CreateShortcut "$SMPROGRAMS\LocalControl\LocalControl.lnk" "$INSTDIR\LocalControl.exe"
 CreateShortcut "$SMPROGRAMS\LocalControl\Uninstall.lnk" "$INSTDIR\Uninstall.exe"
 WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\LocalControl" "DisplayName" "LocalControl"
 WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\LocalControl" "DisplayVersion" "0.3.0-dev"
 WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\LocalControl" "InstallLocation" "$INSTDIR"
 WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\LocalControl" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
 WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\LocalControl" "NoModify" 1
 WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\LocalControl" "NoRepair" 1
SectionEnd
Section /o "$(DesktopShortcut)" SEC_DESKTOP
 CreateShortcut "$DESKTOP\LocalControl.lnk" "$INSTDIR\LocalControl.exe"
SectionEnd
Section /o "$(StartWindows)" SEC_STARTUP
 WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "LocalControl" '$\"$INSTDIR\LocalControl.exe$\" --tray'
SectionEnd
Function un.onInit
 !insertmacro MUI_UNGETLANGUAGE
 SetShellVarContext current
 SetRegView 64
 FindWindow $0 "" "LocalControl"
 ${If} $0 != 0
   MessageBox MB_ICONSTOP "$(CloseApp)"
   Abort
 ${EndIf}
FunctionEnd
Section "Uninstall"
 Delete "$DESKTOP\LocalControl.lnk"
 ReadRegStr $0 HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "LocalControl"
 ${If} $0 == '$\"$INSTDIR\LocalControl.exe$\" --tray'
   DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "LocalControl"
 ${EndIf}
 Delete "$SMPROGRAMS\LocalControl\LocalControl.lnk"
 Delete "$SMPROGRAMS\LocalControl\Uninstall.lnk"
 RMDir "$SMPROGRAMS\LocalControl"
 DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\LocalControl"
 ; Delete only files enumerated from our publish, not unrelated user files.
 ; Preserve %LOCALAPPDATA%\LocalControl and Downloads.
 !include "${PUBLISH_DIR}\uninstall-files.nsh"
SectionEnd
