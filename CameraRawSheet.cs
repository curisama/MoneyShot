// Money Shot — RAW 보정 창 (라이트룸식 현상 패널. "Camera Raw"는 Adobe 상표라 화면엔 쓰지 않는다)
//
// Compositor(MIT, Wonder Assembly LLC)의 CameraRawControls·CameraRawColorControls·
// CameraRawDetailOpticsControls·CameraRawGeometryCalibrationControls·CameraRawSlider.swift를 옮겼다.
// 계산은 CameraRaw.cs.
//
// 라이트룸 패널처럼 접었다 폈다 하는 칸 열 개. 칸 머리의 눈을 끄면 그 칸만 빼고 본다.
// 맨 위 히스토그램은 레이어를 작게 줄인 사본에 같은 설정을 걸어 센다 — 슬라이더를 끄는 대로 따라 움직인다.
// 그림 위를 눌러야 하는 것은 이 창에서 할 수 없어 뺐다: 화이트 밸런스 스포이드, 이미지 위에서 끌어
// 조정하기, 업라이트 안내선 긋기, Option 누르고 끌 때의 클리핑 보기, 점 색 고르기, 프린지 스포이드.
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace MoneyShot
{
    public class CameraRawSheet : ToneSheet
    {
        public CameraRawSettings Value = new CameraRawSettings();

        // 묶음 번호는 CameraRawSettings.Applying과 같다. 화면 순서는 Compositor를 따른다.
        const int GLight = 0, GColor = 1, GEffects = 2, GCurve = 3, GMixer = 4, GGrading = 5, GDetail = 6, GOptics = 7, GGeometry = 8, GCalib = 9;
        static readonly int[] Order = { GLight, GColor, GGrading, GEffects, GCurve, GMixer, GDetail, GOptics, GGeometry, GCalib };
        static readonly string[] Titles = { L.T("빛", "Light"), L.T("색", "Color"), L.T("효과", "Effects"), L.T("커브", "Curve"), L.T("색상 혼합", "Color Mixer"), L.T("색 보정", "Color Grading"), L.T("세부", "Detail"), L.T("광학", "Optics"), L.T("기하", "Geometry"), L.T("카메라 보정", "Calibration") };
        static readonly string[] Families = { L.T("빨강", "Red"), L.T("주황", "Orange"), L.T("노랑", "Yellow"), L.T("녹색", "Green"), L.T("아쿠아", "Aqua"), L.T("파랑", "Blue"), L.T("자주", "Purple"), L.T("마젠타", "Magenta") };

        const double HistW = 356, HistH = 84;
        static readonly double LabelW = L.En ? 116 : 92;   // 영어 이름이 더 길다

        readonly Canvas32 source;
        Canvas32 thumb;
        double thumbScale = 1;

        readonly bool[] shown = { true, true, true, true, true, true, true, true, true, true };
        readonly bool[] open = { true, true, false, false, false, true, false, false, false, false };
        readonly StackPanel[] content = new StackPanel[10];
        readonly TextBlock[] chevron = new TextBlock[10];
        readonly Border[] eye = new Border[10];
        readonly List<Action>[] refresh = new List<Action>[10];
        readonly Action<StackPanel>[] builders = new Action<StackPanel>[10];

        readonly Path histR, histG, histB;
        readonly DispatcherTimer histWait;

        // 창 안에서만 쓰는 쪽 고르기 상태
        int mixerPage, mixerTab, mixerSwatch, curvePage, curveChannel, gradePage;
        int curveSelected = -1;

        public CameraRawSheet(Canvas32 source) : base(L.T("RAW 보정", "RAW Develop"), 400)
        {
            this.source = source;
            Value.Seed = (uint)Environment.TickCount;

            // ---- 히스토그램 ----
            var plot = new Canvas { Width = HistW, Height = HistH, Background = Theme.Alpha(Colors.Black, 0x55), ClipToBounds = true };
            histR = new Path { Fill = new SolidColorBrush(Color.FromArgb(0x8C, 0xF0, 0x4A, 0x4A)) };
            histG = new Path { Fill = new SolidColorBrush(Color.FromArgb(0x8C, 0x48, 0xD0, 0x6C)) };
            histB = new Path { Fill = new SolidColorBrush(Color.FromArgb(0x8C, 0x4A, 0x8A, 0xF0)) };
            plot.Children.Add(histR); plot.Children.Add(histG); plot.Children.Add(histB);
            // 어두운 영역·중간·밝은 영역 눈금
            for (int i = 1; i < 4; i++)
                plot.Children.Add(new Line { X1 = HistW * i / 4, X2 = HistW * i / 4, Y1 = 0, Y2 = HistH, Stroke = Theme.Alpha(Colors.White, 0x14), StrokeThickness = 1 });
            Body.Children.Add(new Border
            {
                Child = plot, BorderBrush = Theme.BrBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3),
                HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 10),
                ToolTip = L.T("왼쪽 검정부터 오른쪽 흰색까지: 검정 계열, 어두운 영역, 중간톤, 밝은 영역, 흰색 계열.", "Black on the left to white on the right: Blacks, Shadows, Midtones, Highlights, Whites.")
            });
            histWait = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
            histWait.Tick += delegate { histWait.Stop(); DrawHistogram(); };
            MakeThumb();

            // ---- 칸들 ----
            builders[GLight] = BuildLight;
            builders[GColor] = BuildColor;
            builders[GEffects] = BuildEffects;
            builders[GCurve] = BuildCurve;
            builders[GMixer] = BuildMixer;
            builders[GGrading] = BuildGrading;
            builders[GDetail] = BuildDetail;
            builders[GOptics] = BuildOptics;
            builders[GGeometry] = BuildGeometry;
            builders[GCalib] = BuildCalibration;

            var host = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
            foreach (int g in Order) host.Children.Add(Section(g));
            // 낮은 화면에서도 아래 버튼 줄이 잘리지 않게, 머리·히스토그램·버튼 몫을 빼고 남는 만큼만
            double room = SystemParameters.WorkArea.Height - 280;
            var scroll = new ScrollViewer
            {
                Content = host, MaxHeight = Math.Max(240, Math.Min(640, room)),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            Body.Children.Add(scroll);
            Footer();

            bool q = Quiet; Quiet = true;
            for (int g = 0; g < 10; g++) Rebuild(g);
            Quiet = q;
            Eyes();
            DrawHistogram();
        }

        // ================= 껍데기 =================

        CameraRawSettings Shown() { return Value.Applying(shown); }

        public override bool IsIdentity { get { return Shown().IsIdentity; } }

        public override ToneFn Current()
        {
            var c = Shown().Clone();
            return delegate(Canvas32 s, Canvas32 d) { CameraRaw.Apply(c, s, d); };
        }

        protected override void ResetAll()
        {
            Value = new CameraRawSettings { Seed = Value.Seed };
            for (int g = 0; g < 10; g++) shown[g] = true;
            curveSelected = -1;
            for (int g = 0; g < 10; g++) Rebuild(g);
            Eyes();
            HistLater();
        }

        protected override void OnClosed(EventArgs e) { histWait.Stop(); base.OnClosed(e); }

        // 값이 바뀌었다: 미리보기 예약, 다른 칸 표시 맞추기, 히스토그램 예약
        void Touch()
        {
            if (Quiet) return;
            Fire();
            After();
        }

        void After()
        {
            bool q = Quiet; Quiet = true;
            foreach (var list in refresh)
                if (list != null) foreach (var a in list) a();
            Quiet = q;
            Eyes();
            HistLater();
        }

        void Rebuild(int g)
        {
            bool q = Quiet; Quiet = true;
            content[g].Children.Clear();
            refresh[g] = new List<Action>();
            builders[g](content[g]);
            Quiet = q;
        }

        // ---------- 칸 머리 ----------

        FrameworkElement Section(int g)
        {
            var box = new StackPanel();
            var head = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand, Margin = new Thickness(0, 4, 0, 4) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            chevron[g] = new TextBlock { FontFamily = Theme.Glyph, FontSize = 9, Foreground = Theme.BrDim, VerticalAlignment = VerticalAlignment.Center };
            head.Children.Add(chevron[g]);
            var title = new TextBlock { Text = Titles[g], Foreground = Theme.BrText, FontSize = 13.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(title, 1);
            head.Children.Add(title);

            var eyeGlyph = new TextBlock { FontFamily = Theme.Glyph, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            eye[g] = new Border { Child = eyeGlyph, Padding = new Thickness(6, 2, 2, 2), Background = Brushes.Transparent, Cursor = Cursors.Hand };
            Grid.SetColumn(eye[g], 2);
            head.Children.Add(eye[g]);
            int k = g;
            eye[g].MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs a) { a.Handled = true; };
            eye[g].MouseLeftButtonUp += delegate(object o, MouseButtonEventArgs a)
            {
                a.Handled = true;
                shown[k] = !shown[k];
                Fire();
                Eyes();
                HistLater();
            };

            content[g] = new StackPanel { Margin = new Thickness(18, 0, 0, 10) };
            head.MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs a) { a.Handled = true; };
            head.MouseLeftButtonUp += delegate
            {
                open[k] = !open[k];
                content[k].Visibility = open[k] ? Visibility.Visible : Visibility.Collapsed;
                chevron[k].Text = open[k] ? "" : "";
            };
            content[g].Visibility = open[g] ? Visibility.Visible : Visibility.Collapsed;
            chevron[g].Text = open[g] ? "" : "";

            box.Children.Add(new Border { Height = 1, Background = Theme.Alpha(Colors.White, 0x12), Margin = new Thickness(0, 2, 0, 2) });
            box.Children.Add(head);
            box.Children.Add(content[g]);
            return box;
        }

        bool Adjusts(int g)
        {
            var v = Value;
            switch (g)
            {
                case GLight: return v.AdjustsLight;
                case GColor: return v.AdjustsColor;
                case GEffects: return v.AdjustsEffects;
                case GCurve: return v.Curve.Adjusts;
                case GMixer: return v.Mixer.Adjusts;
                case GGrading: return v.Grading.Adjusts;
                case GDetail: return v.Detail.Adjusts;
                case GOptics: return v.Optics.Adjusts;
                case GGeometry: return v.Geometry.Adjusts;
                default: return v.Calibration.Adjusts;
            }
        }

        // 바꾼 게 있는 칸에만 눈이 뜬다(원본과 같다). 꺼 둔 칸은 값이 없어도 계속 보여 다시 켤 수 있게.
        void Eyes()
        {
            for (int g = 0; g < 10; g++)
            {
                if (eye[g] == null) continue;
                bool any = Adjusts(g);
                eye[g].Visibility = any || !shown[g] ? Visibility.Visible : Visibility.Collapsed;
                var t = (TextBlock)eye[g].Child;
                t.Text = shown[g] ? "" : "";
                t.Foreground = shown[g] ? Theme.BrDim : Theme.BrAccent;
                eye[g].ToolTip = shown[g] ? L.F("{0} 숨기고 보기", "Hide {0} to compare", Titles[g]) : L.F("{0} 다시 켜기", "Turn {0} back on", Titles[g]);
            }
        }

        // ---------- 히스토그램 ----------

        // 긴 변이 256쯤 되게 블록 평균으로 줄인다(투명한 픽셀은 덜 센다)
        void MakeThumb()
        {
            if (source == null || source.W <= 0 || source.H <= 0) return;
            int f = Math.Max(1, (int)Math.Ceiling(Math.Max(source.W, source.H) / 256.0));
            int sw = (source.W + f - 1) / f, sh = (source.H + f - 1) / f;
            thumb = new Canvas32(sw, sh);
            thumbScale = 1.0 / f;
            for (int ty = 0; ty < sh; ty++)
                for (int tx = 0; tx < sw; tx++)
                {
                    long sa = 0, sr = 0, sg = 0, sb = 0; int n = 0;
                    for (int y = ty * f; y < Math.Min(source.H, ty * f + f); y++)
                        for (int x = tx * f; x < Math.Min(source.W, tx * f + f); x++)
                        {
                            int c = source.P[y * source.W + x];
                            int a = (int)((uint)c >> 24);
                            sa += a; sr += a * ((c >> 16) & 255); sg += a * ((c >> 8) & 255); sb += a * (c & 255);
                            n++;
                        }
                    if (sa == 0) continue;
                    thumb.P[ty * sw + tx] = (int)((sa + n / 2) / n) << 24 | (int)(sr / sa) << 16 | (int)(sg / sa) << 8 | (int)(sb / sa);
                }
        }

        void HistLater() { histWait.Stop(); histWait.Start(); }

        void DrawHistogram()
        {
            if (thumb == null) return;
            var outp = new Canvas32(thumb.W, thumb.H);
            CameraRaw.Apply(Shown(), thumb, outp, thumbScale);
            var h = Tone.Histogram(outp, null);
            // 세 띠가 같은 높이 눈금을 써야 서로 견줄 수 있다
            double scale = Math.Max(Tone.HistogramScale(h[1]), Math.Max(Tone.HistogramScale(h[2]), Tone.HistogramScale(h[3])));
            histR.Data = Ribbon(h[1], scale);
            histG.Data = Ribbon(h[2], scale);
            histB.Data = Ribbon(h[3], scale);
        }

        static Geometry Ribbon(double[] bins, double scale)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(0, HistH), true, true);
                for (int i = 0; i < 256; i++)
                {
                    double v = scale > 0 ? Math.Min(1, bins[i] / scale) : 0;
                    c.LineTo(new Point((i + 0.5) * HistW / 256, HistH - v * HistH), true, true);
                }
                c.LineTo(new Point(HistW, HistH), true, false);
            }
            g.Freeze();
            return g;
        }

        // ================= 조립 부품 =================

        // 이름 | 슬라이더(아래에 색 띠) | 값. 이름이나 손잡이를 두 번 누르면 home으로.
        Row Slide(Panel host, string label, double min, double max, double init, double home,
                  Func<double, string> fmt, Action<double> set, Brush strip)
        {
            var r = new Row { Fmt = fmt };
            var g = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LabelW) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            r.Name = new TextBlock
            {
                Text = label, Foreground = Theme.BrDim, FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis, Background = Brushes.Transparent
            };
            g.Children.Add(r.Name);

            var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            r.S = new Slider
            {
                Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, init)),
                IsSnapToTickEnabled = true, TickFrequency = 1, SmallChange = 1, LargeChange = 10,
                Foreground = Theme.BrAccent
            };
            mid.Children.Add(r.S);
            if (strip != null)
            {
                r.Strip = new Border { Height = 3, CornerRadius = new CornerRadius(1.5), Background = strip, Margin = new Thickness(7, -2, 7, 0) };
                mid.Children.Add(r.Strip);
            }
            Grid.SetColumn(mid, 1);
            g.Children.Add(mid);

            r.Val = new TextBlock
            {
                Text = fmt(r.S.Value), Foreground = Theme.BrAccent, FontFamily = Theme.Mono, FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(r.Val, 2);
            g.Children.Add(r.Val);

            r.S.ValueChanged += delegate
            {
                r.Val.Text = fmt(r.S.Value);
                set(r.S.Value);
                Touch();
            };
            r.S.MouseDoubleClick += delegate { r.S.Value = home; };
            r.Name.MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs a)
            {
                if (a.ClickCount == 2) { r.S.Value = home; a.Handled = true; }
            };
            host.Children.Add(g);
            return r;
        }

        // 값을 다른 곳에서 바꿨을 때 슬라이더를 조용히 맞추는 일을 칸의 새로 고침 목록에 건다
        void Bind(int g, Row r, Func<double> get) { refresh[g].Add(delegate { r.Quietly(get(), this); }); }

        static TextBlock Sub(string t)
        {
            return new TextBlock { Text = t, Foreground = Theme.BrText, FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 2) };
        }

        Seg Choice(Panel host, string[] names, int init, Action<int> changed)
        {
            var s = new Seg(names, init);
            s.Panel.Margin = new Thickness(0, 6, 0, 4);
            s.Changed = changed;
            host.Children.Add(s.Panel);
            return s;
        }

        static Color Hsb(double deg, double s, double v)
        {
            deg = ((deg % 360) + 360) % 360;
            double c = v * s, hp = deg / 60, x = c * (1 - Math.Abs(hp % 2 - 1)), m = v - c;
            double r = 0, g = 0, b = 0;
            if (hp < 1) { r = c; g = x; }
            else if (hp < 2) { r = x; g = c; }
            else if (hp < 3) { g = c; b = x; }
            else if (hp < 4) { g = x; b = c; }
            else if (hp < 5) { r = x; b = c; }
            else { r = c; b = x; }
            return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
        }

        static Color Rgbf(double r, double g, double b) { return Color.FromRgb((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255)); }

        // CameraRawSliderTrack의 색들
        static readonly Brush TempTrack = Grad(Rgbf(0.22, 0.46, 0.95), Rgbf(0.98, 0.82, 0.18));
        static readonly Brush TintTrack = Grad(Rgbf(0.28, 0.70, 0.34), Rgbf(0.70, 0.40, 0.64));
        static readonly Brush ChromaTrack = Grad(Rgbf(0.62, 0.62, 0.64), Rgbf(0.86, 0.18, 0.20));
        static readonly Brush ToneTrack = Grad(Color.FromRgb(0x10, 0x10, 0x12), Color.FromRgb(0xF2, 0xF2, 0xF2));

        static Brush HueTrack(double c) { return Grad(Hsb(c - 50, 0.85, 0.9), Hsb(c - 25, 0.85, 0.9), Hsb(c, 0.85, 0.9), Hsb(c + 25, 0.85, 0.9), Hsb(c + 50, 0.85, 0.9)); }
        static Brush SatTrack(double c) { return Grad(Rgbf(0.55, 0.55, 0.56), Hsb(c, 0.9, 0.9)); }
        static Brush LumTrack(double c) { return Grad(Hsb(c, 0.55, 0.18), Hsb(c, 0.35, 0.95)); }

        static Brush Spectrum()
        {
            var cs = new Color[13];
            for (int i = 0; i <= 12; i++) cs[i] = Hsb(i * 30, 0.85, 0.9);
            return Grad(cs);
        }

        // ================= 빛 =================

        void BuildLight(StackPanel p)
        {
            var e = Slide(p, L.T("노출", "Exposure"), -500, 500, Value.Exposure * 100, 0,
                          delegate(double v) { return (v / 100).ToString("+0.00;-0.00;0.00"); },
                          delegate(double v) { Value.Exposure = v / 100; }, ToneTrack);
            e.S.ToolTip = L.T("그림 전체를 빛의 스톱 단위로 밝히거나 어둡게 한다.", "Brightens or darkens the whole image in stops of light.");
            Bind(GLight, e, delegate { return Value.Exposure * 100; });
            Bind(GLight, Slide(p, L.T("대비", "Contrast"), -100, 100, Value.Contrast, 0, Signed, delegate(double v) { Value.Contrast = v; }, null), delegate { return Value.Contrast; });
            Bind(GLight, Slide(p, L.T("밝은 영역", "Highlights"), -100, 100, Value.Highlights, 0, Signed, delegate(double v) { Value.Highlights = v; }, null), delegate { return Value.Highlights; });
            Bind(GLight, Slide(p, L.T("어두운 영역", "Shadows"), -100, 100, Value.Shadows, 0, Signed, delegate(double v) { Value.Shadows = v; }, null), delegate { return Value.Shadows; });
            Bind(GLight, Slide(p, L.T("흰색 계열", "Whites"), -100, 100, Value.Whites, 0, Signed, delegate(double v) { Value.Whites = v; }, null), delegate { return Value.Whites; });
            Bind(GLight, Slide(p, L.T("검정 계열", "Blacks"), -100, 100, Value.Blacks, 0, Signed, delegate(double v) { Value.Blacks = v; }, null), delegate { return Value.Blacks; });
        }

        // ================= 색 =================

        void BuildColor(StackPanel p)
        {
            var head = new Grid { Margin = new Thickness(0, 2, 0, 0) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LabelW) });
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.Children.Add(new TextBlock { Text = L.T("화이트 밸런스", "White Balance"), Foreground = Theme.BrDim, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            var wb = new Seg(new[] { L.T("사용자 정의", "Custom"), L.T("자동", "Auto") }, Value.AutoWhiteBalance ? 1 : 0);
            wb.Panel.Margin = new Thickness(0, 4, 0, 4);
            Grid.SetColumn(wb.Panel, 1);
            head.Children.Add(wb.Panel);
            p.Children.Add(head);
            Row temp = null, tint = null;
            wb.Changed = delegate(int k)
            {
                Value.AutoWhiteBalance = k == 1;
                double t, ti;
                if (k == 1 && source != null && CameraRaw.AutoBalance(source, out t, out ti))
                {
                    Value.Temperature = Math.Max(-100, Math.Min(100, t));
                    Value.Tint = Math.Max(-100, Math.Min(100, ti));
                }
                Touch();
            };
            temp = Slide(p, L.T("색온도", "Temperature"), -100, 100, Value.Temperature, 0, Signed, delegate(double v)
            {
                Value.Temperature = v;
                if (!Quiet) { Value.AutoWhiteBalance = false; wb.Set(0); }
            }, TempTrack);
            temp.S.ToolTip = L.T("파랑에서 노랑 쪽으로 민다. 켈빈이 아니라 지금 색에서 얼마나 옮기나다.", "Shifts from blue toward yellow. Not Kelvin — how far to move from the current color.");
            tint = Slide(p, L.T("색조", "Tint"), -100, 100, Value.Tint, 0, Signed, delegate(double v)
            {
                Value.Tint = v;
                if (!Quiet) { Value.AutoWhiteBalance = false; wb.Set(0); }
            }, TintTrack);
            tint.S.ToolTip = L.T("녹색에서 자홍 쪽으로 민다.", "Shifts from green toward magenta.");
            Bind(GColor, temp, delegate { return Value.Temperature; });
            Bind(GColor, tint, delegate { return Value.Tint; });
            refresh[GColor].Add(delegate { wb.Set(Value.AutoWhiteBalance ? 1 : 0); });
            var vib = Slide(p, L.T("생동감", "Vibrance"), -100, 100, Value.Vibrance, 0, Signed, delegate(double v) { Value.Vibrance = v; }, ChromaTrack);
            vib.S.ToolTip = L.T("이미 진한 색보다 옅은 색을 더 밀고, 피부색은 아낀다.", "Boosts muted colors more than saturated ones and spares skin tones.");
            Bind(GColor, vib, delegate { return Value.Vibrance; });
            Bind(GColor, Slide(p, L.T("채도", "Saturation"), -100, 100, Value.Saturation, 0, Signed, delegate(double v) { Value.Saturation = v; }, ChromaTrack), delegate { return Value.Saturation; });
        }

        // ================= 효과 =================

        void BuildEffects(StackPanel p)
        {
            var t = Slide(p, L.T("텍스처", "Texture"), -100, 100, Value.Texture, 0, Signed, delegate(double v) { Value.Texture = v; }, null);
            t.S.ToolTip = L.T("잔결을 살리거나 누그러뜨린다.", "Brings out or softens fine texture.");
            Bind(GEffects, t, delegate { return Value.Texture; });
            var c = Slide(p, L.T("부분 대비", "Clarity"), -100, 100, Value.Clarity, 0, Signed, delegate(double v) { Value.Clarity = v; }, null);
            c.S.ToolTip = L.T("Clarity — 더 넓은 덩어리의 경계 대비를 올리거나 내린다.", "Raises or lowers edge contrast across larger areas.");
            Bind(GEffects, c, delegate { return Value.Clarity; });
            var d = Slide(p, L.T("디헤이즈", "Dehaze"), -100, 100, Value.Dehaze, 0, Signed, delegate(double v) { Value.Dehaze = v; }, null);
            d.S.ToolTip = L.T("올리면 뿌연 기운을 걷고, 내리면 안개를 더한다.", "Raise to cut through haze, lower to add fog.");
            Bind(GEffects, d, delegate { return Value.Dehaze; });

            p.Children.Add(Sub(L.T("글로우", "Glow")));
            Bind(GEffects, Slide(p, L.T("양", "Amount"), 0, 100, Value.Glow, 0, Plain, delegate(double v) { Value.Glow = v; }, null), delegate { return Value.Glow; });
            var gs = Choice(p, new[] { L.T("확산", "Diffuse"), L.T("블룸", "Bloom"), L.T("할레이션", "Halation") }, (int)Value.GlowStyle, delegate(int k) { Value.GlowStyle = (CameraRawGlowStyle)k; Touch(); });
            gs.Panel.ToolTip = L.T("확산은 부드럽고 넓게, 블룸은 더 좁게, 할레이션은 붉은 테두리.", "Diffuse is soft and wide, Bloom is tighter, Halation adds a red fringe.");
            refresh[GEffects].Add(delegate { gs.Set((int)Value.GlowStyle); });
            var glowDeps = new List<Row>();
            glowDeps.Add(Slide(p, L.T("범위", "Range"), -100, 100, Value.GlowRange, 0, Signed, delegate(double v) { Value.GlowRange = v; }, null));
            glowDeps.Add(Slide(p, L.T("퍼짐", "Spread"), -100, 100, Value.GlowSpread, 0, Signed, delegate(double v) { Value.GlowSpread = v; }, null));
            glowDeps.Add(Slide(p, L.T("따뜻함", "Warmth"), -100, 100, Value.GlowWarmth, 0, Signed, delegate(double v) { Value.GlowWarmth = v; }, TempTrack));
            Bind(GEffects, glowDeps[0], delegate { return Value.GlowRange; });
            Bind(GEffects, glowDeps[1], delegate { return Value.GlowSpread; });
            Bind(GEffects, glowDeps[2], delegate { return Value.GlowWarmth; });
            Action glowOn = delegate { foreach (var r in glowDeps) r.S.IsEnabled = Value.Glow > 0; };
            refresh[GEffects].Add(glowOn); glowOn();

            p.Children.Add(Sub(L.T("비네팅", "Vignette")));
            var va = Slide(p, L.T("양", "Amount"), -100, 100, Value.VignetteAmount, 0, Signed, delegate(double v) { Value.VignetteAmount = v; }, ToneTrack);
            va.S.ToolTip = L.T("가장자리를 어둡게(−) 또는 밝게(+). 가운데는 그대로.", "Darkens (−) or brightens (+) the edges. The center stays as is.");
            Bind(GEffects, va, delegate { return Value.VignetteAmount; });
            var vs = Choice(p, new[] { L.T("밝은 영역 우선", "Highlight Priority"), L.T("색상 우선", "Color Priority"), L.T("페인트 오버레이", "Paint Overlay") }, (int)Value.VignetteStyle,
                            delegate(int k) { Value.VignetteStyle = (CameraRawVignetteStyle)k; Touch(); });
            refresh[GEffects].Add(delegate { vs.Set((int)Value.VignetteStyle); });
            var vigDeps = new List<Row>();
            vigDeps.Add(Slide(p, L.T("중간점", "Midpoint"), 0, 100, Value.VignetteMidpoint, 50, Plain, delegate(double v) { Value.VignetteMidpoint = v; }, null));
            vigDeps.Add(Slide(p, L.T("원형률", "Roundness"), -100, 100, Value.VignetteRoundness, 0, Signed, delegate(double v) { Value.VignetteRoundness = v; }, null));
            vigDeps.Add(Slide(p, L.T("페더", "Feather"), 0, 100, Value.VignetteFeather, 50, Plain, delegate(double v) { Value.VignetteFeather = v; }, null));
            var vh = Slide(p, L.T("밝은 영역", "Highlights"), 0, 100, Value.VignetteHighlights, 0, Plain, delegate(double v) { Value.VignetteHighlights = v; }, null);
            vh.S.ToolTip = L.T("어둡게 하는 비네팅에서 밝은 픽셀을 지킨다. 밝은 영역 우선에서만 듣는다.", "Protects bright pixels in a darkening vignette. Only works with Highlight Priority.");
            Bind(GEffects, vigDeps[0], delegate { return Value.VignetteMidpoint; });
            Bind(GEffects, vigDeps[1], delegate { return Value.VignetteRoundness; });
            Bind(GEffects, vigDeps[2], delegate { return Value.VignetteFeather; });
            Bind(GEffects, vh, delegate { return Value.VignetteHighlights; });
            Action vigOn = delegate
            {
                foreach (var r in vigDeps) r.S.IsEnabled = Value.VignetteAmount != 0;
                vh.S.IsEnabled = Value.VignetteAmount < 0 && Value.VignetteStyle == CameraRawVignetteStyle.HighlightPriority;
            };
            refresh[GEffects].Add(vigOn); vigOn();

            p.Children.Add(Sub(L.T("그레인", "Grain")));
            Bind(GEffects, Slide(p, L.T("양", "Amount"), 0, 100, Value.GrainAmount, 0, Plain, delegate(double v) { Value.GrainAmount = v; }, null), delegate { return Value.GrainAmount; });
            var grDeps = new List<Row>();
            grDeps.Add(Slide(p, L.T("크기", "Size"), 0, 100, Value.GrainSize, 25, Plain, delegate(double v) { Value.GrainSize = v; }, null));
            grDeps.Add(Slide(p, L.T("거칠기", "Roughness"), 0, 100, Value.GrainRoughness, 50, Plain, delegate(double v) { Value.GrainRoughness = v; }, null));
            Bind(GEffects, grDeps[0], delegate { return Value.GrainSize; });
            Bind(GEffects, grDeps[1], delegate { return Value.GrainRoughness; });
            Action grOn = delegate { foreach (var r in grDeps) r.S.IsEnabled = Value.GrainAmount > 0; };
            refresh[GEffects].Add(grOn); grOn();
        }

        // ================= 커브 =================

        const double CW = 300, CH = 176;

        List<double[]> CurPoints
        {
            get
            {
                var c = Value.Curve;
                switch (curveChannel) { case 1: return c.Red; case 2: return c.Green; case 3: return c.Blue; default: return c.Rgb; }
            }
        }

        void SetCurPoints(List<double[]> pts)
        {
            var c = Value.Curve;
            switch (curveChannel) { case 1: c.Red = pts; break; case 2: c.Green = pts; break; case 3: c.Blue = pts; break; default: c.Rgb = pts; break; }
        }

        void BuildCurve(StackPanel p)
        {
            Choice(p, new[] { L.T("파라메트릭", "Parametric"), L.T("포인트", "Point") }, curvePage, delegate(int k) { curvePage = k; curveSelected = -1; Rebuild(GCurve); });
            if (curvePage == 1)
                Choice(p, new[] { "RGB", L.T("빨강", "Red"), L.T("녹색", "Green"), L.T("파랑", "Blue") }, curveChannel, delegate(int k) { curveChannel = k; curveSelected = -1; Rebuild(GCurve); });

            // ---- 그래프 ----
            var plot = new Canvas { Width = CW, Height = CH, Background = Theme.Alpha(Colors.Black, 0x55), ClipToBounds = true, Cursor = Cursors.Cross };
            for (int i = 1; i < 4; i++)
            {
                plot.Children.Add(new Line { X1 = CW * i / 4, X2 = CW * i / 4, Y1 = 0, Y2 = CH, Stroke = Theme.Alpha(Colors.White, 0x18), StrokeThickness = 1 });
                plot.Children.Add(new Line { Y1 = CH * i / 4, Y2 = CH * i / 4, X1 = 0, X2 = CW, Stroke = Theme.Alpha(Colors.White, 0x18), StrokeThickness = 1 });
            }
            plot.Children.Add(new Line { X1 = 0, Y1 = CH, X2 = CW, Y2 = 0, Stroke = Theme.Alpha(Colors.White, 0x40), StrokeThickness = 1 });
            var line = new Polyline { StrokeThickness = 1.6, Stroke = Brushes.White };
            plot.Children.Add(line);
            var marks = new Canvas();
            plot.Children.Add(marks);
            p.Children.Add(new Border
            {
                Child = plot, BorderBrush = Theme.BrBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3),
                HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 4),
                ToolTip = curvePage == 0
                    ? L.T("위아래로 끌면 그 구간 톤이 오르내린다. 아래 가장자리의 눈금을 끌면 구간 경계가 옮겨진다.", "Drag up or down to raise or lower that range. Drag the markers on the bottom edge to move range boundaries.")
                    : L.T("점을 끌어 옮긴다. 빈 곳을 누르면 점이 생기고, 두 번 누르거나 오른쪽 클릭하면 빠진다.", "Drag a point to move it. Click an empty spot to add one; double-click or right-click to remove it.")
            });
            var readout = new TextBlock { Foreground = Theme.BrAccent, FontFamily = Theme.Mono, FontSize = 11, Margin = new Thickness(1, 0, 0, 2) };

            Color[] chColor = { Colors.White, Color.FromRgb(0xF0, 0x5A, 0x5A), Color.FromRgb(0x4C, 0xD9, 0x7B), Color.FromRgb(0x5A, 0x9B, 0xF0) };
            Action draw = delegate
            {
                var pc = new PointCollection();
                marks.Children.Clear();
                var cur = Value.Curve;
                if (curvePage == 0)
                {
                    var anchors = cur.HasParametric ? cur.ParametricAnchors() : null;
                    for (int i = 0; i <= 96; i++)
                    {
                        double x = i / 96.0, y = anchors != null ? CameraRawCurve.Value(anchors, x) : x;
                        pc.Add(new Point(x * CW, (1 - y) * CH));
                    }
                    line.Stroke = Brushes.White;
                    foreach (double split in new[] { cur.ShadowSplit, cur.DarkSplit, cur.LightSplit })
                        marks.Children.Add(new Line { X1 = split / 100 * CW, X2 = split / 100 * CW, Y1 = CH - 9, Y2 = CH, Stroke = Brushes.White, StrokeThickness = 3 });
                    readout.Text = " ";
                }
                else
                {
                    var pts = CurPoints;
                    for (int i = 0; i <= 96; i++)
                    {
                        double x = i / 96.0;
                        pc.Add(new Point(x * CW, (1 - CameraRawCurve.Value(pts, x)) * CH));
                    }
                    line.Stroke = new SolidColorBrush(chColor[curveChannel]);
                    for (int i = 0; i < pts.Count; i++)
                    {
                        var dot = new Ellipse { Width = 9, Height = 9, Fill = i == curveSelected ? Theme.BrAccent : Brushes.White, Stroke = Brushes.Black, StrokeThickness = 1 };
                        Canvas.SetLeft(dot, pts[i][0] * CW - 4.5); Canvas.SetTop(dot, (1 - pts[i][1]) * CH - 4.5);
                        marks.Children.Add(dot);
                    }
                    readout.Text = curveSelected >= 0 && curveSelected < pts.Count
                        ? L.F("입력 {0}  →  출력 {1}", "Input {0}  →  Output {1}", (int)Math.Round(pts[curveSelected][0] * 255), (int)Math.Round(pts[curveSelected][1] * 255))
                        : " ";
                }
                line.Points = pc;
            };
            refresh[GCurve].Add(draw);

            // ---- 끌기: 누를 때 무엇을 끌지 정하고 뗄 때까지 그것만 ----
            int dragKind = 0, dragIndex = -1;          // 1 점, 2 경계, 3 구간
            double dragStart = 0, dragStartY = 0;
            plot.MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs e)
            {
                e.Handled = true;
                var m = e.GetPosition(plot);
                double x = m.X / CW, y = 1 - m.Y / CH;
                var cur = Value.Curve;
                dragKind = 0;
                if (curvePage == 0)
                {
                    double tone = x * 100;
                    double[] splits = { cur.ShadowSplit, cur.DarkSplit, cur.LightSplit };
                    if (m.Y > CH - 18)
                    {
                        int best = 0;
                        for (int i = 1; i < 3; i++) if (Math.Abs(splits[i] - tone) < Math.Abs(splits[best] - tone)) best = i;
                        dragKind = 2; dragIndex = best;
                    }
                    else
                    {
                        dragKind = 3;
                        dragIndex = tone < splits[0] ? 0 : tone < splits[1] ? 1 : tone < splits[2] ? 2 : 3;
                        dragStart = Region(dragIndex);
                        dragStartY = m.Y;
                    }
                }
                else
                {
                    var pts = CurPoints;
                    if (e.ClickCount == 2) { RemoveNear(x); return; }
                    int near = -1; double nd = 0.055;
                    for (int i = 0; i < pts.Count; i++)
                    {
                        double dd = Math.Sqrt((pts[i][0] - x) * (pts[i][0] - x) + (pts[i][1] - y) * (pts[i][1] - y));
                        if (dd < nd) { nd = dd; near = i; }
                    }
                    if (near < 0)
                    {
                        if (pts.Count >= 16 || x <= 0.01 || x >= 0.99) return;
                        foreach (var q in pts) if (Math.Abs(q[0] - x) <= 0.01) return;
                        var np = new[] { x, Math.Max(0, Math.Min(1, y)) };
                        pts.Add(np);
                        pts.Sort(delegate(double[] a, double[] b) { return a[0].CompareTo(b[0]); });
                        near = pts.IndexOf(np);
                        Touch();
                    }
                    curveSelected = near;
                    dragKind = 1; dragIndex = near;
                    draw();
                }
                plot.CaptureMouse();
            };
            plot.MouseMove += delegate(object o, MouseEventArgs e)
            {
                if (dragKind == 0 || !plot.IsMouseCaptured) return;
                var m = e.GetPosition(plot);
                double x = m.X / CW, y = 1 - m.Y / CH;
                var cur = Value.Curve;
                if (dragKind == 1)
                {
                    var pts = CurPoints;
                    int i = dragIndex;
                    if (i < 0 || i >= pts.Count) return;
                    pts[i][1] = Math.Max(0, Math.Min(1, y));
                    // 양 끝은 끝에 머물고, 나머지는 순서를 지킨다
                    if (i > 0 && i < pts.Count - 1) pts[i][0] = Math.Min(pts[i + 1][0] - 0.01, Math.Max(pts[i - 1][0] + 0.01, x));
                }
                else if (dragKind == 2)
                {
                    double v = Math.Min(98, Math.Max(2, x * 100));
                    // 경계는 어두운 영역 < 어두움 < 밝음 순서를 지킨다
                    if (dragIndex == 0) cur.ShadowSplit = Math.Max(5, Math.Min(v, cur.DarkSplit - 2));
                    else if (dragIndex == 1) cur.DarkSplit = Math.Min(cur.LightSplit - 2, Math.Max(cur.ShadowSplit + 2, v));
                    else cur.LightSplit = Math.Max(v, cur.DarkSplit + 2);
                }
                else
                {
                    double amount = Math.Round(Math.Min(100, Math.Max(-100, dragStart - (m.Y - dragStartY) / CH * 200)));
                    SetRegion(dragIndex, amount);
                }
                Touch();
            };
            plot.MouseLeftButtonUp += delegate { dragKind = 0; plot.ReleaseMouseCapture(); };
            plot.MouseRightButtonUp += delegate(object o, MouseButtonEventArgs e)
            {
                if (curvePage == 1) { RemoveNear(e.GetPosition(plot).X / CW); e.Handled = true; }
            };

            if (curvePage == 0)
            {
                Bind(GCurve, Slide(p, L.T("밝은 영역", "Highlights"), -100, 100, Value.Curve.Highlights, 0, Signed, delegate(double v) { Value.Curve.Highlights = v; }, null), delegate { return Value.Curve.Highlights; });
                Bind(GCurve, Slide(p, L.T("밝음", "Lights"), -100, 100, Value.Curve.Lights, 0, Signed, delegate(double v) { Value.Curve.Lights = v; }, null), delegate { return Value.Curve.Lights; });
                Bind(GCurve, Slide(p, L.T("어두움", "Darks"), -100, 100, Value.Curve.Darks, 0, Signed, delegate(double v) { Value.Curve.Darks = v; }, null), delegate { return Value.Curve.Darks; });
                Bind(GCurve, Slide(p, L.T("어두운 영역", "Shadows"), -100, 100, Value.Curve.Shadows, 0, Signed, delegate(double v) { Value.Curve.Shadows = v; }, null), delegate { return Value.Curve.Shadows; });
            }
            else
            {
                p.Children.Add(readout);
                var presets = new[] { CameraRawCurve.Linear(), CameraRawCurve.MediumContrast(), CameraRawCurve.StrongContrast() };
                Func<int> match = delegate
                {
                    for (int i = 0; i < 3; i++) if (CameraRawCurve.Same(CurPoints, presets[i])) return i;
                    return -1;
                };
                var pre = Choice(p, new[] { L.T("선형", "Linear"), L.T("중간 대비", "Medium Contrast"), L.T("강한 대비", "Strong Contrast") }, match(), delegate(int k)
                {
                    SetCurPoints(CameraRawCurve.Copy(presets[k]));
                    curveSelected = -1;
                    Touch();
                });
                pre.Panel.ToolTip = L.T("이 채널 커브를 곧은 선이나 대비 커브로 바꾼다.", "Sets this channel's curve to a straight line or a contrast curve.");
                refresh[GCurve].Add(delegate { pre.Set(match()); });
                if (curveChannel == 0)
                {
                    var rs = Slide(p, L.T("채도 다듬기", "Refine Saturation"), -100, 100, Value.Curve.RefineSaturation, 0, Signed, delegate(double v) { Value.Curve.RefineSaturation = v; }, ChromaTrack);
                    rs.S.ToolTip = L.T("커브가 색의 진하기도 얼마나 바꾸나. 0이면 포토샵과 같고, 낮추면 밝기만, 올리면 색을 더한다.", "How much the curve also changes color intensity. 0 matches Photoshop; lower changes brightness only, higher adds color.");
                    Bind(GCurve, rs, delegate { return Value.Curve.RefineSaturation; });
                }
            }
            draw();
        }

        double Region(int i)
        {
            var c = Value.Curve;
            return i == 0 ? c.Shadows : i == 1 ? c.Darks : i == 2 ? c.Lights : c.Highlights;
        }

        void SetRegion(int i, double v)
        {
            var c = Value.Curve;
            if (i == 0) c.Shadows = v; else if (i == 1) c.Darks = v; else if (i == 2) c.Lights = v; else c.Highlights = v;
        }

        void RemoveNear(double x)
        {
            var pts = CurPoints;
            int best = -1; double bd = 0.04;
            for (int i = 1; i < pts.Count - 1; i++)
                if (Math.Abs(pts[i][0] - x) < bd) { bd = Math.Abs(pts[i][0] - x); best = i; }
            if (best < 0) return;
            pts.RemoveAt(best);
            curveSelected = -1;
            Touch();
        }

        // ================= 색상 혼합 =================

        void BuildMixer(StackPanel p)
        {
            Choice(p, new[] { "HSL", L.T("색상", "Color") }, mixerPage, delegate(int k) { mixerPage = k; Rebuild(GMixer); });
            var m = Value.Mixer;
            if (mixerPage == 0)
            {
                Choice(p, new[] { L.T("색조", "Hue"), L.T("채도", "Saturation"), L.T("광도", "Luminance") }, mixerTab, delegate(int k) { mixerTab = k; Rebuild(GMixer); });
                for (int i = 0; i < 8; i++)
                {
                    int k = i;
                    double c = CameraRawMixer.Centers[i];
                    Brush strip = mixerTab == 0 ? HueTrack(c) : mixerTab == 1 ? SatTrack(c) : LumTrack(c);
                    var r = Slide(p, Families[i], -100, 100, Arr()[i], 0, Signed, delegate(double v) { Arr()[k] = v; }, strip);
                    Bind(GMixer, r, delegate { return Arr()[k]; });
                }
            }
            else
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
                for (int i = 0; i < 8; i++)
                {
                    int k = i;
                    var dot = new Ellipse
                    {
                        Width = 20, Height = 20, Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand,
                        Fill = new SolidColorBrush(Hsb(CameraRawMixer.Centers[i], 0.8, 0.9)),
                        Stroke = i == mixerSwatch ? Brushes.White : Brushes.Transparent, StrokeThickness = 2,
                        ToolTip = L.F("{0} 고치기", "Adjust {0}", Families[i])
                    };
                    dot.MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs a) { a.Handled = true; };
                    dot.MouseLeftButtonUp += delegate { mixerSwatch = k; Rebuild(GMixer); };
                    row.Children.Add(dot);
                }
                p.Children.Add(row);
                int s = mixerSwatch;
                double c = CameraRawMixer.Centers[s];
                Bind(GMixer, Slide(p, L.T("색조", "Hue"), -100, 100, m.Hue[s], 0, Signed, delegate(double v) { Value.Mixer.Hue[s] = v; }, HueTrack(c)), delegate { return Value.Mixer.Hue[s]; });
                Bind(GMixer, Slide(p, L.T("채도", "Saturation"), -100, 100, m.Saturation[s], 0, Signed, delegate(double v) { Value.Mixer.Saturation[s] = v; }, SatTrack(c)), delegate { return Value.Mixer.Saturation[s]; });
                Bind(GMixer, Slide(p, L.T("광도", "Luminance"), -100, 100, m.Luminance[s], 0, Signed, delegate(double v) { Value.Mixer.Luminance[s] = v; }, LumTrack(c)), delegate { return Value.Mixer.Luminance[s]; });
            }
            p.Children.Add(Note(L.T("이웃한 색 계열은 서로 겹쳐 함께 움직인다.", "Neighboring color ranges overlap and move together.")));
        }

        double[] Arr() { var m = Value.Mixer; return mixerTab == 0 ? m.Hue : mixerTab == 1 ? m.Saturation : m.Luminance; }

        // ================= 색 보정 =================

        static readonly string[] WheelNames = { L.T("어두운 영역", "Shadows"), L.T("중간톤", "Midtones"), L.T("밝은 영역", "Highlights"), L.T("전체", "Global") };

        CameraRawWheel WheelAt(int i)
        {
            var g = Value.Grading;
            return i == 0 ? g.Shadows : i == 1 ? g.Midtones : i == 2 ? g.Highlights : g.Global;
        }

        void BuildGrading(StackPanel p)
        {
            Choice(p, new[] { L.T("3방향", "3-Way"), L.T("어두운", "Shad."), L.T("중간", "Mid"), L.T("밝은", "High"), L.T("전체", "Global") }, gradePage, delegate(int k) { gradePage = k; Rebuild(GGrading); });
            if (gradePage == 0)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 2) };
                for (int i = 0; i < 3; i++) row.Children.Add(WheelColumn(i, 84, 100));
                p.Children.Add(row);
            }
            else
            {
                int i = gradePage - 1;
                var col = WheelColumn(i, 132, 300);
                col.HorizontalAlignment = HorizontalAlignment.Left;
                p.Children.Add(col);
            }
            var bl = Slide(p, L.T("혼합", "Blending"), 0, 100, Value.Grading.Blending, 50, Plain, delegate(double v) { Value.Grading.Blending = v; }, null);
            bl.S.ToolTip = L.T("세 바퀴가 얼마나 겹치나.", "How much the three wheels overlap.");
            Bind(GGrading, bl, delegate { return Value.Grading.Blending; });
            var ba = Slide(p, L.T("균형", "Balance"), -100, 100, Value.Grading.Balance, 0, Signed, delegate(double v) { Value.Grading.Balance = v; }, ToneTrack);
            ba.S.ToolTip = L.T("바퀴들을 어두운 쪽이나 밝은 쪽으로 기울인다.", "Tilts the wheels toward shadows or highlights.");
            Bind(GGrading, ba, delegate { return Value.Grading.Balance; });
        }

        // 바퀴 하나: 제목, 색 바퀴(각도=색조, 거리=채도), 읽음, 광도 슬라이더
        FrameworkElement WheelColumn(int index, double size, double width)
        {
            var col = new StackPanel { Width = width, Margin = new Thickness(0, 0, 8, 0) };
            col.Children.Add(new TextBlock { Text = WheelNames[index], Foreground = Theme.BrDim, FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 4) });

            var face = new Grid { Width = size, Height = size, Cursor = Cursors.Cross, Background = Brushes.Transparent, ToolTip = L.T("끌어서 색조·채도를 고른다. 두 번 누르면 이 바퀴를 비운다.", "Drag to pick hue and saturation. Double-click to clear this wheel.") };
            face.Children.Add(new Image { Source = WheelBitmap(), Width = size, Height = size, Opacity = 0.9 });
            face.Children.Add(new Ellipse { Width = size, Height = size, Stroke = Theme.Alpha(Colors.White, 0xCC), StrokeThickness = 1 });
            var layer = new Canvas { Width = size, Height = size };
            var dot = new Ellipse { Width = 10, Height = 10, Fill = Brushes.White, Stroke = Brushes.Black, StrokeThickness = 1 };
            layer.Children.Add(dot);
            face.Children.Add(layer);
            col.Children.Add(face);

            var read = new TextBlock { Foreground = Theme.BrAccent, FontFamily = Theme.Mono, FontSize = 10.5, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
            col.Children.Add(read);

            double radius = size / 2 - 5;
            Action place = delegate
            {
                var w = WheelAt(index);
                double ang = w.Hue * Math.PI / 180, dist = w.Saturation / 100 * radius;
                Canvas.SetLeft(dot, size / 2 + Math.Cos(ang) * dist - 5);
                Canvas.SetTop(dot, size / 2 - Math.Sin(ang) * dist - 5);
                read.Text = (int)Math.Round(w.Hue) + "°  " + (int)Math.Round(w.Saturation);
            };
            place();
            refresh[GGrading].Add(place);

            Action<Point> setFrom = delegate(Point m)
            {
                double dx = m.X - size / 2, dy = size / 2 - m.Y;
                double deg = Math.Atan2(dy, dx) * 180 / Math.PI;
                if (deg < 0) deg += 360;
                var w = WheelAt(index);
                w.Hue = Math.Round(deg);
                w.Saturation = Math.Round(Math.Min(100, Math.Sqrt(dx * dx + dy * dy) / radius * 100));
                Touch();
            };
            face.MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs e)
            {
                e.Handled = true;
                if (e.ClickCount == 2) { var w = WheelAt(index); w.Hue = 0; w.Saturation = 0; Touch(); return; }
                face.CaptureMouse();
                setFrom(e.GetPosition(face));
            };
            face.MouseMove += delegate(object o, MouseEventArgs e) { if (face.IsMouseCaptured) setFrom(e.GetPosition(face)); };
            face.MouseLeftButtonUp += delegate { face.ReleaseMouseCapture(); };

            // 광도: 바퀴 밑 작은 슬라이더(이름 칸 없이)
            var lum = new Slider
            {
                Minimum = -100, Maximum = 100, Value = WheelAt(index).Luminance, IsSnapToTickEnabled = true, TickFrequency = 1,
                Width = Math.Min(width, 120), Margin = new Thickness(0, 4, 0, 0), ToolTip = L.T("이 바퀴가 더하는 밝기(광도)", "Brightness (luminance) this wheel adds")
            };
            var lumRead = new TextBlock { Foreground = Theme.BrDim, FontFamily = Theme.Mono, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center };
            Action lumText = delegate { lumRead.Text = L.T("광도 ", "Luminance ") + Signed(lum.Value); };
            lumText();
            lum.ValueChanged += delegate
            {
                lumText();
                WheelAt(index).Luminance = lum.Value;
                Touch();
            };
            lum.MouseDoubleClick += delegate { lum.Value = 0; };
            refresh[GGrading].Add(delegate { lum.Value = WheelAt(index).Luminance; lumText(); });
            col.Children.Add(lum);
            col.Children.Add(lumRead);
            return col;
        }

        static BitmapSource wheelBmp;

        // 바깥으로 갈수록 진해지는 색상환. 오른쪽이 빨강, 반시계 방향으로 색조가 돈다(원본 바퀴와 같은 방향).
        static BitmapSource WheelBitmap()
        {
            if (wheelBmp != null) return wheelBmp;
            const int n = 264;
            var px = new byte[n * n * 4];
            double r0 = n / 2.0;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    double dx = x + 0.5 - r0, dy = r0 - (y + 0.5);
                    double d = Math.Sqrt(dx * dx + dy * dy) / (r0 - 1);
                    double cover = Math.Max(0, Math.Min(1, (1 - d) * (r0 - 1) + 0.5));
                    if (cover <= 0) continue;
                    double deg = Math.Atan2(dy, dx) * 180 / Math.PI;
                    double r, g, b;
                    Tone.ToRgb(((deg % 360) + 360) % 360, 1, 0.5, out r, out g, out b);
                    double t = Math.Pow(Math.Min(1, d), 0.8), gray = 0.5;
                    r = gray + (r - gray) * t; g = gray + (g - gray) * t; b = gray + (b - gray) * t;
                    int i = (y * n + x) * 4;
                    // Pbgra32: 알파를 곱해 둔다
                    px[i] = (byte)Math.Round(b * 255 * cover); px[i + 1] = (byte)Math.Round(g * 255 * cover);
                    px[i + 2] = (byte)Math.Round(r * 255 * cover); px[i + 3] = (byte)Math.Round(255 * cover);
                }
            var bmp = BitmapSource.Create(n, n, 96, 96, PixelFormats.Pbgra32, null, px, n * 4);
            bmp.Freeze();
            wheelBmp = bmp;
            return bmp;
        }

        // ================= 세부 =================

        void BuildDetail(StackPanel p)
        {
            var d = Value.Detail;
            p.Children.Add(Sub(L.T("선명하게", "Sharpening")));
            var amt = Slide(p, L.T("양", "Amount"), 0, 150, d.SharpenAmount, 0, Plain, delegate(double v) { Value.Detail.SharpenAmount = v; }, null);
            Bind(GDetail, amt, delegate { return Value.Detail.SharpenAmount; });
            var sharpDeps = new List<Row>();
            // 반경은 실제 픽셀로 보여 준다(0.5~3.0)
            sharpDeps.Add(Slide(p, L.T("반경", "Radius"), 0, 100, d.SharpenRadius, 10, delegate(double v) { return (0.5 + v / 100 * 2.5).ToString("0.0"); },
                                delegate(double v) { Value.Detail.SharpenRadius = v; }, null));
            sharpDeps.Add(Slide(p, L.T("세부", "Detail"), 0, 100, d.SharpenDetail, 25, Plain, delegate(double v) { Value.Detail.SharpenDetail = v; }, null));
            sharpDeps.Add(Slide(p, L.T("마스킹", "Masking"), 0, 100, d.SharpenMasking, 0, Plain, delegate(double v) { Value.Detail.SharpenMasking = v; }, null));
            sharpDeps[0].S.ToolTip = L.T("경계에서 얼마나 멀리까지 선명하게 하나(픽셀).", "How far from edges sharpening reaches (pixels).");
            sharpDeps[1].S.ToolTip = L.T("넓은 경계보다 잔결을 더 살린다.", "Favors fine texture over broad edges.");
            sharpDeps[2].S.ToolTip = L.T("센 경계에만 걸리게 한다. 평평한 하늘·피부가 거칠어지지 않게.", "Limits sharpening to strong edges so flat sky and skin stay smooth.");
            Bind(GDetail, sharpDeps[0], delegate { return Value.Detail.SharpenRadius; });
            Bind(GDetail, sharpDeps[1], delegate { return Value.Detail.SharpenDetail; });
            Bind(GDetail, sharpDeps[2], delegate { return Value.Detail.SharpenMasking; });

            p.Children.Add(Sub(L.T("노이즈 감소", "Noise Reduction")));
            var lumN = Slide(p, L.T("광도", "Luminance"), 0, 100, d.NoiseLuminance, 0, Plain, delegate(double v) { Value.Detail.NoiseLuminance = v; }, null);
            lumN.S.ToolTip = L.T("밝기의 자글자글한 잡음을 고르게 편다.", "Smooths out grainy brightness noise.");
            Bind(GDetail, lumN, delegate { return Value.Detail.NoiseLuminance; });
            var lumDeps = new List<Row>();
            lumDeps.Add(Slide(p, L.T("광도 세부", "Luminance Detail"), 0, 100, d.NoiseLuminanceDetail, 50, Plain, delegate(double v) { Value.Detail.NoiseLuminanceDetail = v; }, null));
            lumDeps.Add(Slide(p, L.T("광도 대비", "Luminance Contrast"), 0, 100, d.NoiseLuminanceContrast, 0, Plain, delegate(double v) { Value.Detail.NoiseLuminanceContrast = v; }, null));
            Bind(GDetail, lumDeps[0], delegate { return Value.Detail.NoiseLuminanceDetail; });
            Bind(GDetail, lumDeps[1], delegate { return Value.Detail.NoiseLuminanceContrast; });
            var colN = Slide(p, L.T("색상", "Color"), 0, 100, d.NoiseColor, 0, Plain, delegate(double v) { Value.Detail.NoiseColor = v; }, null);
            colN.S.ToolTip = L.T("알록달록한 색 얼룩을 지운다.", "Removes blotchy color speckles.");
            Bind(GDetail, colN, delegate { return Value.Detail.NoiseColor; });
            var colDeps = new List<Row>();
            colDeps.Add(Slide(p, L.T("색상 세부", "Color Detail"), 0, 100, d.NoiseColorDetail, 50, Plain, delegate(double v) { Value.Detail.NoiseColorDetail = v; }, null));
            colDeps.Add(Slide(p, L.T("색상 매끄러움", "Color Smoothness"), 0, 100, d.NoiseColorSmoothness, 50, Plain, delegate(double v) { Value.Detail.NoiseColorSmoothness = v; }, null));
            Bind(GDetail, colDeps[0], delegate { return Value.Detail.NoiseColorDetail; });
            Bind(GDetail, colDeps[1], delegate { return Value.Detail.NoiseColorSmoothness; });

            Action enable = delegate
            {
                foreach (var r in sharpDeps) r.S.IsEnabled = Value.Detail.SharpenAmount > 0;
                foreach (var r in lumDeps) r.S.IsEnabled = Value.Detail.NoiseLuminance > 0;
                foreach (var r in colDeps) r.S.IsEnabled = Value.Detail.NoiseColor > 0;
            };
            refresh[GDetail].Add(enable);
            enable();
        }

        // ================= 광학 =================

        void BuildOptics(StackPanel p)
        {
            var o = Value.Optics;
            var ca = Toggle(L.T("색수차 제거", "Remove Chromatic Aberration"), o.RemoveChromaticAberration, delegate(bool on) { Value.Optics.RemoveChromaticAberration = on; After(); });
            ca.Margin = new Thickness(0, 4, 0, 0);
            ca.ToolTip = L.T("빨강·파랑 테두리를 가운데 쪽으로 맞붙여 색 번짐을 줄인다.", "Pulls red and blue fringes toward the center to reduce color fringing.");
            p.Children.Add(ca);
            var profile = new StackPanel { Visibility = o.EnableLensProfile ? Visibility.Visible : Visibility.Collapsed };
            var pr = Toggle(L.T("렌즈 프로필 보정 사용", "Enable Profile Corrections"), o.EnableLensProfile, delegate(bool on)
            {
                Value.Optics.EnableLensProfile = on;
                profile.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                After();
            });
            pr.Margin = new Thickness(0, 8, 0, 0);
            p.Children.Add(pr);
            profile.Children.Add(Note(L.T("이 레이어엔 렌즈 정보가 없다. 아래 슬라이더는 일반적인 보정 세기만 정한다.", "This layer has no lens data. The sliders below only set a generic correction amount.")));
            Bind(GOptics, Slide(profile, L.T("왜곡", "Distortion"), 0, 100, o.ProfileDistortion, 100, Plain, delegate(double v) { Value.Optics.ProfileDistortion = v; }, null), delegate { return Value.Optics.ProfileDistortion; });
            Bind(GOptics, Slide(profile, L.T("비네팅", "Vignetting"), 0, 100, o.ProfileVignetting, 100, Plain, delegate(double v) { Value.Optics.ProfileVignetting = v; }, null), delegate { return Value.Optics.ProfileVignetting; });
            p.Children.Add(profile);

            p.Children.Add(Sub(L.T("수동", "Manual")));
            var dist = Slide(p, L.T("왜곡", "Distortion"), -100, 100, o.Distortion, 0, Signed, delegate(double v) { Value.Optics.Distortion = v; }, null);
            dist.S.ToolTip = L.T("술통형·실패형으로 휜 선을 편다.", "Straightens lines bowed by barrel or pincushion distortion.");
            Bind(GOptics, dist, delegate { return Value.Optics.Distortion; });

            p.Children.Add(Sub(L.T("프린지 제거", "Defringe")));
            var spec = Spectrum();
            Func<double, string> deg = delegate(double v) { return Plain(v) + "°"; };
            Bind(GOptics, Slide(p, L.T("자주 양", "Purple Amount"), 0, 100, o.PurpleAmount, 0, Plain, delegate(double v) { Value.Optics.PurpleAmount = v; }, null), delegate { return Value.Optics.PurpleAmount; });
            Bind(GOptics, Slide(p, L.T("자주 색조 시작", "Purple Hue Start"), 0, 360, o.PurpleHueLow, 270, deg, delegate(double v) { Value.Optics.PurpleHueLow = v; }, spec), delegate { return Value.Optics.PurpleHueLow; });
            Bind(GOptics, Slide(p, L.T("자주 색조 끝", "Purple Hue End"), 0, 360, o.PurpleHueHigh, 310, deg, delegate(double v) { Value.Optics.PurpleHueHigh = v; }, spec), delegate { return Value.Optics.PurpleHueHigh; });
            Bind(GOptics, Slide(p, L.T("녹색 양", "Green Amount"), 0, 100, o.GreenAmount, 0, Plain, delegate(double v) { Value.Optics.GreenAmount = v; }, null), delegate { return Value.Optics.GreenAmount; });
            Bind(GOptics, Slide(p, L.T("녹색 색조 시작", "Green Hue Start"), 0, 360, o.GreenHueLow, 60, deg, delegate(double v) { Value.Optics.GreenHueLow = v; }, spec), delegate { return Value.Optics.GreenHueLow; });
            Bind(GOptics, Slide(p, L.T("녹색 색조 끝", "Green Hue End"), 0, 360, o.GreenHueHigh, 120, deg, delegate(double v) { Value.Optics.GreenHueHigh = v; }, spec), delegate { return Value.Optics.GreenHueHigh; });

            p.Children.Add(Sub(L.T("렌즈 비네팅", "Lens Vignetting")));
            var va = Slide(p, L.T("양", "Amount"), -100, 100, o.VignetteAmount, 0, Signed, delegate(double v) { Value.Optics.VignetteAmount = v; }, ToneTrack);
            va.S.ToolTip = L.T("렌즈 때문에 어두워진 모서리를 밝히거나 더 어둡게 한다.", "Brightens corners darkened by the lens, or darkens them further.");
            Bind(GOptics, va, delegate { return Value.Optics.VignetteAmount; });
            Bind(GOptics, Slide(p, L.T("중간점", "Midpoint"), 0, 100, o.VignetteMidpoint, 50, Plain, delegate(double v) { Value.Optics.VignetteMidpoint = v; }, null), delegate { return Value.Optics.VignetteMidpoint; });
        }

        // ================= 기하 =================

        void BuildGeometry(StackPanel p)
        {
            var geo = Value.Geometry;
            var pj = Choice(p, new[] { L.T("원근", "Perspective"), L.T("직선", "Rectilinear") }, (int)geo.Projection, delegate(int k) { Value.Geometry.Projection = (CameraRawProjection)k; Touch(); });
            pj.Panel.ToolTip = L.T("원근은 키스톤을 세게, 직선은 부드럽게 비튼다.", "Perspective corrects keystoning strongly, Rectilinear more gently.");
            refresh[GGeometry].Add(delegate { pj.Set((int)Value.Geometry.Projection); });
            Bind(GGeometry, Slide(p, L.T("세로", "Vertical"), -100, 100, geo.Vertical, 0, Signed, delegate(double v) { Value.Geometry.Vertical = v; }, null), delegate { return Value.Geometry.Vertical; });
            Bind(GGeometry, Slide(p, L.T("가로", "Horizontal"), -100, 100, geo.Horizontal, 0, Signed, delegate(double v) { Value.Geometry.Horizontal = v; }, null), delegate { return Value.Geometry.Horizontal; });
            Bind(GGeometry, Slide(p, L.T("회전", "Rotate"), -45, 45, geo.Rotate, 0, delegate(double v) { return Signed(v) + "°"; }, delegate(double v) { Value.Geometry.Rotate = v; }, null), delegate { return Value.Geometry.Rotate; });
            Bind(GGeometry, Slide(p, L.T("종횡비", "Aspect"), -100, 100, geo.Aspect, 0, Signed, delegate(double v) { Value.Geometry.Aspect = v; }, null), delegate { return Value.Geometry.Aspect; });
            Bind(GGeometry, Slide(p, L.T("비율", "Scale"), -100, 100, geo.Scale, 0, Signed, delegate(double v) { Value.Geometry.Scale = v; }, null), delegate { return Value.Geometry.Scale; });
            Bind(GGeometry, Slide(p, L.T("X 오프셋", "X Offset"), -100, 100, geo.OffsetX, 0, Signed, delegate(double v) { Value.Geometry.OffsetX = v; }, null), delegate { return Value.Geometry.OffsetX; });
            Bind(GGeometry, Slide(p, L.T("Y 오프셋", "Y Offset"), -100, 100, geo.OffsetY, 0, Signed, delegate(double v) { Value.Geometry.OffsetY = v; }, null), delegate { return Value.Geometry.OffsetY; });
            var crop = Toggle(L.T("자르기 제한", "Constrain Crop"), geo.ConstrainCrop, delegate(bool on) { Value.Geometry.ConstrainCrop = on; After(); });
            crop.ToolTip = L.T("비튼 뒤 생긴 빈 가장자리를 잘라 내고 판에 다시 맞춘다.", "Crops away empty edges left by the transform and fits the image back to the canvas.");
            p.Children.Add(crop);
        }

        // ================= 카메라 보정 =================

        void BuildCalibration(StackPanel p)
        {
            var c = Value.Calibration;
            var head = new TextBlock { Text = L.T("처리 버전", "Process Version"), Foreground = Theme.BrDim, FontSize = 12, Margin = new Thickness(0, 4, 0, 0) };
            p.Children.Add(head);
            var note = Note(CameraRawCalibration.Summaries[Math.Max(1, Math.Min(6, c.Process)) - 1]);
            var pv = Choice(p, new[] { "1", "2", "3", "4", "5", "6" }, c.Process - 1, delegate(int k)
            {
                Value.Calibration.Process = k + 1;
                Touch();
            });
            pv.Panel.Margin = new Thickness(0, 4, 0, 0);
            refresh[GCalib].Add(delegate
            {
                pv.Set(Value.Calibration.Process - 1);
                note.Text = CameraRawCalibration.Summaries[Math.Max(1, Math.Min(6, Value.Calibration.Process)) - 1];
            });
            p.Children.Add(note);

            p.Children.Add(Sub(L.T("어두운 영역", "Shadows")));
            Bind(GCalib, Slide(p, L.T("색조", "Tint"), -100, 100, c.ShadowTint, 0, Signed, delegate(double v) { Value.Calibration.ShadowTint = v; }, TintTrack), delegate { return Value.Calibration.ShadowTint; });
            string[] names = { L.T("빨강 원색", "Red Primary"), L.T("녹색 원색", "Green Primary"), L.T("파랑 원색", "Blue Primary") };
            double[] hues = { 0, 120, 240 };
            for (int i = 0; i < 3; i++)
            {
                int k = i;
                p.Children.Add(Sub(names[i]));
                var hr = Slide(p, L.T("색조", "Hue"), -100, 100, CalHue(k), 0, Signed, delegate(double v) { SetCalHue(k, v); }, HueTrack(hues[i]));
                var sr = Slide(p, L.T("채도", "Saturation"), -100, 100, CalSat(k), 0, Signed, delegate(double v) { SetCalSat(k, v); }, SatTrack(hues[i]));
                Bind(GCalib, hr, delegate { return CalHue(k); });
                Bind(GCalib, sr, delegate { return CalSat(k); });
            }
        }

        double CalHue(int i) { var c = Value.Calibration; return i == 0 ? c.RedHue : i == 1 ? c.GreenHue : c.BlueHue; }
        double CalSat(int i) { var c = Value.Calibration; return i == 0 ? c.RedSaturation : i == 1 ? c.GreenSaturation : c.BlueSaturation; }
        void SetCalHue(int i, double v) { var c = Value.Calibration; if (i == 0) c.RedHue = v; else if (i == 1) c.GreenHue = v; else c.BlueHue = v; }
        void SetCalSat(int i, double v) { var c = Value.Calibration; if (i == 0) c.RedSaturation = v; else if (i == 1) c.GreenSaturation = v; else c.BlueSaturation = v; }
    }
}
