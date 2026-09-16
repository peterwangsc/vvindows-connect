#define AppName "vindOS"
#define AppVersion GetStringFileInfo("..\VindOS\bin\publish\vindOS.exe", "ProductVersion")
#define Publish "..\VindOS\bin\publish"

[Setup]
AppId={{7D2E9C4A-3B1F-4E6D-9A0C-5F8B2D7E1C43}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=golfcore
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=.\out
OutputBaseFilename={#AppName}-{#AppVersion}-setup
SetupIconFile=..\VindOS\vindOS.ico
UninstallDisplayIcon={app}\vindOS.exe
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
CloseApplications=yes
RestartApplications=no

[Files]
Source: "{#Publish}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\vindOS.exe"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\vindOS.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Run]
Filename: "{app}\vindOS.exe"; Description: "Start {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\vindOS.exe"; Parameters: "--restore-bridges"; Flags: runhidden waituntilterminated; RunOnceId: "RestoreBridges"

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\{#AppName}\CloudXR\Server"
