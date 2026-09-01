[Version]
Class=IEXPRESS
SEDVersion=3

[Options]
PackagePurpose=InstallApp
ShowInstallProgramWindow=1
HideExtractAnimation=0
UseLongFileName=1
InsideCompressed=0
CAB_FixedSize=0
CAB_ResvCodeSigning=0
RebootMode=N
InstallPrompt=CrowLink 1.5를 현재 사용자 계정에 설치하시겠습니까?
DisplayLicense=LICENSE
FinishMessage=CrowLink 1.5 설치가 완료되었습니다.
TargetName=artifacts\CrowLink-1.5-Setup-win-x64.exe
FriendlyName=CrowLink 1.5 Setup
AppLaunched=install.cmd
PostInstallCmd=<None>
AdminQuietInstCmd=install.cmd
UserQuietInstCmd=install.cmd
SourceFiles=SourceFiles

[Strings]
FILE0=install.cmd
FILE1=CrowLink-Uninstall.cmd
FILE2=CrowLink.exe
FILE3=CrowLink-1.5-Manual-KO.html
FILE4=LICENSE

[SourceFiles]
SourceFiles0=artifacts\installer-stage\

[SourceFiles0]
%FILE0%=
%FILE1%=
%FILE2%=
%FILE3%=
%FILE4%=
