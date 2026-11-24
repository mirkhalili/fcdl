#define AppName "Jacquard Studio"
#ifndef AppVersion
#error Pass /DAppVersion from version.txt
#endif
#ifndef BinaryVersion
#error Pass /DBinaryVersion from the numeric product version
#endif
#ifndef BuildTimestamp
#error Pass /DBuildTimestamp as a UTC ISO-8601 build timestamp
#endif
#define AppPublisher "Mirkhalili"
#define AppUrl "https://www.mirkhalili.com"
#define AppExeName "JacquardStudio.exe"
#define AppCopyrightText "Copyright (C) 2026 Mirkhalili. All rights reserved."
[Setup]
AppId={{5C0E5A7E-DF2A-4D61-9039-B9EC80CD87AD}
AppName={#AppName}
AppVerName={#AppName} {#AppVersion}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}
AppUpdatesURL={#AppUrl}
AppCopyright={#AppCopyrightText}
AppComments=Designed by {#AppPublisher} | Built UTC {#BuildTimestamp}
VersionInfoVersion={#BinaryVersion}
VersionInfoTextVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoCopyright={#AppCopyrightText}
VersionInfoDescription={#AppName} by {#AppPublisher} | {#BuildTimestamp}
VersionInfoOriginalFileName=JacquardStudio-Setup.exe
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#BinaryVersion}
VersionInfoProductTextVersion={#AppVersion}
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
UninstallDisplayName={#AppName} {#AppVersion}
[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\src\JacquardStudio\Assets\app.ico"; DestDir: "{app}"; DestName: "sam-{#AppVersion}.ico"; Flags: ignoreversion
[Icons]
Name: "{autoprograms}\Jacquard Studio"; Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\sam-{#AppVersion}.ico"
[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch Jacquard Studio"; Flags: nowait postinstall skipifsilent
