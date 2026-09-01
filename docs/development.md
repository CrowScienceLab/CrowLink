# 개발 및 검증

## 준비

- Windows 11 x64
- .NET 10 SDK
- Visual Studio 2022를 사용할 경우 **.NET 데스크톱 개발** 워크로드

```powershell
dotnet --info
dotnet restore .\CrowLink.sln
dotnet build .\CrowLink.sln -c Debug
dotnet run --project .\tests\CrowLink.Tests\CrowLink.Tests.csproj
```

SDK가 없고 런타임만 설치된 시스템에서는 빌드할 수 없습니다. 런타임 설치 유무와 SDK 설치 유무는 별개입니다.

## 두 PC 수동 시나리오

두 장치에서 같은 빌드를 실행하고 검색, 거부, 승인, 재연결을 차례로 확인합니다. 1KB, 50~100MB, 1GB 이상, 파일 10개, 중첩 폴더를 보내고 원본과 수신 파일의 크기와 해시를 비교합니다. 같은 이름 파일을 미리 둔 뒤 `(1)` 이름으로 저장되는지 확인합니다. 전송 중 한 프로세스를 종료해 다른 프로세스가 종료되지 않고 연결 손실을 표시하는지도 확인합니다.

Mobile Touchpad는 PC와 휴대폰을 같은 Wi-Fi에 둔 뒤 MOBILE 메뉴에서 서버를 시작합니다. QR 접속, 틀린 코드 거부, 올바른 코드 후 PC 거부·승인, 한 손가락 이동·탭, 두 손가락 탭·상하/좌우 스크롤, 더블탭 드래그를 순서대로 확인합니다. 화면 회전, 브라우저 백그라운드 전환, Wi-Fi 끄기, PC의 연결 종료와 `Ctrl+Alt+Esc` 뒤에 마우스 버튼이 눌린 상태로 남지 않아야 합니다. 표시되는 events/s와 평균 latency도 기록합니다.

Explorer Bridge는 파일, 빈 폴더, 중첩 폴더, 같은 이름 항목, 1GB 파일을 각각 OLE Lab에 놓고 수신 승인을 시험합니다. 준비 완료 카드에서 바탕 화면과 일반 Explorer 폴더로 끌어 대상 파일이 생성되고 `Downloads\CrowLink` staging 원본이 삭제되는지 확인합니다. 드래그를 Esc로 취소하면 staging은 유지되어야 합니다. 요청 거부, 승인 시간 초과, 전송 중 연결 해제 시 카드는 드래그 불가 상태여야 합니다.

경로 검사는 자체 테스트에서 `..`와 절대 경로를 다룹니다. 실제 네트워크 변조 테스트에서는 `FILE_METADATA.relativePath`에 `..\\..\\Windows` 등을 넣고 `%USERPROFILE%\Downloads\CrowLink` 밖에 파일이 생기지 않는지 확인합니다.

## 방화벽

CrowLink는 관리자 권한을 요청하거나 규칙을 자동 생성하지 않습니다. Windows 보안 경고가 표시되면 신뢰하는 사설 네트워크에 한해 허용합니다. 수동 규칙이 필요하면 TCP 45100, UDP 45101과 Mobile TCP 45102의 인바운드를 사설 프로필과 CrowLink 실행 파일로 제한합니다. 포트를 설정에서 변경하면 규칙도 맞춰야 합니다.

## 알려진 제한

- TCP payload는 아직 TLS로 암호화되지 않습니다.
- 전송 재개, 체크섬 검증, 대역폭 제한, 동시 batch 큐 제어는 없습니다.
- 신뢰 장치 해제 UI는 아직 없으며 `settings.json`의 `TrustedDevices`를 관리해야 합니다.
- 설정한 장치 이름과 포트는 앱 재시작 후 discovery/server에 반영됩니다.
- 테마 변경은 저장 즉시 적용되며 앱 재시작 후에도 유지됩니다.
- 1.5의 PC 간 protocol version은 5이므로 파일·Explorer 패키지·클립보드·키보드·마우스 시험 시 두 PC 모두 같은 버전을 사용하는 것이 안전합니다.
- Mobile Web은 현재 HTTP/WS이며 TLS가 아닙니다. 사설망 제한, 임시 코드와 PC 승인을 적용하지만 공용 Wi-Fi에서는 사용하지 않아야 합니다.
- iOS/Android 네이티브 앱, 백그라운드 자동 재연결, Virtual HID/Precision Touchpad 등록은 PHASE 2 이후 범위입니다.
- UDP 브로드캐스트가 차단되거나 VLAN이 다르면 자동 검색이 되지 않습니다.
- Windows 경로의 최대 길이와 대상 디스크 용량은 운영체제 오류로 보고됩니다.
- 원격 키보드·마우스는 Windows `SendInput`을 사용하므로 일반 권한 CrowLink에서 관리자 권한 창을 조작할 수 없습니다.
- `Ctrl+Alt+Delete`는 Windows 보안 화면 전용이므로 전달하지 않으며, `Ctrl+Alt+Esc`는 CrowLink 비상 종료로 예약됩니다.
- 현재 모니터 배치는 선택한 상대 PC 전체 가상 데스크톱을 왼쪽 또는 오른쪽의 한 영역으로 취급합니다. 모니터 사이의 비직사각형 빈 공간을 위한 사용자 지정 연결선은 없습니다.
- 앱은 Per-Monitor V2 DPI aware입니다. 100%, 125%, 150% 등 서로 다른 배율의 모니터에서 창 크기와 포인터 위치를 수동 확인해야 합니다.
- Control 화면에서 한 모니터 카드를 드래그했을 때 같은 PC의 모든 모니터가 함께 이동하고, 재시작 후 장치별 배치가 유지되는지 확인합니다.
- Connect 및 Control 풀다운을 펼쳐 팝업 배경과 일반·선택·hover 항목의 글자가 모두 다크 팔레트에서 읽히는지 확인합니다.
- Explorer Bridge는 원격 OLE 포인터를 네트워크로 마샬링하지 않습니다. 파일을 먼저 `%USERPROFILE%\Downloads\CrowLink`에 확정한 뒤 로컬 `CF_HDROP`으로 복사합니다.
- OLE 출력은 안전한 copy effect만 요청하고, 성공 결과가 돌아온 뒤 CrowLink가 package에 속한 staging 원본만 별도로 삭제해 이동처럼 동작합니다.
