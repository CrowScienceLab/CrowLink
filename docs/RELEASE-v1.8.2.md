# CrowLink 1.8.2 — Windows 공용 업데이트 수정

## 수정 사항

- ARM OS에서 ARM64 설치 파일 링크를 우선 선택하던 업데이트 로직을 제거했습니다.
- 업데이트 버튼은 항상 GitHub 최신 릴리스 페이지를 열어 사용자가 Windows 공용 설치 파일을 확인하고 다운로드하도록 합니다.
- 배포 파일은 `CrowLink-1.8.2-Setup-win-x64.exe` 한 종류입니다. Intel/AMD Windows와 x64 앱 에뮬레이션을 지원하는 Windows 11 ARM에서 동일하게 사용합니다.
- ARM64 전용 빌드·설치 파일은 1.8.2부터 별도로 만들거나 GitHub에 게시하지 않습니다.
- 기존 AppId를 유지하므로 설치 파일을 실행하면 이전 CrowLink 설치 위치에 업그레이드됩니다.

## 사용 방법

1. CrowLink 설정에서 **업데이트 확인 · GitHub 열기**를 누릅니다.
2. 새 버전 안내에서 **예**를 눌러 GitHub 최신 릴리스 페이지를 엽니다.
3. `CrowLink-1.8.2-Setup-win-x64.exe`를 다운로드하고 실행합니다.
4. 설치 마법사를 완료하면 기존 버전이 1.8.2로 갱신됩니다.

Windows 10 ARM은 x64 앱 에뮬레이션을 지원하지 않으므로 이 공용 x64 설치 파일의 지원 대상이 아닙니다.

## 검증 범위

- Release 회귀 테스트 및 모바일 제스처 테스트
- x64 self-contained publish와 Inno Setup 설치 파일 생성
- 설치 파일 ProductVersion, 파일 크기, SHA-256 확인
- GitHub 최신 릴리스의 자산 이름·크기·SHA-256 확인

실제 Windows 11 ARM 장치에서의 설치와 입력 공유는 별도 실기기 확인이 필요합니다.

