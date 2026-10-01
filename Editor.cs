// Money Shot — 편집창
//
// 구조: 바탕 그림 한 장(WriteableBitmap) 위에 주석 객체(벡터)를 얹는 두 겹이다.
//   · 도형·화살표·텍스트·번호는 벡터로 남아 나중에 옮기고 지울 수 있다.
//   · 자르기·모자이크·지우개·누끼는 바탕 픽셀을 직접 고친다. 되돌리기로만 복구된다.
// 저장할 때 두 겹을 합쳐 한 장으로 만든다.
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using D = System.Drawing;

namespace MoneyShot
{
    // 화살표 촉이 어디에 붙는가.
    public enum ArrowHead { End, Start, Both }

    // 선 종류.
    public enum LineDash { Solid, Dash, Dot, DashDot }

    public enum Tool
    {
        Select, Marquee, Rect, Ellipse, Arrow, Line, Pen, Highlight, Text, Counter,
        Mosaic, Blur, MagicErase, BgErase, Erase, Restore, Picker, Transform, Heal, Clone, Liquify, Brush, Gradient
    }

    // 주석 하나.
    public class Ann
    {
        public Tool Kind;
        public Point A, B;
        public List<Point> Pts;
        public string Text = "";
        public double FontSize = 20;
        public int Number;
        public Color Color;
        public double Thickness = 3;
        public bool Filled;
        public ArrowHead Head = ArrowHead.End;
        public LineDash Dash = LineDash.Solid;

        // 글자 서식
        public string Font = "맑은 고딕";
        public bool Bold = true;
        public bool Italic;
        public bool Underline;
        public bool Outline = true;      // 어수선한 화면 위에서도 읽히도록
        public bool BoxBg;

        public double Angle;             // 도 단위. 개체 가운데를 축으로 돈다
        public bool Shadow;

        public UIElement Visual;
        public System.Windows.Controls.TextBox Edit;   // 글자를 고칠 때 쓰는 상자

        public Ann Copy()
        {
            var c = (Ann)MemberwiseClone();
            if (Pts != null) c.Pts = new List<Point>(Pts);
            c.Visual = null;
            c.Edit = null;
            return c;
        }

        public Rect Bounds
        {
            get
            {
                if (Pts != null && Pts.Count > 0)
                {
                    double x0 = Pts[0].X, y0 = Pts[0].Y, x1 = x0, y1 = y0;
                    foreach (var p in Pts)
                    {
                        if (p.X < x0) x0 = p.X; if (p.X > x1) x1 = p.X;
                        if (p.Y < y0) y0 = p.Y; if (p.Y > y1) y1 = p.Y;
                    }
                    double pad = Thickness;
                    return new Rect(x0 - pad, y0 - pad, x1 - x0 + pad * 2, y1 - y0 + pad * 2);
                }
                if (Kind == Tool.Counter)
                {
                    double r = FontSize * 0.95;
                    return new Rect(A.X - r, A.Y - r, r * 2, r * 2);
                }
                if (Kind == Tool.Text)
                {
                    var el = Visual as FrameworkElement;
                    double w = el != null && el.ActualWidth > 4 ? el.ActualWidth : FontSize * 6;
                    double h = el != null && el.ActualHeight > 4 ? el.ActualHeight : FontSize * 1.5;
                    return new Rect(A.X, A.Y, w, h);
                }
                return new Rect(Math.Min(A.X, B.X), Math.Min(A.Y, B.Y),
                                Math.Abs(B.X - A.X), Math.Abs(B.Y - A.Y));
            }
        }
    }

    class Snapshot
    {
        public DocSnap Doc;
        public List<Ann> Anns;
        public int Counter;
    }

    public class EditorWindow : Window
    {
        // ---- 상태 ----
        Doc doc;                         // 레이어 묶음. 화면에 보이는 건 합쳐진 결과(doc.Comp)다.
        WriteableBitmap baseWb;
        readonly List<Ann> anns = new List<Ann>();
        readonly Stack<Snapshot> undo = new Stack<Snapshot>();
        readonly Stack<Snapshot> redo = new Stack<Snapshot>();
        int counterNext = 1;
        string filePath;
        bool dirty;

        Tool tool = Tool.Rect;
        Color ink = Theme.H("#F87171");
        double thickness = 3;
        int brushSize = 28;
        int tolerance = 32;
        int hardness = 70;
        int mosaicCell = 12;
        int blurStrength = 6;
        bool contiguous = true;

        // 스팟 힐링·도장
        HealMode healMode = HealMode.ContentAware;
        bool cloneAligned = true;
        Point? cloneSrc;                 // Alt+클릭한 자리 (문서 좌표)
        Vector? cloneOffset;             // 정렬 모드에서 첫 획이 정한 거리. 다음 획도 이 거리로 가져온다
        Vector cloneStroke;              // 이번 획이 쓰는 거리
        Canvas32 strokeSnap;             // 획을 시작할 때의 픽셀. 칠한 것을 다시 퍼오지 않게 여기서 가져온다
        byte[] strokeCov;                // 이번 획이 칠한 정도 (레이어 크기)
        List<Point> healPts;
        Polyline healTrail;
        Shape cloneMark;

        // 유동화
        int warpMode;                    // 0 밀기 · 1 문지르기 · 2 흐리게 칠하기
        int warpStrength = 50;
        int blurBrushRadius = 6;
        WarpStroke warp;

        // 붓·그라디언트
        int paintOpacity = 100;
        int gradShape;                   // 0 직선 · 1 원형
        int gradEnd;                     // 0 투명으로 · 1 흰색으로 · 2 검정으로
        bool gradReverse;
        Point gradStart;
        Canvas32 gradSnap;
        byte[] gradCover;
        Line gradLine;
        Canvas32 blurSnap;               // 흐리게 칠하기: 획을 시작할 때 레이어를 통째로 흐려 둔 것
        ArrowHead arrowHead = ArrowHead.End;
        LineDash lineDash = LineDash.Solid;

        // ---- 시각 요소 ----
        Grid stackHost;
        Image baseImg;
        Canvas inkLayer, uiLayer;
        ScrollViewer viewport;
        ScaleTransform zoom = new ScaleTransform(1, 1);
        StackPanel optionsBar;
        TextBlock statusText, zoomText;
        Border toolRail;
        readonly Dictionary<Tool, Border> toolButtons = new Dictionary<Tool, Border>();
        readonly Dictionary<Tool, FrameworkElement> toolArt = new Dictionary<Tool, FrameworkElement>();
        readonly Dictionary<Tool, TextBlock> toolName = new Dictionary<Tool, TextBlock>();
        readonly Dictionary<Tool, TextBlock> toolKey = new Dictionary<Tool, TextBlock>();

        // ---- 조작 중 ----
        bool drawing;
        Point startPt, lastPt;

        // 자유 변형. 원본 레이어 자리(xfO*)를 기준으로 한 변형값만 들고 있다가,
        // 확정할 때 한 번만 픽셀을 다시 만든다.
        bool xfOn;
        int xfLayer = -1;
        double xfOx, xfOy, xfW, xfH;
        double xfTx, xfTy, xfSx = 1, xfSy = 1, xfAngle, xfSkewX, xfSkewY;
        Image xfImage;
        System.Windows.Shapes.Polygon xfFrame;
        int xfGrab = -1;
        Point xfAnchorDoc;
        double xfAngleStart;
        Ann current;
        Ann selected;
        int grabHandle = -1;
        Selection selection;
        SelShape selShape = SelShape.Rect;
        List<Point> lassoPts;
        EdgeMap edges;
        Path selDim, selAnts;
        Rectangle marquee;
        Ellipse brushRing;

        bool glass;
        double railW;
        ColumnDefinition railCol, panelCol;
        FrameworkElement panelSplit;
        Border layerPanel;
        StackPanel layerList;
        Slider opacitySlider;
        TextBlock blendLabel;
        bool suppressOpacity;
        static readonly List<EditorWindow> open_ = new List<EditorWindow>();

        // 창 크기는 DIP가 아니라 물리 픽셀로 다룬다. 모니터마다 배율이 다르면
        // DIP로 지정한 크기가 화면마다 달라 보여서, 어디서 열리든 같은 크기가 되도록 맞춘다.
        void PlaceWindow(IntPtr hwnd)
        {
            var s = Settings.Current;
            POINT c; Native.GetCursorPos(out c);
            var mon = Native.MonitorFromPoint(c, Native.MONITOR_DEFAULTTONEAREST);
            var mi = new MONITORINFOEX();
            mi.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(MONITORINFOEX));
            RECT work = Native.GetMonitorInfo(mon, ref mi) ? mi.rcWork : Native.GetVirtualScreen();

            int w = (int)Math.Min(s.editorW, work.W * 0.94);
            int h = (int)Math.Min(s.editorH, work.H * 0.94);
            int x = work.Left + (work.W - w) / 2;
            int y = work.Top + (work.H - h) / 2;
            Native.SetWindowPos(hwnd, IntPtr.Zero, x, y, w, h,
                                Native.SWP_NOZORDER | Native.SWP_SHOWWINDOW);
            RECT got;
            if (Native.GetWindowRect(hwnd, out got))
                Log.W("편집창 배치 요청 " + w + "x" + h + " → 실제 " + got.W + "x" + got.H);
        }

        public static void Open(D.Bitmap bmp, string path)
        {
            var w = new EditorWindow(bmp, path);
            open_.Add(w);
            w.Show();
            w.Activate();
        }

        // 레이어가 여러 장인 문서(PSD)를 그대로 연다.
        public static void OpenDoc(Doc d, string path)
        {
            var w = new EditorWindow(d, path);
            open_.Add(w);
            w.Show();
            w.Activate();
        }

        // PSD를 새 편집창으로 연다. 못 가져온 것이 있으면 알려준다. 열었으면 true.
        public static bool OpenPsd(string path, Action<string> report)
        {
            try
            {
                var notes = new List<string>();
                var d = Psd.Read(path, notes);
                OpenDoc(d, path);
                var w = open_[open_.Count - 1];
                w.Dispatcher.BeginInvoke(new Action(delegate
                {
                    w.Status(L.F("{0} — 레이어 {1}장", "{0} — {1} layers", System.IO.Path.GetFileName(path), d.Layers.Count) +
                             (notes.Count > 0 ? "   · " + string.Join(" · ", notes) : ""));
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                return true;
            }
            catch (Exception ex)
            {
                if (report != null) report(L.T("PSD를 열지 못했다 — ", "Couldn't open the PSD — ") + ex.Message);
                return false;
            }
        }

        static Doc DocFrom(D.Bitmap bmp)
        {
            var first = Canvas32.From(bmp);
            bmp.Dispose();
            return new Doc(first, first.W, first.H);
        }

        EditorWindow(D.Bitmap bmp, string path) : this(DocFrom(bmp), path) { }

        EditorWindow(Doc d, string path)
        {
            filePath = path;
            doc = d;

            var s = Settings.Current;
            brushSize = s.brushSize; tolerance = s.tolerance;

            railW = s.railWidth;

            Title = L.T("Money Shot — 편집", "Money Shot — Editor");
            WindowStyle = WindowStyle.None;
            AllowsTransparency = false;
            ResizeMode = ResizeMode.CanResize;
            MinWidth = 620; MinHeight = 460;
            WindowStartupLocation = WindowStartupLocation.Manual;
            // Width/Height는 건드리지 않는다. 값을 넣어두면 WPF가 첫 레이아웃에서
            // 그 값으로 창을 다시 잡아, 아래에서 물리 픽셀로 앉힌 크기를 덮어쓴다.
            Left = -32000; Top = -32000;
            Background = Theme.BrBg;
            FontFamily = Theme.UI;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            glass = Glass.Apply(this, true);
            var hwnd = new WindowInteropHelper(this).Handle;
            var src = HwndSource.FromHwnd(hwnd);
            if (src != null) src.AddHook(ResizeHook);
            Build();
            PlaceWindow(hwnd);
            // 레이아웃이 한 번 돌고 난 뒤 한 번 더 앉힌다 (WPF가 되돌리는 경우 대비).
            Dispatcher.BeginInvoke(new Action(delegate { PlaceWindow(hwnd); }),
                                   System.Windows.Threading.DispatcherPriority.Loaded);
            RefreshBase(null);
            Dispatcher.BeginInvoke(new Action(FitToWindow), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        // ================= 화면 구성 =================

        void Build()
        {
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // 제목줄
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // 옵션줄
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // 상태줄

            var title = BuildTitleBar();
            Grid.SetRow(title, 0); root.Children.Add(title);

            optionsBar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(14, 0, 14, 10),
                VerticalAlignment = VerticalAlignment.Center
            };
            var optWrap = new Border
            {
                Child = optionsBar,
                MinHeight = 44
            };
            Grid.SetRow(optWrap, 1); root.Children.Add(optWrap);

            // 가운데: 왼쪽 도구 레일 + 캔버스
            var mid = new Grid();
            railCol = new ColumnDefinition { Width = new GridLength(railW), MinWidth = 52, MaxWidth = 320 };
            mid.ColumnDefinitions.Add(railCol);
            mid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            mid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            toolRail = BuildToolRail();
            toolRail.SizeChanged += delegate { SyncRailWidth(); };
            Grid.SetColumn(toolRail, 0); mid.Children.Add(toolRail);

            var split = BuildSplitter();
            Grid.SetColumn(split, 1); mid.Children.Add(split);

            viewport = BuildCanvas();
            var canvasHost = BuildRulers(viewport);
            Grid.SetColumn(canvasHost, 2); mid.Children.Add(canvasHost);

            mid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            panelCol = new ColumnDefinition
            {
                Width = new GridLength(Settings.Current.LayerPanelOn ? Settings.Current.layerPanelWidth : 0),
                MinWidth = 0, MaxWidth = 420
            };
            mid.ColumnDefinitions.Add(panelCol);

            panelSplit = BuildPanelSplitter();
            Grid.SetColumn(panelSplit, 3); mid.Children.Add(panelSplit);

            layerPanel = BuildLayerPanel();
            Grid.SetColumn(layerPanel, 4); mid.Children.Add(layerPanel);
            SyncPanelVisible();

            Grid.SetRow(mid, 2); root.Children.Add(mid);

            var status = BuildStatusBar();
            Grid.SetRow(status, 3); root.Children.Add(status);

            // 아크릴은 뒤에 뭐가 있느냐에 따라 밝기가 널뛴다. 어두운 막을 한 겹 깔아
            // 글자와 아이콘 대비를 어떤 배경에서도 일정하게 만든다.
            root.Background = glass ? Theme.Alpha(Theme.Bg, 0x9E) : Theme.BrBg;

            Content = root;
            SelectTool(Tool.Rect);
            Dispatcher.BeginInvoke(new Action(SyncRailWidth),
                                   System.Windows.Threading.DispatcherPriority.Loaded);

            PreviewKeyDown += OnKey;
            SizeChanged += delegate
            {
                // 최대화된 크기를 기억하면 다음에 열 때 화면을 꽉 채워 버린다. 보통 상태만 기억한다.
                if (WindowState != WindowState.Normal) return;
                RECT r;
                if (Native.GetWindowRect(new WindowInteropHelper(this).Handle, out r) && r.W > 200)
                {
                    Settings.Current.editorW = r.W;
                    Settings.Current.editorH = r.H;
                }
            };
            StateChanged += delegate { SyncMaxButton(); };
            AllowDrop = true;
            Drop += OnDrop;
        }

        Border BuildTitleBar()
        {
            var g = new Grid { Height = 48, Background = Brushes.Transparent };

            var left = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(16, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            left.Children.Add(Theme.AppMark(18));
            left.Children.Add(new TextBlock
            {
                Text = "Money Shot",
                Foreground = Theme.BrText, FontSize = 14, FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(9, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center
            });
            g.Children.Add(left);

            var right = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            right.Children.Add(TitleBtn("undo", L.T("되돌리기", "Undo"), "Ctrl+Z", delegate { Undo(); }));
            right.Children.Add(TitleBtn("redo", L.T("다시하기", "Redo"), "Ctrl+Y", delegate { Redo(); }));
            right.Children.Add(Gap(8));
            right.Children.Add(TitleBtn("layers", L.T("레이어", "Layers"), L.T("패널 보이기/숨기기", "Show/hide panel"), delegate
            {
                Settings.Current.showLayerPanel = !Settings.Current.LayerPanelOn;
                Settings.Current.Save();
                SyncPanelVisible();
            }));
            right.Children.Add(TitleBtn("more", L.T("더보기", "More"), L.T("회전·크기·효과·누끼", "Rotate · Size · Effects · Cutout"), delegate { ShowMoreMenu(); }));
            right.Children.Add(Gap(12));
            right.Children.Add(Action_(L.T("복사", "Copy"), false, delegate { CopyOut(); }));
            right.Children.Add(Gap(6));
            right.Children.Add(Action_(L.T("저장", "Save"), true, delegate { SaveOut(); }));
            right.Children.Add(Gap(10));

            right.Children.Add(WinBtn("minimize", L.T("최소화", "Minimize"), false,
                delegate { WindowState = WindowState.Minimized; }));
            maxBtn = WinBtn("maximize", L.T("최대화", "Maximize"), false, delegate { ToggleMaximize(); });
            right.Children.Add(maxBtn);
            right.Children.Add(WinBtn("close", L.T("닫기", "Close"), true, delegate { Close(); }));
            g.Children.Add(right);

            g.MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs a)
            {
                if (a.Handled) return;
                if (a.ClickCount == 2)
                    WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                else try { DragMove(); } catch { }
            };

            return new Border { Child = g };
        }

        static Border Gap(double w) { return new Border { Width = w }; }

        Border BuildToolRail()
        {
            var sp = new StackPanel { Margin = new Thickness(8, 2, 6, 8) };

            AddTool(sp, Tool.Select, "select", L.T("선택", "Select"), "V", L.T("도형을 눌러 옮기고 크기를 바꾼다", "Click a shape to move or resize it"));
            AddTool(sp, Tool.Marquee, "crop", L.T("영역 선택", "Marquee"), "C", L.T("영역을 골라 자르거나 지우거나 떼어내 변형한다", "Select an area to crop, erase, or lift and transform"));
            RailDivider(sp);
            AddTool(sp, Tool.Rect, "rect", L.T("사각형", "Rectangle"), "R", L.T("Shift를 누르면 정사각형", "Hold Shift for a square"));
            AddTool(sp, Tool.Ellipse, "ellipse", L.T("타원", "Ellipse"), "O", L.T("Shift를 누르면 정원", "Hold Shift for a circle"));
            AddTool(sp, Tool.Arrow, "arrow", L.T("화살표", "Arrow"), "A", L.T("Shift를 누르면 45도 단위", "Hold Shift to snap to 45°"));
            AddTool(sp, Tool.Line, "line", L.T("직선", "Line"), "L", L.T("Shift를 누르면 45도 단위", "Hold Shift to snap to 45°"));
            AddTool(sp, Tool.Pen, "pen", L.T("펜", "Pen"), "P", L.T("손으로 그린 선", "Freehand line"));
            AddTool(sp, Tool.Highlight, "highlight", L.T("형광펜", "Highlight"), "H", L.T("밑의 글자가 비치는 반투명 선", "Translucent stroke that lets text show through"));
            AddTool(sp, Tool.Text, "text", L.T("텍스트", "Text"), "T", L.T("누른 자리에 글자를 넣는다", "Click to add text"));
            AddTool(sp, Tool.Counter, "counter", L.T("번호", "Counter"), "N", L.T("누를 때마다 1, 2, 3… 순서대로", "Numbers 1, 2, 3… with each click"));
            RailDivider(sp);
            AddTool(sp, Tool.Mosaic, "mosaic", L.T("모자이크", "Mosaic"), "M", L.T("가릴 범위를 끌어라", "Drag over the area to hide"));
            AddTool(sp, Tool.Blur, "blur", L.T("흐리기", "Blur"), "B", L.T("가릴 범위를 끌어라", "Drag over the area to hide"));
            AddTool(sp, Tool.Heal, "heal", L.T("스팟 힐링", "Heal"), "J", L.T("잡티·글자를 칠하면 둘레에 맞춰 메운다", "Paint over spots or text to blend them into the surroundings"));
            AddTool(sp, Tool.Clone, "clone", L.T("도장", "Clone"), "S", L.T("Alt+클릭으로 가져올 곳을 찍고 칠한다", "Alt+click to set the source, then paint"));
            AddTool(sp, Tool.Liquify, "liquify", L.T("유동화", "Liquify"), "U", L.T("밀거나 문지르거나 흐리게 칠한다", "Push, smudge, or blur by painting"));
            AddTool(sp, Tool.Brush, "brush", L.T("붓", "Brush"), "K", L.T("레이어 픽셀에 직접 칠한다", "Paint directly on layer pixels"));
            AddTool(sp, Tool.Gradient, "gradient", L.T("그라디언트", "Gradient"), "D", L.T("끌어서 색이 번지듯 바뀌게 칠한다", "Drag to paint a color blend"));
            RailDivider(sp);
            AddTool(sp, Tool.MagicErase, "wand", L.T("마술봉", "Magic Wand"), "W", L.T("누른 자리의 색이 번져나가며 지워진다", "Click to erase similar colors spreading from that spot"));
            AddTool(sp, Tool.BgErase, "bgerase", L.T("배경 지우개", "Background Eraser"), "G", L.T("브러시 한가운데 색만 지운다. 경계에 물려 끌어라", "Erases only the color under the brush center. Drag along the edge"));
            AddTool(sp, Tool.Erase, "erase", L.T("지우개", "Eraser"), "E", L.T("색을 가리지 않고 지운다", "Erases everything it touches"));
            AddTool(sp, Tool.Restore, "restore", L.T("복구", "Restore"), "Q", L.T("지운 자리를 도로 칠해 되살린다", "Paint back erased areas"));
            AddTool(sp, Tool.Picker, "picker", L.T("스포이드", "Eyedropper"), "I", L.T("그림에서 색을 집는다", "Pick a color from the image"));
            RailDivider(sp);
            AddTool(sp, Tool.Transform, "transform", L.T("자유 변형", "Free Transform"), "Y", L.T("지금 레이어를 옮기고 늘이고 돌리고 기울인다", "Move, scale, rotate, or skew the current layer"));

            return new Border
            {
                Child = new ScrollViewer
                {
                    Content = sp,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
                }
            };
        }

        // 레일과 캔버스 사이의 손잡이. 좁히면 이름이 사라지고 아이콘만 남는다.
        FrameworkElement BuildSplitter()
        {
            var line = new Rectangle
            {
                Width = 1,
                Fill = Theme.Alpha(Colors.White, 0x12),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            var host = new Grid { Width = 7, Background = Brushes.Transparent, Cursor = Cursors.SizeWE };
            host.Children.Add(line);

            var gs = new GridSplitter
            {
                Width = 7,
                Background = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                ResizeBehavior = GridResizeBehavior.CurrentAndNext,
                ResizeDirection = GridResizeDirection.Columns,
                Cursor = Cursors.SizeWE,
                Focusable = false
            };
            gs.MouseEnter += delegate { line.Fill = Theme.BrAccent; line.Width = 2; };
            gs.MouseLeave += delegate { line.Fill = Theme.Alpha(Colors.White, 0x12); line.Width = 1; };
            gs.DragDelta += delegate { SyncRailWidth(); };
            gs.DragCompleted += delegate
            {
                SyncRailWidth();
                Settings.Current.railWidth = railCol.ActualWidth;
                Settings.Current.Save();
            };
            host.Children.Add(gs);
            return host;
        }

        // 폭에 따라 이름과 단축키 표시를 켜고 끈다.
        // 레이아웃이 끝나기 전에는 폭이 0이라, 실제 크기가 정해진 뒤에 다시 불러야 한다.
        void SyncRailWidth()
        {
            double w = toolRail != null && toolRail.ActualWidth > 1 ? toolRail.ActualWidth : railCol.Width.Value;
            bool wide = w >= 100;
            bool keys = w >= 128;
            foreach (var kv in toolName)
            {
                kv.Value.Visibility = wide ? Visibility.Visible : Visibility.Collapsed;
                toolKey[kv.Key].Visibility = keys ? Visibility.Visible : Visibility.Collapsed;
            }
            foreach (var kv in toolButtons)
                kv.Value.Padding = new Thickness(wide ? 9 : 0, 0, wide ? 8 : 0, 0);
            foreach (var kv in toolArt)
                kv.Value.Margin = new Thickness(0, 0, wide ? 9 : 0, 0);
        }

        static void RailDivider(StackPanel sp)
        {
            sp.Children.Add(new Rectangle
            {
                Height = 1, Margin = new Thickness(10, 6, 10, 6),
                Fill = Theme.Alpha(Colors.White, 0x14)
            });
        }

        void AddTool(StackPanel sp, Tool t, string icon, string label, string key, string tip)
        {
            var art = Icons.Boxed(icon, 18, Theme.BrDim);
            art.VerticalAlignment = VerticalAlignment.Center;
            art.HorizontalAlignment = HorizontalAlignment.Center;
            art.Margin = new Thickness(0, 0, 9, 0);

            var name = new TextBlock
            {
                Text = label,
                Foreground = Theme.BrDim,
                FontSize = 12.5,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var hint = new TextBlock
            {
                Text = key,
                Foreground = Theme.Alpha(Theme.Muted, 0x88),
                FontFamily = Theme.Mono,
                FontSize = 9.5,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 2, 0)
            };

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(art, 0); row.Children.Add(art);
            Grid.SetColumn(name, 1); row.Children.Add(name);
            Grid.SetColumn(hint, 2); row.Children.Add(hint);

            var b = new Border
            {
                Height = 30,
                Padding = new Thickness(9, 0, 8, 0),
                Margin = new Thickness(0, 1, 0, 1),
                CornerRadius = new CornerRadius(8),
                Background = Brushes.Transparent,
                Child = row,
                Cursor = Cursors.Hand,
                ToolTip = label + "   (" + key + ")\n" + tip
            };
            b.MouseLeftButtonUp += delegate { SelectTool(t); };
            b.MouseEnter += delegate { if (tool != t) b.Background = Theme.Alpha(Colors.White, 0x12); };
            b.MouseLeave += delegate { if (tool != t) b.Background = Brushes.Transparent; };
            toolButtons[t] = b;
            toolArt[t] = art;
            toolName[t] = name;
            toolKey[t] = hint;
            sp.Children.Add(b);
        }

        ScrollViewer BuildCanvas()
        {
            stackHost = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                LayoutTransform = zoom
            };

            var checker = new Rectangle { Fill = CheckerBrush(10) };
            stackHost.Children.Add(checker);

            baseImg = new Image { Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(baseImg, BitmapScalingMode.NearestNeighbor);
            stackHost.Children.Add(baseImg);

            inkLayer = new Canvas { Background = Brushes.Transparent, ClipToBounds = true };
            stackHost.Children.Add(inkLayer);

            uiLayer = new Canvas { Background = Brushes.Transparent, IsHitTestVisible = false };
            stackHost.Children.Add(uiLayer);

            var pad = new Border { Padding = new Thickness(28), Child = stackHost };

            var sv = new ScrollViewer
            {
                Content = pad,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = Theme.Alpha(Colors.Black, 0x40),
                Focusable = false
            };

            // 마우스는 그림 판이 아니라 보기 영역 전체에서 받는다.
            // 회전 손잡이처럼 그림 바깥에 있는 것도 잡아야 하기 때문이다.
            // 좌표는 그대로 그림 기준으로 읽으므로(음수도 나온다) 계산은 달라지지 않는다.
            sv.MouseLeftButtonDown += OnCanvasDown;
            sv.MouseMove += OnCanvasMove;
            sv.MouseLeftButtonUp += OnCanvasUp;
            sv.MouseRightButtonUp += OnCanvasRightUp;
            sv.MouseLeave += delegate { if (brushRing != null) brushRing.Visibility = Visibility.Collapsed; };
            sv.PreviewMouseWheel += OnWheel;
            return sv;
        }

        void SyncPanelVisible()
        {
            bool on = Settings.Current.LayerPanelOn;
            panelCol.Width = new GridLength(on ? Settings.Current.layerPanelWidth : 0);
            layerPanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            panelSplit.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (on) RefreshLayerPanel();
        }

        FrameworkElement BuildPanelSplitter()
        {
            var line = new Rectangle
            {
                Width = 1, Fill = Theme.Alpha(Colors.White, 0x12),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            var host = new Grid { Width = 7, Background = Brushes.Transparent, Cursor = Cursors.SizeWE };
            host.Children.Add(line);

            var gs = new GridSplitter
            {
                Width = 7,
                Background = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                ResizeDirection = GridResizeDirection.Columns,
                Cursor = Cursors.SizeWE,
                Focusable = false
            };
            gs.MouseEnter += delegate { line.Fill = Theme.BrAccent; line.Width = 2; };
            gs.MouseLeave += delegate { line.Fill = Theme.Alpha(Colors.White, 0x12); line.Width = 1; };
            gs.DragCompleted += delegate
            {
                if (panelCol.ActualWidth > 60)
                {
                    Settings.Current.layerPanelWidth = panelCol.ActualWidth;
                    Settings.Current.Save();
                }
            };
            host.Children.Add(gs);
            return host;
        }

        Border BuildLayerPanel()
        {
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // 머리말과 단추
            var head = new Grid { Margin = new Thickness(12, 10, 8, 8) };
            head.Children.Add(new TextBlock
            {
                Text = L.T("레이어", "Layers"), Foreground = Theme.BrMuted, FontSize = 11,
                FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center
            });
            var headBtns = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            headBtns.Children.Add(PanelBtn("plus", L.T("새 빈 레이어", "New empty layer"), delegate { AddEmptyLayer(); }));
            headBtns.Children.Add(PanelBtn("copy", L.T("복제", "Duplicate"), delegate { DuplicateLayer(); }));
            headBtns.Children.Add(PanelBtn("trash", L.T("삭제", "Delete"), delegate { DeleteLayer(); }));
            head.Children.Add(headBtns);
            Grid.SetRow(head, 0); root.Children.Add(head);

            layerList = new StackPanel { Margin = new Thickness(6, 0, 6, 6) };
            var sv = new ScrollViewer
            {
                Content = layerList,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            Grid.SetRow(sv, 1); root.Children.Add(sv);

            // 꼬리말 — 불투명도와 마스크
            var foot = new StackPanel { Margin = new Thickness(12, 8, 12, 12) };
            var opRow = new Grid();
            opRow.Children.Add(new TextBlock
            {
                Text = L.T("불투명도", "Opacity"), Foreground = Theme.BrMuted, FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            });
            var opVal = new TextBlock
            {
                Text = "100%", Foreground = Theme.BrAccent, FontFamily = Theme.Mono, FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center
            };
            opRow.Children.Add(opVal);
            foot.Children.Add(opRow);

            opacitySlider = new Slider
            {
                Minimum = 0, Maximum = 100, Value = 100,
                Margin = new Thickness(0, 6, 0, 0), Foreground = Theme.BrAccent
            };
            opacitySlider.ValueChanged += delegate
            {
                opVal.Text = (int)opacitySlider.Value + "%";
                if (suppressOpacity) return;
                var grp = doc.Group(activeGroup);
                if (grp != null) { grp.Opacity = opacitySlider.Value / 100.0; dirty = true; RefreshBase(null); return; }
                var lay = doc.Current;
                if (lay == null) return;
                lay.Opacity = opacitySlider.Value / 100.0;
                dirty = true;
                RefreshBase(null);
            };
            foot.Children.Add(opacitySlider);

            // 블렌드 모드 — 누르면 묶음별 메뉴
            blendLabel = new TextBlock { Text = L.T("보통", "Normal"), Foreground = Theme.BrText, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            var blendRow = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            blendRow.Children.Add(new TextBlock { Text = L.T("블렌드", "Blend"), Foreground = Theme.BrMuted, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
            var blendInner = new StackPanel { Orientation = Orientation.Horizontal };
            blendInner.Children.Add(blendLabel);
            blendInner.Children.Add(new TextBlock { Text = "  ▾", Foreground = Theme.BrMuted, FontSize = 10, VerticalAlignment = VerticalAlignment.Center });
            var blendBtn = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(10, 4, 8, 5),
                CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), BorderBrush = Theme.BrBorder,
                Background = Theme.Alpha(Colors.White, 0x08), Cursor = Cursors.Hand, Child = blendInner
            };
            blendBtn.MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs a) { a.Handled = true; };
            blendBtn.MouseLeftButtonUp += delegate { ShowBlendMenu(blendBtn); };
            blendRow.Children.Add(blendBtn);
            foot.Children.Add(blendRow);

            var maskRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            maskRow.Children.Add(Action_(L.T("효과…", "Effects…"), false, delegate { ShowEffects(); }));
            maskRow.Children.Add(Gap(6));
            maskRow.Children.Add(Action_(L.T("마스크 해제", "Clear Mask"), false, delegate
            {
                var lay = doc.Current;
                if (lay == null || !lay.HasMask) { Status(L.T("이 레이어엔 지운 자리가 없다", "Nothing has been erased on this layer")); return; }
                Push();
                doc.Current.ClearMask();
                RefreshBase(null);
                Status(L.T("지운 자리를 전부 되살렸다", "Restored all erased areas"));
            }));
            foot.Children.Add(maskRow);

            Grid.SetRow(foot, 2); root.Children.Add(foot);

            return new Border { Child = root };
        }

        Border PanelBtn(string icon, string tip, Action act)
        {
            var art = Icons.Boxed(icon, 14, Theme.BrDim);
            var b = new Border
            {
                Width = 28, Height = 26,
                CornerRadius = new CornerRadius(7),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = tip,
                Child = art
            };
            b.MouseEnter += delegate { b.Background = Theme.Alpha(Colors.White, 0x16); Icons.Tint(art, Theme.BrText); };
            b.MouseLeave += delegate { b.Background = Brushes.Transparent; Icons.Tint(art, Theme.BrDim); };
            b.MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs a) { a.Handled = true; };
            b.MouseLeftButtonUp += delegate { act(); };
            return b;
        }

        // 레이어 목록을 다시 그린다. 브러시를 끄는 동안에는 부르지 않는다 (RefreshBase 전체 갱신 때만).
        void RefreshLayerPanel()
        {
            if (layerList == null || !Settings.Current.LayerPanelOn) return;
            layerList.Children.Clear();

            // 위에 있는 레이어가 목록에서도 위에 오도록 뒤집어 보여준다.
            // 같은 그룹이 이어지는 동안은 머리줄 하나 아래 들여 쓴다.
            int prevGroup = 0;
            for (int i = doc.Layers.Count - 1; i >= 0; i--)
            {
                var l = doc.Layers[i];
                var g = doc.Group(l.GroupId);
                if (g != null && l.GroupId != prevGroup) layerList.Children.Add(GroupRow(g));
                prevGroup = g != null ? l.GroupId : 0;
                if (g != null && g.Collapsed) continue;
                var row = LayerRow(i);
                if (g != null) row.Margin = new Thickness(16, 1, 0, 1);
                layerList.Children.Add(row);
            }

            var cur = doc.Current;
            if (doc.Group(activeGroup) == null) activeGroup = 0;
            var ag = doc.Group(activeGroup);
            if (cur != null && opacitySlider != null)
            {
                suppressOpacity = true;
                opacitySlider.Value = Math.Round((ag != null ? ag.Opacity : cur.Opacity) * 100);
                suppressOpacity = false;
            }
            if (cur != null && blendLabel != null)
            {
                blendLabel.Text = Blend.Name(cur.Blend);
            }
        }

        Border LayerRow(int index)
        {
            var lay = doc.Layers[index];
            bool active = index == doc.Active;

            var row = new Grid { Height = 42 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 눈
            var eyeArt = Icons.Boxed(lay.Visible ? "eye" : "eye-off", 14,
                                     lay.Visible ? Theme.BrDim : Theme.Alpha(Theme.Muted, 0x80));
            var eye = new Border
            {
                Width = 26, Height = 26, CornerRadius = new CornerRadius(6),
                Background = Brushes.Transparent, Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = L.T("보이기/숨기기", "Show/hide"), Child = eyeArt
            };
            eye.MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs a) { a.Handled = true; };
            eye.MouseLeftButtonUp += delegate
            {
                lay.Visible = !lay.Visible;
                dirty = true;
                RefreshBase(null);
            };
            Grid.SetColumn(eye, 0); row.Children.Add(eye);

            // 미리보기
            var thumbHost = new Grid { Width = 40, Height = 28, Margin = new Thickness(2, 0, 8, 0) };
            thumbHost.Children.Add(new Rectangle { Fill = CheckerBrush(5) });
            if (lay.Adjust != null)
                thumbHost.Children.Add(new TextBlock { Text = "◐", FontSize = 17, Foreground = Theme.BrAccent,
                                                       HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            else try
            {
                var t = lay.Thumb(40, 28);
                using (var tb = t.ToBitmap())
                {
                    var img = new Image { Source = Cap.ToSource(tb), Stretch = Stretch.Fill };
                    RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                    thumbHost.Children.Add(img);
                }
            }
            catch { }
            var thumbClip = new Border
            {
                CornerRadius = new CornerRadius(4), ClipToBounds = true,
                BorderThickness = new Thickness(1), BorderBrush = Theme.Alpha(Colors.White, 0x1A),
                VerticalAlignment = VerticalAlignment.Center, Child = thumbHost
            };
            Grid.SetColumn(thumbClip, 1); row.Children.Add(thumbClip);

            // 이름
            var nameBox = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            nameBox.Children.Add(new TextBlock
            {
                Text = lay.Name,
                Foreground = active ? Theme.BrAccent : Theme.BrDim,
                FontSize = 12,
                FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            var tags = new List<string>();
            if (lay.HasMask) tags.Add(L.T("마스크", "Mask"));
            if (lay.Opacity < 0.999) tags.Add((int)(lay.Opacity * 100) + "%");
            if (lay.Blend != BlendMode.Normal) tags.Add(Blend.Name(lay.Blend));
            if (lay.Fx != null && lay.Fx.Any) tags.Add("fx");
            if (lay.Adjust != null) tags.Insert(0, L.T("조정 · ", "Adjustment · ") + lay.Adjust.Title);
            if (tags.Count > 0)
                nameBox.Children.Add(new TextBlock
                {
                    Text = string.Join(" · ", tags),
                    Foreground = Theme.BrMuted, FontSize = 9.5, Margin = new Thickness(0, 2, 0, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
            Grid.SetColumn(nameBox, 2); row.Children.Add(nameBox);

            // 순서
            var order = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0
            };
            order.Children.Add(MiniOrder("up", L.T("위로", "Move up"), delegate { MoveLayer(index, 1); }));
            order.Children.Add(MiniOrder("down", L.T("아래로", "Move down"), delegate { MoveLayer(index, -1); }));
            Grid.SetColumn(order, 3); row.Children.Add(order);

            var card = new Border
            {
                Padding = new Thickness(4, 0, 4, 0),
                Margin = new Thickness(0, 1, 0, 1),
                CornerRadius = new CornerRadius(8),
                Background = active ? Theme.Alpha(Theme.Accent, 0x22) : Brushes.Transparent,
                Cursor = Cursors.Hand,
                Child = row
            };
            card.MouseEnter += delegate
            {
                order.Opacity = 1;
                if (!active) card.Background = Theme.Alpha(Colors.White, 0x0E);
            };
            card.MouseLeave += delegate
            {
                order.Opacity = 0;
                card.Background = active ? Theme.Alpha(Theme.Accent, 0x22) : Brushes.Transparent;
            };
            card.MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs a)
            {
                if (a.ClickCount != 2) return;
                a.Handled = true;
                if (lay.Adjust != null) EditAdjustment(index); else RenameLayer(index);
            };
            card.MouseLeftButtonUp += delegate
            {
                if (xfOn && index != xfLayer) CommitTransform();
                activeGroup = 0;
                doc.Active = index;
                RefreshLayerPanel();
                if (tool == Tool.Transform) { BeginTransformIfNeeded(); BuildOptions(); }
                Status(L.F("{0} 선택", "Selected {0}", lay.Name));
            };
            card.MouseRightButtonUp += delegate(object o, MouseButtonEventArgs a)
            {
                doc.Active = index;
                RefreshLayerPanel();
                ShowLayerMenu(index);
                a.Handled = true;
            };
            return card;
        }

        // ---------- 그룹 ----------

        int activeGroup;      // 머리줄을 눌러 고른 그룹. 불투명도 슬라이더가 이 그룹을 민다

        void MoveLayer(int index, int delta)
        {
            Push();
            doc.Move(index, delta);
            RefreshBase(null);
        }

        // 그룹에서 뺀 레이어는 그룹 바로 위로 옮긴다 — 가운데서 빼면 그룹이 두 토막 난다
        void UngroupLayer(int index)
        {
            var l = doc.Layers[index];
            int gid = l.GroupId;
            l.GroupId = 0;
            int top = -1;
            for (int i = 0; i < doc.Layers.Count; i++) if (doc.Layers[i].GroupId == gid) top = i;
            if (top > index)
            {
                doc.Layers.RemoveAt(index);
                doc.Layers.Insert(top, l);          // 지운 만큼 한 칸 당겨졌으니 top 자리가 곧 그룹 바로 위
                doc.Active = top;
            }
            doc.PruneGroups();
        }

        void GroupLayer(int index)
        {
            Push();
            var g = doc.NewGroup(L.T("그룹 ", "Group ") + (doc.Groups.Count + 1));
            doc.Layers[index].GroupId = g.Id;
            activeGroup = 0;
            RefreshBase(null);
            Status(L.T("새 그룹을 만들었다 — 다른 레이어를 이 그룹 사이로 옮기면 함께 묶인다", "New group created — move other layers into it to group them"));
        }

        Border GroupRow(GroupInfo g)
        {
            bool active = activeGroup == g.Id;
            var row = new Grid { Height = 32 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var eyeArt = Icons.Boxed(g.Visible ? "eye" : "eye-off", 14, g.Visible ? Theme.BrDim : Theme.Alpha(Theme.Muted, 0x80));
            var eye = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(6), Background = Brushes.Transparent,
                                   Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, Child = eyeArt, ToolTip = L.T("그룹 보이기/숨기기", "Show/hide group") };
            eye.MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs a) { a.Handled = true; };
            eye.MouseLeftButtonUp += delegate { Push(); g.Visible = !g.Visible; dirty = true; RefreshBase(null); };
            Grid.SetColumn(eye, 0); row.Children.Add(eye);

            var fold = new TextBlock { Text = g.Collapsed ? "▸" : "▾", Foreground = Theme.BrDim, FontSize = 13, Width = 18,
                                       VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand, Margin = new Thickness(2, 0, 2, 0) };
            fold.MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs a) { a.Handled = true; };
            fold.MouseLeftButtonUp += delegate { g.Collapsed = !g.Collapsed; RefreshLayerPanel(); };
            Grid.SetColumn(fold, 1); row.Children.Add(fold);

            var name = new TextBlock
            {
                Text = "📁 " + g.Name + (g.Opacity < 0.999 ? "   " + (int)Math.Round(g.Opacity * 100) + "%" : ""),
                Foreground = active ? Theme.BrAccent : Theme.BrDim, FontSize = 12, FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(name, 2); row.Children.Add(name);

            var card = new Border
            {
                Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(0, 3, 0, 1), CornerRadius = new CornerRadius(8),
                Background = active ? Theme.Alpha(Theme.Accent, 0x22) : Theme.Alpha(Colors.White, 0x06), Cursor = Cursors.Hand, Child = row
            };
            card.MouseLeftButtonUp += delegate
            {
                activeGroup = g.Id;
                RefreshLayerPanel();
                Status(g.Name + L.T(" — 불투명도 슬라이더가 그룹 전체를 민다", " — the opacity slider now controls the whole group"));
            };
            card.MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs a)
            {
                if (a.ClickCount != 2) return;
                a.Handled = true;
                var d = new TextPrompt(L.T("그룹 이름", "Group name"), g.Name) { Owner = this };
                if (d.ShowDialog() != true) return;
                Push(); g.Name = d.Value; RefreshLayerPanel();
            };
            card.MouseRightButtonUp += delegate(object o, MouseButtonEventArgs a)
            {
                a.Handled = true;
                var m = new ContextMenu();
                m.Items.Add(MI(L.T("이름 바꾸기…", "Rename…"), delegate
                {
                    var d = new TextPrompt(L.T("그룹 이름", "Group name"), g.Name) { Owner = this };
                    if (d.ShowDialog() != true) return;
                    Push(); g.Name = d.Value; RefreshLayerPanel();
                }));
                m.Items.Add(MI(L.T("그룹 풀기 (레이어는 남긴다)", "Ungroup (keep layers)"), delegate
                {
                    Push();
                    foreach (var l in doc.Layers) if (l.GroupId == g.Id) l.GroupId = 0;
                    doc.PruneGroups();
                    if (activeGroup == g.Id) activeGroup = 0;
                    RefreshBase(null);
                }));
                m.IsOpen = true;
            };
            return card;
        }

        // ---------- 조정 레이어 ----------

        ToneSheet AdjustSheet(string kind, object settings)
        {
            switch (kind)
            {
                case "levels": return new LevelsSheet(Tone.Histogram(doc.Flatten(), null), (Tone.Levels)settings);
                case "curves": return new CurvesSheet(Tone.Histogram(doc.Flatten(), null), (Tone.Curves)settings);
                case "huesat": return new HueSatSheet((Tone.HueSat)settings);
                case "balance": return new BalanceSheet((Tone.Balance)settings);
                case "bw": return new BlackWhiteSheet((Tone.BlackWhite)settings);
                default: return null;
            }
        }

        void NewAdjustment(string kind)
        {
            if (xfOn) CommitTransform();
            Push();
            var adj = new Layer(new Canvas32(doc.W, doc.H), "") { X = 0, Y = 0 };
            int at = Math.Min(doc.Active + 1, doc.Layers.Count);
            var cur = doc.Current;
            if (cur != null && cur.GroupId != 0) adj.GroupId = cur.GroupId;
            adj.Adjust = new Adjustment(kind, kind == "invert" ? null : DefaultSettings(kind));
            adj.Name = adj.Adjust.Title;
            doc.Layers.Insert(at, adj);
            doc.Active = at;
            activeGroup = 0;
            RefreshBase(null);
            if (kind == "invert") { dirty = true; Status(L.T("반전 조정 레이어를 얹었다 — 지우개로 마스크를 칠하면 그 자리만 빠진다", "Added an Invert adjustment layer — paint its mask with the Eraser to exclude areas")); return; }
            if (!RunAdjustment(adj, true))
            {
                doc.Layers.Remove(adj);
                doc.Active = Math.Max(0, Math.Min(at - 1, doc.Layers.Count - 1));
                CancelPush();
                RefreshBase(null);
                Status(L.T("조정 레이어를 취소했다", "Adjustment layer canceled"));
            }
        }

        static object DefaultSettings(string kind)
        {
            switch (kind)
            {
                case "levels": return new Tone.Levels();
                case "curves": return new Tone.Curves();
                case "huesat": return new Tone.HueSat();
                case "balance": return new Tone.Balance();
                default: return new Tone.BlackWhite();
            }
        }

        void EditAdjustment(int index)
        {
            var adj = doc.Layers[index];
            if (adj.Adjust == null) return;
            if (adj.Adjust.Kind == "invert") { Status(L.T("반전에는 고칠 설정이 없다 — 마스크와 불투명도로 조절한다", "Invert has no settings — use the mask and opacity")); return; }
            Push();
            var before = adj.Adjust;
            if (!RunAdjustment(adj, false))
            {
                adj.Adjust = before;
                CancelPush();
                RefreshBase(null);
                Status(L.T("바꾸지 않았다", "No changes"));
            }
        }

        // 창을 띄워 움직이는 대로 조정 레이어에 반영한다. 적용하면 true.
        bool RunAdjustment(Layer adj, bool isNew)
        {
            var kind = adj.Adjust.Kind;
            var d = AdjustSheet(kind, adj.Adjust.Settings);
            if (d == null) return false;
            d.Preview = delegate(ToneFn f)
            {
                adj.Adjust = new Adjustment(kind, d.Settings());
                RefreshBase(null);
            };
            d.Owner = this;
            if (!d.ShowDialog().GetValueOrDefault()) return false;
            adj.Adjust = new Adjustment(kind, d.Settings());
            RefreshBase(null);
            dirty = true;
            Status((isNew ? L.F("{0} 조정 레이어를 얹었다", "Added a {0} adjustment layer", adj.Adjust.Title) : L.F("{0} 설정을 바꿨다", "Updated {0} settings", adj.Adjust.Title)) +
                   L.T(" — 두 번 누르면 다시 고친다 · 지우개로 마스크를 칠하면 그 자리만 빠진다", " — double-click to edit again · paint the mask with the Eraser to exclude areas"));
            return true;
        }

        Border MiniOrder(string icon, string tip, Action act)
        {
            var art = Icons.Boxed(icon, 12, Theme.BrMuted);
            var b = new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(5),
                Background = Theme.Alpha(Colors.White, 0x10),
                Margin = new Thickness(2, 0, 0, 0),
                Cursor = Cursors.Hand, ToolTip = tip, Child = art
            };
            b.MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs a) { a.Handled = true; };
            b.MouseLeftButtonUp += delegate(object o, MouseButtonEventArgs a) { a.Handled = true; act(); };
            return b;
        }

        void ShowLayerMenu(int index)
        {
            var lay = doc.Layers[index];
            var m = new ContextMenu();
            m.Items.Add(MI(L.T("이름 바꾸기…", "Rename…"), delegate { RenameLayer(index); }));
            m.Items.Add(MI(L.T("복제", "Duplicate"), delegate { DuplicateLayer(); }));
            if (lay.Adjust != null) m.Items.Add(MI(L.T("조정 설정 고치기…", "Edit Adjustment…"), delegate { EditAdjustment(index); }));
            m.Items.Add(new Separator());
            if (lay.GroupId == 0) m.Items.Add(MI(L.T("새 그룹으로 묶기", "Group into New Group"), delegate { GroupLayer(index); }));
            else m.Items.Add(MI(L.T("그룹에서 빼기", "Remove from Group"), delegate { Push(); UngroupLayer(index); RefreshBase(null); }));
            m.Items.Add(new Separator());
            m.Items.Add(MI(L.T("위로", "Move Up"), delegate { MoveLayer(index, 1); }));
            m.Items.Add(MI(L.T("아래로", "Move Down"), delegate { MoveLayer(index, -1); }));
            m.Items.Add(new Separator());
            if (lay.HasMask)
                m.Items.Add(MI(L.T("마스크 해제 (지운 자리 되살리기)", "Clear Mask (restore erased areas)"), delegate
                {
                    Push(); doc.Layers[index].ClearMask(); RefreshBase(null);
                }));
            m.Items.Add(MI(L.T("마스크를 픽셀에 굽기", "Apply Mask to Pixels"), delegate { BakeMask(index); }));
            m.Items.Add(new Separator());
            m.Items.Add(MI(L.T("오른쪽으로 90°", "Rotate 90° Right"), delegate { RotateLayer(index, 90); }));
            m.Items.Add(MI(L.T("왼쪽으로 90°", "Rotate 90° Left"), delegate { RotateLayer(index, 270); }));
            m.Items.Add(MI(L.T("좌우 뒤집기", "Flip Horizontal"), delegate { FlipLayer(index, true); }));
            m.Items.Add(MI(L.T("상하 뒤집기", "Flip Vertical"), delegate { FlipLayer(index, false); }));
            m.Items.Add(new Separator());
            m.Items.Add(MI(L.T("아래 레이어와 합치기", "Merge Down"), delegate { MergeDown(index); }));
            m.Items.Add(MI(L.T("삭제", "Delete"), delegate { doc.Active = index; DeleteLayer(); }));
            m.PlacementTarget = this;
            m.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            m.IsOpen = true;
        }

        // ---------- 레이어 명령 ----------

        void AddEmptyLayer()
        {
            Push();
            doc.Add(new Canvas32(doc.W, doc.H), L.T("레이어 ", "Layer ") + (doc.Layers.Count + 1), 0, 0);
            RefreshBase(null);
        }

        void DuplicateLayer()
        {
            var lay = doc.Current;
            if (lay == null) return;
            Push();
            // 마스크·불투명도·블렌드·효과까지 통째로. 버퍼는 나눠 쓰다가 고칠 때 복사된다.
            var copy = lay.ShallowCopy();
            lay.MarkShared();            // 원본도 이제 남과 버퍼를 나눠 쓰니 고치기 전에 복사해야 한다
            copy.Name = lay.Name + L.T(" 복사", " copy");
            doc.Layers.Insert(doc.Active + 1, copy);
            doc.Active = doc.Active + 1;
            RefreshBase(null);
        }

        void DeleteLayer()
        {
            if (doc.Layers.Count <= 1) { Status(L.T("마지막 레이어는 지울 수 없다", "Can't delete the last layer")); return; }
            Push();
            doc.Remove(doc.Active);
            doc.PruneGroups();
            RefreshBase(null);
        }

        void RenameLayer(int index)
        {
            var d = new TextPrompt(L.T("레이어 이름", "Layer name"), doc.Layers[index].Name) { Owner = this };
            if (d.ShowDialog() != true) return;
            Push();
            doc.Layers[index].Name = d.Value;
            RefreshLayerPanel();
        }

        // 마스크를 픽셀의 알파에 구워 넣는다. 되살릴 수 없게 되는 대신 메모리를 아낀다.
        void BakeMask(int index)
        {
            var lay = doc.Layers[index];
            if (lay.Adjust != null) { Status(L.T("조정 레이어의 마스크는 굽지 않는다 — 마스크가 곧 걸 자리다", "Adjustment layer masks can't be applied — the mask defines where it applies")); return; }
            if (!lay.HasMask) { Status(L.T("구울 마스크가 없다", "No mask to apply")); return; }
            Push();
            var baked = doc.Layers[index].Flatten(false);   // 불투명도는 레이어에 남으니 픽셀엔 안 굽는다
            doc.Layers[index] = lay.Derive(baked);
            RefreshBase(null);
            Status(L.T("마스크를 구웠다 — 이제 복구 브러시로는 되살릴 수 없다", "Mask applied — erased areas can no longer be restored"));
        }

        // 레이어 하나만 돌린다. 마스크도 같이 돌려야 지운 자리가 따라간다.
        void RotateLayer(int index, int degrees)
        {
            var lay = doc.Layers[index];
            if (lay.Adjust != null) { Status(L.T("조정 레이어는 돌릴 게 없다 — 마스크만 있다", "Adjustment layers can't be rotated — they only have a mask")); return; }
            Push();
            var baked = lay.Flatten(false);     // 마스크를 알파에 구워 함께 돌린다(불투명도는 레이어에 남긴다)
            using (var bmp = baked.ToBitmap())
            using (var rot = Ops.Rotate(bmp, degrees))
            {
                var px = Canvas32.From(rot);
                // 가운데를 유지한 채 돌린다
                int ccx = lay.X + lay.W / 2, ccy = lay.Y + lay.H / 2;
                var nl = lay.Derive(px);
                nl.X = ccx - px.W / 2;
                nl.Y = ccy - px.H / 2;
                doc.Layers[index] = nl;
            }
            RefreshBase(null);
            Status(L.T("레이어를 돌렸다", "Layer rotated"));
        }

        void FlipLayer(int index, bool horizontal)
        {
            var lay = doc.Layers[index];
            if (lay.Adjust != null) { Status(L.T("조정 레이어는 뒤집을 게 없다 — 마스크만 있다", "Adjustment layers can't be flipped — they only have a mask")); return; }
            Push();
            var baked = lay.Flatten(false);
            using (var bmp = baked.ToBitmap())
            using (var fl = Ops.Flip(bmp, horizontal))
            {
                doc.Layers[index] = lay.Derive(Canvas32.From(fl));
            }
            RefreshBase(null);
            Status(horizontal ? L.T("좌우로 뒤집었다", "Flipped horizontally") : L.T("상하로 뒤집었다", "Flipped vertically"));
        }

        void MergeDown(int index)
        {
            if (index <= 0) { Status(L.T("아래에 합칠 레이어가 없다", "No layer below to merge with")); return; }
            if (doc.Layers[index - 1].Adjust != null) { Status(L.T("아래가 조정 레이어라 합칠 픽셀이 없다", "The layer below is an adjustment layer — no pixels to merge")); return; }
            if (xfOn) CommitTransform();
            var lower = doc.Layers[index - 1];
            Push();

            // 두 장만 보이게 하고 합성한 결과를 아래 레이어로 삼는다
            var keep = new List<bool>();
            for (int i = 0; i < doc.Layers.Count; i++)
            {
                keep.Add(doc.Layers[i].Visible);
                doc.Layers[i].Visible = (i == index || i == index - 1) && keep[i];
            }
            doc.CompositeAll();
            var merged = doc.Comp.Clone();
            for (int i = 0; i < doc.Layers.Count; i++) doc.Layers[i].Visible = keep[i];

            string name = doc.Layers[index - 1].Name;
            doc.Layers.RemoveAt(index);
            // 두 장을 따로 합친 결과라, 아래 레이어가 밑과 섞이던 방식(블렌드)과 그룹은 이어받는다
            doc.Layers[index - 1] = new Layer(merged, name) { Blend = lower.Blend, GroupId = lower.GroupId };
            doc.PruneGroups();
            doc.Active = index - 1;
            RefreshBase(null);
            Status(L.T("아래 레이어와 합쳤다", "Merged down"));
        }

        Border BuildStatusBar()
        {
            var g = new Grid { Height = 30 };
            statusText = new TextBlock
            {
                Foreground = Theme.BrMuted, FontSize = 11, FontFamily = Theme.Mono,
                Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center
            };
            g.Children.Add(statusText);

            var zoomRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 14, 0)
            };
            zoomRow.Children.Add(Chip("zoomout", L.T("축소   Ctrl+-", "Zoom out   Ctrl+-"), delegate { Zoom(1 / 1.25); }));
            zoomText = new TextBlock
            {
                Text = "100%", Foreground = Theme.BrText, FontSize = 11, FontFamily = Theme.Mono,
                Width = 46, TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand
            };
            zoomText.MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs a) { a.Handled = true; };
            zoomText.MouseLeftButtonUp += delegate { if (zoom.ScaleX == 1) FitToWindow(); else SetZoom(1); };
            zoomRow.Children.Add(zoomText);
            zoomRow.Children.Add(Chip("zoomin", L.T("확대   Ctrl++", "Zoom in   Ctrl++"), delegate { Zoom(1.25); }));
            zoomRow.Children.Add(Gap(8));
            zoomRow.Children.Add(TitleBtn("fit", L.T("창에 맞춤", "Fit to window"), "", delegate { FitToWindow(); }));
            g.Children.Add(zoomRow);

            return new Border { Child = g };
        }

        static DrawingBrush CheckerBrush(int cell)
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x31)),
                null, new RectangleGeometry(new Rect(0, 0, cell * 2, cell * 2))));
            var dark = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x27));
            g.Children.Add(new GeometryDrawing(dark, null, new RectangleGeometry(new Rect(0, 0, cell, cell))));
            g.Children.Add(new GeometryDrawing(dark, null, new RectangleGeometry(new Rect(cell, cell, cell, cell))));
            var b = new DrawingBrush(g)
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, cell * 2, cell * 2),
                ViewportUnits = BrushMappingMode.Absolute,
                Stretch = Stretch.None
            };
            b.Freeze();
            return b;
        }

        // ================= 도구 전환과 옵션줄 =================

        void SelectTool(Tool t)
        {
            CommitText();
            if (xfOn && t != Tool.Transform) CommitTransform();
            tool = t;
            if (cloneMark != null && t != Tool.Clone) cloneMark.Visibility = Visibility.Collapsed;
            foreach (var kv in toolButtons)
            {
                bool on = kv.Key == t;
                kv.Value.Background = on ? Theme.Alpha(Theme.Accent, 0x26) : Brushes.Transparent;
                Icons.Tint(toolArt[kv.Key], on ? Theme.BrAccent : Theme.BrDim);
                toolName[kv.Key].Foreground = on ? Theme.BrAccent : Theme.BrDim;
                toolName[kv.Key].FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
            }
            if (t != Tool.Select) Deselect();
            if (t != Tool.Marquee) { lassoPts = null; edges = null; }
            DrawSelectionChrome();
            if (t == Tool.Transform) { BeginTransformIfNeeded(); BuildOptions(); }
            BuildOptions();
            UpdateCursor();
            EnsureBrushRing();
        }

        void BuildOptions()
        {
            optionsBar.Children.Clear();

            if (tool == Tool.Select)
            {
                if (selected == null)
                {
                    optionsBar.Children.Add(Hint(
                        L.T("개체를 누르면 여기서 색·굵기·선 종류를 바꿀 수 있다.    ", "Click an object to change its color, width, and line style here.    ") +
                        L.T("끌어서 이동 · 모서리로 크기 · 오른쪽 클릭으로 더보기 · Delete로 삭제", "Drag to move · corners to resize · right-click for more · Delete to remove")));
                    return;
                }
                BuildShapeOptions(selected);
                return;
            }

            switch (tool)
            {
                case Tool.Marquee:
                    optionsBar.Children.Add(Seg(L.T("모양", "Shape"),
                        new[] { "sel-rect", "sel-ellipse", "sel-lasso", "sel-magnetic", "sel-object", "sel-color" },
                        new[] { L.T("사각형", "Rectangle"), L.T("원형", "Ellipse"), L.T("올가미 — 손으로 그린다", "Lasso — draw freehand"), L.T("자동 올가미 — 가까운 경계에 달라붙는다", "Magnetic lasso — snaps to nearby edges"),
                                L.T("물체 (AI) — 누른 물체만 고른다", "Object (AI) — selects the object you click"), L.T("색상 범위 — 누른 색과 비슷한 곳을 전부 고른다", "Color range — selects everything close to the color you click") },
                        (int)selShape, delegate(int i) { selShape = (SelShape)i; BuildOptions(); }));
                    optionsBar.Children.Add(Gap(14));
                    if (selShape == SelShape.Color)
                    {
                        AddSlider(L.T("허용 범위", "Fuzziness"), crFuzz, 0, 200, delegate(double v) { crFuzz = (int)v; RebuildColorRange(); });
                        optionsBar.Children.Add(Gap(10));
                        AddToggle(L.T("반전", "Invert"), crInvert, delegate(bool v) { crInvert = v; RebuildColorRange(); });
                        optionsBar.Children.Add(Gap(10));
                    }
                    if (selection == null && selShape == SelShape.Color)
                    {
                        optionsBar.Children.Add(Hint(L.T("색을 눌러라. Shift+클릭은 그 색도, Alt+클릭은 그 색은 빼고.", "Click a color. Shift+click adds a color, Alt+click removes it.")));
                    }
                    else if (selection == null && selShape == SelShape.Object)
                    {
                        optionsBar.Children.Add(Hint(L.T("물체를 눌러라. 같은 자리를 다시 누르면 더 넓게, Shift+클릭은 더하기, Alt+클릭은 빼기.", "Click an object. Click the same spot again to widen, Shift+click to add, Alt+click to subtract.")));
                    }
                    else if (selection == null)
                    {
                        optionsBar.Children.Add(Hint(L.T("끌어서 영역을 고른다. 고르고 나면 자르거나 지우거나 떼어내 변형할 수 있다.", "Drag to select an area, then crop, erase, or lift it out to transform.")));
                    }
                    else
                    {
                        optionsBar.Children.Add(Action_(L.T("자르기", "Crop"), true, delegate { CropToSelection(); }));
                        optionsBar.Children.Add(Gap(6));
                        optionsBar.Children.Add(Action_(L.T("안쪽 지우기", "Erase Inside"), false, delegate { EraseSelection(true); }));
                        optionsBar.Children.Add(Gap(6));
                        optionsBar.Children.Add(Action_(L.T("바깥 지우기", "Erase Outside"), false, delegate { EraseSelection(false); }));
                        optionsBar.Children.Add(Gap(6));
                        optionsBar.Children.Add(Action_(L.T("내용 채우기", "Content-Aware Fill"), false, delegate { ContentFillSelection(); }));
                        optionsBar.Children.Add(Gap(6));
                        optionsBar.Children.Add(Action_(L.T("새 레이어로", "To New Layer"), false, delegate { SelectionToLayer(); }));
                        optionsBar.Children.Add(Gap(6));
                        optionsBar.Children.Add(Action_(L.T("자유 변형", "Free Transform"), false, delegate { TransformSelection(); }));
                        optionsBar.Children.Add(Gap(6));
                        optionsBar.Children.Add(Action_(L.T("색 조절", "Adjust Color"), false, delegate { ShowAdjust(); }));
                        optionsBar.Children.Add(Gap(12));
                        optionsBar.Children.Add(Action_(L.T("반전", "Invert"), false, delegate { InvertSelection(); }));
                        optionsBar.Children.Add(Gap(6));
                        optionsBar.Children.Add(Action_(L.T("해제", "Deselect"), false, delegate { ClearSelection(); }));
                    }
                    break;

                case Tool.Rect:
                case Tool.Ellipse:
                case Tool.Arrow:
                case Tool.Line:
                case Tool.Pen:
                case Tool.Highlight:
                case Tool.Text:
                case Tool.Counter:
                    BuildShapeOptions(null);
                    break;

                case Tool.Mosaic:
                    AddSlider(L.T("타일 크기", "Tile size"), mosaicCell, 4, 48, delegate(double v) { mosaicCell = (int)v; });
                    optionsBar.Children.Add(Gap(10));
                    optionsBar.Children.Add(Hint(L.T("가릴 범위를 끌어라. 되돌리기로만 복구된다.", "Drag over the area to hide. Only Undo can bring it back.")));
                    break;

                case Tool.Blur:
                    AddSlider(L.T("세기", "Strength"), blurStrength, 1, 24, delegate(double v) { blurStrength = (int)v; });
                    optionsBar.Children.Add(Gap(10));
                    optionsBar.Children.Add(Hint(L.T("흐릴 범위를 끌어라. 영역이 좁으면 세기가 자동으로 줄어든다.", "Drag over the area to blur. Strength drops automatically for small areas.")));
                    break;

                case Tool.MagicErase:
                    AddSlider(L.T("허용 오차", "Tolerance"), tolerance, 1, 100, delegate(double v) { tolerance = (int)v; Settings.Current.tolerance = tolerance; });
                    optionsBar.Children.Add(Gap(12));
                    AddSlider(L.T("경계 부드럽게", "Feather"), Settings.Current.featherPx, 0, 6, delegate(double v) { Settings.Current.featherPx = (int)v; });
                    optionsBar.Children.Add(Gap(12));
                    AddToggle(L.T("이어진 곳만", "Contiguous"), contiguous, delegate(bool v) { contiguous = v; });
                    break;

                case Tool.BgErase:
                    AddSlider(L.T("브러시", "Brush"), brushSize, 4, 200, delegate(double v) { brushSize = (int)v; Settings.Current.brushSize = brushSize; EnsureBrushRing(); });
                    optionsBar.Children.Add(Gap(12));
                    AddSlider(L.T("허용 오차", "Tolerance"), tolerance, 1, 100, delegate(double v) { tolerance = (int)v; Settings.Current.tolerance = tolerance; });
                    optionsBar.Children.Add(Gap(12));
                    AddSlider(L.T("가장자리", "Hardness"), hardness, 0, 100, delegate(double v) { hardness = (int)v; });
                    optionsBar.Children.Add(Gap(10));
                    optionsBar.Children.Add(Hint(L.T("브러시 한가운데 색만 지운다. 경계에 물려 끌어라.", "Erases only the color under the brush center. Drag along the edge.")));
                    break;

                case Tool.Heal:
                    AddSlider(L.T("브러시", "Brush"), brushSize, 4, 200, delegate(double v) { brushSize = (int)v; Settings.Current.brushSize = brushSize; EnsureBrushRing(); });
                    optionsBar.Children.Add(Gap(12));
                    foreach (var hm in new[] { HealMode.ContentAware, HealMode.Proximity, HealMode.Smooth })
                    {
                        var m_ = hm;
                        optionsBar.Children.Add(Action_(HealModeName(m_), healMode == m_, delegate { healMode = m_; BuildOptions(); }));
                        optionsBar.Children.Add(Gap(4));
                    }
                    optionsBar.Children.Add(Gap(8));
                    optionsBar.Children.Add(Hint(healMode == HealMode.Smooth
                        ? L.T("둘레 색을 안쪽으로 펴서 메운다. 무늬 없는 바탕에 좋다.", "Fills by spreading surrounding colors inward. Best on plain backgrounds.")
                        : L.T("칠하고 손을 떼면 둘레와 닮은 곳을 찾아 메운다.", "Paint and release to fill from similar nearby areas.")));
                    break;

                case Tool.Clone:
                    AddSlider(L.T("브러시", "Brush"), brushSize, 4, 200, delegate(double v) { brushSize = (int)v; Settings.Current.brushSize = brushSize; EnsureBrushRing(); });
                    optionsBar.Children.Add(Gap(12));
                    AddSlider(L.T("가장자리", "Hardness"), hardness, 0, 100, delegate(double v) { hardness = (int)v; });
                    optionsBar.Children.Add(Gap(12));
                    AddToggle(L.T("정렬", "Aligned"), cloneAligned, delegate(bool v) { cloneAligned = v; cloneOffset = null; });
                    optionsBar.Children.Add(Gap(10));
                    optionsBar.Children.Add(Hint(cloneSrc == null
                        ? L.T("Alt+클릭으로 가져올 곳부터 찍어라.", "Alt+click to set the source first.")
                        : (cloneAligned ? L.T("획마다 같은 거리에서 가져온다.", "Each stroke samples from the same offset.") : L.T("획마다 찍은 자리에서 다시 가져온다.", "Each stroke samples from the original source point."))));
                    break;

                case Tool.Brush:
                    AddColors(DefaultSwatches, null);
                    optionsBar.Children.Add(Gap(14));
                    AddSlider(L.T("브러시", "Brush"), brushSize, 1, 200, delegate(double v) { brushSize = (int)v; Settings.Current.brushSize = brushSize; EnsureBrushRing(); });
                    optionsBar.Children.Add(Gap(12));
                    AddSlider(L.T("가장자리", "Hardness"), hardness, 0, 100, delegate(double v) { hardness = (int)v; });
                    optionsBar.Children.Add(Gap(12));
                    AddSlider(L.T("불투명도", "Opacity"), paintOpacity, 1, 100, delegate(double v) { paintOpacity = (int)v; });
                    break;

                case Tool.Gradient:
                    AddColors(DefaultSwatches, null);
                    optionsBar.Children.Add(Gap(12));
                    {
                        string[] gs = { L.T("직선", "Linear"), L.T("원형", "Radial") };
                        for (int gi = 0; gi < 2; gi++)
                        {
                            int k = gi;
                            optionsBar.Children.Add(Action_(gs[k], gradShape == k, delegate { gradShape = k; BuildOptions(); }));
                            optionsBar.Children.Add(Gap(4));
                        }
                        optionsBar.Children.Add(Gap(8));
                        string[] ge = { L.T("→ 투명", "→ Transparent"), L.T("→ 흰색", "→ White"), L.T("→ 검정", "→ Black") };
                        for (int gi = 0; gi < 3; gi++)
                        {
                            int k = gi;
                            optionsBar.Children.Add(Action_(ge[k], gradEnd == k, delegate { gradEnd = k; BuildOptions(); }));
                            optionsBar.Children.Add(Gap(4));
                        }
                    }
                    optionsBar.Children.Add(Gap(8));
                    AddToggle(L.T("뒤집기", "Reverse"), gradReverse, delegate(bool v) { gradReverse = v; });
                    optionsBar.Children.Add(Gap(10));
                    AddSlider(L.T("불투명도", "Opacity"), paintOpacity, 1, 100, delegate(double v) { paintOpacity = (int)v; });
                    break;

                case Tool.Liquify:
                    AddSlider(L.T("브러시", "Brush"), brushSize, 4, 200, delegate(double v) { brushSize = (int)v; Settings.Current.brushSize = brushSize; EnsureBrushRing(); });
                    optionsBar.Children.Add(Gap(10));
                    {
                        string[] wn = { L.T("밀기", "Push"), L.T("문지르기", "Smudge"), L.T("흐리게", "Blur") };
                        for (int wi = 0; wi < 3; wi++)
                        {
                            int k = wi;
                            optionsBar.Children.Add(Action_(wn[k], warpMode == k, delegate { warpMode = k; BuildOptions(); }));
                            optionsBar.Children.Add(Gap(4));
                        }
                    }
                    optionsBar.Children.Add(Gap(8));
                    AddSlider(L.T("세기", "Strength"), warpStrength, 1, 100, delegate(double v) { warpStrength = (int)v; });
                    optionsBar.Children.Add(Gap(10));
                    if (warpMode == 2)
                        AddSlider(L.T("흐림", "Blur"), blurBrushRadius, 1, 50, delegate(double v) { blurBrushRadius = (int)v; });
                    else
                        AddSlider(L.T("가장자리", "Hardness"), hardness, 0, 100, delegate(double v) { hardness = (int)v; });
                    break;

                case Tool.Erase:
                    AddSlider(L.T("브러시", "Brush"), brushSize, 4, 200, delegate(double v) { brushSize = (int)v; Settings.Current.brushSize = brushSize; EnsureBrushRing(); });
                    optionsBar.Children.Add(Gap(12));
                    AddSlider(L.T("가장자리", "Hardness"), hardness, 0, 100, delegate(double v) { hardness = (int)v; });
                    break;

                case Tool.Restore:
                    AddSlider(L.T("브러시", "Brush"), brushSize, 4, 200, delegate(double v) { brushSize = (int)v; Settings.Current.brushSize = brushSize; EnsureBrushRing(); });
                    optionsBar.Children.Add(Gap(12));
                    AddSlider(L.T("가장자리", "Hardness"), hardness, 0, 100, delegate(double v) { hardness = (int)v; });
                    optionsBar.Children.Add(Gap(10));
                    optionsBar.Children.Add(Hint(L.T("지운 자리를 도로 칠한다. 원본이 남아 있어 몇 번이고 오간다.", "Paint back erased areas. The original is kept, so you can go back and forth.")));
                    break;

                case Tool.Transform:
                    if (!xfOn)
                    {
                        optionsBar.Children.Add(Hint(L.T("레이어를 고르면 틀이 생긴다.", "Select a layer to show the transform box.")));
                        break;
                    }
                    optionsBar.Children.Add(Action_(L.T("적용", "Apply"), true, delegate { CommitTransform(); }));
                    optionsBar.Children.Add(Gap(6));
                    optionsBar.Children.Add(Action_(L.T("되돌리기", "Reset"), false, delegate { ResetTransform(); }));
                    optionsBar.Children.Add(Gap(12));
                    optionsBar.Children.Add(Action_(L.T("가운데로", "Center"), false, delegate { CenterLayer(); }));
                    optionsBar.Children.Add(Gap(6));
                    optionsBar.Children.Add(Action_(L.T("판에 맞춤", "Fit to Canvas"), false, delegate { FitLayerToDoc(); }));
                    optionsBar.Children.Add(Gap(6));
                    optionsBar.Children.Add(Action_(L.T("원근", "Perspective"), xfPersp, delegate { TogglePerspective(); }));
                    optionsBar.Children.Add(Gap(14));
                    optionsBar.Children.Add(Hint(
                        L.T("모서리 크기 · 손잡이 회전 · 안쪽 이동   ·   Shift 비율유지/15°   ·   Ctrl+변 기울이기   ·   Enter 적용", "Corners resize · handle rotates · inside moves   ·   Shift keeps ratio/15°   ·   Ctrl+edge skews   ·   Enter applies")));
                    break;

                case Tool.Picker:
                    optionsBar.Children.Add(Hint(L.T("그림에서 색을 집으면 그리기 색으로 쓴다.", "Pick a color from the image to draw with it.")));
                    optionsBar.Children.Add(Gap(12));
                    AddColors();
                    break;
            }
        }

        // 도형 속성줄.
        // t가 null이면 '앞으로 그릴 도형'의 기본값을 다루고, t가 있으면 고른 개체를 그 자리에서 고친다.
        // 같은 조절기를 양쪽에 쓰기 때문에 새로 그릴 때와 고칠 때의 조작이 완전히 같다.
        void BuildShapeOptions(Ann t)
        {
            Tool kind = t != null ? t.Kind : tool;

            AddColors(kind == Tool.Highlight ? HighlightSwatches : DefaultSwatches, t);
            optionsBar.Children.Add(Gap(14));

            switch (kind)
            {
                case Tool.Text:
                    AddFontPicker(t);
                    optionsBar.Children.Add(Gap(10));
                    AddSlider(L.T("크기", "Size"), t != null ? t.FontSize : textSize, 10, 120, delegate(double v)
                    {
                        if (t != null) { t.FontSize = v; Rebuild(t); DrawHandles(); }
                        else textSize = v;
                    });
                    optionsBar.Children.Add(Gap(12));
                    AddTextStyleToggles(t);
                    break;

                case Tool.Counter:
                    AddSlider(L.T("크기", "Size"), t != null ? t.FontSize : counterSize, 12, 60, delegate(double v)
                    {
                        if (t != null) { t.FontSize = v; Rebuild(t); DrawHandles(); }
                        else counterSize = v;
                    });
                    if (t == null)
                    {
                        optionsBar.Children.Add(Gap(10));
                        optionsBar.Children.Add(Action_(L.T("번호 1부터", "Restart at 1"), false, delegate
                        { counterNext = 1; Status(L.T("다음 번호를 1로 되돌렸다", "Next number reset to 1")); }));
                    }
                    break;

                case Tool.Highlight:
                    AddSlider(L.T("굵기", "Width"), t != null ? t.Thickness : Math.Max(10, thickness), 8, 60, delegate(double v)
                    {
                        if (t != null) { t.Thickness = v; Rebuild(t); }
                        else thickness = v;
                    });
                    break;

                default:
                    AddSlider(L.T("굵기", "Width"), t != null ? t.Thickness : thickness, 1, 24, delegate(double v)
                    {
                        if (t != null) { t.Thickness = v; Rebuild(t); }
                        else thickness = v;
                    });
                    break;
            }

            if (kind == Tool.Arrow)
            {
                optionsBar.Children.Add(Gap(14));
                int active = (int)(t != null ? t.Head : arrowHead);
                optionsBar.Children.Add(Seg(L.T("촉", "Head"),
                    new[] { "head-end", "head-start", "head-both" },
                    new[] { L.T("끝에 촉", "Head at end"), L.T("시작에 촉", "Head at start"), L.T("양쪽 다", "Both ends") },
                    active, delegate(int i)
                    {
                        if (t != null) { Push(); t.Head = (ArrowHead)i; Rebuild(t); }
                        else arrowHead = (ArrowHead)i;
                        BuildOptions();
                    }));
            }

            if (kind == Tool.Rect || kind == Tool.Ellipse || kind == Tool.Line
                || kind == Tool.Arrow || kind == Tool.Pen)
            {
                optionsBar.Children.Add(Gap(14));
                int active = (int)(t != null ? t.Dash : lineDash);
                optionsBar.Children.Add(Seg(L.T("선", "Line"),
                    new[] { "dash-solid", "dash-dash", "dash-dot", "dash-dashdot" },
                    new[] { L.T("실선", "Solid"), L.T("파선", "Dashed"), L.T("점선", "Dotted"), L.T("일점쇄선", "Dash-dot") },
                    active, delegate(int i)
                    {
                        if (t != null) { Push(); t.Dash = (LineDash)i; Rebuild(t); }
                        else lineDash = (LineDash)i;
                        BuildOptions();
                    }));
            }

            if (kind != Tool.Highlight)
            {
                optionsBar.Children.Add(Gap(12));
                optionsBar.Children.Add(StyleBtn(L.T("그림자", "Shadow"), L.T("개체 뒤에 그림자를 깐다", "Add a drop shadow behind the object"),
                    t != null ? t.Shadow : shapeShadow, FontWeights.Normal, FontStyles.Normal, false,
                    delegate(bool v) { if (t != null) { t.Shadow = v; Rebuild(t); } else shapeShadow = v; }));
            }

            if (t != null)
            {
                optionsBar.Children.Add(Gap(12));
                AddSlider(L.T("회전", "Rotate"), t.Angle, -180, 180, delegate(double v)
                {
                    t.Angle = Math.Round(v, 1);
                    Rebuild(t);
                    DrawHandles();
                });
                optionsBar.Children.Add(Gap(16));
                optionsBar.Children.Add(Action_(L.T("복제", "Duplicate"), false, delegate { Duplicate(t); }));
                optionsBar.Children.Add(Gap(6));
                optionsBar.Children.Add(Action_(L.T("삭제", "Delete"), false, delegate { DeleteAnn(t); }));
            }
        }

        bool shapeShadow;
        string textFont = "맑은 고딕";
        bool textBold = true, textItalic, textUnderline, textOutline = true, textBoxBg;

        // 글꼴 고르기. 목록의 각 줄을 그 글꼴로 직접 그려서 눈으로 고른다.
        void AddFontPicker(Ann t)
        {
            string cur = t != null ? t.Font : textFont;

            var label = new TextBlock
            {
                Text = cur,
                Foreground = Theme.BrText,
                FontFamily = SafeFont(cur),
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 128,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var caret = Icons.Boxed("down", 11, Theme.BrMuted);
            caret.VerticalAlignment = VerticalAlignment.Center;
            caret.Margin = new Thickness(7, 0, 0, 0);

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(label);
            row.Children.Add(caret);

            var btn = new Border
            {
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(10, 5, 8, 6),
                Background = Theme.Alpha(Colors.White, 0x0E),
                BorderBrush = Theme.BrBorder,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = L.T("글꼴", "Font"),
                Child = row
            };
            btn.MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs a) { a.Handled = true; };
            btn.MouseLeftButtonUp += delegate { ShowFontPopup(btn, t); };
            optionsBar.Children.Add(btn);
        }

        static List<string> fontNames;

        static List<string> FontNames()
        {
            if (fontNames != null) return fontNames;
            var favorites = new List<string>
            { "맑은 고딕", "나눔고딕", "본고딕", "Pretendard", "Segoe UI", "Arial", "Consolas", "Times New Roman" };

            var all = new List<string>();
            foreach (var f in Fonts.SystemFontFamilies)
            {
                string n = null;
                // 한국어 이름이 있으면 그걸 쓴다
                foreach (var kv in f.FamilyNames)
                {
                    if (kv.Key.IetfLanguageTag.StartsWith("ko")) { n = kv.Value; break; }
                    if (n == null) n = kv.Value;
                }
                if (!string.IsNullOrEmpty(n) && !all.Contains(n)) all.Add(n);
            }
            all.Sort(StringComparer.CurrentCulture);

            fontNames = new List<string>();
            foreach (var f in favorites) if (all.Contains(f)) fontNames.Add(f);
            foreach (var f in all) if (!fontNames.Contains(f)) fontNames.Add(f);
            return fontNames;
        }

        void ShowFontPopup(UIElement anchor, Ann t)
        {
            var list = new StackPanel();
            var names = FontNames();
            string cur = t != null ? t.Font : textFont;

            var popup = new System.Windows.Controls.Primitives.Popup
            {
                PlacementTarget = anchor,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true
            };

            for (int i = 0; i < names.Count; i++)
            {
                string name = names[i];
                bool on = name == cur;
                var item = new Border
                {
                    Padding = new Thickness(12, 6, 12, 7),
                    CornerRadius = new CornerRadius(6),
                    Margin = new Thickness(3, 1, 3, 1),
                    Background = on ? Theme.Alpha(Theme.Accent, 0x26) : Brushes.Transparent,
                    Cursor = Cursors.Hand,
                    Child = new TextBlock
                    {
                        Text = name,
                        FontFamily = SafeFont(name),
                        FontSize = 13.5,
                        Foreground = on ? Theme.BrAccent : Theme.BrText
                    }
                };
                item.MouseEnter += delegate { if (!on) item.Background = Theme.Alpha(Colors.White, 0x12); };
                item.MouseLeave += delegate { item.Background = on ? Theme.Alpha(Theme.Accent, 0x26) : Brushes.Transparent; };
                item.MouseLeftButtonUp += delegate
                {
                    if (t != null) { Push(); t.Font = name; Rebuild(t); DrawHandles(); }
                    else textFont = name;
                    popup.IsOpen = false;
                    BuildOptions();
                };
                // 즐겨 쓰는 것과 나머지를 줄 하나로 가른다
                if (i == 0 || (i > 0 && i < names.Count && IsFavoriteBoundary(names, i)))
                    list.Children.Add(new Rectangle
                    {
                        Height = 1, Margin = new Thickness(10, 4, 10, 4),
                        Fill = Theme.Alpha(Colors.White, 0x14),
                        Visibility = i == 0 ? Visibility.Collapsed : Visibility.Visible
                    });
                list.Children.Add(item);
            }

            popup.Child = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xF4, 0x15, 0x15, 0x19)),
                BorderBrush = Theme.BrBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(3),
                Effect = new DropShadowEffect { BlurRadius = 20, ShadowDepth = 5, Opacity = 0.5, Color = Colors.Black },
                Child = new ScrollViewer
                {
                    MaxHeight = 380,
                    Width = 236,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = list
                }
            };
            popup.IsOpen = true;
        }

        static bool IsFavoriteBoundary(List<string> names, int i)
        {
            // 즐겨 쓰는 글꼴 묶음이 끝나는 자리 한 번만 줄을 긋는다
            var favorites = new[] { "맑은 고딕", "나눔고딕", "본고딕", "Pretendard", "Segoe UI", "Arial", "Consolas", "Times New Roman" };
            bool prevFav = Array.IndexOf(favorites, names[i - 1]) >= 0;
            bool curFav = Array.IndexOf(favorites, names[i]) >= 0;
            return prevFav && !curFav;
        }

        void AddTextStyleToggles(Ann t)
        {
            optionsBar.Children.Add(StyleBtn(L.T("가", "A"), L.T("굵게", "Bold"), t != null ? t.Bold : textBold, FontWeights.Bold, FontStyles.Normal, false,
                delegate(bool v) { if (t != null) { t.Bold = v; Rebuild(t); } else textBold = v; }));
            optionsBar.Children.Add(StyleBtn(L.T("가", "A"), L.T("기울임", "Italic"), t != null ? t.Italic : textItalic, FontWeights.Normal, FontStyles.Italic, false,
                delegate(bool v) { if (t != null) { t.Italic = v; Rebuild(t); } else textItalic = v; }));
            optionsBar.Children.Add(StyleBtn(L.T("가", "A"), L.T("밑줄", "Underline"), t != null ? t.Underline : textUnderline, FontWeights.Normal, FontStyles.Normal, true,
                delegate(bool v) { if (t != null) { t.Underline = v; Rebuild(t); } else textUnderline = v; }));
            optionsBar.Children.Add(Gap(10));
            optionsBar.Children.Add(StyleBtn(L.T("외곽선", "Outline"), L.T("어수선한 배경 위에서도 읽히게 반대 밝기 테두리를 두른다", "Adds a contrasting outline so text reads on busy backgrounds"),
                t != null ? t.Outline : textOutline, FontWeights.Normal, FontStyles.Normal, false,
                delegate(bool v) { if (t != null) { t.Outline = v; Rebuild(t); } else textOutline = v; }));
            optionsBar.Children.Add(StyleBtn(L.T("배경", "Background"), L.T("글자 뒤에 판을 깐다", "Adds a backing plate behind the text"),
                t != null ? t.BoxBg : textBoxBg, FontWeights.Normal, FontStyles.Normal, false,
                delegate(bool v) { if (t != null) { t.BoxBg = v; Rebuild(t); DrawHandles(); } else textBoxBg = v; }));
        }

        Border StyleBtn(string label, string tip, bool on,
                        FontWeight weight, FontStyle style, bool underline, Action<bool> set)
        {
            var txt = new TextBlock
            {
                Text = label,
                FontSize = 12.5,
                FontWeight = weight,
                FontStyle = style,
                TextDecorations = underline ? TextDecorations.Underline : null,
                Foreground = on ? Theme.BrAccent : Theme.BrDim,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var b = new Border
            {
                MinWidth = 30, Height = 26,
                Padding = new Thickness(8, 0, 8, 0),
                Margin = new Thickness(0, 0, 4, 0),
                CornerRadius = new CornerRadius(6),
                Background = on ? Theme.Alpha(Theme.Accent, 0x2B) : Theme.Alpha(Colors.White, 0x0C),
                Cursor = Cursors.Hand,
                ToolTip = tip,
                VerticalAlignment = VerticalAlignment.Center,
                Child = txt
            };
            b.MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs a) { a.Handled = true; };
            b.MouseLeftButtonUp += delegate { set(!on); BuildOptions(); };
            return b;
        }

        // 몇 개 중 하나를 고르는 작은 띠.
        FrameworkElement Seg(string label, string[] icons, string[] tips, int active, Action<int> pick)
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            if (!string.IsNullOrEmpty(label))
                row.Children.Add(new TextBlock
                {
                    Text = label, Foreground = Theme.BrMuted, FontSize = 11.5,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                });

            var inner = new StackPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < icons.Length; i++)
            {
                bool on = i == active;
                var art = Icons.Boxed(icons[i], 16, on ? Theme.BrAccent : Theme.BrDim);
                var b = new Border
                {
                    Width = 30, Height = 24,
                    CornerRadius = new CornerRadius(6),
                    Background = on ? Theme.Alpha(Theme.Accent, 0x2B) : Brushes.Transparent,
                    Cursor = Cursors.Hand,
                    ToolTip = tips[i],
                    Child = art
                };
                int idx = i;
                b.MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs a) { a.Handled = true; };
                b.MouseLeftButtonUp += delegate { pick(idx); };
                inner.Children.Add(b);
            }

            row.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(8),
                Background = Theme.Alpha(Colors.White, 0x0C),
                Padding = new Thickness(2),
                VerticalAlignment = VerticalAlignment.Center,
                Child = inner
            });
            return row;
        }

        double textSize = 20;
        double counterSize = 20;

        static TextBlock Hint(string t)
        {
            return new TextBlock
            {
                Text = t, Foreground = Theme.BrMuted, FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        static readonly string[] DefaultSwatches =
            { "#F87171", "#FDE047", "#34D399", "#60A5FA", "#C084FC", "#F4F4F5", "#0B0B0F" };
        static readonly string[] HighlightSwatches =
            { "#FDE047", "#34D399", "#60A5FA", "#F472B6", "#FB923C" };

        void AddColors() { AddColors(DefaultSwatches, null); }

        void AddColors(string[] hexes, Ann t)
        {
            Color cur = t != null ? t.Color : ink;
            foreach (var hex in hexes)
            {
                var c = Theme.H(hex);
                var dot = new Border
                {
                    Width = 22, Height = 22,
                    Margin = new Thickness(0, 0, 6, 0),
                    CornerRadius = new CornerRadius(11),
                    Background = new SolidColorBrush(c),
                    BorderThickness = new Thickness(2),
                    BorderBrush = SameColor(c, cur) ? Theme.BrText : Theme.Alpha(Colors.White, 0x22),
                    Cursor = Cursors.Hand,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var captured = c;
                dot.MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs a) { a.Handled = true; };
                dot.MouseLeftButtonUp += delegate
                {
                    if (t != null) { Push(); t.Color = captured; Rebuild(t); }
                    else { ink = captured; ApplyToSelected(); }
                    BuildOptions();
                };
                optionsBar.Children.Add(dot);
            }
        }

        static bool SameColor(Color a, Color b) { return a.R == b.R && a.G == b.G && a.B == b.B; }

        void AddSlider(string label, double value, double min, double max, Action<double> set)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new TextBlock
            {
                Text = label, Foreground = Theme.BrMuted, FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0)
            });
            var sl = new Slider
            {
                Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, value)),
                Width = 112, VerticalAlignment = VerticalAlignment.Center, Foreground = Theme.BrAccent
            };
            var val = new TextBlock
            {
                Text = ((int)sl.Value).ToString(), Foreground = Theme.BrAccent,
                FontFamily = Theme.Mono, FontSize = 11, Width = 28,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 0, 0)
            };
            sl.ValueChanged += delegate { set(sl.Value); val.Text = ((int)sl.Value).ToString(); };
            sp.Children.Add(sl);
            sp.Children.Add(val);
            optionsBar.Children.Add(sp);
        }

        void AddToggle(string label, bool value, Action<bool> set)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new TextBlock
            {
                Text = label, Foreground = Theme.BrMuted, FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0)
            });
            var t = new Toggle(value) { VerticalAlignment = VerticalAlignment.Center };
            t.Changed += delegate(bool v) { set(v); };
            sp.Children.Add(t);
            optionsBar.Children.Add(sp);
        }

        Border maxBtn;

        // 최소화·최대화·닫기. 이 셋은 윈도 관례가 워낙 굳어 있어 아이콘만으로 읽힌다.
        Border WinBtn(string icon, string tip, bool danger, Action act)
        {
            var art = Icons.Boxed(icon, 14, Theme.BrDim);
            var b = new Border
            {
                Width = 36, Height = 30,
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(1, 0, 0, 0),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = tip,
                Child = art
            };
            b.MouseEnter += delegate
            {
                b.Background = danger ? Theme.Alpha(Theme.Danger, 0x3A) : Theme.Alpha(Colors.White, 0x18);
                Icons.Tint(art, Theme.BrText);
            };
            b.MouseLeave += delegate
            {
                b.Background = Brushes.Transparent;
                Icons.Tint(art, Theme.BrDim);
            };
            b.MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs a) { a.Handled = true; };
            b.MouseLeftButtonUp += delegate { act(); };
            return b;
        }

        void ToggleMaximize()
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        // 최대화 상태에 따라 단추 그림과 설명을 바꾼다.
        void SyncMaxButton()
        {
            if (maxBtn == null) return;
            bool max = WindowState == WindowState.Maximized;
            var art = Icons.Boxed(max ? "restore" : "maximize", 14, Theme.BrDim);
            maxBtn.Child = art;
            maxBtn.ToolTip = max ? L.T("이전 크기로", "Restore Down") : L.T("최대화", "Maximize");
        }

        // 제목줄 버튼. 아이콘만 두면 무슨 기능인지 읽히지 않아 이름을 같이 쓴다.
        Border TitleBtn(string icon, string label, string tip, Action act)
        {
            var art = Icons.Boxed(icon, 15, Theme.BrDim);
            art.VerticalAlignment = VerticalAlignment.Center;

            var text = new TextBlock
            {
                Text = label, Foreground = Theme.BrDim, FontSize = 12,
                Margin = new Thickness(7, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(art);
            row.Children.Add(text);

            var b = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(9, 6, 11, 7),
                Margin = new Thickness(2, 0, 0, 0),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = label + "   " + tip,
                Child = row
            };
            b.MouseEnter += delegate
            {
                b.Background = Theme.Alpha(Colors.White, 0x14);
                text.Foreground = Theme.BrText;
                Icons.Tint(art, Theme.BrText);
            };
            b.MouseLeave += delegate
            {
                b.Background = Brushes.Transparent;
                text.Foreground = Theme.BrDim;
                Icons.Tint(art, Theme.BrDim);
            };
            b.MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs a) { a.Handled = true; };
            b.MouseLeftButtonUp += delegate { act(); };
            return b;
        }

        Border Chip(string icon, string tip, Action act)
        {
            var b = new Border
            {
                Width = 32, Height = 30,
                CornerRadius = new CornerRadius(8),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = tip,
                Child = Icons.Boxed(icon, 16, Theme.BrMuted)
            };
            b.MouseEnter += delegate { b.Background = Theme.Alpha(Colors.White, 0x14); };
            b.MouseLeave += delegate { b.Background = Brushes.Transparent; };
            b.MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs a) { a.Handled = true; };
            b.MouseLeftButtonUp += delegate { act(); };
            return b;
        }

        Border Action_(string text, bool primary, Action act)
        {
            var b = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(13, 6, 13, 7),
                Background = primary ? Theme.BrAccent : Theme.Alpha(Colors.White, 0x10),
                BorderBrush = primary ? Brushes.Transparent : Theme.BrBorder,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = text, FontSize = 12.5,
                    FontWeight = primary ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = primary ? new SolidColorBrush(Theme.H("#08110D")) : Theme.BrText
                }
            };
            b.MouseEnter += delegate { b.Opacity = 0.85; };
            b.MouseLeave += delegate { b.Opacity = 1; };
            b.MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs a) { a.Handled = true; };
            b.MouseLeftButtonUp += delegate { act(); };
            return b;
        }

        void Status(string t) { if (statusText != null) statusText.Text = t; }

        // ================= 바탕 갱신 =================

        // rect가 null이면 전체를 다시 올린다. 브러시로 끄는 중에는 닿은 곳만 올려야 빠르다.
        void RefreshBase(D.Rectangle? rect)
        {
            if (baseWb == null || baseWb.PixelWidth != doc.W || baseWb.PixelHeight != doc.H)
            {
                baseWb = new WriteableBitmap(doc.W, doc.H, 96, 96, PixelFormats.Bgra32, null);
                baseImg.Source = baseWb;
                stackHost.Width = doc.W; stackHost.Height = doc.H;
                Dispatcher.BeginInvoke(new Action(delegate { DrawGuides(); DrawRulers(); }), System.Windows.Threading.DispatcherPriority.Render);
                inkLayer.Width = doc.W; inkLayer.Height = doc.H;
                uiLayer.Width = doc.W; uiLayer.Height = doc.H;
                rect = null;
            }
            if (rect.HasValue)
            {
                var r = rect.Value;
                r.Intersect(doc.Full);
                if (r.Width <= 0 || r.Height <= 0) return;
                doc.Composite(r);
                baseWb.WritePixels(new Int32Rect(r.X, r.Y, r.Width, r.Height), doc.Comp.P, doc.W * 4, r.X, r.Y);
            }
            else
            {
                doc.CompositeAll();
                baseWb.WritePixels(new Int32Rect(0, 0, doc.W, doc.H), doc.Comp.P, doc.W * 4, 0);
                // 미리보기를 다시 만드는 값이 싸지 않다. 브러시를 끄는 동안(부분 갱신)엔 건너뛴다.
                RefreshLayerPanel();
            }
            Status(doc.W + " × " + doc.H
                   + (doc.Layers.Count > 1 ? L.F("   레이어 {0}장", "   {0} layers", doc.Layers.Count) : "")
                   + (filePath != null ? "   " + System.IO.Path.GetFileName(filePath) : ""));
        }

        // ================= 되돌리기 =================

        void Push()
        {
            // doc.Snapshot()은 버퍼를 복사하지 않고 가리키기만 한다.
            // 실제 복사는 누군가 그 버퍼를 고치려 할 때 일어난다.
            var snap = new Snapshot { Doc = doc.Snapshot(), Anns = new List<Ann>(), Counter = counterNext };
            foreach (var a in anns) snap.Anns.Add(a.Copy());
            // 창을 열었다 취소하면 이 기록을 도로 빼는데, 그때 다시하기와 밀려난 가장 오래된 기록도 되살려야 한다
            pushRedo = redo.ToArray();
            pushDirty = dirty;
            pushTrimmed = null;
            undo.Push(snap);
            while (undo.Count > 25)
            {
                // 가장 오래된 것을 버린다 (Stack은 밑을 못 빼므로 뒤집어 다시 쌓는다)
                var keep = undo.ToArray();
                pushTrimmed = keep[keep.Length - 1];
                undo.Clear();
                for (int i = keep.Length - 2; i >= 0; i--) undo.Push(keep[i]);
            }
            redo.Clear();
            dirty = true;
        }

        Snapshot[] pushRedo;
        Snapshot pushTrimmed;
        bool pushDirty;

        // 방금 한 Push를 없던 일로 — 취소한 창, 끌지 않은 클릭 등
        void CancelPush()
        {
            if (undo.Count > 0) undo.Pop();
            if (pushRedo != null)
            {
                redo.Clear();
                for (int i = pushRedo.Length - 1; i >= 0; i--) redo.Push(pushRedo[i]);
            }
            if (pushTrimmed != null)
            {
                var arr = undo.ToArray();
                undo.Clear();
                undo.Push(pushTrimmed);
                for (int i = arr.Length - 1; i >= 0; i--) undo.Push(arr[i]);
            }
            pushRedo = null; pushTrimmed = null;
            dirty = pushDirty;
        }

        void Undo()
        {
            if (drawing) return;          // 칠하는 중에 되돌리면 획이 남의 버퍼에 쓰게 된다
            if (undo.Count == 0) { Status(L.T("되돌릴 것이 없다", "Nothing to undo")); return; }
            var cur = new Snapshot { Doc = doc.Snapshot(), Anns = new List<Ann>(), Counter = counterNext };
            foreach (var a in anns) cur.Anns.Add(a.Copy());
            redo.Push(cur);
            Restore(undo.Pop());
        }

        void Redo()
        {
            if (drawing) return;
            if (redo.Count == 0) { Status(L.T("다시 할 것이 없다", "Nothing to redo")); return; }
            var cur = new Snapshot { Doc = doc.Snapshot(), Anns = new List<Ann>(), Counter = counterNext };
            foreach (var a in anns) cur.Anns.Add(a.Copy());
            undo.Push(cur);
            Restore(redo.Pop());
        }

        void Restore(Snapshot s)
        {
            Deselect();
            doc.Restore(s.Doc);
            counterNext = s.Counter;
            anns.Clear();
            inkLayer.Children.Clear();
            foreach (var a in s.Anns) { anns.Add(a); Rebuild(a); }
            RefreshBase(null);
        }

        // ================= 입력 =================

        Point ImgPt(MouseEventArgs e) { return e.GetPosition(inkLayer); }

        // 픽셀을 칠하거나 읽는 도구는 조정 레이어(픽셀 없음)에서 쓸 수 없다. 마스크 도구와 변형은 된다.
        bool PixelLayer(string what)
        {
            var lay = doc.Current;
            if (lay != null && lay.Adjust != null)
            {
                Status(L.F("조정 레이어에는 픽셀이 없다 — {0}은(는) 그림 레이어를 골라 써라. 지우개·복구로 마스크는 칠할 수 있다", "Adjustment layers have no pixels — select an image layer to use {0}. You can still paint the mask with Eraser or Restore.", what));
                return false;
            }
            return true;
        }

        static bool PaintsPixels(Tool t)
        {
            return t == Tool.Brush || t == Tool.Gradient || t == Tool.Clone || t == Tool.Heal || t == Tool.Liquify ||
                   t == Tool.Mosaic || t == Tool.Blur || t == Tool.MagicErase || t == Tool.BgErase;
        }

        void OnCanvasDown(object sender, MouseButtonEventArgs e)
        {
            var p = ImgPt(e);
            if (PaintsPixels(tool) && !PixelLayer(L.T("이 도구", "this tool"))) return;
            // 선택 도구로 가이드를 잡으면 가이드를 옮긴다
            if (tool == Tool.Select && guidesOn && GuideAt(p, out dragGuideV, out dragGuideI))
            {
                draggingGuide = true;
                viewport.CaptureMouse();
                return;
            }
            p = SnapPt(p);
            viewport.CaptureMouse();
            startPt = lastPt = p;
            drawing = true;

            switch (tool)
            {
                case Tool.Select:
                    {
                        // 글자를 두 번 누르면 바로 고칠 수 있게 연다
                        if (e.ClickCount == 2)
                        {
                            var dbl = HitTest(p);
                            if (dbl != null && dbl.Kind == Tool.Text)
                            { Select(dbl); FocusText(dbl); drawing = false; return; }
                        }
                        CommitText();
                        if (selected != null)
                        {
                            grabHandle = HandleAt(selected, p);
                            if (grabHandle >= 0) { Push(); return; }
                        }
                        var hit = HitTest(p);
                        if (hit != selected) { Select(hit); }
                        if (hit != null) Push();
                        break;
                    }

                case Tool.Marquee:
                    if (selShape == SelShape.Color)
                    {
                        drawing = false;
                        if (viewport.IsMouseCaptured) viewport.ReleaseMouseCapture();
                        ColorRangeClick(p);
                        break;
                    }
                    if (selShape == SelShape.Object)
                    {
                        drawing = false;
                        if (viewport.IsMouseCaptured) viewport.ReleaseMouseCapture();
                        ObjectClick(p);
                        break;
                    }
                    if (selShape == SelShape.Lasso || selShape == SelShape.Magnetic)
                    {
                        lassoPts = new List<Point> { p };
                        if (selShape == SelShape.Magnetic)
                        {
                            doc.CompositeAll();
                            edges = new EdgeMap(doc.Comp);      // 이 획 동안 쓸 윤곽 지도
                        }
                    }
                    DrawSelectionPreview(new Rect(p, p));
                    break;

                case Tool.Text:
                    Push();
                    CommitText();
                    current = new Ann
                    {
                        Kind = Tool.Text, A = p, Color = ink, FontSize = textSize, Text = "",
                        Font = textFont, Bold = textBold, Italic = textItalic,
                        Underline = textUnderline, Outline = textOutline, BoxBg = textBoxBg
                    };
                    anns.Add(current);
                    Rebuild(current);
                    FocusText(current);
                    drawing = false;
                    break;

                case Tool.Counter:
                    Push();
                    var cn = new Ann
                    {
                        Kind = Tool.Counter, A = p, Color = ink,
                        FontSize = counterSize, Number = counterNext++
                    };
                    anns.Add(cn);
                    Rebuild(cn);
                    drawing = false;
                    break;

                case Tool.Pen:
                case Tool.Highlight:
                    Push();
                    current = new Ann
                    {
                        Kind = tool, Color = ink,
                        Thickness = tool == Tool.Highlight ? Math.Max(10, thickness) : thickness,
                        Dash = tool == Tool.Highlight ? LineDash.Solid : lineDash,
                        Pts = new List<Point> { p }
                    };
                    anns.Add(current);
                    Rebuild(current);
                    break;

                case Tool.Rect:
                case Tool.Ellipse:
                case Tool.Arrow:
                case Tool.Line:
                    Push();
                    current = new Ann
                    {
                        Kind = tool, A = p, B = p, Color = ink, Thickness = thickness,
                        Head = arrowHead, Dash = lineDash, Shadow = shapeShadow
                    };
                    anns.Add(current);
                    Rebuild(current);
                    break;

                case Tool.Mosaic:
                case Tool.Blur:
                    ShowMarquee(new Rect(p, p));
                    break;

                case Tool.MagicErase:
                    {
                        Push();
                        var lay = doc.Current;
                        int lx = (int)p.X - lay.X, ly = (int)p.Y - lay.Y;
                        Ops.MaskMagicErase(lay.PixelsRead, lay.EditMask(), lx, ly,
                                           tolerance, contiguous, Settings.Current.featherPx);
                        RefreshBase(null);
                        drawing = false;
                        Status(L.T("마술봉으로 지웠다 — 원본은 남아 있다. 복구 브러시로 되살릴 수 있다", "Erased with Magic Wand — the original is kept. Use Restore to bring it back"));
                        break;
                    }

                case Tool.BgErase:
                case Tool.Erase:
                case Tool.Restore:
                    Push();
                    Dab(p, p);
                    break;

                case Tool.Heal:
                    healPts = new List<Point> { p };
                    ShowHealTrail();
                    break;

                case Tool.Brush:
                    {
                        var lay = doc.Current;
                        if (lay == null) { drawing = false; break; }
                        Push();
                        strokeSnap = lay.PixelsRead.Clone();
                        strokeCov = new byte[lay.W * lay.H];
                        gradCover = SelectionCoverFor(lay);
                        PaintDab(p, p);
                    }
                    break;

                case Tool.Gradient:
                    {
                        var lay = doc.Current;
                        if (lay == null) { drawing = false; break; }
                        Push();
                        gradSnap = lay.PixelsRead.Clone();
                        gradCover = SelectionCoverFor(lay);
                        gradStart = p;
                        if (gradLine == null)
                        {
                            gradLine = new Line { Stroke = Brushes.White, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 4, 3 }, IsHitTestVisible = false };
                            uiLayer.Children.Add(gradLine);
                        }
                        gradLine.X1 = gradLine.X2 = p.X; gradLine.Y1 = gradLine.Y2 = p.Y;
                        gradLine.Visibility = Visibility.Visible;
                    }
                    break;

                case Tool.Liquify:
                    {
                        var lay = doc.Current;
                        if (lay == null) { drawing = false; break; }
                        Push();
                        if (warpMode == 2)
                        {
                            strokeSnap = lay.PixelsRead.Clone();
                            blurSnap = new Canvas32(lay.W, lay.H);
                            Filters.Gaussian(blurBrushRadius)(strokeSnap, blurSnap);
                            strokeCov = new byte[lay.W * lay.H];
                            StrokePaint(blurSnap, 0, 0, warpStrength / 100.0, p, p);
                        }
                        else
                        {
                            warp = new WarpStroke(lay.EditPixels(), warpMode == 1, brushSize * 2, hardness / 100.0, warpStrength / 100.0);
                            warp.Append(new Point(p.X - lay.X, p.Y - lay.Y));
                        }
                    }
                    break;

                case Tool.Clone:
                    if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0)
                    {
                        cloneSrc = new Point(Math.Round(p.X), Math.Round(p.Y));
                        cloneOffset = null;
                        drawing = false;
                        MoveCloneMark(p);
                        BuildOptions();
                        Status(L.T("가져올 곳을 찍었다 — 이제 고칠 자리를 칠해라", "Source set — now paint over the area to fix"));
                        break;
                    }
                    if (cloneSrc == null) { drawing = false; Status(L.T("Alt+클릭으로 가져올 곳부터 찍어라", "Alt+click to set the source first")); break; }
                    {
                        var lay = doc.Current;
                        if (lay == null) { drawing = false; break; }
                        cloneStroke = cloneAligned && cloneOffset != null
                            ? cloneOffset.Value
                            : new Vector(Math.Round(cloneSrc.Value.X - p.X), Math.Round(cloneSrc.Value.Y - p.Y));
                        cloneOffset = cloneStroke;
                        strokeSnap = lay.PixelsRead.Clone();
                        strokeCov = new byte[lay.W * lay.H];
                        Push();
                        CloneDab(p, p);
                    }
                    break;

                case Tool.Transform:
                    BeginTransformIfNeeded();
                    XfDown(p);
                    break;

                case Tool.Picker:
                    if (doc.Comp.In((int)p.X, (int)p.Y))
                    {
                        int v = doc.Comp.At((int)p.X, (int)p.Y);
                        ink = Color.FromRgb(Canvas32.R(v), Canvas32.G(v), Canvas32.B(v));
                        BuildOptions();
                        Status(L.F("#{0:X2}{1:X2}{2:X2} 을 집었다", "Picked #{0:X2}{1:X2}{2:X2}", ink.R, ink.G, ink.B));
                    }
                    drawing = false;
                    break;
            }
        }

        void OnCanvasMove(object sender, MouseEventArgs e)
        {
            var p = ImgPt(e);
            if (draggingGuide)
            {
                var g = dragGuideV ? vGuides : hGuides;
                g[dragGuideI] = Math.Round(dragGuideV ? p.X : p.Y);
                DrawGuides();
                Status((dragGuideV ? L.T("세로 가이드 x = ", "Vertical guide x = ") : L.T("가로 가이드 y = ", "Horizontal guide y = ")) + (int)g[dragGuideI] + L.T("   판 밖으로 끌면 지운다", "   drag off the canvas to remove"));
                return;
            }
            MoveBrushRing(p);

            if (!drawing)
            {
                HoverCursor(p);
                bool gv; int gi;
                if (tool == Tool.Select && guidesOn && GuideAt(p, out gv, out gi))
                    viewport.Cursor = gv ? Cursors.SizeWE : Cursors.SizeNS;
                if (doc.Comp.In((int)p.X, (int)p.Y))
                    Status(doc.W + " × " + doc.H + "    " + (int)p.X + ", " + (int)p.Y);
                return;
            }

            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            p = SnapPt(p);

            switch (tool)
            {
                case Tool.Select:
                    if (selected == null) break;
                    if (grabHandle >= 0) ResizeSelected(grabHandle, p);
                    else
                    {
                        var d = p - lastPt;
                        MoveAnn(selected, d.X, d.Y);
                    }
                    Rebuild(selected);
                    DrawHandles();
                    break;

                case Tool.Marquee:
                    if (lassoPts != null)
                    {
                        var prev = lassoPts[lassoPts.Count - 1];
                        if (Math.Abs(p.X - prev.X) + Math.Abs(p.Y - prev.Y) >= 3)
                        {
                            var next = selShape == SelShape.Magnetic && edges != null
                                ? edges.Snap(prev, p, 12)
                                : p;
                            lassoPts.Add(next);
                            DrawSelectionPreview(Rect.Empty);
                        }
                    }
                    else DrawSelectionPreview(Norm(startPt, p, shift));
                    break;

                case Tool.Pen:
                case Tool.Highlight:
                    if (current != null && (Math.Abs(p.X - lastPt.X) + Math.Abs(p.Y - lastPt.Y) > 1.2))
                    {
                        current.Pts.Add(p);
                        Rebuild(current);
                    }
                    break;

                case Tool.Rect:
                case Tool.Ellipse:
                    if (current != null) { var r = Norm(startPt, p, shift); current.A = r.TopLeft; current.B = r.BottomRight; Rebuild(current); }
                    break;

                case Tool.Arrow:
                case Tool.Line:
                    if (current != null)
                    {
                        current.B = shift ? Snap45(startPt, p) : p;
                        Rebuild(current);
                    }
                    break;

                case Tool.Mosaic:
                case Tool.Blur:
                    ShowMarquee(Norm(startPt, p, false));
                    break;

                case Tool.BgErase:
                case Tool.Erase:
                case Tool.Restore:
                    Dab(lastPt, p);
                    break;

                case Tool.Heal:
                    if (healPts != null && (Math.Abs(p.X - lastPt.X) + Math.Abs(p.Y - lastPt.Y) > 1))
                    {
                        healPts.Add(p);
                        ShowHealTrail();
                    }
                    break;

                case Tool.Clone:
                    if (strokeSnap != null) CloneDab(lastPt, p);
                    break;

                case Tool.Brush:
                    if (strokeSnap != null) PaintDab(lastPt, p);
                    break;

                case Tool.Gradient:
                    if (gradSnap != null)
                    {
                        if (shift) p = Snap45(gradStart, p);
                        gradLine.X2 = p.X; gradLine.Y2 = p.Y;
                        RenderGradient(p);
                    }
                    break;

                case Tool.Liquify:
                    {
                        var lay = doc.Current;
                        if (lay == null) break;
                        if (warp != null)
                        {
                            var r = warp.Append(new Point(p.X - lay.X, p.Y - lay.Y));
                            if (!r.IsEmpty)
                            {
                                lay.EditPixels(r);           // 효과가 붙어 있으면 이 자리만 다시 그리게
                                r.Offset(lay.X, lay.Y);
                                RefreshBase(WithFx(lay, r));
                                dirty = true;
                            }
                        }
                        else if (blurSnap != null) StrokePaint(blurSnap, 0, 0, warpStrength / 100.0, lastPt, p);
                    }
                    break;

                case Tool.Transform:
                    XfMove(p);
                    break;
            }
            lastPt = p;
        }

        void OnCanvasUp(object sender, MouseButtonEventArgs e)
        {
            if (viewport.IsMouseCaptured) viewport.ReleaseMouseCapture();
            if (draggingGuide)
            {
                draggingGuide = false;
                var g = dragGuideV ? vGuides : hGuides;
                double v = g[dragGuideI];
                if (v < 0 || v > (dragGuideV ? doc.W : doc.H)) { g.RemoveAt(dragGuideI); Status(L.T("가이드를 지웠다", "Guide removed")); }
                DrawGuides();
                return;
            }
            if (!drawing) return;
            drawing = false;
            var p = SnapPt(ImgPt(e));

            switch (tool)
            {
                case Tool.Select:
                    grabHandle = -1;
                    break;

                case Tool.Marquee:
                    CommitSelection(Norm(startPt, p, (Keyboard.Modifiers & ModifierKeys.Shift) != 0));
                    break;

                case Tool.Transform:
                    xfGrab = -1;
                    xfQuadGrab = -1;
                    break;

                case Tool.Heal:
                    FinishHeal();
                    break;

                case Tool.Clone:
                    strokeSnap = null;
                    strokeCov = null;
                    break;

                case Tool.Brush:
                    strokeSnap = null;
                    strokeCov = null;
                    gradCover = null;
                    dirty = true;
                    break;

                case Tool.Gradient:
                    if (gradLine != null) gradLine.Visibility = Visibility.Collapsed;
                    if (gradSnap != null && (p - gradStart).Length < 2)
                    {
                        // 끌지 않고 누르기만 했다 — 없던 일로
                        var lay = doc.Current;
                        if (lay != null) { Array.Copy(gradSnap.P, lay.EditPixels().P, gradSnap.P.Length); RefreshBase(null); }
                        CancelPush();
                    }
                    else if (gradSnap != null) { dirty = true; Status(L.T("그라디언트를 칠했다", "Gradient applied")); }
                    gradSnap = null;
                    gradCover = null;
                    break;

                case Tool.Liquify:
                    warp = null;
                    blurSnap = null;
                    strokeSnap = null;
                    strokeCov = null;
                    break;

                case Tool.Mosaic:
                case Tool.Blur:
                    {
                        var r = Norm(startPt, p, false);
                        HideMarquee();
                        if (r.Width >= 2 && r.Height >= 2)
                        {
                            Push();
                            var rr = new D.Rectangle((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height);
                            var lay = doc.Current;
                            var lrr = new D.Rectangle(rr.X - lay.X, rr.Y - lay.Y, rr.Width, rr.Height);
                            if (tool == Tool.Mosaic) Ops.Pixelate(lay.EditPixels(), lrr, mosaicCell);
                            else Ops.Blur(lay.EditPixels(), lrr, blurStrength);
                            RefreshBase(WithFx(lay, rr));
                        }
                        break;
                    }

                case Tool.Rect:
                case Tool.Ellipse:
                case Tool.Arrow:
                case Tool.Line:
                    // 실수로 찍기만 한 경우 빈 도형을 남기지 않는다
                    if (current != null && current.Bounds.Width < 3 && current.Bounds.Height < 3)
                    {
                        anns.Remove(current);
                        inkLayer.Children.Remove(current.Visual);
                        CancelPush();
                    }
                    else if (current != null) selected = current;
                    break;
            }
            current = null;
        }

        void OnWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            e.Handled = true;
            Zoom(e.Delta > 0 ? 1.15 : 1 / 1.15);
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

            // 텍스트를 입력하는 중에는 단축키를 가로채지 않는다. Ctrl 조합도 저장 말고는 글상자에 맡긴다
            // (안 그러면 글자를 쓰다 Ctrl+B·U·M이 보정 창을 띄운다).
            if (Keyboard.FocusedElement is TextBox)
            {
                if (e.Key == Key.Escape) { CommitText(); Focus(); e.Handled = true; }
                else if (ctrl && e.Key == Key.S) { SaveOut(); e.Handled = true; }
                return;
            }

            // Alt를 떼면 윈도가 창 메뉴 모드로 들어가 다음 키를 삼킨다. Alt+클릭(물체·색상 범위 빼기, 도장 원본)을
            // 쓰는 도구라 Alt 단독 입력은 여기서 막는다.
            if (e.Key == Key.System && (e.SystemKey == Key.LeftAlt || e.SystemKey == Key.RightAlt)) { e.Handled = true; return; }

            if (ctrl)
            {
                switch (e.Key)
                {
                    case Key.Z: Undo(); e.Handled = true; return;
                    case Key.Y: Redo(); e.Handled = true; return;
                    case Key.C: CopyOut(); e.Handled = true; return;
                    case Key.S: SaveOut(); e.Handled = true; return;
                    case Key.V: PasteAsLayer(); e.Handled = true; return;
                    case Key.O: OpenAsLayer(); e.Handled = true; return;
                    case Key.L:
                        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) ShowLevels(); else ShowAdjust();
                        e.Handled = true; return;
                    case Key.M: ShowCurves(); e.Handled = true; return;
                    case Key.R: ToggleRulers(); e.Handled = true; return;
                    case Key.OemSemicolon: ToggleGuides(); e.Handled = true; return;
                    case Key.U: ShowHueSat(); e.Handled = true; return;
                    case Key.B:
                        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) ShowBlackWhite(); else ShowBalance();
                        e.Handled = true; return;
                    case Key.OemPlus: case Key.Add: Zoom(1.25); e.Handled = true; return;
                    case Key.OemMinus: case Key.Subtract: Zoom(1 / 1.25); e.Handled = true; return;
                    case Key.D0: case Key.NumPad0: SetZoom(1); e.Handled = true; return;
                    case Key.D: ClearSelection(); e.Handled = true; return;
                    case Key.A:
                        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) ShowCameraRaw(); else SelectAll();
                        e.Handled = true; return;
                    case Key.I:
                        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) { InvertSelection(); e.Handled = true; }
                        return;
                }
                return;
            }

            switch (e.Key)
            {
                case Key.V: SelectTool(Tool.Select); break;
                case Key.C: SelectTool(Tool.Marquee); break;
                case Key.R: SelectTool(Tool.Rect); break;
                case Key.O: SelectTool(Tool.Ellipse); break;
                case Key.A: SelectTool(Tool.Arrow); break;
                case Key.L: SelectTool(Tool.Line); break;
                case Key.P: SelectTool(Tool.Pen); break;
                case Key.H: SelectTool(Tool.Highlight); break;
                case Key.T: SelectTool(Tool.Text); break;
                case Key.N: SelectTool(Tool.Counter); break;
                case Key.M: SelectTool(Tool.Mosaic); break;
                case Key.B: SelectTool(Tool.Blur); break;
                case Key.W: SelectTool(Tool.MagicErase); break;
                case Key.G: SelectTool(Tool.BgErase); break;
                case Key.E: SelectTool(Tool.Erase); break;
                case Key.Q: SelectTool(Tool.Restore); break;
                case Key.Y: SelectTool(Tool.Transform); break;
                case Key.J: SelectTool(Tool.Heal); break;
                case Key.S: SelectTool(Tool.Clone); break;
                case Key.U: SelectTool(Tool.Liquify); break;
                case Key.K: SelectTool(Tool.Brush); break;
                case Key.D: SelectTool(Tool.Gradient); break;
                case Key.I: SelectTool(Tool.Picker); break;

                case Key.Left: case Key.Right: case Key.Up: case Key.Down:
                    if (tool == Tool.Transform)
                    {
                        BeginTransformIfNeeded();
                        int step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 10 : 1;
                        if (e.Key == Key.Left) xfTx -= step;
                        else if (e.Key == Key.Right) xfTx += step;
                        else if (e.Key == Key.Up) xfTy -= step;
                        else xfTy += step;
                        XfRedraw();
                        break;
                    }
                    return;

                case Key.Delete: case Key.Back:
                    if (selection != null && selected == null)
                    {
                        // Shift를 같이 누르면 지우는 대신 둘레로 채운다
                        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) ContentFillSelection();
                        else EraseSelection(true);
                        break;
                    }
                    if (selected != null)
                    {
                        Push();
                        inkLayer.Children.Remove(selected.Visual);
                        anns.Remove(selected);
                        Deselect();
                    }
                    break;

                case Key.Enter:
                    if (xfOn) { CommitTransform(); break; }
                    if (tool == Tool.Marquee && selection != null) CropToSelection();
                    break;

                case Key.Escape:
                    if (xfOn) { CancelTransform(); break; }
                    if (selection != null) ClearSelection();
                    else if (selected != null) Deselect();
                    else Close();
                    break;

                default: return;
            }
            e.Handled = true;
        }

        void OnDrop(object sender, DragEventArgs e)
        {
            try
            {
                if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                int added = 0;
                foreach (var f in files)
                {
                    if (Psd.IsPsd(f)) { OpenPsd(f, Status); continue; }   // 레이어째 새 창으로
                    if (!Cap.IsImageFile(f)) continue;
                    try
                    {
                        AddAsLayer(Cap.Load(f), System.IO.Path.GetFileNameWithoutExtension(f));
                        added++;
                    }
                    catch (Exception ex) { Status(ex.Message); }
                }
                if (added > 1) Status(L.F("{0}장을 레이어로 올렸다", "Added {0} images as layers", added));
            }
            catch { }
        }

        // ================= 기하 =================

        static Rect Norm(Point a, Point b, bool square)
        {
            double x = Math.Min(a.X, b.X), y = Math.Min(a.Y, b.Y);
            double w = Math.Abs(b.X - a.X), h = Math.Abs(b.Y - a.Y);
            if (square)
            {
                double d = Math.Max(w, h);
                if (b.X < a.X) x = a.X - d;
                if (b.Y < a.Y) y = a.Y - d;
                w = d; h = d;
            }
            return new Rect(x, y, w, h);
        }

        static Point Snap45(Point a, Point b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double ang = Math.Atan2(dy, dx);
            double step = Math.PI / 4;
            ang = Math.Round(ang / step) * step;
            double len = Math.Sqrt(dx * dx + dy * dy);
            return new Point(a.X + Math.Cos(ang) * len, a.Y + Math.Sin(ang) * len);
        }

        static void MoveAnn(Ann a, double dx, double dy)
        {
            a.A = new Point(a.A.X + dx, a.A.Y + dy);
            a.B = new Point(a.B.X + dx, a.B.Y + dy);
            if (a.Pts != null)
                for (int i = 0; i < a.Pts.Count; i++)
                    a.Pts[i] = new Point(a.Pts[i].X + dx, a.Pts[i].Y + dy);
        }

        // ================= 브러시 표시 =================

        void EnsureBrushRing()
        {
            bool needed = tool == Tool.BgErase || tool == Tool.Erase || tool == Tool.Restore ||
                          tool == Tool.Heal || tool == Tool.Clone || tool == Tool.Liquify || tool == Tool.Brush;
            if (brushRing == null)
            {
                brushRing = new Ellipse
                {
                    Stroke = Theme.BrAccent,
                    StrokeThickness = 1.5,
                    Fill = Theme.Alpha(Theme.Accent, 0x18),
                    IsHitTestVisible = false
                };
                uiLayer.Children.Add(brushRing);
            }
            brushRing.Visibility = needed ? Visibility.Visible : Visibility.Collapsed;
            brushRing.Width = brushRing.Height = brushSize * 2;
        }

        void MoveBrushRing(Point p)
        {
            MoveCloneMark(p);
            if (brushRing == null || brushRing.Visibility != Visibility.Visible) return;
            Canvas.SetLeft(brushRing, p.X - brushSize);
            Canvas.SetTop(brushRing, p.Y - brushSize);
        }

        // 두 점 사이를 촘촘히 메워 찍는다. 빠르게 끌어도 점선처럼 끊기지 않는다.
        // 칠하는 대상은 픽셀이 아니라 현재 레이어의 마스크다 — 원본은 건드리지 않는다.
        void Dab(Point from, Point to)
        {
            var lay = doc.Current;
            if (lay == null) return;
            // 이번에 칠할 자리만 효과를 다시 그리게 알려준다
            var mask = lay.EditMask(new D.Rectangle(
                (int)Math.Min(from.X, to.X) - lay.X - brushSize - 1, (int)Math.Min(from.Y, to.Y) - lay.Y - brushSize - 1,
                (int)Math.Abs(to.X - from.X) + brushSize * 2 + 3, (int)Math.Abs(to.Y - from.Y) + brushSize * 2 + 3));
            var pix = lay.PixelsRead;
            bool erase = tool != Tool.Restore;

            double dist = Math.Sqrt(Math.Pow(to.X - from.X, 2) + Math.Pow(to.Y - from.Y, 2));
            int steps = Math.Max(1, (int)(dist / Math.Max(1, brushSize * 0.22)));
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;

            for (int i = 0; i <= steps; i++)
            {
                double t = steps == 0 ? 0 : (double)i / steps;
                int x = (int)Math.Round(from.X + (to.X - from.X) * t);
                int y = (int)Math.Round(from.Y + (to.Y - from.Y) * t);
                int lx = x - lay.X, ly = y - lay.Y;

                if (tool == Tool.BgErase)
                    Ops.MaskBackgroundErase(pix, mask, lx, ly, brushSize, tolerance, hardness, true);
                else
                    Ops.MaskBrush(mask, pix.W, pix.H, lx, ly, brushSize, hardness, erase);

                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            }
            var r = new D.Rectangle(
                (int)minX - brushSize - 2, (int)minY - brushSize - 2,
                (int)(maxX - minX) + brushSize * 2 + 4, (int)(maxY - minY) + brushSize * 2 + 4);
            RefreshBase(WithFx(lay, r));
        }

        // 효과가 붙은 레이어는 고친 자리에서 효과가 닿는 거리만큼 화면도 다시 그려야 한다
        static D.Rectangle WithFx(Layer lay, D.Rectangle r)
        {
            if (lay.Fx == null || !lay.Fx.Any) return r;
            int k = Fx.Reach(lay.Fx);
            r.Inflate(k, k);
            return r;
        }

        // ================= 선택과 핸들 =================

        // 위에 있는 것부터 훑는다.
        // 펜·형광펜·직선처럼 가늘고 긴 개체를 사각형 범위로만 판정하면, 획 근처가 아닌
        // 빈 공간을 눌러도 잡혀서 아래 개체를 고를 수 없다. 그런 것은 선까지의 거리로 본다.
        Ann HitTest(Point p)
        {
            for (int i = anns.Count - 1; i >= 0; i--)
            {
                var a = anns[i];
                var probe = Unrotate(a, p);
                var b = a.Bounds;
                b.Inflate(Math.Max(4, a.Thickness), Math.Max(4, a.Thickness));
                if (!b.Contains(probe)) continue;
                var p0 = probe;

                double slack = Math.Max(6, a.Thickness * 0.5 + 5);

                if (a.Pts != null)
                {
                    for (int j = 1; j < a.Pts.Count; j++)
                        if (DistToSegment(p0, a.Pts[j - 1], a.Pts[j]) <= slack) return a;
                    if (a.Pts.Count == 1 && DistToSegment(p0, a.Pts[0], a.Pts[0]) <= slack) return a;
                    continue;
                }
                if (a.Kind == Tool.Line || a.Kind == Tool.Arrow)
                {
                    if (DistToSegment(p0, a.A, a.B) <= slack) return a;
                    continue;
                }
                return a;
            }
            return null;
        }

        // 돌아간 개체를 다룰 땐 마우스 위치를 반대로 돌려 원래 좌표계로 되돌려 놓고 따진다.
        static Point Unrotate(Ann a, Point p)
        {
            if (Math.Abs(a.Angle) < 0.01) return p;
            var r = a.Bounds;
            double cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            double rad = -a.Angle * Math.PI / 180.0;
            double dx = p.X - cx, dy = p.Y - cy;
            return new Point(cx + dx * Math.Cos(rad) - dy * Math.Sin(rad),
                             cy + dx * Math.Sin(rad) + dy * Math.Cos(rad));
        }

        static Point Rotate(Point p, double cx, double cy, double deg)
        {
            double rad = deg * Math.PI / 180.0;
            double dx = p.X - cx, dy = p.Y - cy;
            return new Point(cx + dx * Math.Cos(rad) - dy * Math.Sin(rad),
                             cy + dx * Math.Sin(rad) + dy * Math.Cos(rad));
        }

        static double DistToSegment(Point p, Point a, Point b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double len2 = dx * dx + dy * dy;
            double t = len2 <= 0.0001 ? 0 : ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2;
            t = Math.Max(0, Math.Min(1, t));
            double cx = a.X + dx * t, cy = a.Y + dy * t;
            return Math.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy));
        }

        void Select(Ann a)
        {
            selected = a;
            DrawHandles();
            if (tool == Tool.Select) BuildOptions();
        }

        void Deselect()
        {
            selected = null;
            grabHandle = -1;
            ClearHandles();
            if (tool == Tool.Select) BuildOptions();
        }

        readonly List<Shape> handleDots = new List<Shape>();
        Rectangle selOutline;
        Point rotateKnob;

        void ClearHandles()
        {
            foreach (var h in handleDots) uiLayer.Children.Remove(h);
            handleDots.Clear();
            if (selOutline != null) { uiLayer.Children.Remove(selOutline); selOutline = null; }
        }

        static Point[] HandlePts(Rect r)
        {
            return new[]
            {
                new Point(r.Left, r.Top), new Point(r.Left + r.Width / 2, r.Top), new Point(r.Right, r.Top),
                new Point(r.Right, r.Top + r.Height / 2),
                new Point(r.Right, r.Bottom), new Point(r.Left + r.Width / 2, r.Bottom), new Point(r.Left, r.Bottom),
                new Point(r.Left, r.Top + r.Height / 2)
            };
        }

        void DrawHandles()
        {
            ClearHandles();
            if (selected == null) return;
            var r = selected.Bounds;

            selOutline = new Rectangle
            {
                Width = Math.Max(1, r.Width), Height = Math.Max(1, r.Height),
                Stroke = Theme.BrAccent, StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                IsHitTestVisible = false
            };
            Canvas.SetLeft(selOutline, r.X); Canvas.SetTop(selOutline, r.Y);
            if (Math.Abs(selected.Angle) > 0.01)
            {
                selOutline.RenderTransformOrigin = new Point(0.5, 0.5);
                selOutline.RenderTransform = new RotateTransform(selected.Angle);
            }
            uiLayer.Children.Add(selOutline);

            // 회전 손잡이 — 위쪽 가운데에서 조금 떨어진 자리
            double ccx = r.X + r.Width / 2, ccy = r.Y + r.Height / 2;
            var spin = Rotate(new Point(ccx, r.Y - 22 / Math.Max(0.25, zoom.ScaleX)), ccx, ccy, selected.Angle);
            var knob = new Ellipse
            {
                Width = 11, Height = 11,
                Fill = Theme.BrAccent,
                Stroke = new SolidColorBrush(Theme.Bg), StrokeThickness = 1.5,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(knob, spin.X - 5.5); Canvas.SetTop(knob, spin.Y - 5.5);
            uiLayer.Children.Add(knob);
            handleDots.Add(knob);
            rotateKnob = spin;

            // 펜·형광펜은 점마다 모양이 달라 크기 조절을 주지 않는다
            if (selected.Pts != null || selected.Kind == Tool.Text || selected.Kind == Tool.Counter) return;

            foreach (var p0 in HandlePts(r))
            {
                var p = Rotate(p0, ccx, ccy, selected.Angle);
                var d = new Rectangle
                {
                    Width = 9, Height = 9,
                    Fill = Theme.BrAccent,
                    Stroke = new SolidColorBrush(Theme.Bg), StrokeThickness = 1.5,
                    RadiusX = 2, RadiusY = 2,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(d, p.X - 4.5); Canvas.SetTop(d, p.Y - 4.5);
                uiLayer.Children.Add(d);
                handleDots.Add(d);
            }
        }

        int HandleAt(Ann a, Point p)
        {
            double grab = 9 / Math.Max(0.25, zoom.ScaleX);
            // 8번은 회전 손잡이. 모든 개체가 가진다.
            if (Math.Abs(p.X - rotateKnob.X) <= grab && Math.Abs(p.Y - rotateKnob.Y) <= grab) return 8;

            if (a.Pts != null || a.Kind == Tool.Text || a.Kind == Tool.Counter) return -1;
            var r = a.Bounds;
            double ccx = r.X + r.Width / 2, ccy = r.Y + r.Height / 2;
            var pts = HandlePts(r);
            for (int i = 0; i < 8; i++)
            {
                var h = Rotate(pts[i], ccx, ccy, a.Angle);
                if (Math.Abs(p.X - h.X) <= grab && Math.Abs(p.Y - h.Y) <= grab) return i;
            }
            return -1;
        }

        void ResizeSelected(int h, Point p)
        {
            var r0 = selected.Bounds;
            if (h == 8)
            {
                // 회전: 가운데에서 마우스를 향한 각도. Shift를 누르면 15도 단위.
                double ccx = r0.X + r0.Width / 2, ccy = r0.Y + r0.Height / 2;
                double deg = Math.Atan2(p.Y - ccy, p.X - ccx) * 180.0 / Math.PI + 90;
                if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) deg = Math.Round(deg / 15) * 15;
                selected.Angle = Math.Round(deg, 1);
                Status(L.T("회전  ", "Rotate  ") + (int)selected.Angle + "°");
                return;
            }
            p = Unrotate(selected, p);
            var r = selected.Bounds;
            double l = r.Left, t = r.Top, rt = r.Right, b = r.Bottom;
            if (h == 0 || h == 7 || h == 6) l = p.X;
            if (h == 2 || h == 3 || h == 4) rt = p.X;
            if (h == 0 || h == 1 || h == 2) t = p.Y;
            if (h == 4 || h == 5 || h == 6) b = p.Y;

            if (selected.Kind == Tool.Arrow || selected.Kind == Tool.Line)
            {
                // 선은 끝점을 직접 옮기는 게 자연스럽다
                if (h == 0 || h == 1 || h == 7) selected.A = p;
                else selected.B = p;
                return;
            }
            selected.A = new Point(Math.Min(l, rt), Math.Min(t, b));
            selected.B = new Point(Math.Max(l, rt), Math.Max(t, b));
        }

        void ApplyToSelected()
        {
            if (selected == null) return;
            selected.Color = ink;
            if (selected.Kind == Tool.Counter) selected.FontSize = counterSize;
            else if (selected.Kind != Tool.Text) selected.Thickness = thickness;
            Rebuild(selected);
        }

        // ================= 개체 조작 =================

        void RebuildAll()
        {
            inkLayer.Children.Clear();
            foreach (var a in anns) { a.Visual = null; Rebuild(a); }
            DrawHandles();
        }

        void Duplicate(Ann a)
        {
            Push();
            var c = a.Copy();
            MoveAnn(c, 16, 16);
            if (c.Kind == Tool.Counter) c.Number = counterNext++;
            anns.Add(c);
            Rebuild(c);
            Select(c);
            Status(L.T("복제했다", "Duplicated"));
        }

        void DeleteAnn(Ann a)
        {
            Push();
            inkLayer.Children.Remove(a.Visual);
            anns.Remove(a);
            if (selected == a) Deselect();
        }

        void Reorder(Ann a, bool toFront)
        {
            Push();
            anns.Remove(a);
            if (toFront) anns.Add(a); else anns.Insert(0, a);
            RebuildAll();
        }

        // 어느 도구에 있든 개체를 오른쪽 클릭하면 그 개체를 고르고 선택 도구로 넘어간다.
        void OnCanvasRightUp(object sender, MouseButtonEventArgs e)
        {
            var p = ImgPt(e);
            var hit = HitTest(p);
            if (hit == null) return;

            if (tool != Tool.Select) SelectTool(Tool.Select);
            Select(hit);
            ShowObjectMenu(hit);
            e.Handled = true;
        }

        void ShowObjectMenu(Ann a)
        {
            var m = new ContextMenu();

            if (a.Kind == Tool.Arrow)
            {
                m.Items.Add(MI(L.T("촉 — 끝에만  →", "Head — End Only  →"), delegate { SetHead(a, ArrowHead.End); }));
                m.Items.Add(MI(L.T("촉 — 시작에만  ←", "Head — Start Only  ←"), delegate { SetHead(a, ArrowHead.Start); }));
                m.Items.Add(MI(L.T("촉 — 양쪽  ↔", "Head — Both Ends  ↔"), delegate { SetHead(a, ArrowHead.Both); }));
                m.Items.Add(new Separator());
            }

            if (a.Kind == Tool.Rect || a.Kind == Tool.Ellipse || a.Kind == Tool.Line
                || a.Kind == Tool.Arrow || a.Kind == Tool.Pen)
            {
                m.Items.Add(MI(L.T("실선", "Solid"), delegate { SetDash(a, LineDash.Solid); }));
                m.Items.Add(MI(L.T("파선  ----", "Dashed  ----"), delegate { SetDash(a, LineDash.Dash); }));
                m.Items.Add(MI(L.T("점선  ·····", "Dotted  ·····"), delegate { SetDash(a, LineDash.Dot); }));
                m.Items.Add(MI(L.T("일점쇄선  -·-·-", "Dash-dot  -·-·-"), delegate { SetDash(a, LineDash.DashDot); }));
                m.Items.Add(new Separator());
            }

            if (a.Kind == Tool.Text)
            {
                m.Items.Add(MI(L.T("글자 고치기", "Edit Text"), delegate { FocusText(a); }));
                m.Items.Add(new Separator());
            }

            m.Items.Add(MI(L.T("맨 앞으로", "Bring to Front"), delegate { Reorder(a, true); }));
            m.Items.Add(MI(L.T("맨 뒤로", "Send to Back"), delegate { Reorder(a, false); }));
            m.Items.Add(MI(L.T("복제", "Duplicate"), delegate { Duplicate(a); }));
            m.Items.Add(new Separator());
            m.Items.Add(MI(L.T("삭제   Delete", "Delete   Delete"), delegate { DeleteAnn(a); }));

            m.PlacementTarget = this;
            m.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            m.IsOpen = true;
        }

        void SetHead(Ann a, ArrowHead h) { Push(); a.Head = h; Rebuild(a); BuildOptions(); }
        void SetDash(Ann a, LineDash d) { Push(); a.Dash = d; Rebuild(a); BuildOptions(); }

        // ================= 주석 그리기 =================

        void Rebuild(Ann a)
        {
            if (a.Visual != null) inkLayer.Children.Remove(a.Visual);
            a.Visual = MakeVisual(a);
            if (a.Visual == null) return;
            ApplyShadow(a);
            ApplyRotation(a);
            inkLayer.Children.Add(a.Visual);
        }

        static void ApplyShadow(Ann a)
        {
            var el = a.Visual as FrameworkElement;
            if (el == null || !a.Shadow) return;
            el.Effect = new DropShadowEffect
            {
                BlurRadius = Math.Max(5, a.Thickness * 2.4),
                ShadowDepth = Math.Max(2, a.Thickness * 0.9),
                Direction = 315,
                Opacity = 0.5,
                Color = Colors.Black
            };
        }

        // 개체 가운데를 축으로 돌린다.
        //
        // 좌표계가 두 가지다. 사각형·타원·글자·번호는 Canvas.Left로 자리를 잡아 놓아서
        // 자기 안에서의 가운데(비율 0.5, 0.5)로 돌리면 되고, 직선·화살표·펜은 점 좌표를
        // 그대로 들고 있어서 캔버스 절대 좌표로 축을 잡아야 한다.
        static void ApplyRotation(Ann a)
        {
            var el = a.Visual as FrameworkElement;
            if (el == null) return;
            if (Math.Abs(a.Angle) < 0.01) { el.RenderTransform = null; return; }

            if (double.IsNaN(Canvas.GetLeft(el)))
            {
                var r = a.Bounds;
                el.RenderTransform = new RotateTransform(a.Angle,
                    r.X + r.Width / 2, r.Y + r.Height / 2);
            }
            else
            {
                el.RenderTransformOrigin = new Point(0.5, 0.5);
                el.RenderTransform = new RotateTransform(a.Angle);
            }
        }

        // 선 종류를 굵기에 비례한 간격으로 만든다. 굵은 선에 촘촘한 점선을 쓰면 그냥 실선으로 보인다.
        static void ApplyDash(Shape s, Ann a)
        {
            if (a.Dash == LineDash.Solid) return;
            s.StrokeDashCap = PenLineCap.Round;
            switch (a.Dash)
            {
                case LineDash.Dash: s.StrokeDashArray = new DoubleCollection { 3.2, 2.4 }; break;
                case LineDash.Dot: s.StrokeDashArray = new DoubleCollection { 0.01, 2.2 }; break;
                case LineDash.DashDot: s.StrokeDashArray = new DoubleCollection { 4, 2, 0.01, 2 }; break;
            }
        }

        UIElement MakeVisual(Ann a)
        {
            var brush = new SolidColorBrush(a.Color);
            switch (a.Kind)
            {
                case Tool.Rect:
                    {
                        var r = a.Bounds;
                        var el = new Rectangle
                        {
                            Width = Math.Max(1, r.Width), Height = Math.Max(1, r.Height),
                            Stroke = brush, StrokeThickness = a.Thickness,
                            Fill = Brushes.Transparent, IsHitTestVisible = false,
                            RadiusX = 2, RadiusY = 2
                        };
                        ApplyDash(el, a);
                        Canvas.SetLeft(el, r.X); Canvas.SetTop(el, r.Y);
                        return el;
                    }
                case Tool.Ellipse:
                    {
                        var r = a.Bounds;
                        var el = new Ellipse
                        {
                            Width = Math.Max(1, r.Width), Height = Math.Max(1, r.Height),
                            Stroke = brush, StrokeThickness = a.Thickness,
                            Fill = Brushes.Transparent, IsHitTestVisible = false
                        };
                        ApplyDash(el, a);
                        Canvas.SetLeft(el, r.X); Canvas.SetTop(el, r.Y);
                        return el;
                    }
                case Tool.Line:
                    {
                        var l = new Line
                        {
                            X1 = a.A.X, Y1 = a.A.Y, X2 = a.B.X, Y2 = a.B.Y,
                            Stroke = brush, StrokeThickness = a.Thickness,
                            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                            IsHitTestVisible = false
                        };
                        ApplyDash(l, a);
                        return l;
                    }
                case Tool.Arrow:
                    return MakeArrow(a, brush);

                case Tool.Pen:
                case Tool.Highlight:
                    {
                        var pl = new Polyline
                        {
                            Stroke = brush,
                            StrokeThickness = a.Thickness,
                            StrokeStartLineCap = a.Kind == Tool.Highlight ? PenLineCap.Flat : PenLineCap.Round,
                            StrokeEndLineCap = a.Kind == Tool.Highlight ? PenLineCap.Flat : PenLineCap.Round,
                            StrokeLineJoin = PenLineJoin.Round,
                            IsHitTestVisible = false,
                            // 형광펜은 곱하기 혼합 대신 반투명으로 근사한다. 밑의 글자가 비쳐 보인다.
                            Opacity = a.Kind == Tool.Highlight ? 0.38 : 1.0
                        };
                        foreach (var p in a.Pts) pl.Points.Add(p);
                        if (a.Kind == Tool.Pen) ApplyDash(pl, a);
                        return pl;
                    }
                case Tool.Text:
                    {
                        var tb = new TextBox
                        {
                            Text = a.Text,
                            Foreground = brush,
                            Background = Brushes.Transparent,
                            BorderThickness = new Thickness(0),
                            FontSize = a.FontSize,
                            FontFamily = SafeFont(a.Font),
                            FontWeight = a.Bold ? FontWeights.Bold : FontWeights.Normal,
                            FontStyle = a.Italic ? FontStyles.Italic : FontStyles.Normal,
                            TextDecorations = a.Underline ? TextDecorations.Underline : null,
                            Padding = new Thickness(0),
                            AcceptsReturn = true,
                            CaretBrush = brush,
                            SelectionBrush = brush,
                            MinWidth = 8,
                            IsHitTestVisible = tool == Tool.Text
                        };
                        tb.TextChanged += delegate { a.Text = tb.Text; dirty = true; };
                        a.Edit = tb;

                        // 늘 Border로 한 겹 감싼다. 그래야 외곽선은 글자에, 그림자는 바깥에
                        // 따로 걸 수 있다 — 둘 다 같은 종류의 효과라 한 겹으로는 못 겹친다.
                        var host = new Border
                        {
                            Background = a.BoxBg
                                ? (Brightness(a.Color) > 0.55
                                    ? Theme.Alpha(Colors.Black, 0xB4)
                                    : Theme.Alpha(Colors.White, 0xD2))
                                : Brushes.Transparent,
                            CornerRadius = new CornerRadius(a.BoxBg ? Math.Max(2, a.FontSize * 0.12) : 0),
                            Padding = a.BoxBg
                                ? new Thickness(a.FontSize * 0.3, a.FontSize * 0.1, a.FontSize * 0.3, a.FontSize * 0.1)
                                : new Thickness(0),
                            Child = tb
                        };
                        if (a.Outline)
                        {
                            // 글자에 진짜 외곽선을 두르려면 글자를 도형으로 바꿔야 해서 고칠 수가 없어진다.
                            // 대신 퍼짐 없는 그림자를 반대 밝기로 깔면, 고칠 수 있는 상태로 같은 효과가 난다.
                            tb.Effect = new DropShadowEffect
                            {
                                ShadowDepth = 0,
                                BlurRadius = Math.Max(3, a.FontSize * 0.22),
                                Opacity = 1,
                                Color = Brightness(a.Color) > 0.55 ? Colors.Black : Colors.White
                            };
                        }
                        Canvas.SetLeft(host, a.A.X); Canvas.SetTop(host, a.A.Y);
                        return host;
                    }
                case Tool.Counter:
                    {
                        double r = a.FontSize * 0.95;
                        var g = new Grid { Width = r * 2, Height = r * 2, IsHitTestVisible = false };
                        g.Children.Add(new Ellipse
                        {
                            Fill = brush,
                            Stroke = new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)),
                            StrokeThickness = Math.Max(1, r * 0.08)
                        });
                        g.Children.Add(new TextBlock
                        {
                            Text = a.Number.ToString(),
                            Foreground = Brightness(a.Color) > 0.6 ? new SolidColorBrush(Theme.Bg) : Brushes.White,
                            FontSize = a.FontSize,
                            FontWeight = FontWeights.Bold,
                            FontFamily = Theme.UI,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        });
                        Canvas.SetLeft(g, a.A.X - r); Canvas.SetTop(g, a.A.Y - r);
                        return g;
                    }
            }
            return null;
        }

        static double Brightness(Color c) { return (c.R * 0.299 + c.G * 0.587 + c.B * 0.114) / 255.0; }

        static UIElement MakeArrow(Ann a, Brush brush)
        {
            double dx = a.B.X - a.A.X, dy = a.B.Y - a.A.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.5) return null;
            double ux = dx / len, uy = dy / len;

            bool atEnd = a.Head == ArrowHead.End || a.Head == ArrowHead.Both;
            bool atStart = a.Head == ArrowHead.Start || a.Head == ArrowHead.Both;

            double head = Math.Max(a.Thickness * 3.6, 10);
            double maxHead = len * (atEnd && atStart ? 0.34 : 0.6);
            if (head > maxHead) head = maxHead;

            // 촉이 붙는 쪽은 촉이 시작되는 지점까지만 선을 긋는다. 그래야 촉 안쪽이 뭉치지 않는다.
            var from = atStart ? new Point(a.A.X + ux * head, a.A.Y + uy * head) : a.A;
            var to = atEnd ? new Point(a.B.X - ux * head, a.B.Y - uy * head) : a.B;

            var canvas = new Canvas { IsHitTestVisible = false };

            var shaft = new Path
            {
                Data = new LineGeometry(from, to),
                Stroke = brush,
                StrokeThickness = a.Thickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
            ApplyDash(shaft, a);
            canvas.Children.Add(shaft);

            if (atEnd) canvas.Children.Add(new Path { Data = Head(a.B, to, ux, uy, head), Fill = brush });
            if (atStart) canvas.Children.Add(new Path { Data = Head(a.A, from, -ux, -uy, head), Fill = brush });
            return canvas;
        }

        // tip에서 back까지의 삼각형 촉.
        static Geometry Head(Point tip, Point back, double ux, double uy, double head)
        {
            double halfW = head * 0.52;
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(tip, true, true);
                ctx.LineTo(new Point(back.X - uy * halfW, back.Y + ux * halfW), true, false);
                ctx.LineTo(new Point(back.X + uy * halfW, back.Y - ux * halfW), true, false);
            }
            g.Freeze();
            return g;
        }

        // 안전하게 쓸 수 있는 글꼴. 없는 이름이 오면 기본값으로 떨어진다.
        static FontFamily SafeFont(string name)
        {
            try { if (!string.IsNullOrEmpty(name)) return new FontFamily(name + ", 맑은 고딕, Segoe UI"); }
            catch { }
            return Theme.UI;
        }

        void FocusText(Ann a)
        {
            var tb = a.Edit;
            if (tb == null) return;
            tb.IsHitTestVisible = true;
            Dispatcher.BeginInvoke(new Action(delegate { tb.Focus(); Keyboard.Focus(tb); }),
                                   System.Windows.Threading.DispatcherPriority.Input);
        }

        // 빈 텍스트는 남기지 않는다.
        void CommitText()
        {
            for (int i = anns.Count - 1; i >= 0; i--)
            {
                var a = anns[i];
                if (a.Kind != Tool.Text) continue;
                var tb = a.Edit;
                if (tb != null) { a.Text = tb.Text; tb.IsHitTestVisible = false; }
                if (string.IsNullOrEmpty(a.Text.Trim()))
                {
                    inkLayer.Children.Remove(a.Visual);
                    anns.RemoveAt(i);
                }
            }
        }

        // ================= 자유 변형 =================
        //
        // 끄는 동안에는 픽셀을 다시 만들지 않는다. 매 프레임 리샘플링하면 큰 레이어에서
        // 바로 버벅인다. 대신 그 레이어를 합성에서 빼두고, 같은 그림을 화면 위에
        // 행렬만 걸어 띄운다 — 그건 그래픽 카드가 공짜로 해준다.
        // 확정할 때 한 번만 진짜로 다시 만든다.

        Matrix xfStartInv;
        double xfStartSx, xfStartSy, xfStartTx, xfStartTy, xfStartSkewXv, xfStartSkewYv;
        Point xfStartLocal, xfStartMouse, xfCenterDoc;
        readonly List<Shape> xfDots = new List<Shape>();

        // 원본 레이어 자리(0..W, 0..H)를 문서 좌표로 옮기는 행렬.
        // 크기·기울임·회전은 레이어 가운데를 축으로 걸고, 그 다음에 옮긴다.
        Matrix XfMatrix()
        {
            var m = new Matrix();
            m.Scale(xfSx, xfSy);
            m.Skew(xfSkewX, xfSkewY);
            m.Rotate(xfAngle);
            var c = m.Transform(new Point(xfW / 2, xfH / 2));
            m.Translate(xfOx + xfW / 2 - c.X + xfTx, xfOy + xfH / 2 - c.Y + xfTy);
            return m;
        }

        void BeginTransformIfNeeded()
        {
            if (xfOn && xfLayer == doc.Active) return;
            if (xfOn) CommitTransform();

            var lay = doc.Current;
            if (lay == null) return;

            xfLayer = doc.Active;
            xfOx = lay.X; xfOy = lay.Y; xfW = lay.W; xfH = lay.H;
            xfTx = xfTy = 0; xfSx = xfSy = 1; xfAngle = 0; xfSkewX = xfSkewY = 0;
            xfOn = true;

            // 미리보기용 그림 한 장 (마스크와 불투명도까지 반영해서 눈에 보이는 그대로)
            var flat = lay.Flatten();
            ImageSource src = null;
            using (var bmp = flat.ToBitmap()) src = Cap.ToSource(bmp);

            xfImage = new Image
            {
                Source = src,
                Width = xfW, Height = xfH,
                Stretch = Stretch.Fill,
                IsHitTestVisible = false
            };
            RenderOptions.SetBitmapScalingMode(xfImage, BitmapScalingMode.HighQuality);
            Canvas.SetLeft(xfImage, 0); Canvas.SetTop(xfImage, 0);
            uiLayer.Children.Insert(0, xfImage);

            xfFrame = new System.Windows.Shapes.Polygon
            {
                Stroke = Theme.BrAccent,
                StrokeThickness = 1.2,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                Fill = Brushes.Transparent,
                IsHitTestVisible = false
            };
            uiLayer.Children.Add(xfFrame);

            doc.HideIndex = xfLayer;     // 합성에서 빼고 미리보기로 대신 보여준다
            RefreshBase(null);
            XfRedraw();
        }

        void XfTeardown()
        {
            if (xfImage != null) { uiLayer.Children.Remove(xfImage); xfImage = null; }
            if (xfPerspImg != null) { uiLayer.Children.Remove(xfPerspImg); xfPerspImg = null; }
            xfPersp = false;
            xfQuadGrab = -1;
            if (xfFrame != null) { uiLayer.Children.Remove(xfFrame); xfFrame = null; }
            foreach (var d in xfDots) uiLayer.Children.Remove(d);
            xfDots.Clear();
            doc.HideIndex = -1;
            xfOn = false;
            xfGrab = -1;
            xfLayer = -1;
        }

        // 손잡이 자리 (원본 레이어 안에서의 좌표)
        Point XfHandleLocal(int i)
        {
            double w = xfW, h = xfH;
            switch (i)
            {
                case 0: return new Point(0, 0);
                case 1: return new Point(w / 2, 0);
                case 2: return new Point(w, 0);
                case 3: return new Point(w, h / 2);
                case 4: return new Point(w, h);
                case 5: return new Point(w / 2, h);
                case 6: return new Point(0, h);
                case 7: return new Point(0, h / 2);
                default: return new Point(w / 2, h / 2);
            }
        }

        // 그 손잡이를 끌 때 붙박이로 둘 자리 (반대편)
        Point XfAnchorLocal(int i) { return XfHandleLocal((i + 4) % 8); }

        void XfRedraw()
        {
            if (!xfOn) return;
            if (xfPersp) { XfRedrawPersp(); return; }
            var m = XfMatrix();
            if (xfImage != null) xfImage.RenderTransform = new MatrixTransform(m);

            var corners = new[] { XfHandleLocal(0), XfHandleLocal(2), XfHandleLocal(4), XfHandleLocal(6) };
            if (xfFrame != null)
            {
                var pc = new PointCollection();
                foreach (var c in corners) pc.Add(m.Transform(c));
                xfFrame.Points = pc;
            }

            foreach (var d in xfDots) uiLayer.Children.Remove(d);
            xfDots.Clear();

            for (int i = 0; i < 8; i++)
            {
                var pt = m.Transform(XfHandleLocal(i));
                var dot = new Rectangle
                {
                    Width = 9, Height = 9,
                    Fill = Theme.BrAccent,
                    Stroke = new SolidColorBrush(Theme.Bg), StrokeThickness = 1.5,
                    RadiusX = 2, RadiusY = 2,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(dot, pt.X - 4.5); Canvas.SetTop(dot, pt.Y - 4.5);
                uiLayer.Children.Add(dot);
                xfDots.Add(dot);
            }

            var knob = m.Transform(XfKnobLocal());
            var k = new Ellipse
            {
                Width = 11, Height = 11,
                Fill = Theme.BrAccent,
                Stroke = new SolidColorBrush(Theme.Bg), StrokeThickness = 1.5,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(k, knob.X - 5.5); Canvas.SetTop(k, knob.Y - 5.5);
            uiLayer.Children.Add(k);
            xfDots.Add(k);

            Status(L.F("변형  {0:0}% × {1:0}%   {2:0.#}°{3}   {4} × {5}", "Transform  {0:0}% × {1:0}%   {2:0.#}°{3}   {4} × {5}",
                xfSx * 100, xfSy * 100, xfAngle,
                (Math.Abs(xfSkewX) > 0.05 || Math.Abs(xfSkewY) > 0.05)
                    ? L.F("   기울임 {0:0.#}° / {1:0.#}°", "   Skew {0:0.#}° / {1:0.#}°", xfSkewX, xfSkewY) : "",
                (int)Math.Round(xfW * Math.Abs(xfSx)), (int)Math.Round(xfH * Math.Abs(xfSy))));
        }

        // 회전 손잡이는 화면에서 늘 비슷한 거리에 보이도록 배율을 되돌려 잡는다.
        // ---------- 원근 (네 모서리를 따로) ----------

        bool xfPersp;
        Point[] xfQuad, xfQuadStart;
        int xfQuadGrab = -1;
        Point xfQuadMouse;
        Image xfPerspImg;

        void TogglePerspective()
        {
            if (!xfOn) return;
            if (xfPersp)
            {
                // 끄면 사각형으로 돌아간다 (원근은 행렬로 나타낼 수 없다)
                xfPersp = false;
                if (xfPerspImg != null) { uiLayer.Children.Remove(xfPerspImg); xfPerspImg = null; }
                if (xfImage != null) xfImage.Visibility = Visibility.Visible;
                XfRedraw();
                BuildOptions();
                return;
            }
            var m = XfMatrix();
            xfQuad = new[] { m.Transform(XfHandleLocal(0)), m.Transform(XfHandleLocal(2)), m.Transform(XfHandleLocal(4)), m.Transform(XfHandleLocal(6)) };
            xfPersp = true;
            if (xfImage != null) xfImage.Visibility = Visibility.Collapsed;
            XfRedraw();
            BuildOptions();
            Status(L.T("원근 — 모서리를 하나씩 끌어라. 안쪽을 끌면 통째로 옮긴다", "Perspective — drag each corner. Drag inside to move the whole thing"));
        }

        // 원근 미리보기: 큰 레이어는 줄여서 CPU로 펴 보인다(확정할 때만 원래 크기로 한 번)
        void XfRedrawPersp()
        {
            var lay = xfLayer >= 0 && xfLayer < doc.Layers.Count ? doc.Layers[xfLayer] : null;
            if (lay == null) return;
            if (xfFrame != null)
            {
                var pc = new PointCollection();
                foreach (var c in xfQuad) pc.Add(c);
                xfFrame.Points = pc;
            }
            foreach (var d in xfDots) uiLayer.Children.Remove(d);
            xfDots.Clear();
            for (int i = 0; i < 4; i++)
            {
                var dot = new Rectangle
                {
                    Width = 10, Height = 10, Fill = Theme.BrAccent,
                    Stroke = new SolidColorBrush(Theme.Bg), StrokeThickness = 1.5, RadiusX = 2, RadiusY = 2, IsHitTestVisible = false
                };
                Canvas.SetLeft(dot, xfQuad[i].X - 5); Canvas.SetTop(dot, xfQuad[i].Y - 5);
                uiLayer.Children.Add(dot);
                xfDots.Add(dot);
            }
            if (!Distort.IsConvex(xfQuad)) { Status(L.T("모서리가 꼬였다 — 볼록한 사각형이어야 편다", "The corners cross — the shape must be a convex quadrilateral")); return; }

            double side = Math.Max(lay.W, lay.H);
            double sc = Math.Min(1.0, 900.0 / Math.Max(1, side)) * Math.Max(0.25, Math.Min(1, zoom.ScaleX * 1.5));
            // 모서리를 멀리 끌어 사각형이 커져도 미리보기는 200만 픽셀을 넘지 않게
            double area = Distort.BoundsArea(xfQuad);
            if (area * sc * sc > 2000000) sc = Math.Sqrt(2000000 / area);
            var flat = lay.Flatten();
            Canvas32 w; byte[] wm; int x0, y0;
            Distort.Warp(flat, null, xfQuad, sc, out w, out wm, out x0, out y0);
            var wb = new WriteableBitmap(w.W, w.H, 96, 96, PixelFormats.Bgra32, null);
            wb.WritePixels(new Int32Rect(0, 0, w.W, w.H), w.P, w.W * 4, 0);
            if (xfPerspImg == null)
            {
                xfPerspImg = new Image { IsHitTestVisible = false, Stretch = Stretch.Fill };
                RenderOptions.SetBitmapScalingMode(xfPerspImg, BitmapScalingMode.HighQuality);
                uiLayer.Children.Insert(0, xfPerspImg);
            }
            xfPerspImg.Source = wb;
            xfPerspImg.Width = w.W / sc; xfPerspImg.Height = w.H / sc;
            Canvas.SetLeft(xfPerspImg, x0); Canvas.SetTop(xfPerspImg, y0);
            Status(L.T("원근  ", "Perspective  ") + string.Join("  ", Array.ConvertAll(xfQuad, delegate(Point q) { return "(" + (int)q.X + "," + (int)q.Y + ")"; })));
        }

        void CommitPerspective()
        {
            int idx = xfLayer;
            var q = xfQuad;
            XfTeardown();
            if (idx < 0 || idx >= doc.Layers.Count || q == null || !Distort.IsConvex(q))
            {
                RefreshBase(null); BuildOptions();
                if (q != null && !Distort.IsConvex(q)) Status(L.T("모서리가 꼬여 펴지 못했다", "The corners cross — couldn't apply perspective"));
                return;
            }
            var lay = doc.Layers[idx];
            if (Distort.BoundsArea(q) > 80000000) { RefreshBase(null); BuildOptions(); Status(L.T("원근 결과가 너무 크다 — 모서리를 판 가까이로", "Perspective result is too large — move the corners closer to the canvas")); return; }
            Push();
            Canvas32 px; byte[] mask; int x0, y0;
            Mouse.OverrideCursor = Cursors.Wait;
            try { Distort.Warp(lay.PixelsRead, lay.MaskRead, q, 1.0, out px, out mask, out x0, out y0); }
            finally { Mouse.OverrideCursor = null; }
            var nl = lay.Derive(px);
            nl.X = x0; nl.Y = y0;
            if (mask != null) Array.Copy(mask, nl.EditMask(), mask.Length);
            doc.Layers[idx] = nl;
            doc.Active = idx;
            dirty = true;
            RefreshBase(null);
            BuildOptions();
            Status(L.T("원근을 적용했다 — ", "Perspective applied — ") + px.W + " × " + px.H);
        }

        Point XfKnobLocal()
        {
            double away = 26 / Math.Max(0.05, Math.Abs(xfSy)) / Math.Max(0.25, zoom.ScaleX);
            return new Point(xfW / 2, -away);
        }

        int XfHandleAt(Point p, Matrix m)
        {
            double grab = 9 / Math.Max(0.25, zoom.ScaleX);
            var knob = m.Transform(XfKnobLocal());
            if (Math.Abs(p.X - knob.X) <= grab && Math.Abs(p.Y - knob.Y) <= grab) return 8;
            for (int i = 0; i < 8; i++)
            {
                var h = m.Transform(XfHandleLocal(i));
                if (Math.Abs(p.X - h.X) <= grab && Math.Abs(p.Y - h.Y) <= grab) return i;
            }
            return -1;
        }

        void XfDown(Point p)
        {
            if (xfPersp)
            {
                double grab = 10 / Math.Max(0.25, zoom.ScaleX);
                xfQuadGrab = -1;
                for (int i = 0; i < 4; i++)
                    if (Math.Abs(p.X - xfQuad[i].X) <= grab && Math.Abs(p.Y - xfQuad[i].Y) <= grab) { xfQuadGrab = i; break; }
                if (xfQuadGrab < 0)
                {
                    var poly = new StreamGeometry();
                    using (var c = poly.Open()) { c.BeginFigure(xfQuad[0], true, true); c.PolyLineTo(new[] { xfQuad[1], xfQuad[2], xfQuad[3] }, false, false); }
                    if (poly.FillContains(p)) xfQuadGrab = 4;
                }
                xfQuadStart = (Point[])xfQuad.Clone();
                xfQuadMouse = p;
                return;
            }
            if (!xfOn) return;
            var m = XfMatrix();
            xfGrab = XfHandleAt(p, m);

            xfStartInv = m;
            xfStartInv.Invert();
            xfStartLocal = xfStartInv.Transform(p);
            xfStartMouse = p;
            xfStartSx = xfSx; xfStartSy = xfSy;
            xfStartTx = xfTx; xfStartTy = xfTy;
            xfStartSkewXv = xfSkewX; xfStartSkewYv = xfSkewY;
            xfAngleStart = xfAngle;
            xfCenterDoc = m.Transform(new Point(xfW / 2, xfH / 2));
            if (xfGrab >= 0 && xfGrab < 8)
                xfAnchorDoc = m.Transform(XfAnchorLocal(xfGrab));
        }

        void XfMove(Point p)
        {
            if (!xfOn) return;
            if (xfPersp)
            {
                if (xfQuadGrab < 0) return;
                double ddx = p.X - xfQuadMouse.X, ddy = p.Y - xfQuadMouse.Y;
                if (xfQuadGrab == 4)
                    for (int i = 0; i < 4; i++) xfQuad[i] = new Point(xfQuadStart[i].X + ddx, xfQuadStart[i].Y + ddy);
                else
                    xfQuad[xfQuadGrab] = new Point(xfQuadStart[xfQuadGrab].X + ddx, xfQuadStart[xfQuadGrab].Y + ddy);
                XfRedraw();
                return;
            }
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;

            // 끄는 동안에는 시작 시점의 행렬로만 좌표를 되돌린다.
            // 매번 지금 행렬로 되돌리면 값이 자기 자신에 곱해져 걷잡을 수 없이 커진다.
            var local = xfStartInv.Transform(p);

            if (xfGrab == 8)
            {
                double a0 = Math.Atan2(xfStartMouse.Y - xfCenterDoc.Y, xfStartMouse.X - xfCenterDoc.X);
                double a1 = Math.Atan2(p.Y - xfCenterDoc.Y, p.X - xfCenterDoc.X);
                double deg = xfAngleStart + (a1 - a0) * 180.0 / Math.PI;
                if (shift) deg = Math.Round(deg / 15) * 15;
                xfAngle = Math.Round(deg, 1);
            }
            else if (xfGrab >= 0)
            {
                if (ctrl && (xfGrab == 1 || xfGrab == 3 || xfGrab == 5 || xfGrab == 7))
                {
                    // 기울이기 — 마주 보는 변을 붙박이로 두고 이 변만 민다
                    if (xfGrab == 1 || xfGrab == 5)
                    {
                        double sign = xfGrab == 5 ? 1 : -1;
                        xfSkewX = xfStartSkewXv +
                            sign * Math.Atan((local.X - xfStartLocal.X) / Math.Max(1, xfH)) * 180.0 / Math.PI;
                    }
                    else
                    {
                        double sign = xfGrab == 3 ? 1 : -1;
                        xfSkewY = xfStartSkewYv +
                            sign * Math.Atan((local.Y - xfStartLocal.Y) / Math.Max(1, xfW)) * 180.0 / Math.PI;
                    }
                }
                else
                {
                    var edge = XfHandleLocal(xfGrab);
                    var anch = XfAnchorLocal(xfGrab);
                    double fx = 1, fy = 1;
                    bool hasX = Math.Abs(edge.X - anch.X) > 0.001;
                    bool hasY = Math.Abs(edge.Y - anch.Y) > 0.001;
                    if (hasX) fx = (local.X - anch.X) / (edge.X - anch.X);
                    if (hasY) fy = (local.Y - anch.Y) / (edge.Y - anch.Y);

                    if (shift)
                    {
                        // 비율 유지 — 더 많이 끈 쪽에 맞춘다
                        double mag = Math.Max(hasX ? Math.Abs(fx) : 0, hasY ? Math.Abs(fy) : 0);
                        if (mag > 0.001)
                        {
                            fx = hasX ? Math.Sign(fx) * mag : mag;
                            fy = hasY ? Math.Sign(fy) * mag : mag;
                        }
                    }

                    xfSx = ClampScale(xfStartSx * fx);
                    xfSy = ClampScale(xfStartSy * fy);
                }

                // 붙박이 자리가 제자리에 남도록 위치를 되맞춘다.
                // Alt를 누르면 가운데를 붙박이로 삼는다.
                var keepLocal = alt ? new Point(xfW / 2, xfH / 2) : XfAnchorLocal(xfGrab);
                var keepDoc = alt ? xfCenterDoc : xfAnchorDoc;
                var now = XfMatrix().Transform(keepLocal);
                xfTx += keepDoc.X - now.X;
                xfTy += keepDoc.Y - now.Y;
            }
            else
            {
                double dx = p.X - xfStartMouse.X, dy = p.Y - xfStartMouse.Y;
                if (shift)
                {
                    if (Math.Abs(dx) > Math.Abs(dy)) dy = 0; else dx = 0;
                }
                xfTx = xfStartTx + dx;
                xfTy = xfStartTy + dy;
            }

            XfRedraw();
        }

        static double ClampScale(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return 1;
            double a = Math.Abs(v);
            if (a < 0.02) a = 0.02;
            if (a > 40) a = 40;
            return v < 0 ? -a : a;
        }

        void ResetTransform()
        {
            if (!xfOn) return;
            if (xfPersp)
            {
                xfQuad = new[] { new Point(xfOx, xfOy), new Point(xfOx + xfW, xfOy), new Point(xfOx + xfW, xfOy + xfH), new Point(xfOx, xfOy + xfH) };
                XfRedraw();
                return;
            }
            xfTx = xfTy = 0; xfSx = xfSy = 1; xfAngle = 0; xfSkewX = xfSkewY = 0;
            XfRedraw();
        }

        void CancelTransform()
        {
            if (!xfOn) return;
            XfTeardown();
            RefreshBase(null);
            BuildOptions();
            Status(L.T("변형을 취소했다", "Transform canceled"));
        }

        // 여기서 딱 한 번 픽셀을 다시 만든다.
        void CommitTransform()
        {
            if (!xfOn) return;
            if (xfPersp) { CommitPerspective(); return; }

            int idx = xfLayer;
            bool changed = Math.Abs(xfTx) > 0.01 || Math.Abs(xfTy) > 0.01
                           || Math.Abs(xfSx - 1) > 0.0005 || Math.Abs(xfSy - 1) > 0.0005
                           || Math.Abs(xfAngle) > 0.01
                           || Math.Abs(xfSkewX) > 0.01 || Math.Abs(xfSkewY) > 0.01;

            var m = XfMatrix();
            XfTeardown();

            if (!changed || idx < 0 || idx >= doc.Layers.Count)
            {
                RefreshBase(null);
                BuildOptions();
                return;
            }

            var lay = doc.Layers[idx];

            // 옮겨간 자리를 감싸는 사각형
            var c0 = m.Transform(new Point(0, 0));
            var c1 = m.Transform(new Point(xfW, 0));
            var c2 = m.Transform(new Point(xfW, xfH));
            var c3 = m.Transform(new Point(0, xfH));
            double minX = Math.Min(Math.Min(c0.X, c1.X), Math.Min(c2.X, c3.X));
            double minY = Math.Min(Math.Min(c0.Y, c1.Y), Math.Min(c2.Y, c3.Y));
            double maxX = Math.Max(Math.Max(c0.X, c1.X), Math.Max(c2.X, c3.X));
            double maxY = Math.Max(Math.Max(c0.Y, c1.Y), Math.Max(c2.Y, c3.Y));

            int dx0 = (int)Math.Floor(minX), dy0 = (int)Math.Floor(minY);
            int dw = (int)Math.Ceiling(maxX) - dx0, dh = (int)Math.Ceiling(maxY) - dy0;
            if (dw < 1 || dh < 1 || (long)dw * dh > 80000000L)
            {
                Status(L.T("변형 결과가 너무 크다", "Transform result is too large"));
                RefreshBase(null);
                BuildOptions();
                return;
            }

            Push();

            // 목적지 픽셀 → 문서 좌표 → 원본 레이어 좌표
            var inv = m;
            inv.Invert();
            var destToLocal = new Matrix(1, 0, 0, 1, dx0, dy0);
            destToLocal.Append(inv);

            Canvas32 px; byte[] mask;
            Ops.Resample(lay.PixelsRead, lay.MaskRead, dw, dh,
                         destToLocal.M11, destToLocal.M12, destToLocal.M21, destToLocal.M22,
                         destToLocal.OffsetX, destToLocal.OffsetY, out px, out mask);

            var nl = lay.Derive(px);
            nl.X = dx0; nl.Y = dy0;
            if (mask != null)
            {
                var mm = nl.EditMask();
                Array.Copy(mask, mm, Math.Min(mask.Length, mm.Length));
            }
            doc.Layers[idx] = nl;
            doc.Active = idx;

            RefreshBase(null);
            BuildOptions();
            Status(L.T("변형을 적용했다 — ", "Transform applied — ") + dw + " × " + dh);
        }

        // ================= 선택 영역 =================
        //
        // 영역을 잡는 일과, 그 영역으로 무엇을 할지는 갈라 둔다.
        // 한 번 잡아두면 도구를 바꿔도 남아 있어서, 나중에 자르거나 지우거나 떼어낼 수 있다.

        // 끄는 동안 보여주는 미리보기. 아직 확정된 선택이 아니다.
        void DrawSelectionPreview(Rect box)
        {
            Geometry g = null;
            if (lassoPts != null && lassoPts.Count >= 2)
                g = SelOps.PolyGeometry(lassoPts, true);
            else if (!box.IsEmpty && box.Width >= 1 && box.Height >= 1)
                g = selShape == SelShape.Ellipse
                    ? (Geometry)new EllipseGeometry(box)
                    : new RectangleGeometry(box);

            ShowChrome(g, true);
            if (g != null)
            {
                var b = g.Bounds;
                Status(L.T("선택  ", "Selection  ") + (int)b.Width + " × " + (int)b.Height);
            }
        }

        // 손을 뗀 순간 실제 선택으로 굳힌다.
        void CommitSelection(Rect box)
        {
            Geometry g = null;
            if (lassoPts != null)
            {
                if (lassoPts.Count >= 3) g = SelOps.PolyGeometry(lassoPts, true);
                lassoPts = null;
                edges = null;
            }
            else if (!box.IsEmpty && box.Width >= 2 && box.Height >= 2)
                g = selShape == SelShape.Ellipse
                    ? (Geometry)new EllipseGeometry(box)
                    : new RectangleGeometry(box);

            if (g == null) { ClearSelection(); return; }

            selection = Selection.FromGeometry(g, doc.W, doc.H);
            DrawSelectionChrome();
            BuildOptions();
            if (selection != null)
                Status(L.T("선택  ", "Selection  ") + selection.Bounds.Width + " × " + selection.Bounds.Height +
                       L.T("    Enter 자르기 · Delete 지우기 · Ctrl+D 해제", "    Enter to crop · Delete to erase · Ctrl+D to deselect"));
        }

        void ClearSelection()
        {
            selection = null;
            lassoPts = null;
            edges = null;
            crInclude.Clear(); crExclude.Clear();
            objPts.Clear(); objPos.Clear(); objCands = null;
            DrawSelectionChrome();
            if (tool == Tool.Marquee) BuildOptions();
        }

        void SelectAll()
        {
            selection = Selection.FromGeometry(new RectangleGeometry(new Rect(0, 0, doc.W, doc.H)), doc.W, doc.H);
            DrawSelectionChrome();
            if (tool == Tool.Marquee) BuildOptions();
            Status(L.T("전체 선택", "Selected all"));
        }

        void InvertSelection()
        {
            if (selection == null) { SelectAll(); return; }
            selection = selection.Invert();
            DrawSelectionChrome();
            BuildOptions();
            Status(L.T("선택을 뒤집었다", "Selection inverted"));
        }

        void DrawSelectionChrome()
        {
            ShowChrome(selection != null ? selection.Geom : null, false);
        }

        // 테두리는 늘 보이고, 바깥을 어둡게 덮는 건 선택 도구를 쓰는 동안만 한다.
        // 다른 도구로 그림을 그릴 때까지 화면이 어두우면 방해만 된다.
        void ShowChrome(Geometry g, bool dragging)
        {
            if (selAnts == null)
            {
                selAnts = new Path
                {
                    Stroke = Theme.BrAccent,
                    StrokeThickness = 1.4,
                    StrokeDashArray = new DoubleCollection { 5, 4 },
                    IsHitTestVisible = false
                };
                uiLayer.Children.Add(selAnts);

                // 개미 행진 — 점선이 천천히 흐른다
                var march = new System.Windows.Media.Animation.DoubleAnimation(0, 9,
                    new Duration(TimeSpan.FromMilliseconds(700)));
                march.RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever;
                selAnts.BeginAnimation(Shape.StrokeDashOffsetProperty, march);
            }
            if (selDim == null)
            {
                selDim = new Path { Fill = Theme.Alpha(Colors.Black, 0x5C), IsHitTestVisible = false };
                uiLayer.Children.Insert(0, selDim);
            }

            selAnts.Data = g;
            selAnts.Visibility = g == null ? Visibility.Collapsed : Visibility.Visible;

            bool dim = g != null && (tool == Tool.Marquee || dragging);
            selDim.Visibility = dim ? Visibility.Visible : Visibility.Collapsed;
            if (dim)
                selDim.Data = new CombinedGeometry(GeometryCombineMode.Exclude,
                    new RectangleGeometry(new Rect(0, 0, doc.W, doc.H)), g);
        }

        // ---------- 선택 영역으로 하는 일 ----------

        void CropToSelection()
        {
            if (selection == null) { Status(L.T("먼저 영역을 골라라", "Select an area first")); return; }
            var b = selection.Bounds;
            if (b.Width < 2 || b.Height < 2) { Status(L.T("고른 영역이 너무 작다", "The selection is too small")); return; }

            Push();
            // 사각형이 아닌 모양이면, 자르기 전에 바깥을 지워 모양을 남긴다.
            if (!IsRectSelection()) EraseThroughSelection(false, false);

            doc.Crop(b);
            foreach (var a in anns) MoveAnn(a, -b.X, -b.Y);
            foreach (var a in anns) Rebuild(a);

            ClearSelection();
            Deselect();
            RefreshBase(null);
            FitToWindow();
            Status(L.T("잘랐다 — ", "Cropped — ") + doc.W + " × " + doc.H);
        }

        bool IsRectSelection()
        {
            return selection != null && selection.Geom is RectangleGeometry;
        }

        void EraseSelection(bool inside)
        {
            if (selection == null) { Status(L.T("먼저 영역을 골라라", "Select an area first")); return; }
            Push();
            EraseThroughSelection(inside, true);
            Status(inside ? L.T("고른 곳을 지웠다 — 복구 브러시로 되살릴 수 있다", "Erased the selection — use Restore to bring it back")
                          : L.T("고른 곳 바깥을 지웠다 — 복구 브러시로 되살릴 수 있다", "Erased outside the selection — use Restore to bring it back"));
        }

        void EraseThroughSelection(bool inside, bool refresh)
        {
            var lay = doc.Current;
            if (lay == null) return;
            SelOps.EraseThrough(lay.EditMask(), lay.W, lay.H, lay.X, lay.Y, selection, inside);
            if (refresh) RefreshBase(null);
        }

        void SelectionToLayer()
        {
            if (selection == null) { Status(L.T("먼저 영역을 골라라", "Select an area first")); return; }
            var src = doc.Current;
            if (src == null) return;
            if (!PixelLayer(L.T("새 레이어로 떼기", "Layer via Copy"))) return;

            Push();
            int ox, oy;
            var px = SelOps.Extract(src, selection, out ox, out oy);
            doc.Add(px, L.T("오려낸 조각", "Cutout piece"), ox, oy);
            ClearSelection();
            RefreshBase(null);
            SelectTool(Tool.Transform);
            Status(L.T("새 레이어로 떼어냈다 — 끌어서 옮겨라", "Copied to a new layer — drag to move it"));
        }

        // 고른 부분을 오려 새 레이어로 띄우고 곧바로 변형에 들어간다.
        //
        // 원본에서 그 자리를 지우는 건 마스크에만 하는 일이라, 마음이 바뀌면
        // 복구 브러시로 도로 칠하거나 되돌리기로 물릴 수 있다.
        void TransformSelection()
        {
            if (selection == null) { Status(L.T("먼저 영역을 골라라", "Select an area first")); return; }
            var src = doc.Current;
            if (src == null) return;
            if (!PixelLayer(L.T("오려 띄우기", "Lift and Transform"))) return;

            Push();
            int ox, oy;
            var px = SelOps.Extract(src, selection, out ox, out oy);
            SelOps.EraseThrough(src.EditMask(), src.W, src.H, src.X, src.Y, selection, true);
            doc.Add(px, L.T("오려낸 조각", "Cutout piece"), ox, oy);

            ClearSelection();
            RefreshBase(null);
            SelectTool(Tool.Transform);
            Status(L.T("오려내서 띄웠다 — 끌어서 옮기고 모서리로 크기, 손잡이로 회전. 원래 자리는 복구 브러시로 되살릴 수 있다", "Lifted out — drag to move, corners to resize, handle to rotate. Use Restore to bring back the original spot"));
        }

        void TrimTransparent()
        {
            doc.CompositeAll();
            var b = Ops.OpaqueBounds(doc.Comp, 8);
            if (b.Width == doc.W && b.Height == doc.H) { Status(L.T("잘라낼 투명 여백이 없다", "No transparent margins to trim")); return; }

            Push();
            doc.Crop(b);
            foreach (var a in anns) MoveAnn(a, -b.X, -b.Y);
            foreach (var a in anns) Rebuild(a);
            ClearSelection();
            Deselect();
            RefreshBase(null);
            FitToWindow();
            Status(L.T("투명 여백을 잘라냈다", "Trimmed transparent margins"));
        }

        // ================= 확대 =================

        void Zoom(double factor) { SetZoom(zoom.ScaleX * factor); }

        void SetZoom(double z)
        {
            z = Math.Max(0.05, Math.Min(16, z));
            zoom.ScaleX = zoom.ScaleY = z;
            if (zoomText != null) zoomText.Text = (int)Math.Round(z * 100) + "%";
            DrawGuides();
            Dispatcher.BeginInvoke(new Action(DrawRulers), System.Windows.Threading.DispatcherPriority.Render);
        }

        // ================= 눈금자·가이드 =================
        // 눈금자에서 끌어내면 가이드가 생기고, 선택 도구로 옮기며, 판 밖으로 끌면 지운다.
        // 영역 선택·도형·그라디언트는 가이드와 판 가장자리·가운데에 달라붙는다(화면에서 6픽셀 안, Ctrl을 누르면 안 붙음).
        // Compositor(MIT, Wonder Assembly LLC)의 Guides·CanvasRulers를 따랐다.

        readonly List<double> vGuides = new List<double>();   // 세로 가이드의 x
        readonly List<double> hGuides = new List<double>();   // 가로 가이드의 y
        bool guidesOn = true, rulersOn = true;
        bool draggingGuide, dragGuideV;
        int dragGuideI;
        Canvas hRuler, vRuler;
        RowDefinition rulerRow;
        ColumnDefinition rulerCol;
        readonly List<Line> guideLines = new List<Line>();
        const double RulerSize = 18;

        Grid BuildRulers(ScrollViewer sv)
        {
            var g = new Grid();
            rulerRow = new RowDefinition { Height = new GridLength(rulersOn ? RulerSize : 0) };
            rulerCol = new ColumnDefinition { Width = new GridLength(rulersOn ? RulerSize : 0) };
            g.RowDefinitions.Add(rulerRow);
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(rulerCol);
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            hRuler = new Canvas { Background = Theme.BrCard, ClipToBounds = true, Cursor = Cursors.SizeNS };
            vRuler = new Canvas { Background = Theme.BrCard, ClipToBounds = true, Cursor = Cursors.SizeWE };
            Grid.SetColumn(hRuler, 1); g.Children.Add(hRuler);
            Grid.SetRow(vRuler, 1); g.Children.Add(vRuler);
            g.Children.Add(new Border { Background = Theme.BrCard });
            Grid.SetRow(sv, 1); Grid.SetColumn(sv, 1); g.Children.Add(sv);

            // 눈금자에서 끌어 새 가이드. 움직임·놓기는 그림판 쪽 처리기가 받는다.
            MouseButtonEventHandler down = delegate(object o, MouseButtonEventArgs e)
            {
                bool vertical = o == vRuler;
                var p = e.GetPosition(uiLayer);
                var list = vertical ? vGuides : hGuides;
                list.Add(Math.Round(vertical ? p.X : p.Y));
                dragGuideV = vertical;
                dragGuideI = list.Count - 1;
                draggingGuide = true;
                guidesOn = true;
                viewport.CaptureMouse();
                DrawGuides();
                e.Handled = true;
            };
            hRuler.MouseLeftButtonDown += down;
            vRuler.MouseLeftButtonDown += down;

            sv.ScrollChanged += delegate { DrawRulers(); };
            sv.SizeChanged += delegate { DrawRulers(); };
            return g;
        }

        void DrawRulers()
        {
            if (hRuler == null || stackHost == null || !rulersOn || doc == null) return;
            hRuler.Children.Clear(); vRuler.Children.Clear();
            Point o, ov;
            try { o = stackHost.TranslatePoint(new Point(0, 0), hRuler); ov = stackHost.TranslatePoint(new Point(0, 0), vRuler); }
            catch { return; }
            double z = zoom.ScaleX;
            double[] steps = { 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000, 2000, 5000, 10000 };
            double step = steps[steps.Length - 1];
            foreach (var st in steps) if (st * z >= 56) { step = st; break; }
            double minor = step / 5;
            var tickBr = Theme.Alpha(Colors.White, 0x40);
            var txtBr = Theme.BrMuted;

            double w = hRuler.ActualWidth;
            for (double d = Math.Floor(-o.X / z / minor) * minor; o.X + d * z <= w; d += minor)
            {
                double x = Math.Round(o.X + d * z) + 0.5;
                bool major = Math.Abs(d / step - Math.Round(d / step)) < 1e-6;
                hRuler.Children.Add(new Line { X1 = x, X2 = x, Y1 = major ? 2 : RulerSize - 5, Y2 = RulerSize, Stroke = tickBr, StrokeThickness = 1 });
                if (!major) continue;
                var t = new TextBlock { Text = ((long)Math.Round(d)).ToString(), FontSize = 9, Foreground = txtBr, FontFamily = Theme.Mono };
                Canvas.SetLeft(t, x + 3); Canvas.SetTop(t, 1);
                hRuler.Children.Add(t);
            }
            // 세로 눈금자는 숫자를 돌려 세운다
            double h = vRuler.ActualHeight;
            for (double d = Math.Floor(-ov.Y / z / minor) * minor; ov.Y + d * z <= h; d += minor)
            {
                double y = Math.Round(ov.Y + d * z) + 0.5;
                bool major = Math.Abs(d / step - Math.Round(d / step)) < 1e-6;
                vRuler.Children.Add(new Line { Y1 = y, Y2 = y, X1 = major ? 2 : RulerSize - 5, X2 = RulerSize, Stroke = tickBr, StrokeThickness = 1 });
                if (!major) continue;
                var t = new TextBlock { Text = ((long)Math.Round(d)).ToString(), FontSize = 9, Foreground = txtBr, FontFamily = Theme.Mono,
                                        LayoutTransform = new RotateTransform(-90) };
                Canvas.SetLeft(t, 1); Canvas.SetTop(t, y + 3);
                vRuler.Children.Add(t);
            }
        }

        void DrawGuides()
        {
            if (uiLayer == null || doc == null) return;
            foreach (var l in guideLines) uiLayer.Children.Remove(l);
            guideLines.Clear();
            if (!guidesOn) return;
            double th = 1 / Math.Max(0.05, zoom.ScaleX);
            var br = new SolidColorBrush(Color.FromRgb(0x22, 0xD3, 0xEE));
            foreach (var x in vGuides)
                guideLines.Add(new Line { X1 = x, X2 = x, Y1 = 0, Y2 = doc.H, Stroke = br, StrokeThickness = th, IsHitTestVisible = false });
            foreach (var y in hGuides)
                guideLines.Add(new Line { Y1 = y, Y2 = y, X1 = 0, X2 = doc.W, Stroke = br, StrokeThickness = th, IsHitTestVisible = false });
            foreach (var l in guideLines) uiLayer.Children.Add(l);
        }

        // p 근처(화면 5픽셀)의 가이드
        bool GuideAt(Point p, out bool vertical, out int index)
        {
            double tol = 5 / Math.Max(0.05, zoom.ScaleX);
            vertical = false; index = -1;
            double best = tol;
            for (int i = 0; i < vGuides.Count; i++)
                if (Math.Abs(vGuides[i] - p.X) <= best && p.Y >= 0 && p.Y <= doc.H) { best = Math.Abs(vGuides[i] - p.X); vertical = true; index = i; }
            for (int i = 0; i < hGuides.Count; i++)
                if (Math.Abs(hGuides[i] - p.Y) <= best && p.X >= 0 && p.X <= doc.W) { best = Math.Abs(hGuides[i] - p.Y); vertical = false; index = i; }
            return index >= 0;
        }

        Point SnapPt(Point p)
        {
            bool snapTool = tool == Tool.Marquee && (selShape == SelShape.Rect || selShape == SelShape.Ellipse) ||
                            tool == Tool.Rect || tool == Tool.Ellipse || tool == Tool.Line || tool == Tool.Arrow ||
                            tool == Tool.Gradient || tool == Tool.Mosaic || tool == Tool.Blur;
            if (!snapTool || (Keyboard.Modifiers & ModifierKeys.Control) != 0) return p;
            double tol = 6 / Math.Max(0.05, zoom.ScaleX);
            double bx = p.X, by = p.Y, dx = tol, dy = tol;
            var xs = new List<double>(); var ys = new List<double>();
            if (guidesOn) { xs.AddRange(vGuides); ys.AddRange(hGuides); }
            xs.Add(0); xs.Add(doc.W); xs.Add(doc.W / 2.0);
            ys.Add(0); ys.Add(doc.H); ys.Add(doc.H / 2.0);
            foreach (var x in xs) if (Math.Abs(x - p.X) < dx) { dx = Math.Abs(x - p.X); bx = x; }
            foreach (var y in ys) if (Math.Abs(y - p.Y) < dy) { dy = Math.Abs(y - p.Y); by = y; }
            return new Point(bx, by);
        }

        void ToggleRulers()
        {
            rulersOn = !rulersOn;
            rulerRow.Height = new GridLength(rulersOn ? RulerSize : 0);
            rulerCol.Width = new GridLength(rulersOn ? RulerSize : 0);
            if (rulersOn) Dispatcher.BeginInvoke(new Action(DrawRulers), System.Windows.Threading.DispatcherPriority.Render);
            Status(rulersOn ? L.T("눈금자를 켰다 — 눈금자에서 끌어내면 가이드", "Rulers on — drag from a ruler to add a guide") : L.T("눈금자를 껐다", "Rulers off"));
        }

        void ToggleGuides()
        {
            guidesOn = !guidesOn;
            DrawGuides();
            Status(guidesOn ? L.T("가이드를 보인다", "Guides shown") : L.T("가이드를 숨겼다 (지우지는 않았다)", "Guides hidden (not removed)"));
        }


        void FitToWindow()
        {
            if (viewport == null || doc == null) return;
            double vw = viewport.ViewportWidth - 60, vh = viewport.ViewportHeight - 60;
            if (vw <= 10 || vh <= 10) return;
            double z = Math.Min(vw / doc.W, vh / doc.H);
            SetZoom(Math.Min(1, z));
        }

        void ShowMarquee(Rect r)
        {
            if (marquee == null)
            {
                marquee = new Rectangle
                {
                    Stroke = Theme.BrAccent, StrokeThickness = 1.5,
                    StrokeDashArray = new DoubleCollection { 4, 3 },
                    Fill = Theme.Alpha(Theme.Accent, 0x1A),
                    IsHitTestVisible = false
                };
                uiLayer.Children.Add(marquee);
            }
            marquee.Visibility = Visibility.Visible;
            marquee.Width = Math.Max(1, r.Width); marquee.Height = Math.Max(1, r.Height);
            Canvas.SetLeft(marquee, r.X); Canvas.SetTop(marquee, r.Y);
        }

        void HideMarquee() { if (marquee != null) marquee.Visibility = Visibility.Collapsed; }

        // 손잡이가 가운데에서 어느 쪽에 있는지로 커서를 고른다.
        // 개체가 돌아가 있으면 방향도 같이 돌아가므로 커서가 자연히 따라간다.
        static Cursor CursorForDirection(double dx, double dy)
        {
            double a = Math.Atan2(dy, dx) * 180.0 / Math.PI;
            a = ((a % 180) + 180) % 180;
            if (a < 22.5 || a >= 157.5) return Cursors.SizeWE;
            if (a < 67.5) return Cursors.SizeNWSE;
            if (a < 112.5) return Cursors.SizeNS;
            return Cursors.SizeNESW;
        }

        // 마우스가 무엇 위에 있느냐에 따라 커서를 바꾼다.
        void HoverCursor(Point p)
        {
            if (tool == Tool.Transform && xfOn)
            {
                var m = XfMatrix();
                int h = XfHandleAt(p, m);
                if (h == 8) { viewport.Cursor = Icons.Rotate; return; }
                if (h >= 0)
                {
                    var hp = m.Transform(XfHandleLocal(h));
                    var c = m.Transform(new Point(xfW / 2, xfH / 2));
                    viewport.Cursor = CursorForDirection(hp.X - c.X, hp.Y - c.Y);
                    return;
                }
                viewport.Cursor = InsideQuad(p, m) ? Cursors.SizeAll : Cursors.Arrow;
                return;
            }

            if (tool == Tool.Select && selected != null)
            {
                int h = HandleAt(selected, p);
                if (h == 8) { viewport.Cursor = Icons.Rotate; return; }
                if (h >= 0)
                {
                    var r = selected.Bounds;
                    double ccx = r.X + r.Width / 2, ccy = r.Y + r.Height / 2;
                    var hp = Rotate(HandlePts(r)[h], ccx, ccy, selected.Angle);
                    viewport.Cursor = CursorForDirection(hp.X - ccx, hp.Y - ccy);
                    return;
                }
                viewport.Cursor = selected.Bounds.Contains(Unrotate(selected, p))
                    ? Cursors.SizeAll : Cursors.Arrow;
                return;
            }

            UpdateCursor();
        }

        // 변형 틀 안쪽인가. 평행사변형이라 네 변의 같은 쪽에 있는지로 본다.
        bool InsideQuad(Point p, Matrix m)
        {
            var q = new[]
            {
                m.Transform(new Point(0, 0)), m.Transform(new Point(xfW, 0)),
                m.Transform(new Point(xfW, xfH)), m.Transform(new Point(0, xfH))
            };
            bool neg = false, pos = false;
            for (int i = 0; i < 4; i++)
            {
                var a = q[i];
                var b = q[(i + 1) % 4];
                double cross = (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
                if (cross < 0) neg = true; else if (cross > 0) pos = true;
                if (neg && pos) return false;
            }
            return true;
        }

        void UpdateCursor()
        {
            switch (tool)
            {
                case Tool.Select: viewport.Cursor = Cursors.Arrow; break;
                case Tool.Marquee: viewport.Cursor = Cursors.Cross; break;
                case Tool.Text: viewport.Cursor = Cursors.IBeam; break;
                case Tool.BgErase: case Tool.Erase: case Tool.Restore:
                case Tool.Heal: case Tool.Clone: case Tool.Liquify: case Tool.Brush: viewport.Cursor = Cursors.None; break;
                case Tool.Transform: viewport.Cursor = Cursors.Arrow; break;
                case Tool.Picker: viewport.Cursor = Cursors.Cross; break;
                default: viewport.Cursor = Cursors.Cross; break;
            }
        }

        // ================= 합치기와 내보내기 =================

        // 바탕과 주석을 한 장으로 합친다.
        public D.Bitmap Compose()
        {
            CommitText();
            Deselect();
            inkLayer.UpdateLayout();

            int w = doc.W, h = doc.H;
            doc.CompositeAll();
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                var baseSrc = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null,
                                                  doc.Comp.P, w * 4);
                dc.DrawImage(baseSrc, new Rect(0, 0, w, h));

                if (inkLayer.Children.Count > 0)
                {
                    var vb = new VisualBrush(inkLayer)
                    {
                        Stretch = Stretch.None,
                        AlignmentX = AlignmentX.Left,
                        AlignmentY = AlignmentY.Top,
                        ViewboxUnits = BrushMappingMode.Absolute,
                        Viewbox = new Rect(0, 0, w, h),
                        ViewportUnits = BrushMappingMode.Absolute,
                        Viewport = new Rect(0, 0, w, h)
                    };
                    dc.DrawRectangle(vb, null, new Rect(0, 0, w, h));
                }
            }
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);

            // Pbgra32(프리멀티플라이드)를 일반 알파로 되돌려야 저장할 때 색이 맞다
            var conv = new FormatConvertedBitmap(rtb, PixelFormats.Bgra32, null, 0);
            return Cap.FromSource(conv);
        }

        // 레이어를 살려 PSD로. 화살표·글자 같은 주석은 맨 위 한 장으로 구워 넣는다.
        void SavePsd(string path)
        {
            var notes = new List<string>();
            Layer inkTop = null;
            if (anns.Count > 0)
            {
                var ink = InkCanvas();
                inkTop = doc.Add(ink, L.T("주석", "Annotations"), 0, 0);
            }
            int active = doc.Active;
            try { Psd.Write(doc, path, notes); }
            finally
            {
                if (inkTop != null) { doc.Layers.Remove(inkTop); doc.Active = Math.Min(active, doc.Layers.Count - 1); doc.CompositeAll(); }
            }
            psdNote = notes.Count > 0 ? "   · " + string.Join(" · ", notes) : "";
        }

        string psdNote = "";

        // 주석만 문서 크기 그림으로
        Canvas32 InkCanvas()
        {
            CommitText();
            Deselect();
            inkLayer.UpdateLayout();
            int w = doc.W, h = doc.H;
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                var vb = new VisualBrush(inkLayer)
                {
                    Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top,
                    ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, w, h),
                    ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, w, h)
                };
                dc.DrawRectangle(vb, null, new Rect(0, 0, w, h));
            }
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            var conv = new FormatConvertedBitmap(rtb, PixelFormats.Bgra32, null, 0);
            var c = new Canvas32(w, h);
            conv.CopyPixels(c.P, w * 4, 0);
            return c;
        }

        void CopyOut()
        {
            using (var b = Compose())
            {
                if (Cap.ToClipboard(b)) Status(L.T("클립보드에 복사했다", "Copied to clipboard"));
                else Status(L.T("클립보드를 다른 앱이 잡고 있다 — 잠시 뒤 다시", "Another app is using the clipboard — try again in a moment"));
            }
        }

        void SaveOut()
        {
            using (var b = Compose())
            {
                psdNote = "";
                string p = App.SaveAsDialog(b, SavePsd);
                if (p != null)
                {
                    filePath = p;
                    dirty = false;
                    Status(L.T("저장했다 — ", "Saved — ") + System.IO.Path.GetFileName(p) + psdNote);
                }
            }
        }

        // 클립보드 이미지를 새 레이어로 올린다. 판보다 크면 판에 맞춰 줄인다.
        void PasteAsLayer()
        {
            var b = Cap.FromClipboard();
            if (b == null) { Status(L.T("클립보드에 이미지가 없다", "No image on the clipboard")); return; }
            AddAsLayer(b, L.T("붙여넣기", "Pasted"));
        }

        void AddAsLayer(D.Bitmap bmp, string name)
        {
            Push();
            D.Bitmap use = bmp;
            if (bmp.Width > doc.W || bmp.Height > doc.H)
            {
                double k = Math.Min((double)doc.W / bmp.Width, (double)doc.H / bmp.Height);
                use = Cap.Resize(bmp, (int)(bmp.Width * k), (int)(bmp.Height * k));
                bmp.Dispose();
            }
            var px = Canvas32.From(use);
            use.Dispose();

            doc.Add(px, name, (doc.W - px.W) / 2, (doc.H - px.H) / 2);
            RefreshBase(null);
            SelectTool(Tool.Transform);
            Status(L.T("새 레이어로 올렸다 — 끌어서 옮겨라", "Added as a new layer — drag to move it"));
        }

        void CenterLayer()
        {
            BeginTransformIfNeeded();
            if (!xfOn) return;
            var m = XfMatrix();
            var c = m.Transform(new Point(xfW / 2, xfH / 2));
            xfTx += doc.W / 2.0 - c.X;
            xfTy += doc.H / 2.0 - c.Y;
            XfRedraw();
        }

        // 판 안에 꽉 차게. 비율은 지킨다.
        void FitLayerToDoc()
        {
            BeginTransformIfNeeded();
            if (!xfOn) return;
            double k = Math.Min(doc.W / xfW, doc.H / xfH);
            xfSx = xfSy = k;
            xfAngle = 0; xfSkewX = xfSkewY = 0;
            xfTx = xfTy = 0;
            var m = XfMatrix();
            var c = m.Transform(new Point(xfW / 2, xfH / 2));
            xfTx += doc.W / 2.0 - c.X;
            xfTy += doc.H / 2.0 - c.Y;
            XfRedraw();
        }

        // ================= 더보기 메뉴 =================

        void ShowMoreMenu()
        {
            var m = new ContextMenu { Background = Theme.BrCard, Foreground = Theme.BrText };
            m.Items.Add(MI(L.T("색 조절…   Ctrl+L", "Adjust Color…   Ctrl+L"), delegate { ShowAdjust(); }));
            m.Items.Add(MI(L.T("자동 보정", "Auto Adjust"), delegate { AutoAdjust(); }));
            m.Items.Add(MI((rulersOn ? L.T("눈금자 숨기기", "Hide Rulers") : L.T("눈금자 보이기", "Show Rulers")) + "   Ctrl+R", delegate { ToggleRulers(); }));
            m.Items.Add(MI((guidesOn ? L.T("가이드 숨기기", "Hide Guides") : L.T("가이드 보이기", "Show Guides")) + "   Ctrl+;", delegate { ToggleGuides(); }));
            m.Items.Add(MI(L.T("가이드 모두 지우기", "Clear All Guides"), delegate { vGuides.Clear(); hGuides.Clear(); DrawGuides(); Status(L.T("가이드를 모두 지웠다", "All guides cleared")); }));
            m.Items.Add(new Separator());
            m.Items.Add(MI(L.T("레이어 효과…", "Layer Effects…"), delegate { ShowEffects(); }));
            {
                var am = new MenuItem { Header = L.T("조정 레이어 (나중에 다시 고침)", "Adjustment Layer (editable later)"), Foreground = Theme.BrText, Background = Theme.BrCard };
                am.Items.Add(MI(L.T("레벨", "Levels"), delegate { NewAdjustment("levels"); }));
                am.Items.Add(MI(L.T("커브", "Curves"), delegate { NewAdjustment("curves"); }));
                am.Items.Add(MI(L.T("색조 / 채도", "Hue / Saturation"), delegate { NewAdjustment("huesat"); }));
                am.Items.Add(MI(L.T("컬러 밸런스", "Color Balance"), delegate { NewAdjustment("balance"); }));
                am.Items.Add(MI(L.T("흑백", "Black & White"), delegate { NewAdjustment("bw"); }));
                am.Items.Add(MI(L.T("반전", "Invert"), delegate { NewAdjustment("invert"); }));
                m.Items.Add(am);
            }
            {
                var fm = new MenuItem { Header = L.T("필터", "Filter"), Foreground = Theme.BrText, Background = Theme.BrCard };
                foreach (FilterKind fk in Enum.GetValues(typeof(FilterKind)))
                {
                    var k = fk;
                    fm.Items.Add(MI(FilterSheet.KindTitle(k) + "…", delegate { ShowFilter(k); }));
                }
                fm.Items.Add(new Separator());
                fm.Items.Add(MI(L.T("디더링…", "Dither…"), delegate { ShowDither(); }));
                fm.Items.Add(new Separator());
                fm.Items.Add(MI(L.T("RAW 보정…   Ctrl+Shift+A", "RAW Develop…   Ctrl+Shift+A"), delegate { ShowCameraRaw(); }));
                m.Items.Add(fm);
            }
            m.Items.Add(MI(L.T("레벨…   Ctrl+Shift+L", "Levels…   Ctrl+Shift+L"), delegate { ShowLevels(); }));
            m.Items.Add(MI(L.T("커브…   Ctrl+M", "Curves…   Ctrl+M"), delegate { ShowCurves(); }));
            m.Items.Add(MI(L.T("색조 / 채도…   Ctrl+U", "Hue / Saturation…   Ctrl+U"), delegate { ShowHueSat(); }));
            m.Items.Add(MI(L.T("컬러 밸런스…   Ctrl+B", "Color Balance…   Ctrl+B"), delegate { ShowBalance(); }));
            m.Items.Add(MI(L.T("흑백…   Ctrl+Shift+B", "Black & White…   Ctrl+Shift+B"), delegate { ShowBlackWhite(); }));
            m.Items.Add(new Separator());
            m.Items.Add(MI(L.T("누끼 따기 (AI)", "Remove Background (AI)"), delegate { RunCutout(false); }));
            m.Items.Add(MI(L.T("누끼 따기 (AI · 정밀, 머리카락·복잡한 배경)", "Remove Background (AI · Precise, for hair and busy backgrounds)"), delegate { RunCutout(true); }));
            m.Items.Add(MI(L.T("피사체 선택 (AI)", "Select Subject (AI)"), delegate { SelectSubject(); }));
            m.Items.Add(MI(L.T("선택 영역 내용 채우기   Shift+Del", "Content-Aware Fill Selection   Shift+Del"), delegate { ContentFillSelection(); }));
            m.Items.Add(MI(L.T("투명 여백 잘라내기", "Trim Transparent Margins"), delegate { TrimTransparent(); }));
            m.Items.Add(MI(L.T("가장자리 색 번짐 제거", "Remove Edge Color Fringe"), delegate
            {
                Push();
                var lay = doc.Current;
                Ops.Defringe(lay.EditPixels(), lay.MaskRead, 2);
                RefreshBase(null);
                Status(L.T("가장자리를 정리했다", "Edges cleaned up"));
            }));
            m.Items.Add(new Separator());
            m.Items.Add(MI(L.T("크기 바꾸기…", "Resize…"), delegate { ResizeDialog(); }));
            m.Items.Add(MI(L.T("오른쪽으로 90° 회전", "Rotate 90° Right"), delegate { Transform_(delegate(D.Bitmap b) { return Ops.Rotate(b, 90); }); }));
            m.Items.Add(MI(L.T("왼쪽으로 90° 회전", "Rotate 90° Left"), delegate { Transform_(delegate(D.Bitmap b) { return Ops.Rotate(b, 270); }); }));
            m.Items.Add(MI(L.T("좌우 뒤집기", "Flip Horizontal"), delegate { Transform_(delegate(D.Bitmap b) { return Ops.Flip(b, true); }); }));
            m.Items.Add(MI(L.T("상하 뒤집기", "Flip Vertical"), delegate { Transform_(delegate(D.Bitmap b) { return Ops.Flip(b, false); }); }));
            m.Items.Add(new Separator());
            m.Items.Add(MI(L.T("테두리 두르기", "Add Border"), delegate
            {
                Transform_(delegate(D.Bitmap b)
                { return Ops.Outline(b, 2, D.Color.FromArgb(255, ink.R, ink.G, ink.B)); });
            }));
            m.Items.Add(MI(L.T("그림자 넣기", "Add Shadow"), delegate
            {
                Transform_(delegate(D.Bitmap b) { return Ops.Shadow(b, 24, 8, 0.45); });
            }));
            m.Items.Add(MI(L.T("여백 넣기 (24px)", "Add Padding (24px)"), delegate
            {
                Transform_(delegate(D.Bitmap b) { return Ops.Pad(b, 24, D.Color.White); });
            }));
            m.Items.Add(new Separator());
            m.Items.Add(MI(L.T("이미지 파일을 새 레이어로…   Ctrl+O", "Open Image as Layer…   Ctrl+O"), delegate { OpenAsLayer(); }));
            m.Items.Add(MI(L.T("이미지 이어붙이기…", "Append Image…"), delegate { AppendFromFile(); }));
            m.Items.Add(MI(L.T("클립보드 이미지를 새 레이어로   Ctrl+V", "Paste Image as Layer   Ctrl+V"), delegate { PasteAsLayer(); }));

            m.PlacementTarget = this;
            m.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            m.IsOpen = true;
        }

        MenuItem MI(string head, Action act)
        {
            var mi = new MenuItem { Header = head, Foreground = Theme.BrText, Background = Theme.BrCard };
            mi.Click += delegate { act(); };
            return mi;
        }

        // 바탕 전체를 갈아끼우는 변형. 주석은 그대로 두되 크기가 바뀌면 위치가 어긋나므로 먼저 굽는다.
        void Transform_(Func<D.Bitmap, D.Bitmap> f)
        {
            Push();
            using (var src = Compose())
            using (var dst = f(src))
            {
                doc.FlattenInto(Canvas32.From(dst), dst.Width, dst.Height);
            }
            anns.Clear();
            inkLayer.Children.Clear();
            Deselect();
            RefreshBase(null);
            FitToWindow();
        }

        void ResizeDialog()
        {
            var d = new SizeDialog(doc.W, doc.H) { Owner = this };
            if (d.ShowDialog() != true) return;
            Transform_(delegate(D.Bitmap b) { return Cap.Resize(b, d.ResultW, d.ResultH); });
            Status(L.F("크기를 {0} × {1}로 바꿨다", "Resized to {0} × {1}", doc.W, doc.H));
        }

        void AppendFromFile()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = L.T("이어붙일 이미지", "Image to Append"),
                Filter = Cap.OpenFilter
            };
            if (dlg.ShowDialog() != true) return;
            try { AppendImage(Cap.Load(dlg.FileName)); }
            catch (Exception ex) { Status(ex.Message); }
        }

        // 파일을 골라 새 레이어로 올린다. 여러 장을 한 번에 고를 수 있다.
        void OpenAsLayer()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = L.T("새 레이어로 올릴 이미지", "Images to Add as Layers"),
                Filter = Cap.OpenFilter,
                Multiselect = true
            };
            if (dlg.ShowDialog() != true) return;
            int n = 0;
            foreach (var f in dlg.FileNames)
            {
                if (Psd.IsPsd(f)) { OpenPsd(f, Status); continue; }
                try
                {
                    AddAsLayer(Cap.Load(f), System.IO.Path.GetFileNameWithoutExtension(f));
                    n++;
                }
                catch (Exception ex) { Status(ex.Message); }
            }
            if (n > 1) Status(L.F("{0}장을 레이어로 올렸다", "Added {0} images as layers", n));
        }

        void AppendImage(D.Bitmap add)
        {
            var d = new JoinDialog() { Owner = this };
            if (d.ShowDialog() != true) { add.Dispose(); return; }

            Push();
            using (var cur = Compose())
            using (var joined = Ops.Join(new[] { cur, add }, d.Vertical, d.Gap,
                                         d.Transparent ? D.Color.Transparent : D.Color.White))
            {
                doc.FlattenInto(Canvas32.From(joined), joined.Width, joined.Height);
            }
            add.Dispose();
            anns.Clear();
            inkLayer.Children.Clear();
            Deselect();
            RefreshBase(null);
            FitToWindow();
            Status(L.T("이어붙였다 — ", "Appended — ") + doc.W + " × " + doc.H);
        }

        // ---------- 색 조절 ----------
        //
        // 선택 영역이 있으면 그 안만, 없으면 지금 레이어 전체를 손본다.
        // 조절하는 동안 화면에 바로 비치고, 취소하면 원래대로 돌아간다.

        // 선택 영역을 지금 레이어 좌표에 맞춰 옮겨 놓은 덮개. 선택이 없으면 null(전체).
        byte[] SelectionCoverFor(Layer lay)
        {
            if (selection == null || lay == null) return null;
            var cov = new byte[lay.W * lay.H];
            for (int y = 0; y < lay.H; y++)
            {
                int dy = y + lay.Y;
                if (dy < 0 || dy >= selection.H) continue;
                int lrow = y * lay.W, srow = dy * selection.W;
                for (int x = 0; x < lay.W; x++)
                {
                    int dx = x + lay.X;
                    if (dx < 0 || dx >= selection.W) continue;
                    cov[lrow + x] = selection.Mask[srow + dx];
                }
            }
            return cov;
        }

        // ================= 스팟 힐링·도장·내용 채우기 =================

        static string HealModeName(HealMode m)
        {
            return m == HealMode.ContentAware ? L.T("내용 인식", "Content-Aware") : m == HealMode.Proximity ? L.T("가까운 곳", "Proximity") : L.T("매끄럽게", "Smooth");
        }

        // 칠하는 동안은 반투명 자국만 보여주고, 손을 떼면 한 번에 메운다.
        void ShowHealTrail()
        {
            if (healTrail == null)
            {
                healTrail = new Polyline
                {
                    Stroke = Theme.Alpha(Theme.Accent, 0x66),
                    StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                    StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false
                };
                uiLayer.Children.Add(healTrail);
            }
            healTrail.StrokeThickness = brushSize * 2;
            healTrail.Points = new PointCollection(healPts.Count == 1
                ? new[] { healPts[0], new Point(healPts[0].X + 0.01, healPts[0].Y) }
                : healPts.ToArray());
            healTrail.Visibility = Visibility.Visible;
        }

        void FinishHeal()
        {
            if (healTrail != null) healTrail.Visibility = Visibility.Collapsed;
            var pts = healPts;
            healPts = null;
            var lay = doc.Current;
            if (pts == null || lay == null) return;

            var cov = new byte[lay.W * lay.H];
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            for (int i = 0; i < pts.Count; i++)
            {
                var a = pts[i == 0 ? 0 : i - 1];
                var b = pts[i];
                double dist = (b - a).Length;
                int steps = Math.Max(1, (int)(dist / Math.Max(1, brushSize * 0.22)));
                for (int k = 0; k <= steps; k++)
                {
                    double t = (double)k / steps;
                    int x = (int)Math.Round(a.X + (b.X - a.X) * t), y = (int)Math.Round(a.Y + (b.Y - a.Y) * t);
                    Ops.MaskBrush(cov, lay.W, lay.H, x - lay.X, y - lay.Y, brushSize, 60, false);
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                }
            }

            Push();
            Mouse.OverrideCursor = Cursors.Wait;
            bool done;
            try { done = Heal.Spot(lay.EditPixels(), cov, healMode, 1.0, (uint)Environment.TickCount); }
            finally { Mouse.OverrideCursor = null; }
            if (!done) { CancelPush(); Status(L.T("고칠 자리가 그림 밖이다", "The area to fix is outside the image")); return; }

            int pad = brushSize + 20;
            RefreshBase(WithFx(lay, new D.Rectangle((int)minX - pad, (int)minY - pad,
                                        (int)(maxX - minX) + pad * 2, (int)(maxY - minY) + pad * 2)));
            dirty = true;
            Status(L.F("메웠다 ({0}) — 마음에 안 들면 Ctrl+Z 하고 방식을 바꿔 봐라", "Filled ({0}) — not happy? Press Ctrl+Z and try another mode", HealModeName(healMode)));
        }

        // 가져올 곳 표시: 획을 긋기 전엔 Alt+클릭한 자리, 정렬 중이면 브러시를 따라다니는 자리.
        void MoveCloneMark(Point p)
        {
            bool show = tool == Tool.Clone && cloneSrc != null;
            if (cloneMark == null)
            {
                if (!show) return;
                cloneMark = new Ellipse
                {
                    Stroke = Brushes.White, StrokeThickness = 1.5,
                    StrokeDashArray = new DoubleCollection { 3, 2 },
                    IsHitTestVisible = false
                };
                uiLayer.Children.Add(cloneMark);
            }
            cloneMark.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (!show) return;
            Point at = cloneSrc.Value;
            if (strokeSnap != null) at = p + cloneStroke;
            else if (cloneAligned && cloneOffset != null) at = p + cloneOffset.Value;
            cloneMark.Width = cloneMark.Height = brushSize * 2;
            Canvas.SetLeft(cloneMark, at.X - brushSize);
            Canvas.SetTop(cloneMark, at.Y - brushSize);
        }

        // 획이 지나간 자리를 획 시작 때 그림의 (거리만큼 떨어진) 픽셀로 덮는다.
        // 칠한 정도는 획 단위로 쌓아서, 같은 곳을 여러 번 지나도 결과는 원본과 가져온 것 사이에 머문다.
        void CloneDab(Point from, Point to)
        {
            if (strokeSnap == null) return;
            StrokePaint(strokeSnap, (int)cloneStroke.X, (int)cloneStroke.Y, 1.0, from, to);
        }

        // 획이 지나간 자리를 source의 (거리만큼 떨어진) 픽셀로 덮는다. 원본은 획 시작 때의 strokeSnap.
        // 도장은 같은 그림의 다른 자리를, 흐리게 칠하기는 흐려 둔 그림의 같은 자리를 가져온다.
        void StrokePaint(Canvas32 source, int odx, int ody, double amount, Point from, Point to)
        {
            var lay = doc.Current;
            if (lay == null || strokeSnap == null) return;
            var dst = lay.EditPixels(new D.Rectangle(
                (int)Math.Min(from.X, to.X) - lay.X - brushSize - 1, (int)Math.Min(from.Y, to.Y) - lay.Y - brushSize - 1,
                (int)Math.Abs(to.X - from.X) + brushSize * 2 + 3, (int)Math.Abs(to.Y - from.Y) + brushSize * 2 + 3));
            int W = lay.W, H = lay.H;

            double dist = (to - from).Length;
            int steps = Math.Max(1, (int)(dist / Math.Max(1, brushSize * 0.22)));
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            for (int k = 0; k <= steps; k++)
            {
                double t = (double)k / steps;
                int x = (int)Math.Round(from.X + (to.X - from.X) * t), y = (int)Math.Round(from.Y + (to.Y - from.Y) * t);
                Ops.MaskBrush(strokeCov, W, H, x - lay.X, y - lay.Y, brushSize, hardness, false);
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            }

            int x0 = Math.Max(0, (int)minX - lay.X - brushSize - 1), x1 = Math.Min(W - 1, (int)maxX - lay.X + brushSize + 1);
            int y0 = Math.Max(0, (int)minY - lay.Y - brushSize - 1), y1 = Math.Min(H - 1, (int)maxY - lay.Y + brushSize + 1);
            for (int y = y0; y <= y1; y++)
            {
                int sy = y + ody;
                for (int x = x0; x <= x1; x++)
                {
                    int i = y * W + x;
                    int c = strokeCov[i];
                    if (c == 0) continue;
                    if (amount < 1) c = (int)(c * amount);
                    int sx = x + odx;
                    if (sx < 0 || sy < 0 || sx >= W || sy >= H) continue;
                    int a = strokeSnap.P[i], b = source.P[sy * W + sx];
                    dst.P[i] = c >= 255 ? b : Mix(a, b, c);
                }
            }
            RefreshBase(WithFx(lay, new D.Rectangle((int)minX - brushSize - 2, (int)minY - brushSize - 2,
                                        (int)(maxX - minX) + brushSize * 2 + 4, (int)(maxY - minY) + brushSize * 2 + 4)));
            dirty = true;
        }

        // 붓: 획 시작 때 그림 위에 지금 색을 (획 단위로 쌓인 정도 × 불투명도)만큼 덮는다.
        // 같은 획 안에서 겹쳐 지나가도 불투명도를 넘지 않는다.
        void PaintDab(Point from, Point to)
        {
            var lay = doc.Current;
            if (lay == null || strokeSnap == null) return;
            var region = new D.Rectangle(
                (int)Math.Min(from.X, to.X) - lay.X - brushSize - 1, (int)Math.Min(from.Y, to.Y) - lay.Y - brushSize - 1,
                (int)Math.Abs(to.X - from.X) + brushSize * 2 + 3, (int)Math.Abs(to.Y - from.Y) + brushSize * 2 + 3);
            var dst = lay.EditPixels(region);
            int W = lay.W, H = lay.H;
            double dist = (to - from).Length;
            int steps = Math.Max(1, (int)(dist / Math.Max(1, brushSize * 0.15)));
            for (int k = 0; k <= steps; k++)
            {
                double t = (double)k / steps;
                Ops.MaskBrush(strokeCov, W, H, (int)Math.Round(from.X + (to.X - from.X) * t) - lay.X,
                              (int)Math.Round(from.Y + (to.Y - from.Y) * t) - lay.Y, brushSize, hardness, false);
            }
            var r = region; r.Intersect(new D.Rectangle(0, 0, W, H));
            for (int y = r.Top; y < r.Bottom; y++)
                for (int x = r.Left; x < r.Right; x++)
                {
                    int i = y * W + x;
                    int c = strokeCov[i];
                    if (c == 0) continue;
                    double a = c / 255.0 * paintOpacity / 100.0;
                    if (gradCover != null) a *= gradCover[i] / 255.0;
                    dst.P[i] = Over(strokeSnap.P[i], ink.R, ink.G, ink.B, a);
                }
            region.Offset(lay.X, lay.Y);
            RefreshBase(WithFx(lay, region));
        }

        // 곱하지 않은 ARGB 위에 단색을 a(0~1)만큼 일반 합성
        static int Over(int under, byte r, byte g, byte b, double a)
        {
            if (a <= 0) return under;
            double da = Canvas32.A(under) / 255.0;
            double oa = a + da * (1 - a);
            if (oa <= 0) return 0;
            double k = da * (1 - a);
            return Canvas32.Pack((byte)Math.Round(oa * 255),
                (byte)Math.Round((r * a + Canvas32.R(under) * k) / oa),
                (byte)Math.Round((g * a + Canvas32.G(under) * k) / oa),
                (byte)Math.Round((b * a + Canvas32.B(under) * k) / oa));
        }

        // 그라디언트: 시작점에서 끝점까지 지금 색 → (투명/흰색/검정). 원형은 시작점이 가운데, 끝점이 둘레.
        void RenderGradient(Point end)
        {
            var lay = doc.Current;
            if (lay == null || gradSnap == null) return;
            var dst = lay.EditPixels();
            double dx = end.X - gradStart.X, dy = end.Y - gradStart.Y, len2 = dx * dx + dy * dy;
            if (len2 < 1) { Array.Copy(gradSnap.P, dst.P, dst.P.Length); RefreshBase(null); return; }
            double len = Math.Sqrt(len2), op = paintOpacity / 100.0;
            byte r0 = ink.R, g0 = ink.G, b0 = ink.B, r1 = r0, g1 = g0, b1 = b0;
            double a0 = 1, a1 = 0;
            if (gradEnd == 1) { r1 = g1 = b1 = 255; a1 = 1; }
            else if (gradEnd == 2) { r1 = g1 = b1 = 0; a1 = 1; }
            int W = lay.W, H = lay.H;
            for (int y = 0; y < H; y++)
            {
                double py = y + lay.Y + 0.5 - gradStart.Y;
                for (int x = 0; x < W; x++)
                {
                    double px = x + lay.X + 0.5 - gradStart.X;
                    double t = gradShape == 0 ? (px * dx + py * dy) / len2 : Math.Sqrt(px * px + py * py) / len;
                    t = t < 0 ? 0 : t > 1 ? 1 : t;
                    if (gradReverse) t = 1 - t;
                    // 투명으로 갈 때는 색은 그대로 두고 알파만 줄인다 — 섞으면 중간이 칙칙해진다
                    double ca = a0 + (a1 - a0) * t;
                    byte cr = (byte)Math.Round(r0 + (r1 - r0) * t), cg = (byte)Math.Round(g0 + (g1 - g0) * t), cb = (byte)Math.Round(b0 + (b1 - b0) * t);
                    double a = ca * op;
                    int i = y * W + x;
                    if (gradCover != null) a *= gradCover[i] / 255.0;
                    dst.P[i] = Over(gradSnap.P[i], cr, cg, cb, a);
                }
            }
            RefreshBase(null);
        }

        static int Mix(int a, int b, int t)
        {
            int u = 255 - t;
            return Canvas32.Pack(
                (byte)((Canvas32.A(a) * u + Canvas32.A(b) * t + 127) / 255),
                (byte)((Canvas32.R(a) * u + Canvas32.R(b) * t + 127) / 255),
                (byte)((Canvas32.G(a) * u + Canvas32.G(b) * t + 127) / 255),
                (byte)((Canvas32.B(a) * u + Canvas32.B(b) * t + 127) / 255));
        }

        void ContentFillSelection()
        {
            var lay = doc.Current;
            if (lay == null) return;
            if (!PixelLayer(L.T("내용 채우기", "Content-Aware Fill"))) return;
            if (selection == null) { Status(L.T("채울 곳을 영역 선택으로 먼저 골라라", "Select the area to fill first")); return; }
            var cover = SelectionCoverFor(lay);
            bool any = false;
            foreach (var c in cover) if (c != 0) { any = true; break; }
            if (!any) { Status(L.T("고른 영역이 이 레이어 밖이다", "The selection is outside this layer")); return; }

            Push();
            Mouse.OverrideCursor = Cursors.Wait;
            bool ok;
            // 둘레와 닮은 한 덩어리를 찾아 붙이는 쪽(스팟 힐링의 내용 인식)이 무늬를 훨씬 잘 살린다.
            // 그만한 자리가 그림 안에 없을 때만 작은 조각을 짜 맞추는 쪽으로 넘어간다.
            try
            {
                var px = lay.EditPixels();
                ok = Heal.Spot(px, cover, HealMode.ContentAware, 1.0, (uint)Environment.TickCount, true)
                     || Heal.ContentFill(px, cover);
            }
            finally { Mouse.OverrideCursor = null; }
            if (!ok)
            {
                CancelPush();
                Status(L.T("가져올 그림이 모자란다 — 둘레가 좀 남게 더 작게 골라라", "Not enough surrounding image — make a smaller selection with room around it"));
                return;
            }
            RefreshBase(null);
            dirty = true;
            Status(L.T("둘레로 채웠다", "Filled from surroundings"));
        }

        // ================= 색상 범위 선택 =================

        readonly List<int[]> crInclude = new List<int[]>();
        readonly List<int[]> crExclude = new List<int[]>();
        int crFuzz = 40;
        bool crInvert;

        void ColorRangeClick(Point p)
        {
            doc.CompositeAll();
            var c = ColorRange.Sample(doc.Comp, (int)p.X, (int)p.Y);
            if (c == null) { Status(L.T("투명한 곳이다 — 색이 있는 곳을 눌러라", "That spot is transparent — click a colored area")); return; }
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
            if (alt && crInclude.Count > 0) crExclude.Add(c);
            else if (shift && crInclude.Count > 0) crInclude.Add(c);
            else { crInclude.Clear(); crExclude.Clear(); crInclude.Add(c); }
            RebuildColorRange();
            BuildOptions();          // 고른 뒤에 쓸 단추(자르기·지우기…)를 보여준다
        }

        void RebuildColorRange()
        {
            if (crInclude.Count == 0) return;
            doc.CompositeAll();
            var mask = ColorRange.Mask(doc.Comp, crInclude, crExclude, crFuzz, crInvert);
            var g = Matte.Trace(mask, doc.W, doc.H, 128, 0, 0);
            if (g == null) { selection = null; DrawSelectionChrome(); Status(L.T("고를 곳이 없다 — 허용 범위를 넓혀라", "Nothing selected — increase Fuzziness")); return; }
            var sel = new Selection { W = doc.W, H = doc.H, Geom = g, Mask = mask };
            var b = g.Bounds;
            sel.Bounds = D.Rectangle.FromLTRB((int)b.Left, (int)b.Top, (int)b.Right, (int)b.Bottom);
            selection = sel;
            DrawSelectionChrome();
            long n = 0;
            foreach (var v in mask) if (v >= 128) n++;
            Status(L.F("색상 범위 · 색 {0}개{1} · 그림의 {2:P0}", "Color range · {0} colors{1} · {2:P0} of image", crInclude.Count,
                   crExclude.Count > 0 ? L.F(" (뺀 색 {0})", " ({0} excluded)", crExclude.Count) : "", (double)n / mask.Length));
        }

        // ================= 물체 선택 (AI) =================

        readonly List<Point> objPts = new List<Point>();
        readonly List<bool> objPos = new List<bool>();
        int objLevel;

        void ObjectClick(Point p)
        {
            if (p.X < 0 || p.Y < 0 || p.X >= doc.W || p.Y >= doc.H) return;
            if (!ObjectSelect.Prepare(this, Status)) return;

            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
            // 그림이 바뀌었으면(크기·내용) 이전 점과 후보는 다른 그림의 것이다
            var shownNow = doc.Flatten();
            long keyNow = ObjectSelect.KeyOf(shownNow);
            if (keyNow != objKey) { objPts.Clear(); objPos.Clear(); objCands = null; objKey = keyNow; }
            if ((shift || alt) && objPts.Count > 0)
            {
                objPts.Add(p); objPos.Add(!alt);
            }
            else if (objPts.Count == 1 && (objPts[0] - p).Length <= 6 && objCands != null && objCands.Count > 0)
            {
                // 같은 자리: 다음으로 넓은 후보, 끝나면 처음으로. 모델을 다시 돌릴 필요가 없다.
                objLevel = (objLevel + 1) % objCands.Count;
                ApplyObject(objCands[objLevel], L.F("넓이 {0}/{1}", "Size {0}/{1}", objLevel + 1, objCands.Count));
                return;
            }
            else
            {
                objPts.Clear(); objPos.Clear();
                objPts.Add(p); objPos.Add(true);
            }

            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                if (!ObjectSelect.Has(keyNow))
                {
                    Status(L.T("그림을 읽는 중…", "Analyzing image…"));
                    ObjectSelect.Encode(shownNow, keyNow);
                }
                objCands = ObjectSelect.Candidates(objPts, objPos);
            }
            catch (Exception ex) { Status(Cutout.Explain(ex)); return; }
            finally { Mouse.OverrideCursor = null; }
            objLevel = 0;
            if (objCands.Count == 0) { Status(L.T("거기서는 물체를 찾지 못했다", "No object found there")); return; }
            ApplyObject(objCands[0], objPts.Count > 1 ? L.F("점 {0}개", "{0} points", objPts.Count) :
                                    objCands.Count > 1 ? L.F("넓이 1/{0}", "Size 1/{0}", objCands.Count) : "");
        }

        List<byte[]> objCands;
        long objKey;

        void ApplyObject(byte[] mask, string how)
        {
            if (mask == null || mask.Length != doc.W * doc.H) { objCands = null; Status(L.T("그림이 바뀌었다 — 다시 눌러라", "The image changed — click again")); return; }
            var g = Matte.Trace(mask, doc.W, doc.H, 128, 0, 0);
            if (g == null) { Status(L.T("거기서는 물체를 찾지 못했다", "No object found there")); return; }
            var sel = new Selection { W = doc.W, H = doc.H, Geom = g, Mask = mask };
            var b = g.Bounds;
            sel.Bounds = D.Rectangle.FromLTRB((int)b.Left, (int)b.Top, (int)b.Right, (int)b.Bottom);
            selection = sel;
            DrawSelectionChrome();
            BuildOptions();
            Status(L.T("물체를 골랐다", "Object selected") + (how.Length > 0 ? " · " + how : "") +
                   L.T("   같은 자리 다시 누르면 넓게 · Shift+클릭 더하기 · Alt+클릭 빼기", "   Click the same spot to widen · Shift+click to add · Alt+click to subtract"));
        }

        // ================= 블렌드 모드·레이어 효과 =================

        void ShowBlendMenu(FrameworkElement anchor)
        {
            var lay = doc.Current;
            if (lay == null) return;
            var m = new ContextMenu { Background = Theme.BrCard, Foreground = Theme.BrText, PlacementTarget = anchor,
                                      Placement = System.Windows.Controls.Primitives.PlacementMode.Top };
            for (int g = 0; g < Blend.Groups.Length; g++)
            {
                if (g > 0) m.Items.Add(new Separator());
                foreach (var mode in Blend.Groups[g])
                {
                    var md = mode;
                    var mi = MI(Blend.Name(md), delegate { SetBlend(md); });
                    mi.IsChecked = lay.Blend == md;
                    m.Items.Add(mi);
                }
            }
            m.IsOpen = true;
        }

        void SetBlend(BlendMode mode)
        {
            var lay = doc.Current;
            if (lay == null || lay.Blend == mode) return;
            Push();
            lay.Blend = mode;
            dirty = true;
            RefreshBase(null);
            Status(L.T("블렌드: ", "Blend: ") + Blend.Name(mode) + (doc.Active == 0 ? L.T(" — 맨 아래 레이어라 섞일 것이 없다", " — bottom layer, nothing to blend with") : ""));
        }

        void ShowEffects()
        {
            var lay = doc.Current;
            if (lay == null) return;
            if (!PixelLayer(L.T("레이어 효과", "Layer Effects"))) return;
            if (xfOn) CommitTransform();
            var before = lay.Fx;
            Push();
            var d = new EffectsSheet(before ?? new LayerFx(), delegate(LayerFx f)
            {
                lay.Fx = f.Any ? f : null;
                RefreshBase(null);
            });
            d.Owner = this;
            bool ok = d.ShowDialog() == true;
            if (!ok)
            {
                lay.Fx = before;
                RefreshBase(null);
                CancelPush();
                Status(L.T("효과를 취소했다", "Effects canceled"));
                return;
            }
            lay.Fx = d.Value.Any ? d.Value.Clone() : null;
            RefreshBase(null);
            dirty = true;
            Status(lay.Fx != null ? L.T("효과를 붙였다 — 레이어를 고쳐도 따라온다", "Effects applied — they follow the layer as you edit it") : L.T("효과를 뗐다", "Effects removed"));
        }

        // ================= 레벨·커브·색조/채도·컬러 밸런스·흑백 =================

        // 다섯 창이 같은 길을 탄다: 원본을 쥐고, 창이 넘기는 함수로 원본 → 레이어를 매번 새로 만든다.
        // 선택 영역이 있으면 그 안만.
        void ToneEdit(string what, Func<Canvas32, byte[], ToneSheet> make)
        {
            var lay = doc.Current;
            if (lay == null) return;
            if (lay.Adjust != null) { Status(L.T("조정 레이어에는 픽셀이 없다 — 레이어 목록에서 두 번 눌러 설정을 고쳐라", "Adjustment layers have no pixels — double-click it in the Layers list to edit its settings")); return; }
            if (xfOn) CommitTransform();

            var cover = SelectionCoverFor(lay);
            var orig = lay.PixelsRead.Clone();
            Push();
            var target = lay.EditPixels();

            var d = make(orig, cover);
            ToneFn shown = null;
            d.Preview = delegate(ToneFn f)
            {
                shown = f;
                if (f == null) Array.Copy(orig.P, target.P, orig.P.Length);
                else { f(orig, target); Tone.KeepInside(target, orig, cover); }
                lay.Touch();                 // 같은 버퍼를 계속 고치니 효과 그림을 직접 버려야 한다
                RefreshBase(null);
            };
            d.Owner = this;
            // 흑백처럼 여는 순간부터 바뀌는 것은 바로 보여준다
            if (!d.IsIdentity) d.Preview(d.Current());
            bool ok = d.ShowDialog() == true;

            if (!ok || d.IsIdentity)
            {
                Array.Copy(orig.P, target.P, orig.P.Length);
                lay.Touch();
                RefreshBase(null);
                CancelPush();
                Status(ok ? L.T("바뀐 게 없다", "No changes") : L.F("{0}을(를) 취소했다", "{0} canceled", what));
                return;
            }
            // 마지막으로 끈 값이 아직 안 그려졌을 수 있다
            d.Preview(d.Current());
            dirty = true;
            Status(selection != null ? L.F("고른 영역에 {0} 적용", "Applied {0} to selection", what) : L.F("{0} 적용", "Applied {0}", what));
        }

        void ShowLevels()
        {
            ToneEdit(L.T("레벨", "Levels"), delegate(Canvas32 o, byte[] c) { return new LevelsSheet(Tone.Histogram(o, c)); });
        }
        void ShowCurves()
        {
            ToneEdit(L.T("커브", "Curves"), delegate(Canvas32 o, byte[] c) { return new CurvesSheet(Tone.Histogram(o, c)); });
        }
        void ShowFilter(FilterKind k) { ToneEdit(FilterSheet.KindTitle(k), delegate { return new FilterSheet(k); }); }
        void ShowCameraRaw() { ToneEdit(L.T("RAW 보정", "RAW Develop"), delegate(Canvas32 o, byte[] c) { return new CameraRawSheet(o); }); }
        void ShowDither() { ToneEdit(L.T("디더링", "Dither"), delegate { return new DitherSheet(); }); }
        void ShowHueSat() { ToneEdit(L.T("색조/채도", "Hue/Saturation"), delegate { return new HueSatSheet(); }); }
        void ShowBalance() { ToneEdit(L.T("컬러 밸런스", "Color Balance"), delegate { return new BalanceSheet(); }); }
        void ShowBlackWhite() { ToneEdit(L.T("흑백", "Black & White"), delegate { return new BlackWhiteSheet(); }); }

        void ShowAdjust()
        {
            var lay = doc.Current;
            if (lay == null) return;
            if (!PixelLayer(L.T("색 조절", "Adjust Color"))) return;

            var cover = SelectionCoverFor(lay);
            var orig = lay.PixelsRead.Clone();      // 미리보기의 기준이 될 원본

            Push();                                  // 여기서부터 되돌릴 수 있다
            var target = lay.EditPixels();           // 기록 시 복사가 여기서 일어난다

            var d = new AdjustSheet(delegate(Ops.Adjust a)
            {
                Ops.ApplyAdjust(target, orig, cover, a);
                lay.Touch();
                RefreshBase(null);
            });
            d.Owner = this;
            bool ok = d.ShowDialog() == true;

            if (!ok || d.Value.IsIdentity)
            {
                Array.Copy(orig.P, target.P, orig.P.Length);
                lay.Touch();
                RefreshBase(null);
                CancelPush();      // 바뀐 게 없으니 되돌리기 기록도 지운다
                Status(ok ? L.T("바뀐 게 없다", "No changes") : L.T("색 조절을 취소했다", "Adjust Color canceled"));
                return;
            }
            dirty = true;
            Status(selection != null ? L.T("고른 영역의 색을 조절했다", "Adjusted selection colors") : L.T("레이어 색을 조절했다", "Adjusted layer colors"));
        }

        void AutoAdjust()
        {
            var lay = doc.Current;
            if (lay == null) return;
            if (!PixelLayer(L.T("자동 보정", "Auto Adjust"))) return;
            var cover = SelectionCoverFor(lay);
            var a = Ops.AutoLevels(lay.PixelsRead, cover);
            if (a.IsIdentity) { Status(L.T("손볼 것이 없다", "Nothing to adjust")); return; }

            Push();
            var orig = lay.PixelsRead.Clone();
            Ops.ApplyAdjust(lay.EditPixels(), orig, cover, a);
            RefreshBase(null);
            Status(L.F("자동 보정 — 밝기 {0:+0;-0;0}, 대비 {1:+0;-0;0}", "Auto Adjust — brightness {0:+0;-0;0}, contrast {1:+0;-0;0}", a.Brightness, a.Contrast));
        }

        void RunCutout() { RunCutout(false); }

        void RunCutout(bool precise)
        {
            var lay = doc.Current;
            if (lay == null) return;
            if (!PixelLayer(L.T("누끼", "Cutout"))) return;

            // 레이어가 문서보다 작을 수 있으니 그 레이어의 픽셀로 분석한다.
            var pixels = lay.PixelsRead;
            Cutout.Run(this, pixels, precise, delegate(byte[] raw, string err)
            {
                if (err != null) { Status(err); return; }
                if (doc.Current != lay || lay.PixelsRead != pixels) { Status(L.T("그 사이 레이어가 바뀌어 누끼를 접었다", "The layer changed in the meantime — cutout canceled")); return; }
                RefineCutout(lay, raw);
            });
        }

        // AI 마스크를 슬라이더로 다듬어 레이어 마스크에 얹는다. 배경 픽셀은 그대로 두고 가리기만 한다.
        void RefineCutout(Layer lay, byte[] raw)
        {
            var guide = lay.PixelsRead;
            byte[] before = lay.HasMask ? (byte[])lay.MaskRead.Clone() : null;

            Push();
            var target = lay.EditMask();

            Action<byte[]> show = delegate(byte[] keep)
            {
                if (before != null) Array.Copy(before, target, target.Length);
                else Ops.MaskFill(target, 255);
                Ops.MaskApplyKeep(target, keep);
                lay.Touch();
                RefreshBase(null);
            };

            var last = Settings.Current.matte;
            var init = last != null
                ? new MatteSettings { Refine = last[0], Contrast = last[1], Shift = last[2] }
                : new MatteSettings();
            show(Matte.Refine(raw, guide, init, Matte.PreviewLimit));

            var d = new CutoutSheet(init, delegate(MatteSettings s)
            {
                show(Matte.Refine(raw, guide, s, Matte.PreviewLimit));
            });
            d.Owner = this;
            Status(L.T("누끼 다듬기 — 슬라이더를 끌면 바로 보인다", "Refine cutout — drag the sliders to preview"));

            if (d.ShowDialog() != true)
            {
                if (before != null) Array.Copy(before, target, target.Length);
                else lay.ClearMask();
                lay.Touch();
                RefreshBase(null);
                CancelPush();
                Status(L.T("누끼를 취소했다", "Cutout canceled"));
                return;
            }

            var v = d.Value;
            Settings.Current.matte = new[] { (int)v.Refine, (int)v.Contrast, (int)v.Shift };
            // 미리보기는 줄인 사본으로 했으니 확정할 때 원래 크기로 한 번 더 한다.
            show(Matte.Refine(raw, guide, v, int.MaxValue));
            Ops.Defringe(lay.EditPixels(), lay.MaskRead, 2);
            RefreshBase(null);
            dirty = true;
            Status(L.T("누끼를 땄다 — 잘려나간 곳은 복구 브러시로 되살리고, 남은 배경은 배경 지우개로 다듬어라", "Subject cut out — use Restore to paint back anything cut, and Background Eraser to clean up leftovers"));
            SelectTool(Tool.Restore);
        }

        // 화면에 보이는 그림에서 피사체를 찾아 선택 영역으로 잡는다. 누끼와 같은 모양을 지우는 대신 고른다.
        void SelectSubject()
        {
            if (doc == null) return;
            var shown = doc.Flatten();
            Cutout.Run(this, shown, delegate(byte[] raw, string err)
            {
                if (err != null) { Status(err); return; }
                if (shown.W != doc.W || shown.H != doc.H) { Status(L.T("그 사이 그림 크기가 바뀌었다", "The image size changed in the meantime")); return; }
                var keep = Matte.Refine(raw, shown, new MatteSettings(), int.MaxValue);
                var g = Matte.Trace(keep, doc.W, doc.H, 128, 0, 0);
                if (g == null) { Status(L.T("피사체를 찾지 못했다", "No subject found")); return; }

                var sel = new Selection { W = doc.W, H = doc.H, Geom = g, Mask = keep };
                var b = g.Bounds;
                sel.Bounds = D.Rectangle.FromLTRB((int)b.Left, (int)b.Top, (int)b.Right, (int)b.Bottom);
                selection = sel;
                SelectTool(Tool.Marquee);
                DrawSelectionChrome();
                BuildOptions();
                Status(L.T("피사체를 골랐다  ", "Subject selected  ") + sel.Bounds.Width + " × " + sel.Bounds.Height +
                       L.T("    Ctrl+Shift+I 반전 · Ctrl+D 해제", "    Ctrl+Shift+I to invert · Ctrl+D to deselect"));
            });
        }

        // WindowStyle=None 창은 테두리가 없어 기본 크기 조절 손잡이가 안 잡힌다.
        // 가장자리 몇 픽셀을 직접 테두리라고 알려주면 윈도가 평소처럼 끌어서 조절하게 해준다.
        // (스냅과 더블클릭 최대화도 그대로 따라온다.)
        IntPtr ResizeHook(IntPtr hwnd, int msg, IntPtr wp, IntPtr lp, ref bool handled)
        {
            const int WM_NCHITTEST = 0x0084;
            if (msg == Native.WM_GETMINMAXINFO)
            {
                Native.ClampMaximize(hwnd, lp);
                return IntPtr.Zero;
            }
            if (msg != WM_NCHITTEST) return IntPtr.Zero;
            if (WindowState == WindowState.Maximized) return IntPtr.Zero;

            RECT r;
            if (!Native.GetWindowRect(hwnd, out r)) return IntPtr.Zero;

            int x = (short)((long)lp & 0xFFFF);
            int y = (short)(((long)lp >> 16) & 0xFFFF);
            int edge = (int)Math.Round(7 * VisualTreeHelper.GetDpi(this).DpiScaleX);

            bool left = x < r.Left + edge, right = x > r.Right - edge;
            bool top = y < r.Top + edge, bottom = y > r.Bottom - edge;
            if (!left && !right && !top && !bottom) return IntPtr.Zero;

            const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13,
                      HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;
            int hit;
            if (top && left) hit = HTTOPLEFT;
            else if (top && right) hit = HTTOPRIGHT;
            else if (bottom && left) hit = HTBOTTOMLEFT;
            else if (bottom && right) hit = HTBOTTOMRIGHT;
            else if (left) hit = HTLEFT;
            else if (right) hit = HTRIGHT;
            else if (top) hit = HTTOP;
            else hit = HTBOTTOM;

            handled = true;
            return new IntPtr(hit);
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (xfOn) CommitTransform();
            if (dirty)
            {
                bool close = ConfirmSheet.Ask(this, L.T("저장 안 했는데", "Unsaved changes"),
                    L.T("손댄 게 남아 있다. 그냥 닫으면 사라진다.", "You have unsaved edits. They'll be lost if you close now."),
                    L.T("ㅇㅇ, 닫아", "Close"), L.T("ㄴㄴ", "Cancel"), true);
                if (!close) { e.Cancel = true; return; }
            }
            Settings.Current.Save();
            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            open_.Remove(this);
            base.OnClosed(e);
        }
    }
}
