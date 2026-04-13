# Claude Usage Tracker 기획/개발 명세서

> 이 문서는 Claude.ai 사용량을 실시간으로 추적하는 데스크톱 애플리케이션을 처음부터 개발하기 위한 완전한 명세서입니다. 기획 배경, 기술적 제약, 아키텍처, 구현 세부사항, 알려진 이슈와 해결 방법까지 모두 포함합니다.

---

## 목차

1. [프로젝트 개요](#1-프로젝트-개요)
2. [기술적 제약과 선택의 배경](#2-기술적-제약과-선택의-배경)
3. [아키텍처](#3-아키텍처)
4. [기술 스택](#4-기술-스택)
5. [기능 명세](#5-기능-명세)
6. [UI/UX 명세](#6-uiux-명세)
7. [데이터 구조](#7-데이터-구조)
8. [API 상세](#8-api-상세)
9. [파일 구조](#9-파일-구조)
10. [빌드 및 배포](#10-빌드-및-배포)
11. [알려진 이슈와 해결 방법](#11-알려진-이슈와-해결-방법)
12. [버전 규칙](#12-버전-규칙)
13. [향후 확장 가능성](#13-향후-확장-가능성)

---

## 1. 프로젝트 개요

### 1.1 목적

Claude.ai 구독자(Pro, Max 등)가 자신의 사용량을 실시간으로 모니터링할 수 있는 Windows/macOS 데스크톱 애플리케이션.

### 1.2 대상 사용자

- Claude.ai 유료 구독자
- 5시간 세션 제한, 주간 한도 등을 자주 확인해야 하는 사용자
- 브라우저를 열어 설정 페이지로 이동하는 과정을 번거롭게 느끼는 사용자
- 데스크톱에서 백그라운드로 사용량을 확인하고 싶은 사용자

### 1.3 핵심 가치

1. **즉시성**: 트레이 아이콘 + 앱 창에서 한눈에 사용량 확인
2. **시각적 명확성**: 숫자보다는 원형 타이머, 진행바로 직관적 표시
3. **이력 관리**: 과거 사용 패턴 추적을 통한 사용 습관 파악
4. **저침습성**: 시스템 트레이 상주, 필요할 때만 창 열기

### 1.4 필수 기능 요약

- 5시간 세션 사용률 및 남은 시간 표시
- 주간 한도 (전체 모델 / Sonnet 별도) 표시
- 추가 사용량(Extra Usage) 크레딧 표시
- 사용량 변화량 막대 그래프
- 시스템 트레이 상주
- 사용률 임계값 알림
- 자동 로그인 감지 및 재로그인 지원

---

## 2. 기술적 제약과 선택의 배경

### 2.1 핵심 제약: Claude.ai 사용량 API는 비공식이다

Claude.ai는 사용량 조회를 위한 공식 API를 제공하지 않습니다. 내부적으로 사용되는 엔드포인트는 존재하지만:

- 공식 문서가 없음
- 응답 형식이 사전 통보 없이 변경될 수 있음
- 인증은 claude.ai 세션 쿠키에 의존
- 외부 도메인에서의 호출은 CORS로 차단됨

이 제약이 **모든 기술적 선택의 근간**이 됩니다.

### 2.2 왜 순수 데스크톱 앱(Native)이 아닌 Electron + Webview인가

**옵션 A: 순수 네이티브 앱 (C# / Swift 등)**
- 쿠키를 외부에서 관리해야 함 (브라우저에서 추출 → 저장 → 헤더에 수동 주입)
- 세션 만료 시 사용자가 직접 브라우저에서 쿠키 추출 반복
- CORS 문제: fetch 요청 시 Origin 헤더가 claude.ai가 아니므로 차단 가능성
- 유지보수 지옥

**옵션 B: Electron + 내장 Webview (선택됨)**
- `partition="persist:claude"`로 쿠키 자동 영구 저장
- 사용자가 앱 안에서 한 번 로그인하면 끝
- `webview.executeJavaScript()`로 claude.ai 페이지 **내부에서** fetch 실행 → Same-origin, 쿠키 자동 포함, CORS 없음
- 브라우저 확장 프로그램과 동일한 원리지만 데스크톱 앱 껍데기

**결론**: Electron은 무겁지만(~90MB) 이 유스케이스에서는 거의 유일한 현실적 선택.

### 2.3 왜 Chrome Extension이 아닌가

Chrome Extension이 가장 자연스러운 방법이지만 사용자가 요청한 것은 **"데스크톱 애플리케이션"** 이었습니다. 다음 기능들은 Chrome Extension으로는 불가능하거나 어색합니다:

- 시스템 트레이 상주
- 네이티브 OS 알림 (푸시 알림)
- 독립적인 창 크기 조절
- 브라우저를 닫아도 동작

### 2.4 Webview의 가시성 처리

Webview를 사용자에게 보여주지 않아야 합니다. 사용자는 Claude.ai를 앱 안에서 브라우징할 일이 없고, 단지 로그인 쿠키만 필요합니다.

**접근**:
- 백그라운드 webview (`bgView`): 1×1 픽셀, 화면 밖(`left: -9999px`), `visibility: hidden`. 평소엔 숨겨져 있으며 fetch 용도로만 사용.
- 로그인 webview (`loginView`): 로그인이 필요할 때만 전체화면 오버레이로 표시. 로그인 완료 감지 시 자동 숨김.

두 webview는 **같은 partition**(`persist:claude`)을 공유하므로 쿠키가 공유됩니다.

---

## 3. 아키텍처

### 3.1 프로세스 구조

```
┌──────────────────────────────────────────────────────────────┐
│  Main Process (main.js)                                       │
│  - 앱 생명주기 관리                                              │
│  - BrowserWindow 생성                                          │
│  - 시스템 트레이 관리                                            │
│  - electron-store를 통한 영구 저장소                             │
│  - IPC 핸들러 (get-history, save-snapshot, open-external 등)   │
│  - shell.openExternal (외부 링크)                              │
│  - Notification (네이티브 알림)                                 │
└──────────────┬───────────────────────────────────────────────┘
               │ IPC (contextBridge 통해 안전하게 노출)
               ▼
┌──────────────────────────────────────────────────────────────┐
│  Preload Script (preload.js)                                  │
│  - contextIsolation: true 환경에서 안전한 API 노출              │
│  - window.api.* 로 제한된 기능만 renderer에 노출                │
└──────────────┬───────────────────────────────────────────────┘
               │
               ▼
┌──────────────────────────────────────────────────────────────┐
│  Renderer Process (renderer.js)                              │
│  ┌─────────────────┐   ┌─────────────────────────────────┐   │
│  │  Main UI        │   │  Hidden Webview (bgView)        │   │
│  │  - 대시보드     │   │  - claude.ai 페이지 로딩        │   │
│  │  - 원형 타이머   │   │  - partition="persist:claude"   │   │
│  │  - 진행바       │   │  - 1x1px, off-screen            │   │
│  │  - 델타 차트     │   │                                  │   │
│  │  - Canvas 렌더  │   │  bgView.executeJavaScript()로   │   │
│  │                 │◄──┤  Same-origin fetch 실행          │   │
│  └─────────────────┘   │  → /api/organizations            │   │
│                        │  → /api/organizations/{id}/usage │   │
│                        └─────────────────────────────────┘   │
│  ┌─────────────────────────────────────────────────────┐    │
│  │  Login Webview (loginView) - 로그인 필요 시만 표시  │    │
│  │  - claude.ai/login 페이지                           │    │
│  │  - 로그인 성공 감지 (URL 변경 이벤트)                │    │
│  └─────────────────────────────────────────────────────┘    │
└──────────────────────────────────────────────────────────────┘
```

### 3.2 데이터 흐름

**사용량 조회 흐름**:
```
1. Renderer가 bgView.executeJavaScript로 페치 요청
2. claude.ai 페이지 컨텍스트에서 fetch('/api/organizations') 실행
3. orgs[0].uuid 획득
4. fetch('/api/organizations/{uuid}/usage') 실행
5. 응답 JSON을 executeJavaScript의 리턴값으로 받음
6. Renderer에서 파싱 → UI 업데이트 → latest 객체에 저장
7. IPC로 main에 스냅샷 저장 요청
8. main이 electron-store에 저장
9. Renderer가 차트 다시 그리기
```

**타이머 구조**:
- **Poll Timer** (5분): 서버에서 새 데이터 페치
- **Tick Timer** (1초): 로컬 카운트다운 갱신 (남은 시간, 마커 위치 등)

두 타이머를 분리한 이유: 시간이 흐르는 것은 서버 호출 없이도 보여줘야 자연스러움.

### 3.3 상태 관리

**`latest` 객체**: Renderer의 전역 변수. 가장 최근 서버 응답 요약.
```javascript
let latest = {
  sessionPct: 0,          // 5시간 세션 사용률 %
  sessionResetAt: null,   // ISO 문자열
  weekPct: 0,             // 주간 전체 %
  weekResetAt: null,
  subPct: 0,              // Sonnet/Opus 주간 %
  subResetAt: null
};
```

`tick()` 함수가 매초 이 객체를 읽어 UI를 갱신하므로, 서버 응답이 없는 시간에도 시간이 자연스럽게 흘러갑니다.

---

## 4. 기술 스택

### 4.1 필수 의존성

| 패키지 | 버전 | 용도 |
|---|---|---|
| `electron` | `^33.0.0` | 데스크톱 앱 프레임워크 |
| `electron-builder` | `^25.0.0` | 빌드/패키징 |
| `electron-store` | `^8.2.0` | JSON 파일 기반 영구 저장소 |

**주의**: `electron-store` v9+ 는 ESM이라 이 프로젝트(CommonJS)에서 작동하지 않습니다. 반드시 `^8.2.0` 사용.

### 4.2 외부 라이브러리 없음

차트, UI 등은 모두 순수 HTML/CSS/JavaScript + Canvas로 구현. Chart.js, React, Vue 등 외부 라이브러리 의존성 없음. 이유:
- 번들 크기 감소
- 보안 표면적 감소
- CSP 설정 단순화

### 4.3 Node 버전

Node.js 20 LTS 이상 권장 (Electron 33이 이를 기반으로 함).

---

## 5. 기능 명세

### 5.1 사용량 표시

#### 5.1.1 5시간 세션 (Hero 영역)

**표시 항목**:
- **Left Ring (사용률)**: 녹색 원형 진행바, 가운데에 `41%` 대형 숫자, 아래에 "of session"
- **Right Ring (남은 시간)**: 파란색 원형 진행바, 가운데에 `3h 27m`, 아래에 "69% of 5h left"
- **우상단**: `Resets at Tue 14:35` (리셋 정확 시각, 요일 포함)
- **제목**: "5-Hour Session"

**색상 규칙 (사용률)**:
- 0~70%: 녹색 `#4ade80`
- 70~90%: 노랑 `#facc15`
- 90~100%: 빨강 `#f87171`

**색상 규칙 (남은 시간)**:
- 30% 초과: 파랑 `#60a5fa`
- 10~30%: 노랑
- 10% 이하: 빨강

**계산 로직**:
- 사용률: API 응답의 `five_hour.utilization` 값 (정수 %로 반환됨, 0~100)
- 남은 시간: `five_hour.resets_at` - 현재 시간
- 남은 시간 %: `(남은 ms) / (5 * 60 * 60 * 1000) * 100`

#### 5.1.2 주간 한도

**두 개 카드로 구성**:
1. **Weekly · All Models**: 전체 모델 통합 사용량 (`seven_day.utilization`)
2. **Weekly · Sonnet**: Sonnet 모델 사용량 (`seven_day_sonnet.utilization`)

**각 카드 구성**:
- 상단: `23%` 대형 숫자
- 중단: 진행바 (사용률)
- 진행바 위에 **시간 경과 마커** (하얀 세로선 + 위/아래 점)
    - 마커 위치 = 7일 윈도우 중 경과한 비율
    - 마커 위 라벨: `60%` 같은 숫자
    - 리셋 직후 = 0% (왼쪽), 리셋 직전 = 100% (오른쪽)
- 하단: `Resets in 4d 12h` (남은 시간)

**마커의 의미**: 사용량 막대보다 마커가 오른쪽에 있으면 여유, 왼쪽에 있으면 페이스가 빠름을 의미.

**주의**: Opus 주간 한도(`seven_day_opus`)는 있을 수도/없을 수도 있음. 응답에 `seven_day_sonnet`이 있으면 그것을 우선 표시, 없으면 `seven_day_opus`를 표시.

#### 5.1.3 추가 사용량 (Extra Usage)

Hero 옆에 1/4 폭으로 배치되는 작은 카드.

**응답 필드**:
```json
"extra_usage": {
  "is_enabled": true,
  "monthly_limit": 5000,      // 센트 단위! $50.00
  "used_credits": 3249.0,     // 센트 단위! $32.49
  "utilization": 64.98         // %
}
```

**⚠️ 중요**: `monthly_limit`와 `used_credits`는 **센트(cents) 단위**입니다. 100으로 나눠서 달러로 변환해야 합니다.

**표시 구성**:
- 제목: "Extra Usage"
- 대형 숫자: `$32.49`
- 그 아래: `of $50.00`
- 진행바
- 하단: `65%` + `Monthly`

**비활성화 상태** (`is_enabled: false`):
- 카드 전체 opacity 0.5
- 상태 텍스트: "Disabled"
- 숫자는 `$0.00`

#### 5.1.4 사용량 변화량 차트 (Delta Chart)

푸터 직전 위치, 가로 전체 폭.

**막대 하나 = 5분 갱신 간격 사이의 사용률 증가분**
- 예: 13:30에 31% → 13:35에 34% → 막대 높이 3%

**색상**:
- 0~5% 증가: 녹색
- 5~10%: 노랑
- 10%+: 빨강

**세션 리셋 감지**: 이전 스냅샷 대비 5% 이상 **떨어진** 경우(예: 78% → 3%)는 리셋으로 판단, 그 위치에 파란 점선으로 표시.

**표시 범위**: 최근 30개 스냅샷 (약 2.5시간)
**Y축**: 자동 스케일 (최소 5%, 5~25% 구간은 5% 간격, 그 이상은 10% 간격)
**X축**: 양쪽 끝에 HH:MM 시각 표시
**빈 상태**: "Collecting data... N snapshot(s) so far"

### 5.2 시스템 트레이

**Windows**: 작업 표시줄 시계 옆
**macOS**: 상단 메뉴바 우측

**동작**:
- 클릭: 창 표시/포커스
- 우클릭: 컨텍스트 메뉴
    - Show Window
    - Refresh Now
    - (구분선)
    - Quit

**툴팁**: 마우스 올리면 `Claude Usage\nSession: 41%\nWeek: 23%` 표시 (멀티라인).

**아이콘**: `assets/tray-icon.png` (우선), 없으면 `assets/icon.ico`, `assets/icon.png` 순서로 fallback. 모두 없으면 빈 아이콘.

**중요**: 창 닫기(`close` 이벤트)는 트레이로 숨김 처리, `before-quit` 또는 트레이 메뉴의 Quit만 실제 종료.

### 5.3 알림

**트리거**: 서버 응답 후, 다음 조건 시 1회 발송
- `sessionPct >= 80` 또는
- `weekPct >= 80`

**내용**: `Session: 82% · Week: 67%` (현재 수치)
**구현**: `Notification` API (Electron이 OS에 위임)

### 5.4 로그인 관리

**자동 감지**:
1. 앱 시작 → bgView 로드
2. `dom-ready` 이벤트 후 1.5초 대기 → fetchUsage
3. 응답 401/403 → `showLoginPane()` 호출

**로그인 패널**:
- 전체화면 오버레이
- 상단 안내: "Log in to claude.ai. This window closes automatically once login is detected."
- Cancel 버튼 (로그인 없이 돌아가기)
- 하단: claude.ai/login 페이지 webview

**로그인 성공 감지**: `loginView`의 `did-navigate` 이벤트에서 URL이 `/login`을 포함하지 않으면 로그인 성공으로 간주.

**후속 동작**:
- 0.5초 후 bgView 리로드 (새 쿠키로 갱신)
- 2초 후 fetchUsage 재시도
- 성공 시 hideLoginPane()

**수동 재로그인**: 헤더의 "Login" 버튼을 클릭하면 언제든 로그인 패널을 띄울 수 있음.

**로그인 필요 상태 시각적 강조**:
- Login 버튼에 `.needs-login` 클래스 추가
- 빨간 배경 + 펄스 애니메이션 (box-shadow 확장/축소)

### 5.5 자동 갱신

**간격**: 5분 (`POLL_INTERVAL_MS = 5 * 60 * 1000`)
**방법**: `setInterval`로 `fetchUsage()` 호출
**시작**: `dom-ready` 이후 첫 fetch 완료 후
**중단**: 앱 종료 시

**수동 새로고침**:
- 헤더의 "↻ Refresh" 버튼
- 시스템 트레이 컨텍스트 메뉴의 "Refresh Now"

### 5.6 이력 저장

**저장소**: `electron-store`의 `usageHistory` 키
**저장 시점**: 매번 fetchUsage 성공 시
**스냅샷 형식**:
```javascript
{
  timestamp: 1712345678901,
  fiveHour: { utilization: 41, resets_at: "..." },
  sevenDay: { utilization: 23, resets_at: "..." },
  sevenDayOpus: { utilization: 7, resets_at: "..." }
}
```

**보관 정책**:
- 최대 30일치만 보관
- 저장 시점에 30일 이전 데이터 자동 삭제
- 100을 초과하는 utilization 값은 무효로 간주하고 제외 (과거 버그 데이터 필터링)

**저장 위치**:
- Windows: `%APPDATA%\ClaudeUsageTracker\config.json`
- macOS: `~/Library/Application Support/ClaudeUsageTracker/config.json`

---

## 6. UI/UX 명세

### 6.1 창 크기

- 기본: 860 × 600 (컨텐츠 기준, `useContentSize: true`)
- 최소: 720 × 560
- 타이틀: "Claude Usage Tracker"
- 배경색: `#1a1a1a` (로딩 중 깜빡임 방지)

### 6.2 전체 레이아웃

```
┌─────────────────────────────────────────────────────────┐
│  [Claude Usage]  [STATUS]        [↻ Refresh] [Login]   │ header
├─────────────────────────────────────────────────────────┤
│  ┌────────────────────────────────┐ ┌─────────────┐    │
│  │  5-Hour Session    Resets at.. │ │ EXTRA USAGE │    │
│  │  ○  41%      ○  3h 27m         │ │  $32.49     │    │ hero row
│  │ Used         Time Left         │ │  of $50.00  │    │ (flex: 3:1)
│  │ of session   69% of 5h left    │ │  [====--]   │    │
│  └────────────────────────────────┘ │  65%  Monthly│   │
│                                      └─────────────┘    │
├─────────────────────────────────────────────────────────┤
│  ┌─────────────────┐  ┌─────────────────┐              │
│  │ Weekly · All    │  │ Weekly · Sonnet │              │ grid
│  │  23%            │  │  7%             │              │ (2 col)
│  │ [===|======]    │  │ [=|========]    │              │
│  │ 60% 경과          │  │ 60% 경과          │              │
│  │ Resets in 4d    │  │ Resets in 2d    │              │
│  └─────────────────┘  └─────────────────┘              │
├─────────────────────────────────────────────────────────┤
│  Session Usage Delta · last refreshes                   │
│  10%├────────────────────────────                       │
│   5%├──█──█────█──█────█──────                          │ chart-card
│   0%└─█──█──█──█──█──█──█──█─                           │ (flex: 1)
│      13:25                    15:55                     │
├─────────────────────────────────────────────────────────┤
│  Last update: 15:42   v0.9.0   made by **zitify**       │ footer
└─────────────────────────────────────────────────────────┘
```

### 6.3 색상 팔레트

| 변수 | 값 | 용도 |
|---|---|---|
| 배경 | `#0f0f0f` | body |
| 카드 배경 | `#1e1e1e` | .card |
| Hero 배경 | `linear-gradient(135deg, #1a1a1a, #1e1e1e)` | .hero, .extra-card |
| 테두리 | `#2a2a2a` | 카드 border |
| 트랙 | `#262626` | 진행바/링 배경 |
| 주 텍스트 | `#e8e8e8` | 일반 |
| 하이라이트 | `#f5f5f5` | 큰 숫자 |
| 보조 | `#888` | 작은 텍스트 |
| 성공/정상 | `#4ade80` (녹색) | 사용량 정상, connected status |
| 경고 | `#facc15` (노랑) | 70~90% |
| 위험 | `#f87171` (빨강) | 90%+, error status |
| 시간 링 | `#60a5fa` (파랑) | Time Left 링, reset marker |
| zitify 링크 | `#4ade80` → hover `#6ee7a0` | credit link |

### 6.4 타이포그래피

- 기본 font-family: `-apple-system, "Segoe UI", system-ui, sans-serif`
- 숫자에는 `font-variant-numeric: tabular-nums` 적용 (정렬)
- 제목(h2): 10~11px, uppercase, letter-spacing 0.6px, 색 #888
- 큰 숫자: 22~26px, bold 700
- 본문 보조: 11~12px

### 6.5 헤더

- 좌측: 앱 이름 "Claude Usage" (18~20px) + STATUS 라벨 (11px uppercase)
- 우측: Refresh 버튼, Login 버튼 (둘 다 #262626 배경)

**STATUS 값**:
- `Connecting...` (초기)
- `Loading...` (노랑, class="loading")
- `Refreshing...` (노랑)
- `Connected` (녹색, class="connected")
- `Login required` (빨강, class="error")
- `Failed to load claude.ai` (빨강)
- `Error: {reason}` (빨강)

### 6.6 애니메이션

- 링 stroke-dashoffset: `transition: stroke-dashoffset 0.6s ease`
- 진행바 width: `transition: width 0.4s ease`
- 마커 left: `transition: left 0.6s ease`
- Login 버튼 펄스: `animation: pulse 1.5s ease-in-out infinite`
- 버튼 hover: `background 0.15s`

### 6.7 푸터

**일반 버전**:
```
Last update: 15:42     v0.9.0 · 2026-04-08    made by zitify
```
- 마지막 갱신 시각 (HH:MM:SS 로컬)
- 버전 · 빌드 날짜 (package.json의 mtime 기반)
- 크레딧 링크 (zitify만 녹색 #4ade80, 클릭 시 https://zitify.co.kr로 외부 브라우저 열기)

**개인 버전**:
```
Last update: 15:42     v0.9.0 · 2026-04-08
```
- 크레딧 부분 제거

### 6.8 메뉴바 처리

**Windows/Linux**: `Menu.setApplicationMenu(null)`로 완전 제거 + BrowserWindow에 `autoHideMenuBar: true, menuBarVisible: false`

**macOS**: 최소 메뉴 유지 (macOS는 앱 메뉴바 완전 제거 불가, 완전 제거 시 Cmd+C/V/Q 등 단축키 작동 안 함)
- App 메뉴: About, Hide, Hide Others, Unhide, Quit
- Edit 메뉴: Undo, Redo, Cut, Copy, Paste, Select All
- View 메뉴: Reload, Toggle DevTools, Toggle Fullscreen

---

## 7. 데이터 구조

### 7.1 electron-store 스키마

```javascript
{
  usageHistory: [
    {
      timestamp: number,           // Date.now()
      fiveHour: {
        utilization: number,       // 0~100 (정수)
        resets_at: string          // ISO 8601
      },
      sevenDay: { utilization, resets_at },
      sevenDayOpus: { utilization, resets_at }  // sonnet or opus
    },
    ...
  ],
  settings: {
    pollIntervalMinutes: 5,
    notifyThreshold: 80,
    startMinimized: false,
    autoLaunch: false
  }
}
```

### 7.2 API 응답 정규화

Claude.ai의 응답은 다음 후보 필드명 중 하나를 가질 수 있음 (기존 API 변경 방어):

| 카테고리 | 후보 필드명 |
|---|---|
| 5시간 세션 | `five_hour`, `fiveHour`, `session`, `current_session` |
| 주간 전체 | `seven_day`, `sevenDay`, `weekly`, `seven_day_all`, `seven_day_all_models` |
| 주간 Sonnet | `seven_day_sonnet`, `sevenDaySonnet` |
| 주간 Opus | `seven_day_opus`, `sevenDayOpus` |
| 추가 사용량 | `extra_usage`, `extraUsage` |
| 리셋 시각 | `resets_at`, `reset_at` |

`pickField(data, candidates)` 헬퍼로 자동 선택.

### 7.3 값 정규화

**`toPercent(v)` 함수**:
- `null` 또는 `NaN` → 0
- `0 <= v <= 1` → 비율로 간주, 100 곱함 (과거 형식 호환)
- `v > 1` → 이미 퍼센트로 간주, 그대로

API가 현재는 `48.0` 같은 퍼센트 값을 반환하지만, 이 헬퍼는 어느 쪽이든 처리.

---

## 8. API 상세

### 8.1 사용하는 엔드포인트

**1단계: 조직 UUID 획득**
```
GET https://claude.ai/api/organizations
```
- 인증: 쿠키 (자동, credentials: 'include')
- 응답: `[{ uuid: "...", ... }]`
- 오류: 401/403 = 미로그인

**2단계: 사용량 조회**
```
GET https://claude.ai/api/organizations/{uuid}/usage
Accept: application/json
```
- 인증: 쿠키
- 응답 예시:
```json
{
  "five_hour": {
    "utilization": 48.0,
    "resets_at": "2026-04-08T19:00:01.292299+00:00"
  },
  "seven_day": {
    "utilization": 23.0,
    "resets_at": "2026-04-13T00:00:00.292322+00:00"
  },
  "seven_day_oauth_apps": null,
  "seven_day_opus": null,
  "seven_day_sonnet": {
    "utilization": 7.0,
    "resets_at": "2026-04-08T16:00:00.292333+00:00"
  },
  "seven_day_cowork": null,
  "iguana_necktie": null,
  "extra_usage": {
    "is_enabled": true,
    "monthly_limit": 5000,
    "used_credits": 3249.0,
    "utilization": 64.98
  }
}
```

### 8.2 executeJavaScript 페이로드

bgView에서 실행되는 스크립트 (문자열 그대로 전송):

```javascript
(async () => {
  try {
    const orgsRes = await fetch('/api/organizations', { credentials: 'include' });
    if (orgsRes.status === 401 || orgsRes.status === 403) {
      return { error: 'not_logged_in', status: orgsRes.status };
    }
    if (!orgsRes.ok) return { error: 'orgs_failed', status: orgsRes.status };
    const orgs = await orgsRes.json();
    if (!orgs || !orgs.length) return { error: 'no_org' };
    const orgId = orgs[0].uuid;

    const usageRes = await fetch('/api/organizations/' + orgId + '/usage', {
      credentials: 'include',
      headers: { 'Accept': 'application/json' }
    });
    if (usageRes.status === 401 || usageRes.status === 403) {
      return { error: 'not_logged_in', status: usageRes.status };
    }
    if (!usageRes.ok) {
      const text = await usageRes.text().catch(() => '');
      return { error: 'usage_failed', status: usageRes.status, body: text.slice(0, 200) };
    }
    const usage = await usageRes.json();
    return { ok: true, data: usage, orgId };
  } catch (e) {
    return { error: 'exception', message: String(e) };
  }
})()
```

### 8.3 에러 상태 처리 매핑

| result.error | status | UI 동작 |
|---|---|---|
| `not_logged_in` | 401/403 | Login required 상태, 로그인 패널 자동 표시, 버튼 빨갛게 |
| `no_org` | - | "Error: no_org" 표시 |
| `orgs_failed` | 404/500 등 | "Error: orgs_failed" + 콘솔 로그 |
| `usage_failed` | 기타 | "Error: usage_failed" + 응답 body 200자 콘솔 |
| `exception` | - | JavaScript 예외, 메시지 표시 |

### 8.4 토큰 수 조회 불가

**확인됨**: 현재 API 응답에는 토큰 수치(input_tokens, output_tokens 등)가 없음. utilization 퍼센트만 제공. claude.ai 설정 화면에도 토큰 수는 표시되지 않음. 이 앱으로는 토큰 수 조회 **불가능**.

대안 (구현 복잡도 매우 높음):
- webview에서 실시간 대화를 가로채서 gpt-tokenizer로 직접 계산 → 이 앱 구조상 사용자가 대화하는 창을 우리가 못 보므로 불가능

---

## 9. 파일 구조

```
claude-usage-tracker/
├── package.json                    # 메타데이터, 의존성, 빌드 설정
├── package-lock.json               # (자동 생성)
├── CHANGELOG.md                    # 버전 기록
├── README.md                       # 사용자용 문서
├── .github/
│   └── workflows/
│       └── build.yml               # GitHub Actions (Windows + macOS 자동 빌드)
├── assets/
│   ├── icon.ico                    # Windows 앱 아이콘 (256x256)
│   ├── icon.icns                   # macOS 앱 아이콘
│   ├── icon.png                    # Linux/fallback
│   └── tray-icon.png               # 트레이 전용 (16x16 or 22x22)
└── src/
    ├── main/
    │   └── main.js                 # Main process
    ├── preload/
    │   └── preload.js              # contextBridge API
    └── renderer/
        ├── index.html              # 메인 UI
        ├── styles.css              # 스타일
        └── renderer.js             # UI 로직 + fetch + 차트
```

### 9.1 package.json 핵심 설정

```json
{
  "name": "claude-usage-tracker",
  "version": "0.9.0",
  "main": "src/main/main.js",
  "scripts": {
    "start": "electron .",
    "dev": "electron . --dev",
    "build": "electron-builder --win",
    "build:portable": "electron-builder --win portable",
    "build:mac": "electron-builder --mac",
    "build:mac-arm": "electron-builder --mac --arm64",
    "build:mac-intel": "electron-builder --mac --x64"
  },
  "devDependencies": {
    "electron": "^33.0.0",
    "electron-builder": "^25.0.0"
  },
  "dependencies": {
    "electron-store": "^8.2.0"
  },
  "build": {
    "appId": "com.example.claude-usage-tracker",
    "productName": "ClaudeUsageTracker",
    "win": {
      "icon": "assets/icon.ico",
      "target": [{ "target": "portable", "arch": ["x64"] }]
    },
    "portable": {
      "artifactName": "ClaudeUsageTracker-${version}-portable.exe"
    },
    "mac": {
      "category": "public.app-category.productivity",
      "icon": "assets/icon.icns",
      "target": [
        { "target": "dmg", "arch": ["arm64", "x64"] },
        { "target": "zip", "arch": ["arm64", "x64"] }
      ],
      "hardenedRuntime": false,
      "gatekeeperAssess": false,
      "identity": null
    },
    "dmg": {
      "artifactName": "ClaudeUsageTracker-${version}-${arch}.dmg"
    },
    "files": ["src/**/*", "assets/**/*", "node_modules/**/*"]
  }
}
```

**주의 사항**:
- `productName`은 공백 없이. 공백이 있으면 빌드 시 일부 환경에서 실패.
- `identity: null`: macOS 코드 사이닝 생략 (개인 사용 목적).
- `hardenedRuntime: false`: 인증서 없이 빌드.

### 9.2 main.js 주요 책임

```javascript
// 1. 플랫폼별 메뉴 처리
if (process.platform === 'darwin') {
  // macOS용 최소 메뉴 설정
} else {
  Menu.setApplicationMenu(null);  // Windows/Linux는 제거
}

// 2. BrowserWindow 생성
new BrowserWindow({
  width: 860, height: 600,
  useContentSize: true,
  autoHideMenuBar: true,
  menuBarVisible: false,
  icon: iconPath,
  webPreferences: {
    preload: path.join(__dirname, '../preload/preload.js'),
    contextIsolation: true,
    nodeIntegration: false,
    webviewTag: true,
    partition: 'persist:claude'
  }
});

// 3. 트레이 생성
// 4. close 이벤트 가로채서 hide
// 5. window-all-closed에서 preventDefault (트레이로만 살아있기)

// 6. IPC 핸들러
ipcMain.handle('get-app-info', () => ...);
ipcMain.handle('get-history', () => store.get('usageHistory'));
ipcMain.handle('get-settings', () => store.get('settings'));
ipcMain.handle('save-usage-snapshot', (e, snap) => ...);
ipcMain.handle('save-settings', (e, s) => ...);
ipcMain.handle('show-notification', (e, {title, body}) => ...);
ipcMain.handle('open-external', (e, url) => ...);  // allowlist 체크 필수
```

### 9.3 preload.js

contextBridge로 안전한 API만 노출. Renderer는 nodeIntegration이 꺼져 있어서 직접 require 불가.

```javascript
const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('api', {
  getAppInfo: () => ipcRenderer.invoke('get-app-info'),
  getHistory: () => ipcRenderer.invoke('get-history'),
  getSettings: () => ipcRenderer.invoke('get-settings'),
  saveSettings: (s) => ipcRenderer.invoke('save-settings', s),
  saveUsageSnapshot: (snap) => ipcRenderer.invoke('save-usage-snapshot', snap),
  showNotification: (n) => ipcRenderer.invoke('show-notification', n),
  openExternal: (url) => ipcRenderer.invoke('open-external', url),
  onTriggerRefresh: (cb) => ipcRenderer.on('trigger-refresh', cb)
});
```

### 9.4 index.html 구조

```html
<body>
  <div class="app" id="app">
    <main class="dashboard" id="dashboard">
      <header class="dash-header"> ... </header>

      <!-- Hero row: 5-hour session + extra usage -->
      <div class="top-row">
        <section class="hero"> ...두 개 링... </section>
        <section class="extra-card"> ...추가 사용량... </section>
      </div>

      <!-- Weekly limits -->
      <div class="grid">
        <section class="card"> ...All Models 카드... </section>
        <section class="card"> ...Sonnet 카드... </section>
      </div>

      <!-- Delta chart -->
      <section class="card chart-card">
        <canvas id="deltaChart"></canvas>
      </section>

      <footer class="footer"> ... </footer>
    </main>

    <!-- Login webview (hidden by default) -->
    <section class="login-pane hidden" id="loginPane">
      <webview id="loginView" src="https://claude.ai/login"
               partition="persist:claude" allowpopups></webview>
    </section>

    <!-- Hidden background webview (always present) -->
    <webview id="bgView" src="https://claude.ai/"
             partition="persist:claude"
             style="position:absolute; width:1px; height:1px;
                    left:-9999px; top:-9999px; visibility:hidden;">
    </webview>
  </div>
  <script src="renderer.js"></script>
</body>
```

**CSP**:
```html
<meta http-equiv="Content-Security-Policy"
      content="default-src 'self';
               style-src 'self' 'unsafe-inline';
               script-src 'self';
               img-src 'self' data: https:;">
```

### 9.5 renderer.js 구조

```
// 1. 상수 및 상태
const POLL_INTERVAL_MS = 5 * 60 * 1000;
const TICK_INTERVAL_MS = 1000;
const RING_CIRCUMFERENCE = 540.35;  // 2π × 86 (SVG r=86)
const SESSION_TOTAL_MS = 5 * 60 * 60 * 1000;
const WEEK_TOTAL_MS = 7 * 24 * 60 * 60 * 1000;
let latest = { ... };

// 2. DOM 헬퍼
function setStatus(text, kind)
function updateBar(barId, percent)
function formatRemaining(ms)      // ms → "1h 23m" or "5:42"
function formatResetText(iso)      // "Resets in 4d 12h"
function formatResetAtClock(iso)  // "Resets at Tue 14:35"
function toPercent(v)              // 정규화
function pickField(data, candidates)

// 3. 로그인 패널
function showLoginPane()
function hideLoginPane()

// 4. 링
function setRingProgress(ringId, fillPercent)
function setRingColor(ringId, percent, isUsage)

// 5. 차트
async function drawDeltaChart()    // try-catch로 감싸기 필수

// 6. 마커
function updateMarker(markerId, labelId, resetAtIso)

// 7. 메인 렌더
function tick()                    // 1초마다, latest 읽어서 UI 갱신
function renderUsage(data)         // fetch 결과 처리
function renderExtraUsage(extra)   // extra_usage 전용

// 8. 페치
async function fetchUsage()

// 9. 생명주기
function startTimers()
async function loadAppInfo()

// 10. 이벤트 리스너
bgView.addEventListener('dom-ready', async () => { ... });
bgView.addEventListener('did-fail-load', ...);
loginView.addEventListener('did-navigate', ...);
document.getElementById('refreshBtn').addEventListener('click', ...);
document.getElementById('loginBtn').addEventListener('click', ...);
document.getElementById('cancelLoginBtn').addEventListener('click', ...);
document.getElementById('creditLink').addEventListener('click', ...);
window.api.onTriggerRefresh(async () => { ... });

// 11. 초기화
loadAppInfo();
```

**중요한 함수 시그니처**:

```javascript
// renderUsage는 async가 아님!
// 내부에서 await 절대 금지 (SyntaxError 발생)
function renderUsage(data) {
  // saveUsageSnapshot은 fire-and-forget + .then()
  window.api.saveUsageSnapshot({...})
    .then(() => drawDeltaChart())
    .catch(e => console.warn(...));
}
```

---

## 10. 빌드 및 배포

### 10.1 로컬 빌드 (Windows)

**사전 준비**:
```bash
set CSC_IDENTITY_AUTO_DISCOVERY=false
```
(또는 Windows 개발자 모드 활성화)

**빌드**:
```bash
npm install
npm run build:portable
```

**결과**: `dist/ClaudeUsageTracker-{version}-portable.exe` (단일 80-100MB 파일)

### 10.2 로컬 빌드 (macOS)

```bash
npm install
npm run build:mac       # Universal (arm64 + x64)
# 또는
npm run build:mac-arm   # Apple Silicon만
npm run build:mac-intel # Intel Mac만
```

**결과**: `dist/ClaudeUsageTracker-{version}-{arch}.dmg`

### 10.3 GitHub Actions 자동 빌드

`.github/workflows/build.yml`:
```yaml
name: Build
on:
  push:
    tags: ['v*']
  workflow_dispatch:

jobs:
  build-windows:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
        with: { node-version: '20' }
      - run: npm install
      - run: npm run build:portable
        env: { CSC_IDENTITY_AUTO_DISCOVERY: 'false' }
      - uses: actions/upload-artifact@v4
        with:
          name: windows-portable
          path: dist/*.exe

  build-mac:
    runs-on: macos-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
        with: { node-version: '20' }
      - run: npm install
      - run: npm run build:mac
        env: { CSC_IDENTITY_AUTO_DISCOVERY: 'false' }
      - uses: actions/upload-artifact@v4
        with:
          name: macos-builds
          path: |
            dist/*.dmg
            dist/*.zip
```

### 10.4 배포 방식

**무료 옵션**:
- win-unpacked 폴더를 zip으로 배포 (단일 exe 실패 시 대안)
- GitHub Releases

**첫 실행 가이드**:

Windows: SmartScreen "PC 보호" → 추가 정보 → 실행
macOS: "확인되지 않은 개발자" 경고 → 우클릭 → 열기
또는 `xattr -cr /Applications/ClaudeUsageTracker.app`

### 10.5 자동 업데이트 (현재 미구현)

구현 시 검토 사항:
- electron-updater 패키지 사용
- GitHub Releases를 업데이트 서버로 활용
- macOS 자동 업데이트는 코드 사이닝 필수 (Apple Developer $99/년)
- Windows는 사이닝 없이도 가능하나 SmartScreen 경고 지속

**저비용 대안**: 버전 체크만 하고 새 버전 발견 시 다운로드 링크로 안내 (반자동).

---

## 11. 알려진 이슈와 해결 방법

### 11.1 빌드 이슈

#### 11.1.1 `Cannot create symbolic link` 에러 (Windows)

**증상**:
```
ERROR: Cannot create symbolic link : ...darwin/10.12/lib/libcrypto.dylib
```

**원인**: electron-builder가 winCodeSign 캐시 압축 해제 시 symlink 생성 권한 필요.

**해결**:
1. Windows 설정 → 개발자 모드 켜기 (가장 확실)
2. 관리자 권한 cmd에서 빌드
3. 환경변수 `CSC_IDENTITY_AUTO_DISCOVERY=false` (부분적 효과)

#### 11.1.2 `Access is denied` 에러

**증상**: `remove dist\win-unpacked\ClaudeUsageTracker.exe: Access is denied.`

**원인**: 이전에 빌드한 앱이 실행 중이거나 탐색기가 폴더 잠금.

**해결**:
1. 트레이에서 ClaudeUsageTracker 종료
2. 작업 관리자에서 관련 프로세스 모두 종료
3. `rmdir /s /q dist` 후 재빌드

### 11.2 런타임 이슈

#### 11.2.1 "Connecting..." 무한 대기

**흔한 원인**: renderer.js의 syntax error로 스크립트 전체가 파싱 실패
- 특히 `function renderUsage(data)` (non-async) 안에 `await` 사용 시 `SyntaxError: await is only valid in async functions`

**확인**: DevTools Console에서 빨간 에러 확인

**방지**:
- `renderUsage`는 절대 async로 만들지 말 것
- `saveUsageSnapshot()`은 fire-and-forget으로 호출

#### 11.2.2 사용률이 100배 크게 표시 (3100% 등)

**원인**: API가 이미 퍼센트값(`48.0`)을 반환하는데 × 100 하면 4800%가 됨.

**해결**: `toPercent()` 헬퍼로 방어 (0~1이면 ratio, >1이면 이미 %).

#### 11.2.3 작업표시줄 아이콘 깨짐

**원인**: `assets/icon.ico` 파일이 없음.

**해결**: ICO 파일 준비 후 `assets/icon.ico`에 배치. main.js에 `fs.existsSync` 체크로 없어도 앱 실행은 가능하도록 안전장치.

### 11.3 데이터 이슈

#### 11.3.1 productName 변경 시 데이터 유실

**원인**: electron-store는 `productName`을 폴더 이름으로 사용. 변경 시 기존 `%APPDATA%\{oldName}\` 폴더 데이터 접근 불가.

**영향**: 로그인 쿠키, 사용량 히스토리 모두 초기화됨.

**해결**: productName을 신중하게 결정, 나중에 바꾸지 말기. 부득이한 경우 마이그레이션 코드 필요.

#### 11.3.2 과거 버그 데이터 (utilization > 100)

**원인**: 초기 버전에서 × 100 이중 적용으로 3100% 같은 값 저장된 경우.

**방지**: `save-usage-snapshot` 핸들러에서 `v >= 0 && v <= 100` 필터링.

### 11.4 플랫폼 차이

#### 11.4.1 macOS 메뉴바 완전 제거 불가

macOS는 앱 메뉴바가 OS 레벨. `Menu.setApplicationMenu(null)` 해도 완전히 안 사라짐. 게다가 이러면 Cmd+C/V/Q 등 단축키 동작 안 함.

**해결**: macOS에서는 최소 메뉴(App/Edit/View) 유지.

#### 11.4.2 macOS Gatekeeper

첫 실행 시 "확인되지 않은 개발자" 경고. 코드 사이닝 없이는 회피 불가.

**해결 안내**:
- 우클릭 → 열기 (1회만)
- 또는 `xattr -cr`로 quarantine 제거

---

## 12. 버전 규칙

**x.y.z 형식 (Semantic Versioning 변형)**
- **x** (major): 대규모 업데이트, 사용자의 명시적 지시가 있을 때만
- **y** (minor): 기능 추가, 대부분의 경우가 여기에 해당
- **z** (patch): 텍스트 수정, 사소한 버그 수정

### 12.1 이력 요약 (참고용)

| 버전 | 주요 변경 |
|---|---|
| 0.1.0 | 초기 버전. Electron + 숨김 webview, 트레이, 알림, 히스토리 |
| 0.2.0 | 메뉴바 숨김 + 단일 원형 타이머 |
| 0.3.0 | 원형 타이머 2개로 분리 (사용량 / 남은 시간) |
| 0.4.0 | 주간 진행바 시간 경과 마커 |
| 0.5.0 | 크레딧 링크 + 버전/날짜 표시 |
| 0.6.0 | 큰 "문의하러 가기" 버튼 (0.6.2에서 철회) |
| 0.6.1 | 아이콘 깨짐 수정 |
| 0.6.2 | 컬러 텍스트로 크레딧 롤백, 스크롤 제거 |
| 0.7.0 | 델타 차트 추가 (**syntax error 유발, 0.7.3에서 수정**) |
| 0.7.1 | 디버깅 로그, drawDeltaChart 안전장치 |
| 0.7.2 | Re-login → Login 이름 변경, 강조 애니메이션 |
| 0.7.3 | **renderUsage의 await 제거** (0.7.0 치명적 버그 수정) |
| 0.8.0 | macOS 빌드 지원 (build:mac 스크립트) |
| 0.9.0 | 추가 사용량(Extra Usage) 카드 추가 |

---

## 13. 향후 확장 가능성

### 13.1 단기 (1-2 minor 버전)

- **설정 UI**: poll 간격, 알림 임계값, 차트 범위 설정
- **자동 시작**: `app.setLoginItemSettings` (Windows 시작 시 자동 실행)
- **CSV 내보내기**: 과거 사용량 히스토리 다운로드
- **다국어**: 한국어/영어 전환 (현재 영어 UI)
- **테마**: 라이트 모드

### 13.2 중기

- **업데이트 알림**: GitHub Releases API로 새 버전 체크 → 알림 (반자동 업데이트)
- **다중 계정**: `partition` 여러 개로 계정 전환
- **모바일 웹뷰**: 반응형 지원 (창 크기 따라 레이아웃)

### 13.3 장기 (검토만)

- **Anthropic API 크레딧 조회**: 별개 기능으로, API 키를 입력받아 Anthropic API 콘솔 사용량 조회 (구독과 무관)
- **자동 업데이트 (완전)**: electron-updater + 코드 사이닝 (Windows 연 15-30만원, macOS 연 14만원)
- **Android 위젯**: 완전히 다른 프로젝트 (Kotlin + Room + WorkManager). Electron 코드 재사용 불가.

### 13.4 구현 불가능하거나 비현실적

- **토큰 수 표시**: API 응답에 없음. 대화 가로채기 방식은 이 앱 구조상 불가능.
- **정확한 남은 메시지 수**: Anthropic 공식 수치 없음. 추정만 가능.
- **Chrome Extension과 연동**: 보안 경계 때문에 쿠키 공유 불가.

---

## 14. 개발 체크리스트 (새로 개발할 때)

### 14.1 초기 설정
- [ ] Node.js 20 LTS 설치
- [ ] 프로젝트 폴더 생성, `npm init -y`
- [ ] `npm install --save-dev electron@^33.0.0 electron-builder@^25.0.0`
- [ ] `npm install electron-store@^8.2.0` (v9+ 아님!)
- [ ] 파일 구조 생성 (`src/main`, `src/preload`, `src/renderer`, `assets`)

### 14.2 핵심 구현 순서
1. [ ] main.js: BrowserWindow + 기본 창 표시
2. [ ] preload.js: contextBridge 기본 구조
3. [ ] index.html + styles.css: 대시보드 정적 레이아웃
4. [ ] renderer.js: bgView + executeJavaScript로 fetch 테스트
5. [ ] 로그인 감지 + 로그인 패널
6. [ ] 데이터 파싱 및 UI 바인딩 (pickField, toPercent 헬퍼)
7. [ ] 원형 링 타이머 (SVG stroke-dashoffset)
8. [ ] 주간 카드 + 시간 경과 마커
9. [ ] 추가 사용량 카드 (센트→달러 변환 주의)
10. [ ] 델타 차트 (Canvas)
11. [ ] 트레이
12. [ ] 알림
13. [ ] electron-store 연동
14. [ ] 타이머 (poll + tick)
15. [ ] 빌드 설정 + 테스트

### 14.3 배포 전 검증
- [ ] DevTools Console에 에러 없음
- [ ] "[ClaudeUsage] raw payload:" 로그 정상 출력
- [ ] 로그인 → 자동 감지 → 데이터 표시 플로우
- [ ] 창 닫기 시 트레이로 숨겨짐
- [ ] 5분 후 자동 갱신 확인
- [ ] 1초마다 남은 시간 업데이트
- [ ] 재시작 후 로그인 유지
- [ ] 빌드된 exe/dmg로 클린 환경 테스트

### 14.4 작성자/디버깅용 특이사항

- **SVG 원 반지름은 86** (viewBox 200 기준). 원주 = 2π × 86 ≈ **540.35**. 이 값이 stroke-dasharray와 RING_CIRCUMFERENCE 상수로 고정됨.
- **센트 단위 조심**: `extra_usage.monthly_limit: 5000` = $50.00, 항상 /100.
- **webview 크기 0이면 dom-ready 이벤트가 안 올 수 있음**. 1×1 픽셀은 유지.
- **renderUsage는 절대 async 아님**. await 쓰려면 `.then()` 사용.
- **productName 변경 = 데이터 초기화**. 신중할 것.

---

## 15. 별도 버전 (Personal)

개인용/내부용으로 배포 시 zitify 크레딧을 제거한 별도 빌드.

### 15.1 차이점

| 항목 | 일반 버전 | Personal 버전 |
|---|---|---|
| footer "made by zitify" | 있음 | 없음 |
| package.json name | claude-usage-tracker | claude-usage-tracker-personal |
| productName | ClaudeUsageTracker | ClaudeUsageTrackerPersonal |
| appId | com.example.claude-usage-tracker | com.example.claude-usage-tracker-personal |
| 윈도우 타이틀 | Claude Usage Tracker | Claude Usage Tracker (Personal) |
| 데이터 저장 경로 | %APPDATA%\ClaudeUsageTracker | %APPDATA%\ClaudeUsageTrackerPersonal |
| 빌드 파일명 | ClaudeUsageTracker-x.y.z-portable.exe | ClaudeUsageTrackerPersonal-x.y.z-portable.exe |
| 기능/버전 | 동일 | 동일 |

### 15.2 관리 방식

두 프로젝트를 완전히 별도 폴더로 유지. 변경 시 양쪽 모두 동기화 필요.

**동기화 대상**:
- `src/renderer/index.html` (단, footer credit 제거 유지)
- `src/renderer/styles.css` (단, `.credit` 스타일 없음)
- `src/renderer/renderer.js` (단, creditLink 이벤트 리스너 없음)
- `src/main/main.js` (단, 제품명 관련 부분 제외)
- `src/preload/preload.js` (동일)

---

## 16. 라이선스 및 면책

### 16.1 프로젝트 라이선스
MIT 권장.

### 16.2 Anthropic과의 관계

이 앱은 **Anthropic의 공식 제품이 아닙니다.** Anthropic과 무관한 서드파티 도구.

사용하는 API(`/api/organizations/{id}/usage`)는:
- 공식 문서 없음
- 사전 통보 없이 변경될 수 있음
- Anthropic이 차단해도 이의 제기 불가

사용자에게 README 및 설정 화면에서 이 점 명시 권장.

### 16.3 사용자 데이터

- **네트워크 전송 없음**: 모든 데이터는 로컬에만 저장
- **로그인 쿠키**: Chromium이 관리, `%APPDATA%\...\Cookies` SQLite
- **사용량 히스토리**: electron-store JSON
- **외부 서버 없음**: zitify.co.kr은 단순 브라우저 열기용 링크, 데이터 전송 없음

---

## 부록 A. 주요 코드 스니펫 모음

### A.1 fetchUsage 구현 (renderer.js)

```javascript
async function fetchUsage() {
  if (!bgReady) {
    console.log('[ClaudeUsage] fetchUsage skipped: bgView not ready');
    return false;
  }
  try {
    try {
      console.log('[ClaudeUsage] bgView URL:', bgView.getURL());
    } catch (e) {}

    const result = await bgView.executeJavaScript(`
      (async () => {
        try {
          const orgsRes = await fetch('/api/organizations', { credentials: 'include' });
          if (orgsRes.status === 401 || orgsRes.status === 403) {
            return { error: 'not_logged_in', status: orgsRes.status };
          }
          if (!orgsRes.ok) return { error: 'orgs_failed', status: orgsRes.status };
          const orgs = await orgsRes.json();
          if (!orgs || !orgs.length) return { error: 'no_org' };
          const orgId = orgs[0].uuid;
          const usageRes = await fetch('/api/organizations/' + orgId + '/usage', {
            credentials: 'include',
            headers: { 'Accept': 'application/json' }
          });
          if (usageRes.status === 401 || usageRes.status === 403) {
            return { error: 'not_logged_in', status: usageRes.status };
          }
          if (!usageRes.ok) {
            const text = await usageRes.text().catch(() => '');
            return { error: 'usage_failed', status: usageRes.status, body: text.slice(0, 200) };
          }
          const usage = await usageRes.json();
          return { ok: true, data: usage, orgId };
        } catch (e) {
          return { error: 'exception', message: String(e) };
        }
      })()
    `);

    console.log('[ClaudeUsage] fetch result:', result);

    if (result && result.ok) {
      setStatus('Connected', 'connected');
      hideLoginPane();
      document.getElementById('loginBtn').classList.remove('needs-login');
      renderUsage(result.data);
      return true;
    } else {
      const reason = result ? result.error : 'unknown';
      if (reason === 'not_logged_in') {
        setStatus('Login required', 'error');
        document.getElementById('loginBtn').classList.add('needs-login');
        showLoginPane();
      } else {
        setStatus('Error: ' + reason, 'error');
      }
      return false;
    }
  } catch (e) {
    console.error('[ClaudeUsage] fetchUsage exception:', e);
    setStatus('Error: ' + e.message, 'error');
    return false;
  }
}
```

### A.2 링 진행바 설정

```javascript
const RING_CIRCUMFERENCE = 540.35;  // 2π × 86

function setRingProgress(ringId, fillPercent) {
  const ring = document.getElementById(ringId);
  const filled = Math.min(100, Math.max(0, fillPercent));
  const offset = RING_CIRCUMFERENCE * (1 - filled / 100);
  ring.style.strokeDashoffset = offset;
}
```

SVG:
```html
<svg viewBox="0 0 200 200">
  <circle cx="100" cy="100" r="86" class="ring-track"/>
  <circle cx="100" cy="100" r="86" class="ring-progress" id="usageRing"/>
</svg>
```

CSS:
```css
.ring { transform: rotate(-90deg); }  /* 12시 방향 시작 */
.ring-progress {
  fill: none; stroke: #4ade80; stroke-width: 14;
  stroke-linecap: round;
  stroke-dasharray: 540.35;
  stroke-dashoffset: 540.35;
  transition: stroke-dashoffset 0.6s ease, stroke 0.3s ease;
}
```

### A.3 시간 경과 마커

```javascript
const WEEK_TOTAL_MS = 7 * 24 * 60 * 60 * 1000;

function updateMarker(markerId, labelId, resetAtIso) {
  const marker = document.getElementById(markerId);
  const label = document.getElementById(labelId);
  if (!resetAtIso) { marker.style.display = 'none'; return; }
  marker.style.display = 'block';
  const remainingMs = Math.max(0, new Date(resetAtIso) - new Date());
  const elapsedMs = Math.max(0, WEEK_TOTAL_MS - remainingMs);
  const elapsedPct = Math.min(100, (elapsedMs / WEEK_TOTAL_MS) * 100);
  marker.style.left = elapsedPct + '%';
  label.textContent = Math.round(elapsedPct) + '%';
}
```

HTML:
```html
<div class="bar bar-with-marker">
  <div class="bar-fill" id="weekBar"></div>
  <div class="bar-marker" id="weekMarker">
    <span class="marker-label" id="weekMarkerLabel">--%</span>
  </div>
</div>
```

### A.4 센트 → 달러 변환 (추가 사용량)

```javascript
function renderExtraUsage(extra) {
  const card = document.getElementById('extraCard');
  if (!extra || extra.is_enabled === false) {
    card.classList.add('disabled');
    document.getElementById('extraUsed').textContent = '$0.00';
    return;
  }
  card.classList.remove('disabled');
  const usedDollars = (extra.used_credits || 0) / 100;
  const limitDollars = (extra.monthly_limit || 0) / 100;
  const pct = Math.round(extra.utilization);
  document.getElementById('extraUsed').textContent = '$' + usedDollars.toFixed(2);
  document.getElementById('extraLimit').textContent = '$' + limitDollars.toFixed(2);
  // ...
}
```

---

**문서 끝.**

이 문서는 2026년 4월 기준으로 작성되었으며, Claude.ai의 API 구조 변경 시 업데이트가 필요합니다.