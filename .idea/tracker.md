# Claude Usage Tracker 기획/개발 명세서

> 이 문서는 Claude.ai 사용량을 실시간으로 추적하는 Windows 데스크톱 애플리케이션의 최종 명세서입니다.
> **최종 업데이트: 2026-04-14 · v1.2.0**

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
9. [보안](#9-보안)
10. [파일 구조](#10-파일-구조)
11. [빌드 및 배포](#11-빌드-및-배포)
12. [알려진 이슈와 해결 방법](#12-알려진-이슈와-해결-방법)
13. [버전 규칙](#13-버전-규칙)
14. [향후 확장 가능성](#14-향후-확장-가능성)

---

## 1. 프로젝트 개요

### 1.1 목적

Claude.ai 구독자(Pro, Max 등)가 자신의 사용량을 실시간으로 모니터링할 수 있는 Windows 데스크톱 애플리케이션.

### 1.2 대상 사용자

- Claude.ai 유료 구독자
- 5시간 세션 제한, 주간 한도 등을 자주 확인해야 하는 사용자
- 브라우저를 열어 설정 페이지로 이동하는 과정을 번거롭게 느끼는 사용자
- 데스크톱에서 백그라운드로 사용량을 확인하고 싶은 사용자

### 1.3 핵심 가치

1. **즉시성**: 트레이 아이콘 + 앱 창에서 한눈에 사용량 확인
2. **시각적 명확���**: 원형 게이지, 진행바, 꺾은선 그래프로 직관적 표시
3. **이력 관리**: 과거 사용 패턴 ���적을 통한 사용 습관 파악
4. **저침습성**: 시스템 트레이 상주, 필요할 때만 창 열기

### 1.4 기능 요약

- 5시간 세션 사용률 및 남은 시간 표시 (원형 게이지)
- 주간 한도 (전체 모델 / Sonnet 별도) 표시 (진행바 + 시간 마커)
- 추가 사용량(Extra Usage) 크레딧 표시
- 사용량 변화량 꺾은선 그래프 (delta line chart)
- 시스템 트레이 상주 + 풍선 알림
- WebView2 기반 로그인 + 로그아웃/계정 전환
- 항상 위(Always on Top) 토글
- Claude.ai 바로가기 버튼
- GitHub Releases 기반 자동 업데이트 확인
- Inno Setup 설치 프로그램 + 작업 표시줄 고정 옵션
- MSIX 패키지 빌드 지원

---

## 2. 기술적 제약과 선택의 배경

### 2.1 핵심 제약: Claude.ai 사용량 API는 비공식이다

Claude.ai는 사용량 조회를 위한 공식 API를 제공하지 않습니다. 내부적으로 사용되는 엔드포인트는 존재하지만:

- 공식 ���서가 없음
- 응답 형식이 사전 통보 없이 변경될 수 있음
- 인증은 claude.ai 세션 쿠키에 의존
- 외부 도메인에서의 호출은 CORS/Cloudflare로 차단됨

이 제약이 **모든 기술적 선택의 근간**이 됩니다.

### 2.2 왜 WPF + WebView2인가

**옵션 A: 순수 네이티브 앱 (HttpClient)**
- Cloudflare 보호로 인해 일반 HTTP 클라이언트 요청이 차단됨
- ���션 쿠키 수동 관리 필요
- 실질적으로 불가능

**옵션 B: Electron + Webview**
- 크로스플랫폼 가능하지만 ~90MB 번들 크기
- Node.js 런타임 필요

**옵션 C: WPF + WebView2 (선택됨)**
- .NET 8 네이���브 Windows 앱 — 가볍고 빠름
- WebView2(Chromium 기반)로 claude.ai에 Same-origin fetch 실행
- ��키 자동 영구 저장 (UserDataFolder 공유)
- `window.chrome.webview.postMessage`로 비동기 결과를 안정적으로 수신
- 시스템 트레이는 WinForms NotifyIcon 활용

**결론**: WPF는 Windows 전용이지만, 네이티브 성능과 WebView2의 쿠키/fetch 기능을 조합하면 ���장 안정적.

### 2.3 WebView2의 postMessage 방식

**핵심 발견**: `ExecuteScriptAsync`는 async IIFE/Promise의 반환값을 제대로 처리하지 못하는 경우가 있음.

**해결**: `window.chrome.webview.postMessage()`로 결과를 전송하고, C#에서 `WebMessageReceived` 이벤트로 수신하는 방식을 채택. 이 방식은 100% 안정적으로 동작.

```csharp
// C# 측: 메시지 수신 대기
var tcs = new TaskCompletionSource<string>();
webView.CoreWebView2.WebMessageReceived += (s, args) => tcs.TrySetResult(args.WebMessageAsJson);

// JS 측: 결과 전송
window.chrome.webview.postMessage({ ok: true, data: JSON.stringify(usage) });
```

### 2.4 WebView2 구성

- **숨김 WebView2 (BgWebView)**: MainWindow에 1x1px Collapsed 상태. 5분 주기 자동 갱신용.
- **��그인 WebView2 (LoginWindow)**: 별도 창(500x700). 로그인 + 즉시 fetch 수행.
- **쿠키 공유**: 두 WebView2는 동일한 `UserDataFolder`(`%APPDATA%\ClaudeUsageTracker\WebView2Data`)를 사용하므로 쿠키가 자동 공유.

---

## 3. 아키텍처

### 3.1 프로세스 구조

```
┌──────────────────────────────────────────────────────────────────┐
│  App.xaml.cs (Application)                                       │
│  - 앱 생명주기 관리                                                │
│  - 서비스 ���성 (StorageService, ClaudeApiService, UsageService)   │
│  - MainWindow ��성                                                │
│  - 시스템 트레이 (WinForms NotifyIcon)                             │
│  - 풍선 알림                                                       │
└���─────────────┬───────────────────────────────────────────────────┘
               │
               ▼
┌──────────────────────────────────────────────────────────────────┐
│  MainWindow.xaml.cs                                               │
│  ┌──���──────────────┐   ┌─────��────────────────────────────┐      │
│  │  WPF Dashboard   │   │  Hidden WebView2 (BgWebView)     │      │
│  │  - 원형 게이지    │   │  - claude.ai 로드                │      │
│  │  - 진행바        │   │  - UserDataFolder 공유            │      │
│  │  - 꺾은선 차트   │◄──┤  - postMessage로 fetch 결과 수신  │      │
│  │  - 상태 표시     │   │  → /api/organizations              │      │
│  └─────────────────┘   │  → /api/organizations/{id}/usage   │      │
│                        └──────────────────────────────────┘      │
│  ┌────────────────────────────────────────────────────────┐      │
│  │  LoginWindow (별도 Window)                              │      │
│  │  - WebView2로 claude.ai/login 표시                      │      │
│  │  - "로그인 완료" 버튼 → 직접 fetch → 결과 반환           │      │
│  └────────────────────────────────────────────────────────┘      │
└──────────────────────────────────────────────────────────────────┘
```

### 3.2 데이터 흐름

**사용량 조회 (자동 갱신)**:
```
1. DispatcherTimer (5분 간격) → UsageService.FetchUsageAsync()
2. ClaudeApiService: BgWebView에서 JS fetch 스크립트 실행
3. JS가 /api/organizations → /api/organizations/{uuid}/usage 순차 호출
4. 결과를 window.chrome.webview.postMessage()로 C#에 전달
5. C#: WebMessageReceived 이벤트에서 JSON 파싱
6. UsageService: UsageApiResponse → LatestUsage 변환
7. StorageService: 스냅샷 저장 (config.json)
8. MainWindow: UsageUpdated 이벤트 → UI 갱신
```

**로그인 흐름**:
```
1. MainWindow: fetch 실패 (not_logged_in) → LoginWindow 열기
2. 사용자: WebView2에서 claude.ai 로그인
3. 사용자: "✓ 로그인 완료" 버튼 클릭
4. LoginWindow: 현재 WebView2에서 직접 fetch 실행
5. 성공 → FetchResultJson 설정 → 창 닫기
6. MainWindow: LoginWindow.FetchResultJson을 직접 파싱 → UI 표시
7. BgWebView: 백그라운드���서 리로드 (이후 자동 갱신용)
```

**타이머 구조**:
- **Poll Timer** (5분): 서버에서 새 데이터 fetch
- **Tick Timer** (1초): 로컬 카운트다운 갱신 (남은 시간, 마커 위치)

### 3.3 서비스 레이어

| 서비스 | 역할 |
|---|---|
| `ClaudeApiService` | WebView2를 통한 API 통신. postMessage 기반 fetch. |
| `UsageService` | 비즈니스 로직. API 결과 파싱, 상태 관리, 이벤트 발행. |
| `StorageService` | JSON 파일 영구 저장. 히스토리, 설정 관리. |
| `UpdateService` | GitHub Releases API로 업데이트 확인 + 다운로드/실행. |

---

## 4. 기술 스택

### 4.1 필수 의존성

| 패키지 | 버전 | 용도 |
|---|---|---|
| .NET | 8.0 | 런타임 |
| WPF | (내장) | UI 프레임워크 |
| WinForms | (내장) | 시스템 트레이 NotifyIcon |
| Microsoft.Web.WebView2 | 1.0.3912.50 | Chromium 기반 웹뷰 |
| System.Text.Json | 10.0.5 | JSON 직렬화 |
| Inno Setup | 6.7.1 | 설치 프로그램 생성 (빌드 도구) |

### 4.2 외부 UI 라이브러리 없음

차트, 링, 진��바 등은 모두 ���수 WPF (Path, ArcSegment, Canvas, Polyline)로 구현. 외부 차트 라이브러리 의존성 없음.

---

## 5. 기능 명세

### 5.1 ���용량 표시

#### 5.1.1 5시간 세션 (Hero 영역)

**표시 항목**:
- **Left Ring (사용률)**: WPF Path/ArcSegment 원형 진행바, 가운데에 `41%` 대형 숫자
- **Right Ring (남은 시간)**: 파란색 원형 진행바, 가운데에 `3h 27m`, 아래에 "69% of 5h left"
- **우상단**: `Resets at Tue 14:35`
- **제목**: "5-HOUR SESSION"

**색상 규칙 (사용률)**:
- 0~70%: 녹색 `#4ade80`
- 70~90%: 노랑 `#facc15`
- 90~100%: 빨강 `#f87171`

**색상 규칙 (남은 시간)**:
- 30% 초과: 파랑 `#60a5fa`
- 10~30%: 노랑
- 10% 이하: 빨강

**원형 게이지 구현**:
- WPF `PathFigure` + `ArcSegment` 사용 (SVG stroke-dashoffset 방식 대신)
- ViewBox 200x200, 반지름 86, 스트로크 14
- `SetRing()` 메서드로 각도 계산 → ArcSegment.Point 설정
- `IsLargeArc` = 각도 > 180도

#### 5.1.2 주간 한도

**두 개 카드**:
1. **WEEKLY · ALL MODELS**: `seven_day.utilization`
2. **WEEKLY · SONNET**: `seven_day_sonnet.utilization` (없으면 opus)

**각 카드 구성**:
- 대형 숫자 (%)
- 진행바 (Border Width 비율)
- 시간 경과 마커 (Canvas 위 Grid, 하얀 세로선 + 위/아래 점)
- 하단: "Resets in 4d 12h"

**마커 의미**: 7일 중 경과한 비율. 사용량 바보다 오른쪽이면 여유, 왼쪽이면 페이스 빠름.

#### 5.1.3 추가 사용량 (Extra Usage)

Hero 옆 1/4 폭 카드.

**표시**: `$32.49` / `of $50.00` / 진행바 / `65% Monthly`
**비활성화**: opacity 0.5, "Disabled" 텍스트
**⚠️ 주의**: `monthly_limit`, `used_credits`는 **센트 단위**. 100으로 나눠서 달러 변환.

#### 5.1.4 사용량 변화량 꺾은선 그래프

**변경 (v1.2.0)**: 막대 그래프 → 꺾은선 그래프

**X축**: 시��� 순서 (최대 60개 스냅샷)
**Y축**: 각 갱신 시점별 사용률 증가분(delta %)
**표현**:
- WPF `Polyline`으로 라인 연결
- `Polygon` + `LinearGradientBrush`로 라인 아래 그라디언트 채우기
- 각 데이터 포인트에 `Ellipse` 점 표시
- 점 색상: 0~8% 녹색, 8~15% 노랑, 15%+ 빨강
- 마지막 값 라벨: `+3.2%`

**Y축 자동 스케일**: 최소 5%, 5~25% 구간 5% 간격, 그 이상 10% 간격

### 5.2 시스템 트레이

**위치**: 작업 표시줄 시계 옆 (WinForms NotifyIcon)

**동작**:
- 더블클릭: 창 표시/포커스
- 우클릭: 컨텍스트 메뉴
    - Show Window
    - Refresh Now
    - (구분선)
    - Quit

**툴팁**: 5초마다 갱신, `Claude Usage\nSession: 41%\nWeek: 23%`

**아이콘**: `Assets/icon.ico` (없으면 SystemIcons.Application 대체)

**창 닫기**: 트레이로 숨김 처리. Quit만 실제 종료.

### 5.3 알림

**트리거**: fetch 성공 후, `SessionPct >= 80` 또는 `WeekPct >= 80` 시 1회 발송
**내용**: `Session: 82% · Week: 67%`
**구현**: `NotifyIcon.ShowBalloonTip()`
**리셋**: 둘 �� 80% 미만이 되면 다시 알림 가능

### 5.4 로그인 관리

**자동 감지**:
1. 앱 시작 → BgWebView로 claude.ai 로드 → fetch 시도
2. 401/403 → LoginWindow 열기

**LoginWindow**:
- 500x700 별도 창, WebView2로 claude.ai/login 표시
- 상단 안내: "로그인 후 아래 '로그인 완료' 버튼을 클릭하세요."
- "✓ 로그인 완료" 버튼: **LoginWindow의 WebView2에서 직접 fetch 실행**
- 성공 시: `FetchResultJson` 속성에 결과 저장 → 창 닫기
- 실패 시: 에러 메시지 표시, 버튼 다시 활성화

**로그아웃/계정 전환 (v1.2.0)**:
- 로그인 상태에서 버튼이 "Logout"으로 변경
- 클릭 시: 쿠키 삭제 (`CookieManager.DeleteAllCookies()`) → 상태 초기화 → LoginWindow 열기
- 다른 계정으로 로그인 가능

### 5.5 자동 갱신

**간격**: 5분 (`DispatcherTimer`)
**시작**: 첫 fetch 성공 후
**수동**: "↻ Refresh" 버튼 또는 트레이 "Refresh Now"

### 5.6 항상 위 (v1.2.0)

**📌 버튼**: 헤더에 위치
- 클릭: `Window.Topmost` 토글
- 활성화 시: 버튼 녹색 배경 "📌 On"
- 비활성화 시: 기본 "📌"

### 5.7 Claude 바로가기 (v1.2.0)

**💬 Claude 버튼**: 클릭 시 기본 브라우저에서 `https://claude.ai` 열기

### 5.8 자동 업데이트 (v1.2.0)

**구성**:
- `UpdateService`: GitHub Releases API로 최신 버전 확인
- GitHub repo: `zitify-blip/zitify_claude_usage_tracker`
- 현재 버전��� `Assembly.GetExecutingAssembly().GetName().Version`에서 가져옴

**흐름**:
1. 앱 시작 시 백그라운드로 업데이트 확인
2. 새 버전 발견 → 헤더에 "⬆ 업데이트 가능" 초록 버튼 표시 + 트레이 알림
3. 버튼 클릭 → 설치 파일 다운로드 (진행률 표시) → 자동 실행 → 앱 종료

**수동 확인**: "🔄 Update" 버튼 클릭 → 확인 결�� 표시

**Asset 탐색 우선순위**: `.exe` > `.msi` > `.zip`

### 5.9 이력 저장

**저장소**: `%APPDATA%\ClaudeUsageTracker\config.json`
**저장 시점**: 매 fetch 성공 시
**보관 정책**: 최대 30일, 초과분 자동 삭제

---

## 6. UI/UX 명세

### 6.1 창 크기

- 기본: 860 × 600
- 최소: 720 × 560
- 타이틀: "Claude Usage Tracker"
- 리사이즈: 가능 (`CanResize`)

### 6.2 전체 레이아��� (스크롤 없음)

```
┌──────────────────────────────────────────────────────────────────┐
│ [Claude Usage] [STATUS]  [💬] [🔄] [📌] [↻ Refresh] [Logout]  │ header
├──────────────────────────────────────────────────────────────────┤
│  ┌──────────────────────────────────┐ ┌──────────────┐          │
│  │  5-HOUR SESSION   Resets at..   │ │ EXTRA USAGE  │          │
│  │  ○  41%      ○  3h 27m          │ │  $32.49      │  hero    │
│  │ Used         Time Left          │ │  of $50.00   │  (3:1)   │
│  │ of session   69% of 5h left     │ │  [====--]    │          │
│  └──────────────────────────────────┘ │  65% Monthly │          │
│                                       └──────────��───┘          │
├──────────────────────────────────────────────────────────────────┤
│  ┌──────────────────┐  ┌──────────────────┐                     │
│  │ WEEKLY · ALL     │  │ WEEKLY · SONNET  │  weekly cards       │
│  │  23%             │  │  7%              │  (2 col)            │
│  │ [===|=======]    │  │ [=|=========]    │                     │
│  │ Resets in 4d     │  │ Resets in 2d     │                     │
│  └──────────────────┘  └──────────────────┘                     │
��──────────────────────────────────────────────────────────────────┤
│  SESSION USAGE DELTA · REALTIME                                  │
│   5%├───·───·───·                                                │
│   3%├──·─────·──────·──·                   line chart            │
│   0%└────────��─────────────                                      │
│      13:25                    15:55         +3.2%                │
├──────────────────────────────────────────────────────────────────┤
│  Last update: 15:42       v1.2.0       made by zitify           │ footer
└──────────────────────────────────────────────────────────────────┘
```

**Grid RowDefinitions**: `Auto / 3* / 8 / 2* / 8 / 2* / Auto`

### 6.3 색상 팔레트

| 용도 | 값 |
|---|---|
| body 배경 | `#0f0f0f` |
| 카드 배경 | `#1e1e1e` |
| 테두리 | `#2a2a2a` |
| 트랙 (링/바 배경) | `#262626` |
| 주 텍스트 | `#e8e8e8` |
| 하이라이트 | `#f5f5f5` |
| 보조 텍스트 | `#888888` |
| 성공/정상 | `#4ade80` (녹색) |
| 경고 | `#facc15` (노랑) |
| 위험 | `#f87171` (빨강) |
| 시간 링/리셋 마커 | `#60a5fa` (파랑) |
| zitify 링크 | `#4ade80` |

### 6.4 헤더 버튼 (좌→우)

| 버튼 | 기능 |
|---|---|
| 💬 Claude | 브라우저에서 claude.ai 열기 |
| 🔄 Update | 업데이트 ���동 확인 |
| ⬆ 업데이트 가능 | (새 버전 있을 때만 표시) 다운로드/설치 |
| 📌 | 항상 위 토글 |
| ↻ Refresh | 수동 새로고침 |
| Login / Logout | 로그인 또는 로그아웃 |

### 6.5 푸터

```
Last update: 15:42:30     v1.2.0     made by zitify
```
- 버전: `UpdateService.CurrentVersion`에서 동적 표시
- zitify: 녹색 `#4ade80`, 클릭 시 https://zitify.co.kr 외부 브라우저 열기

---

## 7. 데이터 구조

### 7.1 StorageService (config.json)

```json
{
  "history": [
    {
      "timestamp": 1712345678901,
      "fiveHourUtilization": 41.0,
      "fiveHourResetsAt": "2026-04-08T19:00:01+00:00",
      "sevenDayUtilization": 23.0,
      "sevenDayResetsAt": "2026-04-13T00:00:00+00:00",
      "subModelUtilization": 7.0,
      "subModelResetsAt": "2026-04-08T16:00:00+00:00"
    }
  ],
  "settings": {}
}
```

**보관 정책**: 최대 30일, 저장 시 오래된 데이터 자동 삭제.

### 7.2 LatestUsage (런타임 상태)

```csharp
public class LatestUsage
{
    public double SessionPct { get; set; }
    public string? SessionResetAt { get; set; }
    public double WeekPct { get; set; }
    public string? WeekResetAt { get; set; }
    public double SubPct { get; set; }
    public string? SubResetAt { get; set; }
    public string SubModelName { get; set; } = "sonnet";
    public ExtraUsage? Extra { get; set; }
}
```

**⚠️ 주의 (v1.2.0)**: `extra_usage`의 `monthly_limit`, `used_credits`, `utilization`은 API에서 `null`로 올 수 있음. 모델에서 `double?`(nullable)로 선언하고, UI에서 `?? 0`으로 기본값 처리.

### 7.3 API 응답 정규화

후보 필드명 (API 변경 방어):

| 카테고리 | 후보 |
|---|---|
| 5시간 세션 | `five_hour`, `fiveHour`, `session`, `current_session` |
| 주간 전체 | `seven_day`, `sevenDay`, `weekly`, `seven_day_all` |
| 주간 Sonnet | `seven_day_sonnet`, `sevenDaySonnet` |
| 주간 Opus | `seven_day_opus`, `sevenDayOpus` |
| 추가 사용량 | `extra_usage`, `extraUsage` |
| 리셋 시각 | `resets_at`, `reset_at` |

### 7.4 값 정규화

**`ToPercent(v)`**: `0 <= v <= 1` → ×100 (비율), `v > 1` → 그대로 (이미 %)

---

## 8. API 상세

### 8.1 사용하는 엔드포인트

**1단계: 조직 UUID 획득**
```
GET https://claude.ai/api/organizations
credentials: include
```

**2단계: 사용량 조회**
```
GET https://claude.ai/api/organizations/{uuid}/usage
Accept: application/json
credentials: include
```

### 8.2 응답 예시

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
  "seven_day_sonnet": {
    "utilization": 7.0,
    "resets_at": "2026-04-08T16:00:00.292333+00:00"
  },
  "extra_usage": {
    "is_enabled": true,
    "monthly_limit": 5000,
    "used_credits": 3249.0,
    "utilization": 64.98
  }
}
```

### 8.3 fetch 스크립트 (postMessage 방식)

```javascript
(function() {
  fetch('https://claude.ai/api/organizations', { credentials: 'include' })
    .then(function(orgsRes) {
      if (orgsRes.status === 401 || orgsRes.status === 403) {
        window.chrome.webview.postMessage({ ok: false, error: 'not_logged_in', status: orgsRes.status });
        return;
      }
      if (!orgsRes.ok) {
        window.chrome.webview.postMessage({ ok: false, error: 'orgs_failed', status: orgsRes.status });
        return;
      }
      orgsRes.json().then(function(orgs) {
        if (!orgs || !orgs.length) {
          window.chrome.webview.postMessage({ ok: false, error: 'no_org' });
          return;
        }
        var orgId = orgs[0].uuid;
        fetch('https://claude.ai/api/organizations/' + orgId + '/usage', {
          credentials: 'include',
          headers: { 'Accept': 'application/json' }
        }).then(function(usageRes) {
          if (!usageRes.ok) {
            window.chrome.webview.postMessage({ ok: false, error: 'usage_failed', status: usageRes.status });
            return;
          }
          usageRes.json().then(function(usage) {
            window.chrome.webview.postMessage({ ok: true, data: JSON.stringify(usage) });
          });
        });
      });
    })
    .catch(function(e) {
      window.chrome.webview.postMessage({ ok: false, error: 'exception', message: String(e) });
    });
})()
```

**⚠️ 중요**: `async/await` 대신 `.then()` 체인 사용. `ExecuteScriptAsync`가 async IIFE 반환값을 제대로 처리하지 못하는 문제 회피. 절대 URL(`https://claude.ai/api/...`) 사용으로 네비게이션 없이 어디서든 fetch 가능.

### 8.4 에러 처리 매핑

| error | 의미 | UI 동작 |
|---|---|---|
| `not_logged_in` | 401/403 | "Login required", LoginWindow 열기 |
| `no_org` | 조직 없음 | "Error: no_org" |
| `orgs_failed` | 조직 API 실패 | "Error: orgs_failed" |
| `usage_failed` | 사용량 API 실패 | "Error: usage_failed" |
| `exception` | JS 예외 | "Error: exception" |
| `timeout` | 30초 초과 | "Error: timeout" |
| `webview_not_ready` | WebView2 미초기화 | "WebView not ready" |

---

## 9. 보안

### 9.1 WebView2 네비게이션 제한 (v1.2.0)

- **BgWebView (숨김)**: `https://claude.ai/` 도메인만 허용, 그 외 네비게이션 차단
- **LoginWindow**: `https://claude.ai/` + `https://accounts.google.com/` (Google 로그인) 허용

```csharp
_webView.CoreWebView2.NavigationStarting += (_, args) =>
{
    if (!args.Uri.StartsWith("https://claude.ai/", StringComparison.OrdinalIgnoreCase) &&
        !args.Uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
        args.Cancel = true;
};
```

### 9.2 업데이트 보안 (v1.2.0)

- **파일명 검증**: `Path.GetFileName()` + 정규식(`^[a-zA-Z0-9._\-]+$`)으로 경로 탐색 공격 차단
- **다운로드 URL 검증**: `https://github.com/` 도메인만 허용
- **임시파일 격리**: GUID 기반 임시 디렉토리 사용, 실패 시 자동 정리
- **에러 메시지**: 내부 예외 정보를 UI에 노출하지 않음 (사용자 친화적 메시지로 대체)

### 9.3 데이터 보안 (v1.2.0)

- **인증 토큰 미저장**: `SessionKey`, `Cookies`를 config.json에 저장하지 않음. 인증은 WebView2의 쿠키 저장소(DPAPI 보호)에 위임
- **원자적 파일 저장**: temp 파일에 먼저 쓴 뒤 `File.Move()`로 교체하여 저장 중 크래시에도 파일 손상 방지
- **빌드 비밀번호**: MSIX 인증서 비밀번호는 환경변수(`MSIX_CERT_PASSWORD`)로 관리, 소스에 하드코딩하지 않음

### 9.4 gitignore

민감 파일 제외 목록: `certs/`, `*.pfx`, `*.log`, `*.user`

---

## 10. 파일 구조

```
claude_usage_tracker/
├── ClaudeUsageTracker.csproj      # .NET 8 WPF 프로젝트
├── App.xaml                        # Application 리소스
├── App.xaml.cs                     # 앱 생명주기, 트레이
├── AssemblyInfo.cs                 # 어셈블리 정���
├── installer.iss                   # Inno Setup 설치 스크립트
├── .gitignore
├── Assets/
│   └── icon.ico                    # 앱 아이콘
├── Models/
│   └── UsageData.cs                # 데이터 모델 (UsageApiResponse, LatestUsage 등)
├── Services/
│   ├── ClaudeApiService.cs         # WebView2 + postMessage API 통신
│   ├── UsageService.cs             # 비즈니스 로직, 상태 관리
│   ├── StorageService.cs           # JSON 파일 영구 저장
│   └── UpdateService.cs            # GitHub Releases 업데이트 확인
├── Views/
│   ├── MainWindow.xaml             # 메인 대시보드 UI (XAML)
│   ├── MainWindow.xaml.cs          # 메인 로직 (링, 차트, 이벤트)
│   ├── LoginWindow.xaml            # 로그인 창 UI
│   └── LoginWindow.xaml.cs         # 로그인 + 직접 fetch
├── Package.appxmanifest            # MSIX 패키지 매니페스트
├── build-msix.ps1                  # MSIX 빌드 스크립트
├── bin/                            # (빌드 출력)
├── obj/                            # (빌드 중간물)
├── publish/                        # (배포용 빌드 결과)
├── installer_output/               # (설치 프로그램)
│   └── ClaudeUsageTracker_Setup_v1.2.0.exe
├── msix_output/                    # (MSIX 패키지)
│   └── ClaudeUsageTracker_v1.2.0.msix
└── certs/                          # (서명 인증서, gitignore 대상)
```

---

## 11. 빌드 및 배포

### 12.1 개발 실행

```bash
dotnet run
```

### 12.2 배포 빌드 (Self-contained Single File)

```bash
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -o ./publish
```

결과: `publish/ClaudeUsageTracker.exe` (~155MB, .NET 런타임 포함)

### 12.3 설치 프로그램 (Inno Setup)

```bash
"C:\Users\...\Inno Setup 6\ISCC.exe" installer.iss
```

결과: `installer_output/ClaudeUsageTracker_Setup_v{버전}.exe` (~47MB, LZMA2 압축)

**설치 프로��램 기능**:
- 한국어/영어 설치 마법사
- ���탕화면 바로가기 (선택)
- Windows 시작 시 자동 ���행 (선택)
- 시작 메뉴 등록
- 설치 후 자동 실행
- 제거 시 AppData 정리
- 작업 표시줄 고정 옵션 (v1.2.0)

### 12.4 MSIX 패키지 빌드 (v1.2.0)

```powershell
# 환경변수로 비밀번호 설정 (또는 실행 시 입력)
$env:MSIX_CERT_PASSWORD = "your_password"
.\build-msix.ps1
```

**과정**: 자체 서명 인증서 생성 → dotnet publish → makeappx pack → signtool 서명

**설치 방법**:
1. 인증서를 신뢰할 수 있는 루트에 설치 (최초 1회, 관리자 권한)
2. `.msix` 파일 더블클릭하여 설치

### 11.5 버전 올릴 때 변경해야 할 파일

| 파일 | 변경 항목 |
|---|---|
| `ClaudeUsageTracker.csproj` | `Version`, `AssemblyVersion`, `FileVersion`, `InformationalVersion` |
| `installer.iss` | `AppVersion`, `AppVerName`, `OutputBaseFilename` |
| `Package.appxmanifest` | `Version` |
| `build-msix.ps1` | `$Version` 파라미터 기본값 |

### 11.6 GitHub Releases 배포

```bash
git add -A && git commit -m "v1.x.0: ..." && git tag v1.x.0
git push -u origin main && git push origin v1.x.0
```

GitHub에서 Release 생성 → `ClaudeUsageTracker_Setup_v1.x.0.exe`를 asset으로 첨부.

---

## 12. 알려진 이슈와 해결 방법

### 12.1 ExecuteScriptAsync + async IIFE 문제

**증상**: `ExecuteScriptAsync`로 async IIFE 실행 ��� null/빈 값 반환
**원인**: WebView2의 async 함수 반환값 처리 불안정
**해결**: `window.chrome.webview.postMessage()` + `WebMessageReceived` 이벤트 방식으로 전환. `.then()` 체인 사용 (async/await 금지).

### 12.2 WebView2 쿠키 동기화

**증상**: LoginWindow에서 로그인 후 BgWebView에서 fetch 실패 ("not_logged_in")
**원인**: 같은 UserDataFolder를 쓰지만 ��키 동기화에 시간 필요
**해결**: LoginWindow에서 직접 fetch 후 결과를 MainWindow에 전달. BgWebView는 백그라운드에서 리로드.

### 12.3 WPF + WinForms 네임스페이스 충돌

**증상**: CS0104 ambiguous reference (Color, Brush, Point, Size, Rectangle, MessageBox 등)
**���결**: `using` 별칭으로 명시적 지정
```csharp
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
```
**참고 (v1.2.0)**: `Brush`, `Rectangle` alias는 미사용으로 제거됨. 현재 사용 중인 alias: `Color`, `Point`, `Size`.

### 12.4 프로세스 잠금

**증상**: 빌드 시 exe 접근 거부
**해결**: 트레이에서 종료 또는 `taskkill /IM ClaudeUsageTracker.exe /F`

---

## 13. 버전 규칙

**x.y.z (Semantic Versioning)**
- **x** (major): 대규모 업데이트
- **y** (minor): 기능 추가
- **z** (patch): 버그 수���, 텍스트 수정

### 13.1 버전 이력

| 버전 | 날짜 | 주요 변경 |
|---|---|---|
| 1.0.0 | 2026-04-13 | 초기 릴리즈. WPF + WebView2 대시보드, 로그인, 트레이, 알림, 히스토리 |
| 1.1.0 | 2026-04-13 | 자동 업데이트, 꺾은선 차트, 로그아웃/계정 전환, 항상 위, Claude 버튼, 업데이트 확인 버튼, Inno Setup 설치 프로그램 |
| 1.2.0 | 2026-04-14 | 코드 정리/최적화, 보안 강화 (WebView2 네비게이션 제한, 업데이트 파일 검증, 에러 메시지 정리, 민감 데이터 저장 제거), ExtraUsage nullable 수정, MSIX 빌드 지원, 작업 표시줄 고정 옵션, 미사용 코드/리소스 제거 |

---

## 14. 향후 확장 가능성

### 14.1 단기

- 설정 UI (poll 간격, 알림 임계값)
- CSV 내보내기 (히스토리 다운로드)
- 다국어 (한국어/영어 전환)

### 14.2 중기

- ���중 계정 프로필
- 반응형 레이아웃 (창 크기 적응)
- GitHub Actions 자동 빌드/릴리즈

### 14.3 구현 불가능

- **토큰 수 표시**: API 응답에 없음
- **macOS 지원**: WPF/WebView2가 Windows 전용. Avalonia UI 또는 Electron으로 재작성 필요.
- **Chrome Extension 연동**: 보안 경계로 쿠키 공유 불가

---

## 15. 라이선스 및 면책

이 앱은 **Anthropic의 공식 제품이 아닙니다.** 사용하는 API는 비공식이며 사전 통보 없이 변경/차단될 수 있습니다.

- 네트워크 전송 없음: 모든 데이터 로컬 저장
- 로그인 쿠키: WebView2 UserDataFolder에 Chromium이 관���
- 외부 서버 없음: zitify.co.kr은 브라���저 링크용

---

**문서 끝.**
최종 업데이트: 2026-04-14 · v1.2.0
