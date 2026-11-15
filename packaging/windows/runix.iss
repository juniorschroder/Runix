#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#ifndef PublishDir
  #define PublishDir "..\..\publish\win-x64"
#endif

#ifndef OutputDir
  #define OutputDir "..\..\artifacts"
#endif

[Setup]
AppId={{8D6E7F4A-9B3C-4E12-A5D1-7C9E2B4F8A10}
AppName=RUNIX Database Studio
AppVersion={#AppVersion}
AppVerName=RUNIX Database Studio {#AppVersion}
AppPublisher=José Carlos Schröder Júnior
AppPublisherURL=https://github.com/juniorschroder/Runix
AppSupportURL=https://github.com/juniorschroder/Runix/issues
AppUpdatesURL=https://github.com/juniorschroder/Runix/releases
DefaultDirName={autopf}\RUNIX Database Studio
DefaultGroupName=RUNIX Database Studio
DisableProgramGroupPage=yes
LicenseFile=..\..\LICENSE
OutputDir={#OutputDir}
OutputBaseFilename=Runix-Setup-{#AppVersion}-win-x64
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
SetupIconFile=..\..\src\Runix.Desktop\Assets\runix.ico
UninstallDisplayIcon={app}\Runix.Desktop.exe

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\RUNIX Database Studio"; Filename: "{app}\Runix.Desktop.exe"
Name: "{group}\{cm:UninstallProgram,RUNIX Database Studio}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\RUNIX Database Studio"; Filename: "{app}\Runix.Desktop.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Runix.Desktop.exe"; Description: "{cm:LaunchProgram,RUNIX Database Studio}"; Flags: nowait postinstall skipifsilent
