#define MyAppName "CrowLink"
#define MyAppVersion "1.5.0"
#define MyAppPublisher "CrowScienceLab"
#define MyAppExeName "CrowLink.exe"
#define MyAppUrl "https://github.com/CrowScienceLab/CrowLink"

[Setup]
AppId={{8F0EA48E-1E8E-4FBA-9D18-10D05F9B5CB7}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppUrl}
AppSupportURL={#MyAppUrl}/issues
AppUpdatesURL={#MyAppUrl}/releases/latest
DefaultDirName={autopf}\CrowScienceLab\CrowLink
DefaultGroupName=CrowScienceLab\CrowLink
AllowNoIcons=yes
DisableDirPage=no
DisableProgramGroupPage=no
LicenseFile=..\LICENSE
OutputDir=..\artifacts
OutputBaseFilename=CrowLink-1.5-Setup-win-x64
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
UninstallDisplayName=CrowLink 1.5
UninstallDisplayIcon={app}\{#MyAppExeName}
VersionInfoVersion=1.5.0.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=CrowLink 1.5 설치 프로그램
VersionInfoProductName=CrowLink
VersionInfoProductVersion=1.5.0
SetupLogging=yes
CloseApplications=yes
RestartApplications=no
UsePreviousAppDir=yes
UsePreviousGroup=yes
UsePreviousTasks=yes

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "바탕 화면에 CrowLink 바로가기 만들기"; GroupDescription: "추가 바로가기:"; Flags: checkedonce

[Files]
Source: "..\artifacts\publish\CrowLink.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\docs\CrowLink-1.5-Manual-KO.html"; DestDir: "{app}\Docs"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion

[Icons]
Name: "{group}\CrowLink 1.5"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{group}\CrowLink 사용 설명서"; Filename: "{app}\Docs\CrowLink-1.5-Manual-KO.html"
Name: "{group}\CrowLink 제거"; Filename: "{uninstallexe}"
Name: "{commondesktop}\CrowLink 1.5"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "CrowLink 1.5 실행"; Flags: nowait postinstall skipifsilent
