#define MyAppVersion "1.8.2"
[Setup]
AppId={{8F0EA48E-1E8E-4FBA-9D18-10D05F9B5CB7}
AppName=CrowLink
AppVersion={#MyAppVersion}
AppPublisher=CrowScienceLab
DefaultDirName={autopf}\CrowScienceLab\CrowLink
DefaultGroupName=CrowScienceLab\CrowLink
LicenseFile=..\LICENSE
OutputDir=..\artifacts
OutputBaseFilename=CrowLink-1.8.2-Setup-win-x64
SetupIconFile=..\src\CrowLink.App\Assets\CrowLink.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
UninstallDisplayIcon={app}\CrowLink.exe
VersionInfoVersion=1.8.2.0
CloseApplications=yes
RestartApplications=no
[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"
[Tasks]
Name: "desktopicon"; Description: "바탕 화면에 CrowLink 바로가기 만들기"; Flags: checkedonce
[Files]
Source: "..\artifacts\publish-1.8.2-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\docs\CrowLink-1.8.2-Manual-KO.html"; DestDir: "{app}\Docs"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
[Icons]
Name: "{group}\CrowLink"; Filename: "{app}\CrowLink.exe"
Name: "{group}\CrowLink 사용 설명서"; Filename: "{app}\Docs\CrowLink-1.8.2-Manual-KO.html"
Name: "{commondesktop}\CrowLink"; Filename: "{app}\CrowLink.exe"; Tasks: desktopicon
[Run]
Filename: "{app}\CrowLink.exe"; Description: "CrowLink 1.8.2 실행"; Flags: nowait postinstall skipifsilent

