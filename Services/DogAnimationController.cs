using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using WpfBrush   = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor   = System.Windows.Media.Color;
using WpfPoint   = System.Windows.Point;
using WpfRect    = System.Windows.Rect;

namespace AIUsageTracker.Services;

// ═══════════════════════════════════════════════════════════════
//  DogAnimationController — 강아지 캐릭터 3마리를 화면에서 뛰어놀게 함
// ═══════════════════════════════════════════════════════════════
public sealed class DogAnimationController : IDisposable
{
    private readonly Canvas    _canvas;
    private readonly UIElement _searchRoot;
    private readonly List<DogActor> _dogs = [];
    private readonly DispatcherTimer _animTimer;
    private readonly DispatcherTimer _obstacleTimer;
    private List<Rect> _obstacles = [];
    private static readonly Random Rng = new();

    public DogAnimationController(Canvas canvas, UIElement searchRoot)
    {
        _canvas     = canvas;
        _searchRoot = searchRoot;

        _animTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(33)   // ~30 fps
        };
        _animTimer.Tick += AnimTick;

        _obstacleTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1.2)
        };
        _obstacleTimer.Tick += ObstacleTick;
    }

    public void Start(int count = 4)
    {
        _canvas.SizeChanged += OnSizeChanged;
        // 4종이 모두 한 번씩 등장하도록 index→breed 1:1 매핑 (count>=4면 자연 분산)
        for (int i = 0; i < count; i++)
            _dogs.Add(new DogActor(_canvas, Rng, i, (DogBreed)(i % 4)));
        _animTimer.Start();
        _obstacleTimer.Start();
    }

    public void Stop()
    {
        _animTimer.Stop();
        _obstacleTimer.Stop();
        _canvas.SizeChanged -= OnSizeChanged;
        foreach (var d in _dogs) d.Remove();
        _dogs.Clear();
    }

    private void OnSizeChanged(object s, SizeChangedEventArgs e) => ScanObstacles();
    private void ObstacleTick(object? s, EventArgs e) => ScanObstacles();

    private void ScanObstacles()
    {
        var list = new List<Rect>();
        Collect(_searchRoot, list);
        _obstacles = list;
    }

    private void Collect(DependencyObject parent, List<Rect> list)
    {
        int n = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is Border b && b.IsVisible
                && b.CornerRadius.TopLeft >= 8
                && b.ActualWidth > 80 && b.ActualHeight > 50)
            {
                try
                {
                    var p = b.TransformToAncestor(_canvas).Transform(default);
                    list.Add(new Rect(p.X, p.Y, b.ActualWidth, b.ActualHeight));
                }
                catch { /* not in same visual tree */ }
            }
            Collect(child, list);
        }
    }

    private void AnimTick(object? s, EventArgs e)
    {
        double w = _canvas.ActualWidth, h = _canvas.ActualHeight;
        if (w <= 0 || h <= 0) return;
        foreach (var d in _dogs)
            d.Update(w, h, _obstacles);
    }

    public void Dispose() => Stop();
}

// ═══════════════════════════════════════════════════════════════
//  DogBreed — 4종 (코기, 비숑, 골든 리트리버, 초코 푸들)
//  HTML mockup (gemini-code) 디자인을 WPF 64×44 sprite로 포팅
// ═══════════════════════════════════════════════════════════════
public enum DogBreed { Corgi, Bichon, Golden, Poodle }

internal enum EarStyle { Pointy, FluffyRound, FloppyLong, CurlyPompom }
internal enum TailStyle { Curl, Pompom, Feather }
internal enum AccessoryKind { Scarf, Collar, Bandana, Bow }

internal sealed class BreedPalette
{
    public required string Name;
    public required WpfColor Body, BodyShadow, Cream, Dark, AccA, AccB, Pink;
    public required EarStyle Ear;
    public required TailStyle Tail;
    public required AccessoryKind Accessory;

    public static BreedPalette For(DogBreed b) => b switch
    {
        DogBreed.Corgi => new BreedPalette {
            Name = "Corgi",
            Body       = WpfColor.FromRgb(0xFF, 0x9F, 0x43),
            BodyShadow = WpfColor.FromRgb(0xE6, 0x8A, 0x35),
            Cream      = WpfColor.FromRgb(0xFF, 0xF6, 0xEC),
            Dark       = WpfColor.FromRgb(0x3A, 0x28, 0x20),
            AccA       = WpfColor.FromRgb(0x48, 0xDB, 0xFB), // blue scarf
            AccB       = WpfColor.FromRgb(0x2E, 0xB8, 0xD9),
            Pink       = WpfColor.FromRgb(0xFF, 0x7B, 0x9C),
            Ear = EarStyle.Pointy, Tail = TailStyle.Curl,
            Accessory = AccessoryKind.Scarf,
        },
        DogBreed.Bichon => new BreedPalette {
            Name = "Bichon",
            Body       = WpfColor.FromRgb(0xFF, 0xFF, 0xFF),
            BodyShadow = WpfColor.FromRgb(0xE0, 0xE0, 0xE6),
            Cream      = WpfColor.FromRgb(0xFA, 0xFA, 0xFC),
            Dark       = WpfColor.FromRgb(0x3A, 0x28, 0x20),
            AccA       = WpfColor.FromRgb(0xFF, 0x7B, 0x9C), // pink collar
            AccB       = WpfColor.FromRgb(0xFF, 0xD3, 0x2A), // yellow tag
            Pink       = WpfColor.FromRgb(0xFF, 0x7B, 0x9C),
            Ear = EarStyle.FluffyRound, Tail = TailStyle.Pompom,
            Accessory = AccessoryKind.Collar,
        },
        DogBreed.Golden => new BreedPalette {
            Name = "Golden",
            Body       = WpfColor.FromRgb(0xF5, 0xC6, 0x64),
            BodyShadow = WpfColor.FromRgb(0xD9, 0xA9, 0x48),
            Cream      = WpfColor.FromRgb(0xFD, 0xEB, 0xB3),
            Dark       = WpfColor.FromRgb(0x3A, 0x28, 0x20),
            AccA       = WpfColor.FromRgb(0xFF, 0x52, 0x52), // red bandana
            AccB       = WpfColor.FromRgb(0xFF, 0xFF, 0xFF), // dots
            Pink       = WpfColor.FromRgb(0xFF, 0x7B, 0x9C),
            Ear = EarStyle.FloppyLong, Tail = TailStyle.Feather,
            Accessory = AccessoryKind.Bandana,
        },
        DogBreed.Poodle => new BreedPalette {
            Name = "Poodle",
            Body       = WpfColor.FromRgb(0x6D, 0x4C, 0x41),
            BodyShadow = WpfColor.FromRgb(0x5D, 0x40, 0x37),
            Cream      = WpfColor.FromRgb(0x8D, 0x6E, 0x63),
            Dark       = WpfColor.FromRgb(0x2D, 0x1B, 0x15),
            AccA       = WpfColor.FromRgb(0x00, 0xCE, 0xC9), // mint bow
            AccB       = WpfColor.FromRgb(0xFF, 0xFF, 0xFF),
            Pink       = WpfColor.FromRgb(0xFF, 0x9F, 0xF3),
            Ear = EarStyle.CurlyPompom, Tail = TailStyle.Pompom,
            Accessory = AccessoryKind.Bow,
        },
        _ => For(DogBreed.Corgi),
    };
}

// ═══════════════════════════════════════════════════════════════
//  DogActor — 개별 강아지 캐릭터 (상태 머신 + 스프라이트)
// ═══════════════════════════════════════════════════════════════
internal sealed class DogActor
{
    // ── 크기 ────────────────────────────────────────────────────
    //  HTML 'gemini-code-1777825562665.html' 디자인을 그대로 입력 →
    //  ScaleTransform(S)로 축소하여 W×H 스프라이트 영역에 맞춤.
    //  HTML 원점(0,0)은 스프라이트 중심 (W/2, H/2)에 매핑됨.
    const double W = 72, H = 52;
    const double Speed = 2.8;
    const double S = 0.7;       // HTML 좌표 → 스프라이트 스케일

    // ── 행동 상태 ────────────────────────────────────────────────
    enum State { Running, Sitting, Hiding, Sniffing, Zoomies, Bark }

    private readonly Canvas _parent;
    private readonly Random _rng;
    private readonly Canvas _sprite;
    private readonly BreedPalette _palette;

    double _x, _y, _vx, _vy;
    bool   _facingRight = true;
    State  _state       = State.Running;
    double _stateTimer;
    Rect   _hideTarget;
    double _t;        // 누적 시간(초)
    double _bounceY;  // 수직 바운스 오프셋

    // ── 스프라이트 부위별 변환 ────────────────────────────────────
    readonly ScaleTransform     _flip      = new(1, 1);
    readonly RotateTransform    _bodyBob   = new();
    readonly RotateTransform    _tailWag   = new();
    readonly RotateTransform    _legFL     = new();
    readonly RotateTransform    _legFR     = new();
    readonly RotateTransform    _legBL     = new();
    readonly RotateTransform    _legBR     = new();
    readonly TranslateTransform _noseSniff = new();
    readonly RotateTransform    _earFlop   = new();   // 귀 펄럭임
    readonly RotateTransform    _headTilt  = new();   // 고개 갸웃
    readonly TranslateTransform _tongueWag = new();   // 혀 흔들림
    readonly RotateTransform    _eyebrow   = new();   // 눈썹 표정
    UIElement? _tongue;

    public DogActor(Canvas parent, Random rng, int index, DogBreed breed)
    {
        _parent  = parent;
        _rng     = rng;
        _palette = BreedPalette.For(breed);
        _t       = index * 1.4;   // 위상 오프셋

        double cw = parent.ActualWidth  > 0 ? parent.ActualWidth  : 600;
        double ch = parent.ActualHeight > 0 ? parent.ActualHeight : 500;
        _x = 60 + rng.NextDouble() * (cw - 180);
        _y = 60 + rng.NextDouble() * (ch - 180);

        double a = rng.NextDouble() * Math.PI * 2;
        _vx = Math.Cos(a) * Speed;
        _vy = Math.Sin(a) * Speed * 0.4;
        _stateTimer = 2 + rng.NextDouble() * 4;

        _sprite = BuildSprite();
        Canvas.SetLeft(_sprite, _x);
        Canvas.SetTop(_sprite,  _y);
        parent.Children.Add(_sprite);
    }

    public void Remove() => _parent.Children.Remove(_sprite);

    // ── 매 프레임 호출 ────────────────────────────────────────────
    public void Update(double cw, double ch, List<Rect> obstacles)
    {
        _t          += 0.033;
        _stateTimer -= 0.033;
        _bounceY     = 0;

        switch (_state)
        {
            case State.Running:  DoRunning(cw, ch, obstacles); break;
            case State.Sitting:  DoSitting();                   break;
            case State.Hiding:   DoHiding(cw, ch);              break;
            case State.Sniffing: DoSniffing();                  break;
            case State.Zoomies:  DoZoomies(cw, ch);             break;
            case State.Bark:     DoBark();                      break;
        }

        Canvas.SetLeft(_sprite, _x);
        Canvas.SetTop(_sprite,  _y + _bounceY);
        _flip.ScaleX = _facingRight ? 1 : -1;
    }

    // ────────────────────────────────────────────────────────────
    //  Running — 달리기 (귀 펄럭, 혀 내밀기, 수직 바운스)
    // ────────────────────────────────────────────────────────────
    void DoRunning(double cw, double ch, List<Rect> obstacles)
    {
        AnimateLegs(14, 26);
        _bodyBob.Angle  = Math.Sin(_t * 14) * 2;
        _tailWag.Angle  = Math.Sin(_t * 16) * 28;
        _earFlop.Angle  = Math.Sin(_t * 14) * 14;   // 귀가 달리면서 펄럭
        _headTilt.Angle = 0;
        _eyebrow.Angle  = -3;
        _noseSniff.Y    = 0;
        _bounceY        = Math.Abs(Math.Sin(_t * 14)) * -2.5;  // 종종 뛰는 느낌
        ShowTongue(true);
        _tongueWag.Y = Math.Sin(_t * 14) * 2.5;   // 혀가 위아래로 흔들림

        _x += _vx; _y += _vy;
        UpdateFacing();
        Bounce(cw, ch);

        foreach (var obs in obstacles)
        {
            var dogCenter = new WpfPoint(_x + W / 2, _y + H / 2);
            if (obs.Contains(dogCenter))
            {
                _vx = dogCenter.X < obs.Left + obs.Width / 2 ? -Speed : Speed;
                _vy = (_rng.Next(2) == 0 ? -1 : 1) * Speed * 0.35;
                break;
            }
        }

        if (_stateTimer > 0) return;
        switch (_rng.Next(13))
        {
            case < 3:  TurnRandom(); _stateTimer = 2.5 + _rng.NextDouble() * 4; break;
            case < 6:  Sit();   break;
            case < 8:  Sniff(); break;
            case < 10 when obstacles.Count > 0: Hide(obstacles); break;
            case < 12: Zoom();  break;
            default:   Bark();  break;
        }
    }

    // ────────────────────────────────────────────────────────────
    //  Sitting — 앉기 (고개 갸웃, 꼬리 신나게 흔들기)
    // ────────────────────────────────────────────────────────────
    void DoSitting()
    {
        _legFL.Angle    =  38; _legFR.Angle  =  38;
        _legBL.Angle    = -28; _legBR.Angle  = -28;
        _bodyBob.Angle  = 0;
        _tailWag.Angle  = Math.Sin(_t * 6) * 34;
        _earFlop.Angle  = Math.Sin(_t * 2.5) * 5;
        _headTilt.Angle = Math.Sin(_t * 1.8) * 14;  // 고개 갸웃!
        _eyebrow.Angle  = -6;                        // 눈썹 올라감 (기대하는 표정)
        _noseSniff.Y    = 0;
        ShowTongue(false);

        if (_stateTimer <= 0) Run();
    }

    // ────────────────────────────────────────────────────────────
    //  Hiding — 카드 아래로 숨기 (납작하게 기어가기)
    // ────────────────────────────────────────────────────────────
    void DoHiding(double cw, double ch)
    {
        double targetX = _hideTarget.Left + (_hideTarget.Width - W) * 0.5;
        double targetY = _hideTarget.Bottom - H * 0.85;
        targetX = Math.Clamp(targetX, 0, cw - W);
        targetY = Math.Clamp(targetY, 0, ch - H);

        double dx = targetX - _x, dy = targetY - _y;
        double dist = Math.Sqrt(dx * dx + dy * dy);

        if (dist > 5)
        {
            double spd = Math.Min(Speed * 1.8, dist);
            _vx = dx / dist * spd; _vy = dy / dist * spd;
            _x += _vx; _y += _vy;
            UpdateFacing();
            AnimateLegs(18, 32);
            _tailWag.Angle  = Math.Sin(_t * 20) * 28;
            _earFlop.Angle  = Math.Sin(_t * 18) * 10;
            _headTilt.Angle = 0;
            _eyebrow.Angle  = 0;
        }
        else
        {
            // 도착 → 납작하게 숨기
            _vx = 0; _vy = 0;
            _legFL.Angle    = 50; _legFR.Angle  = 50;
            _legBL.Angle    =-35; _legBR.Angle  =-35;
            _bodyBob.Angle  = -8;
            _tailWag.Angle  = Math.Sin(_t * 4) * 10;
            _earFlop.Angle  = -15;   // 귀 납작하게
            _headTilt.Angle = 0;
            _eyebrow.Angle  = 8;     // 걱정하는 눈썹
        }
        ShowTongue(false);
        if (_stateTimer <= 0) Run();
    }

    // ────────────────────────────────────────────────────────────
    //  Sniffing — 냄새 맡기 (집중하는 표정, 귀 앞으로)
    // ────────────────────────────────────────────────────────────
    void DoSniffing()
    {
        _bodyBob.Angle  = Math.Sin(_t * 7) * 7;
        _noseSniff.Y    = Math.Sin(_t * 7) * 3;
        _tailWag.Angle  = Math.Sin(_t * 5) * 22;
        _earFlop.Angle  = 12;    // 귀 앞으로 (집중)
        _headTilt.Angle = 0;
        _eyebrow.Angle  = 6;     // 찌푸린 눈썹 (집중)
        _legFL.Angle    =  14; _legFR.Angle  = -14;
        _legBL.Angle    =  -8; _legBR.Angle  =   8;
        ShowTongue(false);

        if (_stateTimer <= 0) Run();
    }

    // ────────────────────────────────────────────────────────────
    //  Zoomies — 미친 듯이 달리기 (귀 뒤로, 혀 크게 날림)
    // ────────────────────────────────────────────────────────────
    void DoZoomies(double cw, double ch)
    {
        AnimateLegs(24, 42);
        _bodyBob.Angle  = Math.Sin(_t * 24) * 5;
        _tailWag.Angle  = Math.Sin(_t * 28) * 42;
        _earFlop.Angle  = -22;   // 귀가 속도로 뒤로 젖혀짐
        _headTilt.Angle = 0;
        _eyebrow.Angle  = -5;
        _bounceY        = Math.Abs(Math.Sin(_t * 24)) * -4;  // 큰 점프
        ShowTongue(true);
        _tongueWag.Y = Math.Sin(_t * 24) * 5;   // 혀가 크게 흔들림

        _x += _vx * 2.2; _y += _vy * 2.2;
        UpdateFacing();
        Bounce(cw, ch);

        if (_stateTimer <= 0) Sit();
    }

    // ────────────────────────────────────────────────────────────
    //  Bark — 짖기 (몸 튀기기, 꼬리 최대 흔들기, 혀 번쩍)
    // ────────────────────────────────────────────────────────────
    void DoBark()
    {
        _vx = 0; _vy = 0;
        _legFL.Angle    = -8; _legFR.Angle  = -8;
        _legBL.Angle    =  5; _legBR.Angle  =  5;

        double barkCycle = Math.Sin(_t * 10);
        _bodyBob.Angle  = barkCycle * 6;
        _headTilt.Angle = barkCycle * 4;      // 짖을 때 고개가 앞뒤로
        _tailWag.Angle  = Math.Sin(_t * 9) * 40;
        _earFlop.Angle  = -10;               // 귀 쫑긋
        _eyebrow.Angle  = -8;               // 눈썹 최대로 올림 (흥분)
        _noseSniff.Y    = 0;
        _bounceY        = barkCycle > 0.5 ? -3 : 0;   // 짖을 때 살짝 점프
        ShowTongue(barkCycle > 0.6);          // 짖을 때마다 혀 번쩍
        _tongueWag.Y    = 0;

        if (_stateTimer <= 0) Run();
    }

    // ── 헬퍼 ─────────────────────────────────────────────────────
    void AnimateLegs(double freq, double swing)
    {
        double s = Math.Sin(_t * freq) * swing;
        _legFL.Angle =  s; _legBR.Angle =  s;
        _legFR.Angle = -s; _legBL.Angle = -s;
    }

    void ShowTongue(bool show)
    {
        if (_tongue is not null)
            _tongue.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    void UpdateFacing()
    {
        if (_vx >  0.15) _facingRight = true;
        if (_vx < -0.15) _facingRight = false;
    }

    void Bounce(double cw, double ch)
    {
        if (_x < 0)      { _x = 0;      _vx =  Math.Abs(_vx); }
        if (_x + W > cw) { _x = cw - W; _vx = -Math.Abs(_vx); }
        if (_y < 0)      { _y = 0;      _vy =  Math.Abs(_vy); }
        if (_y + H > ch) { _y = ch - H; _vy = -Math.Abs(_vy); }
    }

    void TurnRandom()
    {
        double a = _rng.NextDouble() * Math.PI * 2;
        _vx = Math.Cos(a) * Speed;
        _vy = Math.Sin(a) * Speed * 0.4;
    }

    void Run()   { TurnRandom(); _state = State.Running;  _stateTimer = 3   + _rng.NextDouble() * 5; }
    void Sit()   { _vx = 0; _vy = 0; _state = State.Sitting;  _stateTimer = 1.5 + _rng.NextDouble() * 3; }
    void Sniff() { _vx = 0; _vy = 0; _state = State.Sniffing; _stateTimer = 1   + _rng.NextDouble() * 2; }
    void Zoom()  { TurnRandom(); _state = State.Zoomies;  _stateTimer = 1.5 + _rng.NextDouble() * 2; }
    void Bark()  { _vx = 0; _vy = 0; _state = State.Bark;     _stateTimer = 1   + _rng.NextDouble() * 2; }
    void Hide(List<Rect> obs)
    {
        _hideTarget = obs[_rng.Next(obs.Count)];
        _state      = State.Hiding;
        _stateTimer = 2 + _rng.NextDouble() * 3;
    }

    // ════════════════════════════════════════════════════════════
    //  스프라이트 생성 — 4종 분기
    //  HTML 원본 'gemini-code-1777825562665.html' 디자인을 HTML 좌표
    //  그대로 입력해 ScaleTransform(S)로 W×H 영역에 맞춰 축소함.
    //  HTML 원점(0,0)은 content 캔버스 위치 (W/2, H/2)에 매핑.
    //  좌표 컨벤션: HTML drawEllipse(cx,cy,rx,ry) → El(cx,cy,rx,ry)
    //
    //  다리 변환 매핑(대각선 보행 위상 일치):
    //    HTML bl1 → _legBL  (근거리-뒷다리, 앞 레이어)
    //    HTML bl2 → _legBR  (원거리-뒷다리, 뒷 레이어)
    //    HTML fl1 → _legFL  (근거리-앞다리, 앞 레이어)
    //    HTML fl2 → _legFR  (원거리-앞다리, 뒷 레이어)
    // ════════════════════════════════════════════════════════════
    Canvas BuildSprite()
    {
        var sprite = new Canvas { Width = W, Height = H, IsHitTestVisible = false };
        sprite.RenderTransformOrigin = new WpfPoint(0.5, 0.5);
        sprite.RenderTransform = _flip;

        var content = new Canvas { IsHitTestVisible = false };
        Canvas.SetLeft(content, W / 2);
        Canvas.SetTop(content, H / 2);
        content.RenderTransform = new ScaleTransform(S, S);
        sprite.Children.Add(content);

        switch (_palette.Name)
        {
            case "Corgi":  BuildCorgi(content);  break;
            case "Bichon": BuildBichon(content); break;
            case "Golden": BuildGolden(content); break;
            default:       BuildPoodle(content); break;
        }
        return sprite;
    }

    // ── 1. 식빵 코기 (오렌지 + 흰 양말 4개 + 파랑 스카프) ──────────
    void BuildCorgi(Canvas c)
    {
        var body   = B(_palette.Body);
        var bodyS  = B(_palette.BodyShadow);
        var cream  = B(_palette.Cream);
        var dark   = B(_palette.Dark);
        var blue   = B(_palette.AccA);
        var pink   = B(_palette.Pink);
        var earIn  = B(WpfColor.FromRgb(0xFF, 0xD1, 0xD9));
        var sock   = B(WpfColor.FromRgb(0xEA, 0xE0, 0xD6));
        var blush  = B(WpfColor.FromArgb(0x80, 0xFF, 0x7B, 0x9C));

        // 꼬리 (오렌지 + 크림 끝)
        var tail = SubCv(-38, 5, _tailWag);
        tail.Children.Add(El(-5, 0, 10, 6, body));
        tail.Children.Add(Cir(-10, 0, 5, cream));
        c.Children.Add(tail);

        // 뒤 레이어 다리 (그림자 컬러)
        c.Children.Add(LegPair(-15, 18, _legBR, bodyS, sock, 6, 12, 6.5, 6, 6, 13));
        c.Children.Add(LegPair( 15, 18, _legFR, bodyS, sock, 6, 12, 6.5, 6, 6, 13));

        // 몸통 + 배
        var bodyEl = El(-5, 12, 34, 22, body);
        bodyEl.RenderTransformOrigin = new WpfPoint(0.5, 0.5);
        bodyEl.RenderTransform = _bodyBob;
        c.Children.Add(bodyEl);
        c.Children.Add(El(-5, 20, 30, 12, cream));

        // 앞 레이어 다리 (오렌지 + 크림 양말)
        c.Children.Add(LegPair(-25, 20, _legBL, body, cream, 7, 12, 7.5, 6, 5, 12));
        c.Children.Add(LegPair(  5, 20, _legFL, body, cream, 7, 12, 7.5, 6, 5, 12));

        // 파랑 스카프
        var scarf = SubCv(10, 8, new RotateTransform(RadDeg(0.2)));
        scarf.Children.Add(El(0, 0, 10, 18, blue));
        scarf.Children.Add(Tri(0, 15, -8, 25, 2, 22, blue));
        c.Children.Add(scarf);

        // 머리
        var head = SubCv(18, -6, _headTilt);

        // 뾰족 귀 (양쪽 안쪽 핑크)
        var earL = SubCv(-12, -15, ComposeRot(_earFlop, RadDeg(-0.3)));
        earL.Children.Add(El(0, 0, 10, 20, body));
        earL.Children.Add(El(0, 3, 5, 14, earIn));
        head.Children.Add(earL);

        var earR = SubCv(12, -15, ComposeRot(_earFlop, RadDeg(0.3)));
        earR.Children.Add(El(0, 0, 10, 20, body));
        earR.Children.Add(El(0, 3, 5, 14, earIn));
        head.Children.Add(earR);

        // 머리 본체 + 양 볼 + 주둥이
        head.Children.Add(El(0, 0, 26, 21, body));
        head.Children.Add(El(-11, 8, 13, 11, cream));
        head.Children.Add(El( 11, 8, 13, 11, cream));
        head.Children.Add(El(  0, 10, 16, 13, cream));

        // 코 (sniff 변환)
        var nose = El(0, 4, 5, 3.5, dark);
        nose.RenderTransform = _noseSniff;
        head.Children.Add(nose);
        head.Children.Add(El(-1, 3, 2, 1, B(WpfColor.FromRgb(0xFF, 0xFF, 0xFF))));

        // 눈
        AddEye(head, -13, -2, 4.5);
        AddEye(head,  13, -2, 4.5);

        // 미소 (양쪽 곡선 입)
        head.Children.Add(Arc(-5, 9, 5, 0, 180, dark, 2.5));
        head.Children.Add(Arc( 5, 9, 5, 0, 180, dark, 2.5));

        // 혀 (헥헥용)
        var tongue = El(0, 13, 4, 6, pink);
        tongue.Visibility = Visibility.Collapsed;
        tongue.RenderTransform = _tongueWag;
        _tongue = tongue;
        head.Children.Add(tongue);

        // 볼 블러시
        head.Children.Add(El(-19, 5, 6, 3.5, blush));
        head.Children.Add(El( 19, 5, 6, 3.5, blush));

        c.Children.Add(head);
    }

    // ── 2. 구름 비숑 (흰 솜뭉치 + 핑크 목걸이) ─────────────────────
    void BuildBichon(Canvas c)
    {
        var bodyW   = B(_palette.Body);          // WHITE
        var bodyS   = B(_palette.BodyShadow);     // 그림자 흰색
        var dark    = B(_palette.Dark);
        var pink    = B(_palette.AccA);           // 핑크 목걸이
        var yellow  = B(_palette.AccB);           // 노란 인식표
        var earPink = B(WpfColor.FromRgb(0xFF, 0x7B, 0x9C));
        var blush   = B(WpfColor.FromArgb(0x66, 0xFF, 0x7B, 0x9C));
        var legShad = B(WpfColor.FromRgb(0xE0, 0xE0, 0xE6));

        // 뒤 다리 (짧음, 그림자 컬러)
        c.Children.Add(LegBichon(-4, 24, _legBR, legShad));
        c.Children.Add(LegBichon(22, 24, _legFR, legShad));

        // 몸통: 그림자 솜뭉치 → 흰 솜뭉치
        c.Children.Add(Cir(  5, 12, 26, bodyS));
        c.Children.Add(Cir(-14, 14, 15, bodyS));
        c.Children.Add(Cir( 24, 14, 15, bodyS));
        var bodyMain = Cir(3, 10, 25, bodyW);
        bodyMain.RenderTransformOrigin = new WpfPoint(0.5, 0.5);
        bodyMain.RenderTransform = _bodyBob;
        c.Children.Add(bodyMain);
        c.Children.Add(Cir(-12, 13, 14, bodyW));
        c.Children.Add(Cir( 20, 13, 14, bodyW));

        // 앞 다리
        c.Children.Add(LegBichon(-14, 25, _legBL, bodyW));
        c.Children.Add(LegBichon( 14, 25, _legFL, bodyW));

        // 핑크 목걸이 + 노란 인식표
        var collar = SubCv(4, -5);
        collar.Children.Add(El(0, 0, 14, 6, pink));
        collar.Children.Add(Cir(0, 4, 5, yellow));
        collar.Children.Add(Cir(0, 6, 1, dark));
        c.Children.Add(collar);

        // 머리
        var head = SubCv(4, -12, _headTilt);

        // 머리 솜뭉치 (5겹)
        head.Children.Add(Cir(  0,   0, 28, bodyS));
        head.Children.Add(Cir(  0,  -2, 28, bodyW));
        head.Children.Add(Cir(-16, -10, 16, bodyW));
        head.Children.Add(Cir( 16, -10, 16, bodyW));
        head.Children.Add(Cir(  0, -20, 18, bodyW));

        // 보송한 귀 (양쪽 핑크 + 가운데 흰)
        var earGrp = SubCv(-14, -22, ComposeRot(_earFlop, RadDeg(-0.3)));
        earGrp.Children.Add(El(-8, 0, 9, 7, earPink));
        earGrp.Children.Add(El( 8, 0, 9, 7, earPink));
        earGrp.Children.Add(Cir(0, 0, 5, bodyW));
        head.Children.Add(earGrp);

        // 코
        var nose = El(0, 6, 5.5, 4, dark);
        nose.RenderTransform = _noseSniff;
        head.Children.Add(nose);
        head.Children.Add(El(-1, 5, 2, 1.5, B(WpfColor.FromRgb(0xFF, 0xFF, 0xFF))));

        // 눈
        AddEye(head, -12, 0, 4.5);
        AddEye(head,  12, 0, 4.5);

        // 미소 (반쪽 호)
        head.Children.Add(Arc(-5, 11, 5, 12, 168, dark, 2.5));
        head.Children.Add(Arc( 5, 11, 5, 12, 168, dark, 2.5));

        // 혀
        var tongue = El(0, 14, 4, 5, pink);
        tongue.Visibility = Visibility.Collapsed;
        tongue.RenderTransform = _tongueWag;
        _tongue = tongue;
        head.Children.Add(tongue);

        // 블러시
        head.Children.Add(Cir(-18, 6, 5.5, blush));
        head.Children.Add(Cir( 18, 6, 5.5, blush));

        c.Children.Add(head);
    }

    // ── 3. 골든 리트리버 (금색 + 빨강 반다나 + 처진 귀) ────────────
    void BuildGolden(Canvas c)
    {
        var gold    = B(_palette.Body);
        var goldS   = B(_palette.BodyShadow);
        var light   = B(_palette.Cream);
        var dark    = B(_palette.Dark);
        var red     = B(_palette.AccA);
        var white   = B(WpfColor.FromRgb(0xFF, 0xFF, 0xFF));
        var pink    = B(_palette.Pink);
        var legBot  = B(WpfColor.FromRgb(0xEA, 0xE0, 0xA6));
        var blush   = B(WpfColor.FromArgb(0x4C, 0xFF, 0x7B, 0x9C));

        // 깃털 꼬리 (기본 -0.3 rad 처짐 + tail wag)
        var tail = SubCv(-32, 10, ComposeRot(_tailWag, RadDeg(-0.3)));
        tail.Children.Add(El(-12, 0, 18, 8, gold));
        tail.Children.Add(El(-12, 4, 16, 6, light));
        c.Children.Add(tail);

        // 뒤 다리
        c.Children.Add(LegPair(-10, 18, _legBR, goldS, legBot, 7, 13, 8, 5, 1, 15));
        c.Children.Add(LegPair( 22, 18, _legFR, goldS, legBot, 7, 13, 8, 5, 1, 15));

        // 몸통 + 배
        var bodyEl = El(-4, 10, 36, 24, gold);
        bodyEl.RenderTransformOrigin = new WpfPoint(0.5, 0.5);
        bodyEl.RenderTransform = _bodyBob;
        c.Children.Add(bodyEl);
        c.Children.Add(El(-4, 18, 30, 14, light));

        // 앞 다리
        c.Children.Add(LegPair(-20, 20, _legBL, gold, light, 8, 14, 9, 6, 1, 16));
        c.Children.Add(LegPair( 12, 20, _legFL, gold, light, 8, 14, 9, 6, 1, 16));

        // 빨간 반다나 (목 삼각형 + 흰 점 3개)
        var bandana = SubCv(22, 5);
        bandana.Children.Add(Tri(-15, 0, 15, 0, 0, 18, red));
        bandana.Children.Add(Cir(  0, 4, 2, white));
        bandana.Children.Add(Cir( -6,-1, 2, white));
        bandana.Children.Add(Cir(  6,-1, 2, white));
        c.Children.Add(bandana);

        // 머리
        var head = SubCv(22, -6, _headTilt);

        head.Children.Add(El(0, 0, 24, 22, gold));
        head.Children.Add(El(0, 9, 18, 14, light));

        // 처진 긴 귀 (그림자 컬러)
        var earL = SubCv(-18, -4, ComposeRot(_earFlop, RadDeg(0.2)));
        earL.Children.Add(El(0, 14, 8, 20, goldS));
        head.Children.Add(earL);
        var earR = SubCv(18, -4, ComposeRot(_earFlop, RadDeg(-0.2)));
        earR.Children.Add(El(0, 14, 8, 20, goldS));
        head.Children.Add(earR);

        // 코
        var nose = El(4, 6, 6, 4, dark);
        nose.RenderTransform = _noseSniff;
        head.Children.Add(nose);
        head.Children.Add(El(3, 5, 2, 1, white));

        // 행복한 감은 눈 (∩ 모양)
        head.Children.Add(Quad(-10,-2, -6,-6, -2,-2, dark, 3));
        head.Children.Add(Quad( 10,-2,  6,-6,  2,-2, dark, 3));

        // 큰 미소 + 늘어진 혀
        head.Children.Add(Quad(-6, 12, 4, 20, 12, 10, dark, 3));
        var tongue = El(6, 16, 5, 8, pink);
        tongue.RenderTransformOrigin = new WpfPoint(0.5, 0.5);
        tongue.RenderTransform = _tongueWag;
        tongue.Visibility = Visibility.Collapsed;
        _tongue = tongue;
        head.Children.Add(tongue);

        // 블러시
        head.Children.Add(Cir(-10, 7, 5, blush));
        head.Children.Add(Cir( 18, 7, 5, blush));

        c.Children.Add(head);
    }

    // ── 4. 초코 푸들 (초콜릿 컬리 + 민트 보타이) ──────────────────
    void BuildPoodle(Canvas c)
    {
        var choco   = B(_palette.Body);
        var shadow  = B(_palette.BodyShadow);
        var mocha   = B(_palette.Cream);
        var dark    = B(_palette.Dark);
        var mint    = B(_palette.AccA);
        var white   = B(WpfColor.FromRgb(0xFF, 0xFF, 0xFF));
        var pink    = B(_palette.Pink);
        var legMid  = B(WpfColor.FromRgb(0x75, 0x58, 0x4D));
        var blush   = B(WpfColor.FromArgb(0x66, 0xFF, 0x7B, 0x9C));

        // 짧은 푸들 꼬리 (작은 동그라미 두 개)
        c.Children.Add(Cir(-26, 6, 9, choco));
        c.Children.Add(Cir(-32, 4, 6, choco));

        // 뒤 다리 (어두운 발끝 + 솜뭉치 발목)
        c.Children.Add(LegPoodle(-5, 18, _legBR, legMid, shadow, dark));
        c.Children.Add(LegPoodle(16, 18, _legFR, legMid, shadow, dark));

        // 몸통 (그림자 + 본체 + 측면 솜뭉치)
        c.Children.Add(El(-6, 12, 24, 20, shadow));
        var bodyEl = El(-6, 10, 24, 20, choco);
        bodyEl.RenderTransformOrigin = new WpfPoint(0.5, 0.5);
        bodyEl.RenderTransform = _bodyBob;
        c.Children.Add(bodyEl);
        c.Children.Add(Cir(-18, 8, 13, choco));
        c.Children.Add(Cir(  6, 6, 13, choco));

        // 앞 다리 (모카 색)
        c.Children.Add(LegPoodle(-15, 20, _legBL, mocha, choco, dark));
        c.Children.Add(LegPoodle(  8, 20, _legFL, mocha, choco, dark));

        // 머리
        var head = SubCv(16, -10, _headTilt);

        // 주둥이
        head.Children.Add(El(2, 6, 19, 16, mocha));

        // 컬리 늘어진 귀
        var earL = SubCv(-12, 4, ComposeRot(_earFlop, RadDeg(0.2)));
        earL.Children.Add(El(0, 10, 7, 14, choco));
        earL.Children.Add(Cir(0, 20, 8, choco));
        earL.Children.Add(Cir(-3, 14, 6, choco));
        earL.Children.Add(Cir( 3, 10, 6, choco));
        head.Children.Add(earL);

        var earR = SubCv(16, 4, ComposeRot(_earFlop, RadDeg(-0.2)));
        earR.Children.Add(El(0, 10, 7, 14, choco));
        earR.Children.Add(Cir(0, 20, 8, choco));
        earR.Children.Add(Cir( 3, 14, 6, choco));
        earR.Children.Add(Cir(-3, 10, 6, choco));
        head.Children.Add(earR);

        // 민트 보타이
        var bow = SubCv(14, 2, new RotateTransform(RadDeg(0.3)));
        bow.Children.Add(El(-6, 0, 7, 5, mint));
        bow.Children.Add(El( 6, 0, 7, 5, mint));
        bow.Children.Add(Cir(0, 0, 3, white));
        head.Children.Add(bow);

        // 머리 위 컬리 솜뭉치 (4겹)
        head.Children.Add(Cir(  2,  -6, 14, choco));
        head.Children.Add(Cir( 12,  -4, 11, choco));
        head.Children.Add(Cir( -8,  -4, 11, choco));
        head.Children.Add(Cir(  6, -14, 10, choco));

        // 눈
        AddEye(head, -5, 3, 4);
        AddEye(head, 11, 3, 4);

        // 코
        var nose = El(3, 9, 5, 3.5, dark);
        nose.RenderTransform = _noseSniff;
        head.Children.Add(nose);
        head.Children.Add(Cir(2, 8, 1.5, white));

        // 미소
        head.Children.Add(Arc(0, 13, 3.5, 0, 180, dark, 2));
        head.Children.Add(Arc(7, 13, 3.5, 0, 180, dark, 2));

        // 혀
        var tongue = El(3.5, 15, 3, 5, pink);
        tongue.Visibility = Visibility.Collapsed;
        tongue.RenderTransform = _tongueWag;
        _tongue = tongue;
        head.Children.Add(tongue);

        // 블러시
        head.Children.Add(Cir(-10, 8, 4, blush));
        head.Children.Add(Cir( 16, 8, 4, blush));

        c.Children.Add(head);
    }

    // ════════════════════════════════════════════════════════════
    //  공용 헬퍼 (HTML drawEllipse / drawCircle 매핑)
    // ════════════════════════════════════════════════════════════

    // 중심(cx,cy)에 (rx,ry) 반축인 타원
    static Ellipse El(double cx, double cy, double rx, double ry, WpfBrush fill)
    {
        var e = new Ellipse { Width = rx * 2, Height = ry * 2, Fill = fill };
        Canvas.SetLeft(e, cx - rx);
        Canvas.SetTop(e,  cy - ry);
        return e;
    }

    static Ellipse Cir(double cx, double cy, double r, WpfBrush fill)
        => El(cx, cy, r, r, fill);

    // (tx,ty) 위치의 회전 가능한 서브 캔버스. 회전 중심은 (0,0).
    static Canvas SubCv(double tx, double ty, Transform? t = null)
    {
        var p = new Canvas { IsHitTestVisible = false };
        Canvas.SetLeft(p, tx);
        Canvas.SetTop(p,  ty);
        if (t is not null) p.RenderTransform = t;
        return p;
    }

    // 동적 회전(애니메이션) + 정적 베이스 각도(deg) 합성
    static Transform ComposeRot(RotateTransform anim, double baseDeg)
    {
        var tg = new TransformGroup();
        tg.Children.Add(anim);
        tg.Children.Add(new RotateTransform(baseDeg));
        return tg;
    }

    static double RadDeg(double rad) => rad * 180.0 / Math.PI;

    // 캐싱된 frozen brush (반복 생성 비용 절감)
    static SolidColorBrush B(WpfColor c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    // ── 부속 기하: 호(미소), 삼각형(스카프 끝), 2차 베지어(눈/입) ─
    static Path Arc(double cx, double cy, double r,
                    double startDeg, double endDeg,
                    WpfBrush stroke, double thickness)
    {
        double sRad = startDeg * Math.PI / 180;
        double eRad = endDeg   * Math.PI / 180;
        var fig = new PathFigure { StartPoint = new WpfPoint(cx + r * Math.Cos(sRad), cy + r * Math.Sin(sRad)) };
        fig.Segments.Add(new ArcSegment(
            new WpfPoint(cx + r * Math.Cos(eRad), cy + r * Math.Sin(eRad)),
            new System.Windows.Size(r, r),
            0,
            Math.Abs(endDeg - startDeg) > 180,
            SweepDirection.Clockwise,
            true));
        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        return new Path
        {
            Data = geo, Stroke = stroke, StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
        };
    }

    static Path Tri(double x1, double y1, double x2, double y2, double x3, double y3, WpfBrush fill)
    {
        var fig = new PathFigure { StartPoint = new WpfPoint(x1, y1), IsClosed = true };
        fig.Segments.Add(new LineSegment(new WpfPoint(x2, y2), true));
        fig.Segments.Add(new LineSegment(new WpfPoint(x3, y3), true));
        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        return new Path { Data = geo, Fill = fill };
    }

    // 2차 베지어 곡선 (시작 → 컨트롤 → 끝)
    static Path Quad(double sx, double sy, double cx, double cy, double ex, double ey,
                     WpfBrush stroke, double thickness)
    {
        var fig = new PathFigure { StartPoint = new WpfPoint(sx, sy) };
        fig.Segments.Add(new QuadraticBezierSegment(new WpfPoint(cx, cy), new WpfPoint(ex, ey), true));
        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        return new Path
        {
            Data = geo, Stroke = stroke, StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
        };
    }

    // ── 다리 빌더 ─────────────────────────────────────────────

    // 일반 다리: 위쪽 타원(허벅지) + 아래쪽 타원(발). HTML drawCorgi/Golden 양식.
    static Canvas LegPair(double tx, double ty, RotateTransform rt,
                          WpfBrush legColor, WpfBrush pawColor,
                          double legRx, double legRy,
                          double pawRx, double pawRy,
                          double legCy, double pawCy)
    {
        var p = SubCv(tx, ty, rt);
        p.Children.Add(El(0, legCy, legRx, legRy, legColor));
        p.Children.Add(El(0, pawCy, pawRx, pawRy, pawColor));
        return p;
    }

    // 비숑 다리: 짧은 두 타원 (아래로 기둥형 발자국 표현)
    static Canvas LegBichon(double tx, double ty, RotateTransform rt, WpfBrush color)
    {
        var p = SubCv(tx, ty, rt);
        p.Children.Add(El(0, 0, 6, 8, color));
        p.Children.Add(El(0, 6, 7, 4, color));
        return p;
    }

    // 푸들 다리: 허벅지 + 솜뭉치 + 어두운 발끝
    static Canvas LegPoodle(double tx, double ty, RotateTransform rt,
                            WpfBrush legColor, WpfBrush puffColor, WpfBrush pawColor)
    {
        var p = SubCv(tx, ty, rt);
        p.Children.Add(El(0, 6, 4.5, 12, legColor));
        p.Children.Add(Cir(0, 14, 6, puffColor));
        p.Children.Add(El(0, 19, 5, 3, pawColor));
        return p;
    }

    // 눈 (검은 동공 + 큰 흰빛 + 작은 흰빛). HTML drawEye 비-깜빡임 케이스.
    void AddEye(Canvas parent, double cx, double cy, double r)
    {
        var dark  = B(_palette.Dark);
        var white = B(WpfColor.FromRgb(0xFF, 0xFF, 0xFF));
        parent.Children.Add(Cir(cx, cy, r, dark));
        parent.Children.Add(Cir(cx - r * 0.3,  cy - r * 0.3,  r * 0.35, white));
        parent.Children.Add(Cir(cx + r * 0.35, cy + r * 0.2,  r * 0.15, white));
    }
}
