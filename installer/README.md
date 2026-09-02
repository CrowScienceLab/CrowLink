# CrowLink 1.5 Windows installer

정식 설치 파일은 Unicode 설치 마법사를 제공하는 Inno Setup 6으로 생성합니다.

```powershell
dotnet publish .\src\CrowLink.App\CrowLink.App.csproj -c Release -r win-x64 --self-contained true --no-restore `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -p:PublishReadyToRun=false `
  -p:DebugType=None -p:DebugSymbols=false -o .\artifacts\publish

& '.\.tools\Inno Setup 6\ISCC.exe' '.\installer\CrowLink-1.5.iss'
```

설치 프로그램은 기본적으로 관리자 승인을 받아
`C:\Program Files\CrowScienceLab\CrowLink`에 설치합니다. 설치 마법사에서 다른 폴더를 선택할 수 있고,
시작 메뉴 바로가기와 선택 가능한 바탕 화면 바로가기, Windows 설치된 앱의 정상 제거 항목을 만듭니다.

한국어와 영어 메시지 파일을 명시적으로 포함하며 CMD 또는 시스템 코드 페이지에 의존하지 않습니다.
