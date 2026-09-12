# CrowLink 1.8.2 Windows 공용 설치 파일

.NET SDK 10.0.302와 Inno Setup 6을 사용합니다. x64 한 종류만 생성하며 Windows 11 ARM에서는 OS의 x64 에뮬레이션으로 실행합니다.

```powershell
dotnet publish src/CrowLink.App/CrowLink.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:PublishReadyToRun=false -p:DebugType=None -p:DebugSymbols=false -o artifacts/publish-1.8.2-x64
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
& ISCC.exe installer/CrowLink-1.8.2.iss
if ($LASTEXITCODE -ne 0) { throw 'Installer failed' }
```

출력은 `artifacts/CrowLink-1.8.2-Setup-win-x64.exe` 하나입니다. 설치 위치 선택, 바탕 화면·시작 메뉴 바로가기, 한국어 설명서, 제거 기능을 포함합니다. 설치 시 관리자 승인을 받으며 .NET 별도 설치는 필요 없습니다.

배포 전 회귀 테스트와 실제 두 PC/휴대폰 검증을 구분해 기록하세요. 코드 서명이 없는 설치 파일에는 Windows SmartScreen 경고가 표시될 수 있습니다.
