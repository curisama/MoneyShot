// Money Shot — 필터 창
//
// 필터마다 슬라이더 몇 개뿐이라 창 하나가 종류에 따라 다르게 꾸려진다.
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MoneyShot
{
    public enum FilterKind { Gaussian, Motion, Noise, Vignette, Bloom, Tonal, Lens, Exposure, GradientMap, Grain }

    public class FilterSheet : ToneSheet
    {
        readonly FilterKind kind;
        readonly double[] v = new double[8];
        readonly double[] home = new double[8];
        readonly List<Row> rows = new List<Row>();
        readonly List<Action> resetExtra = new List<Action>();
        bool flagA, flagB;
        readonly FxColor colA = new FxColor(0, 0, 0), colB = new FxColor(255, 255, 255);
        uint seed = (uint)Environment.TickCount;

        public static string KindTitle(FilterKind k)
        {
            switch (k)
            {
                case FilterKind.Gaussian: return L.T("가우시안 흐림", "Gaussian Blur");
                case FilterKind.Motion: return L.T("동작 흐림", "Motion Blur");
                case FilterKind.Noise: return L.T("노이즈 추가", "Add Noise");
                case FilterKind.Vignette: return L.T("비네팅", "Vignette");
                case FilterKind.Bloom: return L.T("블룸 / 광채", "Bloom / Glow");
                case FilterKind.Tonal: return L.T("톤 대비", "Tonal Contrast");
                case FilterKind.Lens: return L.T("렌즈 왜곡 보정", "Lens Correction");
                case FilterKind.Exposure: return L.T("노출", "Exposure");
                case FilterKind.GradientMap: return L.T("그라디언트 맵", "Gradient Map");
                default: return L.T("그레인", "Grain");
            }
        }

        public FilterSheet(FilterKind k) : base(KindTitle(k), 330)
        {
            kind = k;
            Func<double, string> px = delegate(double x) { return Plain(x) + "px"; };
            Func<double, string> pct = delegate(double x) { return Plain(x) + "%"; };
            switch (k)
            {
                case FilterKind.Gaussian:
                    Add(L.T("반경", "Radius"), 0, 1, 100, 4, px);
                    Body.Children.Add(Note(L.T("고르게 흐린다. 사진이면 가장자리 색을 끌어다 써서 둘레가 비치지 않는다.", "Blurs evenly. On photos, edge colors are extended so the border doesn't show through.")));
                    break;
                case FilterKind.Motion:
                    Add(L.T("각도", "Angle"), 0, -90, 90, 0, delegate(double x) { return Plain(x) + "°"; });
                    Add(L.T("거리", "Distance"), 1, 1, 300, 20, px);
                    Body.Children.Add(Note(L.T("한 방향으로 끌리듯 번진다. 각도 0이 가로.", "Smears in one direction. Angle 0 is horizontal.")));
                    break;
                case FilterKind.Noise:
                    Add(L.T("양", "Amount"), 0, 1, 100, 10, pct);
                    Body.Children.Add(Toggle(L.T("가우시안 분포 (더 자글자글)", "Gaussian (grainier)"), false, delegate(bool on) { flagA = on; }));
                    Body.Children.Add(Toggle(L.T("흑백 노이즈 (밝기만)", "Monochrome (brightness only)"), false, delegate(bool on) { flagB = on; }));
                    break;
                case FilterKind.Vignette:
                    Add(L.T("세기", "Strength"), 0, 0, 100, 35, pct);
                    Add(L.T("시작 지점", "Midpoint"), 1, 0, 100, 50, pct);
                    Add(L.T("둥글기", "Roundness"), 2, -100, 100, 100, Signed);
                    Add(L.T("부드럽게", "Feather"), 3, 0, 100, 60, pct);
                    Add(L.T("밝은 곳 지키기", "Protect highlights"), 4, 0, 100, 25, pct);
                    ColorRow(L.T("색", "Color"), colA, "#000000");
                    break;
                case FilterKind.Bloom:
                    Add(L.T("세기", "Strength"), 0, 0, 100, 40, pct);
                    Add(L.T("번짐", "Spread"), 1, 1, 150, 24, px);
                    Body.Children.Add(Note(L.T("밝은 곳이 빛을 머금고 주변으로 번진다.", "Bright areas glow and bleed into their surroundings.")));
                    break;
                case FilterKind.Tonal:
                    Add(L.T("세기", "Strength"), 0, 0, 100, 50, pct);
                    Add(L.T("반경", "Radius"), 1, 1, 100, 16, px);
                    Add(L.T("어두운 쪽", "Shadows"), 2, -100, 100, 40, Signed);
                    Add(L.T("중간", "Midtones"), 3, -100, 100, 60, Signed);
                    Add(L.T("밝은 쪽", "Highlights"), 4, -100, 100, 30, Signed);
                    Body.Children.Add(Note(L.T("가까운 이웃과의 밝기 차를 키워 결을 살린다.", "Boosts brightness differences between nearby pixels to bring out texture.")));
                    break;
                case FilterKind.Lens:
                    Add(L.T("왜곡 없애기", "Remove distortion"), 0, -100, 100, 0, Signed);
                    Body.Children.Add(Note(L.T("+는 바깥으로 휜 선(술통형)을, -는 안으로 휜 선(실패형)을 편다.", "+ straightens outward-bowed lines (barrel), − straightens inward-bowed lines (pincushion).")));
                    break;
                case FilterKind.Exposure:
                    Add(L.T("노출", "Exposure"), 0, -500, 500, 0, delegate(double x) { return (x / 100).ToString("+0.00;-0.00;0.00") + L.T(" 스톱", " stops"); });
                    Add(L.T("오프셋", "Offset"), 1, -50, 50, 0, delegate(double x) { return (x / 100).ToString("+0.00;-0.00;0.00"); });
                    Add(L.T("감마", "Gamma"), 2, -100, 100, 0, delegate(double x) { return Math.Pow(10, x / 100).ToString("0.00"); });
                    Body.Children.Add(Note(L.T("카메라처럼 빛의 양으로 민다. 노출 +1은 빛 두 배.", "Works in amounts of light, like a camera. Exposure +1 doubles the light.")));
                    break;
                case FilterKind.GradientMap:
                    ColorRow(L.T("어두운 색", "Dark color"), colA, "#000000");
                    ColorRow(L.T("밝은 색", "Light color"), colB, "#FFFFFF");
                    Body.Children.Add(Toggle(L.T("뒤집기", "Reverse"), false, delegate(bool on) { flagA = on; }));
                    Body.Children.Add(Note(L.T("밝기에 따라 두 색 사이로 칠한다. 듀오톤 포스터 느낌.", "Maps brightness between two colors. A duotone poster look.")));
                    break;
                default:
                    Add(L.T("양", "Amount"), 0, 0, 100, 25, pct);
                    Add(L.T("크기", "Size"), 1, 5, 200, 15, delegate(double x) { return (x / 10).ToString("0.0") + "px"; });
                    Add(L.T("거칠기", "Roughness"), 2, 0, 100, 50, pct);
                    Body.Children.Add(Note(L.T("필름 입자. 중간 밝기에서 가장 잘 보인다.", "Film grain. Most visible in the midtones.")));
                    break;
            }
            Footer();
        }

        void Add(string label, int slot, double min, double max, double init, Func<double, string> fmt)
        {
            v[slot] = init; home[slot] = init;
            int k = slot;
            rows.Add(AddRow(Body, label, min, max, init, init, fmt, delegate(double x) { v[k] = x; }, null));
        }

        static readonly string[] Presets = { "#000000", "#FFFFFF", "#34D399", "#F43F5E", "#FBBF24", "#3B82F6", "#A855F7", "#1E293B" };

        void ColorRow(string label, FxColor target, string init)
        {
            var c0 = Theme.H(init);
            target.R = c0.R; target.G = c0.G; target.B = c0.B;
            Body.Children.Add(new TextBlock { Text = label, Foreground = Theme.BrText, FontSize = 12.5, Margin = new Thickness(0, 10, 0, 4) });
            var row = new WrapPanel();
            var chips = new List<Border>();
            Action paint = delegate
            {
                foreach (var c in chips)
                {
                    var col = ((SolidColorBrush)c.Background).Color;
                    bool on = col.R == target.R && col.G == target.G && col.B == target.B;
                    c.BorderBrush = on ? Theme.BrAccent : Theme.Alpha(Colors.White, 0x30);
                    c.BorderThickness = new Thickness(on ? 2 : 1);
                }
            };
            foreach (var h in Presets)
            {
                var col = Theme.H(h);
                var chip = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 0, 5, 0), Background = new SolidColorBrush(col), Cursor = Cursors.Hand };
                chip.MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs a) { a.Handled = true; };
                chip.MouseLeftButtonUp += delegate { target.R = col.R; target.G = col.G; target.B = col.B; paint(); Fire(); };
                chips.Add(chip);
                row.Children.Add(chip);
            }
            Body.Children.Add(row);
            resetExtra.Add(delegate { target.R = c0.R; target.G = c0.G; target.B = c0.B; paint(); });
            paint();
        }

        public override bool IsIdentity
        {
            get
            {
                switch (kind)
                {
                    case FilterKind.Lens: return v[0] == 0;
                    case FilterKind.Exposure: return v[0] == 0 && v[1] == 0 && v[2] == 0;
                    case FilterKind.Vignette: case FilterKind.Bloom: case FilterKind.Grain: case FilterKind.Tonal: return v[0] <= 0;
                    default: return false;
                }
            }
        }

        public override ToneFn Current()
        {
            switch (kind)
            {
                case FilterKind.Gaussian: return Filters.Gaussian(v[0]);
                case FilterKind.Motion: return Filters.Motion(v[0], v[1]);
                case FilterKind.Noise: return Filters.Noise(v[0], flagA, flagB, seed);
                case FilterKind.Vignette: return Filters.Vignette(v[0], v[1], v[2], v[3], v[4], colA.Clone());
                case FilterKind.Bloom: return Filters.Bloom(v[0], v[1]);
                case FilterKind.Tonal: return Filters.TonalContrast(v[0], v[1], v[2], v[3], v[4]);
                case FilterKind.Lens: return Filters.Lens(v[0]);
                case FilterKind.Exposure: return Filters.Exposure(v[0] / 100, v[1] / 100, Math.Pow(10, v[2] / 100));
                case FilterKind.GradientMap: return Filters.GradientMap(colA.Clone(), colB.Clone(), flagA);
                default: return Filters.Grain(v[0], v[1] / 10, v[2], seed);
            }
        }

        protected override void ResetAll()
        {
            foreach (var a in resetExtra) a();
            // 슬롯 순서대로 만들었으니 같은 순서로 되돌린다 (슬라이더가 값도 같이 돌려놓는다)
            int r = 0;
            for (int s = 0; s < v.Length && r < rows.Count; s++)
                if (SlotUsed(s)) rows[r++].Quietly(home[s], this);
        }

        bool SlotUsed(int s)
        {
            switch (kind)
            {
                case FilterKind.Gaussian: case FilterKind.Noise: case FilterKind.Lens: return s == 0;
                case FilterKind.Motion: case FilterKind.Bloom: return s <= 1;
                case FilterKind.Exposure: case FilterKind.Grain: return s <= 2;
                case FilterKind.Vignette: case FilterKind.Tonal: return s <= 4;
                default: return false;
            }
        }
    }
}
