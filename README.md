# CrowLink 1.8.1

Windows PC 간 연결·입력 공유·파일 전송과 휴대폰 브라우저 터치패드를 제공하는 WPF 앱입니다. © 2026 CrowScienceLab · MIT License.

## 설치와 메뉴

[최신 릴리스](https://github.com/CrowScienceLab/CrowLink/releases/latest)에서 일반 PC는 win-x64, Windows ARM PC는 win-arm64 설치 파일을 선택하세요. .NET 런타임이 포함됩니다.

- **Connect**: 같은 사설 LAN의 장치를 검색하고 연결을 승인합니다. 첫 검색 장치와 이전 연결 성공 장치를 우선 표시합니다.
- **Control**: 승인된 PC 사이의 마우스·키보드 입력 공유. 화면 배치를 확인하고 공유를 시작합니다. Ctrl+Alt+Esc로 중지합니다.
- **Share**: 여러 파일·폴더와 텍스트·PNG 클립보드 전송, 완료 알림, 수신 파일의 탐색기 복사 드래그.
- **Quick**: 양쪽 설정에서 허용한 연결에서 지원 문서·미디어·ZIP 한 파일을 건별 승인 없이 복사합니다. 자동 실행하거나 송신 원본을 삭제하지 않습니다. ZIP 내부 안전성을 보장하지 않습니다.
- **Mobile**: QR·임시 코드·PC 승인으로 연결하는 터치패드, 펜, 레이저와 화이트보드, 브라우저 범위의 텍스트·이미지 교환.

기본 테마는 Black Crow이며 White & Light Pink도 선택할 수 있습니다. 업데이트 확인·설치 파일 다운로드는 **설정 → 앱 업데이트**에 있습니다.

[한국어 사용 설명서](docs/CrowLink-1.8.1-Manual-KO.html) · [1.8.1 작업 및 검증 내역](docs/RELEASE-v1.8.1.md)

## 개발 및 검증

Windows와 .NET SDK 10.0.302, 모바일 스크립트 테스트용 Node.js를 사용합니다.

```powershell
dotnet restore CrowLink.sln
dotnet build CrowLink.sln -c Release
dotnet run --project src/CrowLink.App/CrowLink.App.csproj
dotnet run --project tests/CrowLink.Tests/CrowLink.Tests.csproj -c Release
node tests/mobile-gestures.cjs
```

설치 파일 제작은 [installer/README.md](installer/README.md)를 참고하세요.

## 제한 및 보안

기본 포트: PC TCP 45100, 검색 UDP 45101, Mobile TCP 45102. 신뢰하는 사설망에서 필요한 통신만 허용하세요. 학교·게스트 Wi-Fi의 단말 격리가 있으면 같은 SSID여도 연결되지 않을 수 있습니다. 인터넷 포트 포워딩은 권장하지 않습니다. Mobile HTTP/WS는 암호화되지 않습니다.

휴대폰의 카카오톡 등 다른 앱을 PC가 직접 제어하지 않습니다. 브라우저에서 받은 글·이미지를 복사/저장해 다른 앱에 사용합니다. PC 화면의 주기적 미리보기는 제거했고 펜 영역은 3×3 가이드와 로컬 필기 궤적을 표시합니다.

일반 권한 앱은 관리자 권한 창을 제어할 수 없고 Ctrl+Alt+Delete는 전달하지 않습니다. Quick은 원격 OLE 객체를 연결하는 완전한 파일 관리자 확장이 아니며 수신 폴더에 복사합니다. 자동 테스트 통과는 물리 PC 간 입력 공유, ARM 하드웨어 및 모든 휴대폰 브라우저 검증 완료를 의미하지 않습니다.
