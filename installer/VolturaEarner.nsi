Unicode true
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"
!ifndef VERSION
  !error "VERSION is required"
!endif
!ifndef PAYLOAD
  !error "PAYLOAD is required"
!endif
!ifndef OUTPUT
  !error "OUTPUT is required"
!endif
!ifndef VARIANT
  !define VARIANT "standard"
!endif
!define APP_NAME "Voltura Earner"
!define PUBLISHER "Voltura AB"
!define DEVELOPER "Joakim Skoglund"
!define PRODUCT_URL "https://voltura.github.io/voltura-earner"
!define POSTAL_ADDRESS "Voltura AB, H${U+00E4}stholmsv${U+00E4}gen 33, SE-131 71 Nacka, Sweden"
!if "${VARIANT}" == "full"
  !define INSTALLER_FILE_SUFFIX "-full"
!else
  !define INSTALLER_FILE_SUFFIX ""
!endif
Name "${APP_NAME}"
Caption "${APP_NAME} ${VERSION}"
BrandingText "${PUBLISHER} | ${VERSION}"
OutFile "${OUTPUT}"
InstallDir "$LOCALAPPDATA\Programs\VolturaEarner"
RequestExecutionLevel user
XPStyle on
ManifestDPIAware true
ManifestSupportedOS all
SetCompressor lzma
VIProductVersion "${VERSION}.0"
VIAddVersionKey /LANG=1033 "ProductName" "${APP_NAME}"
VIAddVersionKey /LANG=1033 "CompanyName" "${PUBLISHER}"
VIAddVersionKey /LANG=1033 "FileDescription" "${APP_NAME} Installer"
VIAddVersionKey /LANG=1033 "FileVersion" "${VERSION}"
VIAddVersionKey /LANG=1033 "ProductVersion" "${VERSION}"
VIAddVersionKey /LANG=1033 "OriginalFilename" "VolturaEarner-Setup-${VERSION}-win-x64${INSTALLER_FILE_SUFFIX}.exe"
VIAddVersionKey /LANG=1033 "InternalName" "VolturaEarnerSetup"
VIAddVersionKey /LANG=1033 "LegalCopyright" "Copyright (c) 2026 ${PUBLISHER}"
VIAddVersionKey /LANG=1033 "Comments" "Developer: ${DEVELOPER}; Website: ${PRODUCT_URL}; Address: ${POSTAL_ADDRESS}"
!define MUI_ICON "..\apps\windows\Assets\App.ico"
!define MUI_WELCOMEFINISHPAGE_BITMAP "Assets\wizard.bmp"
!define MUI_WELCOMEFINISHPAGE_BITMAP_STRETCH "AspectFitHeight"
!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TITLE "${APP_NAME} ${VERSION}"
; Unicode setup must offer every language, regardless of the Windows code page.
!define MUI_LANGDLL_ALLLANGUAGES
!define MUI_FINISHPAGE_RUN "$INSTDIR\VolturaEarner.exe"
!define MUI_FINISHPAGE_REBOOTLATER_DEFAULT
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "..\LICENSE"
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!include "languages.nsh"
Var PowerShell
Function .onInit
  !insertmacro MUI_LANGDLL_DISPLAY
  ${IfNot} ${RunningX64}
    Abort "$(RequiresX64)"
  ${EndIf}
  StrCpy $PowerShell "$WINDIR\Sysnative\WindowsPowerShell\v1.0\powershell.exe"
  ; Use Windows modules even when setup inherits a PowerShell 7 environment.
  System::Call 'kernel32::SetEnvironmentVariableW(w "PSModulePath", w "$WINDIR\System32\WindowsPowerShell\v1.0\Modules") i.r0'
FunctionEnd
Section "Install"
  CreateDirectory "$LOCALAPPDATA\Voltura\Earner"
  FileOpen $2 "$LOCALAPPDATA\Voltura\Earner\setup.log" w
  FileClose $2
  InitPluginsDir
  SetOutPath "$PLUGINSDIR"
  File "maintain.ps1"
  File "prerequisite.ps1"
  File "prepare-payload.ps1"
!if "${VARIANT}" == "standard"
  nsExec::ExecToStack '"$PowerShell" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$PLUGINSDIR\prerequisite.ps1"'
  Pop $0
  Pop $1
  DetailPrint "$1"
  FileOpen $2 "$LOCALAPPDATA\Voltura\Earner\setup.log" a
  FileSeek $2 0 END
  FileWrite $2 "$1$\r$\nExit: $0$\r$\n"
  FileClose $2
  ${If} $0 == 3010
    SetRebootFlag true
    SetErrorLevel 3010
  ${ElseIf} $0 != 0
    MessageBox MB_ICONSTOP "$(RuntimeFailed)"
    SetErrorLevel 1
    Abort
  ${EndIf}
!endif
  SetOutPath "$PLUGINSDIR\payload"
  File /r "${PAYLOAD}\*"
  WriteUninstaller "$PLUGINSDIR\payload\Uninstall.exe"
  ; The generated uninstaller is hashed after creation, before verified staging.
  nsExec::ExecToStack '"$PowerShell" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$PLUGINSDIR\prepare-payload.ps1" -Payload "$PLUGINSDIR\payload"'
  Pop $0
  Pop $1
  DetailPrint "$1"
  FileOpen $2 "$LOCALAPPDATA\Voltura\Earner\setup.log" a
  FileSeek $2 0 END
  FileWrite $2 "$1$\r$\nExit: $0$\r$\n"
  FileClose $2
  ${If} $0 != 0
    SetErrorLevel 1
    Abort "$(Failed)"
  ${EndIf}
  nsExec::ExecToStack '"$PowerShell" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$PLUGINSDIR\maintain.ps1" -Payload "$PLUGINSDIR\payload" -Variant "${VARIANT}" -SetupPath "$EXEPATH"'
  Pop $0
  Pop $1
  DetailPrint "$1"
  FileOpen $2 "$LOCALAPPDATA\Voltura\Earner\setup.log" a
  FileSeek $2 0 END
  FileWrite $2 "$1$\r$\nExit: $0$\r$\n"
  FileClose $2
  ${If} $0 == 2
    MessageBox MB_ICONSTOP "$(AppRunning)"
    SetErrorLevel 2
    Abort
  ${ElseIf} $0 != 0
    MessageBox MB_ICONSTOP "$(Failed)"
    SetErrorLevel 1
    Abort
  ${EndIf}
SectionEnd
Function un.onInit
  !insertmacro MUI_UNGETLANGUAGE
  StrCpy $PowerShell "$WINDIR\Sysnative\WindowsPowerShell\v1.0\powershell.exe"
  System::Call 'kernel32::SetEnvironmentVariableW(w "PSModulePath", w "$WINDIR\System32\WindowsPowerShell\v1.0\Modules") i.r0'
FunctionEnd
Section "Uninstall"
  InitPluginsDir
  SetOutPath "$PLUGINSDIR"
  File "maintain.ps1"
  StrCpy $1 ""
  MessageBox MB_YESNO|MB_DEFBUTTON2 "$(RemoveData)" IDNO keepSettings
    StrCpy $1 "-RemoveSettings"
  keepSettings:
  nsExec::ExecToLog '"$PowerShell" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$PLUGINSDIR\maintain.ps1" -Mode Uninstall $1'
  Pop $0
  ${If} $0 == 2
    MessageBox MB_ICONSTOP "$(AppRunning)"
    SetErrorLevel 2
    Abort
  ${ElseIf} $0 != 0
    MessageBox MB_ICONSTOP "$(Failed)"
    SetErrorLevel 1
    Abort
  ${EndIf}
SectionEnd
