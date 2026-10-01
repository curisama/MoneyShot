// Money Shot — 레벨·커브·색조/채도·컬러 밸런스·흑백 창
//
// 다섯 창이 같은 껍데기를 쓴다: 값이 바뀌면 잠깐 기다렸다가(끄는 동안 수십 번 부르지 않게)
// 편집창에 "원본에서 결과를 만드는 함수"를 넘긴다. 편집창은 그걸로 레이어를 다시 그린다.
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace MoneyShot
{
    public abstract class ToneSheet : Sheet
    {
        public Action<ToneFn> Preview;
        readonly DispatcherTimer wait;
        protected bool Quiet;

        public abstract bool IsIdentity { get; }
        public abstract ToneFn Current();
        // 조정 레이어가 들고 있을 설정 사본과 그 종류. 조정 레이어로 못 쓰는 창은 null.
        public virtual object Settings() { return null; }
        public virtual string Kind { get { return null; } }
        protected abstract void ResetAll();

        protected ToneSheet(string title, double width) : base(title, width)
        {
            wait = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            wait.Tick += delegate
            {
                wait.Stop();
                if (Preview != null) Preview(IsIdentity ? null : Current());
            };
        }

        protected void Fire() { if (Quiet) return; wait.Stop(); wait.Start(); }

        protected void Footer()
        {
            var foot = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };
            foot.Children.Add(Btn(L.T("초기화", "Reset"), false, delegate { Quiet = true; ResetAll(); Quiet = false; Fire(); }));
            foot.Children.Add(new Border { Width = 8 });
            foot.Children.Add(Btn(L.T("취소", "Cancel"), false, delegate { DialogResult = false; }));
            foot.Children.Add(new Border { Width = 8 });
            foot.Children.Add(Btn(L.T("적용", "Apply"), true, delegate { DialogResult = true; }));
            Body.Children.Add(foot);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            PlaceBesideOwner(60);
        }

        protected override void OnClosed(EventArgs e) { wait.Stop(); base.OnClosed(e); }

        // ---------- 조립 부품 ----------

        // 여러 개 중 하나 고르기
        protected class Seg
        {
            public readonly StackPanel Panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            readonly List<Border> items = new List<Border>();
            public int Index;
            public Action<int> Changed = delegate { };

            public Seg(string[] names, int init)
            {
                Index = init;
                for (int i = 0; i < names.Length; i++)
                {
                    int k = i;
                    var b = new Border
                    {
                        CornerRadius = new CornerRadius(6),
                        Padding = new Thickness(9, 4, 9, 5),
                        Margin = new Thickness(0, 0, 4, 0),
                        BorderThickness = new Thickness(1),
                        Cursor = Cursors.Hand,
                        Child = new TextBlock { Text = names[i], FontSize = 11.5 }
                    };
                    b.MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs a) { a.Handled = true; };
                    b.MouseLeftButtonUp += delegate { Set(k); Changed(k); };
                    items.Add(b);
                    Panel.Children.Add(b);
                }
                Paint();
            }

            public void Set(int k) { Index = k; Paint(); }

            void Paint()
            {
                for (int i = 0; i < items.Count; i++)
                {
                    bool on = i == Index;
                    items[i].Background = on ? Theme.Alpha(Theme.Accent, 0x26) : Theme.Alpha(Colors.White, 0x08);
                    items[i].BorderBrush = on ? Theme.BrAccent : Theme.BrBorder;
                    ((TextBlock)items[i].Child).Foreground = on ? Theme.BrAccent : Theme.BrDim;
                }
            }
        }

        // 이름 · 값 · 슬라이더 (· 아래에 색 띠). 두 번 누르면 home으로.
        protected class Row
        {
            public Slider S;
            public TextBlock Val, Name;
            public Border Strip;
            public Func<double, string> Fmt;

            public void Quietly(double v, ToneSheet owner)
            {
                bool q = owner.Quiet; owner.Quiet = true; S.Value = v; owner.Quiet = q;
                Val.Text = Fmt(S.Value);
            }
        }

        protected Row AddRow(Panel host, string label, double min, double max, double init, double home,
                             Func<double, string> fmt, Action<double> set, Brush strip)
        {
            var r = new Row { Fmt = fmt };
            var top = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            r.Name = new TextBlock { Text = label, Foreground = Theme.BrText, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
            top.Children.Add(r.Name);
            r.Val = new TextBlock
            {
                Text = fmt(init), Foreground = Theme.BrAccent, FontFamily = Theme.Mono, FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center
            };
            top.Children.Add(r.Val);
            host.Children.Add(top);

            if (strip != null)
            {
                r.Strip = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = strip, Margin = new Thickness(5, 6, 5, 0) };
                host.Children.Add(r.Strip);
            }

            r.S = new Slider
            {
                Minimum = min, Maximum = max, Value = init,
                IsSnapToTickEnabled = true, TickFrequency = 1,
                Margin = new Thickness(0, strip != null ? 1 : 5, 0, 0), Foreground = Theme.BrAccent
            };
            r.S.ValueChanged += delegate
            {
                r.Val.Text = fmt(r.S.Value);
                set(r.S.Value);
                Fire();
            };
            r.S.MouseDoubleClick += delegate { r.S.Value = home; };
            host.Children.Add(r.S);
            return r;
        }

        protected static string Signed(double v) { int n = (int)Math.Round(v); return (n > 0 ? "+" : "") + n; }
        protected static string Plain(double v) { return ((int)Math.Round(v)).ToString(); }

        protected Border Toggle(string label, bool init, Action<bool> set)
        {
            bool on = init;
            var knob = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(7), Background = Brushes.White };
            var track = new Border
            {
                Width = 30, Height = 18, CornerRadius = new CornerRadius(9), Padding = new Thickness(2),
                Child = knob, VerticalAlignment = VerticalAlignment.Center
            };
            Action paint = delegate
            {
                track.Background = on ? Theme.BrAccent : Theme.Alpha(Colors.White, 0x22);
                knob.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            };
            paint();
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(track);
            row.Children.Add(new TextBlock { Text = label, Foreground = Theme.BrText, FontSize = 12, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            var b = new Border { Child = row, Background = Brushes.Transparent, Cursor = Cursors.Hand, Margin = new Thickness(0, 10, 0, 0) };
            b.MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs a) { a.Handled = true; };
            b.MouseLeftButtonUp += delegate { on = !on; paint(); set(on); Fire(); };
            return b;
        }

        protected static TextBlock Note(string t)
        {
            return new TextBlock { Text = t, Foreground = Theme.BrMuted, FontSize = 10.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(1, 6, 0, 0) };
        }

        protected static LinearGradientBrush Grad(params Color[] cs)
        {
            var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            for (int i = 0; i < cs.Length; i++) g.GradientStops.Add(new GradientStop(cs[i], cs.Length == 1 ? 0 : (double)i / (cs.Length - 1)));
            g.Freeze();
            return g;
        }

        protected static Color HueColor(double h)
        {
            double r, g, b;
            Tone.ToRgb(((h % 360) + 360) % 360, 1, 0.5, out r, out g, out b);
            return Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
        }

        protected static readonly string[] ChannelNames = { "RGB", L.T("빨강", "Red"), L.T("초록", "Green"), L.T("파랑", "Blue") };
        protected static readonly Color[] ChannelColor = { Color.FromRgb(0xE8, 0xE8, 0xEE), Color.FromRgb(0xF0, 0x5A, 0x5A), Color.FromRgb(0x4C, 0xD9, 0x7B), Color.FromRgb(0x5A, 0x9B, 0xF0) };

        // 히스토그램을 w×h 영역에 채운 모양으로
        protected static Geometry HistogramShape(double[] bins, double w, double h)
        {
            double scale = Tone.HistogramScale(bins);
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(0, h), true, true);
                for (int i = 0; i < 256; i++)
                {
                    double v = scale > 0 ? Math.Min(1, bins[i] / scale) : 0;
                    double x0 = i * w / 256, x1 = (i + 1) * w / 256, y = h - v * h;
                    c.LineTo(new Point(x0, y), true, false);
                    c.LineTo(new Point(x1, y), true, false);
                }
                c.LineTo(new Point(w, h), true, false);
            }
            g.Freeze();
            return g;
        }
    }

    // ================= 레벨 =================

    public class LevelsSheet : ToneSheet
    {
        public Tone.Levels Value = new Tone.Levels();
        readonly double[][] hist;
        readonly Seg channel;
        readonly Path histPath;
        readonly Line mBlack, mGamma, mWhite;
        readonly Row rBlack, rGamma, rWhite, rOutB, rOutW;
        const double HW = 300, HH = 96;

        public LevelsSheet(double[][] histogram) : this(histogram, null) { }

        public LevelsSheet(double[][] histogram, Tone.Levels init) : base(L.T("레벨", "Levels"), 340)
        {
            if (init != null) Value = init.Clone();
            hist = histogram;
            channel = new Seg(ChannelNames, 0);
            channel.Changed = delegate { Load(); };
            Body.Children.Add(channel.Panel);

            var plot = new Canvas { Width = HW, Height = HH, Background = Theme.Alpha(Colors.Black, 0x55), ClipToBounds = true };
            histPath = new Path();
            plot.Children.Add(histPath);
            mBlack = Marker(plot, Colors.Black); mGamma = Marker(plot, Color.FromRgb(0x88, 0x88, 0x90)); mWhite = Marker(plot, Colors.White);
            Body.Children.Add(new Border { Child = plot, BorderBrush = Theme.BrBorder, BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left });

            var auto = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 2) };
            auto.Children.Add(Small(L.T("자동 대비", "Contrast"), delegate { SetAll(Tone.Auto(hist, Tone.AutoMode.Contrast)); }));
            auto.Children.Add(Small(L.T("자동 색", "Color"), delegate { SetAll(Tone.Auto(hist, Tone.AutoMode.Color)); }));
            auto.Children.Add(Small(L.T("색 + 중간톤", "Color + Mid"), delegate { SetAll(Tone.Auto(hist, Tone.AutoMode.Neutral)); }));
            Body.Children.Add(auto);

            rBlack = AddRow(Body, L.T("입력 검정", "Input black"), 0, 254, 0, 0, Plain, delegate(double v) { Cur.Black = v; Fix(); }, null);
            // 감마는 0.1~9.99라 곧은 눈금이면 1.00이 왼쪽 끝에 몰린다. 로그 눈금으로 1.00을 한가운데에 둔다.
            rGamma = AddRow(Body, L.T("중간톤 (감마)", "Midtones (gamma)"), -100, 100, 0, 0, delegate(double v) { return Math.Pow(10, v / 100).ToString("0.00"); },
                            delegate(double v) { Cur.Gamma = Math.Pow(10, v / 100); Fix(); }, null);
            rWhite = AddRow(Body, L.T("입력 흰색", "Input white"), 1, 255, 255, 255, Plain, delegate(double v) { Cur.White = v; Fix(); }, null);
            rOutB = AddRow(Body, L.T("출력 검정", "Output black"), 0, 255, 0, 0, Plain, delegate(double v) { Cur.OutBlack = v; Fix(); }, Grad(Colors.Black, Colors.White));
            rOutW = AddRow(Body, L.T("출력 흰색", "Output white"), 0, 255, 255, 255, Plain, delegate(double v) { Cur.OutWhite = v; Fix(); }, Grad(Colors.Black, Colors.White));
            Body.Children.Add(Note(L.T("검정·흰색 사이로 그림을 펴고, 감마로 중간 밝기를 민다. 채널을 고르면 그 색만.", "Stretches the image between black and white; gamma shifts the midtones. Pick a channel to adjust only that color.")));
            Footer();
            Load();
        }

        Tone.LevelRange Cur { get { return Value.R[channel.Index]; } }

        void Fix() { Cur.Normalize(); Markers(); }

        static Line Marker(Canvas c, Color col)
        {
            var l = new Line { Y1 = 0, Y2 = HH, Stroke = new SolidColorBrush(col), StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 3, 2 } };
            c.Children.Add(l);
            return l;
        }

        void Markers()
        {
            var r = Cur;
            Action<Line, double> at = delegate(Line l, double v) { l.X1 = l.X2 = v / 255 * HW; };
            at(mBlack, r.Black); at(mWhite, r.White);
            // 감마 표시: 출력이 정확히 절반이 되는 입력
            at(mGamma, r.Black + Math.Pow(0.5, r.Gamma) * (r.White - r.Black));
        }

        void Load()
        {
            int c = channel.Index;
            histPath.Data = HistogramShape(hist[c], HW, HH);
            histPath.Fill = new SolidColorBrush(Color.FromArgb(0xB0, ChannelColor[c].R, ChannelColor[c].G, ChannelColor[c].B));
            var r = Cur;
            rBlack.Quietly(r.Black, this); rGamma.Quietly(Math.Round(Math.Log10(r.Gamma) * 100), this); rWhite.Quietly(r.White, this);
            rOutB.Quietly(r.OutBlack, this); rOutW.Quietly(r.OutWhite, this);
            Markers();
        }

        void SetAll(Tone.Levels l) { Value = l; Load(); Fire(); }

        Border Small(string t, Action act)
        {
            var b = Btn(t, false, act);
            b.Padding = new Thickness(10, 5, 10, 6);
            b.Margin = new Thickness(0, 0, 6, 0);
            return b;
        }

        public override bool IsIdentity { get { return Value.IsIdentity; } }
        public override ToneFn Current() { return Value.Clone().Fn(); }
        public override object Settings() { return Value.Clone(); }
        public override string Kind { get { return "levels"; } }
        protected override void ResetAll() { Value = new Tone.Levels(); Load(); }
    }

    // ================= 커브 =================

    public class CurvesSheet : ToneSheet
    {
        public Tone.Curves Value = new Tone.Curves();
        readonly double[][] hist;
        readonly Seg channel;
        readonly Canvas plot;
        readonly Path histPath;
        readonly Polyline line;
        readonly Canvas dots;
        readonly TextBlock readout;
        int selected = -1, dragging = -1;
        const double PW = 280;

        public CurvesSheet(double[][] histogram) : this(histogram, null) { }

        public CurvesSheet(double[][] histogram, Tone.Curves init) : base(L.T("커브", "Curves"), 320)
        {
            if (init != null) Value = init.Clone();
            hist = histogram;
            channel = new Seg(ChannelNames, 0);
            channel.Changed = delegate { selected = -1; Redraw(); };
            Body.Children.Add(channel.Panel);

            plot = new Canvas { Width = PW, Height = PW, Background = Theme.Alpha(Colors.Black, 0x55), ClipToBounds = true, Cursor = Cursors.Cross };
            histPath = new Path();
            plot.Children.Add(histPath);
            for (int i = 1; i < 4; i++)
            {
                double f = PW * i / 4;
                plot.Children.Add(new Line { X1 = f, X2 = f, Y1 = 0, Y2 = PW, Stroke = Theme.Alpha(Colors.White, 0x1E), StrokeThickness = 1 });
                plot.Children.Add(new Line { Y1 = f, Y2 = f, X1 = 0, X2 = PW, Stroke = Theme.Alpha(Colors.White, 0x1E), StrokeThickness = 1 });
            }
            plot.Children.Add(new Line { X1 = 0, Y1 = PW, X2 = PW, Y2 = 0, Stroke = Theme.Alpha(Colors.White, 0x28), StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 2, 3 } });
            line = new Polyline { StrokeThickness = 2 };
            plot.Children.Add(line);
            dots = new Canvas();
            plot.Children.Add(dots);
            plot.MouseLeftButtonDown += OnDown;
            plot.MouseMove += OnMove;
            plot.MouseLeftButtonUp += delegate { dragging = -1; plot.ReleaseMouseCapture(); };
            plot.MouseRightButtonUp += delegate(object o, MouseButtonEventArgs e) { int i = Near(e.GetPosition(plot)); if (i >= 0) { selected = i; RemoveSelected(); } };
            Body.Children.Add(new Border { Child = plot, BorderBrush = Theme.BrBorder, BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left });

            readout = new TextBlock { Foreground = Theme.BrAccent, FontFamily = Theme.Mono, FontSize = 11, Margin = new Thickness(1, 8, 0, 0) };
            Body.Children.Add(readout);
            Body.Children.Add(Note(L.T("눌러서 점을 더하고 끌어서 민다. 오른쪽 클릭이나 Delete로 점을 뺀다.", "Click to add a point, drag to move it. Right-click or press Delete to remove a point.")));

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            var rm = Btn(L.T("이 점 빼기", "Remove Point"), false, delegate { RemoveSelected(); });
            rm.Padding = new Thickness(10, 5, 10, 6); rm.Margin = new Thickness(0, 0, 6, 0);
            var rs = Btn(L.T("이 채널 곧게", "Reset Channel"), false, delegate { Value.P[channel.Index] = Tone.Curves.Straight(); selected = -1; Redraw(); Fire(); });
            rs.Padding = new Thickness(10, 5, 10, 6);
            row.Children.Add(rm); row.Children.Add(rs);
            Body.Children.Add(row);

            PreviewKeyDown += delegate(object o, KeyEventArgs e) { if (e.Key == Key.Delete || e.Key == Key.Back) { RemoveSelected(); e.Handled = true; } };
            Footer();
            Redraw();
        }

        List<double[]> Pts { get { return Value.P[channel.Index]; } }

        Point ToScreen(double x, double y) { return new Point(x / 255 * PW, (1 - y / 255) * PW); }

        int Near(Point m)
        {
            int best = -1; double bd = 9;
            for (int i = 0; i < Pts.Count; i++)
            {
                var s = ToScreen(Pts[i][0], Pts[i][1]);
                double d = (s - m).Length;
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        void OnDown(object o, MouseButtonEventArgs e)
        {
            e.Handled = true;           // 창 끌기로 넘어가지 않게
            var m = e.GetPosition(plot);
            double x = Math.Max(0, Math.Min(255, m.X / PW * 255)), y = Math.Max(0, Math.Min(255, 255 - m.Y / PW * 255));
            int i = Near(m);
            if (i < 0)
            {
                if (Pts.Count >= 32 || x <= 1 || x >= 254) return;
                foreach (var p in Pts) if (Math.Abs(p[0] - x) <= 1) return;
                var np = new[] { x, y };
                Pts.Add(np);
                Pts.Sort(delegate(double[] a, double[] b) { return a[0].CompareTo(b[0]); });
                i = Pts.IndexOf(np);
                Fire();
            }
            selected = dragging = i;
            plot.CaptureMouse();
            Redraw();
        }

        void OnMove(object o, MouseEventArgs e)
        {
            if (dragging < 0) return;
            var m = e.GetPosition(plot);
            double x = Math.Max(0, Math.Min(255, m.X / PW * 255)), y = Math.Max(0, Math.Min(255, 255 - m.Y / PW * 255));
            var p = Pts;
            int i = dragging;
            p[i][1] = Math.Round(y);
            if (i > 0 && i < p.Count - 1) p[i][0] = Math.Round(Math.Min(p[i + 1][0] - 1, Math.Max(p[i - 1][0] + 1, x)));
            Redraw();
            Fire();
        }

        void RemoveSelected()
        {
            if (selected <= 0 || selected >= Pts.Count - 1) return;
            Pts.RemoveAt(selected);
            selected = -1;
            Redraw();
            Fire();
        }

        void Redraw()
        {
            int c = channel.Index;
            histPath.Data = HistogramShape(hist[c], PW, PW);
            histPath.Fill = new SolidColorBrush(Color.FromArgb(0x40, ChannelColor[c].R, ChannelColor[c].G, ChannelColor[c].B));
            line.Stroke = new SolidColorBrush(ChannelColor[c]);
            var pc = new PointCollection();
            for (int x = 0; x <= 255; x++) pc.Add(ToScreen(x, Value.Value(x, c)));
            line.Points = pc;
            dots.Children.Clear();
            for (int i = 0; i < Pts.Count; i++)
            {
                var s = ToScreen(Pts[i][0], Pts[i][1]);
                var d = new Ellipse { Width = 9, Height = 9, Fill = i == selected ? Theme.BrAccent : Brushes.White, Stroke = Brushes.Black, StrokeThickness = 1 };
                Canvas.SetLeft(d, s.X - 4.5); Canvas.SetTop(d, s.Y - 4.5);
                dots.Children.Add(d);
            }
            readout.Text = selected >= 0 && selected < Pts.Count
                ? L.F("입력 {0}  →  출력 {1}", "Input {0}  →  Output {1}", (int)Pts[selected][0], (int)Pts[selected][1])
                : " ";
        }

        public override bool IsIdentity { get { return Value.IsIdentity; } }
        public override ToneFn Current() { return Value.Clone().Fn(); }
        public override object Settings() { return Value.Clone(); }
        public override string Kind { get { return "curves"; } }
        protected override void ResetAll() { Value = new Tone.Curves(); selected = -1; Redraw(); }
    }

    // ================= 색조/채도 =================

    public class HueSatSheet : ToneSheet
    {
        public Tone.HueSat Value = new Tone.HueSat();
        readonly Seg range;
        readonly Row rHue, rSat, rLight;
        readonly Border before, after;
        readonly StackPanel rangeHost;

        public HueSatSheet() : this(null) { }

        public HueSatSheet(Tone.HueSat init) : base(L.T("색조 / 채도", "Hue / Saturation"), L.En ? 470 : 400)
        {
            if (init != null) Value = init.Clone();
            rangeHost = new StackPanel();
            range = new Seg(Tone.RangeNames, 0);
            range.Changed = delegate { Load(); };
            rangeHost.Children.Add(range.Panel);
            Body.Children.Add(rangeHost);

            rHue = AddRow(Body, L.T("색조", "Hue"), -180, 180, 0, 0, Signed, delegate(double v)
            { if (Value.Colorize) Value.CHue = v; else Value.Hue[range.Index] = v; Bars(); }, null);
            rSat = AddRow(Body, L.T("채도", "Saturation"), -100, 100, 0, 0, Signed, delegate(double v)
            { if (Value.Colorize) Value.CSat = v; else Value.Sat[range.Index] = v; Bars(); }, null);
            rLight = AddRow(Body, L.T("밝기", "Lightness"), -100, 100, 0, 0, Signed, delegate(double v)
            { if (Value.Colorize) Value.CLight = v; else Value.Light[range.Index] = v; Bars(); }, Grad(Colors.Black, Colors.Gray, Colors.White));

            before = new Border { Height = 10, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 14, 0, 3) };
            after = new Border { Height = 10, CornerRadius = new CornerRadius(2) };
            Body.Children.Add(before);
            Body.Children.Add(after);
            Body.Children.Add(Note(L.T("위 띠는 원래 색, 아래 띠는 바뀐 색. 범위를 고르면 그 색 부근만 바뀐다.", "Top bar shows the original colors, bottom bar the result. Pick a range to change only colors near it.")));

            if (Value.Colorize) rangeHost.Visibility = Visibility.Collapsed;
            Body.Children.Add(Toggle(L.T("색 입히기 (한 가지 색으로)", "Colorize (single color)"), Value.Colorize, delegate(bool on)
            {
                Value.Colorize = on;
                rangeHost.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
                Load();
            }));
            Footer();
            Load();
        }

        void Load()
        {
            // 범위를 바꾸면 슬라이더가 끝으로 밀리며 값을 덮어쓸 수 있어, 원하는 값을 먼저 쥐고 바꾼다.
            bool q = Quiet; Quiet = true;
            int k = range.Index;
            double h = Value.Colorize ? Value.CHue : Value.Hue[k];
            double s = Value.Colorize ? Value.CSat : Value.Sat[k];
            double l = Value.Colorize ? Value.CLight : Value.Light[k];
            rHue.S.Minimum = Value.Colorize ? 0 : -180; rHue.S.Maximum = Value.Colorize ? 360 : 180;
            rSat.S.Minimum = Value.Colorize ? 0 : -100;
            rHue.Fmt = Value.Colorize ? (Func<double, string>)Plain : Signed;
            rHue.Quietly(h, this); rSat.Quietly(s, this); rLight.Quietly(l, this);
            Quiet = q;
            Bars();
        }

        void Bars()
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            var a = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            var fn = Value.Clone().Fn();
            var probe = new Canvas32(37, 1);
            for (int i = 0; i <= 36; i++)
            {
                var c = HueColor(i * 10);
                b.GradientStops.Add(new GradientStop(c, i / 36.0));
                probe.P[i] = Canvas32.Pack(255, c.R, c.G, c.B);
            }
            var outp = new Canvas32(37, 1);
            fn(probe, outp);
            for (int i = 0; i <= 36; i++)
                a.GradientStops.Add(new GradientStop(Color.FromRgb(Canvas32.R(outp.P[i]), Canvas32.G(outp.P[i]), Canvas32.B(outp.P[i])), i / 36.0));
            before.Background = b;
            after.Background = a;
        }

        public override bool IsIdentity { get { return Value.IsIdentity; } }
        public override object Settings() { return Value.Clone(); }
        public override string Kind { get { return "huesat"; } }
        public override ToneFn Current() { return Value.Clone().Fn(); }
        protected override void ResetAll()
        {
            bool col = Value.Colorize;
            Value = new Tone.HueSat { Colorize = col };
            Load();
        }
    }

    // ================= 컬러 밸런스 =================

    public class BalanceSheet : ToneSheet
    {
        public Tone.Balance Value = new Tone.Balance();
        readonly Seg tone;
        readonly Row[] rows = new Row[3];

        public BalanceSheet() : this(null) { }

        public BalanceSheet(Tone.Balance init) : base(L.T("컬러 밸런스", "Color Balance"), 330)
        {
            if (init != null) Value = init.Clone();
            tone = new Seg(new[] { L.T("어두운 쪽", "Shadows"), L.T("중간", "Midtones"), L.T("밝은 쪽", "Highlights") }, 1);
            tone.Changed = delegate { Load(); };
            Body.Children.Add(tone.Panel);

            string[] names = { L.T("청록 ↔ 빨강", "Cyan ↔ Red"), L.T("자홍 ↔ 초록", "Magenta ↔ Green"), L.T("노랑 ↔ 파랑", "Yellow ↔ Blue") };
            Color[][] ends =
            {
                new[] { Color.FromRgb(0x00, 0xC8, 0xD8), Color.FromRgb(0xE8, 0x3A, 0x3A) },
                new[] { Color.FromRgb(0xD8, 0x3A, 0xC8), Color.FromRgb(0x3A, 0xC8, 0x50) },
                new[] { Color.FromRgb(0xE8, 0xD0, 0x30), Color.FromRgb(0x3A, 0x6A, 0xE8) },
            };
            for (int i = 0; i < 3; i++)
            {
                int k = i;
                rows[i] = AddRow(Body, names[i], -100, 100, 0, 0, Signed, delegate(double v) { Value.V[tone.Index, k] = v; },
                                 Grad(ends[i][0], Color.FromRgb(0x80, 0x80, 0x80), ends[i][1]));
            }
            Body.Children.Add(Toggle(L.T("밝기 유지", "Preserve Luminosity"), Value.KeepLuminosity, delegate(bool on) { Value.KeepLuminosity = on; }));
            Body.Children.Add(Note(L.T("어두운 곳·중간·밝은 곳의 색을 따로 민다. 밝기 유지를 켜면 색만 바뀐다.", "Shifts color separately in shadows, midtones and highlights. With Preserve Luminosity on, only color changes.")));
            Footer();
            Load();
        }

        void Load() { for (int i = 0; i < 3; i++) rows[i].Quietly(Value.V[tone.Index, i], this); }

        public override bool IsIdentity { get { return Value.IsIdentity; } }
        public override ToneFn Current() { return Value.Fn(); }
        public override object Settings() { return Value.Clone(); }
        public override string Kind { get { return "balance"; } }
        protected override void ResetAll() { bool keep = Value.KeepLuminosity; Value = new Tone.Balance { KeepLuminosity = keep }; Load(); }
    }

    // ================= 흑백 =================

    public class BlackWhiteSheet : ToneSheet
    {
        public Tone.BlackWhite Value = new Tone.BlackWhite();
        readonly Row[] rows = new Row[6];
        readonly Row rTHue, rTSat;
        readonly StackPanel tintHost;

        static readonly double[] Hues = { 0, 60, 120, 180, 240, 300 };

        public BlackWhiteSheet() : this(null) { }

        public BlackWhiteSheet(Tone.BlackWhite init) : base(L.T("흑백", "Black & White"), 330)
        {
            if (init != null) Value = init.Clone();
            for (int i = 0; i < 6; i++)
            {
                int k = i;
                rows[i] = AddRow(Body, Tone.BwNames[i], -200, 300, Value.W[i], Value.W[i],
                                 delegate(double v) { return Plain(v) + "%"; },
                                 delegate(double v) { Value.W[k] = v; }, Grad(Colors.Black, HueColor(Hues[k]), Colors.White));
            }
            tintHost = new StackPanel { Visibility = Value.Tint ? Visibility.Visible : Visibility.Collapsed };
            Body.Children.Add(Toggle(L.T("색조 입히기 (세피아 등)", "Tint (sepia, etc.)"), Value.Tint, delegate(bool on)
            {
                Value.Tint = on;
                tintHost.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            }));
            var hues = new Color[13];
            for (int i = 0; i <= 12; i++) hues[i] = HueColor(i * 30);
            rTHue = AddRow(tintHost, L.T("색조", "Hue"), 0, 360, Value.TintHue, 40, Plain, delegate(double v) { Value.TintHue = v; }, Grad(hues));
            rTSat = AddRow(tintHost, L.T("채도", "Saturation"), 0, 100, Value.TintSat, 20, delegate(double v) { return Plain(v) + "%"; }, delegate(double v) { Value.TintSat = v; }, null);
            Body.Children.Add(tintHost);
            Body.Children.Add(Note(L.T("색마다 회색이 될 때 얼마나 밝을지 정한다. 하늘을 어둡게 하려면 파랑을 내려라.", "Sets how bright each color becomes in gray. Lower Blue to darken the sky.")));
            Footer();
        }

        // 흑백은 켜는 순간부터 바뀐 것이다
        public override bool IsIdentity { get { return false; } }
        public override object Settings() { return Value.Clone(); }
        public override string Kind { get { return "bw"; } }
        public override ToneFn Current() { return Value.Fn(); }
        protected override void ResetAll()
        {
            var d = new Tone.BlackWhite { Tint = Value.Tint, TintHue = Value.TintHue, TintSat = Value.TintSat };
            Value = d;
            for (int i = 0; i < 6; i++) rows[i].Quietly(d.W[i], this);
        }
    }
}
