// Money Shot — 캡처 직후 뜨는 썸네일 카드
//
// 클립보드 복사는 이미 끝난 상태로 뜬다. 이 카드는 "필요하면 더 할 수 있다"는 안내일 뿐이라
// 무시하면 스스로 사라지고, 그동안 아무것도 막지 않는다.
//   클릭   편집창
//   드래그 다른 앱으로 파일 떨구기
//   우클릭 메뉴
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
using System.Windows.Threading;
using D = System.Drawing;

namespace MoneyShot
{
    public class ThumbCard : Window
    {
        static readonly List<ThumbCard> live = new List<ThumbCard>();

        readonly D.Bitmap image;
        readonly string savedPath;
        double S = 1.0;
        int cardW, cardH;           // 물리 픽셀
        int homeX, homeY;           // 최종 자리
        int offX;                   // 오른쪽에서 밀려들어오는 거리

        DispatcherTimer life;
        DateTime animStart;
        bool animIn, animating, closing;
        double animFrom, animTo;
        Point pressPt;
        bool pressed, dragged;
        Border shell;
        Grid actions;

        public static void Show(D.Bitmap bmp, string savedPath)
        {
            if (!Settings.Current.showThumbnail) { bmp.Dispose(); return; }
            var c = new ThumbCard(bmp, savedPath);
            c.Show();
        }

        ThumbCard(D.Bitmap bmp, string path)
        {
            image = bmp; savedPath = path;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = false;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            Background = Theme.BrGlassFallback;
            Title = "Money Shot";
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -4000; Top = -4000; Width = 1; Height = 1;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            Glass.MakeToolWindow(this, true);
            bool glass = Glass.Apply(this, true);
            S = VisualTreeHelper.GetDpi(this).DpiScaleX;

            Build(glass);
            Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            cardW = (int)Math.Round(DesiredSize.Width * S);
            cardH = (int)Math.Round(DesiredSize.Height * S);

            Place();
            var hwnd = new WindowInteropHelper(this).Handle;
            Native.SetWindowPos(hwnd, IntPtr.Zero, homeX + offX, homeY, cardW, cardH,
                                Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);

            live.Add(this);
            Restack();
            SlideIn();
            StartLife();
        }

        // 커서가 있는 모니터의 오른쪽 아래. 작업표시줄을 피해 작업 영역 기준으로 잡는다.
        void Place()
        {
            POINT p; Native.GetCursorPos(out p);
            var mon = Native.MonitorFromPoint(p, Native.MONITOR_DEFAULTTONEAREST);
            var mi = new MONITORINFOEX();
            mi.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(MONITORINFOEX));
            RECT work;
            if (Native.GetMonitorInfo(mon, ref mi)) work = mi.rcWork;
            else work = Native.GetVirtualScreen();

            int margin = (int)Math.Round(18 * S);
            homeX = work.Right - cardW - margin;
            homeY = work.Bottom - cardH - margin;
            offX = cardW + margin * 2;          // 화면 밖에서 출발
        }

        void Build(bool glass)
        {
            double imgMax = 232, imgMaxH = 168;
            double iw = image.Width, ih = image.Height;
            double scale = Math.Min(imgMax / iw, imgMaxH / ih);
            if (scale > 1) scale = 1;
            double dw = Math.Max(48, Math.Round(iw * scale)), dh = Math.Max(36, Math.Round(ih * scale));

            var img = new Image
            {
                Source = Cap.ToSource(image),
                Width = dw, Height = dh,
                Stretch = Stretch.Fill
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);

            // 투명 영역이 있으면 체크무늬 위에 얹어 보여준다.
            var imgHost = new Grid { Width = dw, Height = dh };
            imgHost.Children.Add(new Rectangle
            {
                Fill = Checker(8),
                RadiusX = 0, RadiusY = 0
            });
            imgHost.Children.Add(img);

            var imgClip = new Border
            {
                CornerRadius = new CornerRadius(7),
                ClipToBounds = true,
                Child = imgHost,
                BorderThickness = new Thickness(1),
                BorderBrush = Theme.Alpha(Colors.White, 0x1C)
            };

            var caption = new TextBlock
            {
                Text = image.Width + " × " + image.Height + (savedPath != null ? L.T("  ·  저장됨", "  ·  Saved") : ""),
                Foreground = Theme.BrMuted,
                FontFamily = Theme.Mono,
                FontSize = 10.5,
                Margin = new Thickness(2, 7, 2, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            // 마우스를 올리면 나타나는 빠른 동작
            actions = new Grid { Opacity = 0, Margin = new Thickness(0, 7, 0, 0) };
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            row.Children.Add(Mini("edit", L.T("편집", "Edit"), delegate { OpenEditor(); }));
            row.Children.Add(Mini("save", L.T("저장", "Save"), delegate { SaveAs(); }));
            row.Children.Add(Mini("close", L.T("닫기", "Close"), delegate { Dismiss(); }));
            actions.Children.Add(row);

            var bottom = new Grid();
            bottom.Children.Add(caption);
            bottom.Children.Add(actions);

            // 찍을 때마다 한마디. 살짝 기울여 튀어나오게 한다.
            var shout = new TextBlock
            {
                Text = "Money~~!!",
                Foreground = Theme.BrAccent,
                FontFamily = Theme.UI,
                FontSize = 17,
                FontWeight = FontWeights.Black,
                Margin = new Thickness(2, 0, 0, 7),
                HorizontalAlignment = HorizontalAlignment.Left,
                RenderTransformOrigin = new Point(0, 0.5)
            };
            var pop = new ScaleTransform(0.4, 0.4);
            var tg = new TransformGroup();
            tg.Children.Add(pop);
            tg.Children.Add(new RotateTransform(-5));
            shout.RenderTransform = tg;
            shout.Loaded += delegate
            {
                var a = new System.Windows.Media.Animation.DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(520))
                {
                    BeginTime = TimeSpan.FromMilliseconds(120),
                    EasingFunction = new System.Windows.Media.Animation.ElasticEase { Oscillations = 2, Springiness = 4 }
                };
                pop.BeginAnimation(ScaleTransform.ScaleXProperty, a);
                pop.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            };

            var stack = new StackPanel();
            stack.Children.Add(shout);
            stack.Children.Add(imgClip);
            stack.Children.Add(bottom);

            shell = new Border
            {
                Padding = new Thickness(10, 10, 10, 8),
                CornerRadius = new CornerRadius(12),
                Background = glass ? Brushes.Transparent : Theme.BrGlassFallback,
                BorderThickness = new Thickness(glass ? 0 : 1),
                BorderBrush = Theme.BrBorder,
                Child = stack
            };
            if (!glass)
                shell.Effect = new DropShadowEffect
                { BlurRadius = 24, ShadowDepth = 5, Direction = 270, Opacity = 0.5, Color = Colors.Black };

            Content = shell;

            MouseEnter += delegate
            {
                if (life != null) life.Stop();
                caption.BeginAnimation(OpacityProperty, Theme.A(caption.Opacity, 0, 120));
                actions.BeginAnimation(OpacityProperty, Theme.A(actions.Opacity, 1, 140));
            };
            MouseLeave += delegate
            {
                if (!pressed)
                {
                    caption.BeginAnimation(OpacityProperty, Theme.A(caption.Opacity, 1, 140));
                    actions.BeginAnimation(OpacityProperty, Theme.A(actions.Opacity, 0, 120));
                    StartLife();
                }
            };
            MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            { pressed = true; dragged = false; pressPt = e.GetPosition(this); };
            MouseMove += OnMove;
            MouseLeftButtonUp += delegate
            {
                if (pressed && !dragged) OpenEditor();
                pressed = false;
            };
            MouseRightButtonUp += delegate { ShowMenu(); };
            ContextMenu = null;
        }

        static DrawingBrush Checker(int cell)
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x30)),
                null, new RectangleGeometry(new Rect(0, 0, cell * 2, cell * 2))));
            var dark = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x25));
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

        Border Mini(string icon, string tip, Action act)
        {
            var bd = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(6, 5, 6, 5),
                Margin = new Thickness(4, 0, 0, 0),
                Background = Theme.Alpha(Colors.White, 0x10),
                Child = Icons.Boxed(icon, 13, Theme.BrText)
            };
            bd.Cursor = Cursors.Hand;
            bd.ToolTip = tip;
            bd.MouseEnter += delegate { bd.Background = Theme.Alpha(Theme.Accent, 0x33); };
            bd.MouseLeave += delegate { bd.Background = Theme.Alpha(Colors.White, 0x10); };
            bd.MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs a) { a.Handled = true; };
            bd.MouseLeftButtonUp += delegate { act(); };
            return bd;
        }

        void OnMove(object s, MouseEventArgs e)
        {
            if (!pressed || dragged) return;
            var p = e.GetPosition(this);
            if (Math.Abs(p.X - pressPt.X) < 5 && Math.Abs(p.Y - pressPt.Y) < 5) return;

            dragged = true;
            pressed = false;
            if (life != null) life.Stop();
            try
            {
                string file = savedPath != null && System.IO.File.Exists(savedPath)
                    ? savedPath : Cap.WriteTemp(image, "MoneyShot");
                var data = new DataObject();
                data.SetFileDropList(new System.Collections.Specialized.StringCollection { file });
                data.SetImage(Cap.ToSource(image));
                DragDrop.DoDragDrop(this, data, DragDropEffects.Copy);
            }
            catch { }
            Dismiss();
        }

        void ShowMenu()
        {
            if (life != null) life.Stop();
            var m = new ContextMenu { Background = Theme.BrCard, Foreground = Theme.BrText, BorderBrush = Theme.BrBorder };
            m.Items.Add(Item(L.T("편집창에서 열기", "Open in Editor"), delegate { OpenEditor(); }));
            m.Items.Add(Item(L.T("다른 이름으로 저장…", "Save As…"), delegate { SaveAs(); }));
            m.Items.Add(Item(L.T("클립보드에 다시 복사", "Copy to Clipboard Again"), delegate { Cap.ToClipboard(image); Dismiss(); }));
            if (savedPath != null)
                m.Items.Add(Item(L.T("탐색기에서 보기", "Show in Explorer"), delegate
                {
                    try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + savedPath + "\""); }
                    catch { }
                    Dismiss();
                }));
            m.Items.Add(new Separator());
            m.Items.Add(Item(L.T("닫기", "Close"), delegate { Dismiss(); }));
            m.Closed += delegate { if (!IsMouseOver) StartLife(); };
            m.PlacementTarget = this;
            m.IsOpen = true;
        }

        MenuItem Item(string head, Action act)
        {
            var mi = new MenuItem { Header = head, Foreground = Theme.BrText, Background = Theme.BrCard };
            mi.Click += delegate { act(); };
            return mi;
        }

        void OpenEditor()
        {
            var copy = Cap.Clone32(image);
            Dismiss();
            App.OpenEditor(copy, savedPath);
        }

        void SaveAs()
        {
            var p = App.SaveAsDialog(image);
            if (p != null) Dismiss();
            else StartLife();
        }

        // ---------- 생명주기 ----------

        void StartLife()
        {
            if (closing) return;
            if (life == null)
            {
                life = new DispatcherTimer();
                life.Tick += delegate { life.Stop(); Dismiss(); };
            }
            life.Stop();
            life.Interval = TimeSpan.FromSeconds(Math.Max(2, Settings.Current.thumbnailSeconds));
            life.Start();
        }

        void SlideIn()
        {
            animIn = true; animFrom = offX; animTo = 0;
            animStart = DateTime.UtcNow;
            if (!animating) { animating = true; CompositionTarget.Rendering += OnFrame; }
        }

        void Dismiss()
        {
            if (closing) return;
            closing = true;
            if (life != null) life.Stop();
            animIn = false; animFrom = offX; animTo = cardW + 40 * S;
            animStart = DateTime.UtcNow;
            if (!animating) { animating = true; CompositionTarget.Rendering += OnFrame; }
        }

        void OnFrame(object s, EventArgs e)
        {
            double ms = (DateTime.UtcNow - animStart).TotalMilliseconds;
            double dur = animIn ? 420 : 240;
            double t = Math.Min(1, ms / dur);
            double k = animIn ? BackOut(t, 1.15) : t * t;      // 들어올 땐 살짝 지나쳤다 제자리, 나갈 땐 가속
            offX = (int)Math.Round(animFrom + (animTo - animFrom) * k);

            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
                Native.SetWindowPos(hwnd, IntPtr.Zero, homeX + offX, homeY, cardW, cardH,
                                    Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
            if (!animIn) Opacity = Math.Max(0, 1 - t * 1.4);

            if (t >= 1)
            {
                animating = false;
                CompositionTarget.Rendering -= OnFrame;
                if (!animIn) Close();
            }
        }

        // 되돌아오며 살짝 지나치는 감속 곡선.
        static double BackOut(double t, double s)
        {
            t -= 1;
            return t * t * ((s + 1) * t + s) + 1;
        }

        // 여러 장을 연달아 찍으면 위로 쌓는다.
        static void Restack()
        {
            int gap = 0;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var c = live[i];
                if (c.closing) continue;
                if (gap > 0)
                {
                    c.homeY -= gap;
                    var h = new WindowInteropHelper(c).Handle;
                    if (h != IntPtr.Zero)
                        Native.SetWindowPos(h, IntPtr.Zero, c.homeX + c.offX, c.homeY, c.cardW, c.cardH,
                                            Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
                }
                gap += c.cardH + (int)(10 * c.S);
                if (live.Count - i >= 3) { c.Dismiss(); }   // 넉 장째부터는 아래 것을 내보낸다
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            live.Remove(this);
            try { image.Dispose(); } catch { }
            base.OnClosed(e);
        }
    }
}
