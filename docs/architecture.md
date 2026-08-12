# CrowLink 1.0 아키텍처

## 전체 구조

WPF 화면은 `ViewModels`에만 바인딩되고, 네트워크·전송·설정 로직은 `Services`에 있습니다. `AppHost`가 서비스를 한 번 구성하고 종료 시 역순으로 정리합니다. 외부 패키지나 전역 가변 싱글턴을 사용하지 않습니다.

- `Discovery`: UDP 브로드캐스트 송수신, 장치 갱신, 8초 만료
- `Network`: TCP 수신/발신, HELLO 및 페어링 핸드셰이크, 연결 수명 관리
- `Security`: 로컬 신뢰 장치 확인, WPF 승인 콜백, 신뢰 정보 저장
- `Transfer`: 폴더 열거, 메타데이터, 청크 스트리밍, 수신 파일 확정
- `Clipboard`: 명시적 텍스트/PNG 송신과 수신 승인 요청
- `RemoteMouse`: 모니터·DPI 정보 교환, 별도 입력 승인, 저수준 키보드·마우스 추적과 `SendInput` 주입
- `Explorer`: 원격 패키지 승인·상태 관리, WPF OLE drop target 입력, 로컬 `CF_HDROP` drag source 출력
- `Theming`: Crow Black/Bright Sky Blue 리소스 팔레트의 런타임 전환
- `Settings`: `%LOCALAPPDATA%\CrowLink\settings.json`의 원자적 JSON 저장
- `Logging`: `%LOCALAPPDATA%\CrowLink\logs`의 5MB 제한 로그와 최대 4개 보관본

## 데이터 흐름

송신 측은 드롭된 각 루트를 하나의 batch로 열거하고 `FILE_METADATA` 다음에 1MB 청크를 순서대로 보냅니다. 수신 측은 메타데이터의 상대 경로를 정규화하고 수신 루트 내부인지 확인한 뒤 `.crowpart`에 기록합니다. 선언 크기와 실제 크기가 일치할 때만 최종 이름으로 이동합니다.

TCP 연결 하나에는 수신 루프가 하나만 존재합니다. 쓰기는 `ProtocolSerializer`의 비동기 잠금으로 직렬화되어 서로 다른 발신 작업의 프레임이 섞이지 않습니다. 연결 손실은 해당 연결만 정리하며 애플리케이션과 검색 루프는 계속 실행됩니다.

사용자 취소는 진행 중인 TCP 프레임을 중간에서 자르지 않습니다. 현재 청크 프레임의 쓰기를 끝낸 다음 `TRANSFER_CANCEL`을 전송해 수신 측 임시 스트림과 `.crowpart`를 정리합니다. 사용자 연결 해제는 선택한 peer 연결만 제거하며 discovery는 계속 동작합니다.

Explorer Bridge는 `EXPLORER_DRAG_OFFER` 승인 후 각 `FILE_METADATA`에 package id를 붙여 기존 파일 전송기를 재사용합니다. 수신 측은 각 root batch의 최종 로컬 경로를 수집하고 모두 완료된 경우에만 패키지를 드래그 가능 상태로 바꿉니다. 사용자가 카드를 끌면 WPF `DataObject`가 관리형 COM `IDataObject`를 제공하고, 자체 `IDropSource` 구현을 전달한 `Ole32.DoDragDrop`이 Explorer의 등록된 `IDropTarget`과 로컬 `CF_HDROP` 복사를 수행합니다. copy effect가 성공으로 반환된 뒤에만 수신 폴더 내부로 검증된 package root를 삭제하여 사용자 관점의 이동을 완성합니다.

원격 Explorer가 제공한 COM `IDataObject` 포인터는 프로세스/데스크톱 로컬 OLE drag loop의 일부이므로 TCP로 직렬화하지 않습니다. `CFSTR_FILEDESCRIPTOR/CFSTR_FILECONTENTS` 지연 스트림도 연구했지만 네트워크 오류가 OLE 동기 호출을 장시간 막을 수 있어, 최종 기능 시험판은 먼저 파일을 완전히 수신한 뒤 `CF_HDROP`으로 노출하는 방식을 선택했습니다.

## 보안 상태

1.0은 영구 랜덤 device id, 승인 이력, 입력 크기 제한, 프로토콜 버전 검사, canonical path 검사, reparse point 제외를 구현합니다. 기본 정책은 연결·클립보드·원격 입력·Explorer 패키지를 매번 확인하는 것입니다. 사용자가 설정에서 기능별 자동 승인을 명시적으로 켜면 해당 확인 단계만 생략합니다. 자동 승인 여부와 무관하게 파일·입력 메시지는 연결 성립 전에는 처리되지 않으며, 원격 입력은 승인된 peer와 session id가 일치할 때만 적용합니다.

저수준 키보드·마우스 훅은 제어 요청이 수락된 동안에만 설치되며 실제 입력은 포인터가 원격 화면에 들어간 동안만 차단합니다. 주입된 이벤트는 다시 송신하지 않도록 제외하고, 버튼·휠·정규화 좌표와 키다운·키업을 TCP 순서대로 보냅니다. 원격 화면에서 나올 때 `KEYBOARD_RESET`으로 남은 수정 키를 해제합니다. `Ctrl+Alt+Esc`는 로컬에서 세션을 중지하는 예약 단축키이고 `Ctrl+Alt+Delete`는 Windows 보안 제한으로 차단합니다. 일반 권한으로 실행한 CrowLink는 UIPI 제한 때문에 관리자 권한 창을 제어할 수 없습니다.

각 PC는 모니터별 물리 픽셀 영역, 작업 영역, 주 모니터 여부, DPI X/Y를 교환합니다. 프로세스는 Per-Monitor V2 DPI aware이며, 마우스 좌표는 WPF DIP가 아니라 전체 가상 데스크톱 물리 좌표를 0~1로 정규화하므로 서로 다른 배율과 해상도를 함께 사용할 수 있습니다.

Control의 Monitor topology는 이 정보를 Windows 디스플레이 설정과 유사한 사각형 묶음으로 투영합니다. 각 PC 내부의 실제 모니터 상대 좌표는 유지하고 PC 묶음만 드래그할 수 있습니다. 배치 위치는 장치 ID별 정규화 좌표로 저장되며, 원격 PC가 로컬 PC의 왼쪽/오른쪽 중 어디에 놓였는지에 따라 마우스 전환 경계를 자동 선택합니다.

현재 전송은 **암호화되지 않은 LAN TCP**입니다. TLS를 끄고 문제를 우회한 것이 아니라 전송 계층과 메시지 계층을 분리해 둔 1차 구현입니다. 다음 보완에서는 `TcpClient.GetStream()`을 인증된 `SslStream`으로 교체하고, 최초 승인 화면에서 인증서 공개키 지문을 장치 ID에 고정해야 합니다. 이 작업 전에는 신뢰할 수 없는 공용 LAN에서 사용하지 않아야 합니다.

## 향후 확장

`IDeviceDiscoveryService`를 통해 UDP를 mDNS로 교체할 수 있습니다. 화면 사이의 비직사각형 빈 공간 회피와 모니터별 사용자 지정 연결선은 이후 확장 대상입니다. 끊김 없는 Explorer 간 드래그가 필요하면 별도 Shell Extension과 원격 `CFSTR_FILECONTENTS` 스트리밍 프록시가 필요합니다.
