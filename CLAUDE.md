# CLAUDE.md — A.I. Usage Tracker

## 버전 관리 지침 (Version Management)

**사용자가 "업데이트" / 기능 변경 / 버그 수정 / 대시보드 수정 등을 요청할 때마다 작업 마지막 단계에서 반드시 버전을 올릴 것.**

### 버전 체계 (SemVer: MAJOR.MINOR.PATCH)

| 변경 성격 | 올릴 자리 | 예시 |
| --- | --- | --- |
| 호환성 깨는 큰 구조 변경, 프로젝트 리네임, 저장 포맷 변경 | MAJOR | `2.0.0 → 3.0.0` |
| 신규 기능, 대시보드 개편, 새 Provider, 새 UI 카드 | MINOR | `2.0.0 → 2.1.0` |
| 버그 수정, 문구 교정, 작은 UX 개선, 리팩터 | PATCH | `2.1.0 → 2.1.1` |

판단이 애매하면 **한 단계 낮은 쪽(보수적)** 으로 올린다.

### 매 업데이트마다 반드시 동기화할 위치

하나라도 빠지면 GitHub 업데이트 체크·설치 파일명·About 탭이 어긋나므로 **전부 같은 버전**으로 맞춘다.

1. **`AI_usage_tracker.csproj`** — 네 군데 전부 (빌드 버전 소스)
   - `<Version>2.1.0</Version>`
   - `<AssemblyVersion>2.1.0.0</AssemblyVersion>`
   - `<FileVersion>2.1.0.0</FileVersion>`
   - `<InformationalVersion>2.1.0</InformationalVersion>`
2. **`installer.iss`** — Inno Setup 세 줄
   - `AppVersion=2.1.0`
   - `AppVerName=A.I. Usage Tracker 2.1.0`
   - `OutputBaseFilename=AI_usage_tracker_Setup_v2.1.0`
3. **`Package.appxmanifest`** — MSIX `Identity Version="2.1.0.0"`
4. **`build-msix.ps1`** — 기본 파라미터 `[string]$Version = "2.1.0.0"`

`Services/UpdateService.cs`의 `CurrentVersion`은 `Assembly.GetExecutingAssembly().GetName().Version`을 읽기 때문에 별도 수정 불필요 — csproj만 맞추면 자동 반영.

### 업데이트 절차 (작업 완료 직전)

1. 코드 수정 완료 → 빌드 성공 확인
2. 위 4개 파일의 버전 문자열을 새 버전으로 교체
3. Release 빌드로 재확인 (`dotnet build -c Release`)
4. (설치 파일 배포 시) `publish` 재빌드 후 Inno Setup 재컴파일 — 출력 파일명에 새 버전이 반영됐는지 확인

### 커밋 메시지 규칙

버전 변경이 포함된 커밋은 제목에 `vX.Y.Z:` prefix를 붙인다. 예: `v2.1.0: Global dashboard overhaul`.
