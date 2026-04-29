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

    public void Start(int count = 3)
    {
        _canvas.SizeChanged += OnSizeChanged;
        for (int i = 0; i < count; i++)
            _dogs.Add(new DogActor(_canvas, Rng, i));
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
//  DogActor — 개별 강아지 캐릭터 (상태 머신 + 스프라이트)
// ═══════════════════════════════════════════════════════════════
internal sealed class DogActor
{
    // ── 크기 ────────────────────────────────────────────────────
    const double W = 64, H = 44;
    const double Speed = 2.8;

    // ── 행동 상태 ────────────────────────────────────────────────
    enum State { Running, Sitting, Hiding, Sniffing, Zoomies, Bark }

    private readonly Canvas _parent;
    private readonly Random _rng;
    private readonly Canvas _sprite;

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

    public DogActor(Canvas parent, Random rng, int index)
    {
        _parent = parent;
        _rng    = rng;
        _t      = index * 1.4;   // 위상 오프셋

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
    //  스프라이트 생성 — 64×44 캔버스에 코기 옆면 그리기
    //  Z-순서: 꼬리 → 뒷다리 → 몸통 → 배 → 목줄 → 앞다리 → 귀 → 머리
    // ════════════════════════════════════════════════════════════
    Canvas BuildSprite()
    {
        var c = new Canvas { Width = W, Height = H, IsHitTestVisible = false };
        c.RenderTransformOrigin = new WpfPoint(0.5, 0.5);
        c.RenderTransform = _flip;

        var colBody   = Col(0xD4, 0xA5, 0x6A);
        var colAccent = Col(0xB0, 0x78, 0x40);
        var colCream  = Col(0xFA, 0xEB, 0xD7);
        var colDark   = Col(0x2C, 0x1A, 0x0E);
        var colBlush  = ColA(0x55, 0xE8, 0x80, 0x60);
        var colPink   = Col(0xE8, 0x78, 0x8A);
        var colCollar = Col(0x3B, 0x82, 0xF6);

        // ── 꼬리 (코기 스텁 — 잎사귀 모양 곡선) ─────────────────
        var tail = new Path
        {
            Data = Geometry.Parse("M 5,26 Q 0,19 4,14 Q 9,9 13,15 Q 11,22 5,26 Z"),
            Fill = colAccent,
            RenderTransformOrigin = new WpfPoint(0.8, 0.85),
            RenderTransform = _tailWag
        };

        // ── 뒷다리 (발바닥 포함) ──────────────────────────────────
        var bLeg1 = LegWithPaw(colAccent, _legBL); SetPos(bLeg1,  8, 25);
        var bLeg2 = LegWithPaw(colAccent, _legBR); SetPos(bLeg2, 16, 25);

        // ── 몸통 ─────────────────────────────────────────────────
        var body = new Ellipse
        {
            Width = 40, Height = 22, Fill = colBody,
            RenderTransformOrigin = new WpfPoint(0.5, 0.5),
            RenderTransform = _bodyBob
        };
        SetPos(body, 5, 13);

        // ── 배/가슴 크림 패치 ─────────────────────────────────────
        var belly = Oval(16, 10, 20, 18, colCream);

        // ── 목줄 ─────────────────────────────────────────────────
        var collar = Oval(10, 5.5, 34, 17, colCollar);

        // ── 앞다리 (발바닥 포함) ──────────────────────────────────
        var fLeg1 = LegWithPaw(colAccent, _legFL); SetPos(fLeg1, 37, 25);
        var fLeg2 = LegWithPaw(colAccent, _legFR); SetPos(fLeg2, 45, 25);

        // ── 귀 캔버스 (큰 쫑긋 코기 귀, 귀 펄럭 변환 포함) ────────
        var earCv = new Canvas { Width = 22, Height = 20, IsHitTestVisible = false };
        earCv.RenderTransformOrigin = new WpfPoint(0.5, 1.0);
        earCv.RenderTransform = _earFlop;
        var earOuter = new Path { Data = Geometry.Parse("M 1,18 L 10,-2 L 20,14 Z"), Fill = colAccent };
        var earInner = new Path { Data = Geometry.Parse("M 4,16 L 10,2  L 17,13 Z"), Fill = Col(0xE8, 0xA0, 0x72) };
        earCv.Children.Add(earOuter);
        earCv.Children.Add(earInner);
        SetPos(earCv, 40, 1);

        // ── 머리 캔버스 (얼굴 전체가 함께 갸웃) ──────────────────
        var headCv = new Canvas { Width = 26, Height = 30, IsHitTestVisible = false };
        headCv.RenderTransformOrigin = new WpfPoint(0.15, 0.65);
        headCv.RenderTransform = _headTilt;

        // 머리
        var head = new Ellipse { Width = 24, Height = 20, Fill = colBody };
        SetPos(head, 0, 0);

        // 주둥이
        var snout = new Ellipse { Width = 14, Height = 10, Fill = colCream };
        SetPos(snout, 9, 11);

        // 볼 블러시
        var blush = Oval(9, 5, 1, 12, colBlush);

        // 눈 흰자
        var eyeW = new Ellipse { Width = 6.5, Height = 6, Fill = WpfBrushes.White };
        SetPos(eyeW, 2.5, 3.5);

        // 눈동자
        var pupil = new Ellipse { Width = 4.5, Height = 4.5, Fill = colDark };
        SetPos(pupil, 3.5, 4.5);

        // 눈 빛 반사
        var shine = new Ellipse { Width = 1.8, Height = 1.8, Fill = WpfBrushes.White };
        SetPos(shine, 5.5, 4.5);

        // 눈썹 (표정 변화)
        var eyebrow = new Path
        {
            Data = Geometry.Parse("M 2,1.5 Q 6,0 10,1.5"),
            Stroke = new SolidColorBrush(WpfColor.FromRgb(0x5C, 0x35, 0x18)),
            StrokeThickness = 1.4,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            RenderTransformOrigin = new WpfPoint(0.5, 1.0),
            RenderTransform = _eyebrow
        };
        SetPos(eyebrow, 3, 2);

        // 수염 점 (whisker dots)
        var wDot1 = Oval(1.5, 1.5,  9.5, 16.0, Col(0x8B, 0x5C, 0x28));
        var wDot2 = Oval(1.5, 1.5,  9.5, 18.0, Col(0x8B, 0x5C, 0x28));
        var wDot3 = Oval(1.5, 1.5, 13.0, 15.0, Col(0x8B, 0x5C, 0x28));
        var wDot4 = Oval(1.5, 1.5, 13.0, 17.5, Col(0x8B, 0x5C, 0x28));

        // 코
        var nose = new Ellipse { Width = 5.5, Height = 4.5, Fill = colDark };
        nose.RenderTransform = _noseSniff;
        SetPos(nose, 16.5, 14.5);

        // 코 빛 반사
        var noseShine = new Ellipse { Width = 2, Height = 1.5, Fill = ColA(0x88, 0xFF, 0xFF, 0xFF) };
        SetPos(noseShine, 16.5, 14.5);

        // 미소
        var smile = new Path
        {
            Data = Geometry.Parse("M 8,19 Q 14,24 20,19"),
            Stroke = colDark, StrokeThickness = 1.3,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
        };

        // 혀 (달릴 때 보임)
        var tongue = new Path
        {
            Data = Geometry.Parse("M 9,20 Q 14,27 19,20 Q 14,18 9,20 Z"),
            Fill = colPink,
            Visibility = Visibility.Collapsed,
            RenderTransformOrigin = new WpfPoint(0.5, 0),
            RenderTransform = _tongueWag
        };
        _tongue = tongue;

        foreach (UIElement el in new UIElement[]
            { head, snout, blush, eyeW, pupil, shine, eyebrow,
              wDot1, wDot2, wDot3, wDot4, nose, noseShine, smile, tongue })
            headCv.Children.Add(el);

        SetPos(headCv, 36, 3);

        // ── Z-순서대로 조립 ──────────────────────────────────────
        foreach (UIElement el in new UIElement[]
            { tail, bLeg1, bLeg2, body, belly, collar, fLeg1, fLeg2, earCv, headCv })
            c.Children.Add(el);

        return c;
    }

    // ── 정적 헬퍼 ────────────────────────────────────────────────

    // 다리 + 발바닥 세트 (회전 변환 포함, 발이 다리와 함께 움직임)
    static Canvas LegWithPaw(WpfBrush fill, RotateTransform rt)
    {
        var lc = new Canvas { Width = 8, Height = 18, IsHitTestVisible = false };
        lc.RenderTransformOrigin = new WpfPoint(0.5, 0);
        lc.RenderTransform = rt;
        var leg = new System.Windows.Shapes.Rectangle { Width = 5, Height = 13, Fill = fill, RadiusX = 2, RadiusY = 2 };
        var paw = new Ellipse   { Width = 8, Height = 4.5, Fill = fill };
        Canvas.SetLeft(leg, 1.5); Canvas.SetTop(leg, 0);
        Canvas.SetLeft(paw, 0);   Canvas.SetTop(paw, 12);
        lc.Children.Add(leg);
        lc.Children.Add(paw);
        return lc;
    }

    static Ellipse Oval(double w, double h, double x, double y, WpfBrush fill)
    {
        var e = new Ellipse { Width = w, Height = h, Fill = fill };
        SetPos(e, x, y); return e;
    }

    static void SetPos(UIElement e, double x, double y)
    { Canvas.SetLeft(e, x); Canvas.SetTop(e, y); }

    static SolidColorBrush Col(byte r, byte g, byte b)
        => new(WpfColor.FromRgb(r, g, b));

    static SolidColorBrush ColA(byte a, byte r, byte g, byte b)
        => new(WpfColor.FromArgb(a, r, g, b));
}
