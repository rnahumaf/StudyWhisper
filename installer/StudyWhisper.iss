#ifndef PublishDir
  #define PublishDir "..\artifacts\publish-0.1.6"
#endif
#ifndef ArtifactDir
  #define ArtifactDir "..\artifacts"
#endif
[Setup]
AppId={{DA58DA7B-2A51-4F8A-90FA-70637B036A4B}
AppName=StudyWhisper
AppVersion=0.1.6
AppPublisher=StudyWhisper contributors
DefaultDirName={localappdata}\Programs\StudyWhisper
DefaultGroupName=StudyWhisper
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#ArtifactDir}
OutputBaseFilename=StudyWhisper-0.1.6-Setup-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
LicenseFile=..\LICENSE
UninstallDisplayIcon={app}\StudyWhisper.exe
CloseApplications=yes
RestartApplications=no
[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"
[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"
Source: "..\LICENSE"; DestDir: "{app}"
Source: "..\README.md"; DestDir: "{app}"
Source: "..\docs\*"; DestDir: "{app}\docs"; Flags: recursesubdirs createallsubdirs
[Icons]
Name: "{userprograms}\StudyWhisper"; Filename: "{app}\StudyWhisper.exe"
[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "StudyWhisper"; Flags: uninsdeletevalue
[Run]
Filename: "{app}\StudyWhisper.exe"; Description: "Abrir StudyWhisper (monitoramento pausado)"; Flags: nowait postinstall skipifsilent unchecked
