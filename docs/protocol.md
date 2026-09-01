# CrowLink 프로토콜 5

## 전송 프레이밍

모든 정수는 network byte order(big endian)입니다.

| 필드 | 크기 | 설명 |
|---|---:|---|
| Message Type | 1 byte | 메시지 종류 |
| Payload Length | 4 bytes | payload 바이트 수, 최대 8 MiB |
| Payload | 가변 | JSON UTF-8 또는 `FILE_CHUNK` 이진 데이터 |

한 번의 `ReadAsync`가 한 메시지를 반환한다고 가정하지 않습니다. 헤더와 선언된 payload 길이를 각각 정확히 채울 때까지 읽고, EOF나 잘못된 길이는 연결 단위 프로토콜 오류로 처리합니다.

## 핸드셰이크

발신자는 `HELLO`를 보내고 수신자의 `HELLO`를 받은 뒤 `PAIR_REQUEST`를 보냅니다. 수신자는 과거 승인 이력과 관계없이 장치 이름·주소를 사용자에게 보여 주고 매번 승인을 받습니다. 결과는 `PAIR_ACCEPT` 또는 `PAIR_REJECT`입니다. HELLO와 페어 요청의 device id/name이 다르면 연결을 종료합니다. 핸드셰이크 제한 시간은 30초입니다.

## 메시지

| Type | Payload | 용도 |
|---|---|---|
| `HELLO` | protocolVersion, deviceId, deviceName | 버전과 장치 정체성 교환 |
| `PAIR_REQUEST` | deviceId, deviceName | 사용자 승인 요청 |
| `PAIR_ACCEPT`, `PAIR_REJECT` | deviceId, deviceName | 승인 결과 |
| `FILE_METADATA` | batchId, transferId, relativePath, size, lastWriteTime, isDirectory, isRoot, explorerPackageId | 파일·폴더 구조와 선택적 Explorer 패키지 연결 |
| `FILE_CHUNK` | transferId 16 bytes + data | 한 파일의 순서 보장 청크 |
| `FILE_COMPLETE` | batchId, transferId, isBatchComplete | 파일 또는 batch 완료 |
| `TRANSFER_CANCEL` | batchId, reason | batch 취소 |
| `PING`, `PONG` | 없음 | 연결 생존 확인용 예약 |
| `CLIPBOARD_TEXT` | text | 명시적으로 보낸 UTF-8 텍스트 클립보드 |
| `CLIPBOARD_IMAGE` | PNG bytes | 명시적으로 보낸 이미지 클립보드 |
| `MONITOR_INFO` | virtualWidth, virtualHeight, monitorCount, monitors[] | 각 모니터의 물리 영역·작업 영역·DPI·주 모니터 정보 |
| `MOUSE_CONTROL_REQUEST` | sessionId, entryEdge | 별도 원격 마우스 승인 요청 |
| `MOUSE_CONTROL_ACCEPT`, `MOUSE_CONTROL_REJECT` | sessionId | 원격 마우스 승인 결과 |
| `MOUSE_MOVE` | sessionId, x, y | 0~1 정규화 원격 좌표 |
| `MOUSE_BUTTON` | sessionId, button, isDown | 원격 버튼 상태 |
| `MOUSE_WHEEL` | sessionId, delta | 원격 휠 이동량 |
| `MOUSE_CONTROL_STOP` | sessionId | 마우스 공유 종료 |
| `KEYBOARD_INPUT` | sessionId, virtualKey, scanCode, isDown, isExtended | 원격 키다운·키업과 확장 키 정보 |
| `KEYBOARD_RESET` | sessionId | 원격 화면 이탈 시 눌린 키 전체 해제 |
| `EXPLORER_DRAG_OFFER` | packageId, items[] | 이름·종류·크기로 구성된 Explorer 패키지 제안 |
| `EXPLORER_DRAG_ACCEPT`, `EXPLORER_DRAG_REJECT` | packageId | 패키지별 사용자 승인 결과 |
| `EXPLORER_DRAG_READY` | packageId | 모든 root가 로컬 파일로 확정됨 |
| `EXPLORER_DRAG_ABORT` | packageId, reason | 승인·전송 실패 또는 취소 |
| `ERROR` | code, message | 프로토콜 오류용 예약 |

같은 TCP 연결의 청크는 순서대로 처리합니다. 파일 크기가 메타데이터의 선언 값을 넘거나 완료 시 값과 다르면 임시 파일을 폐기합니다. 알 수 없는 전송 ID, 8 MiB 초과 payload, 잘못된 JSON, 루트 밖 상대 경로는 오류입니다.

## 호환성

현재 protocol version은 `5`이며 다른 버전의 HELLO와 discovery packet은 받지 않습니다. TLS 추가 시 이 애플리케이션 프레임은 그대로 `SslStream` 위에서 동작합니다.

## Mobile Touchpad WebSocket

Mobile Touchpad는 PC 간 protocol 5 프레임을 변경하지 않고 TCP 45102의 HTTP/WebSocket으로 분리합니다. `/mobile`은 내장 HTML을 제공하고 `/ws`가 입력 세션을 처리합니다. 첫 메시지는 `hello(code, name)`이며 PC 승인 후 `paired(session)`을 반환합니다. 이후 `move(dx,dy)`, `click(button)`, `button(button,down)`, `scroll(delta,horizontal)`, `disconnect` 메시지마다 발급된 session id가 필요합니다. 최대 WebSocket payload는 16KiB이며 인증 전 입력, 세션 불일치, 비정상 수치와 허용 빈도를 넘는 입력은 무시합니다. 상세 규약은 `mobile-touchpad.md`에 있습니다.
