# CrowLink 1.5

**PC CONNECT / FILE SHARE / MOUSE KEYBOARD CONTROL / MOBILE TOUCHPAD**

© 2026 CrowScienceLab. CrowLink는 MIT License로 누구나 자유롭게 사용할 수 있는 무료 유틸리티입니다.

CrowLink는 같은 LAN의 Windows PC를 자동으로 찾고, 사용자가 승인한 장치 사이에서 파일과 폴더를 전송하는 경량 WPF 유틸리티입니다.

## 현재 구현 기능

메인 창 상단의 **? 도움말** 버튼을 누르면 Connect, Share, Control, Explorer의 기능 설명과 단계별 사용법, 요청 승인 정책 및 주요 주의사항을 확인할 수 있습니다.

- UDP 브로드캐스트 기반 주변 장치 검색과 타임아웃 처리
- TCP 연결 및 길이 프리픽스 메시지 프레이밍
- 기본은 매 요청 수동 승인, 설정에서 Connect·Share·Control·Explorer별 자동 승인 선택 가능
- 선택 장치 연결 해제
- Explorer에서 드롭한 파일·폴더를 보낼 목록에 추가한 뒤 명시적으로 전송 시작
- 고정 메모리 청크 스트리밍 방식의 파일 및 폴더 전송
- 송신 중 전송 취소와 수신 중 `.crowpart` 정리
- 전송률, 전송량, 속도, 완료·실패·취소 상태 표시
- 수신 중 `.crowpart` 임시 파일 사용 및 완료 후 원자적 이름 변경
- 중복 파일 자동 이름 변경과 수신 루트 밖 경로 쓰기 차단
- JSON 설정 저장과 크기 제한 로그 회전
- 까마귀를 연상시키는 near-black 셸, Crow 심볼, 선택 상태가 분명한 SVG path 내비게이션
- Connect와 Control을 포함한 모든 풀다운 팝업·항목의 다크 배경 및 고대비 글자색
- native 흰색 프레임을 제거한 compact dark window chrome과 실제 장치명·상태 풀다운
- 선택 장치로 텍스트·PNG 이미지 클립보드 전송
- 수신 클립보드를 적용하기 전 사용자 승인
- 파일과 클립보드 송수신을 함께 보여 주는 활동 기록
- 로컬·상대 PC의 각 모니터 해상도, 주 모니터, DPI 배율을 보여 주는 다중 모니터 UI
- 왼쪽/오른쪽 화면 경계를 이용한 전역 마우스 추적 및 원격 마우스 입력
- 원격 키보드와 `Alt+Tab`, `Ctrl+C` 같은 일반 단축키 전달
- 화면을 빠져나가거나 세션을 종료할 때 원격 수정 키 자동 해제
- 연결 승인과 별개의 원격 입력 승인, `Ctrl+Alt+Esc` 비상 종료
- Windows Per-Monitor V2 DPI 인식과 물리 픽셀 기반 가상 데스크톱 좌표 매핑
- Windows 디스플레이 설정형 Monitor topology: 실제 PC별 모니터 수·좌표·해상도·DPI를 사각형 비율로 표시
- PC 1/PC 2 모니터 묶음 드래그 배치, 상대 장치별 위치 저장, 좌우 배치에 따른 입력 경계 자동 선택
- Explorer에서 CrowLink로 `FileDrop/CF_HDROP` 수신
- 상대 PC 승인 후 기존 청크 전송과 연결된 원격 Explorer 패키지
- 수신 완료 패키지를 OLE `IDataObject`·`IDropSource`·`DoDragDrop`으로 Explorer에 복사
- 앱 설치 없이 휴대폰 브라우저로 사용하는 Mobile Web Touchpad
- QR 접속, 6자리 임시 코드, PC 매회 승인과 메모리 전용 세션
- 한 손가락 커서·탭, 두 손가락 오른쪽 클릭·스크롤, 더블탭 드래그
- 터치패드/펜 모드, 대상 모니터 절대좌표 매칭, 0.5~5.0배 감도와 선택형 가속
- 연결 시 모바일 인증창 닫기·전체화면 전환, 모바일/PC 연결 상태 점멸 표시
- 연결 종료·브라우저 취소·네트워크 단절 시 마우스 버튼 해제와 PC 모바일 서버 자동 중지
- 도움말의 GitHub 최신 버전 확인 및 설치 파일 다운로드 연결

기본 포트는 PC TCP `45100`, 검색 UDP `45101`, Mobile TCP `45102`이며 기본 수신 폴더는 `%USERPROFILE%\Downloads\CrowLink`입니다.

## 개발 환경과 빌드

Windows 11과 .NET 10 SDK가 필요합니다. 저장소 루트에서 다음 명령을 실행합니다.

```powershell
dotnet restore .\CrowLink.sln
dotnet build .\CrowLink.sln -c Release
dotnet run --project .\src\CrowLink.App\CrowLink.App.csproj
```

## 1.5 최소 용량 독립 실행 배포

개발 PC가 아닌 Windows 10/11 x64 PC에서도 별도 .NET 설치 없이 실행할 수 있도록 런타임을 포함한 단일 실행 파일로 배포합니다.

```powershell
dotnet restore .\src\CrowLink.App\CrowLink.App.csproj -r win-x64 --source https://api.nuget.org/v3/index.json
dotnet publish .\src\CrowLink.App\CrowLink.App.csproj -c Release -r win-x64 --self-contained true --no-restore -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:PublishReadyToRun=false -p:DebugType=None -p:DebugSymbols=false -p:PublishDir=.\artifacts\CrowLink-1.5\
```

프로젝트는 한국어·영어 위성 리소스만 포함하도록 제한합니다. 최종 배포물은 설명서와 제거 기능을 포함하는 `artifacts\CrowLink-1.5-Setup-win-x64.exe`와 휴대용 `artifacts\CrowLink-1.5-win-x64.zip`입니다. 둘 다 별도 .NET 설치가 필요 없습니다.

외부 NuGet 패키지는 사용하지 않습니다. 간단한 자체 검증 스위트는 다음과 같이 실행합니다.

```powershell
dotnet run --project .\tests\CrowLink.Tests\CrowLink.Tests.csproj -c Release
```

현재 소스는 .NET SDK 10에서 Release 빌드와 자체 및 WPF 회귀 테스트를 수행합니다.

전송 취소는 현재 송신 측에서 실행합니다. 전송 중이던 파일의 수신 임시 파일은 삭제되며, 같은 폴더 전송에서 이미 완료된 파일까지 되돌리지는 않습니다.

클립보드는 자동 감시하지 않으며 송신자가 `텍스트 보내기` 또는 `이미지 보내기`를 눌러야 합니다. 수신 적용은 기본적으로 승인을 요청하고, 설정에서 Share 자동 적용을 켜면 확인창을 생략합니다. 텍스트는 최대 1,000,000자, 이미지는 PNG 기준 최대 7MB입니다.

## 두 PC에서 확인하기

1. 두 PC를 같은 LAN에 연결하고 CrowLink를 실행합니다.
2. Windows 방화벽에서 TCP 45100과 UDP 45101의 사설 네트워크 인바운드 통신을 허용합니다. Mobile Touchpad를 사용할 때는 TCP 45102도 허용합니다.
3. 주변 장치에서 상대 PC를 선택하고 **선택한 장치에 연결**을 누릅니다.
4. 상대 PC에서 요청 장치 이름과 주소를 확인한 뒤 승인합니다.
5. 파일이나 폴더를 전송 영역에 놓아 보낼 목록을 확인합니다.
6. 연결된 장치를 선택하고 **전송 시작**을 누릅니다.

입력 공유는 상대 PC를 연결한 뒤 화면 배치 방향(왼쪽/오른쪽)을 선택하고 **입력 공유 시작**을 누릅니다. 상대 PC에서 키보드·마우스 제어 요청을 별도로 수락한 다음 선택한 화면 경계로 포인터를 밀면 상대 PC로 넘어갑니다. 원격 화면에 있는 동안 키보드와 일반 단축키도 상대 PC로 전달됩니다. 언제든 `Ctrl+Alt+Esc`로 공유 세션을 즉시 종료할 수 있습니다.

`Ctrl+Alt+Delete`는 Windows 보안 화면 전용 조합이므로 원격 전송하지 않습니다. 일반 권한 CrowLink에서 관리자 권한으로 실행된 창을 조작할 수도 없습니다.

## Explorer Bridge 시험 방법

1. 두 PC에서 1.5를 실행하고 연결을 승인합니다.
2. 송신 PC 상단의 **EXPLORER** 아이콘을 선택합니다.
3. Explorer 파일·폴더를 왼쪽 OLE Lab 영역에 놓습니다.
4. 수신 PC에서 Explorer 패키지 요청을 승인합니다.
5. 수신이 끝나면 오른쪽 패키지 카드에 **Explorer로 드래그할 준비 완료**가 표시됩니다.
6. 그 카드를 잡아 원하는 로컬 Explorer 폴더에 놓습니다.
7. Explorer 복사가 성공하면 CrowLink가 `Downloads\CrowLink`의 해당 staging 원본을 삭제해 이동을 마칩니다.

Windows OLE drag loop는 한 데스크톱 안에서 `IDataObject` 포인터를 `IDropSource`와 대상 창의 `IDropTarget` 사이에 전달합니다. 이 COM 포인터를 네트워크로 직접 전달하지 않고, 파일을 먼저 상대 PC의 CrowLink 수신 폴더에 완전히 저장한 다음 로컬 `CF_HDROP` 객체로 Explorer에 제공합니다. 따라서 1.5는 한 번의 끊김 없는 PC 간 드래그가 아니라 **놓기 → 전송 → 상대 패키지를 다시 드래그 → staging 삭제** 방식입니다.

`Share`는 목록 검토·취소·진행 기록을 제공하며 설정된 수신 폴더에 파일을 영구 복사합니다. Share 자동 실행을 켜면 연결된 상태에서 드롭한 목록을 즉시 전송하고, 수신 클립보드도 확인창 없이 적용합니다. `Explorer`는 빠른 위치 지정 이동을 위한 시험 기능으로, PC 2에서 원하는 폴더로 드롭이 성공하면 임시 수신 원본을 자동 삭제합니다.

CrowLink는 관리자 권한을 요구하거나 방화벽 규칙을 자동 변경하지 않습니다.

## Mobile Touchpad 사용 방법

1. PC와 휴대폰을 같은 Wi-Fi에 연결합니다.
2. CrowLink의 **MOBILE** 메뉴에서 `서버 시작`을 누릅니다.
3. Windows 방화벽 알림이 나오면 신뢰하는 사설 네트워크에 한해 허용합니다.
4. 휴대폰 카메라로 QR을 스캔하거나 표시된 주소를 브라우저에서 엽니다.
5. PC 화면의 6자리 코드를 입력하고 PC에 나타난 승인창에서 장치 이름과 주소를 확인해 허용합니다.
6. 터치패드 모드에서는 한 손가락 이동·탭, 두 손가락 탭·스크롤, 더블탭 드래그를 사용하고 감도·가속을 조절합니다.
7. 펜 모드에서는 대상 모니터를 고르고 가로 방향에서 휴대폰 입력 영역과 PC 화면을 절대좌표로 매칭합니다.
8. 휴대폰의 붉은 `중지`, PC의 중지 버튼 또는 `Ctrl+Alt+Esc`로 입력을 차단합니다. 연결 종료 후 PC 모바일 서버도 자동으로 중지됩니다.

Mobile 서버 기본 포트는 TCP `45102`입니다. 모바일 입력은 PC 장치 목록의 신뢰 기록이나 자동 승인 옵션을 사용하지 않고, 새 브라우저 세션마다 임시 코드와 PC 승인을 모두 요구합니다. 자세한 구조와 이벤트 규약은 [docs/mobile-touchpad.md](docs/mobile-touchpad.md), 휴대폰 실기기 확인 순서는 [docs/mobile-touchpad-test-checklist.md](docs/mobile-touchpad-test-checklist.md)를 참고하세요.

## 현재 범위 밖 기능

Explorer의 원본 `IDataObject`를 유지한 단일 연속 PC 간 드래그, Shell Extension, `Ctrl+Alt+Delete` 전송, 자동 클립보드 감시, 인터넷/NAT 연결, 클라우드·계정, Windows Service, 무인 자동 설치 업데이트, Android/iOS 네이티브 앱 및 Virtual HID/Precision Touchpad 드라이버는 구현하지 않았습니다. 업데이트 확인은 GitHub 릴리스 페이지와 설치 파일을 사용자 동의로 여는 방식입니다.

설계와 제한 사항은 [docs/architecture.md](docs/architecture.md), 프로토콜은 [docs/protocol.md](docs/protocol.md), 개발 및 테스트 절차는 [docs/development.md](docs/development.md)를 참고하세요.
