// 설치 창. 앱과 같은 색과 말투를 쓴다.
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace MoneyShotSetup
{
    public class SetupWindow : Window
    {
        static readonly Color Bg = C("#0B0B0F"), Card = C("#151519"), Border_ = C("#26262C");
        static readonly Color Text = C("#F4F4F5"), Muted = C("#8A8A94"), Dim = C("#B9B9C4");
        static readonly Color Accent = C("#34D399"), Danger = C("#F87171");
        static readonly FontFamily UI = new FontFamily("Segoe UI Variable Text, Segoe UI, Malgun Gothic");

        readonly bool uninstallMode;
        StackPanel body;
        TextBlock statusLine;
        Toggle deskToggle, autoToggle, dataToggle;
        Border goButton;
        bool busy, done;

        public SetupWindow(bool uninstall)
        {
            uninstallMode = uninstall;

            Title = Setup.AppName + (uninstall ? L.T(" 제거", " Uninstall") : L.T(" 설치", " Setup"));
            WindowStyle = WindowStyle.None;
            AllowsTransparency = false;
            ResizeMode = ResizeMode.NoResize;
            Width = 440;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Bg);
            FontFamily = UI;
            ShowInTaskbar = true;
        }

        static Color C(string hex) { return (Color)ColorConverter.ConvertFromString(hex); }
        static SolidColorBrush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        static SolidColorBrush A(Color c, byte a) { return B(Color.FromArgb(a, c.R, c.G, c.B)); }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            Build();
        }

        void Build()
        {
            var root = new StackPanel();

            // 제목줄
            var title = new Grid { Height = 46, Background = Brushes.Transparent };
            var tl = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(18, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            tl.Children.Add(new Ellipse { Width = 9, Height = 9, Fill = B(Accent), VerticalAlignment = VerticalAlignment.Center });
            tl.Children.Add(new TextBlock
            {
                Text = Setup.AppName,
                Foreground = B(Text), FontSize = 14.5, FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(9, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center
            });
            tl.Children.Add(new TextBlock
            {
                Text = Info.Version,
                Foreground = B(Muted), FontSize = 11,
                Margin = new Thickness(9, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center
            });
            title.Children.Add(tl);

            var close = new Border
            {
                Width = 40, Height = 46,
                Background = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Right,
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = "✕", Foreground = B(Muted), FontSize = 13,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            close.MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs a) { a.Handled = true; };
            close.MouseLeftButtonUp += delegate { if (!busy) Close(); };
            title.Children.Add(close);
            title.MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs a)
            { if (!a.Handled) try { DragMove(); } catch { } };
            root.Children.Add(title);

            body = new StackPanel { Margin = new Thickness(22, 4, 22, 20) };
            root.Children.Add(body);

            Content = new Border
            {
                BorderBrush = B(Border_),
                BorderThickness = new Thickness(1),
                Child = root
            };

            if (uninstallMode) BuildUninstall();
            else BuildInstall();
        }

        // ---------- 설치 ----------

        void BuildInstall()
        {
            bool already = Setup.IsInstalled;
            string old = Setup.InstalledVersion;

            body.Children.Add(Head(already ? L.T("다시 설치", "Reinstall") : L.T("설치", "Install")));
            body.Children.Add(Para(already
                ? L.F("이미 깔려 있다{0}. 새 것으로 덮어쓴다. 설정과 내려받은 모델은 그대로 남는다.", "Already installed{0}. This will replace it. Your settings and downloaded models are kept.", old != null ? " (" + old + ")" : "")
                : L.T("화면 캡처하고 바로 손볼 수 있는 도구다. 관리자 권한은 필요 없다.", "Capture your screen and edit it right away. No administrator rights needed.")));

            body.Children.Add(Field(L.T("설치 위치", "Location"), Setup.InstallDir));
            body.Children.Add(Field(L.T("크기", "Size"), (Setup.PayloadSize / 1024) + " KB"));

            body.Children.Add(Gap(6));
            deskToggle = OptionRow(L.T("바탕화면에 바로가기", "Desktop shortcut"), true, null);
            autoToggle = OptionRow(L.T("컴퓨터를 켜면 자동으로", "Start with Windows"), true, L.T("창 없이 알림 영역에만 올라온다", "Runs quietly in the notification area"));

            body.Children.Add(Gap(6));
            statusLine = new TextBlock
            {
                Foreground = B(Muted), FontSize = 11.5,
                Margin = new Thickness(2, 10, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };
            body.Children.Add(statusLine);

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };
            if (already)
            {
                row.Children.Add(Button_(L.T("제거", "Uninstall"), false, false, delegate { SwitchToUninstall(); }));
                row.Children.Add(Gap2(8));
            }
            goButton = Button_(already ? L.T("다시 설치", "Reinstall") : L.T("설치", "Install"), true, false, delegate { RunInstall(); });
            row.Children.Add(goButton);
            body.Children.Add(row);
        }

        void SwitchToUninstall()
        {
            body.Children.Clear();
            BuildUninstall();
        }

        void RunInstall()
        {
            if (busy) return;
            if (done) { Close(); return; }
            busy = true;
            SetGo(L.T("설치 중…", "Installing…"), false);

            bool desk = deskToggle.Value, auto = autoToggle.Value;
            Dispatcher.BeginInvoke(new Action(delegate
            {
                try
                {
                    Setup.Install(desk, auto, Say);
                    busy = false;
                    done = true;
                    Say(L.T("깔았다. 시작 메뉴에서 열거나, PrintScreen을 눌러보면 된다.", "Installed. Open it from the Start menu, or just press PrintScreen."));
                    SetGo(L.T("지금 실행", "Open Now"), true);
                    goButton.MouseLeftButtonUp += delegate
                    {
                        try { Process.Start(new ProcessStartInfo(Setup.InstalledExe) { UseShellExecute = true }); }
                        catch { }
                        Close();
                    };
                }
                catch (Exception ex)
                {
                    busy = false;
                    Say(L.T("실패: ", "Failed: ") + ex.Message);
                    SetGo(L.T("다시 해보기", "Try Again"), true);
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        // ---------- 제거 ----------

        void BuildUninstall()
        {
            body.Children.Add(Head(L.T("제거", "Uninstall")));
            body.Children.Add(Para(L.T("프로그램과 바로가기, 자동 실행 등록을 지운다.", "Removes the app, its shortcuts and its startup entry.")));
            body.Children.Add(Field(L.T("설치 위치", "Location"), Setup.InstallDir));

            body.Children.Add(Gap(6));
            dataToggle = OptionRow(L.T("설정과 내려받은 AI 모델도 지우기", "Also delete settings and downloaded AI models"), false,
                "%APPDATA%\\" + Setup.AppName + L.T(" — 다시 깔면 모델을 또 받아야 한다", " — models must be downloaded again if you reinstall"));

            statusLine = new TextBlock
            {
                Foreground = B(Muted), FontSize = 11.5,
                Margin = new Thickness(2, 10, 0, 0), TextWrapping = TextWrapping.Wrap
            };
            body.Children.Add(statusLine);

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };
            row.Children.Add(Button_("ㄴㄴ", false, false, delegate { Close(); }));
            row.Children.Add(Gap2(8));
            goButton = Button_(L.T("ㅇㅇ, 지워", "Uninstall"), true, true, delegate { RunUninstall(); });
            row.Children.Add(goButton);
            body.Children.Add(row);
        }

        void RunUninstall()
        {
            if (busy) return;
            if (done) { Close(); return; }
            busy = true;
            SetGo(L.T("지우는 중…", "Uninstalling…"), false);
            bool data = dataToggle.Value;

            Dispatcher.BeginInvoke(new Action(delegate
            {
                try
                {
                    Setup.Uninstall(data, Say);
                    busy = false; done = true;
                    Say(L.T("지웠다.", "Uninstalled."));
                    SetGo(L.T("닫기", "Close"), true);
                }
                catch (Exception ex)
                {
                    busy = false;
                    Say(L.T("실패: ", "Failed: ") + ex.Message);
                    SetGo(L.T("다시 해보기", "Try Again"), true);
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        // ---------- 조각 ----------

        void Say(string t)
        {
            statusLine.Text = t;
            Dispatcher.Invoke(new Action(delegate { }), System.Windows.Threading.DispatcherPriority.Render);
        }

        void SetGo(string text, bool enabled)
        {
            var tb = ((Border)goButton).Child as TextBlock;
            if (tb != null) tb.Text = text;
            goButton.Opacity = enabled ? 1 : 0.5;
            goButton.Cursor = enabled ? Cursors.Hand : Cursors.Wait;
        }

        static TextBlock Head(string t)
        {
            return new TextBlock
            {
                Text = t, Foreground = B(Text), FontSize = 19, FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 6, 0, 8)
            };
        }

        static TextBlock Para(string t)
        {
            return new TextBlock
            {
                Text = t, Foreground = B(Dim), FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14),
                LineHeight = 19, LineStackingStrategy = LineStackingStrategy.BlockLineHeight
            };
        }

        static Border Field(string label, string value)
        {
            var g = new Grid();
            g.Children.Add(new TextBlock
            {
                Text = label, Foreground = B(Muted), FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            });
            g.Children.Add(new TextBlock
            {
                Text = value, Foreground = B(Dim), FontSize = 11,
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 270
            });
            return new Border
            {
                Background = A(Card, 0xB0),
                BorderBrush = B(Border_), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(13, 9, 13, 10),
                Margin = new Thickness(0, 0, 0, 6),
                Child = g
            };
        }

        Toggle OptionRow(string label, bool init, string hint)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var texts = new StackPanel();
            texts.Children.Add(new TextBlock { Text = label, Foreground = B(Text), FontSize = 13 });
            if (hint != null)
                texts.Children.Add(new TextBlock
                {
                    Text = hint, Foreground = B(Muted), FontSize = 11,
                    Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap
                });
            Grid.SetColumn(texts, 0); g.Children.Add(texts);

            var sw = new Toggle(init) { VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(sw, 1); g.Children.Add(sw);

            var card = new Border
            {
                Background = A(Card, 0xB0),
                BorderBrush = B(Border_), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(13, 10, 13, 11),
                Margin = new Thickness(0, 0, 0, 6),
                Cursor = Cursors.Hand,
                Child = g
            };
            card.MouseLeftButtonUp += delegate { sw.Value = !sw.Value; };
            body.Children.Add(card);
            return sw;
        }

        static Border Gap(double h) { return new Border { Height = h }; }
        static Border Gap2(double w) { return new Border { Width = w }; }

        static Border Button_(string text, bool primary, bool danger, Action act)
        {
            var fill = primary ? (danger ? Danger : Accent) : Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF);
            var b = new Border
            {
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(18, 9, 18, 10),
                Background = primary ? B(fill) : A(Colors.White, 0x12),
                BorderBrush = primary ? Brushes.Transparent : B(Border_),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = text, FontSize = 13,
                    FontWeight = primary ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = primary ? B(C(danger ? "#1A0707" : "#08110D")) : B(Text)
                }
            };
            b.MouseEnter += delegate { b.Opacity = 0.86; };
            b.MouseLeave += delegate { b.Opacity = 1; };
            b.MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs a) { a.Handled = true; };
            b.MouseLeftButtonUp += delegate { act(); };
            return b;
        }
    }

    // 앱과 같은 모양의 토글.
    public class Toggle : Grid
    {
        readonly Border track, knob;
        readonly TranslateTransform slide = new TranslateTransform();
        bool value_;

        public bool Value
        {
            get { return value_; }
            set { if (value_ != value) { value_ = value; Sync(); } }
        }

        public Toggle(bool initial)
        {
            value_ = initial;
            Width = 42; Height = 24;
            Cursor = Cursors.Hand;

            track = new Border { CornerRadius = new CornerRadius(12), Width = 42, Height = 24 };
            knob = new Border
            {
                Width = 18, Height = 18,
                CornerRadius = new CornerRadius(9),
                Background = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(3, 0, 0, 0),
                RenderTransform = slide,
                Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Opacity = 0.35, Color = Colors.Black }
            };
            Children.Add(track);
            Children.Add(knob);
            Sync();

            MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs a) { a.Handled = true; };
            MouseLeftButtonUp += delegate(object o, MouseButtonEventArgs a) { a.Handled = true; Value = !Value; };
        }

        void Sync()
        {
            var on = (Color)ColorConverter.ConvertFromString("#34D399");
            track.Background = value_
                ? new SolidColorBrush(on)
                : new SolidColorBrush(Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF));
            slide.X = value_ ? 18 : 0;
        }
    }
}
