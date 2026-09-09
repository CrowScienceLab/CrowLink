# 개발 작업 위치

이 PC의 개발 기준 폴더는 `D:\App coding`, CrowLink 저장소는 `D:\App coding\CrowLink`입니다.

- 소스와 Git 이력: 저장소 루트 및 `src`, `tests`
- 설치 파일과 검증 결과: `artifacts` (Git 제외)
- 로컬 개발 도구와 SDK: `.tools` (Git 제외)
- 개발용 스크립트: `tools`
- 원격 저장소: https://github.com/CrowScienceLab/CrowLink.git

Codex에서 이 저장소를 작업 폴더로 선택한 뒤 작업합니다. 이전 C: 임시 작업 폴더를 소스 저장 위치로 사용하지 않습니다. 다른 개발자는 자신의 경로로 체크아웃할 수 있으며 빌드 스크립트는 저장소 기준 상대 경로를 사용합니다.

GitHub에는 Windows 로컬 경로를 등록하는 보안 설정이 없습니다. 드라이브 이동만으로 권한 문제가 해결되는 것도 아닙니다. 소유자 계정으로 Git을 실행하고, 승인된 작업 폴더만 허용하며 `safe.directory=*` 같은 전체 신뢰 설정이나 광범위한 ACL 변경을 사용하지 않습니다. 자격 증명은 저장소에 저장하지 않습니다.
