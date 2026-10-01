// Money Shot — 디자인 토큰과 공용 시각 요소
// 색은 IPtendo Switch 팔레트를 그대로 계승하고, 움직임과 재질은 맥 계열을 따른다.
using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MoneyShot
{
    public static class Theme
    {
        // ---- IPtendo 팔레트 (IPtendo Switch와 동일) ----
        public const string HexBg = "#0B0B0F";
        public const string HexCard = "#151519";
        public const string HexCardHover = "#1E1E24";
        public const string HexBorder = "#26262C";
        public const string HexText = "#F4F4F5";
        public const string HexMuted = "#8A8A94";
        public const string HexAccent = "#34D399";
        public const string HexAccentDim = "#132A22";
        public const string HexDanger = "#F87171";

        public static readonly Color Bg = H(HexBg);
        public static readonly Color Card = H(HexCard);
        public static readonly Color CardHover = H(HexCardHover);
        public static readonly Color Border = H(HexBorder);
        public static readonly Color Text = H(HexText);
        public static readonly Color Muted = H(HexMuted);
        public static readonly Color Accent = H(HexAccent);
        public static readonly Color AccentDim = H(HexAccentDim);
        public static readonly Color Danger = H(HexDanger);

        public static readonly SolidColorBrush BrBg = Frozen(Bg);
        public static readonly SolidColorBrush BrCard = Frozen(Card);
        public static readonly SolidColorBrush BrCardHover = Frozen(CardHover);
        public static readonly SolidColorBrush BrBorder = Frozen(Border);
        public static readonly SolidColorBrush BrText = Frozen(Text);
        public static readonly SolidColorBrush BrMuted = Frozen(Muted);
        // 아이콘용. 본문 회색(#8A8A94)은 선 그림으로 쓰면 반투명 배경 위에서 너무 묻힌다.
        public static readonly Color Dim = H("#B9B9C4");
        public static readonly SolidColorBrush BrDim = Frozen(Dim);
        public static readonly SolidColorBrush BrAccent = Frozen(Accent);
        public static readonly SolidColorBrush BrAccentDim = Frozen(AccentDim);
        public static readonly SolidColorBrush BrDanger = Frozen(Danger);

        // 아크릴이 실패했을 때 대신 깔 반투명 배경 (알파 0xE8)
        public static readonly SolidColorBrush BrGlassFallback =
            Frozen(Color.FromArgb(0xE8, Card.R, Card.G, Card.B));

        // ---- 타이포 ----
        // 윈도우 11 기본 UI 서체. 한글은 맑은 고딕으로 자동 폴백된다.
        public static readonly FontFamily UI = new FontFamily("Segoe UI Variable Text, Segoe UI, Malgun Gothic");
        // 아이콘 글리프. 윈도우 11에 기본 탑재, 10이면 MDL2로 폴백.
        public static readonly FontFamily Glyph = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        public static readonly FontFamily Mono = new FontFamily("Cascadia Mono, Consolas");

        // 앱 아이콘(실행 파일에 박힌 것)을 화면용 그림으로. 제목줄 왼쪽에 쓴다.
        static ImageSource appIcon;
        public static ImageSource AppIcon
        {
            get
            {
                if (appIcon != null) return appIcon;
                try
                {
                    var path = System.Reflection.Assembly.GetEntryAssembly().Location;
                    using (var ic = new System.Drawing.Icon(System.Drawing.Icon.ExtractAssociatedIcon(path), 32, 32))
                    {
                        var src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(ic.Handle,
                            System.Windows.Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                        src.Freeze();
                        appIcon = src;
                    }
                }
                catch { }
                return appIcon;
            }
        }

        public static System.Windows.UIElement AppMark(double size)
        {
            if (AppIcon == null)
                return new System.Windows.Shapes.Ellipse { Width = 9, Height = 9, Fill = BrAccent, VerticalAlignment = System.Windows.VerticalAlignment.Center };
            var img = new System.Windows.Controls.Image { Source = AppIcon, Width = size, Height = size, VerticalAlignment = System.Windows.VerticalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            return img;
        }

        public static Color H(string hex) { return (Color)ColorConverter.ConvertFromString(hex); }
        static SolidColorBrush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        public static SolidColorBrush Alpha(Color c, byte a)
        {
            return Frozen(Color.FromArgb(a, c.R, c.G, c.B));
        }

        // ---- 움직임 ----
        // 맥의 스프링에 해당하는 느낌. 살짝 지나쳤다가 제자리로 돌아온다.
        public static IEasingFunction Spring(double amplitude)
        {
            return new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = amplitude };
        }
        public static IEasingFunction Out { get { return new CubicEase { EasingMode = EasingMode.EaseOut }; } }
        public static IEasingFunction InOut { get { return new CubicEase { EasingMode = EasingMode.EaseInOut }; } }

        public static DoubleAnimation A(double from, double to, int ms, IEasingFunction ease, int delayMs)
        {
            var a = new DoubleAnimation(from, to, new Duration(TimeSpan.FromMilliseconds(ms)));
            if (ease != null) a.EasingFunction = ease;
            if (delayMs > 0) a.BeginTime = TimeSpan.FromMilliseconds(delayMs);
            return a;
        }
        public static DoubleAnimation A(double from, double to, int ms, IEasingFunction ease)
        {
            return A(from, to, ms, ease, 0);
        }
        public static DoubleAnimation A(double from, double to, int ms) { return A(from, to, ms, Out, 0); }
    }

    // DWM 시스템 백드롭(아크릴)을 창에 입힌다.
    // WPF의 AllowsTransparency=True와는 공존할 수 없으므로, 이 방식을 쓰는 창은
    // WindowStyle=None + AllowsTransparency=False 로 만들어야 한다.
    public static class Glass
    {
        static bool? supported;

        // 이 윈도우 빌드가 아크릴 백드롭을 지원하는지. 윈도우 11 22621 이상.
        public static bool Supported
        {
            get
            {
                if (supported.HasValue) return supported.Value;
                var v = Environment.OSVersion.Version;
                int build = 0;
                try { build = int.Parse(Microsoft.Win32.Registry.GetValue(
                    @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion",
                    "CurrentBuildNumber", "0").ToString()); }
                catch { build = v.Build; }
                supported = (v.Major >= 10 && build >= 22621);
                return supported.Value;
            }
        }

        // 창이 SourceInitialized 된 뒤에 호출한다.
        // 성공하면 true, 실패하면 호출측이 불투명 배경으로 폴백해야 한다.
        public static bool Apply(Window w, bool rounded)
        {
            try
            {
                var hwnd = new WindowInteropHelper(w).Handle;
                if (hwnd == IntPtr.Zero) return false;

                // 다크 모드로 알려야 시스템이 어두운 아크릴을 합성한다.
                int dark = 1;
                Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, 4);

                int corner = rounded ? Native.DWMWCP_ROUND : Native.DWMWCP_DONOTROUND;
                Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, 4);

                if (!Supported) return false;

                // 클라이언트 영역 전체를 프레임으로 확장해야 백드롭이 창 전면에 합성된다.
                var m = new Native.MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
                Native.DwmExtendFrameIntoClientArea(hwnd, ref m);

                int backdrop = Native.DWMSBT_TRANSIENTWINDOW;   // 아크릴
                int hr = Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, 4);
                if (hr != 0) return false;

                // WPF가 칠하는 불투명 배경을 걷어내야 아래 백드롭이 보인다.
                var src = HwndSource.FromHwnd(hwnd);
                if (src != null && src.CompositionTarget != null)
                    src.CompositionTarget.BackgroundColor = Colors.Transparent;
                w.Background = Brushes.Transparent;
                return true;
            }
            catch { return false; }
        }

        // 도구 창으로 표시 (Alt+Tab 목록과 작업표시줄에서 숨김).
        public static void MakeToolWindow(Window w, bool noActivate)
        {
            try
            {
                var hwnd = new WindowInteropHelper(w).Handle;
                if (hwnd == IntPtr.Zero) return;
                int ex = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
                ex |= Native.WS_EX_TOOLWINDOW;
                if (noActivate) ex |= Native.WS_EX_NOACTIVATE;
                Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, ex);
            }
            catch { }
        }
    }
}
