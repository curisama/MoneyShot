// Money Shot — 편집기에서 쓰는 작은 대화상자들
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MoneyShot
{
    // 다크 테마를 입힌 공통 껍데기.
    public class Sheet : Window
    {
        protected StackPanel Body = new StackPanel();

        protected Sheet(string title, double width)
        {
            Title = title;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = false;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            SizeToContent = SizeToContent.Height;
            Width = width;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Theme.BrBg;
            FontFamily = Theme.UI;

            var head = new TextBlock
            {
                Text = title, Foreground = Theme.BrText,
                FontSize = 15, FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 14)
            };
            var outer = new StackPanel { Margin = new Thickness(20, 18, 20, 18) };
            outer.Children.Add(head);
            outer.Children.Add(Body);
            shell = new Border
            {
                Background = Theme.BrBg,
                BorderBrush = Theme.BrBorder,
                BorderThickness = new Thickness(1),
                Child = outer
            };
            Content = shell;
            MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs a)
            { if (!a.Handled) try { DragMove(); } catch { } };
            PreviewKeyDown += delegate(object o, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) { DialogResult = false; }
                else if (e.Key == Key.Enter && !(Keyboard.FocusedElement is TextBox && ((TextBox)Keyboard.FocusedElement).AcceptsReturn))
                { OnOk(); }
            };
        }

        Border shell;

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            if (Glass.Apply(this, true) && shell != null)
                shell.Background = Theme.Alpha(Theme.Bg, 0xA8);
        }

        protected virtual void OnOk() { DialogResult = true; }

        // 주인 창을 가리지 않게 옆에 세운다. 양옆에 자리가 없으면(좁은 화면, 최대화) 화면 안쪽으로 당겨
        // 주인 창 위에 겹치더라도 화면 밖으로는 안 나가게 한다. 크기가 정해진 뒤 한 번 더 맞춘다.
        double besideDy = -1;

        protected void PlaceBesideOwner(double dy)
        {
            if (besideDy < 0)
            {
                besideDy = dy;
                Loaded += delegate { PlaceBesideOwner(besideDy); };
            }
            try
            {
                if (Owner == null) return;
                var hwnd = new System.Windows.Interop.WindowInteropHelper(Owner).Handle;
                var wa = System.Windows.Forms.Screen.FromHandle(hwnd).WorkingArea;
                double kx = 1, ky = 1;
                var src = PresentationSource.FromVisual(Owner);
                if (src != null && src.CompositionTarget != null)
                {
                    kx = src.CompositionTarget.TransformFromDevice.M11;
                    ky = src.CompositionTarget.TransformFromDevice.M22;
                }
                double L = wa.Left * kx, T = wa.Top * ky, R = wa.Right * kx, B = wa.Bottom * ky;
                double w = ActualWidth > 0 ? ActualWidth : Width;
                double h = ActualHeight > 0 ? ActualHeight : 300;
                double x = Owner.Left - w - 12;
                if (x < L) x = Owner.Left + Owner.ActualWidth + 12;
                if (x + w > R) x = R - w - 8;
                if (x < L) x = L + 8;
                double y = Owner.Top + dy;
                if (y + h > B) y = B - h - 8;
                if (y < T) y = T + 8;
                Left = x;
                Top = y;
            }
            catch { }
        }

        protected StackPanel Buttons(string okText)
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 18, 0, 0)
            };
            row.Children.Add(Btn(L.T("취소", "Cancel"), false, delegate { DialogResult = false; }));
            row.Children.Add(new Border { Width = 8 });
            row.Children.Add(Btn(okText, true, delegate { OnOk(); }));
            return row;
        }

        protected static Border Btn(string text, bool primary, Action act)
        {
            var b = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(15, 7, 15, 8),
                Background = primary ? Theme.BrAccent : Theme.Alpha(Colors.White, 0x10),
                BorderBrush = primary ? Brushes.Transparent : Theme.BrBorder,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = text, FontSize = 12.5,
                    FontWeight = primary ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = primary ? new SolidColorBrush(Theme.H("#08110D")) : Theme.BrText
                }
            };
            b.MouseEnter += delegate { b.Opacity = 0.85; };
            b.MouseLeave += delegate { b.Opacity = 1; };
            // 창 전체에 DragMove가 걸려 있어, 여기서 막지 않으면 눌림이 그리로 넘어가 클릭이 죽는다.
            b.MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs a) { a.Handled = true; };
            b.MouseLeftButtonUp += delegate { act(); };
            return b;
        }

        protected static TextBox Field(string value)
        {
            return new TextBox
            {
                Text = value,
                Background = Theme.Alpha(Theme.Card, 0xCC),
                Foreground = Theme.BrText,
                CaretBrush = Theme.BrAccent,
                BorderBrush = Theme.BrBorder,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 6, 8, 7),
                FontFamily = Theme.Mono,
                FontSize = 13,
                Width = 96
            };
        }

        protected static TextBlock Label(string t)
        {
            return new TextBlock
            {
                Text = t, Foreground = Theme.BrMuted, FontSize = 11.5,
                Margin = new Thickness(0, 0, 0, 5)
            };
        }
    }

    // 크기 바꾸기.
    public class SizeDialog : Sheet
    {
        public int ResultW, ResultH;
        readonly int origW, origH;
        readonly TextBox wBox, hBox;
        bool keepRatio = true;
        bool syncing;

        public SizeDialog(int w, int h) : base(L.T("크기 바꾸기", "Resize"), 340)
        {
            origW = w; origH = h;
            ResultW = w; ResultH = h;

            var row = new StackPanel { Orientation = Orientation.Horizontal };

            var wCol = new StackPanel();
            wCol.Children.Add(Label(L.T("가로", "Width")));
            wBox = Field(w.ToString());
            wCol.Children.Add(wBox);
            row.Children.Add(wCol);

            row.Children.Add(new TextBlock
            {
                Text = "×", Foreground = Theme.BrMuted, FontSize = 14,
                Margin = new Thickness(12, 22, 12, 0)
            });

            var hCol = new StackPanel();
            hCol.Children.Add(Label(L.T("세로", "Height")));
            hBox = Field(h.ToString());
            hCol.Children.Add(hBox);
            row.Children.Add(hCol);

            Body.Children.Add(row);

            var ratioRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
            var t = new Toggle(true) { VerticalAlignment = VerticalAlignment.Center };
            t.Changed += delegate(bool v) { keepRatio = v; };
            ratioRow.Children.Add(t);
            ratioRow.Children.Add(new TextBlock
            {
                Text = L.T("비율 유지", "Keep proportions"), Foreground = Theme.BrText, FontSize = 12.5,
                Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center
            });
            Body.Children.Add(ratioRow);

            var presets = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
            foreach (var pct in new[] { 25, 50, 75, 200 })
            {
                int p = pct;
                presets.Children.Add(Btn(p + "%", false, delegate
                {
                    syncing = true;
                    wBox.Text = ((int)Math.Round(origW * p / 100.0)).ToString();
                    hBox.Text = ((int)Math.Round(origH * p / 100.0)).ToString();
                    syncing = false;
                }));
                presets.Children.Add(new Border { Width = 6 });
            }
            Body.Children.Add(presets);

            wBox.TextChanged += delegate
            {
                if (syncing || !keepRatio) return;
                int v; if (!int.TryParse(wBox.Text, out v) || v <= 0) return;
                syncing = true;
                hBox.Text = ((int)Math.Round(v * (double)origH / origW)).ToString();
                syncing = false;
            };
            hBox.TextChanged += delegate
            {
                if (syncing || !keepRatio) return;
                int v; if (!int.TryParse(hBox.Text, out v) || v <= 0) return;
                syncing = true;
                wBox.Text = ((int)Math.Round(v * (double)origW / origH)).ToString();
                syncing = false;
            };

            Body.Children.Add(Buttons(L.T("적용", "Apply")));
            Loaded += delegate { wBox.Focus(); wBox.SelectAll(); };
        }

        protected override void OnOk()
        {
            int w, h;
            if (!int.TryParse(wBox.Text, out w) || !int.TryParse(hBox.Text, out h) || w < 1 || h < 1)
                return;
            ResultW = Math.Min(20000, w);
            ResultH = Math.Min(20000, h);
            DialogResult = true;
        }
    }

    // 한 줄 입력받기 (레이어 이름 등).
    public class TextPrompt : Sheet
    {
        public string Value;
        readonly TextBox box;

        public TextPrompt(string title, string initial) : base(title, 320)
        {
            Value = initial;
            box = Field(initial);
            box.Width = double.NaN;
            box.FontFamily = Theme.UI;
            Body.Children.Add(box);
            Body.Children.Add(Buttons(L.T("확인", "OK")));
            Loaded += delegate { box.Focus(); box.SelectAll(); };
        }

        protected override void OnOk()
        {
            var t = box.Text.Trim();
            if (t.Length == 0) return;
            Value = t;
            DialogResult = true;
        }
    }

    // 이어붙이기 방향과 간격.
    public class JoinDialog : Sheet
    {
        public bool Vertical = true;
        public int Gap = 0;
        public bool Transparent = false;

        public JoinDialog() : base(L.T("이어붙이기", "Join Images"), 320)
        {
            Body.Children.Add(Label(L.T("방향", "Direction")));
            var dir = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
            Border vBtn = null, hBtn = null;
            vBtn = Btn(L.T("세로로", "Vertical"), true, delegate { });
            hBtn = Btn(L.T("가로로", "Horizontal"), false, delegate { });
            vBtn.MouseLeftButtonUp += delegate { Vertical = true; Mark(vBtn, hBtn); };
            hBtn.MouseLeftButtonUp += delegate { Vertical = false; Mark(hBtn, vBtn); };
            dir.Children.Add(vBtn);
            dir.Children.Add(new Border { Width = 8 });
            dir.Children.Add(hBtn);
            Body.Children.Add(dir);

            Body.Children.Add(Label(L.T("사이 간격", "Spacing")));
            var sl = new Slider { Minimum = 0, Maximum = 60, Value = 0, Foreground = Theme.BrAccent };
            var val = new TextBlock
            {
                Text = "0px", Foreground = Theme.BrAccent, FontFamily = Theme.Mono, FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            sl.ValueChanged += delegate { Gap = (int)sl.Value; val.Text = Gap + "px"; };
            Body.Children.Add(val);
            Body.Children.Add(sl);

            var tr = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
            var t = new Toggle(false) { VerticalAlignment = VerticalAlignment.Center };
            t.Changed += delegate(bool v) { Transparent = v; };
            tr.Children.Add(t);
            tr.Children.Add(new TextBlock
            {
                Text = L.T("빈 곳을 투명하게 (끄면 흰색)", "Transparent gaps (white when off)"), Foreground = Theme.BrText, FontSize = 12.5,
                Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center
            });
            Body.Children.Add(tr);

            Body.Children.Add(Buttons(L.T("붙이기", "Join")));
        }

        static void Mark(Border on, Border off)
        {
            on.Background = Theme.BrAccent;
            ((TextBlock)on.Child).Foreground = new SolidColorBrush(Theme.H("#08110D"));
            ((TextBlock)on.Child).FontWeight = FontWeights.SemiBold;
            off.Background = Theme.Alpha(Colors.White, 0x10);
            ((TextBlock)off.Child).Foreground = Theme.BrText;
            ((TextBlock)off.Child).FontWeight = FontWeights.Normal;
        }
    }

    // 예/아니오를 묻는 창.
    //
    // 윈도우 기본 MessageBox를 쓰면 단추가 "예/아니오"로 고정이라, 앱 전체가 반말인데
    // 거기만 존댓말이 튀어나온다. 생김새도 시스템 기본이라 다크 테마에서 겉돈다.
    public class ConfirmSheet : Sheet
    {
        public ConfirmSheet(string title, string body, string yes, string no, bool danger)
            : base(title, 380)
        {
            Body.Children.Add(new TextBlock
            {
                Text = body,
                Foreground = Theme.BrText,
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap
            });

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 18, 0, 0)
            };
            row.Children.Add(Btn(no, false, delegate { DialogResult = false; }));
            row.Children.Add(new Border { Width = 8 });

            var ok = Btn(yes, true, delegate { DialogResult = true; });
            if (danger)
            {
                ok.Background = Theme.BrDanger;
                ((TextBlock)ok.Child).Foreground = new SolidColorBrush(Theme.H("#1A0707"));
            }
            row.Children.Add(ok);
            Body.Children.Add(row);
            risky = danger;
        }

        bool risky;

        // 되돌릴 수 없는 일은 Enter로 얼떨결에 넘어가지 않게 한다. 단추를 눌러야 한다.
        protected override void OnOk() { if (!risky) DialogResult = true; }

        // 짧게 쓰는 길. 기본 단추 말투는 앱과 같게 둔다.
        public static bool Ask(Window owner, string title, string body)
        {
            return Ask(owner, title, body, "ㅇㅇ", "ㄴㄴ", false);
        }

        public static bool Ask(Window owner, string title, string body,
                               string yes, string no, bool danger)
        {
            var d = new ConfirmSheet(title, body, yes, no, danger);
            if (owner != null && owner.IsVisible) d.Owner = owner;
            return d.ShowDialog() == true;
        }
    }

    // 색 조절 창.
    //
    // 값을 움직일 때마다 뒤에 있는 그림이 바로 바뀐다. 숫자만 보고 맞추는 것보다
    // 눈으로 보고 멈추는 편이 빠르기 때문이다.
    // 창이 그림을 가리지 않도록 주인 창 왼쪽에 붙여 띄운다.
    public class AdjustSheet : Sheet
    {
        public Ops.Adjust Value = new Ops.Adjust();
        readonly Action<Ops.Adjust> preview;
        readonly List<Action> resets = new List<Action>();
        bool quiet;

        public AdjustSheet(Action<Ops.Adjust> preview) : base(L.T("색 조절", "Adjust Color"), 330)
        {
            this.preview = preview;

            Row(L.T("밝기", "Brightness"), -100, 100, 0, delegate(double v) { Value.Brightness = v; }, "");
            Row(L.T("대비", "Contrast"), -100, 100, 0, delegate(double v) { Value.Contrast = v; }, "");
            Row(L.T("채도", "Saturation"), -100, 100, 0, delegate(double v) { Value.Saturation = v; }, "");
            Row(L.T("색온도", "Temperature"), -100, 100, 0, delegate(double v) { Value.Temperature = v; }, L.T("차갑게 ↔ 따뜻하게", "Cool ↔ Warm"));
            Row(L.T("어두운 쪽", "Shadows"), -100, 100, 0, delegate(double v) { Value.Shadows = v; }, L.T("그림자만 들어올리거나 누른다", "Lifts or deepens only the shadows"));
            Row(L.T("밝은 쪽", "Highlights"), -100, 100, 0, delegate(double v) { Value.Highlights = v; }, L.T("날아간 곳을 되살린다", "Recovers blown-out areas"));
            Row(L.T("감마", "Gamma"), 20, 300, 100, delegate(double v) { Value.Gamma = v / 100.0; }, L.T("중간 밝기를 민다", "Shifts the midtones"));

            var foot = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };
            foot.Children.Add(Btn(L.T("초기화", "Reset"), false, delegate
            {
                quiet = true;
                foreach (var r in resets) r();
                quiet = false;
                Value = new Ops.Adjust();
                Fire();
            }));
            foot.Children.Add(new Border { Width = 8 });
            foot.Children.Add(Btn(L.T("취소", "Cancel"), false, delegate { DialogResult = false; }));
            foot.Children.Add(new Border { Width = 8 });
            foot.Children.Add(Btn(L.T("적용", "Apply"), true, delegate { DialogResult = true; }));
            Body.Children.Add(foot);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            PlaceBesideOwner(80);
        }

        void Row(string label, double min, double max, double init, Action<double> set, string hint)
        {
            var top = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            top.Children.Add(new TextBlock
            {
                Text = label, Foreground = Theme.BrText, FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center
            });
            var val = new TextBlock
            {
                Text = Fmt(init, min), Foreground = Theme.BrAccent,
                FontFamily = Theme.Mono, FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            top.Children.Add(val);
            Body.Children.Add(top);

            var sl = new Slider
            {
                Minimum = min, Maximum = max, Value = init,
                Margin = new Thickness(0, 5, 0, 0), Foreground = Theme.BrAccent
            };
            sl.ValueChanged += delegate
            {
                val.Text = Fmt(sl.Value, min);
                set(sl.Value);
                if (!quiet) Fire();
            };
            // 두 번 누르면 제자리로
            sl.MouseDoubleClick += delegate { sl.Value = init; };
            Body.Children.Add(sl);

            if (!string.IsNullOrEmpty(hint))
                Body.Children.Add(new TextBlock
                {
                    Text = hint, Foreground = Theme.BrMuted, FontSize = 10.5,
                    Margin = new Thickness(1, 3, 0, 0)
                });

            resets.Add(delegate { sl.Value = init; });
        }

        static string Fmt(double v, double min)
        {
            if (min == 20) return ((int)v) + "%";          // 감마
            return (v > 0 ? "+" : "") + ((int)v).ToString();
        }

        void Fire() { if (preview != null) preview(Value); }

        protected override void OnOk() { DialogResult = true; }
    }

    // 누끼 다듬기. 슬라이더를 움직이는 동안 레이어에 바로 보여준다.
    // AI는 한 번만 돌고, 슬라이더는 그 결과를 다시 다듬기만 한다.
    public class CutoutSheet : Sheet
    {
        public MatteSettings Value;
        readonly Action<MatteSettings> preview;
        readonly System.Collections.Generic.List<Action> resets = new System.Collections.Generic.List<Action>();
        readonly System.Windows.Threading.DispatcherTimer wait;
        bool quiet;

        public CutoutSheet(MatteSettings init, Action<MatteSettings> preview) : base(L.T("누끼 다듬기", "Refine Cutout"), 330)
        {
            this.preview = preview;
            Value = init.Clone();

            // 슬라이더는 초당 수십 번 움직인다. 멈칫한 틈에 한 번만 계산한다.
            wait = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(35) };
            wait.Tick += delegate { wait.Stop(); if (this.preview != null) this.preview(Value.Clone()); };

            Row(L.T("가장자리 다듬기", "Refine edge"), 0, 40, Value.Refine, 12, "px", delegate(double v) { Value.Refine = v; },
                L.T("사진의 경계를 따라 다시 맞춘다. 잘린 머리카락이 돌아온다", "Re-fits the mask to the photo's edges. Brings back clipped hair"));
            Row(L.T("대비", "Contrast"), 0, 100, Value.Contrast, 25, "%", delegate(double v) { Value.Contrast = v; },
                L.T("반쯤 비치는 곳을 정리한다. 배경이 희미하게 남으면 올려라", "Cleans up semi-transparent areas. Raise it if faint background remains"));
            Row(L.T("경계 이동", "Shift edge"), -10, 10, Value.Shift, 0, "px", delegate(double v) { Value.Shift = v; },
                L.T("왼쪽은 안으로 깎는다. 테두리에 배경색 띠가 남으면 당겨라", "Left trims inward. Pull it left if a background-colored fringe remains"));

            var foot = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };
            foot.Children.Add(Btn(L.T("AI 그대로", "AI as is"), false, delegate
            {
                quiet = true;
                foreach (var r in resets) r();
                quiet = false;
                Value.Refine = 0; Value.Contrast = 0; Value.Shift = 0;
                Fire();
            }));
            foot.Children.Add(new Border { Width = 8 });
            foot.Children.Add(Btn(L.T("취소", "Cancel"), false, delegate { DialogResult = false; }));
            foot.Children.Add(new Border { Width = 8 });
            foot.Children.Add(Btn(L.T("적용", "Apply"), true, delegate { DialogResult = true; }));
            Body.Children.Add(foot);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            PlaceBesideOwner(80);
        }

        protected override void OnClosed(EventArgs e)
        {
            wait.Stop();
            base.OnClosed(e);
        }

        void Row(string label, double min, double max, double init, double home, string unit,
                 Action<double> set, string hint)
        {
            var top = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            top.Children.Add(new TextBlock
            {
                Text = label, Foreground = Theme.BrText, FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center
            });
            var val = new TextBlock
            {
                Text = Fmt(init, min, unit), Foreground = Theme.BrAccent,
                FontFamily = Theme.Mono, FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            top.Children.Add(val);
            Body.Children.Add(top);

            var sl = new Slider
            {
                Minimum = min, Maximum = max, Value = init,
                IsSnapToTickEnabled = true, TickFrequency = 1,
                Margin = new Thickness(0, 5, 0, 0), Foreground = Theme.BrAccent
            };
            sl.ValueChanged += delegate
            {
                val.Text = Fmt(sl.Value, min, unit);
                set(sl.Value);
                if (!quiet) Fire();
            };
            // 두 번 누르면 기본값으로
            sl.MouseDoubleClick += delegate { sl.Value = home; };
            Body.Children.Add(sl);

            Body.Children.Add(new TextBlock
            {
                Text = hint, Foreground = Theme.BrMuted, FontSize = 10.5,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(1, 3, 0, 0)
            });

            resets.Add(delegate { sl.Value = 0; });
        }

        static string Fmt(double v, double min, string unit)
        {
            int n = (int)Math.Round(v);
            return (min < 0 && n > 0 ? "+" : "") + n + unit;
        }

        void Fire() { wait.Stop(); wait.Start(); }

        protected override void OnOk() { DialogResult = true; }
    }

    // 오픈소스 고지. 실행 파일에 박아 둔 THIRD_PARTY_NOTICES.md를 그대로 보여준다(레포와 한 벌).
    public class NoticesSheet : Sheet
    {
        public NoticesSheet() : base(L.T("오픈소스 고지", "Open-source notices"), 560)
        {
            string text = "";
            try
            {
                using (var st = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("notices"))
                    if (st != null) using (var r = new System.IO.StreamReader(st, System.Text.Encoding.UTF8)) text = r.ReadToEnd();
            }
            catch { }
            if (text.Length == 0) text = L.T("(고지를 읽지 못했다)", "(Couldn't read the notices)");
            var box = new TextBox
            {
                Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Height = 420, FontFamily = Theme.Mono, FontSize = 11.5,
                Background = Theme.Alpha(Theme.Card, 0xCC), Foreground = Theme.BrText,
                BorderBrush = Theme.BrBorder, BorderThickness = new Thickness(1), Padding = new Thickness(10)
            };
            Body.Children.Add(box);
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            row.Children.Add(Btn(L.T("닫기", "Close"), true, delegate { DialogResult = true; }));
            Body.Children.Add(row);
        }
    }

    // 내려받기·처리 진행 표시. 취소할 수 있다.
    public class ProgressSheet : Sheet
    {
        readonly ProgressBar bar;
        readonly TextBlock detail;
        public bool Cancelled { get; private set; }

        public ProgressSheet(string title, string firstLine) : base(title, 380)
        {
            detail = new TextBlock
            {
                Text = firstLine, Foreground = Theme.BrText, FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12)
            };
            Body.Children.Add(detail);

            bar = new ProgressBar
            {
                Height = 6, Minimum = 0, Maximum = 100, Value = 0,
                Foreground = Theme.BrAccent,
                Background = Theme.Alpha(Colors.White, 0x14),
                BorderThickness = new Thickness(0)
            };
            Body.Children.Add(bar);

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };
            row.Children.Add(Btn(L.T("취소", "Cancel"), false, delegate { Cancelled = true; Close(); }));
            Body.Children.Add(row);
        }

        public void Report(string line, double pct)
        {
            Dispatcher.BeginInvoke(new Action(delegate
            {
                detail.Text = line;
                if (pct < 0) { bar.IsIndeterminate = true; }
                else { bar.IsIndeterminate = false; bar.Value = Math.Max(0, Math.Min(100, pct)); }
            }));
        }

        protected override void OnOk() { }
    }
}
