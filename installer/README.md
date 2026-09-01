# CrowLink installer

CrowLink 1.5의 설치 패키지는 Windows에 기본 포함된 IExpress를 사용해 생성합니다.

1. `dotnet publish`로 self-contained 단일 실행 파일을 만듭니다.
2. `artifacts/installer-stage`에 실행 파일, 설명서, 라이선스와 설치/제거 스크립트를 배치합니다.
3. 저장소 루트에서 `iexpress.exe /N /Q installer\CrowLink-1.5.sed`를 실행합니다.

설치 범위는 현재 사용자이며 관리자 권한을 요구하지 않습니다. 설치 위치는
`%LOCALAPPDATA%\Programs\CrowLink`입니다.
