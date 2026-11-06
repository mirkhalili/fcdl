#define AppName "Jacquard Studio"
#ifndef AppVersion
#error Pass /DAppVersion from version.txt
#endif
#define AppExeName "JacquardStudio.exe"
[Setup]
AppId={{5C0E5A7E-DF2A-4D61-9039-B9EC80CD87AD}
AppName={#AppName}
AppVersion={#AppVersion}
VersionInfoVersion=3.2.0.0
DefaultDirName={autopf}\Jacquard Studio
DefaultGroupName=Jacquard Studio
UsePreviousAppDir=yes
UsePreviousGroup=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts\installer
OutputBaseFilename=JacquardStudio-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\src\JacquardStudio\Assets\app.ico
CloseApplications=yes
UninstallDisplayIcon={app}\{#AppExeName}
[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{autoprograms}\Jacquard Studio"; Filename: "{app}\{#AppExeName}"
[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch Jacquard Studio"; Flags: nowait postinstall skipifsilent
