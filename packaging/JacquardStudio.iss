#define AppName "Jacquard Studio"
#define AppVersion "001.1.0.0-a1"
#define AppExeName "JacquardStudio.exe"
[Setup]
AppId={{5C0E5A7E-DF2A-4D61-9039-B9EC80CD87AD}
AppName={#AppName}
AppVersion={#AppVersion}
VersionInfoVersion=1.1.0.0
DefaultDirName={autopf}\Jacquard Studio
DefaultGroupName=Jacquard Studio
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts\installer
OutputBaseFilename=JacquardStudio-001.1.0.0-a1-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#AppExeName}
[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{autoprograms}\Jacquard Studio"; Filename: "{app}\{#AppExeName}"
[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch Jacquard Studio"; Flags: nowait postinstall skipifsilent
