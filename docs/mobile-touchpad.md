# CrowLink 1.5 Mobile Touchpad

## 연결 흐름

```text
PC MOBILE 메뉴 → TCP 45102 서버 시작 → QR의 /mobile 접속
→ 브라우저에서 6자리 임시 코드 제출 → PC 승인
→ 임의 session id 발급 → WebSocket 입력 → SendInput
```

QR에는 로컬 URL만 들어갑니다. 6자리 코드는 화면에 별도로 표시되고 승인된 연결 직후 교체됩니다. Mobile 장치는 기존 PC `TrustedDevices`에 영구 저장하지 않으며 새 브라우저 연결마다 PC 승인이 필요합니다.

## HTTP endpoint

- `GET /mobile`: CrowLink 실행 파일에 포함된 반응형 HTML/CSS/JavaScript 터치패드
- `GET /health`: 로컬 진단용 상태 응답
- `GET /ws`: WebSocket upgrade

외부 웹 리소스, CDN, JavaScript 패키지와 분석 서비스를 사용하지 않습니다. 페이지는 세로·가로 화면, `Pointer Events`, `touch-action: none`, 화면 wake lock을 지원합니다.

## WebSocket 메시지

모든 메시지는 UTF-8 JSON이며 최대 16KiB입니다.

| 방향 | type | 필드 | 의미 |
|---|---|---|---|
| Mobile→PC | `hello` | `code`, `name` | 임시 코드와 표시 이름 제출 |
| PC→Mobile | `paired` | `session`, `monitors`, `sensitivity`, `acceleration` | PC 승인 후 세션·화면 정보 발급 |
| PC→Mobile | `error` | `message` | 코드·승인·세션 오류 |
| Mobile→PC | `move` | `session`, `dx`, `dy`, `clientTime` | 상대 이동량 |
| Mobile→PC | `click` | `session`, `button` | 좌·우 클릭 |
| Mobile→PC | `button` | `session`, `button`, `down` | 드래그용 버튼 상태 |
| Mobile→PC | `scroll` | `session`, `delta`, `horizontal` | 수직·수평 휠 |
| Mobile→PC | `settings` | `session`, `mode`, `sensitivity`, `acceleration`, `monitorIndex` | 세션 입력 설정 |
| Mobile→PC | `pen` | `session`, `x`, `y`, `phase` | 선택 화면의 정규화 절대좌표 펜 입력 |
| Mobile→PC | `disconnect` | `session` | 세션 종료 |

브라우저는 이동과 스크롤을 animation frame마다 합산합니다. PC는 비정상 수치, 알 수 없는 버튼, 세션 불일치와 초당 180개 초과 입력을 버립니다. 감도는 0.5~5.0, 스크롤 속도는 0.5~3.0 범위이며 빠른 swipe에 제한된 가속을 적용합니다. 펜 모드는 선택한 모니터 비율을 유지한 입력 사각형을 사용하고 화면 좌표를 Windows 가상 데스크톱 좌표로 변환합니다.

## 제스처

- 한 손가락 이동: 상대 커서 이동
- 짧은 한 손가락 탭: 왼쪽 클릭
- 두 손가락 짧은 탭 또는 한 손가락 길게 누르기: 오른쪽 클릭
- 두 손가락 이동: 수직·수평 스크롤
- 더블탭 후 누른 채 이동: 왼쪽 버튼 드래그
- 하단 LEFT/RIGHT 버튼: 명시적 버튼 누름
- 펜 모드 한 손가락 누름·이동: 선택한 PC 모니터의 절대좌표 이동과 왼쪽 버튼 드로잉

`pointercancel`, 페이지 숨김, WebSocket 종료, PC 강제 종료 요청은 좌·우 버튼을 해제합니다. PC의 `Ctrl+Alt+Esc` 전역 단축키도 활성 모바일 세션을 즉시 끊습니다. 휴대폰 연결이 종료되면 PC 모바일 서버도 자동으로 중지되며 설정의 자동 시작 상태가 꺼집니다.

## 보안과 제한

- 기본은 RFC1918 IPv4, link-local, loopback 및 IPv6 local 주소만 허용합니다.
- 6자리 코드는 상수 시간 비교를 사용하고 다섯 번 실패하면 교체됩니다.
- 정확한 코드만으로는 입력할 수 없으며 PC 사용자가 매번 승인해야 합니다.
- 세션 id는 메모리에만 있으며 앱 재시작과 함께 폐기됩니다.
- 서버는 관리자 권한과 URL ACL이 필요 없는 `TcpListener`를 사용합니다.
- 현재 transport는 HTTP/WS로 암호화되지 않았습니다. 공용 Wi-Fi나 인터넷 포트 포워딩 환경에서 사용하면 안 됩니다.
- Windows `SendInput` 제약에 따라 일반 권한 CrowLink는 관리자 권한 창을 제어하지 못합니다.

PHASE 2는 Android 네이티브 앱, 자동 검색, 최근 PC, 햅틱, 키보드·프레젠테이션·미디어 기능을 별도 검증 후 추가합니다. Virtual HID와 Precision Touchpad 드라이버는 PHASE 3 연구 범위입니다.
