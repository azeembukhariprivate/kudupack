; KUDUPACK installer (Inno Setup 6)
; Installs for the current Windows user only, so it never asks for administrator rights.

#ifndef MyAppVersion
  #define MyAppVersion "1.0.3"
#endif
#ifndef SourceDir
  #define SourceDir "..\out\KUDUPACK"
#endif

#define MyAppName "KUDUPACK"
#define MyAppExe "KUDUPACK.exe"
#define MyAppUrl "https://github.com/azeembukhariprivate/KUDUPACK"

[Setup]
AppId={{8C1B5E6A-4E0B-4C7A-9C6E-2B9E4F0A7D11}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher=Azeem
AppPublisherURL={#MyAppUrl}
AppSupportURL={#MyAppUrl}/issues
AppUpdatesURL={#MyAppUrl}/releases
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=Output
OutputBaseFilename=KUDUPACK-{#MyAppVersion}-setup
SetupIconFile=..\src\KUDUPACK\Assets\KUDUPACK.ico
UninstallDisplayIcon={app}\{#MyAppExe}
UninstallDisplayName={#MyAppName}
LicenseFile=..\LICENSE
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany=Azeem
VersionInfoProductName={#MyAppName}
VersionInfoDescription={#MyAppName} Setup

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked
Name: "contextmenu"; Description: "Add KUDUPACK to the right-click menu in File Explorer"
Name: "associate"; Description: "Open archives (ZIP, 7Z, RAR, TAR.GZ and more) with KUDUPACK"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExe}"; Parameters: "--register-shell"; Tasks: contextmenu; Flags: runhidden waituntilterminated
Filename: "{app}\{#MyAppExe}"; Parameters: "--register-assoc"; Tasks: associate; Flags: runhidden waituntilterminated
Filename: "{app}\{#MyAppExe}"; Description: "Open KUDUPACK now"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\{#MyAppExe}"; Parameters: "--unregister"; Flags: runhidden waituntilterminated; RunOnceId: "KUDUPACKUnregister"

[Registry]
; Remove saved settings when uninstalling.
Root: HKCU; Subkey: "Software\KUDUPACK"; Flags: uninsdeletekey dontcreatekey
