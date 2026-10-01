// Money Shot — 영역 선택 오버레이
//
// 동작 원리: 단축키가 눌린 순간 가상 데스크톱 전체를 한 장 떠서 그것을 전체화면 창에 그린다.
// 이후 모든 상호작용은 그 정지 화면 위에서 일어나므로 깜빡임도, 창 순서 다툼도 없다.
// 확대경과 색상값도 이 정지 화면에서 읽으므로 언제나 캡처 결과와 일치한다.
using System;
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
    public enum PostAction { Copy, Edit, Save, Cancel }

    public class CaptureResult
    {
        public D.Bitmap Image;
        public RECT Bounds;
        public PostAction Action;
    }

    // 흐려둔 배경을 잘라 뒤에 깔아, 아크릴이 없는 창에서도 유리 질감을 낸다.
    //
    // 흐림은 여기서 계산하지 않는다. 패널이 움직일 때마다 블러를 돌리면 그 자체로 버벅인다.
    // 바깥에서 미리 흐려둔(그리고 줄여둔) 그림을 받아, 필요한 자리만 잘라 늘린다.
    public class VibrancyPanel : Grid
    {
        readonly Image back = new Image { Stretch = Stretch.Fill };
        readonly Rectangle tint = new Rectangle();
        readonly Grid inner = new Grid();
        BitmapSource source;
        int div = 1;

        public Grid Content_ { get { return inner; } }

        public VibrancyPanel(double cornerRadius, double s)
        {
            tint.Fill = Theme.Alpha(Theme.Card, 0xC8);

            var host = new Grid();
            host.Children.Add(back);
            host.Children.Add(tint);

            var clip = new Border
            {
                CornerRadius = new CornerRadius(cornerRadius),
                ClipToBounds = true,
                Child = host
            };

            var outline = new Border
            {
                CornerRadius = new CornerRadius(cornerRadius),
                BorderThickness = new Thickness(Math.Max(1, s)),
                BorderBrush = Theme.Alpha(Colors.White, 0x24),
                Child = inner
            };

            Children.Add(clip);
            Children.Add(outline);
        }

        // src는 이미 흐려진 그림이고, divisor는 원본 대비 몇 분의 일로 줄었는지다.
        public void SetSource(BitmapSource src, int divisor)
        {
            source = src;
            div = Math.Max(1, divisor);
        }

        public void Refresh(double x, double y, double w, double h)
        {
            if (source == null || w <= 0 || h <= 0) return;
            int cx = (int)Math.Max(0, Math.Floor(x / div));
            int cy = (int)Math.Max(0, Math.Floor(y / div));
            int cw = (int)Math.Max(1, Math.Ceiling(w / div));
            int ch = (int)Math.Max(1, Math.Ceiling(h / div));
            if (cx + cw > source.PixelWidth) cw = source.PixelWidth - cx;
            if (cy + ch > source.PixelHeight) ch = source.PixelHeight - cy;
            if (cw <= 0 || ch <= 0) return;
            try
            {
                var crop = new CroppedBitmap(source, new Int32Rect(cx, cy, cw, ch));
                crop.Freeze();
                back.Source = crop;
            }
            catch { }
        }
    }

    public class RegionOverlay : Window
    {
        enum Mode { Idle, Dragging, Adjusting }
        enum Target { Region, Window }

        readonly RECT virt;
        readonly D.Bitmap shot;
        readonly BitmapSource shotSrc;
        readonly int[] px;          // 확대경 색상 조회용 BGRA 원본
        readonly int shotW, shotH;
        readonly BitmapSource blurSrc;   // 유리 패널 뒤에 깔 미리 흐려둔 배경
        const int BlurDiv = 4;           // 원본의 1/4로 줄여 흐린다
        readonly Action<CaptureResult> done;

        double S = 1.0;             // DPI 배율. 모든 UI 치수에 곱해 물리 픽셀로 맞춘다.
        Canvas canvas;
        Image imgScreen;
        Rectangle[] dim = new Rectangle[4];   // 선택 영역 바깥을 덮는 위·아래·왼·오른
        Rectangle selBorder;
        Rectangle[] handles = new Rectangle[8];
        Line crossH, crossV;
        VibrancyPanel badge, loupe, bar;
        TextBlock badgeText, loupeText, hintText;
        Image loupeImg;
        VibrancyPanel hint;

        Mode mode = Mode.Idle;
        Target target = Target.Region;
        Point anchor, cursor;
        Rect sel = Rect.Empty;
        int grabbing = -1;          // 잡고 있는 핸들 번호, -1이면 없음
        Point grabStart;
        Rect grabSelStart;
        bool moving;
        bool finished;

        public static bool IsOpen { get; private set; }

        bool pickOnly;        // 참이면 이미지를 자르지 않고 고른 영역만 돌려준다 (스크롤 캡처의 대상 지정)
        bool requireConfirm;  // 참이면 손을 떼도 바로 확정하지 않는다 (자동으로 찾아둔 영역을 다듬으라고)
        RECT? preset;

        public static void Begin(Action<CaptureResult> callback) { Begin(callback, false, null); }
        public static void Begin(Action<CaptureResult> callback, bool pickOnly) { Begin(callback, pickOnly, null); }

        public static void Begin(Action<CaptureResult> callback, bool pickOnly, RECT? preset)
        {
            if (IsOpen) return;
            var v = Native.GetVirtualScreen();
            var bmp = Cap.Screen(v, false);
            var w = new RegionOverlay(v, bmp, callback);
            w.pickOnly = pickOnly;
            w.preset = preset;
            w.requireConfirm = preset.HasValue;
            IsOpen = true;
            w.Show();
            w.Activate();
        }

        RegionOverlay(RECT v, D.Bitmap bmp, Action<CaptureResult> callback)
        {
            virt = v; shot = bmp; done = callback;
            shotW = bmp.Width; shotH = bmp.Height;
            shotSrc = Cap.ToSource(bmp);
            px = ReadPixels(bmp);
            blurSrc = MakeBlurred(bmp);

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = false;
            ShowInTaskbar = false;
            Topmost = true;
            Background = Brushes.Black;
            Title = "Money Shot";
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = 0; Top = 0; Width = 1; Height = 1;
            Cursor = Cursors.Cross;
            UseLayoutRounding = false;
            SnapsToDevicePixels = true;
        }

        // 화면을 1/4로 줄여 한 번만 흐려둔다. 유리 패널은 전부 여기서 잘라 쓴다.
        // 박스 블러 세 번이면 가우시안과 눈으로 구분되지 않고, 줄여놔서 비용도 작다.
        static BitmapSource MakeBlurred(D.Bitmap shot)
        {
            try
            {
                int w = Math.Max(1, shot.Width / BlurDiv), h = Math.Max(1, shot.Height / BlurDiv);
                using (var small = Cap.Resize(shot, w, h))
                {
                    var c = Canvas32.From(small);
                    Ops.Blur(c, new D.Rectangle(0, 0, w, h), 5);
                    using (var blurred = c.ToBitmap())
                        return Cap.ToSource(blurred);
                }
            }
            catch { return null; }
        }

        static int[] ReadPixels(D.Bitmap b)
        {
            var data = b.LockBits(new D.Rectangle(0, 0, b.Width, b.Height),
                                  D.Imaging.ImageLockMode.ReadOnly, D.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                var arr = new int[b.Width * b.Height];
                for (int y = 0; y < b.Height; y++)
                    System.Runtime.InteropServices.Marshal.Copy(
                        data.Scan0 + y * data.Stride, arr, y * b.Width, b.Width);
                return arr;
            }
            finally { b.UnlockBits(data); }
        }

        Color At(int x, int y)
        {
            if (x < 0 || y < 0 || x >= shotW || y >= shotH) return Colors.Black;
            int v = px[y * shotW + x];
            return Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwnd = new WindowInteropHelper(this).Handle;
            Glass.MakeToolWindow(this, false);
            // 물리 픽셀로 직접 배치한다. WPF의 DIP 환산을 거치면 혼합 DPI에서 어긋난다.
            Native.SetWindowPos(hwnd, IntPtr.Zero, virt.Left, virt.Top, virt.W, virt.H,
                                Native.SWP_NOZORDER | Native.SWP_SHOWWINDOW);
            S = VisualTreeHelper.GetDpi(this).DpiScaleX;
            Build();
        }

        protected override void OnDpiChanged(DpiScale o, DpiScale n)
        {
            base.OnDpiChanged(o, n);
            S = n.DpiScaleX;
            if (canvas != null) canvas.LayoutTransform = new ScaleTransform(1 / S, 1 / S);
        }

        void Build()
        {
            canvas = new Canvas { Width = shotW, Height = shotH, ClipToBounds = true };
            canvas.LayoutTransform = new ScaleTransform(1 / S, 1 / S);
            canvas.Background = Brushes.Black;

            imgScreen = new Image { Source = shotSrc, Width = shotW, Height = shotH, Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(imgScreen, BitmapScalingMode.NearestNeighbor);
            Canvas.SetLeft(imgScreen, 0); Canvas.SetTop(imgScreen, 0);
            canvas.Children.Add(imgScreen);

            for (int i = 0; i < 4; i++)
            {
                dim[i] = new Rectangle { Fill = Brushes.Black, Opacity = 0, IsHitTestVisible = false };
                canvas.Children.Add(dim[i]);
            }

            crossH = Guide(); crossV = Guide();
            canvas.Children.Add(crossH); canvas.Children.Add(crossV);

            selBorder = new Rectangle
            {
                Stroke = Theme.BrAccent,
                StrokeThickness = Math.Max(1, Math.Round(1.5 * S)),
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed
            };
            canvas.Children.Add(selBorder);

            for (int i = 0; i < 8; i++)
            {
                var h = new Rectangle
                {
                    Width = 9 * S, Height = 9 * S,
                    Fill = Theme.BrAccent,
                    Stroke = new SolidColorBrush(Color.FromArgb(0xCC, 11, 11, 15)),
                    StrokeThickness = Math.Max(1, Math.Round(1.5 * S)),
                    RadiusX = 2 * S, RadiusY = 2 * S,
                    Visibility = Visibility.Collapsed,
                    IsHitTestVisible = false
                };
                handles[i] = h;
                canvas.Children.Add(h);
            }

            // 치수 배지
            badge = new VibrancyPanel(8 * S, S); badge.SetSource(blurSrc, BlurDiv);
            badgeText = new TextBlock
            {
                Foreground = Theme.BrText, FontFamily = Theme.Mono, FontSize = 12 * S,
                Margin = new Thickness(9 * S, 5 * S, 9 * S, 6 * S),
                VerticalAlignment = VerticalAlignment.Center
            };
            badge.Content_.Children.Add(badgeText);
            badge.Visibility = Visibility.Collapsed;
            canvas.Children.Add(badge);

            // 확대경
            loupe = new VibrancyPanel(10 * S, S); loupe.SetSource(blurSrc, BlurDiv);
            var lg = new Grid();
            lg.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            lg.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var frame = new Border
            {
                Width = 136 * S, Height = 136 * S,
                Margin = new Thickness(6 * S, 6 * S, 6 * S, 0),
                CornerRadius = new CornerRadius(6 * S),
                ClipToBounds = true,
                BorderThickness = new Thickness(Math.Max(1, S)),
                BorderBrush = Theme.Alpha(Colors.White, 0x22)
            };
            var fg = new Grid();
            loupeImg = new Image { Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(loupeImg, BitmapScalingMode.NearestNeighbor);
            fg.Children.Add(loupeImg);
            fg.Children.Add(BuildCrosshair(136 * S, 8 * S));
            frame.Child = fg;
            Grid.SetRow(frame, 0); lg.Children.Add(frame);

            loupeText = new TextBlock
            {
                Foreground = Theme.BrMuted, FontFamily = Theme.Mono, FontSize = 11 * S,
                Margin = new Thickness(8 * S, 5 * S, 8 * S, 7 * S),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetRow(loupeText, 1); lg.Children.Add(loupeText);
            loupe.Content_.Children.Add(lg);
            loupe.Visibility = Visibility.Collapsed;
            canvas.Children.Add(loupe);

            // 하단 안내
            hint = new VibrancyPanel(10 * S, S); hint.SetSource(blurSrc, BlurDiv);
            hintText = new TextBlock
            {
                Foreground = Theme.BrMuted, FontFamily = Theme.UI, FontSize = 12 * S,
                Margin = new Thickness(14 * S, 8 * S, 14 * S, 9 * S)
            };
            hint.Content_.Children.Add(hintText);
            canvas.Children.Add(hint);

            // 확정 툴바 (조정 모드)
            bar = BuildBar();
            bar.Visibility = Visibility.Collapsed;
            canvas.Children.Add(bar);

            if (preset.HasValue)
            {
                var pr = preset.Value;
                sel = Clamp(new Rect(pr.Left - virt.Left, pr.Top - virt.Top, pr.W, pr.H));
                mode = Mode.Adjusting;
            }

            Content = canvas;
            UpdateHint();

            MouseMove += OnMove;
            MouseLeftButtonDown += OnDown;
            MouseLeftButtonUp += OnUp;
            MouseRightButtonUp += delegate { Cancel(); };
            KeyDown += OnKey;
            Loaded += delegate
            {
                Focus();
                Keyboard.Focus(this);
                foreach (var d in dim) d.BeginAnimation(OpacityProperty, Theme.A(0, 0.46, 150, Theme.Out));
                RedrawDim();
                PlaceHint();
            };
        }

        // 확대경 십자선.
        //
        // 선이 가운데 픽셀을 덮어버리면 정작 겨눈 색을 못 본다. 그래서 네 방향에서 가운데 칸
        // 직전까지만 긋고, 칸 자체는 테두리로 표시한다.
        // 어떤 배경 위에 놓일지 모르므로 어두운 선을 먼저 깔고 그 위에 민트를 얹는다.
        FrameworkElement BuildCrosshair(double frame, double cell)
        {
            var host = new Canvas { Width = frame, Height = frame, IsHitTestVisible = false };
            double c = frame / 2;
            double half = cell / 2;
            double gap = half + 2 * S;          // 가운데 칸에서 조금 더 띄운다
            double t = Math.Max(1, Math.Round(S));

            var shade = Theme.Alpha(Colors.Black, 0x70);
            var mint = Theme.Alpha(Theme.Accent, 0xE0);

            // 가로: 왼쪽 끝 → 칸 앞, 칸 뒤 → 오른쪽 끝
            AddBar(host, 0, c - t / 2, c - gap, t, shade, mint);
            AddBar(host, c + gap, c - t / 2, frame - (c + gap), t, shade, mint);
            // 세로
            AddBar(host, c - t / 2, 0, t, c - gap, shade, mint);
            AddBar(host, c - t / 2, c + gap, t, frame - (c + gap), shade, mint);

            // 겨냥한 픽셀 한 칸
            var outer = new Rectangle
            {
                Width = cell + 2 * t, Height = cell + 2 * t,
                Stroke = shade, StrokeThickness = t, Fill = Brushes.Transparent
            };
            Canvas.SetLeft(outer, c - cell / 2 - t);
            Canvas.SetTop(outer, c - cell / 2 - t);
            host.Children.Add(outer);

            var inner = new Rectangle
            {
                Width = cell, Height = cell,
                Stroke = Theme.BrAccent, StrokeThickness = Math.Max(1, Math.Round(1.5 * S)),
                Fill = Brushes.Transparent
            };
            Canvas.SetLeft(inner, c - cell / 2);
            Canvas.SetTop(inner, c - cell / 2);
            host.Children.Add(inner);

            return host;
        }

        // 어두운 선 위에 민트 선을 겹쳐 어느 배경에서도 보이게 한다.
        static void AddBar(Canvas host, double x, double y, double w, double h,
                           Brush shade, Brush mint)
        {
            if (w <= 0 || h <= 0) return;
            double grow = Math.Max(1, h < w ? h : w);
            var back = new Rectangle
            {
                Width = w + (h < w ? 0 : grow * 2),
                Height = h + (h < w ? grow * 2 : 0),
                Fill = shade
            };
            Canvas.SetLeft(back, h < w ? x : x - grow);
            Canvas.SetTop(back, h < w ? y - grow : y);
            host.Children.Add(back);

            var bar = new Rectangle { Width = w, Height = h, Fill = mint };
            Canvas.SetLeft(bar, x);
            Canvas.SetTop(bar, y);
            host.Children.Add(bar);
        }

        Line Guide()
        {
            return new Line
            {
                Stroke = Theme.Alpha(Theme.Accent, 0x55),
                StrokeThickness = Math.Max(1, Math.Round(S)),
                StrokeDashArray = new DoubleCollection { 4, 4 },
                IsHitTestVisible = false
            };
        }

        VibrancyPanel BuildBar()
        {
            var p = new VibrancyPanel(12 * S, S); p.SetSource(blurSrc, BlurDiv);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(5 * S) };
            if (pickOnly)
            {
                row.Children.Add(BarButton("check", L.T("이 영역으로 스크롤 캡처", "Capture This Area"), PostAction.Copy, true));
            }
            else
            {
                row.Children.Add(BarButton("copy", L.T("복사", "Copy"), PostAction.Copy, true));
                row.Children.Add(BarButton("edit", L.T("편집", "Edit"), PostAction.Edit, false));
                row.Children.Add(BarButton("save", L.T("저장", "Save"), PostAction.Save, false));
            }
            row.Children.Add(new Rectangle
            {
                Width = Math.Max(1, S), Margin = new Thickness(4 * S, 6 * S, 4 * S, 6 * S),
                Fill = Theme.Alpha(Colors.White, 0x1A)
            });
            row.Children.Add(BarButton("close", L.T("취소", "Cancel"), PostAction.Cancel, false));
            p.Content_.Children.Add(row);
            return p;
        }

        Border BarButton(string icon, string label, PostAction act, bool primary)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            var art = Icons.Boxed(icon, 15 * S, primary ? Theme.BrAccent : Theme.BrText);
            art.VerticalAlignment = VerticalAlignment.Center;
            content.Children.Add(art);
            content.Children.Add(new TextBlock
            {
                Text = label, FontFamily = Theme.UI, FontSize = 12.5 * S,
                Margin = new Thickness(7 * S, 0, 2 * S, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = primary ? Theme.BrAccent : Theme.BrText
            });

            var border = new Border
            {
                CornerRadius = new CornerRadius(8 * S),
                Padding = new Thickness(11 * S, 7 * S, 12 * S, 8 * S),
                Background = primary ? Theme.Alpha(Theme.Accent, 0x22) : Brushes.Transparent,
                Child = content
            };
            border.Cursor = Cursors.Hand;
            border.MouseEnter += delegate { if (!primary) border.Background = Theme.Alpha(Colors.White, 0x14); };
            border.MouseLeave += delegate { border.Background = primary ? Theme.Alpha(Theme.Accent, 0x22) : Brushes.Transparent; };
            border.MouseLeftButtonUp += delegate { Finish(act); };
            return border;
        }

        // ---------- 상호작용 ----------

        void OnKey(object s, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    if (mode == Mode.Adjusting) { sel = Rect.Empty; mode = Mode.Idle; Sync(); }
                    else Cancel();
                    e.Handled = true; break;

                case Key.Enter:
                    if (!sel.IsEmpty) Finish(PostAction.Copy);
                    e.Handled = true; break;

                case Key.Space:
                    target = target == Target.Region ? Target.Window : Target.Region;
                    if (target == Target.Window) { mode = Mode.Idle; sel = WindowRectAt(cursor); }
                    else if (mode == Mode.Idle) sel = Rect.Empty;
                    Sync(); UpdateHint();
                    e.Handled = true; break;

                case Key.E: if (!sel.IsEmpty) Finish(PostAction.Edit); e.Handled = true; break;
                case Key.S: if (!sel.IsEmpty) Finish(PostAction.Save); e.Handled = true; break;

                case Key.A:
                    if (Keyboard.Modifiers == ModifierKeys.Control)
                    {
                        var m = MonitorAt(cursor);
                        sel = m; mode = Mode.Adjusting; Sync(); e.Handled = true;
                    }
                    break;

                case Key.Left: case Key.Right: case Key.Up: case Key.Down:
                    if (!sel.IsEmpty)
                    {
                        double step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 10 : 1;
                        double dx = e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0;
                        double dy = e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0;
                        // Alt를 누르면 이동 대신 오른쪽·아래 변을 늘린다.
                        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0)
                            sel = new Rect(sel.X, sel.Y, Math.Max(1, sel.Width + dx), Math.Max(1, sel.Height + dy));
                        else
                            sel = new Rect(sel.X + dx, sel.Y + dy, sel.Width, sel.Height);
                        sel = Clamp(sel);
                        mode = Mode.Adjusting; Sync();
                        e.Handled = true;
                    }
                    break;
            }
        }

        void OnDown(object s, MouseButtonEventArgs e)
        {
            cursor = e.GetPosition(canvas);

            if (target == Target.Window)
            {
                sel = WindowRectAt(cursor);
                if ((Settings.Current.captureOnRelease || pickOnly) && !requireConfirm) { Finish(PostAction.Copy); return; }
                mode = Mode.Adjusting; target = Target.Region; Sync(); UpdateHint();
                return;
            }

            if (mode == Mode.Adjusting)
            {
                grabbing = HandleAt(cursor);
                if (grabbing >= 0) { grabStart = cursor; grabSelStart = sel; CaptureMouse(); return; }
                if (sel.Contains(cursor)) { moving = true; grabStart = cursor; grabSelStart = sel; CaptureMouse(); return; }
                // 바깥을 찍으면 새 선택 시작
            }

            anchor = cursor;
            sel = new Rect(anchor, anchor);
            mode = Mode.Dragging;
            CaptureMouse();
            Sync();
        }

        Point lastSynced = new Point(double.NaN, double.NaN);

        void OnMove(object s, MouseEventArgs e)
        {
            var now = e.GetPosition(canvas);
            // 같은 픽셀에 머무는 동안에는 다시 그릴 이유가 없다.
            if (mode == Mode.Idle && grabbing < 0 && !moving && target == Target.Region
                && Math.Abs(now.X - lastSynced.X) < 1 && Math.Abs(now.Y - lastSynced.Y) < 1) return;
            lastSynced = now;
            cursor = now;

            if (mode == Mode.Dragging)
            {
                sel = Clamp(Normalize(anchor, cursor, (Keyboard.Modifiers & ModifierKeys.Shift) != 0));
            }
            else if (mode == Mode.Adjusting && grabbing >= 0)
            {
                sel = Clamp(ResizeBy(grabSelStart, grabbing, cursor - grabStart));
            }
            else if (mode == Mode.Adjusting && moving)
            {
                var d = cursor - grabStart;
                sel = Clamp(new Rect(grabSelStart.X + d.X, grabSelStart.Y + d.Y, grabSelStart.Width, grabSelStart.Height));
            }
            else if (target == Target.Window)
            {
                sel = WindowRectAt(cursor);
            }
            else if (mode == Mode.Adjusting)
            {
                int h = HandleAt(cursor);
                Cursor = h >= 0 ? HandleCursor(h) : (sel.Contains(cursor) ? Cursors.SizeAll : Cursors.Cross);
            }

            Sync();
        }

        void OnUp(object s, MouseButtonEventArgs e)
        {
            if (IsMouseCaptured) ReleaseMouseCapture();

            if (mode == Mode.Dragging)
            {
                double dist = Math.Abs(cursor.X - anchor.X) + Math.Abs(cursor.Y - anchor.Y);
                if (dist < 4 * S)
                {
                    // 끌지 않고 그냥 찍었다 — 그 자리의 창을 잡는 뜻으로 받는다.
                    sel = WindowRectAt(cursor);
                    if ((Settings.Current.captureOnRelease || pickOnly) && !requireConfirm) { Finish(PostAction.Copy); return; }
                    mode = Mode.Adjusting; Sync(); UpdateHint(); return;
                }
                if ((Settings.Current.captureOnRelease || pickOnly) && !requireConfirm) { Finish(PostAction.Copy); return; }
                mode = Mode.Adjusting;
            }
            grabbing = -1; moving = false;
            Sync(); UpdateHint();
        }

        // ---------- 계산 ----------

        static Rect Normalize(Point a, Point b, bool square)
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
            return new Rect(Math.Round(x), Math.Round(y), Math.Round(w), Math.Round(h));
        }

        Rect Clamp(Rect r)
        {
            double x = Math.Max(0, Math.Min(r.X, shotW - 1));
            double y = Math.Max(0, Math.Min(r.Y, shotH - 1));
            double w = Math.Max(1, Math.Min(r.Width, shotW - x));
            double h = Math.Max(1, Math.Min(r.Height, shotH - y));
            return new Rect(Math.Round(x), Math.Round(y), Math.Round(w), Math.Round(h));
        }

        // 핸들 번호: 0 좌상 1 상 2 우상 3 우 4 우하 5 하 6 좌하 7 좌
        static Rect ResizeBy(Rect r, int h, Vector d)
        {
            double l = r.Left, t = r.Top, rt = r.Right, b = r.Bottom;
            if (h == 0 || h == 7 || h == 6) l += d.X;
            if (h == 2 || h == 3 || h == 4) rt += d.X;
            if (h == 0 || h == 1 || h == 2) t += d.Y;
            if (h == 4 || h == 5 || h == 6) b += d.Y;
            return new Rect(Math.Min(l, rt), Math.Min(t, b), Math.Abs(rt - l), Math.Abs(b - t));
        }

        Point[] HandlePoints(Rect r)
        {
            return new[]
            {
                new Point(r.Left, r.Top), new Point(r.Left + r.Width / 2, r.Top), new Point(r.Right, r.Top),
                new Point(r.Right, r.Top + r.Height / 2),
                new Point(r.Right, r.Bottom), new Point(r.Left + r.Width / 2, r.Bottom), new Point(r.Left, r.Bottom),
                new Point(r.Left, r.Top + r.Height / 2)
            };
        }

        int HandleAt(Point p)
        {
            if (sel.IsEmpty) return -1;
            var pts = HandlePoints(sel);
            double grab = 11 * S;
            for (int i = 0; i < 8; i++)
                if (Math.Abs(p.X - pts[i].X) <= grab && Math.Abs(p.Y - pts[i].Y) <= grab) return i;
            return -1;
        }

        static Cursor HandleCursor(int i)
        {
            switch (i)
            {
                case 0: case 4: return Cursors.SizeNWSE;
                case 2: case 6: return Cursors.SizeNESW;
                case 1: case 5: return Cursors.SizeNS;
                default: return Cursors.SizeWE;
            }
        }

        Rect WindowRectAt(Point p)
        {
            var pt = new POINT { X = (int)p.X + virt.Left, Y = (int)p.Y + virt.Top };
            IntPtr h = Native.WindowFromPoint(pt);
            if (h == IntPtr.Zero) return MonitorAt(p);
            IntPtr root = Native.GetAncestor(h, Native.GA_ROOT);
            if (root != IntPtr.Zero) h = root;
            // 오버레이 자신을 집었으면 모니터 전체로 대신한다.
            if (h == new WindowInteropHelper(this).Handle) return MonitorAt(p);
            var r = Cap.ClampToVirtual(Native.GetRealWindowRect(h));
            if (r.W <= 0 || r.H <= 0) return MonitorAt(p);
            return Clamp(new Rect(r.Left - virt.Left, r.Top - virt.Top, r.W, r.H));
        }

        Rect MonitorAt(Point p)
        {
            int sx = (int)p.X + virt.Left, sy = (int)p.Y + virt.Top;
            foreach (var m in Native.GetMonitors())
                if (sx >= m.Left && sx < m.Right && sy >= m.Top && sy < m.Bottom)
                    return new Rect(m.Left - virt.Left, m.Top - virt.Top, m.W, m.H);
            return new Rect(0, 0, shotW, shotH);
        }

        // ---------- 그리기 ----------

        void Sync()
        {
            RedrawDim();

            bool has = !sel.IsEmpty && sel.Width >= 1 && sel.Height >= 1;
            selBorder.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
            if (has)
            {
                Canvas.SetLeft(selBorder, sel.X); Canvas.SetTop(selBorder, sel.Y);
                selBorder.Width = sel.Width; selBorder.Height = sel.Height;
            }

            bool showHandles = has && mode == Mode.Adjusting;
            var pts = has ? HandlePoints(sel) : null;
            for (int i = 0; i < 8; i++)
            {
                handles[i].Visibility = showHandles ? Visibility.Visible : Visibility.Collapsed;
                if (showHandles)
                {
                    Canvas.SetLeft(handles[i], pts[i].X - handles[i].Width / 2);
                    Canvas.SetTop(handles[i], pts[i].Y - handles[i].Height / 2);
                }
            }

            bool guides = mode == Mode.Idle && target == Target.Region;
            crossH.Visibility = crossV.Visibility = guides ? Visibility.Visible : Visibility.Collapsed;
            if (guides)
            {
                crossH.X1 = 0; crossH.X2 = shotW; crossH.Y1 = crossH.Y2 = Math.Round(cursor.Y) + 0.5;
                crossV.Y1 = 0; crossV.Y2 = shotH; crossV.X1 = crossV.X2 = Math.Round(cursor.X) + 0.5;
            }

            UpdateBadge(has);
            UpdateLoupe();
            UpdateBar(has && mode == Mode.Adjusting);
        }

        // 선택 영역 바깥을 사각형 네 개로 덮는다.
        // 기하 연산(Exclude)을 매 프레임 돌리는 것보다 훨씬 싸다 — 좌표만 바꾸면 된다.
        void RedrawDim()
        {
            if (sel.IsEmpty || sel.Width < 1 || sel.Height < 1)
            {
                Put(dim[0], 0, 0, shotW, shotH);
                Put(dim[1], 0, 0, 0, 0);
                Put(dim[2], 0, 0, 0, 0);
                Put(dim[3], 0, 0, 0, 0);
                return;
            }
            Put(dim[0], 0, 0, shotW, sel.Y);                                   // 위
            Put(dim[1], 0, sel.Bottom, shotW, shotH - sel.Bottom);             // 아래
            Put(dim[2], 0, sel.Y, sel.X, sel.Height);                          // 왼
            Put(dim[3], sel.Right, sel.Y, shotW - sel.Right, sel.Height);      // 오른
        }

        static void Put(Rectangle r, double x, double y, double w, double h)
        {
            r.Width = Math.Max(0, w);
            r.Height = Math.Max(0, h);
            Canvas.SetLeft(r, x);
            Canvas.SetTop(r, y);
        }

        void UpdateBadge(bool has)
        {
            if (!has) { badge.Visibility = Visibility.Collapsed; return; }
            badge.Visibility = Visibility.Visible;
            badgeText.Text = (int)sel.Width + " × " + (int)sel.Height;
            badge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double bw = badge.DesiredSize.Width, bh = badge.DesiredSize.Height;

            double x = sel.X, y = sel.Y - bh - 8 * S;
            if (y < 4 * S) y = sel.Y + 8 * S;                       // 위쪽에 자리가 없으면 안쪽 위로
            if (y + bh > shotH) y = Math.Max(0, sel.Bottom - bh - 8 * S);
            if (x + bw > shotW) x = shotW - bw - 4 * S;
            Canvas.SetLeft(badge, Math.Round(x)); Canvas.SetTop(badge, Math.Round(y));
            badge.Refresh(x, y, bw, bh);
        }

        void UpdateLoupe()
        {
            if (!Settings.Current.showMagnifier || mode == Mode.Adjusting)
            { loupe.Visibility = Visibility.Collapsed; return; }

            int cx = (int)Math.Round(cursor.X), cy = (int)Math.Round(cursor.Y);
            if (cx < 0 || cy < 0 || cx >= shotW || cy >= shotH) { loupe.Visibility = Visibility.Collapsed; return; }
            loupe.Visibility = Visibility.Visible;

            const int span = 17;                                     // 17×17 픽셀을 8배로 본다
            int sx = Math.Max(0, Math.Min(shotW - span, cx - span / 2));
            int sy = Math.Max(0, Math.Min(shotH - span, cy - span / 2));
            try
            {
                var crop = new CroppedBitmap(shotSrc, new Int32Rect(sx, sy, span, span));
                crop.Freeze();
                loupeImg.Source = crop;
            }
            catch { }

            var c = At(cx, cy);
            loupeText.Text = string.Format("{0}, {1}   #{2:X2}{3:X2}{4:X2}",
                cx + virt.Left, cy + virt.Top, c.R, c.G, c.B);

            loupe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double lw = loupe.DesiredSize.Width, lh = loupe.DesiredSize.Height;
            double gap = 22 * S;
            double x = cursor.X + gap, y = cursor.Y + gap;
            if (x + lw > shotW) x = cursor.X - gap - lw;             // 오른쪽 끝이면 왼쪽으로 뒤집는다
            if (y + lh > shotH) y = cursor.Y - gap - lh;
            x = Math.Max(0, x); y = Math.Max(0, y);
            Canvas.SetLeft(loupe, Math.Round(x)); Canvas.SetTop(loupe, Math.Round(y));
            loupe.Refresh(x, y, lw, lh);
        }

        void UpdateBar(bool show)
        {
            bar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (!show) return;
            bar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double bw = bar.DesiredSize.Width, bh = bar.DesiredSize.Height;

            double x = sel.X + (sel.Width - bw) / 2;
            double y = sel.Bottom + 12 * S;
            if (y + bh > shotH - 4 * S) y = sel.Y - bh - 12 * S;     // 아래가 막히면 위로
            if (y < 4 * S) y = Math.Min(shotH - bh - 4 * S, sel.Bottom - bh - 12 * S);
            x = Math.Max(4 * S, Math.Min(x, shotW - bw - 4 * S));
            Canvas.SetLeft(bar, Math.Round(x)); Canvas.SetTop(bar, Math.Round(y));
            bar.Refresh(x, y, bw, bh);
        }

        void UpdateHint()
        {
            string t;
            if (pickOnly && mode == Mode.Adjusting)
                t = requireConfirm
                    ? L.T("스크롤되는 영역을 찾아뒀다. 맞으면 Enter, 아니면 모서리를 끌어 고쳐라   ·   Esc 취소", "Found the scrolling area. Press Enter if it's right, or drag the corners to fix it   ·   Esc to cancel")
                    : L.T("Enter로 스크롤 캡처 시작   ·   방향키 이동, Alt+방향키 크기   ·   Esc 다시", "Enter to start scrolling capture   ·   Arrows move, Alt+Arrows resize   ·   Esc to redo");
            else if (target == Target.Window)
                t = L.T("창 선택 — 클릭해서 캡처   ·   Space 영역 선택으로   ·   Esc 취소", "Window selection — click to capture   ·   Space for region   ·   Esc to cancel");
            else if (mode == Mode.Adjusting)
                t = L.T("Enter 복사   ·   E 편집   ·   S 저장   ·   방향키 이동, Alt+방향키 크기   ·   Esc 다시", "Enter Copy   ·   E Edit   ·   S Save   ·   Arrows move, Alt+Arrows resize   ·   Esc to redo");
            else
                t = pickOnly
                    ? L.T("스크롤할 영역을 끌어서 지정   ·   클릭하면 그 창   ·   Space 창 모드   ·   Esc 취소", "Drag to select the area to scroll   ·   Click for that window   ·   Space for window mode   ·   Esc to cancel")
                    : L.T("끌어서 영역 선택   ·   클릭하면 그 창   ·   Space 창 모드   ·   Ctrl+A 모니터 전체   ·   Esc 취소", "Drag to select   ·   Click for that window   ·   Space for window mode   ·   Ctrl+A whole monitor   ·   Esc to cancel");
            hintText.Text = t;
            PlaceHint();
        }

        void PlaceHint()
        {
            if (hint == null) return;
            hint.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double w = hint.DesiredSize.Width, h = hint.DesiredSize.Height;
            // 커서가 있는 모니터 아래쪽 가운데에 둔다.
            var m = MonitorAt(cursor.X == 0 && cursor.Y == 0 ? new Point(shotW / 2.0, shotH / 2.0) : cursor);
            double x = m.X + (m.Width - w) / 2;
            double y = m.Bottom - h - 46 * S;
            Canvas.SetLeft(hint, Math.Round(x)); Canvas.SetTop(hint, Math.Round(y));
            hint.Refresh(x, y, w, h);
        }

        // ---------- 종료 ----------

        void Cancel() { Finish(PostAction.Cancel); }

        void Finish(PostAction act)
        {
            if (finished) return;
            finished = true;
            IsOpen = false;

            CaptureResult r = null;
            if (act != PostAction.Cancel && !sel.IsEmpty && sel.Width >= 1 && sel.Height >= 1)
            {
                var crop = pickOnly
                    ? null
                    : Cap.Crop(shot, new D.Rectangle((int)sel.X, (int)sel.Y, (int)sel.Width, (int)sel.Height));
                r = new CaptureResult
                {
                    Image = crop,
                    Action = act,
                    Bounds = new RECT
                    {
                        Left = (int)sel.X + virt.Left,
                        Top = (int)sel.Y + virt.Top,
                        Right = (int)(sel.X + sel.Width) + virt.Left,
                        Bottom = (int)(sel.Y + sel.Height) + virt.Top
                    }
                };
            }

            Close();
            try { shot.Dispose(); } catch { }
            if (done != null) done(r);
        }

        protected override void OnClosed(EventArgs e)
        {
            IsOpen = false;
            base.OnClosed(e);
        }
    }
}
