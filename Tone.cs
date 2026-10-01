// Money Shot — 레벨·커브·색조/채도·컬러 밸런스·흑백
//
// 계산만 여기 둔다. 창은 ToneSheets.cs.
// 다섯 가지 모두 "원본 → 결과"를 처음부터 다시 만든다. 슬라이더를 끌 때마다 결과 위에 덧칠하면
// 0~255로 잘린 것이 쌓여 계단이 생긴다.
//
// Compositor(MIT, Wonder Assembly LLC)의 Levels·LevelsAutomatic·Curves·HueSaturation·
// ImageAdjustments·AdjustPixels.c(color_balance, black_white)를 옮겼다.
// 원본은 알파를 곱한 RGBA를 풀었다 묶는데, Canvas32는 곱하지 않은 ARGB라 그 단계가 없다.
using System;
using System.Collections.Generic;

namespace MoneyShot
{
    // src를 보고 dst를 쓴다. 둘은 같은 크기다.
    public delegate void ToneFn(Canvas32 src, Canvas32 dst);

    public static class Tone
    {
        // ---------- 공통 ----------

        // 채널별 256칸 표를 그대로 먹인다.
        public static void ApplyLut(Canvas32 src, Canvas32 dst, byte[] r, byte[] g, byte[] b)
        {
            var S = src.P; var D = dst.P;
            for (int i = 0; i < S.Length; i++)
            {
                int p = S[i];
                D[i] = (p & unchecked((int)0xFF000000)) | (r[(p >> 16) & 0xFF] << 16) | (g[(p >> 8) & 0xFF] << 8) | b[p & 0xFF];
            }
        }

        // 선택 영역이 있으면 그 안만 바꾼 것으로 남긴다(경계는 덮개 값만큼 섞는다).
        public static void KeepInside(Canvas32 dst, Canvas32 orig, byte[] cover)
        {
            if (cover == null) return;
            var D = dst.P; var O = orig.P;
            for (int i = 0; i < D.Length; i++)
            {
                int c = cover[i];
                if (c == 255) continue;
                if (c == 0) { D[i] = O[i]; continue; }
                int a = O[i], b = D[i], u = 255 - c;
                D[i] = Canvas32.Pack(
                    (byte)((Canvas32.A(a) * u + Canvas32.A(b) * c + 127) / 255),
                    (byte)((Canvas32.R(a) * u + Canvas32.R(b) * c + 127) / 255),
                    (byte)((Canvas32.G(a) * u + Canvas32.G(b) * c + 127) / 255),
                    (byte)((Canvas32.B(a) * u + Canvas32.B(b) * c + 127) / 255));
            }
        }

        static byte B(double v01) { return (byte)Math.Max(0, Math.Min(255, (int)Math.Round(v01 * 255))); }
        static double Clamp01(double v) { return v < 0 ? 0 : v > 1 ? 1 : v; }

        // 히스토그램 [0]=RGB(세 채널 평균) [1]=R [2]=G [3]=B. 투명한 곳과 덮개 밖은 뺀다.
        public static double[][] Histogram(Canvas32 c, byte[] cover)
        {
            var h = new double[4][];
            for (int k = 0; k < 4; k++) h[k] = new double[256];
            for (int i = 0; i < c.P.Length; i++)
            {
                int p = c.P[i];
                double w = Canvas32.A(p) / 255.0;
                if (cover != null) w *= cover[i] / 255.0;
                if (w <= 0) continue;
                h[1][(p >> 16) & 0xFF] += w;
                h[2][(p >> 8) & 0xFF] += w;
                h[3][p & 0xFF] += w;
            }
            for (int v = 0; v < 256; v++) h[0][v] = (h[1][v] + h[2][v] + h[3][v]) / 3;
            return h;
        }

        // 그리기용 높이 상한. 단색 바탕 하나가 솟아 나머지를 납작하게 만들지 않게,
        // 양 끝을 뺀 칸의 95번째 값의 4배에서 자른다.
        public static double HistogramScale(double[] bins)
        {
            double peak = 0;
            var inner = new List<double>();
            for (int i = 0; i < bins.Length; i++)
            {
                if (bins[i] > peak) peak = bins[i];
                if (i > 0 && i < bins.Length - 1 && bins[i] > 0) inner.Add(bins[i]);
            }
            if (peak <= 0) return 0;
            if (inner.Count == 0) return peak;
            inner.Sort();
            return Math.Min(peak, inner[(int)((inner.Count - 1) * 0.95)] * 4);
        }

        // ---------- 레벨 ----------

        public class LevelRange
        {
            public double Black = 0, Gamma = 1, White = 255, OutBlack = 0, OutWhite = 255;

            public LevelRange Clone() { return (LevelRange)MemberwiseClone(); }
            public bool IsIdentity
            {
                get { return Black == 0 && Gamma == 1 && White == 255 && OutBlack == 0 && OutWhite == 255; }
            }
            public void Normalize()
            {
                Black = Math.Max(0, Math.Min(254, Black));
                White = Math.Max(Black + 1, Math.Min(255, White));
                Gamma = Math.Max(0.1, Math.Min(9.99, Gamma));
                OutBlack = Math.Max(0, Math.Min(255, OutBlack));
                OutWhite = Math.Max(0, Math.Min(255, OutWhite));
            }
            public double Apply(double v01)
            {
                double input = Clamp01((v01 * 255 - Black) / (White - Black));
                return (OutBlack + Math.Pow(input, 1 / Gamma) * (OutWhite - OutBlack)) / 255;
            }
        }

        public class Levels
        {
            public LevelRange[] R = { new LevelRange(), new LevelRange(), new LevelRange(), new LevelRange() };  // RGB,R,G,B
            public bool IsIdentity { get { foreach (var r in R) if (!r.IsIdentity) return false; return true; } }
            public Levels Clone()
            {
                var c = new Levels();
                for (int i = 0; i < 4; i++) c.R[i] = R[i].Clone();
                return c;
            }
            // 채널 각자 먼저, 그 위에 RGB 전체
            public byte[] Lut(int ch)
            {
                var t = new byte[256];
                for (int v = 0; v < 256; v++) t[v] = B(R[0].Apply(R[ch].Apply(v / 255.0)));
                return t;
            }
            public ToneFn Fn()
            {
                var r = Lut(1); var g = Lut(2); var b = Lut(3);
                return delegate(Canvas32 s, Canvas32 d) { ApplyLut(s, d, r, g, b); };
            }
        }

        public enum AutoMode { Contrast, Color, Neutral }

        // 양 끝 0.1%를 버린 범위를 0~255로 편다. 대비는 세 채널을 같은 폭으로(색 관계 유지),
        // 색은 채널마다 따로(색 틀어짐 바로잡기), 중간톤까지는 채널 평균이 한가운데 오도록 감마도 맞춘다.
        public static Levels Auto(double[][] hist, AutoMode mode)
        {
            var result = new Levels();
            if (mode == AutoMode.Contrast)
            {
                double lo = 256, hi = -1;
                for (int c = 1; c <= 3; c++)
                {
                    int l, h;
                    if (!Endpoints(hist[c], out l, out h)) continue;
                    lo = Math.Min(lo, l); hi = Math.Max(hi, h);
                }
                if (lo < hi) { result.R[0].Black = lo; result.R[0].White = hi; }
                return result;
            }
            for (int c = 1; c <= 3; c++)
            {
                int l, h;
                if (!Endpoints(hist[c], out l, out h)) continue;
                var range = new LevelRange { Black = l, White = h };
                if (mode == AutoMode.Neutral)
                {
                    double total = 0, sum = 0;
                    for (int v = 0; v < 256; v++) { total += hist[c][v]; sum += range.Apply(v / 255.0) * hist[c][v]; }
                    double mean = total > 0 ? sum / total : 0;
                    if (mean > 0 && mean < 1) range.Gamma = Math.Min(9.99, Math.Max(0.1, Math.Log(mean) / Math.Log(0.5)));
                }
                result.R[c] = range;
            }
            return result;
        }

        static bool Endpoints(double[] bins, out int low, out int high)
        {
            double total = 0;
            foreach (var b in bins) total += b;
            low = 0; high = 255;
            if (total <= 0) return false;
            double sum = 0;
            for (int i = 0; i < 256; i++) { sum += bins[i]; if (sum > total * 0.001) { low = i; break; } }
            sum = 0;
            for (int i = 255; i >= 0; i--) { sum += bins[i]; if (sum > total * 0.001) { high = i; break; } }
            return low < high;
        }

        // ---------- 커브 ----------

        public class Curves
        {
            // 채널마다 (x, y) 점들. x는 0에서 시작해 255로 끝나고 오름차순.
            public List<double[]>[] P = new List<double[]>[4];

            public Curves() { for (int c = 0; c < 4; c++) P[c] = Straight(); }
            public static List<double[]> Straight() { return new List<double[]> { new double[] { 0, 0 }, new double[] { 255, 255 } }; }

            public Curves Clone()
            {
                var k = new Curves();
                for (int c = 0; c < 4; c++)
                {
                    k.P[c] = new List<double[]>();
                    foreach (var p in P[c]) k.P[c].Add(new[] { p[0], p[1] });
                }
                return k;
            }

            public bool IsIdentity
            {
                get
                {
                    foreach (var pts in P)
                        foreach (var p in pts) if (Math.Abs(p[0] - p[1]) > 0.01) return false;
                    return true;
                }
            }

            // 모양을 지키는 3차 에르미트 보간 — 점 사이에서 넘쳐 출렁이지 않는다.
            public double Value(double x, int ch)
            {
                var p = P[ch];
                int n = p.Count;
                int i = 0;
                for (int k = 0; k < n; k++) if (p[k][0] <= x) i = k;
                i = Math.Min(n - 2, Math.Max(0, i));
                var d = new double[n - 1];
                for (int k = 0; k < n - 1; k++) d[k] = (p[k + 1][1] - p[k][1]) / (p[k + 1][0] - p[k][0]);
                Func<int, double> slope = delegate(int j)
                {
                    if (j == 0) return d[0];
                    if (j == n - 1) return d[n - 2];
                    if (d[j - 1] * d[j] <= 0) return 0;
                    return 2 / (1 / d[j - 1] + 1 / d[j]);
                };
                double h = p[i + 1][0] - p[i][0], t = Clamp01((x - p[i][0]) / h);
                double y = (2 * t * t * t - 3 * t * t + 1) * p[i][1] + (t * t * t - 2 * t * t + t) * h * slope(i)
                         + (-2 * t * t * t + 3 * t * t) * p[i + 1][1] + (t * t * t - t * t) * h * slope(i + 1);
                return Math.Min(255, Math.Max(0, y));
            }

            public byte[] Lut(int ch)
            {
                var t = new byte[256];
                for (int v = 0; v < 256; v++) t[v] = (byte)Math.Round(Value(Value(v, ch), 0));
                return t;
            }

            public ToneFn Fn()
            {
                var r = Lut(1); var g = Lut(2); var b = Lut(3);
                return delegate(Canvas32 s, Canvas32 d) { ApplyLut(s, d, r, g, b); };
            }
        }

        // ---------- 색조/채도 ----------

        // 0 마스터, 1 빨강, 2 노랑, 3 초록, 4 청록, 5 파랑, 6 자홍
        public static readonly string[] RangeNames = { L.T("전체", "Master"), L.T("빨강", "Reds"), L.T("노랑", "Yellows"), L.T("초록", "Greens"), L.T("청록", "Cyans"), L.T("파랑", "Blues"), L.T("자홍", "Magentas") };

        // 범위마다 [바깥 시작, 안쪽 시작, 안쪽 끝, 바깥 끝] (각도)
        static readonly double[][] Bands =
        {
            new double[] { 0, 0, 360, 360 },
            new double[] { 315, 345, 15, 45 },
            new double[] { 15, 45, 75, 105 },
            new double[] { 75, 105, 135, 165 },
            new double[] { 135, 165, 195, 225 },
            new double[] { 195, 225, 255, 285 },
            new double[] { 255, 285, 315, 345 },
        };

        static double Forward(double from, double to)
        {
            double d = (to - from) % 360;
            return d < 0 ? d + 360 : d;
        }

        // 범위가 이 색조를 얼마나 차지하나: 안쪽은 1, 어깨에서 직선으로 줄고, 바깥은 0.
        public static double BandWeight(int range, double hue)
        {
            if (range == 0) return 1;
            var b = Bands[range];
            double span = Forward(b[0], b[3]);
            if (span <= 0) return 1;
            double pos = Forward(b[0], hue);
            if (pos > span) return 0;
            double rampIn = Forward(b[0], b[1]), plateauEnd = Forward(b[0], b[2]);
            if (pos < rampIn) return rampIn > 0 ? pos / rampIn : 1;
            if (pos <= plateauEnd) return 1;
            double rampOut = span - plateauEnd;
            return rampOut > 0 ? (span - pos) / rampOut : 1;
        }

        public class HueSat
        {
            public double[] Hue = new double[7], Sat = new double[7], Light = new double[7];
            public bool Colorize;
            public double CHue = 0, CSat = 25, CLight = 0;     // 색 입히기 (포토샵 시작값)

            public HueSat Clone()
            {
                var c = (HueSat)MemberwiseClone();
                c.Hue = (double[])Hue.Clone(); c.Sat = (double[])Sat.Clone(); c.Light = (double[])Light.Clone();
                return c;
            }
            public bool IsIdentity
            {
                get
                {
                    if (Colorize) return false;
                    for (int i = 0; i < 7; i++) if (Hue[i] != 0 || Sat[i] != 0 || Light[i] != 0) return false;
                    return true;
                }
            }

            // 색조 한 도마다 모든 범위가 얼마씩 미는지 미리 더해 둔다.
            double[,] Response()
            {
                var r = new double[361, 3];
                for (int deg = 0; deg <= 360; deg++)
                    for (int k = 0; k < 7; k++)
                    {
                        if (Hue[k] == 0 && Sat[k] == 0 && Light[k] == 0) continue;
                        double w = BandWeight(k, deg);
                        if (w <= 0) continue;
                        r[deg, 0] += Hue[k] * w; r[deg, 1] += Sat[k] * w; r[deg, 2] += Light[k] * w;
                    }
                return r;
            }

            public void Adjust(double red, double green, double blue, double[,] resp, out double ro, out double go, out double bo)
            {
                double h, s, l;
                ToHsl(red, green, blue, out h, out s, out l);
                double la;
                if (Colorize)
                {
                    h = CHue % 360;
                    s = Clamp01(CSat / 100);
                    la = CLight / 100;
                }
                else
                {
                    int i = Math.Min(360, Math.Max(0, (int)Math.Round(h)));
                    la = resp[i, 2] / 100;
                    h = (h + resp[i, 0]) % 360;
                    if (h < 0) h += 360;
                    s = AdjustSat(s, resp[i, 1]);
                }
                // 밝기는 +면 흰색 쪽으로, -면 검정 쪽으로. ±100에서 끝까지 간다.
                double amt = Math.Min(1, Math.Max(-1, la));
                l = amt >= 0 ? l + (1 - l) * amt : l * (1 + amt);
                ToRgb(h, s, Clamp01(l), out ro, out go, out bo);
            }

            // 포토샵식 채도: 0 아래는 회색 쪽으로 곱하고, 위는 남은 만큼으로 나눈다(+50이면 두 배).
            // 둘 다 곱셈이라 회색은 회색으로 남는다.
            static double AdjustSat(double s, double amount)
            {
                double a = Math.Min(1, Math.Max(-1, amount / 100));
                if (a <= 0) return Math.Max(0, s * (1 + a));
                return a >= 1 ? (s > 0 ? 1 : 0) : Math.Min(1, s / (1 - a));
            }

            const int Dim = 33;     // 표 한 변. 만들기 빠르고 충분히 매끄럽다

            // 33³ 표를 만들고 세 방향 선형 보간으로 찾는다. 픽셀마다 HSL을 오가는 것보다 훨씬 빠르다.
            public ToneFn Fn()
            {
                var resp = Response();
                var cube = new float[Dim * Dim * Dim * 3];
                double step = Dim - 1;
                int idx = 0;
                for (int bb = 0; bb < Dim; bb++)
                    for (int gg = 0; gg < Dim; gg++)
                        for (int rr = 0; rr < Dim; rr++)
                        {
                            double ro, go, bo;
                            Adjust(rr / step, gg / step, bb / step, resp, out ro, out go, out bo);
                            cube[idx++] = (float)ro; cube[idx++] = (float)go; cube[idx++] = (float)bo;
                        }
                return delegate(Canvas32 src, Canvas32 dst) { ApplyCube(src, dst, cube, Dim); };
            }
        }

        public static void ApplyCube(Canvas32 src, Canvas32 dst, float[] cube, int dim)
        {
            var S = src.P; var D = dst.P;
            float sc = (dim - 1) / 255f;
            int dd = dim * dim;
            var outc = new int[3];
            for (int i = 0; i < S.Length; i++)
            {
                int p = S[i];
                float fr = ((p >> 16) & 0xFF) * sc, fg = ((p >> 8) & 0xFF) * sc, fb = (p & 0xFF) * sc;
                int r0 = Math.Min(dim - 2, (int)fr), g0 = Math.Min(dim - 2, (int)fg), b0 = Math.Min(dim - 2, (int)fb);
                float tr = fr - r0, tg = fg - g0, tb = fb - b0;
                int bas = (b0 * dd + g0 * dim + r0) * 3;
                int dr = 3, dg = dim * 3, db = dd * 3;
                for (int c = 0; c < 3; c++)
                {
                    int k = bas + c;
                    float c00 = cube[k] + (cube[k + dr] - cube[k]) * tr;
                    float c10 = cube[k + dg] + (cube[k + dg + dr] - cube[k + dg]) * tr;
                    float c01 = cube[k + db] + (cube[k + db + dr] - cube[k + db]) * tr;
                    float c11 = cube[k + db + dg] + (cube[k + db + dg + dr] - cube[k + db + dg]) * tr;
                    float c0 = c00 + (c10 - c00) * tg, c1 = c01 + (c11 - c01) * tg;
                    float v = c0 + (c1 - c0) * tb;
                    outc[c] = Math.Max(0, Math.Min(255, (int)(v * 255 + 0.5f)));
                }
                D[i] = (p & unchecked((int)0xFF000000)) | (outc[0] << 16) | (outc[1] << 8) | outc[2];
            }
        }

        static void ToHsl(double r, double g, double b, out double h, out double s, out double l)
        {
            double hi = Math.Max(r, Math.Max(g, b)), lo = Math.Min(r, Math.Min(g, b));
            l = (hi + lo) / 2;
            double d = hi - lo;
            if (d <= 0) { h = 0; s = 0; return; }
            s = Math.Min(1, d / (1 - Math.Abs(2 * l - 1)));
            if (hi == r) h = (g - b) / d;
            else if (hi == g) h = (b - r) / d + 2;
            else h = (r - g) / d + 4;
            h *= 60;
            if (h < 0) h += 360;
        }

        public static void ToRgb(double h, double s, double l, out double r, out double g, out double b)
        {
            if (s <= 0) { r = g = b = l; return; }
            double chroma = (1 - Math.Abs(2 * l - 1)) * s;
            double sector = h / 60;
            double second = chroma * (1 - Math.Abs(sector % 2 - 1));
            double m = l - chroma / 2;
            double r1, g1, b1;
            switch ((int)sector)
            {
                case 0: r1 = chroma; g1 = second; b1 = 0; break;
                case 1: r1 = second; g1 = chroma; b1 = 0; break;
                case 2: r1 = 0; g1 = chroma; b1 = second; break;
                case 3: r1 = 0; g1 = second; b1 = chroma; break;
                case 4: r1 = second; g1 = 0; b1 = chroma; break;
                default: r1 = chroma; g1 = 0; b1 = second; break;
            }
            r = Clamp01(r1 + m); g = Clamp01(g1 + m); b = Clamp01(b1 + m);
        }

        // ---------- 컬러 밸런스 ----------

        public class Balance
        {
            // [톤 0 어두운 쪽 / 1 중간 / 2 밝은 쪽][축 0 청록↔빨강 / 1 자홍↔초록 / 2 노랑↔파랑], -100~100
            public double[,] V = new double[3, 3];
            public bool KeepLuminosity = true;

            public Balance Clone() { var c = (Balance)MemberwiseClone(); c.V = (double[,])V.Clone(); return c; }
            public bool IsIdentity
            {
                get { foreach (var v in V) if (v != 0) return false; return true; }
            }

            // 어두운 쪽·중간·밝은 쪽이 겹쳐 합이 대략 1이 되는 세 곡선. 문턱에서 띠가 생기지 않는다.
            static void Weights(double v, out double sh, out double mid, out double hi)
            {
                const double a = 0.25, b = 0.333, scale = 0.7;
                double s = Clamp01((v - b) / -a + 0.5);
                double h = Clamp01((v + b - 1.0) / a + 0.5);
                double m1 = Clamp01((v - b) / a + 0.5);
                double m2 = Clamp01((v + b - 1.0) / -a + 0.5);
                sh = s * scale; mid = m1 * m2 * scale; hi = h * scale;
            }

            public ToneFn Fn()
            {
                var s = this.Clone();
                return delegate(Canvas32 src, Canvas32 dst)
                {
                    // 채널 값마다 결과가 정해지니(밝기 보존 전까지) 채널별 256칸 표로 만든다.
                    var lut = new double[3, 256];
                    for (int c = 0; c < 3; c++)
                        for (int v = 0; v < 256; v++)
                        {
                            double x = v / 255.0, ws, wm, wh;
                            Weights(x, out ws, out wm, out wh);
                            lut[c, v] = Clamp01(x + s.V[0, c] / 100 * ws + s.V[1, c] / 100 * wm + s.V[2, c] / 100 * wh);
                        }
                    var S = src.P; var D = dst.P;
                    for (int i = 0; i < S.Length; i++)
                    {
                        int p = S[i];
                        int ir = (p >> 16) & 0xFF, ig = (p >> 8) & 0xFF, ib = p & 0xFF;
                        double r = lut[0, ir], g = lut[1, ig], b = lut[2, ib];
                        if (s.KeepLuminosity)
                        {
                            double before = (0.299 * ir + 0.587 * ig + 0.114 * ib) / 255.0;
                            double after = 0.299 * r + 0.587 * g + 0.114 * b;
                            if (after > 0.0001)
                            {
                                double k = before / after;
                                r = Clamp01(r * k); g = Clamp01(g * k); b = Clamp01(b * k);
                            }
                        }
                        D[i] = (p & unchecked((int)0xFF000000)) | (B(r) << 16) | (B(g) << 8) | B(b);
                    }
                };
            }
        }

        // ---------- 흑백 ----------

        public static readonly string[] BwNames = { L.T("빨강", "Reds"), L.T("노랑", "Yellows"), L.T("초록", "Greens"), L.T("청록", "Cyans"), L.T("파랑", "Blues"), L.T("자홍", "Magentas") };

        public class BlackWhite
        {
            // 빨강·노랑·초록·청록·파랑·자홍이 회색으로 바뀔 때 얼마나 밝게 (-200~300, 포토샵 기본값)
            public double[] W = { 40, 60, 40, 60, 20, 80 };
            public bool Tint;
            public double TintHue = 40, TintSat = 20;

            public BlackWhite Clone() { var c = (BlackWhite)MemberwiseClone(); c.W = (double[])W.Clone(); return c; }

            public ToneFn Fn()
            {
                var s = this.Clone();
                var w = new double[6];
                for (int i = 0; i < 6; i++) w[i] = s.W[i] / 100;
                return delegate(Canvas32 src, Canvas32 dst)
                {
                    var S = src.P; var D = dst.P;
                    for (int i = 0; i < S.Length; i++)
                    {
                        int p = S[i];
                        double r = ((p >> 16) & 0xFF) / 255.0, g = ((p >> 8) & 0xFF) / 255.0, b = (p & 0xFF) / 255.0;
                        double mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b));
                        double md = r + g + b - mx - mn;
                        int primary, secondary;
                        if (mx == r) { primary = 0; secondary = g >= b ? 1 : 5; }
                        else if (mx == g) { primary = 2; secondary = r >= b ? 1 : 3; }
                        else { primary = 4; secondary = g >= r ? 3 : 5; }
                        double gray = Clamp01(mn + (md - mn) * w[secondary] + (mx - md) * w[primary]);
                        double or = gray, og = gray, ob = gray;
                        if (s.Tint && s.TintSat > 0)
                        {
                            // 회색이 고른 색조의 밝기가 된다 — 세피아·청사진
                            double c = (1.0 - Math.Abs(2.0 * gray - 1.0)) * (s.TintSat / 100);
                            double hp = (s.TintHue % 360.0) / 60.0;
                            double xx = c * (1.0 - Math.Abs(hp % 2.0 - 1.0));
                            double r1 = 0, g1 = 0, b1 = 0;
                            if (hp < 1) { r1 = c; g1 = xx; }
                            else if (hp < 2) { r1 = xx; g1 = c; }
                            else if (hp < 3) { g1 = c; b1 = xx; }
                            else if (hp < 4) { g1 = xx; b1 = c; }
                            else if (hp < 5) { r1 = xx; b1 = c; }
                            else { r1 = c; b1 = xx; }
                            double m = gray - c / 2.0;
                            or = Clamp01(r1 + m); og = Clamp01(g1 + m); ob = Clamp01(b1 + m);
                        }
                        D[i] = (p & unchecked((int)0xFF000000)) | (B(or) << 16) | (B(og) << 8) | B(ob);
                    }
                };
            }
        }
    }
}
