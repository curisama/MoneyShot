// Money Shot — Camera Raw 필터 (계산)
//
// 창은 CameraRawSheet.cs. 여기는 "설정 + 원본 → 결과"만 한다.
//
// Compositor(MIT, Wonder Assembly LLC)의 CameraRaw*.swift·AdjustPixels.c를 옮겼다
// (렌즈 왜곡은 LensPixels.c, 자르기 제한은 BrushPixels.c의 brush_alpha_bounds).
// 원본과 달라진 점:
//   · 원본 C는 알파를 곱한 RGBA를 풀었다 묶지만 Canvas32는 곱하지 않은 ARGB라 그 단계가 없다. 알파는 그대로.
//   · 원본은 단계마다 8비트로 다시 굳힌다. 여기는 단계 사이를 float로 들고 가서 마지막에 한 번만 굳힌다.
//     계단이 덜 생길 뿐 셈은 같다.
//   · 기하(원근 보정)의 CIPerspectiveTransform → 사각형↔사각형 호모그래피 역매핑 + 겹선형 보간.
//   · 노이즈 감소(광도)에서 원본은 고친 밝기를 같은 판에 바로 덮어써 다음 픽셀의 경계 판정이
//     훑는 순서에 따라 조금씩 달라진다. 여기는 띠로 나눠 동시에 돌리므로 원래 밝기로만 판정한다.
// 원본의 블러는 Core Image가 아니라 C의 상자 블러라 그대로 옮겼다.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MoneyShot
{
    // 순서가 곧 원본 C의 번호다. 바꾸지 말 것.
    public enum CameraRawGlowStyle { Diffusion, Bloom, Halation }
    public enum CameraRawVignetteStyle { HighlightPriority, ColorPriority, PaintOverlay }
    public enum CameraRawProjection { Perspective, Rectilinear }

    // ---------- 커브 ----------

    // 파라메트릭 네 구간과 포인트 커브. 양은 −100~100, 점은 두 축 다 0~1.
    public class CameraRawCurve
    {
        public double Shadows, Darks, Lights, Highlights;
        // 구간 경계(0~100). 순서를 지킨다.
        public double ShadowSplit = 25, DarkSplit = 50, LightSplit = 75;
        public List<double[]> Rgb = Linear(), Red = Linear(), Green = Linear(), Blue = Linear();
        // 합성 커브가 채도도 얼마나 같이 바꾸나. 0이면 포토샵과 같다.
        public double RefineSaturation;

        public static List<double[]> Linear() { return Pts(0, 0, 1, 1); }
        public static List<double[]> MediumContrast() { return Pts(0, 0, 0.25, 0.18, 0.75, 0.82, 1, 1); }
        public static List<double[]> StrongContrast() { return Pts(0, 0, 0.25, 0.10, 0.75, 0.90, 1, 1); }

        static List<double[]> Pts(params double[] xy)
        {
            var l = new List<double[]>();
            for (int i = 0; i + 1 < xy.Length; i += 2) l.Add(new[] { xy[i], xy[i + 1] });
            return l;
        }

        public static List<double[]> Copy(List<double[]> p)
        {
            var l = new List<double[]>();
            foreach (var q in p) l.Add(new[] { q[0], q[1] });
            return l;
        }

        public CameraRawCurve Clone()
        {
            var c = (CameraRawCurve)MemberwiseClone();
            c.Rgb = Copy(Rgb); c.Red = Copy(Red); c.Green = Copy(Green); c.Blue = Copy(Blue);
            return c;
        }

        public static bool IsLinear(List<double[]> p)
        {
            return p.Count == 2 && p[0][0] == 0 && p[0][1] == 0 && p[1][0] == 1 && p[1][1] == 1;
        }

        public static bool Same(List<double[]> a, List<double[]> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (a[i][0] != b[i][0] || a[i][1] != b[i][1]) return false;
            return true;
        }

        public bool Adjusts
        {
            get
            {
                return Shadows != 0 || Darks != 0 || Lights != 0 || Highlights != 0 || RefineSaturation != 0
                    || !IsLinear(Rgb) || !IsLinear(Red) || !IsLinear(Green) || !IsLinear(Blue);
            }
        }

        public CameraRawCurve Normalized()
        {
            var r = Clone();
            r.Shadows = CameraRaw.Clamp(Shadows, -100, 100, 0);
            r.Darks = CameraRaw.Clamp(Darks, -100, 100, 0);
            r.Lights = CameraRaw.Clamp(Lights, -100, 100, 0);
            r.Highlights = CameraRaw.Clamp(Highlights, -100, 100, 0);
            r.RefineSaturation = CameraRaw.Clamp(RefineSaturation, -100, 100, 0);
            r.ShadowSplit = CameraRaw.Clamp(ShadowSplit, 5, 90, 25);
            r.DarkSplit = CameraRaw.Clamp(DarkSplit, r.ShadowSplit + 2, 95, 50);
            r.LightSplit = CameraRaw.Clamp(LightSplit, r.DarkSplit + 2, 98, 75);
            r.Rgb = Repair(Rgb); r.Red = Repair(Red); r.Green = Repair(Green); r.Blue = Repair(Blue);
            return r;
        }

        // 순서대로 세우고, 양 끝은 0과 1에 붙이고, 너무 붙은 점은 버린다.
        static List<double[]> Repair(List<double[]> points)
        {
            var sorted = new List<double[]>();
            foreach (var p in points)
                if (!double.IsNaN(p[0]) && !double.IsInfinity(p[0]) && !double.IsNaN(p[1]) && !double.IsInfinity(p[1]))
                    sorted.Add(new[] { p[0], p[1] });
            if (sorted.Count < 2) return Linear();
            sorted.Sort(delegate(double[] a, double[] b) { return a[0].CompareTo(b[0]); });
            int n = sorted.Count;
            var kept = new List<double[]> { new[] { 0.0, Clamp01(sorted[0][1]) } };
            for (int i = 1; i < n - 1; i++)
            {
                double x = Math.Min(0.99, Math.Max(0.01, sorted[i][0]));
                if (!(x > kept[kept.Count - 1][0] + 0.01)) continue;
                kept.Add(new[] { x, Clamp01(sorted[i][1]) });
            }
            kept.Add(new[] { 1.0, Clamp01(sorted[n - 1][1]) });
            return kept;
        }

        static double Clamp01(double v) { return v < 0 ? 0 : v > 1 ? 1 : v; }

        // 모양을 지키는 3차 에르미트 보간(Image › Curves와 같은 것). 점 사이에서 넘쳐 출렁이지 않는다.
        public static double Value(List<double[]> p, double x)
        {
            int n = p.Count;
            if (n < 2) return x;
            int i = 0;
            for (int k = 0; k < n; k++) if (p[k][0] <= x) i = k;
            i = Math.Min(n - 2, Math.Max(0, i));
            Func<int, double> d = delegate(int j) { return (p[j + 1][1] - p[j][1]) / (p[j + 1][0] - p[j][0]); };
            Func<int, double> slope = delegate(int j)
            {
                if (j == 0) return d(0);
                if (j == n - 1) return d(n - 2);
                double a = d(j - 1), b = d(j);
                if (a * b <= 0) return 0;
                return 2 / (1 / a + 1 / b);
            };
            double h = p[i + 1][0] - p[i][0], t = Clamp01((x - p[i][0]) / h);
            double y = (2 * t * t * t - 3 * t * t + 1) * p[i][1] + (t * t * t - 2 * t * t + t) * h * slope(i)
                     + (-2 * t * t * t + 3 * t * t) * p[i + 1][1] + (t * t * t - t * t) * h * slope(i + 1);
            return Clamp01(y);
        }

        // 아래 구간은 low만큼, 위 구간은 high만큼 감마로 굽힌다. 검정·흰색·경계는 제자리.
        // 포토샵에 맞췄다: 어두움 −51이면 4분의 1 지점에서 0.1쯤 내려간다.
        static double Bend(double tone, double lower, double low, double upper, double high)
        {
            const double strength = 1.66;
            if (tone < lower && lower > 0) return lower * Math.Pow(tone / lower, Math.Pow(2, -low / 100 * strength));
            if (tone > upper && upper < 1)
            {
                double rest = 1 - upper;
                return 1 - rest * Math.Pow((1 - tone) / rest, Math.Pow(2, high / 100 * strength));
            }
            return tone;
        }

        // 파라메트릭: 어두움·밝음은 가운데 경계 아래·위 전체를, 어두운 영역·밝은 영역은 바깥 경계 너머만 굽힌다.
        // 33개 기준점을 에르미트로 이어 두 반쪽이 모서리 없이 만나게 한다.
        public List<double[]> ParametricAnchors()
        {
            var a = new List<double[]>(33);
            for (int i = 0; i <= 32; i++)
            {
                double x = i / 32.0;
                double y = Bend(Bend(x, ShadowSplit / 100, Shadows, LightSplit / 100, Highlights), DarkSplit / 100, Darks, DarkSplit / 100, Lights);
                a.Add(new[] { x, y });
            }
            return a;
        }

        public bool HasParametric { get { return Shadows != 0 || Darks != 0 || Lights != 0 || Highlights != 0; } }

        public double Parametric(double tone)
        {
            if (!HasParametric) return tone;
            return Value(ParametricAnchors(), tone);
        }

        public float[] ToneTable()
        {
            var t = new float[256];
            var anchors = HasParametric ? ParametricAnchors() : null;
            for (int v = 0; v < 256; v++)
            {
                double x = v / 255.0;
                if (anchors != null) x = Value(anchors, x);
                t[v] = (float)Value(Rgb, x);
            }
            return t;
        }

        public static float[] ChannelTable(List<double[]> points)
        {
            var t = new float[256];
            for (int v = 0; v < 256; v++) t[v] = (float)Value(points, v / 255.0);
            return t;
        }
    }

    // ---------- 혼합(HSL) ----------

    // 점 색: 고른 색 하나와, 어디까지 미치나
    public class CameraRawPointColor
    {
        public double Hue, Saturation, Luminance;            // 0~360, 0~1, 0~1
        public double HueShift, SaturationShift, LuminanceShift;
        public double HueRange = 30, SaturationRange = 0.4, LuminanceRange = 0.4;
        public CameraRawPointColor Clone() { return (CameraRawPointColor)MemberwiseClone(); }
        public CameraRawPointColor Normalized()
        {
            var r = Clone();
            r.Hue = CameraRaw.Clamp(Hue, 0, 360, 0);
            r.Saturation = CameraRaw.Clamp(Saturation, 0, 1, 0);
            r.Luminance = CameraRaw.Clamp(Luminance, 0, 1, 0);
            r.HueShift = CameraRaw.Clamp(HueShift, -100, 100, 0);
            r.SaturationShift = CameraRaw.Clamp(SaturationShift, -100, 100, 0);
            r.LuminanceShift = CameraRaw.Clamp(LuminanceShift, -100, 100, 0);
            r.HueRange = CameraRaw.Clamp(HueRange, 5, 180, 30);
            r.SaturationRange = CameraRaw.Clamp(SaturationRange, 0.05, 1, 0.4);
            r.LuminanceRange = CameraRaw.Clamp(LuminanceRange, 0.05, 1, 0.4);
            return r;
        }
    }

    // 여덟 색 계열마다 색조·채도·광도 이동(−100~100)
    public class CameraRawMixer
    {
        public static readonly double[] Centers = { 0, 30, 60, 120, 180, 240, 270, 300 };
        public double[] Hue = new double[8], Saturation = new double[8], Luminance = new double[8];
        public List<CameraRawPointColor> Points = new List<CameraRawPointColor>();

        public CameraRawMixer Clone()
        {
            var c = new CameraRawMixer();
            c.Hue = (double[])Hue.Clone(); c.Saturation = (double[])Saturation.Clone(); c.Luminance = (double[])Luminance.Clone();
            foreach (var p in Points) c.Points.Add(p.Clone());
            return c;
        }

        public bool Adjusts
        {
            get
            {
                for (int i = 0; i < 8; i++) if (Hue[i] != 0 || Saturation[i] != 0 || Luminance[i] != 0) return true;
                foreach (var p in Points) if (p.HueShift != 0 || p.SaturationShift != 0 || p.LuminanceShift != 0) return true;
                return false;
            }
        }

        public CameraRawMixer Normalized()
        {
            var r = new CameraRawMixer();
            for (int i = 0; i < 8; i++)
            {
                r.Hue[i] = CameraRaw.Clamp(Hue[i], -100, 100, 0);
                r.Saturation[i] = CameraRaw.Clamp(Saturation[i], -100, 100, 0);
                r.Luminance[i] = CameraRaw.Clamp(Luminance[i], -100, 100, 0);
            }
            for (int i = 0; i < Points.Count && i < 8; i++) r.Points.Add(Points[i].Normalized());
            return r;
        }
    }

    // ---------- 색 보정(color grading) ----------

    public class CameraRawWheel
    {
        public double Hue, Saturation, Luminance;    // 0~360, 0~100, −100~100
        public CameraRawWheel Clone() { return (CameraRawWheel)MemberwiseClone(); }
        public CameraRawWheel Normalized()
        {
            return new CameraRawWheel
            {
                Hue = CameraRaw.Clamp(Hue, 0, 360, 0),
                Saturation = CameraRaw.Clamp(Saturation, 0, 100, 0),
                Luminance = CameraRaw.Clamp(Luminance, -100, 100, 0)
            };
        }
    }

    public class CameraRawGrading
    {
        public CameraRawWheel Shadows = new CameraRawWheel(), Midtones = new CameraRawWheel(),
                              Highlights = new CameraRawWheel(), Global = new CameraRawWheel();
        // 0~100. 높을수록 세 바퀴가 많이 겹친다
        public double Blending = 50;
        // −100~100. 음수면 어두운 쪽, 양수면 밝은 쪽
        public double Balance;

        public CameraRawWheel[] Wheels { get { return new[] { Shadows, Midtones, Highlights, Global }; } }

        public CameraRawGrading Clone()
        {
            var c = (CameraRawGrading)MemberwiseClone();
            c.Shadows = Shadows.Clone(); c.Midtones = Midtones.Clone(); c.Highlights = Highlights.Clone(); c.Global = Global.Clone();
            return c;
        }

        public bool Adjusts
        {
            get { foreach (var w in Wheels) if (w.Saturation != 0 || w.Luminance != 0) return true; return false; }
        }

        public CameraRawGrading Normalized()
        {
            var r = Clone();
            r.Shadows = Shadows.Normalized(); r.Midtones = Midtones.Normalized();
            r.Highlights = Highlights.Normalized(); r.Global = Global.Normalized();
            r.Blending = CameraRaw.Clamp(Blending, 0, 100, 50);
            r.Balance = CameraRaw.Clamp(Balance, -100, 100, 0);
            return r;
        }
    }

    // ---------- 세부(선명하게·노이즈 감소) ----------

    public class CameraRawDetail
    {
        public double SharpenAmount, SharpenRadius = 10, SharpenDetail = 25, SharpenMasking;
        public double NoiseLuminance, NoiseLuminanceDetail = 50, NoiseLuminanceContrast;
        public double NoiseColor, NoiseColorDetail = 50, NoiseColorSmoothness = 50;

        public CameraRawDetail Clone() { return (CameraRawDetail)MemberwiseClone(); }
        public bool Adjusts { get { return SharpenAmount != 0 || NoiseLuminance != 0 || NoiseColor != 0; } }

        public CameraRawDetail Normalized()
        {
            var r = Clone();
            r.SharpenAmount = CameraRaw.Clamp(SharpenAmount, 0, 150, 0);
            r.SharpenRadius = CameraRaw.Clamp(SharpenRadius, 0, 100, 10);
            r.SharpenDetail = CameraRaw.Clamp(SharpenDetail, 0, 100, 25);
            r.SharpenMasking = CameraRaw.Clamp(SharpenMasking, 0, 100, 0);
            r.NoiseLuminance = CameraRaw.Clamp(NoiseLuminance, 0, 100, 0);
            r.NoiseLuminanceDetail = CameraRaw.Clamp(NoiseLuminanceDetail, 0, 100, 50);
            r.NoiseLuminanceContrast = CameraRaw.Clamp(NoiseLuminanceContrast, 0, 100, 0);
            r.NoiseColor = CameraRaw.Clamp(NoiseColor, 0, 100, 0);
            r.NoiseColorDetail = CameraRaw.Clamp(NoiseColorDetail, 0, 100, 50);
            r.NoiseColorSmoothness = CameraRaw.Clamp(NoiseColorSmoothness, 0, 100, 50);
            return r;
        }
    }

    // ---------- 광학 ----------

    // 렌더된 레이어엔 렌즈 정보가 없으니 프로필 슬라이더는 일반적인 보정 세기만 정한다.
    public class CameraRawOptics
    {
        public bool RemoveChromaticAberration, EnableLensProfile;
        public double ProfileDistortion = 100, ProfileVignetting = 100;
        public double Distortion;                                       // −100~100 (렌즈 교정 필터와 같은 부호)
        public double PurpleAmount, PurpleHueLow = 270, PurpleHueHigh = 310;
        public double GreenAmount, GreenHueLow = 60, GreenHueHigh = 120;
        public double VignetteAmount, VignetteMidpoint = 50;

        // 렌즈 교정 필터와 같은 세기 환산
        public const double LensStrength = 0.35;

        public CameraRawOptics Clone() { return (CameraRawOptics)MemberwiseClone(); }

        public bool Adjusts
        {
            get
            {
                return RemoveChromaticAberration || EnableLensProfile || Distortion != 0 || PurpleAmount != 0
                    || GreenAmount != 0 || VignetteAmount != 0;
            }
        }

        public CameraRawOptics Normalized()
        {
            var r = Clone();
            r.ProfileDistortion = CameraRaw.Clamp(ProfileDistortion, 0, 100, 100);
            r.ProfileVignetting = CameraRaw.Clamp(ProfileVignetting, 0, 100, 100);
            r.Distortion = CameraRaw.Clamp(Distortion, -100, 100, 0);
            r.PurpleAmount = CameraRaw.Clamp(PurpleAmount, 0, 100, 0);
            r.GreenAmount = CameraRaw.Clamp(GreenAmount, 0, 100, 0);
            r.VignetteAmount = CameraRaw.Clamp(VignetteAmount, -100, 100, 0);
            r.VignetteMidpoint = CameraRaw.Clamp(VignetteMidpoint, 0, 100, 50);
            r.PurpleHueLow = CameraRaw.Clamp(PurpleHueLow, 0, 360, 270);
            r.PurpleHueHigh = CameraRaw.Clamp(PurpleHueHigh, 0, 360, 310);
            r.GreenHueLow = CameraRaw.Clamp(GreenHueLow, 0, 360, 60);
            r.GreenHueHigh = CameraRaw.Clamp(GreenHueHigh, 0, 360, 120);
            if (r.PurpleHueLow > r.PurpleHueHigh) { double t = r.PurpleHueLow; r.PurpleHueLow = r.PurpleHueHigh; r.PurpleHueHigh = t; }
            if (r.GreenHueLow > r.GreenHueHigh) { double t = r.GreenHueLow; r.GreenHueLow = r.GreenHueHigh; r.GreenHueHigh = t; }
            return r;
        }

        public double DistortionK()
        {
            double manual = Distortion / 100 * LensStrength;
            double profile = EnableLensProfile ? ProfileDistortion / 100 * LensStrength : 0;
            return manual + profile;
        }
    }

    // ---------- 기하 ----------

    public class CameraRawGeometry
    {
        public CameraRawProjection Projection = CameraRawProjection.Perspective;
        public double Vertical, Horizontal, Rotate, Aspect, Scale, OffsetX, OffsetY;   // 회전만 −45~45, 나머지 −100~100
        public bool ConstrainCrop;

        public CameraRawGeometry Clone() { return (CameraRawGeometry)MemberwiseClone(); }

        public bool Adjusts
        {
            get { return Vertical != 0 || Horizontal != 0 || Rotate != 0 || Aspect != 0 || Scale != 0 || OffsetX != 0 || OffsetY != 0; }
        }

        public CameraRawGeometry Normalized()
        {
            var r = Clone();
            r.Vertical = CameraRaw.Clamp(Vertical, -100, 100, 0);
            r.Horizontal = CameraRaw.Clamp(Horizontal, -100, 100, 0);
            r.Rotate = CameraRaw.Clamp(Rotate, -45, 45, 0);
            r.Aspect = CameraRaw.Clamp(Aspect, -100, 100, 0);
            r.Scale = CameraRaw.Clamp(Scale, -100, 100, 0);
            r.OffsetX = CameraRaw.Clamp(OffsetX, -100, 100, 0);
            r.OffsetY = CameraRaw.Clamp(OffsetY, -100, 100, 0);
            return r;
        }

        // 결과 그림의 네 모서리. Core Image처럼 y는 아래에서 위로 잰다. 순서: 왼위, 오른위, 오른아래, 왼아래
        public double[][] OutputCorners(int width, int height)
        {
            double w = width, h = height;
            double strength = Projection == CameraRawProjection.Perspective ? 1.0 : 0.55;
            double v = Vertical / 100 * w * 0.18 * strength;
            double hz = Horizontal / 100 * h * 0.18 * strength;
            double aspectScale = 1 + Aspect / 200;
            double zoom = 1 + Scale / 100;
            double shiftX = OffsetX / 100 * w * 0.15;
            double shiftY = OffsetY / 100 * h * 0.15;
            var c = new[]
            {
                new[] { -v + shiftX, h + shiftY },
                new[] { w + v + shiftX, h + shiftY },
                new[] { w + hz + shiftX, -shiftY },
                new[] { -hz + shiftX, -shiftY },
            };
            double cx = w / 2 + shiftX, cy = h / 2 + shiftY;
            double rad = Rotate * Math.PI / 180, cos = Math.Cos(rad), sin = Math.Sin(rad);
            foreach (var p in c)
            {
                double dx = p[0] - cx, dy = p[1] - cy;
                p[0] = cx + dx * cos - dy * sin;
                p[1] = cy + dx * sin + dy * cos;
            }
            if (aspectScale != 1)
                foreach (var p in c) { p[0] = cx + (p[0] - cx) * aspectScale; p[1] = cy + (p[1] - cy) / aspectScale; }
            if (zoom != 1)
                foreach (var p in c) { p[0] = cx + (p[0] - cx) * zoom; p[1] = cy + (p[1] - cy) * zoom; }
            return c;
        }
    }

    // ---------- 보정(calibration) ----------

    public class CameraRawCalibration
    {
        public int Process = 6;     // 처리 버전 1~6
        public double ShadowTint, RedHue, RedSaturation, GreenHue, GreenSaturation, BlueHue, BlueSaturation;

        public static readonly string[] Summaries =
        {
            L.T("가장 이른 반응. 색조·채도·어두운 영역 색조가 버전 6의 절반쯤만 움직인다.", "Earliest response. Hue, saturation and shadow tint move only about half as far as in version 6."),
            L.T("버전 1보다 조금 세다. 그래도 지금 모습엔 한참 못 미친다.", "A bit stronger than version 1, but still well short of the current look."),
            L.T("버전 2보다 색이 단단하다. 원색 이동은 지금 처리보다 부드럽다.", "Firmer color than version 2. Primary shifts are softer than current processing."),
            L.T("2012년 반응. 버전 6 세기의 대부분까지 간다.", "The 2012 response. Reaches most of version 6's strength."),
            L.T("지금 처리에 가깝다. 원색·어두운 영역 이동이 살짝 부드럽다.", "Close to current processing. Primary and shadow shifts are slightly softer."),
            L.T("현재 기본값. 아래 슬라이더가 제 세기 그대로 든다.", "Current default. The sliders below apply at full strength."),
        };

        public CameraRawCalibration Clone() { return (CameraRawCalibration)MemberwiseClone(); }

        public bool Adjusts
        {
            get
            {
                return ShadowTint != 0 || RedHue != 0 || RedSaturation != 0 || GreenHue != 0 || GreenSaturation != 0
                    || BlueHue != 0 || BlueSaturation != 0;
            }
        }

        public CameraRawCalibration Normalized()
        {
            var r = Clone();
            r.Process = Math.Max(1, Math.Min(6, Process));
            r.ShadowTint = CameraRaw.Clamp(ShadowTint, -100, 100, 0);
            r.RedHue = CameraRaw.Clamp(RedHue, -100, 100, 0);
            r.RedSaturation = CameraRaw.Clamp(RedSaturation, -100, 100, 0);
            r.GreenHue = CameraRaw.Clamp(GreenHue, -100, 100, 0);
            r.GreenSaturation = CameraRaw.Clamp(GreenSaturation, -100, 100, 0);
            r.BlueHue = CameraRaw.Clamp(BlueHue, -100, 100, 0);
            r.BlueSaturation = CameraRaw.Clamp(BlueSaturation, -100, 100, 0);
            return r;
        }
    }

    // ---------- 전체 설정 ----------

    // 기본값이면 그림이 그대로다.
    public class CameraRawSettings
    {
        // 화이트 밸런스: 색온도·색조는 켈빈이 아니라 상대 이동이다(렌더된 레이어엔 원래 조명값이 없다).
        public const double TemperatureGain = 0.35, TintRedBlue = 0.15, TintGreen = 0.30;

        public bool AutoWhiteBalance;           // 창에서 "자동"을 골랐는지만 기억한다. 계산엔 안 쓴다
        public double Temperature, Tint;        // −100~100
        public double Exposure;                 // −5~5 스톱
        public double Contrast, Highlights, Shadows, Whites, Blacks;
        public double Vibrance, Saturation;
        public double Texture, Clarity, Dehaze;
        public double Glow;                     // 0~100
        public CameraRawGlowStyle GlowStyle = CameraRawGlowStyle.Diffusion;
        public double GlowRange, GlowSpread, GlowWarmth;
        public double VignetteAmount;
        public CameraRawVignetteStyle VignetteStyle = CameraRawVignetteStyle.HighlightPriority;
        public double VignetteMidpoint = 50, VignetteRoundness, VignetteFeather = 50, VignetteHighlights;
        public double GrainAmount, GrainSize = 25, GrainRoughness = 50;
        // 창이 열려 있는 동안 그레인 무늬가 제자리에 있게
        public uint Seed;

        public CameraRawCurve Curve = new CameraRawCurve();
        public CameraRawMixer Mixer = new CameraRawMixer();
        public CameraRawGrading Grading = new CameraRawGrading();
        public CameraRawDetail Detail = new CameraRawDetail();
        public CameraRawOptics Optics = new CameraRawOptics();
        public CameraRawGeometry Geometry = new CameraRawGeometry();
        public CameraRawCalibration Calibration = new CameraRawCalibration();

        public CameraRawSettings Clone()
        {
            var c = (CameraRawSettings)MemberwiseClone();
            c.Curve = Curve.Clone(); c.Mixer = Mixer.Clone(); c.Grading = Grading.Clone(); c.Detail = Detail.Clone();
            c.Optics = Optics.Clone(); c.Geometry = Geometry.Clone(); c.Calibration = Calibration.Clone();
            return c;
        }

        public bool AdjustsLight { get { return Exposure != 0 || Contrast != 0 || Highlights != 0 || Shadows != 0 || Whites != 0 || Blacks != 0; } }
        public bool AdjustsColor { get { return Temperature != 0 || Tint != 0 || Vibrance != 0 || Saturation != 0; } }
        public bool AdjustsEffects
        {
            get { return Texture != 0 || Clarity != 0 || Dehaze != 0 || Glow != 0 || VignetteAmount != 0 || GrainAmount != 0; }
        }

        public bool IsIdentity
        {
            get
            {
                return !AdjustsLight && !AdjustsColor && !AdjustsEffects && !Curve.Adjusts && !Mixer.Adjusts && !Grading.Adjusts
                    && !Detail.Adjusts && !Optics.Adjusts && !Geometry.Adjusts && !Calibration.Adjusts;
            }
        }

        public CameraRawSettings Normalized()
        {
            var r = Clone();
            r.Exposure = CameraRaw.Clamp(Exposure, -5, 5, 0);
            r.Contrast = CameraRaw.Clamp(Contrast, -100, 100, 0);
            r.Highlights = CameraRaw.Clamp(Highlights, -100, 100, 0);
            r.Shadows = CameraRaw.Clamp(Shadows, -100, 100, 0);
            r.Whites = CameraRaw.Clamp(Whites, -100, 100, 0);
            r.Blacks = CameraRaw.Clamp(Blacks, -100, 100, 0);
            r.Temperature = CameraRaw.Clamp(Temperature, -100, 100, 0);
            r.Tint = CameraRaw.Clamp(Tint, -100, 100, 0);
            r.Vibrance = CameraRaw.Clamp(Vibrance, -100, 100, 0);
            r.Saturation = CameraRaw.Clamp(Saturation, -100, 100, 0);
            r.Texture = CameraRaw.Clamp(Texture, -100, 100, 0);
            r.Clarity = CameraRaw.Clamp(Clarity, -100, 100, 0);
            r.Dehaze = CameraRaw.Clamp(Dehaze, -100, 100, 0);
            r.Glow = CameraRaw.Clamp(Glow, 0, 100, 0);
            r.GlowRange = CameraRaw.Clamp(GlowRange, -100, 100, 0);
            r.GlowSpread = CameraRaw.Clamp(GlowSpread, -100, 100, 0);
            r.GlowWarmth = CameraRaw.Clamp(GlowWarmth, -100, 100, 0);
            r.VignetteAmount = CameraRaw.Clamp(VignetteAmount, -100, 100, 0);
            r.VignetteMidpoint = CameraRaw.Clamp(VignetteMidpoint, 0, 100, 50);
            r.VignetteRoundness = CameraRaw.Clamp(VignetteRoundness, -100, 100, 0);
            r.VignetteFeather = CameraRaw.Clamp(VignetteFeather, 0, 100, 50);
            r.VignetteHighlights = CameraRaw.Clamp(VignetteHighlights, 0, 100, 0);
            r.GrainAmount = CameraRaw.Clamp(GrainAmount, 0, 100, 0);
            r.GrainSize = CameraRaw.Clamp(GrainSize, 0, 100, 25);
            r.GrainRoughness = CameraRaw.Clamp(GrainRoughness, 0, 100, 50);
            r.Curve = Curve.Normalized();
            r.Mixer = Mixer.Normalized();
            r.Grading = Grading.Normalized();
            r.Detail = Detail.Normalized();
            r.Optics = Optics.Normalized();
            r.Geometry = Geometry.Normalized();
            r.Calibration = Calibration.Normalized();
            return r;
        }

        // 패널의 눈을 끈 것: 그 묶음만 기본값으로 돌리고 나머지는 둔다.
        // 순서: 0 기본(빛) 1 색 2 효과 3 커브 4 혼합 5 색 보정 6 세부 7 광학 8 기하 9 보정
        public CameraRawSettings Applying(bool[] shows)
        {
            var r = Clone();
            if (!shows[0]) { r.Exposure = 0; r.Contrast = 0; r.Highlights = 0; r.Shadows = 0; r.Whites = 0; r.Blacks = 0; }
            if (!shows[1]) { r.Temperature = 0; r.Tint = 0; r.Vibrance = 0; r.Saturation = 0; }
            if (!shows[2]) { r.Texture = 0; r.Clarity = 0; r.Dehaze = 0; r.Glow = 0; r.VignetteAmount = 0; r.GrainAmount = 0; }
            if (!shows[3]) r.Curve = new CameraRawCurve();
            if (!shows[4]) r.Mixer = new CameraRawMixer();
            if (!shows[5]) r.Grading = new CameraRawGrading();
            if (!shows[6]) r.Detail = new CameraRawDetail();
            if (!shows[7]) r.Optics = new CameraRawOptics();
            if (!shows[8]) r.Geometry = new CameraRawGeometry();
            if (!shows[9]) r.Calibration = new CameraRawCalibration();
            return r;
        }

        // 원본 0~100 크기를 그레인 커널의 픽셀 단위로
        public double GrainKernelSize { get { return 0.5 + (GrainSize / 100) * 19.5; } }
    }

    // ================= 계산 =================

    public static class CameraRaw
    {
        public static double Clamp(double v, double lo, double hi, double fallback)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return fallback;
            return v < lo ? lo : v > hi ? hi : v;
        }

        // 단계 사이를 오가는 그림: 알파를 곱하지 않은 0~1 float 세 판 + 알파 바이트
        sealed class Img
        {
            public int W, H, N;
            public float[] R, G, B;
            public byte[] A;
        }

        // ---------- 진입점 ----------

        // src는 건드리지 않는다. dst는 src와 같은 크기.
        public static void Apply(CameraRawSettings settings, Canvas32 src, Canvas32 dst) { Apply(settings, src, dst, 1); }

        // scale: 레이어 픽셀 하나가 이 그림에서 몇 픽셀인가. 작게 줄인 미리보기에서 블러 반경·그레인 크기를 맞춘다.
        public static void Apply(CameraRawSettings settings, Canvas32 src, Canvas32 dst, double scale)
        {
            var s = settings.Normalized();
            int w = src.W, h = src.H;
            if (s.IsIdentity || w <= 0 || h <= 0) { Array.Copy(src.P, dst.P, src.P.Length); return; }
            double pixelScale = scale > 0 ? scale : 1;

            Canvas32 source = src;
            if (s.Geometry.Adjusts)
            {
                source = new Canvas32(w, h);
                Warp(s.Geometry, src, source);
            }
            var img = Load(source);
            if (s.Calibration.Adjusts) Calibrate(img, s.Calibration);
            if (s.AdjustsLight || s.AdjustsColor) Basic(img, s);
            if (s.Curve.Adjusts || s.Mixer.Adjusts || s.Grading.Adjusts) CurveColor(img, s);
            if (s.AdjustsEffects)
            {
                if (s.Texture != 0 || s.Clarity != 0 || s.Dehaze != 0 || s.Glow != 0 || s.VignetteAmount != 0) Effects(img, s, pixelScale);
                if (s.GrainAmount > 0) Grain(img, s.GrainAmount, s.GrainKernelSize, s.GrainRoughness, s.Seed, 1 / pixelScale);
            }
            if (s.Optics.Adjusts) Optics(img, s.Optics);
            if (s.Detail.Adjusts) Detail(img, s.Detail, pixelScale);
            Store(img, src, dst);
        }

        // ---------- 공통 ----------

        // 원본의 in_bands처럼: 줄을 토막 내서 코어에 나눠 준다
        static void InBands(int count, Action<int, int> body)
        {
            int bands = count < 64 ? 1 : Math.Min(64, Environment.ProcessorCount * 4), size = (count + bands - 1) / bands;
            if (bands == 1) { body(0, count); return; }
            Parallel.For(0, bands, delegate(int band)
            {
                int start = band * size, end = Math.Min(start + size, count);
                if (start < end) body(start, end);
            });
        }

        static Img Load(Canvas32 c)
        {
            var img = new Img { W = c.W, H = c.H, N = c.P.Length };
            img.R = new float[img.N]; img.G = new float[img.N]; img.B = new float[img.N]; img.A = new byte[img.N];
            const float k = 1f / 255f;
            int[] P = c.P;
            InBands(img.H, delegate(int first, int last)
            {
                for (int i = first * img.W; i < last * img.W; i++)
                {
                    int p = P[i];
                    img.A[i] = (byte)((uint)p >> 24);
                    img.R[i] = ((p >> 16) & 255) * k; img.G[i] = ((p >> 8) & 255) * k; img.B[i] = (p & 255) * k;
                }
            });
            return img;
        }

        // NaN도 0으로 떨어진다
        static int Byte(float v) { return v > 0 ? (v >= 1 ? 255 : (int)(v * 255f + 0.5f)) : 0; }

        // 투명해진 픽셀: 원래도 투명했으면 원래 값을 그대로(색 정보까지 보존), 아니면 비운다(기하로 밀려난 곳).
        static void Store(Img img, Canvas32 orig, Canvas32 dst)
        {
            int[] D = dst.P, O = orig.P;
            InBands(img.H, delegate(int first, int last)
            {
                for (int i = first * img.W; i < last * img.W; i++)
                {
                    int a = img.A[i];
                    if (a == 0) { D[i] = ((uint)O[i] >> 24) == 0 ? O[i] : 0; continue; }
                    D[i] = (a << 24) | (Byte(img.R[i]) << 16) | (Byte(img.G[i]) << 8) | Byte(img.B[i]);
                }
            });
        }

        static double C01(double v) { return v < 0 ? 0 : v > 1 ? 1 : v; }

        static double SrgbToLinear(double e)
        {
            if (e <= 0.04045) return e / 12.92;
            return Math.Pow((e + 0.055) / 1.055, 2.4);
        }

        static double LinearToSrgb(double l)
        {
            if (l <= 0) return 0;
            if (l >= 1) return 1;
            if (l <= 0.0031308) return l * 12.92;
            return 1.055 * Math.Pow(l, 1.0 / 2.4) - 0.055;
        }

        static double Rec709(double r, double g, double b) { return 0.2126 * r + 0.7152 * g + 0.0722 * b; }

        // r, g, b의 Rec.709 밝기를 target으로 옮기되 색조는 지킨다. 완전한 검정은 늘릴 수가 없어
        // 밝히는 쪽이면 그 밝기의 무채색으로 칠한다.
        static void ScaleLuminance(ref double r, ref double g, ref double b, double target)
        {
            target = C01(target);
            double y = Rec709(r, g, b);
            if (Math.Abs(target - y) < 1e-8) return;
            if (y < 1e-8)
            {
                if (target > y) r = g = b = target;
                return;
            }
            double scale = target / y;
            r = C01(r * scale); g = C01(g * scale); b = C01(b * scale);
        }

        // ---------- 빛·색 (adjust_camera_raw) ----------

        static double ToneHighlights(double y, double amount)
        {
            double t = C01((y - 0.5) / 0.5), weight = t * t;
            if (amount >= 0) return C01(y + amount * weight * (1.0 - y));
            return C01(y + amount * weight * (y - 0.5));
        }

        static double ToneShadows(double y, double amount)
        {
            double t = C01((0.5 - y) / 0.5), weight = t * t;
            if (amount >= 0) return C01(y + amount * weight * (0.5 - y));
            return C01(y + amount * weight * y);
        }

        // 위쪽 4분의 1이 흰색 점: +1이면 0.875가 1로, −1이면 0.75 위가 전부 0.75로
        static double ToneWhites(double y, double amount)
        {
            if (y <= 0.75) return y;
            return C01(0.75 + (y - 0.75) * (1.0 + amount));
        }

        // 아래쪽 4분의 1이 검정 점: 음수는 0 쪽으로 누르고, 양수는 0.25 쪽으로 들어 올린다
        static double ToneBlacks(double y, double amount)
        {
            if (y >= 0.25) return y;
            return C01(0.25 + (y - 0.25) * (1.0 - amount));
        }

        // 생동감은 옅은 색을 더 밀고 피부색(색조 10~50°)은 아낀다. 채도는 모두 같은 비율로.
        static void VibranceAndSaturation(ref double r, ref double g, ref double b, double vibrance, double saturation)
        {
            double lum = Rec709(r, g, b);
            double maxc = Math.Max(r, Math.Max(g, b)), minc = Math.Min(r, Math.Min(g, b));
            double chroma = maxc - minc;
            double sat = maxc <= 1e-8 ? 0 : chroma / maxc;
            double hue = 0;
            if (chroma > 1e-8)
            {
                if (r >= g && r >= b) hue = 60.0 * Fmod((g - b) / chroma, 6.0);
                else if (g >= r && g >= b) hue = 60.0 * ((b - r) / chroma + 2.0);
                else hue = 60.0 * ((r - g) / chroma + 4.0);
                if (hue < 0) hue += 360.0;
            }
            double skin = 0;
            if (hue >= 10.0 && hue <= 50.0)
            {
                skin = hue <= 30.0 ? (hue - 10.0) / 20.0 : (50.0 - hue) / 20.0;
                skin *= C01((sat - 0.15) / 0.35);
            }
            double amount = vibrance * (1.0 - sat);
            if (vibrance > 0) amount *= 1.0 - 0.7 * skin;
            double factor = 1.0 + amount;
            r = C01(lum + (r - lum) * factor); g = C01(lum + (g - lum) * factor); b = C01(lum + (b - lum) * factor);
            lum = Rec709(r, g, b);
            factor = 1.0 + saturation;
            r = C01(lum + (r - lum) * factor); g = C01(lum + (g - lum) * factor); b = C01(lum + (b - lum) * factor);
        }

        // C의 fmod: 부호는 나뉘는 수를 따른다(C#의 %와 같다)
        static double Fmod(double a, double m) { return a % m; }

        static void Basic(Img img, CameraRawSettings s)
        {
            double warm = s.Temperature / 100, magenta = s.Tint / 100;
            double redGain = 1 + CameraRawSettings.TemperatureGain * warm + CameraRawSettings.TintRedBlue * magenta;
            double greenGain = 1 - CameraRawSettings.TintGreen * magenta;
            double blueGain = 1 - CameraRawSettings.TemperatureGain * warm + CameraRawSettings.TintRedBlue * magenta;
            double light = Math.Pow(2, s.Exposure);
            double contrastScale = 1.0 + s.Contrast / 100.0;
            double hiAmt = s.Highlights / 100.0, shAmt = s.Shadows / 100.0, whAmt = s.Whites / 100.0, blAmt = s.Blacks / 100.0;
            double vib = s.Vibrance / 100.0, sat = s.Saturation / 100.0;
            bool tones = hiAmt != 0 || shAmt != 0 || whAmt != 0 || blAmt != 0;
            InBands(img.H, delegate(int first, int last)
            {
                for (int i = first * img.W; i < last * img.W; i++)
                {
                    if (img.A[i] == 0) continue;
                    double r = img.R[i], g = img.G[i], b = img.B[i];
                    // 노출·화이트 밸런스는 선형 빛에서 곱한다
                    r = C01(SrgbToLinear(r) * redGain * light);
                    g = C01(SrgbToLinear(g) * greenGain * light);
                    b = C01(SrgbToLinear(b) * blueGain * light);
                    r = C01(0.5 + (LinearToSrgb(r) - 0.5) * contrastScale);
                    g = C01(0.5 + (LinearToSrgb(g) - 0.5) * contrastScale);
                    b = C01(0.5 + (LinearToSrgb(b) - 0.5) * contrastScale);
                    if (tones)
                    {
                        // 0이면 같은 값을 돌려주니 건너뛰어도 결과가 같다
                        if (hiAmt != 0) ScaleLuminance(ref r, ref g, ref b, ToneHighlights(Rec709(r, g, b), hiAmt));
                        if (shAmt != 0) ScaleLuminance(ref r, ref g, ref b, ToneShadows(Rec709(r, g, b), shAmt));
                        if (whAmt != 0) ScaleLuminance(ref r, ref g, ref b, ToneWhites(Rec709(r, g, b), whAmt));
                        if (blAmt != 0) ScaleLuminance(ref r, ref g, ref b, ToneBlacks(Rec709(r, g, b), blAmt));
                    }
                    VibranceAndSaturation(ref r, ref g, ref b, vib, sat);
                    img.R[i] = (float)r; img.G[i] = (float)g; img.B[i] = (float)b;
                }
            });
        }

        // ---------- 상자 블러 (box_blur_plane) ----------

        // 가장자리는 끝 픽셀이 이어진다고 본다. dst와 src는 달라야 한다.
        static void BoxBlur(float[] src, float[] dst, int w, int h, int radius)
        {
            if (radius < 1) { Array.Copy(src, dst, src.Length); return; }
            var tmp = new float[w * h];
            double window = radius * 2 + 1;
            InBands(h, delegate(int first, int last)
            {
                for (int y = first; y < last; y++)
                {
                    int row = y * w;
                    double sum = 0;
                    for (int k = -radius; k <= radius; k++) sum += src[row + Idx(k, w)];
                    for (int x = 0; x < w; x++)
                    {
                        tmp[row + x] = (float)(sum / window);
                        sum += src[row + Idx(x + radius + 1, w)];
                        sum -= src[row + Idx(x - radius, w)];
                    }
                }
            });
            InBands(w, delegate(int first, int last)
            {
                for (int x = first; x < last; x++)
                {
                    double sum = 0;
                    for (int k = -radius; k <= radius; k++) sum += tmp[Idx(k, h) * w + x];
                    for (int y = 0; y < h; y++)
                    {
                        dst[y * w + x] = (float)(sum / window);
                        sum += tmp[Idx(y + radius + 1, h) * w + x];
                        sum -= tmp[Idx(y - radius, h) * w + x];
                    }
                }
            });
        }

        static int Idx(int i, int limit) { return i < 0 ? 0 : i >= limit ? limit - 1 : i; }

        static int EffectsRadius(double baseRadius, double scale)
        {
            double radius = baseRadius * (scale > 0 ? scale : 1);
            if (radius < 1) radius = 1;
            if (radius > 64) radius = 64;
            return (int)Math.Round(radius, MidpointRounding.AwayFromZero);
        }

        static float[] Luma(Img img)
        {
            var luma = new float[img.N];
            InBands(img.H, delegate(int first, int last)
            {
                for (int i = first * img.W; i < last * img.W; i++)
                    luma[i] = img.A[i] == 0 ? 0 : (float)Rec709(img.R[i], img.G[i], img.B[i]);
            });
            return luma;
        }

        // ---------- 효과 (adjust_camera_raw_effects) ----------

        static void Dehaze(ref double r, ref double g, ref double b, double amount)
        {
            double d = amount / 100.0;
            double y = Rec709(r, g, b);
            double contrast = 1.0 + 0.8 * d;
            double pivot = 0.45 - 0.1 * (d > 0 ? d : 0);
            double y2 = C01(pivot + (y - 0.45) * contrast);
            if (d < 0) y2 = C01(y2 + (-d) * (1.0 - y2) * 0.45);
            else y2 = C01(y2 - d * Math.Max(0.0, 0.4 - y2));
            ScaleLuminance(ref r, ref g, ref b, y2);
            y2 = Rec709(r, g, b);
            double sat = 1.0 + 0.7 * d;
            r = C01(y2 + (r - y2) * sat); g = C01(y2 + (g - y2) * sat); b = C01(y2 + (b - y2) * sat);
        }

        // 가운데 0, 가장자리 너머 1. 둥글기는 원과 네모 사이를 섞는다.
        static double VignetteMask(double px, double py, double width, double height, double midpoint, double roundness, double feather)
        {
            double nx = px / width * 2.0 - 1.0;
            double ny = py / height * 2.0 - 1.0;
            double square = Math.Max(Math.Abs(nx), Math.Abs(ny));
            double circle = Math.Sqrt(nx * nx + ny * ny) / Math.Sqrt(2.0);
            double shape = (1.0 - roundness / 100.0) * 0.5;
            double dist = circle + (square - circle) * shape;
            double start = (midpoint / 100.0) * 0.85;
            double soft = feather / 100.0;
            if (soft < 0.05) soft = 0.05;
            double t = C01((dist - start) / soft);
            return t * t * (3.0 - 2.0 * t);
        }

        static void Vignette(ref double r, ref double g, ref double b, int x, int y, int width, int height,
                             double amount, double midpoint, double roundness, double feather, double highlights, int style)
        {
            if (amount == 0 || width == 0 || height == 0) return;
            double mask = VignetteMask(x + 0.5, y + 0.5, width, height, midpoint, roundness, feather);
            double effect = (amount / 100.0) * mask;
            // 밝은 영역 우선은 어둡게 하는 비네팅을 밝은 픽셀에서 늦춘다. 다른 스타일은 그러지 않는다.
            if (effect < 0 && style == 0)
            {
                double bright = C01((Rec709(r, g, b) - 0.45) / 0.55);
                effect *= 1.0 - (highlights / 100.0) * bright;
            }
            if (effect < 0)
            {
                double factor = 1.0 + effect;
                r *= factor; g *= factor; b *= factor;
            }
            else if (effect > 0)
            {
                r = r + (1.0 - r) * effect; g = g + (1.0 - g) * effect; b = b + (1.0 - b) * effect;
            }
            if (style == 1 && mask > 0)
            {
                double lum = Rec709(r, g, b);
                double sat = 1.0 - 0.75 * mask * Math.Abs(amount / 100.0);
                r = C01(lum + (r - lum) * sat); g = C01(lum + (g - lum) * sat); b = C01(lum + (b - lum) * sat);
            }
        }

        static void Effects(Img img, CameraRawSettings s, double scale)
        {
            int w = img.W, h = img.H;
            double texture = s.Texture, clarity = s.Clarity, dehaze = s.Dehaze, glow = s.Glow;
            int glowStyle = (int)s.GlowStyle;
            float[] fine = null, coarse = null, glowPlane = null;
            if (texture != 0 || clarity != 0 || glow > 0)
            {
                var luma = Luma(img);
                if (texture != 0) { fine = new float[img.N]; BoxBlur(luma, fine, w, h, EffectsRadius(1, scale)); }
                if (clarity != 0) { coarse = new float[img.N]; BoxBlur(luma, coarse, w, h, EffectsRadius(4, scale)); }
                if (glow > 0)
                {
                    double spread = s.GlowSpread / 100.0;
                    double baseRadius = glowStyle == 1 ? 2.0 : 5.0;
                    double widened = baseRadius * (1.0 + spread);
                    if (widened < 1) widened = 1;
                    int glowRadius = EffectsRadius(widened, scale);
                    float threshold = (float)(0.55 + 0.4 * (s.GlowRange / 100.0));
                    float denom = 1.0f - threshold;
                    if (denom < 0.05f) denom = 0.05f;
                    var bright = new float[img.N];
                    for (int i = 0; i < img.N; i++)
                    {
                        float t = (luma[i] - threshold) / denom;
                        bright[i] = t < 0 ? 0 : t > 1 ? 1 : t;
                    }
                    glowPlane = new float[img.N];
                    BoxBlur(bright, glowPlane, w, h, glowRadius);
                }
            }
            double warmth = s.GlowWarmth / 100.0;
            double glowRed, glowGreen, glowBlue, glowGain;
            if (glowStyle == 2)
            {
                // 할레이션의 테두리는 빨강. 따뜻함은 노랑·파랑이 아니라 더 빨간 쪽으로 민다.
                glowRed = 1; glowGreen = 0.35 - 0.3 * warmth; glowBlue = 0.2 - 0.2 * warmth; glowGain = 1;
            }
            else
            {
                glowRed = 0.75 + 0.25 * warmth; glowGreen = 0.6 + 0.2 * warmth; glowBlue = 0.75 - 0.6 * warmth;
                glowGain = glowStyle == 1 ? 1.4 : 1;
            }
            double vAmt = s.VignetteAmount, vMid = s.VignetteMidpoint, vRound = s.VignetteRoundness, vFeather = s.VignetteFeather,
                   vHi = s.VignetteHighlights;
            int vStyle = (int)s.VignetteStyle;
            InBands(h, delegate(int first, int last)
            {
                for (int y = first; y < last; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int i = y * w + x;
                        if (img.A[i] == 0) continue;
                        double r = img.R[i], g = img.G[i], b = img.B[i];
                        if (fine != null || coarse != null)
                        {
                            double tone = Rec709(r, g, b);
                            double detail = 0;
                            if (fine != null) detail += (texture / 100.0) * (tone - fine[i]);
                            if (coarse != null) detail += (clarity / 100.0) * (tone - coarse[i]);
                            if (detail != 0) ScaleLuminance(ref r, ref g, ref b, C01(tone + detail));
                        }
                        if (dehaze != 0) Dehaze(ref r, ref g, ref b, dehaze);
                        if (glowPlane != null)
                        {
                            double add = glowPlane[i] * (glow / 100.0) * glowGain;
                            r = C01(r + add * glowRed); g = C01(g + add * glowGreen); b = C01(b + add * glowBlue);
                        }
                        Vignette(ref r, ref g, ref b, x, y, w, h, vAmt, vMid, vRound, vFeather, vHi, vStyle);
                        img.R[i] = (float)r; img.G[i] = (float)g; img.B[i] = (float)b;
                    }
            });
        }

        // ---------- 그레인 (adjust_grain) ----------

        static uint Mix32(uint x)
        {
            unchecked
            {
                x ^= x >> 16; x *= 0x7feb352dU; x ^= x >> 15; x *= 0x846ca68bU; x ^= x >> 16;
                return x;
            }
        }

        // 정수 격자점마다 −1~1 값 하나(점과 씨앗으로 정해진다). 고른 값 둘을 더해 삼각 분포 — 평평한 잡음보다 필름 입자에 가깝다.
        static float Lattice(long ix, long iy, uint seed)
        {
            unchecked
            {
                uint h = Mix32(((uint)ix * 0x9E3779B1U) ^ Mix32(((uint)iy * 0x85EBCA77U) ^ seed));
                return (float)(h & 0xFFFFU) / 65535.0f + (float)(h >> 16) / 65535.0f - 1.0f;
            }
        }

        // scale 픽셀 크기의 매끈한 잡음
        static float GrainField(double u, double v, double scale, uint seed)
        {
            double cellX = Math.Floor(u / scale), cellY = Math.Floor(v / scale);
            float tx = (float)(u / scale - cellX), ty = (float)(v / scale - cellY);
            tx = tx * tx * (3.0f - 2.0f * tx);
            ty = ty * ty * (3.0f - 2.0f * ty);
            long ix = (long)cellX, iy = (long)cellY;
            float n00 = Lattice(ix, iy, seed), n10 = Lattice(ix + 1, iy, seed);
            float n01 = Lattice(ix, iy + 1, seed), n11 = Lattice(ix + 1, iy + 1, seed);
            float top = n00 + (n10 - n00) * tx, bottom = n01 + (n11 - n01) * tx;
            // 이웃끼리 섞으면 폭이 좁아지니 대략 원래 폭으로 되돌린다
            return (top + (bottom - top) * ty) * 1.6f;
        }

        static float Clamp255(float v) { return v < 0 ? 0 : v > 255 ? 255 : v; }

        static void Grain(Img img, double amount, double size, double roughness, uint seed, double unitsPerPixel)
        {
            if (!(amount > 0) || !(unitsPerPixel > 0)) return;
            if (!(size > 0)) size = 1;
            float strength = (float)(amount > 100 ? 1.0 : amount / 100.0) * 0.35f * 255.0f;
            float rough = (float)(roughness < 0 ? 0.0 : roughness > 100 ? 1.0 : roughness / 100.0);
            uint fineSeed = Mix32(seed ^ 0xA511E9B3U);
            // 거칠기는 더 작고 불규칙한 입자를 얹는다. 그 크기도 크기 슬라이더를 따라간다.
            double detailSize = Math.Max(0.5, size * 0.35);
            int w = img.W;
            InBands(img.H, delegate(int first, int last)
            {
                for (int y = first; y < last; y++)
                {
                    double v = (y + 0.5) * unitsPerPixel;
                    for (int x = 0; x < w; x++)
                    {
                        int i = y * w + x;
                        if (img.A[i] == 0) continue;
                        double u = (x + 0.5) * unitsPerPixel;
                        float smooth = GrainField(u, v, size, seed);
                        float fine = GrainField(u, v, detailSize, fineSeed);
                        float noise = smooth + (fine - smooth) * rough;
                        float r = img.R[i] * 255f, g = img.G[i] * 255f, b = img.B[i] * 255f;
                        float level = (0.2126f * r + 0.7152f * g + 0.0722f * b) / 255.0f;
                        if (level > 1) level = 1;
                        // 필름 입자는 중간톤에서 가장 잘 보인다
                        float delta = noise * strength * (0.4f + 2.4f * level * (1.0f - level));
                        img.R[i] = Clamp255(r + delta) / 255f; img.G[i] = Clamp255(g + delta) / 255f; img.B[i] = Clamp255(b + delta) / 255f;
                    }
                }
            });
        }

        // ---------- 커브·혼합·색 보정 (adjust_camera_raw_curve_color) ----------

        static double LutAt(float[] lut, double value)
        {
            double scaled = C01(value) * 255.0;
            int lo = (int)scaled;
            int hi = lo < 255 ? lo + 1 : 255;
            double t = scaled - lo;
            return lut[lo] + (lut[hi] - lut[lo]) * t;
        }

        // 색조 0~1
        static void RgbToHsl(double r, double g, double b, out double h, out double s, out double l)
        {
            double maxc = Math.Max(r, Math.Max(g, b)), minc = Math.Min(r, Math.Min(g, b));
            l = (maxc + minc) * 0.5;
            double d = maxc - minc;
            if (d < 1e-6) { h = 0; s = 0; return; }
            s = d / (1.0 - Math.Abs(2.0 * l - 1.0));
            if (maxc == r) h = Fmod((g - b) / d, 6.0);
            else if (maxc == g) h = (b - r) / d + 2.0;
            else h = (r - g) / d + 4.0;
            h /= 6.0;
            if (h < 0) h += 1;
        }

        static double HueToRgb(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 0.5) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }

        static void HslToRgb(double h, double s, double l, out double r, out double g, out double b)
        {
            if (s <= 1e-6) { r = g = b = l; return; }
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            double p = 2 * l - q;
            r = HueToRgb(p, q, h + 1.0 / 3);
            g = HueToRgb(p, q, h);
            b = HueToRgb(p, q, h - 1.0 / 3);
        }

        static double CircularDistance(double a, double b)
        {
            double d = Math.Abs(a - b);
            return d > 0.5 ? 1 - d : d;
        }

        static readonly double[] MixerCenters = { 0, 30.0 / 360, 60.0 / 360, 120.0 / 360, 180.0 / 360, 240.0 / 360, 270.0 / 360, 300.0 / 360 };

        // point: [색조 0~1, 채도, 광도, 색조 이동, 채도 이동, 광도 이동, 색조 범위 0~1, 채도 범위, 광도 범위]
        static double PointWeight(double h, double s, double l, float[] pt)
        {
            double hueHalf = pt[6] > 0.01f ? pt[6] : 0.01f;
            double satHalf = pt[7] > 0.01f ? pt[7] : 0.01f;
            double lumHalf = pt[8] > 0.01f ? pt[8] : 0.01f;
            double hueW = 1 - CircularDistance(h, pt[0]) / hueHalf;
            double satW = 1 - Math.Abs(s - pt[1]) / satHalf;
            double lumW = 1 - Math.Abs(l - pt[2]) / lumHalf;
            if (hueW < 0 || satW < 0 || lumW < 0) return 0;
            return hueW * satW * lumW;
        }

        static void CurveColor(Img img, CameraRawSettings s)
        {
            var curve = s.Curve;
            var tone = curve.ToneTable();
            var redLut = CameraRawCurve.ChannelTable(curve.Red);
            var greenLut = CameraRawCurve.ChannelTable(curve.Green);
            var blueLut = CameraRawCurve.ChannelTable(curve.Blue);
            double refine = curve.RefineSaturation / 100;
            var mixer = new float[24];
            for (int i = 0; i < 8; i++)
            {
                mixer[i] = (float)(s.Mixer.Hue[i] / 100);
                mixer[8 + i] = (float)(s.Mixer.Saturation[i] / 100);
                mixer[16 + i] = (float)(s.Mixer.Luminance[i] / 100);
            }
            bool anyMixer = s.Mixer.Adjusts;
            var points = new List<float[]>();
            foreach (var p in s.Mixer.Points)
                points.Add(new[]
                {
                    (float)(p.Hue / 360), (float)p.Saturation, (float)p.Luminance,
                    (float)(p.HueShift / 100), (float)(p.SaturationShift / 100), (float)(p.LuminanceShift / 100),
                    (float)(p.HueRange / 360), (float)p.SaturationRange, (float)p.LuminanceRange
                });
            var wheels = s.Grading.Wheels;
            var grade = new double[12];
            for (int k = 0; k < 4; k++)
            {
                grade[k * 3] = (float)(wheels[k].Hue / 360);
                grade[k * 3 + 1] = (float)(wheels[k].Saturation / 100);
                grade[k * 3 + 2] = (float)(wheels[k].Luminance / 100);
            }
            // 바퀴 색은 바퀴마다 하나뿐이니 미리
            var wheelRgb = new double[12];
            for (int k = 0; k < 4; k++)
            {
                double cr, cg, cb;
                HslToRgb(grade[k * 3], 1, 0.5, out cr, out cg, out cb);
                wheelRgb[k * 3] = cr; wheelRgb[k * 3 + 1] = cg; wheelRgb[k * 3 + 2] = cb;
            }
            double blending = s.Grading.Blending / 100, balance = s.Grading.Balance / 100;
            bool anyGrade = s.Grading.Adjusts;
            InBands(img.H, delegate(int first, int last)
            {
                var weights = new double[4];
                for (int i = first * img.W; i < last * img.W; i++)
                {
                    if (img.A[i] == 0) continue;
                    double r = img.R[i], g = img.G[i], b = img.B[i];
                    // 톤 커브는 포토샵처럼 R·G·B에 똑같이 걸려 대비가 채도를 함께 끌고 온다.
                    // 채도 다듬기가 0보다 작으면 밝기만 바꾸는 쪽(−100)으로, 크면 색을 더한다.
                    double curvedR = LutAt(tone, r), curvedG = LutAt(tone, g), curvedB = LutAt(tone, b);
                    if (refine < 0)
                    {
                        double br = r, bg = g, bb = b;
                        ScaleLuminance(ref br, ref bg, ref bb, LutAt(tone, Rec709(r, g, b)));
                        double k = -refine;
                        curvedR += (br - curvedR) * k; curvedG += (bg - curvedG) * k; curvedB += (bb - curvedB) * k;
                    }
                    else if (refine > 0)
                    {
                        double lum = Rec709(curvedR, curvedG, curvedB), factor = 1 + refine;
                        curvedR = C01(lum + (curvedR - lum) * factor);
                        curvedG = C01(lum + (curvedG - lum) * factor);
                        curvedB = C01(lum + (curvedB - lum) * factor);
                    }
                    r = LutAt(redLut, curvedR); g = LutAt(greenLut, curvedG); b = LutAt(blueLut, curvedB);

                    if (anyMixer)
                    {
                        double h, sa, l;
                        RgbToHsl(r, g, b, out h, out sa, out l);
                        double hueDelta = 0, satDelta = 0, lumDelta = 0, weightSum = 0;
                        for (int k = 0; k < 8; k++)
                        {
                            double wgt = 1 - CircularDistance(h, MixerCenters[k]) / (40.0 / 360);
                            if (wgt <= 0) continue;
                            hueDelta += mixer[k] * wgt * (30.0 / 360);
                            satDelta += mixer[8 + k] * wgt;
                            lumDelta += mixer[16 + k] * wgt * 0.25;
                            weightSum += wgt;
                        }
                        if (weightSum > 1) { hueDelta /= weightSum; satDelta /= weightSum; lumDelta /= weightSum; }
                        h += hueDelta; if (h < 0) h += 1; if (h >= 1) h -= 1;
                        sa = C01(sa * (1 + satDelta));
                        l = C01(l + lumDelta);
                        for (int k = 0; k < points.Count; k++)
                        {
                            var pt = points[k];
                            double wgt = PointWeight(h, sa, l, pt);
                            if (wgt <= 0) continue;
                            h += pt[3] * wgt * (30.0 / 360);
                            sa = C01(sa * (1 + pt[4] * wgt));
                            l = C01(l + pt[5] * wgt * 0.25);
                        }
                        if (h < 0) h += 1; if (h >= 1) h -= 1;
                        HslToRgb(h, sa, l, out r, out g, out b);
                    }
                    // 원본은 혼합이 없어도 HSL을 한 바퀴 돈다. 그 왕복은 값을 바꾸지 않으니(오차 수준) 건너뛴다.

                    if (anyGrade)
                    {
                        // 균형은 어두운·밝은 바퀴의 갈림점을 옮긴다. 밝은 쪽으로 가려면 갈림점이 내려와야
                        // 그림의 더 많은 부분이 밝은 영역으로 잡히고 어두운 바퀴의 힘이 빠진다.
                        double split = 0.5 - balance * 0.2;
                        double reach = 0.12 + blending * 0.38;
                        double y0 = Rec709(r, g, b);
                        double shadowW = C01((split + reach - y0) / Math.Max(0.05, reach * 2));
                        double highlightW = C01((y0 - (split - reach)) / Math.Max(0.05, reach * 2));
                        double midW = C01(1 - Math.Abs(y0 - split) / (0.35 + reach));
                        double sum = shadowW + midW + highlightW;
                        if (sum > 1e-4) { shadowW /= sum; midW /= sum; highlightW /= sum; }
                        weights[0] = shadowW; weights[1] = midW; weights[2] = highlightW; weights[3] = 1;
                        for (int k = 0; k < 4; k++)
                        {
                            double ws = grade[k * 3 + 1], wl = grade[k * 3 + 2];
                            double wgt = weights[k];
                            if (wgt <= 0 || (ws <= 0 && wl == 0)) continue;
                            r = C01(r + (wheelRgb[k * 3] - 0.5) * ws * wgt * 0.85);
                            g = C01(g + (wheelRgb[k * 3 + 1] - 0.5) * ws * wgt * 0.85);
                            b = C01(b + (wheelRgb[k * 3 + 2] - 0.5) * ws * wgt * 0.85);
                            if (wl != 0) ScaleLuminance(ref r, ref g, ref b, C01(Rec709(r, g, b) + wl * 0.25 * wgt));
                        }
                    }
                    img.R[i] = (float)C01(r); img.G[i] = (float)C01(g); img.B[i] = (float)C01(b);
                }
            });
        }

        // ---------- 세부 (adjust_camera_raw_detail) ----------

        static double DetailRadius(double slider, double scale)
        {
            double radius = (0.5 + (slider / 100.0) * 2.5) * (scale > 0 ? scale : 1);
            if (radius < 0.5) radius = 0.5;
            if (radius > 64) radius = 64;
            return radius;
        }

        // 가운데와 반경만큼 떨어진 여덟 이웃의 평균 밝기 차
        static float EdgeAt(float[] luma, int width, int height, int x, int y, int radius)
        {
            if (radius < 1) radius = 1;
            float center = luma[y * width + x];
            float sum = 0;
            int count = 0;
            for (int dy = -radius; dy <= radius; dy += radius)
                for (int dx = -radius; dx <= radius; dx += radius)
                {
                    if (dx == 0 && dy == 0) continue;
                    int sx = x + dx, sy = y + dy;
                    if (sx < 0 || sy < 0 || sx >= width || sy >= height) continue;
                    sum += Math.Abs(luma[sy * width + sx] - center);
                    count++;
                }
            return count > 0 ? sum / count : 0;
        }

        static void Detail(Img img, CameraRawDetail d, double scale)
        {
            int w = img.W, h = img.H;
            var work = new float[img.N];
            if (d.NoiseLuminance > 0)
            {
                var luma = Luma(img);
                BoxBlur(luma, work, w, h, EffectsRadius(1.0 + d.NoiseLuminance / 50.0, scale));
                double strength = d.NoiseLuminance / 100.0, preserve = d.NoiseLuminanceDetail / 100.0, contrast = d.NoiseLuminanceContrast / 100.0;
                InBands(h, delegate(int first, int last)
                {
                    for (int y = first; y < last; y++)
                        for (int x = 0; x < w; x++)
                        {
                            int i = y * w + x;
                            if (img.A[i] == 0) continue;
                            float edge = EdgeAt(luma, w, h, x, y, 1);
                            double local = strength * (1.0 - preserve * Math.Min(1.0, edge * 6.0));
                            float blurred = work[i];
                            float target = (float)(luma[i] * (1.0 - local) + blurred * local);
                            if (contrast != 0) target = (float)(target + contrast * 0.25 * (luma[i] - blurred));
                            double r = img.R[i], g = img.G[i], b = img.B[i];
                            ScaleLuminance(ref r, ref g, ref b, target);
                            img.R[i] = (float)r; img.G[i] = (float)g; img.B[i] = (float)b;
                        }
                });
            }
            if (d.NoiseColor > 0)
            {
                var chroma = new float[img.N];
                InBands(h, delegate(int first, int last)
                {
                    for (int i = first * w; i < last * w; i++)
                    {
                        if (img.A[i] == 0) continue;
                        double hh, ss, ll;
                        RgbToHsl(img.R[i], img.G[i], img.B[i], out hh, out ss, out ll);
                        chroma[i] = (float)ss;
                    }
                });
                var chromaBlur = new float[img.N];
                BoxBlur(chroma, chromaBlur, w, h, EffectsRadius(1.0 + d.NoiseColorSmoothness / 40.0, scale));
                double strength = d.NoiseColor / 100.0, preserve = d.NoiseColorDetail / 100.0;
                InBands(h, delegate(int first, int last)
                {
                    for (int i = first * w; i < last * w; i++)
                    {
                        if (img.A[i] == 0) continue;
                        float edge = Math.Abs(chroma[i] - chromaBlur[i]);
                        double local = strength * (1.0 - preserve * Math.Min(1.0, edge * 4.0));
                        float sat = chroma[i] * (float)(1.0 - local) + chromaBlur[i] * (float)local;
                        double hh, ss, ll, r, g, b;
                        RgbToHsl(img.R[i], img.G[i], img.B[i], out hh, out ss, out ll);
                        HslToRgb(hh, sat, ll, out r, out g, out b);
                        img.R[i] = (float)C01(r); img.G[i] = (float)C01(g); img.B[i] = (float)C01(b);
                    }
                });
            }
            if (d.SharpenAmount > 0)
            {
                var luma = Luma(img);
                int radius = EffectsRadius(DetailRadius(d.SharpenRadius, scale), 1);
                BoxBlur(luma, work, w, h, radius);
                double amount = d.SharpenAmount / 100.0, detailMix = d.SharpenDetail / 100.0;
                double threshold = (d.SharpenMasking / 100.0) * 0.35;
                InBands(h, delegate(int first, int last)
                {
                    for (int y = first; y < last; y++)
                        for (int x = 0; x < w; x++)
                        {
                            int i = y * w + x;
                            if (img.A[i] == 0) continue;
                            float edge = EdgeAt(luma, w, h, x, y, radius);
                            double mask = C01((edge * (0.5 + detailMix) - threshold) / Math.Max(0.04, 0.35 - threshold * 0.5));
                            double high = luma[i] - work[i];
                            double sharpened = C01(luma[i] + high * amount * mask * (0.5 + detailMix));
                            double r = img.R[i], g = img.G[i], b = img.B[i];
                            ScaleLuminance(ref r, ref g, ref b, sharpened);
                            img.R[i] = (float)r; img.G[i] = (float)g; img.B[i] = (float)b;
                        }
                });
            }
        }

        // ---------- 광학 (adjust_camera_raw_optics) ----------

        static double PixelHueDeg(double r, double g, double b)
        {
            double maxc = Math.Max(r, Math.Max(g, b)), minc = Math.Min(r, Math.Min(g, b));
            double chroma = maxc - minc;
            if (chroma < 1e-6) return 0;
            double hue;
            if (maxc == r) hue = Fmod((g - b) / chroma, 6.0);
            else if (maxc == g) hue = (b - r) / chroma + 2.0;
            else hue = (r - g) / chroma + 4.0;
            hue *= 60.0;
            if (hue < 0) hue += 360.0;
            return hue;
        }

        static bool HueInRange(double hue, double low, double high)
        {
            if (low <= high) return hue >= low && hue <= high;
            return hue >= low || hue <= high;
        }

        // 자주·녹색 테두리: 그 색조 범위에 든 픽셀의 채도를 뺀다(진한 색일수록 더)
        static void Defringe(ref double r, ref double g, ref double b, CameraRawOptics o)
        {
            double hue = PixelHueDeg(r, g, b);
            double maxc = Math.Max(r, Math.Max(g, b)), minc = Math.Min(r, Math.Min(g, b));
            double chroma = maxc - minc;
            if (chroma < 1e-6) return;
            double sat = chroma / maxc;
            double reduce = 0;
            if (o.PurpleAmount > 0 && HueInRange(hue, o.PurpleHueLow, o.PurpleHueHigh)) reduce = Math.Max(reduce, o.PurpleAmount / 100.0);
            if (o.GreenAmount > 0 && HueInRange(hue, o.GreenHueLow, o.GreenHueHigh)) reduce = Math.Max(reduce, o.GreenAmount / 100.0);
            if (reduce <= 0) return;
            double lum = Rec709(r, g, b);
            double factor = 1.0 - reduce * sat;
            r = C01(lum + (r - lum) * factor); g = C01(lum + (g - lum) * factor); b = C01(lum + (b - lum) * factor);
        }

        static void VignetteCorrect(ref double r, ref double g, ref double b, int x, int y, int width, int height, double amount, double midpoint)
        {
            if (amount == 0 || width == 0 || height == 0) return;
            double nx = (x + 0.5) / width * 2.0 - 1.0;
            double ny = (y + 0.5) / height * 2.0 - 1.0;
            double dist = Math.Sqrt(nx * nx + ny * ny) / Math.Sqrt(2.0);
            double start = (midpoint / 100.0) * 0.85;
            double t = C01((dist - start) / 0.35);
            double mask = t * t * (3.0 - 2.0 * t);
            double lift = (amount / 100.0) * mask;
            if (lift > 0)
            {
                r = C01(r + (1.0 - r) * lift); g = C01(g + (1.0 - g) * lift); b = C01(b + (1.0 - b) * lift);
            }
            else
            {
                double factor = 1.0 + lift;
                r *= factor; g *= factor; b *= factor;
            }
        }

        // lens_distort: 가운데에서 멀수록 k만큼 안팎으로 당겨 읽는다. 원본은 알파를 곱한 채로 섞으니
        // 여기서도 알파로 무게를 줘서 섞는다(투명한 가장자리의 색이 번져 들지 않게).
        static void LensDistort(Img img, double k)
        {
            int w = img.W, h = img.H;
            var sr = (float[])img.R.Clone(); var sg = (float[])img.G.Clone(); var sb = (float[])img.B.Clone(); var sa = (byte[])img.A.Clone();
            double cx = w * 0.5, cy = h * 0.5;
            double halfDiagonal2 = cx * cx + cy * cy;
            InBands(h, delegate(int first, int last)
            {
                for (int y = first; y < last; y++)
                {
                    double dy = y + 0.5 - cy;
                    for (int x = 0; x < w; x++)
                    {
                        double dx = x + 0.5 - cx;
                        double scale = 1.0 - k * (dx * dx + dy * dy) / halfDiagonal2;
                        double fxs = cx + dx * scale - 0.5, fys = cy + dy * scale - 0.5;
                        Sample(sr, sg, sb, sa, w, h, fxs, fys, img, y * w + x);
                    }
                }
            });
        }

        // 겹선형 보간 한 점. 범위 밖 이웃은 투명으로 친다.
        static void Sample(float[] sr, float[] sg, float[] sb, byte[] sa, int w, int h, double sx, double sy, Img dst, int at)
        {
            double fx0 = Math.Floor(sx), fy0 = Math.Floor(sy);
            double fx = sx - fx0, fy = sy - fy0;
            long x0 = (long)fx0, y0 = (long)fy0;
            double A = 0, R = 0, G = 0, B = 0;
            for (int j = 0; j < 2; j++)
            {
                long row = y0 + j;
                if (row < 0 || row >= h) continue;
                double wy = j != 0 ? fy : 1 - fy;
                if (wy == 0) continue;
                for (int i = 0; i < 2; i++)
                {
                    long col = x0 + i;
                    if (col < 0 || col >= w) continue;
                    double wt = wy * (i != 0 ? fx : 1 - fx);
                    if (wt == 0) continue;
                    int p = (int)(row * w + col);
                    double a = sa[p] * wt;
                    A += a; R += a * sr[p]; G += a * sg[p]; B += a * sb[p];
                }
            }
            int ai = (int)Math.Round(A, MidpointRounding.AwayFromZero);
            if (ai > 255) ai = 255;
            dst.A[at] = (byte)ai;
            if (A > 1e-9) { dst.R[at] = (float)(R / A); dst.G[at] = (float)(G / A); dst.B[at] = (float)(B / A); }
            else { dst.R[at] = dst.G[at] = dst.B[at] = 0; }
        }

        // 빨강은 가운데 쪽에서, 파랑은 바깥쪽에서 가져와 색 테두리를 맞붙인다
        static void Chromatic(Img img, double strength)
        {
            if (strength <= 0) return;
            int w = img.W, h = img.H;
            var sr = (float[])img.R.Clone(); var sb = (float[])img.B.Clone();
            double cx = w * 0.5, cy = h * 0.5;
            double maxR = Math.Sqrt(cx * cx + cy * cy);
            InBands(h, delegate(int first, int last)
            {
                for (int y = first; y < last; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int i = y * w + x;
                        if (img.A[i] == 0) continue;
                        double dx = x + 0.5 - cx, dy = y + 0.5 - cy;
                        double radial = Math.Sqrt(dx * dx + dy * dy) / maxR;
                        double shift = strength * radial * radial * 2.5;
                        int rx = (int)Math.Round(x - shift, MidpointRounding.AwayFromZero);
                        int bx = (int)Math.Round(x + shift, MidpointRounding.AwayFromZero);
                        img.R[i] = sr[y * w + Idx(rx, w)];
                        img.B[i] = sb[y * w + Idx(bx, w)];
                    }
            });
        }

        static void Optics(Img img, CameraRawOptics o)
        {
            int w = img.W, h = img.H;
            double profileVignette = o.EnableLensProfile ? o.ProfileVignetting / 100.0 : 0;
            double vignette = o.VignetteAmount + profileVignette * 35.0;
            double k = o.DistortionK();
            if (k != 0) LensDistort(img, k);
            if (o.RemoveChromaticAberration) Chromatic(img, 0.45);
            if (o.PurpleAmount == 0 && o.GreenAmount == 0 && vignette == 0) return;
            double mid = o.VignetteMidpoint;
            InBands(h, delegate(int first, int last)
            {
                for (int y = first; y < last; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int i = y * w + x;
                        if (img.A[i] == 0) continue;
                        double r = img.R[i], g = img.G[i], b = img.B[i];
                        Defringe(ref r, ref g, ref b, o);
                        VignetteCorrect(ref r, ref g, ref b, x, y, w, h, vignette, mid);
                        img.R[i] = (float)C01(r); img.G[i] = (float)C01(g); img.B[i] = (float)C01(b);
                    }
            });
        }

        // ---------- 보정 (adjust_camera_raw_calibration) ----------

        static void Calibrate(Img img, CameraRawCalibration c)
        {
            int v = c.Process;
            double versionScale = v <= 1 ? 0.55 : v == 2 ? 0.65 : v == 3 ? 0.75 : v == 4 ? 0.85 : v == 5 ? 0.92 : 1.0;
            double tint = c.ShadowTint / 100.0 * versionScale;
            double rh = c.RedHue / 100.0 * (15.0 / 360.0) * versionScale;
            double rs = c.RedSaturation / 100.0 * 0.45 * versionScale;
            double gh = c.GreenHue / 100.0 * (15.0 / 360.0) * versionScale;
            double gs = c.GreenSaturation / 100.0 * 0.45 * versionScale;
            double bh = c.BlueHue / 100.0 * (15.0 / 360.0) * versionScale;
            double bs = c.BlueSaturation / 100.0 * 0.45 * versionScale;
            InBands(img.H, delegate(int first, int last)
            {
                for (int i = first * img.W; i < last * img.W; i++)
                {
                    if (img.A[i] == 0) continue;
                    double r = img.R[i], g = img.G[i], b = img.B[i];
                    double h, s, l;
                    RgbToHsl(r, g, b, out h, out s, out l);
                    if (l < 0.35 && tint != 0)
                    {
                        h += tint * 0.06;
                        if (h < 0) h += 1;
                        if (h >= 1) h -= 1;
                    }
                    double maxc = Math.Max(r, Math.Max(g, b)), minc = Math.Min(r, Math.Min(g, b));
                    if (maxc - minc > 1e-5)
                    {
                        if (r >= g && r >= b) { h += rh; s = C01(s * (1 + rs)); }
                        else if (g >= r && g >= b) { h += gh; s = C01(s * (1 + gs)); }
                        else { h += bh; s = C01(s * (1 + bs)); }
                        if (h < 0) h += 1;
                        if (h >= 1) h -= 1;
                    }
                    HslToRgb(h, s, l, out r, out g, out b);
                    img.R[i] = (float)C01(r); img.G[i] = (float)C01(g); img.B[i] = (float)C01(b);
                }
            });
        }

        // ---------- 기하 (CameraRawGeometrySettings.apply) ----------

        // 단위 정사각형 → 사각형 (Heckbert). (0,0)→q[0], (1,0)→q[1], (1,1)→q[2], (0,1)→q[3]
        static double[] SquareToQuad(double[][] q)
        {
            double x0 = q[0][0], y0 = q[0][1], x1 = q[1][0], y1 = q[1][1], x2 = q[2][0], y2 = q[2][1], x3 = q[3][0], y3 = q[3][1];
            double sx = x0 - x1 + x2 - x3, sy = y0 - y1 + y2 - y3;
            double a, b, c, d, e, f, g, h;
            if (Math.Abs(sx) < 1e-12 && Math.Abs(sy) < 1e-12)
            {
                a = x1 - x0; b = x3 - x0; c = x0; d = y1 - y0; e = y3 - y0; f = y0; g = 0; h = 0;
            }
            else
            {
                double dx1 = x1 - x2, dx2 = x3 - x2, dy1 = y1 - y2, dy2 = y3 - y2;
                double det = dx1 * dy2 - dx2 * dy1;
                if (Math.Abs(det) < 1e-12) det = 1e-12;
                g = (sx * dy2 - dx2 * sy) / det;
                h = (dx1 * sy - sx * dy1) / det;
                a = x1 - x0 + g * x1; b = x3 - x0 + h * x3; c = x0;
                d = y1 - y0 + g * y1; e = y3 - y0 + h * y3; f = y0;
            }
            return new[] { a, b, c, d, e, f, g, h, 1 };
        }

        static double[] Invert3(double[] m)
        {
            double a = m[0], b = m[1], c = m[2], d = m[3], e = m[4], f = m[5], g = m[6], h = m[7], i = m[8];
            double A = e * i - f * h, B = -(d * i - f * g), C = d * h - e * g;
            double det = a * A + b * B + c * C;
            if (Math.Abs(det) < 1e-18) return null;
            double k = 1 / det;
            return new[]
            {
                A * k, -(b * i - c * h) * k, (b * f - c * e) * k,
                B * k, (a * i - c * g) * k, -(a * f - c * d) * k,
                C * k, -(a * h - b * g) * k, (a * e - b * d) * k
            };
        }

        // CIPerspectiveTransform: 레이어의 네 모서리를 OutputCorners로 옮긴 그림을 같은 크기 판에 그린다.
        // 결과 픽셀마다 거꾸로 원본 어디인지 찾아 겹선형으로 읽는다. 원본 밖은 투명.
        static void Warp(CameraRawGeometry geo, Canvas32 src, Canvas32 dst)
        {
            int w = src.W, h = src.H;
            var corners = geo.OutputCorners(w, h);
            // 정사각형 (u,v)는 Core Image 좌표(아래가 0)로 원본의 (u·w, v·h). 모서리 순서를 맞춘다
            var quad = new[] { corners[3], corners[2], corners[1], corners[0] };   // 왼아래, 오른아래, 오른위, 왼위
            var inv = Invert3(SquareToQuad(quad));
            var img = Load(src);
            var outImg = new Img { W = w, H = h, N = w * h, R = new float[w * h], G = new float[w * h], B = new float[w * h], A = new byte[w * h] };
            if (inv != null)
            {
                InBands(h, delegate(int first, int last)
                {
                    for (int y = first; y < last; y++)
                    {
                        double Y = h - (y + 0.5);
                        for (int x = 0; x < w; x++)
                        {
                            double X = x + 0.5;
                            double u = inv[0] * X + inv[1] * Y + inv[2];
                            double v = inv[3] * X + inv[4] * Y + inv[5];
                            double z = inv[6] * X + inv[7] * Y + inv[8];
                            int at = y * w + x;
                            if (Math.Abs(z) < 1e-12) continue;
                            u /= z; v /= z;
                            double sx = u * w - 0.5, sy = (h - v * h) - 0.5;
                            if (sx < -1 || sy < -1 || sx > w || sy > h) continue;
                            Sample(img.R, img.G, img.B, img.A, w, h, sx, sy, outImg, at);
                        }
                    }
                });
            }
            if (geo.ConstrainCrop) outImg = FitBounds(outImg);
            for (int i = 0; i < outImg.N; i++)
            {
                int a = outImg.A[i];
                dst.P[i] = a == 0 ? 0 : (a << 24) | (Byte(outImg.R[i]) << 16) | (Byte(outImg.G[i]) << 8) | Byte(outImg.B[i]);
            }
        }

        // 자르기 제한: 빈 가장자리를 잘라 낸 뒤 비율을 지켜 판에 다시 맞춘다
        static Img FitBounds(Img img)
        {
            int w = img.W, h = img.H;
            int left = w, right = 0, top = h, bottom = 0;
            for (int y = 0; y < h; y++)
            {
                int row = y * w, first = 0;
                while (first < w && img.A[row + first] == 0) first++;
                if (first == w) continue;
                int lastX = w;
                while (lastX > first && img.A[row + lastX - 1] == 0) lastX--;
                if (first < left) left = first;
                if (lastX > right) right = lastX;
                if (y < top) top = y;
                bottom = y + 1;
            }
            if (right == 0) return img;
            int cw = right - left, ch = bottom - top;
            if (cw < 1 || ch < 1 || (cw >= w && ch >= h)) return img;
            double scale = Math.Min((double)w / cw, (double)h / ch);
            double dw = cw * scale, dh = ch * scale;
            double ox = (w - dw) / 2, oy = (h - dh) / 2;
            var outImg = new Img { W = w, H = h, N = w * h, R = new float[w * h], G = new float[w * h], B = new float[w * h], A = new byte[w * h] };
            InBands(h, delegate(int first, int last)
            {
                for (int y = first; y < last; y++)
                    for (int x = 0; x < w; x++)
                    {
                        double px = x + 0.5, py = y + 0.5;
                        if (px < ox || py < oy || px > ox + dw || py > oy + dh) continue;
                        double sx = left + (px - ox) / scale - 0.5, sy = top + (py - oy) / scale - 0.5;
                        Sample(img.R, img.G, img.B, img.A, w, h, sx, sy, outImg, y * w + x);
                    }
            });
            return outImg;
        }

        // ---------- 화이트 밸런스 ----------

        // 선형 빛 한 픽셀을 무채색으로 만드는 색온도·색조(Apply가 곱하는 같은 이득으로). 풀 수 없으면 false.
        public static bool Neutralize(double red, double green, double blue, out double temperature, out double tint)
        {
            temperature = tint = 0;
            if (!(red > 1e-4) || !(green > 1e-4) || !(blue > 1e-4)) return false;
            double a1 = CameraRawSettings.TemperatureGain * red;
            double b1 = CameraRawSettings.TintRedBlue * red + CameraRawSettings.TintGreen * green;
            double c1 = green - red;
            double a2 = -CameraRawSettings.TemperatureGain * blue;
            double b2 = CameraRawSettings.TintRedBlue * blue + CameraRawSettings.TintGreen * green;
            double c2 = green - blue;
            double det = a1 * b2 - a2 * b1;
            if (Math.Abs(det) <= 1e-8) return false;
            double warm = (c1 * b2 - c2 * b1) / det, magenta = (a1 * c2 - a2 * c1) / det;
            if (double.IsNaN(warm) || double.IsInfinity(warm) || double.IsNaN(magenta) || double.IsInfinity(magenta)) return false;
            temperature = warm * 100; tint = magenta * 100;
            return true;
        }

        // 자동: 불투명한 픽셀의 평균이 회색이 되게(gray world)
        public static bool AutoBalance(Canvas32 c, out double temperature, out double tint)
        {
            temperature = tint = 0;
            var lin = new double[256];
            for (int v = 0; v < 256; v++) lin[v] = SrgbToLinear(v / 255.0);
            double r = 0, g = 0, b = 0, n = 0;
            foreach (int p in c.P)
            {
                if (((uint)p >> 24) == 0) continue;
                r += lin[(p >> 16) & 255]; g += lin[(p >> 8) & 255]; b += lin[p & 255];
                n++;
            }
            if (n == 0) return false;
            return Neutralize(r / n, g / n, b / n, out temperature, out tint);
        }
    }
}
