; POTA Planner by VA6DM - Inno Setup installer definition
; Compile with Inno Setup 6 after publishing the release build.

#define MyAppName "POTA Planner by VA6DM"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "VA6DM"
#define MyAppURL "mailto:va6dm@dmnet.ca"
#define MyAppExeName "POTAPlanner.exe"
#define MyPublishDir "..\release\POTAPlanner_v1.0.0"

[Setup]
AppId={{0C3727AF-BBF7-4B8F-982D-1B4B12A8800C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
DefaultDirName={autopf}\POTA Planner by VA6DM
DefaultGroupName=POTA Planner by VA6DM
DisableProgramGroupPage=yes
OutputDir=..\..\release\installer
OutputBaseFilename=POTAPlanner_by_VA6DM_Setup_v1.0.0
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UninstallDisplayName={#MyAppName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\POTA Planner by VA6DM"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\POTA Planner by VA6DM"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch POTA Planner by VA6DM"; Flags: nowait postinstall skipifsilent

