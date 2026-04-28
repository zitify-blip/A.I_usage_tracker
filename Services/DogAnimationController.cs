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
    private readonly Canvas _canvas;
    private readonly UIElement _searchRoot;
    private readonly List<DogActor> _dogs = [];
    private readonly DispatcherTimer _animTimer;
    private readonly DispatcherTimer _obstacleTimer;
    private List<Rect> _obstacles = [];
    private static readonly Random Rng = new();

    public DogAnimationController(Canvas canvas, UIElement searchRoot)
    {
        _canvas = canvas;
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
    const double W = 58, H = 38;
    const double Speed = 2.8;

    // ── 행동 상태 ────────────────────────────────────────────────
    enum State { Running, Sitting, Hiding, Sniffing, Zoomies }

    private readonly Canvas _parent;
    private readonly Random _rng;
    private readonly Canvas _sprite;

    double _x, _y, _vx, _vy;
    bool _facingRight = true;
    State _state = State.Running;
    double _stateTimer;
    Rect _hideTarget;
    double _t;   // 누적 시간(초) — 삼각함수 애니메이션 위상

    // ── 스프라이트 부위별 변환 ────────────────────────────────────
    readonly ScaleTransform _flip     = new(1, 1);
    readonly RotateTransform _bodyBob = new();
    readonly RotateTransform _tailWag = new();
    readonly RotateTransform _legFL   = new();   // 앞왼발
    readonly RotateTransform _legFR   = new();   // 앞오른발
    readonly RotateTransform _legBL   = new();   // 뒷왼발
    readonly RotateTransform _legBR   = new();   // 뒷오른발

    // ── 코 상하 이동 (냄새 맡기) ─────────────────────────────────
    readonly TranslateTransform _noseSniff = new();

    public DogActor(Canvas parent, Random rng, int index)
    {
        _parent = parent;
        _rng    = rng;
        _t      = index * 1.4;   // 위상 오프셋으로 3마리 동기화 방지

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
        Canvas.SetTop(_sprite, _y);
        parent.Children.Add(_sprite);
    }

    public void Remove() => _parent.Children.Remove(_sprite);

    // ── 매 프레임 호출 ────────────────────────────────────────────
    public void Update(double cw, double ch, List<Rect> obstacles)
    {
        _t           += 0.033;
        _stateTimer  -= 0.033;

        switch (_state)
        {
            case State.Running:  DoRunning(cw, ch, obstacles);  break;
            case State.Sitting:  DoSitting();                    break;
            case State.Hiding:   DoHiding(cw, ch);               break;
            case State.Sniffing: DoSniffing();                   break;
            case State.Zoomies:  DoZoomies(cw, ch);              break;
        }

        Canvas.SetLeft(_sprite, _x);
        Canvas.SetTop(_sprite, _y);
        _flip.ScaleX = _facingRight ? 1 : -1;
    }

    // ────────────────────────────────────────────────────────────
    //  Running — 일반 달리기
    // ────────────────────────────────────────────────────────────
    void DoRunning(double cw, double ch, List<Rect> obstacles)
    {
        AnimateLegs(14, 28);
        _bodyBob.Angle = Math.Sin(_t * 14) * 2.5;
        _tailWag.Angle = Math.Sin(_t * 16) * 20;
        _noseSniff.Y   = 0;

        _x += _vx; _y += _vy;
        UpdateFacing();
        Bounce(cw, ch);

        // 카드 안으로 들어가면 밀어냄
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
        switch (_rng.Next(12))
        {
            case < 3: TurnRandom(); _stateTimer = 2.5 + _rng.NextDouble() * 4; break;
            case < 6: Sit();  break;
            case < 8: Sniff(); break;
            case < 10 when obstacles.Count > 0: Hide(obstacles); break;
            case < 12: Zoom(); break;
            default:  TurnRandom(); _stateTimer = 3 + _rng.NextDouble() * 4; break;
        }
    }

    // ────────────────────────────────────────────────────────────
    //  Sitting — 앉아서 꼬리 흔들기
    // ────────────────────────────────────────────────────────────
    void DoSitting()
    {
        _legFL.Angle  =  35; _legFR.Angle  =  35;
        _legBL.Angle  = -25; _legBR.Angle  = -25;
        _bodyBob.Angle = Math.Sin(_t * 3) * 1.5;
        _tailWag.Angle = Math.Sin(_t * 5) * 22;
        _noseSniff.Y   = 0;

        if (_stateTimer <= 0) Run();
    }

    // ────────────────────────────────────────────────────────────
    //  Hiding — 카드 아래쪽으로 뛰어가 숨기 (크롤)
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
            _tailWag.Angle = Math.Sin(_t * 20) * 28;
        }
        else
        {
            // 도착 → 납작하게 숨기 (다리 접기)
            _vx = 0; _vy = 0;
            _legFL.Angle = 50; _legFR.Angle = 50;
            _legBL.Angle =-35; _legBR.Angle =-35;
            _bodyBob.Angle = -8;
            _tailWag.Angle = Math.Sin(_t * 4) * 12;
        }

        if (_stateTimer <= 0) Run();
    }

    // ────────────────────────────────────────────────────────────
    //  Sniffing — 냄새 맡기
    // ────────────────────────────────────────────────────────────
    void DoSniffing()
    {
        _bodyBob.Angle  = Math.Sin(_t * 6) * 6;
        _noseSniff.Y    = Math.Sin(_t * 6) * 2;
        _tailWag.Angle  = Math.Sin(_t * 5) * 30;
        _legFL.Angle    =  12; _legFR.Angle  = -12;
        _legBL.Angle    =  -8; _legBR.Angle  =   8;

        if (_stateTimer <= 0) Run();
    }

    // ────────────────────────────────────────────────────────────
    //  Zoomies — 미친 듯이 빠르게 달리기
    // ────────────────────────────────────────────────────────────
    void DoZoomies(double cw, double ch)
    {
        AnimateLegs(22, 40);
        _bodyBob.Angle = Math.Sin(_t * 22) * 5;
        _tailWag.Angle = Math.Sin(_t * 25) * 35;

        _x += _vx * 2.2; _y += _vy * 2.2;
        UpdateFacing();
        Bounce(cw, ch);

        if (_stateTimer <= 0) { Sit(); }
    }

    // ── 헬퍼 ─────────────────────────────────────────────────────
    void AnimateLegs(double freq, double swing)
    {
        double s = Math.Sin(_t * freq) * swing;
        _legFL.Angle =  s; _legBR.Angle =  s;
        _legFR.Angle = -s; _legBL.Angle = -s;
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

    void Run()   { TurnRandom(); _state = State.Running;  _stateTimer = 3 + _rng.NextDouble() * 5; }
    void Sit()   { _vx = 0; _vy = 0; _state = State.Sitting;  _stateTimer = 1.5 + _rng.NextDouble() * 3; }
    void Sniff() { _vx = 0; _vy = 0; _state = State.Sniffing; _stateTimer = 1   + _rng.NextDouble() * 2; }
    void Zoom()  { TurnRandom(); _state = State.Zoomies;  _stateTimer = 1.5 + _rng.NextDouble() * 2; }
    void Hide(List<Rect> obs)
    {
        _hideTarget = obs[_rng.Next(obs.Count)];
        _state = State.Hiding;
        _stateTimer = 2 + _rng.NextDouble() * 3;
    }

    // ════════════════════════════════════════════════════════════
    //  스프라이트 생성 — 58×38 캔버스 안에 코기 옆면 그리기
    // ════════════════════════════════════════════════════════════
    Canvas BuildSprite()
    {
        var c = new Canvas { Width = W, Height = H, IsHitTestVisible = false };
        c.RenderTransformOrigin = new WpfPoint(0.5, 0.5);
        c.RenderTransform = _flip;

        // 색상
        var colBody   = Col(0xD4, 0xA5, 0x6A);
        var colAccent = Col(0xB0, 0x78, 0x40);
        var colSnout  = Col(0xFA, 0xEB, 0xD7);
        var colDark   = Col(0x2C, 0x1A, 0x0E);
        var colBlush  = ColA(0x44, 0xE8, 0x90, 0x60);

        // ── 꼬리 (코기 스터브) ──────────────────────────────────
        var tail = Oval(9, 7, 1, 16, colAccent);
        tail.RenderTransformOrigin = new WpfPoint(1.0, 0.5);
        tail.RenderTransform = _tailWag;

        // ── 뒷다리 (몸통 앞에 그려서 몸통 뒤로 보임) ───────────
        var bLeg1 = Leg(colAccent, _legBL); SetPos(bLeg1, 10, 25);
        var bLeg2 = Leg(colAccent, _legBR); SetPos(bLeg2, 17, 25);

        // ── 몸통 ────────────────────────────────────────────────
        var body = new Ellipse
        {
            Width = 36, Height = 18, Fill = colBody,
            RenderTransformOrigin = new WpfPoint(0.5, 0.5),
            RenderTransform = _bodyBob
        };
        SetPos(body, 6, 13);

        // ── 앞다리 ───────────────────────────────────────────────
        var fLeg1 = Leg(colAccent, _legFL); SetPos(fLeg1, 33, 25);
        var fLeg2 = Leg(colAccent, _legFR); SetPos(fLeg2, 39, 25);

        // ── 귀 (뾰족한 코기 귀) ──────────────────────────────────
        var ear = new Path { Data = Geometry.Parse("M 42,5 L 48,0 L 52,8 Z"), Fill = colAccent };

        // ── 머리 ────────────────────────────────────────────────
        var head = Oval(20, 16, 33, 3, colBody);

        // ── 귀 안쪽 (밝은 연어색) ─────────────────────────────────
        var earIn = new Path { Data = Geometry.Parse("M 44,5 L 48,2 L 50,7 Z"), Fill = Col(0xE0, 0x95, 0x6A) };

        // ── 주둥이 ───────────────────────────────────────────────
        var snout = Oval(12, 8, 40, 13, colSnout);

        // ── 볼 터치 ──────────────────────────────────────────────
        var blush = Oval(8, 5, 36, 14, colBlush);

        // ── 눈 흰자 ───────────────────────────────────────────────
        var eyeW = Oval(5, 5, 37, 5, WpfBrushes.White);

        // ── 눈동자 ───────────────────────────────────────────────
        var pupil = Oval(3.5, 3.5, 38.5, 6, colDark);

        // ── 눈 빛 반사 ────────────────────────────────────────────
        var shine = Oval(1.5, 1.5, 40, 7, WpfBrushes.White);

        // ── 코 ──────────────────────────────────────────────────
        var nose = new Ellipse { Width = 5, Height = 4, Fill = colDark };
        var noseTg = new TransformGroup();
        noseTg.Children.Add(_noseSniff);
        nose.RenderTransform = noseTg;
        SetPos(nose, 47, 16);

        // ── 미소 (선) ────────────────────────────────────────────
        var smile = new Path
        {
            Data = Geometry.Parse("M 42,21 Q 50,26 58,21"),
            Stroke = colDark, StrokeThickness = 1.3,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
        };

        // Z-순서대로 추가 (뒤→앞)
        foreach (UIElement e in new UIElement[]
            { tail, bLeg1, bLeg2, body, fLeg1, fLeg2, ear, head, earIn, snout, blush, eyeW, pupil, shine, nose, smile })
            c.Children.Add(e);

        return c;
    }

    // ── 정적 헬퍼 ────────────────────────────────────────────────
    static Ellipse Oval(double w, double h, double x, double y, WpfBrush fill)
    {
        var e = new Ellipse { Width = w, Height = h, Fill = fill };
        SetPos(e, x, y); return e;
    }

    static System.Windows.Shapes.Rectangle Leg(WpfBrush fill, RotateTransform rt) => new()
    {
        Width = 5, Height = 13, Fill = fill, RadiusX = 2, RadiusY = 2,
        RenderTransformOrigin = new WpfPoint(0.5, 0),
        RenderTransform = rt
    };

    static void SetPos(UIElement e, double x, double y)
    { Canvas.SetLeft(e, x); Canvas.SetTop(e, y); }

    static SolidColorBrush Col(byte r, byte g, byte b)
        => new(WpfColor.FromRgb(r, g, b));

    static SolidColorBrush ColA(byte a, byte r, byte g, byte b)
        => new(WpfColor.FromArgb(a, r, g, b));
}
