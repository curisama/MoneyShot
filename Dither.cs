// Money Shot — 디더링 필터
//
// Compositor(MIT, Wonder Assembly LLC)의 Dither.swift·DitherPixels.c를 옮겼다.
// 원본은 알파를 곱해 둔(premultiplied) RGBA 위에서 돌지만 Canvas32는 곱하지 않은 ARGB라
// 알파로 나눴다 다시 곱하는 단계가 통째로 빠진다. 알파는 그대로 둔다.
// Core Image·CoreText가 하던 일은 이렇게 바꿨다:
//   · 굵은 픽셀 만들기(작게 그렸다 키우기) → 블록마다 정확한 평균
//   · 주사선 번짐의 가우시안 블러 → 줄여서 상자 블러 세 번 뒤 다시 키우기
//   · 아스키 글자 지도 → System.Drawing으로 Consolas 굵게 찍어 회색 지도로
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using D = System.Drawing;

namespace MoneyShot
{
    // 순서가 곧 원본 C 코드의 스타일 번호다. 바꾸지 말 것.
    public enum DitherStyle { Atkinson, FloydSteinberg, Bayer2, Bayer4, Bayer8, Dots, Lines, Diamonds, Patterns, Ascii, Scanlines }
    public enum DitherPixelShape { Square, Dot }
    public enum DitherColors { BlackWhite, TwoColors, Original }

    public class DitherSettings
    {
        public const string DefaultCharacters = " .:-=+*#%@";

        public DitherStyle Style = DitherStyle.Atkinson;
        // 디더 한 점이 레이어 픽셀 몇 개 너비인가 — 옛 화면의 굵은 픽셀
        public double PixelSize = 2;
        public DitherPixelShape PixelShape = DitherPixelShape.Square;
        // 망점 칸 크기
        public double CellSize = 8;
        // 아스키 한 줄 높이. 글자 너비는 대략 그 6할
        public double TextSize = 14;
        // 주사선: 줄 사이 간격(px), 번짐·구슬(%), 흔들림(px)
        public double LineSpacing = 4;
        public double Glow = 35;
        public double Dots = 0;
        public double Wobble = 0;
        // 망점 각도(도)
        public double Angle = 45;
        // 채널당 톤 수. 2면 1비트
        public double Levels = 2;
        // 오차를 얼마나 넘길지(%). 줄이면 면이 납작해진다
        public double Diffusion = 100;
        // −100~100. 디더 전에 잉크를 더(어둡게)/덜, 대비를 낮게/높게
        public double Density = 0;
        public double Contrast = 0;
        public DitherColors Colors = DitherColors.BlackWhite;
        // 0xRRGGBB
        public int Dark = 0x000000;
        public int Light = 0xFFFFFF;
        // 무늬가 밝은 톤을 맡아 어두운 바탕 위에 밝은 색으로 — 검은 화면에 빛나는 점. 망점·무늬·아스키에만 듣는다
        public bool LightOnDark = true;
        // 순서는 상관없다. 잉크 양으로 다시 줄 세운다
        public string Characters = DefaultCharacters;

        public DitherSettings Clone() { return (DitherSettings)MemberwiseClone(); }

        static double Clamp(double v, double lo, double hi, double fallback)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return fallback;
            return v < lo ? lo : v > hi ? hi : v;
        }
        static double Rnd(double v) { return Math.Round(v, MidpointRounding.AwayFromZero); }

        public DitherSettings Normalized()
        {
            var r = Clone();
            r.PixelSize = Rnd(Clamp(PixelSize, 1, 32, 2));
            r.CellSize = Rnd(Clamp(CellSize, 4, 64, 8));
            r.TextSize = Rnd(Clamp(TextSize, 6, 64, 14));
            r.LineSpacing = Rnd(Clamp(LineSpacing, 2, 32, 4));
            r.Glow = Clamp(Glow, 0, 100, 35);
            r.Dots = Clamp(Dots, 0, 100, 0);
            r.Wobble = Clamp(Wobble, 0, 64, 0);
            r.Angle = Clamp(Angle, -90, 90, 45);
            r.Levels = Rnd(Clamp(Levels, 2, 8, 2));
            r.Diffusion = Clamp(Diffusion, 0, 100, 100);
            r.Density = Clamp(Density, -100, 100, 0);
            r.Contrast = Clamp(Contrast, -100, 100, 0);
            r.Dark &= 0xFFFFFF;
            r.Light &= 0xFFFFFF;
            // 줄바꿈은 글자가 아니다. 64자까지만
            string c = (Characters ?? "").Replace("\r", "").Replace("\n", "");
            if (c.Length > 64) c = c.Substring(0, 64);
            r.Characters = c;
            return r;
        }
    }

    public static class Dither
    {
        // ---------- 스타일 묶음 ----------

        // 오차 확산: 픽셀마다 반올림 오차를 이웃에 넘긴다
        public static bool Diffuses(DitherStyle s) { return s <= DitherStyle.FloydSteinberg; }
        // 확산·격자 방식은 톤 수로 양자화하고, 나머지는 두 톤으로 무늬를 찍는다
        public static bool HasTones(DitherStyle s) { return s <= DitherStyle.Bayer8; }
        public static bool IsHalftone(DitherStyle s) { return s >= DitherStyle.Dots && s <= DitherStyle.Diamonds; }
        // 무늬가 어느 쪽 톤을 맡는지가 의미 있는 스타일
        public static bool DrawsMarks(DitherStyle s) { return !HasTones(s) && s != DitherStyle.Scanlines; }
        // 아스키 글자와 주사선은 원래 해상도로 그린다. 먼저 줄이면 뭉개지고 끊긴다
        public static bool UsesPixelSize(DitherStyle s) { return s != DitherStyle.Ascii && s != DitherStyle.Scanlines; }

        // ---------- 진입점 ----------

        // src는 건드리지 않는다. dst는 src와 같은 크기.
        public static void Apply(DitherSettings settings, Canvas32 src, Canvas32 dst)
        {
            var s = settings.Normalized();
            int w = src.W, h = src.H;
            if (w <= 0 || h <= 0) return;
            int block = UsesPixelSize(s.Style) ? (int)s.PixelSize : 1;

            // 굵은 픽셀: 블록 평균으로 줄인 사본을 디더한 뒤 매끈하게 하지 않고 다시 키운다
            Canvas32 work;
            if (block > 1) work = Shrink(src, block);
            else { Array.Copy(src.P, dst.P, src.P.Length); work = dst; }

            Run(s, work);

            if (s.Style == DitherStyle.Scanlines && s.Glow > 0) { Glowing(s, work); return; }
            if (block <= 1) return;
            // 점 사이 틈은 어두운 색: 고른 색이 있으면 그것, 아니면 검정
            int gap = s.Colors == DitherColors.TwoColors ? s.Dark : 0;
            Expand(work, dst, block, s.PixelShape == DitherPixelShape.Dot, gap);
        }

        // ---------- 공통 ----------

        static float Clamp01(float v) { return v < 0 ? 0 : v > 1 ? 1 : v; }

        // lroundf: 반은 0에서 먼 쪽으로
        static int LRound(float v) { return v >= 0 ? (int)(v + 0.5f) : -(int)(-v + 0.5f); }

        static int Byte(float v) { return (int)(Clamp01(v) * 255f + 0.5f); }

        static int Pack(int a, float r, float g, float b)
        {
            return (a << 24) | (Byte(r) << 16) | (Byte(g) << 8) | Byte(b);
        }

        // 원본의 in_bands: 일을 32토막쯤 내서 코어에 나눠 준다
        static void InBands(int count, Action<int, int> body)
        {
            int bands = count < 64 ? 1 : 32, size = (count + bands - 1) / bands;
            if (bands == 1) { body(0, count); return; }
            Parallel.For(0, bands, delegate(int band)
            {
                int start = band * size, end = Math.Min(start + size, count);
                if (start < end) body(start, end);
            });
        }

        // 농도는 감마로 민다(검정·흰색은 제자리). 대비는 중간 회색을 축으로.
        static float AdjustTone(float v, float gamma, float contrast)
        {
            v = (float)Math.Pow(Clamp01(v), gamma);
            return Clamp01((v - 0.5f) * contrast + 0.5f);
        }

        // ---------- 굵은 픽셀 ----------

        // 블록마다 알파 가중 평균. 가장자리 블록은 안에 든 픽셀만 센다.
        static Canvas32 Shrink(Canvas32 src, int block)
        {
            int w = src.W, h = src.H;
            int sw = (w + block - 1) / block, sh = (h + block - 1) / block;
            var small = new Canvas32(sw, sh);
            InBands(sh, delegate(int first, int last)
            {
                for (int sy = first; sy < last; sy++)
                {
                    int y0 = sy * block, y1 = Math.Min(y0 + block, h);
                    for (int sx = 0; sx < sw; sx++)
                    {
                        int x0 = sx * block, x1 = Math.Min(x0 + block, w);
                        long sa = 0, sr = 0, sg = 0, sb = 0; int n = 0;
                        for (int y = y0; y < y1; y++)
                        {
                            int row = y * w;
                            for (int x = x0; x < x1; x++)
                            {
                                int c = src.P[row + x];
                                int a = (int)((uint)c >> 24);
                                sa += a;
                                sr += a * ((c >> 16) & 255); sg += a * ((c >> 8) & 255); sb += a * (c & 255);
                                n++;
                            }
                        }
                        int v = 0;
                        if (sa > 0)
                        {
                            int A = (int)((sa + n / 2) / n);
                            int R = (int)((sr + sa / 2) / sa), G = (int)((sg + sa / 2) / sa), B = (int)((sb + sa / 2) / sa);
                            v = (A << 24) | (R << 16) | (G << 8) | B;
                        }
                        small.P[sy * sw + sx] = v;
                    }
                }
            });
            return small;
        }

        // 다시 키우기 + dither_dots: 굵은 픽셀을 제 색의 둥근 점으로, 틈은 gap 색으로 (도트 매트릭스 화면처럼)
        static void Expand(Canvas32 small, Canvas32 dst, int block, bool dots, int gap)
        {
            int w = dst.W, h = dst.H, sw = small.W;
            // 덮임 정도는 블록 안 위치로만 정해지니 한 번만 계산한다
            float[] cover = null;
            if (dots)
            {
                cover = new float[block * block];
                float radius = block * 0.42f, middle = block / 2f;
                for (int yy = 0; yy < block; yy++)
                    for (int xx = 0; xx < block; xx++)
                    {
                        float dx = xx + 0.5f - middle, dy = yy + 0.5f - middle;
                        cover[yy * block + xx] = Clamp01(radius - (float)Math.Sqrt(dx * dx + dy * dy) + 0.5f);
                    }
            }
            int gr = (gap >> 16) & 255, gg = (gap >> 8) & 255, gb = gap & 255;
            InBands(h, delegate(int first, int last)
            {
                for (int y = first; y < last; y++)
                {
                    int srow = (y / block) * sw, row = y * w, cy = (y % block) * block;
                    for (int x = 0; x < w; x++)
                    {
                        int c = small.P[srow + x / block];
                        if (dots && ((uint)c >> 24) != 0)
                        {
                            float k = cover[cy + x % block];
                            if (k < 1)
                            {
                                float j = 1 - k;
                                int r = (int)(((c >> 16) & 255) * k + gr * j + 0.5f);
                                int g = (int)(((c >> 8) & 255) * k + gg * j + 0.5f);
                                int b = (int)((c & 255) * k + gb * j + 0.5f);
                                c = (int)((uint)c & 0xFF000000u) | (r << 16) | (g << 8) | b;
                            }
                        }
                        dst.P[row + x] = c;
                    }
                }
            });
        }

        // ---------- dither_apply ----------

        struct Tap { public int Dx, Dy, Weight; public Tap(int dx, int dy, int w) { Dx = dx; Dy = dy; Weight = w; } }

        static readonly Tap[] AtkinsonTaps = { new Tap(1, 0, 1), new Tap(2, 0, 1), new Tap(-1, 1, 1), new Tap(0, 1, 1), new Tap(1, 1, 1), new Tap(0, 2, 1) };
        static readonly Tap[] FloydTaps = { new Tap(1, 0, 7), new Tap(-1, 1, 3), new Tap(0, 1, 5), new Tap(1, 1, 1) };

        static float Quantize(float v, int levels)
        {
            float steps = levels - 1;
            return (float)Math.Floor(Clamp01(v) * steps + 0.5f) / steps;
        }

        // 한 판씩 지그재그로 훑는다. 오차가 한쪽으로 흘러 줄무늬가 지지 않게.
        // 앳킨슨은 오차의 6/8만 넘긴다 — 옛 맥 특유의 또렷하고 센 맛이 거기서 나온다.
        static void Diffuse(float[] plane, int off, byte[] alpha, int width, int height, DitherStyle style, int levels, float diffusion)
        {
            Tap[] taps = style == DitherStyle.Atkinson ? AtkinsonTaps : FloydTaps;
            float divisor = style == DitherStyle.Atkinson ? 8 : 16;
            for (int y = 0; y < height; y++)
            {
                bool reverse = (y & 1) != 0;
                for (int i = 0; i < width; i++)
                {
                    int x = reverse ? width - 1 - i : i;
                    int at = y * width + x;
                    if (alpha[at] == 0) continue;
                    float old = plane[off + at], q = Quantize(old, levels);
                    plane[off + at] = q;
                    float error = (old - q) * diffusion / divisor;
                    for (int t = 0; t < taps.Length; t++)
                    {
                        int nx = x + (reverse ? -taps[t].Dx : taps[t].Dx), ny = y + taps[t].Dy;
                        if (nx < 0 || nx >= width || ny >= height) continue;
                        plane[off + ny * width + nx] += error * taps[t].Weight;
                    }
                }
            }
        }

        static readonly byte[] Bayer8 =
        {
             0, 32,  8, 40,  2, 34, 10, 42, 48, 16, 56, 24, 50, 18, 58, 26,
            12, 44,  4, 36, 14, 46,  6, 38, 60, 28, 52, 20, 62, 30, 54, 22,
             3, 35, 11, 43,  1, 33,  9, 41, 51, 19, 59, 27, 49, 17, 57, 25,
            15, 47,  7, 39, 13, 45,  5, 37, 63, 31, 55, 23, 61, 29, 53, 21,
        };
        static readonly byte[] Bayer2 = { 0, 2, 3, 1 };
        static readonly byte[] Bayer4 = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

        // 격자 문턱값 [0,1)
        static float OrderedThreshold(DitherStyle style, int x, int y)
        {
            switch (style)
            {
                case DitherStyle.Bayer2: return (Bayer2[(y & 1) * 2 + (x & 1)] + 0.5f) / 4;
                case DitherStyle.Bayer4: return (Bayer4[(y & 3) * 4 + (x & 3)] + 0.5f) / 16;
                default: return (Bayer8[(y & 7) * 8 + (x & 7)] + 0.5f) / 64;
            }
        }

        static float Ordered(float v, float threshold, int levels)
        {
            float steps = levels - 1;
            float q = (float)Math.Floor(Clamp01(v) * steps + threshold);
            return (q > steps ? steps : q) / steps;
        }

        // 망점 칸 안의 한 점이 칠해지려면 덮임이 얼마나 돼야 하나. u, v는 칸을 −0.5~0.5로 가로지른다.
        static float Spot(DitherStyle style, float u, float v)
        {
            float au = Math.Abs(u), av = Math.Abs(v);
            switch (style)
            {
                case DitherStyle.Dots: return 3.14159265f * (u * u + v * v);
                case DitherStyle.Lines: return av * 2;
                default: return au + av;
            }
        }

        // 옛 맥 채우기 무늬 8×8. 한 줄 한 바이트, 왼쪽 픽셀이 최상위 비트. 성긴 것부터.
        static readonly byte[][] Patterns =
        {
            new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 },
            new byte[] { 0x80, 0x00, 0x00, 0x00, 0x08, 0x00, 0x00, 0x00 },
            new byte[] { 0x88, 0x00, 0x22, 0x00, 0x88, 0x00, 0x22, 0x00 },
            new byte[] { 0x80, 0x40, 0x20, 0x10, 0x08, 0x04, 0x02, 0x01 },
            new byte[] { 0x88, 0x22, 0x88, 0x22, 0x88, 0x22, 0x88, 0x22 },
            new byte[] { 0x00, 0xFF, 0x00, 0x00, 0x00, 0xFF, 0x00, 0x00 },
            new byte[] { 0x11, 0x22, 0x44, 0x88, 0x11, 0x22, 0x44, 0x88 },
            new byte[] { 0xAA, 0x00, 0xAA, 0x00, 0xAA, 0x00, 0xAA, 0x00 },
            new byte[] { 0x88, 0x55, 0x22, 0x55, 0x88, 0x55, 0x22, 0x55 },
            new byte[] { 0xFF, 0x80, 0x80, 0x80, 0xFF, 0x08, 0x08, 0x08 },
            new byte[] { 0xAA, 0x55, 0xAA, 0x55, 0xAA, 0x55, 0xAA, 0x55 },
            new byte[] { 0x81, 0x42, 0x24, 0x18, 0x18, 0x24, 0x42, 0x81 },
            new byte[] { 0x77, 0xAA, 0xDD, 0xAA, 0x77, 0xAA, 0xDD, 0xAA },
            new byte[] { 0xEE, 0xDD, 0xBB, 0x77, 0xEE, 0xDD, 0xBB, 0x77 },
            new byte[] { 0x77, 0xFF, 0xDD, 0xFF, 0x77, 0xFF, 0xDD, 0xFF },
            new byte[] { 0x7F, 0xFF, 0xFF, 0xFF, 0xF7, 0xFF, 0xFF, 0xFF },
            new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF },
        };

        // 제자리에서 디더한다. 알파는 그대로, 완전히 투명한 픽셀은 손대지 않는다.
        static void Run(DitherSettings s, Canvas32 img)
        {
            int width = img.W, height = img.H, count = width * height;
            int[] P = img.P;
            bool original = s.Colors == DitherColors.Original;
            int planes = original ? 3 : 1;
            var tone = new float[count * planes];
            var alpha = new byte[count];

            float gamma = (float)Math.Pow(2, s.Density / 100 * 1.5);
            float c100 = (float)(s.Contrast / 100);
            float contrast = c100 >= 0 ? 1f / (1f - 0.95f * c100) : 1f + c100;
            // 원래 색 모드는 채널값이 바이트뿐이라 표 한 장이면 된다
            var lut = new float[256];
            for (int i = 0; i < 256; i++) lut[i] = AdjustTone(i / 255f, gamma, contrast);
            bool flat = gamma == 1f;

            InBands(height, delegate(int first, int last)
            {
                for (int y = first; y < last; y++)
                    for (int x = 0; x < width; x++)
                    {
                        int at = y * width + x, c = P[at];
                        alpha[at] = (byte)((uint)c >> 24);
                        int R = (c >> 16) & 255, G = (c >> 8) & 255, B = c & 255;
                        if (alpha[at] == 0) R = G = B = 0;
                        if (original)
                        {
                            tone[at] = lut[R]; tone[count + at] = lut[G]; tone[2 * count + at] = lut[B];
                        }
                        else
                        {
                            float l = (0.2126f * R + 0.7152f * G + 0.0722f * B) / 255f;
                            tone[at] = flat ? Clamp01((Clamp01(l) - 0.5f) * contrast + 0.5f) : AdjustTone(l, gamma, contrast);
                        }
                    }
            });

            float[] dark = { ((s.Dark >> 16) & 255) / 255f, ((s.Dark >> 8) & 255) / 255f, (s.Dark & 255) / 255f };
            float[] light = { ((s.Light >> 16) & 255) / 255f, ((s.Light >> 8) & 255) / 255f, (s.Light & 255) / 255f };
            if (s.Colors != DitherColors.TwoColors) { dark = new float[] { 0, 0, 0 }; light = new float[] { 1, 1, 1 }; }
            DitherStyle style = s.Style;
            int levels = Math.Max(2, Math.Min(16, (int)s.Levels));

            if (style <= DitherStyle.Bayer8)
            {
                // 확산·격자: 판마다 톤 수로 양자화한 뒤 색으로 바꾼다
                if (style <= DitherStyle.FloydSteinberg)
                {
                    float diffusion = (float)(s.Diffusion / 100);
                    // 판끼리는 서로 모르니 동시에 돌려도 결과가 같다
                    Parallel.For(0, planes, delegate(int c) { Diffuse(tone, c * count, alpha, width, height, style, levels, diffusion); });
                }
                else
                {
                    InBands(height, delegate(int first, int last)
                    {
                        for (int c = 0; c < planes; c++)
                        {
                            int off = c * count;
                            for (int y = first; y < last; y++)
                                for (int x = 0; x < width; x++)
                                {
                                    int at = y * width + x;
                                    if (alpha[at] != 0) tone[off + at] = Ordered(tone[off + at], OrderedThreshold(style, x, y), levels);
                                }
                        }
                    });
                }
                InBands(height, delegate(int first, int last)
                {
                    for (int at = first * width; at < last * width; at++)
                    {
                        int a = alpha[at];
                        if (a == 0) continue;
                        if (original) P[at] = Pack(a, tone[at], tone[count + at], tone[2 * count + at]);
                        else
                        {
                            float t = tone[at];
                            P[at] = Pack(a, dark[0] + (light[0] - dark[0]) * t, dark[1] + (light[1] - dark[1]) * t, dark[2] + (light[2] - dark[2]) * t);
                        }
                    }
                });
            }
            else if (style == DitherStyle.Scanlines)
            {
                // 브라운관: 줄마다 덮는 행들의 평균으로 그림을 훑는다. 밝은 곳에선 빔이 더 밝고 굵게 번지고,
                // 줄 사이 화면은 어둡게 남는다.
                int spacing = Math.Max(2, (int)s.LineSpacing);
                float middle = spacing / 2f, dots = Clamp01((float)(s.Dots / 100));
                float wobble = (float)s.Wobble;
                int lines = (height + spacing - 1) / spacing;
                float[] screen = dark, phosphor = light;
                InBands(lines, delegate(int firstLine, int lastLine)
                {
                    var scan = new float[width * planes];
                    var sum = new float[3];
                    for (int line = firstLine; line < lastLine; line++)
                    {
                        int top = line * spacing, bottom = Math.Min(top + spacing, height);
                        // 흔들림: 느린 물결에 빠른 물결을 얹어 줄을 옆으로 민다 — 동기가 흔들리는 브라운관처럼
                        float wave = (float)(Math.Sin(line * 0.45) * 0.7 + Math.Sin(line * 1.7 + 1.3) * 0.3);
                        int shift = LRound(wobble * wave);
                        for (int x = 0; x < width; x++)
                        {
                            sum[0] = sum[1] = sum[2] = 0; int n = 0;
                            int sx = x - shift;
                            if (sx >= 0 && sx < width)
                                for (int y = top; y < bottom; y++)
                                {
                                    int at = y * width + sx;
                                    if (alpha[at] == 0) continue;
                                    for (int c = 0; c < planes; c++) sum[c] += tone[c * count + at];
                                    n++;
                                }
                            for (int c = 0; c < planes; c++) scan[c * width + x] = n > 0 ? sum[c] / n : 0;
                        }
                        for (int y = top; y < bottom; y++)
                        {
                            float offset = Math.Abs((y - top) + 0.5f - middle);
                            for (int x = 0; x < width; x++)
                            {
                                int a = alpha[y * width + x];
                                if (a == 0) continue;
                                // 구슬: 줄이 칸마다 한 알씩 끊기고, 알마다 가운데 색으로 빛난다
                                float along = ((x + 0.5f) % spacing) - middle;
                                int centered = LRound(x - along * dots);
                                int at = centered < 0 ? 0 : centered >= width ? width - 1 : centered;
                                float r, g, b, t;
                                if (original)
                                {
                                    r = scan[at]; g = scan[width + at]; b = scan[2 * width + at];
                                    t = 0.2126f * r + 0.7152f * g + 0.0722f * b;
                                }
                                else
                                {
                                    t = scan[at];
                                    r = screen[0] + (phosphor[0] - screen[0]) * t;
                                    g = screen[1] + (phosphor[1] - screen[1]) * t;
                                    b = screen[2] + (phosphor[2] - screen[2]) * t;
                                }
                                // 줄 사이가 어두운 만큼 빔을 그림보다 세게 쏜다
                                r *= 1.35f; g *= 1.35f; b *= 1.35f;
                                // 빔 높이의 절반: 어두운 데선 가늘게, 밝은 데선 거의 꽉 차게. 그래도 줄 사이는 늘 남는다
                                float beam = middle * (0.2f + 0.5f * (float)Math.Sqrt(Clamp01(t)));
                                float across = along * dots, distance = (float)Math.Sqrt(offset * offset + across * across);
                                float cover = Clamp01(beam - distance + 0.5f);
                                // 줄 사이 화면: 원래 색이면 검정, 아니면 어두운 색
                                float br = original ? 0 : screen[0], bg = original ? 0 : screen[1], bb = original ? 0 : screen[2];
                                P[y * width + x] = Pack(a, br + (r - br) * cover, bg + (g - bg) * cover, bb + (b - bb) * cover);
                            }
                        }
                    }
                });
            }
            else
            {
                // 무늬(망점·채우기 무늬·글자)는 톤만큼 칸을 덮는다. 밝은 바탕이면 어둠을 맡아 어두운 색으로,
                // 어두운 바탕이면 거꾸로.
                float[] marks = tone;
                if (original)
                {
                    marks = new float[count];
                    for (int i = 0; i < count; i++)
                        marks[i] = 0.2126f * tone[i] + 0.7152f * tone[count + i] + 0.0722f * tone[2 * count + i];
                }
                int cell = Math.Max(2, (int)s.CellSize);
                float ang = (float)(s.Angle * Math.PI / 180);
                float cosA = (float)Math.Cos(ang), sinA = (float)Math.Sin(ang);
                bool lod = s.LightOnDark;
                float[] ink = lod ? light : dark, paper = lod ? dark : light;

                // 글자: 칸 하나에 글자 하나. 칸 평균 톤으로 칸마다 한 번만 고른다.
                GlyphSet gs = style == DitherStyle.Ascii ? Glyphs(s.Characters.Length == 0 ? DitherSettings.DefaultCharacters : s.Characters, (int)s.TextSize) : null;
                int gw = gs != null ? Math.Max(1, gs.W) : 1, gh = gs != null ? Math.Max(1, gs.H) : 1;
                int columns = (width + gw - 1) / gw, cellRows = (height + gh - 1) / gh;
                int[] picked = null;
                if (gs != null && gs.Count > 0)
                {
                    picked = new int[columns * cellRows];
                    float most = gs.Coverage[gs.Count - 1];
                    for (int row = 0; row < cellRows; row++)
                        for (int column = 0; column < columns; column++)
                        {
                            float sum = 0; int n = 0;
                            for (int yy = row * gh; yy < (row + 1) * gh && yy < height; yy++)
                                for (int xx = column * gw; xx < (column + 1) * gw && xx < width; xx++)
                                {
                                    int i = yy * width + xx;
                                    if (alpha[i] != 0) { sum += marks[i]; n++; }
                                }
                            float t = n > 0 ? sum / n : 1;
                            float wanted = (lod ? t : 1 - t) * most;
                            int best = 0; float bestDistance = 2;
                            for (int g = 0; g < gs.Count; g++)
                            {
                                float d = Math.Abs(gs.Coverage[g] - wanted);
                                if (d < bestDistance) { bestDistance = d; best = g; }
                            }
                            picked[row * columns + column] = best;
                        }
                }
                // 원래 색: 무늬는 제 픽셀 색, 바탕은 검정(어두운 바탕) 또는 흰색
                float paperOriginal = lod ? 0f : 1f;
                int patternCount = Patterns.Length;
                byte[] maps = gs != null ? gs.Maps : null;
                InBands(height, delegate(int first, int last)
                {
                    for (int y = first; y < last; y++)
                    {
                        float fy = y + 0.5f;
                        for (int x = 0; x < width; x++)
                        {
                            int at = y * width + x;
                            int a = alpha[at];
                            if (a == 0) continue;
                            float amount;
                            if (picked != null)
                            {
                                int glyph = picked[(y / gh) * columns + x / gw];
                                amount = maps[glyph * gw * gh + (y % gh) * gw + x % gw] / 255f;
                            }
                            else if (style == DitherStyle.Patterns)
                            {
                                float t = marks[at];
                                float coverage = lod ? t : 1 - t;
                                int index = LRound(coverage * (patternCount - 1));
                                amount = (Patterns[index][y & 7] >> (7 - (x & 7))) & 1;
                            }
                            else if (style == DitherStyle.Ascii)
                            {
                                amount = 0; // 글자가 하나도 없을 때. 기본 글자로 대신하니 실제로는 오지 않는다
                            }
                            else
                            {
                                float fx = x + 0.5f;
                                float u = (fx * cosA + fy * sinA) / cell, v = (-fx * sinA + fy * cosA) / cell;
                                u -= (float)Math.Floor(u) + 0.5f; v -= (float)Math.Floor(v) + 0.5f;
                                float t = marks[at];
                                amount = (lod ? t : 1 - t) > Spot(style, u, v) ? 1 : 0;
                            }
                            if (original)
                            {
                                int c = P[at];
                                float sr = ((c >> 16) & 255) / 255f, sg = ((c >> 8) & 255) / 255f, sb = (c & 255) / 255f;
                                P[at] = Pack(a, paperOriginal + (sr - paperOriginal) * amount,
                                             paperOriginal + (sg - paperOriginal) * amount, paperOriginal + (sb - paperOriginal) * amount);
                            }
                            else
                            {
                                P[at] = Pack(a, paper[0] + (ink[0] - paper[0]) * amount, paper[1] + (ink[1] - paper[1]) * amount,
                                             paper[2] + (ink[2] - paper[2]) * amount);
                            }
                        }
                    }
                });
            }
        }

        // ---------- 주사선 번짐 (dither_glow) ----------

        // 줄의 빛을 줄 간격 몇 배로 흐려 다시 얹는다 — 브라운관 형광체가 번지듯.
        // 넓고 부드러운 번짐이라 줄여서 흐린 뒤 키워도 티가 안 나고 일은 훨씬 적다.
        static void Glowing(DitherSettings s, Canvas32 img)
        {
            int w = img.W, h = img.H;
            double sigma = s.LineSpacing * 3 + 3;
            int shrink = Math.Max(1, (int)Math.Floor(sigma / 4));
            int sw = (w + shrink - 1) / shrink, sh = (h + shrink - 1) / shrink, n = sw * sh;
            // 알파를 곱한 색으로 흐린다(원본도 그렇게 한다)
            var r = new float[n]; var g = new float[n]; var b = new float[n];
            int[] P = img.P;
            InBands(sh, delegate(int first, int last)
            {
                for (int sy = first; sy < last; sy++)
                {
                    int y0 = sy * shrink, y1 = Math.Min(y0 + shrink, h);
                    for (int sx = 0; sx < sw; sx++)
                    {
                        int x0 = sx * shrink, x1 = Math.Min(x0 + shrink, w);
                        float ar = 0, ag = 0, ab = 0; int k = 0;
                        for (int y = y0; y < y1; y++)
                            for (int x = x0; x < x1; x++)
                            {
                                int c = P[y * w + x];
                                float a = ((uint)c >> 24) / 255f;
                                ar += ((c >> 16) & 255) * a; ag += ((c >> 8) & 255) * a; ab += (c & 255) * a;
                                k++;
                            }
                        int i = sy * sw + sx;
                        r[i] = ar / k; g[i] = ag / k; b[i] = ab / k;
                    }
                }
            });
            int[] boxes = BoxesForGauss(sigma / shrink, 3);
            var tmp = new float[n];
            foreach (var plane in new[] { r, g, b })
                for (int pass = 0; pass < 3; pass++)
                {
                    int rad = (boxes[pass] - 1) / 2;
                    BoxH(plane, tmp, sw, sh, rad);
                    BoxV(tmp, plane, sw, sh, rad);
                }

            float amount = (float)(s.Glow / 100 * 2.5);
            InBands(h, delegate(int first, int last)
            {
                for (int y = first; y < last; y++)
                {
                    // 겹선형으로 키운다 — 원래 해상도 픽셀 중심이 작은 판의 어디에 떨어지나
                    float fy = (y + 0.5f) / shrink - 0.5f;
                    if (fy < 0) fy = 0;
                    int y0 = (int)fy; float ty = fy - y0; int y1 = Math.Min(y0 + 1, sh - 1);
                    if (y0 > sh - 1) { y0 = sh - 1; ty = 0; }
                    for (int x = 0; x < w; x++)
                    {
                        int at = y * w + x, c = P[at];
                        int a = (int)((uint)c >> 24);
                        if (a == 0) continue;
                        float fx = (x + 0.5f) / shrink - 0.5f;
                        if (fx < 0) fx = 0;
                        int x0 = (int)fx; float tx = fx - x0; int x1 = Math.Min(x0 + 1, sw - 1);
                        if (x0 > sw - 1) { x0 = sw - 1; tx = 0; }
                        int i00 = y0 * sw + x0, i01 = y0 * sw + x1, i10 = y1 * sw + x0, i11 = y1 * sw + x1;
                        float lr = Lerp2(r, i00, i01, i10, i11, tx, ty), lg = Lerp2(g, i00, i01, i10, i11, tx, ty), lb = Lerp2(b, i00, i01, i10, i11, tx, ty);
                        // 알파를 곱한 공간에서 "빛 × 양 × 알파를 더하되 알파를 넘지 않게"는
                        // 곱하지 않은 공간에선 그냥 "빛 × 양을 더하고 255에서 자르기"다
                        int nr = Math.Min(255, (int)(((c >> 16) & 255) + lr * amount + 0.5f));
                        int ng = Math.Min(255, (int)(((c >> 8) & 255) + lg * amount + 0.5f));
                        int nb = Math.Min(255, (int)((c & 255) + lb * amount + 0.5f));
                        P[at] = (a << 24) | (nr << 16) | (ng << 8) | nb;
                    }
                }
            });
        }

        static float Lerp2(float[] p, int i00, int i01, int i10, int i11, float tx, float ty)
        {
            float top = p[i00] + (p[i01] - p[i00]) * tx, bot = p[i10] + (p[i11] - p[i10]) * tx;
            return top + (bot - top) * ty;
        }

        // 상자 블러 n번이 주어진 시그마의 가우시안에 가장 가깝게 되는 상자 너비들
        static int[] BoxesForGauss(double sigma, int n)
        {
            double wIdeal = Math.Sqrt(12 * sigma * sigma / n + 1);
            int wl = (int)Math.Floor(wIdeal);
            if (wl % 2 == 0) wl--;
            if (wl < 1) wl = 1;
            int wu = wl + 2;
            double mIdeal = (12 * sigma * sigma - n * wl * wl - 4 * n * wl - 3 * n) / (-4.0 * wl - 4);
            int m = (int)Math.Round(mIdeal);
            var sizes = new int[n];
            for (int i = 0; i < n; i++) sizes[i] = i < m ? wl : wu;
            return sizes;
        }

        // 가장자리는 끝 픽셀이 계속 이어진다고 본다(clampedToExtent와 같다)
        static void BoxH(float[] src, float[] dst, int w, int h, int r)
        {
            if (r <= 0) { Array.Copy(src, dst, src.Length); return; }
            float inv = 1f / (2 * r + 1);
            InBands(h, delegate(int first, int last)
            {
                for (int y = first; y < last; y++)
                {
                    int row = y * w;
                    float acc = 0;
                    for (int k = -r; k <= r; k++) acc += src[row + Math.Max(0, Math.Min(w - 1, k))];
                    for (int x = 0; x < w; x++)
                    {
                        dst[row + x] = acc * inv;
                        acc += src[row + Math.Min(w - 1, x + r + 1)] - src[row + Math.Max(0, x - r)];
                    }
                }
            });
        }

        static void BoxV(float[] src, float[] dst, int w, int h, int r)
        {
            if (r <= 0) { Array.Copy(src, dst, src.Length); return; }
            float inv = 1f / (2 * r + 1);
            InBands(w, delegate(int first, int last)
            {
                for (int x = first; x < last; x++)
                {
                    float acc = 0;
                    for (int k = -r; k <= r; k++) acc += src[Math.Max(0, Math.Min(h - 1, k)) * w + x];
                    for (int y = 0; y < h; y++)
                    {
                        dst[y * w + x] = acc * inv;
                        acc += src[Math.Min(h - 1, y + r + 1) * w + x] - src[Math.Max(0, y - r) * w + x];
                    }
                }
            });
        }

        // ---------- 아스키 글자 지도 ----------

        class GlyphSet
        {
            public byte[] Maps;        // Count장, 장마다 W×H 바이트(255 = 잉크 꽉)
            public float[] Coverage;   // 장마다 평균 잉크 0~1, 오름차순
            public int W, H, Count;
            public string Key;
        }

        static readonly object glyphLock = new object();
        static GlyphSet lastGlyphs;

        // 글자마다 고정폭 글자 한 칸(높이 lineHeight, 너비 한 글자)에 터미널처럼 같은 기준선으로 찍고
        // 잉크가 적은 것부터 줄 세운다. 미리보기마다 다시 그리지 않게 마지막 것을 쥐고 있는다.
        static GlyphSet Glyphs(string characters, int lineHeight)
        {
            string key = lineHeight + "\u0001" + characters;
            lock (glyphLock)
            {
                if (lastGlyphs != null && lastGlyphs.Key == key) return lastGlyphs;
            }

            // 결합 문자·이모지가 반쪽으로 쪼개지지 않게 글자 단위(텍스트 요소)로 센다
            var unique = new List<string>();
            var seen = new HashSet<string>();
            var e = StringInfo.GetTextElementEnumerator(characters);
            while (e.MoveNext())
            {
                string ch = e.GetTextElement();
                if (seen.Add(ch)) unique.Add(ch);
            }

            int height = lineHeight, width;
            var maps = new List<byte[]>();
            var cover = new List<float>();
            using (var font = new D.Font("Consolas", lineHeight / 1.2f, D.FontStyle.Bold, D.GraphicsUnit.Pixel))
            using (var fmt = (D.StringFormat)D.StringFormat.GenericTypographic.Clone())
            {
                var fam = font.FontFamily;
                float em = fam.GetEmHeight(D.FontStyle.Bold);
                float ascent = font.Size * fam.GetCellAscent(D.FontStyle.Bold) / em;
                float descent = font.Size * fam.GetCellDescent(D.FontStyle.Bold) / em;
                fmt.FormatFlags |= D.StringFormatFlags.MeasureTrailingSpaces;
                using (var probe = new D.Bitmap(1, 1))
                using (var pg = D.Graphics.FromImage(probe))
                {
                    width = Math.Max(1, (int)Math.Round(pg.MeasureString("M", font, D.PointF.Empty, fmt).Width, MidpointRounding.AwayFromZero));
                }
                // 원본은 아래가 0인 좌표계에서 글자 상자(어센트+디센트)를 가운데 두고 기준선을 반올림한다
                float baselineFromBottom = (float)Math.Round((height - (ascent + descent)) / 2 + descent, MidpointRounding.AwayFromZero);
                float top = height - baselineFromBottom - ascent;

                using (var bmp = new D.Bitmap(width, height, D.Imaging.PixelFormat.Format32bppArgb))
                using (var g = D.Graphics.FromImage(bmp))
                {
                    g.TextRenderingHint = D.Text.TextRenderingHint.AntiAliasGridFit;
                    var raw = new byte[width * height * 4];
                    foreach (var ch in unique)
                    {
                        g.Clear(D.Color.Black);
                        float adv = g.MeasureString(ch, font, D.PointF.Empty, fmt).Width;
                        float x = (float)Math.Round((width - adv) / 2, MidpointRounding.AwayFromZero);
                        g.DrawString(ch, font, D.Brushes.White, x, top, fmt);
                        g.Flush();
                        var data = bmp.LockBits(new D.Rectangle(0, 0, width, height), D.Imaging.ImageLockMode.ReadOnly, D.Imaging.PixelFormat.Format32bppArgb);
                        try
                        {
                            for (int y = 0; y < height; y++)
                                System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, raw, y * width * 4, width * 4);
                        }
                        finally { bmp.UnlockBits(data); }
                        var map = new byte[width * height];
                        long sum = 0;
                        // 흰 글씨라 초록 채널 하나면 된다
                        for (int i = 0; i < map.Length; i++) { map[i] = raw[i * 4 + 1]; sum += map[i]; }
                        maps.Add(map);
                        cover.Add((float)sum / (255f * width * height));
                    }
                }
            }

            // 잉크 적은 것부터. 같으면 넣은 순서대로
            var order = new int[maps.Count];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            var keys = cover.ToArray();
            Array.Sort(order, delegate(int p, int q) { int c = keys[p].CompareTo(keys[q]); return c != 0 ? c : p.CompareTo(q); });

            var set = new GlyphSet { W = width, H = height, Count = maps.Count, Key = key };
            set.Maps = new byte[width * height * maps.Count];
            set.Coverage = new float[maps.Count];
            for (int i = 0; i < order.Length; i++)
            {
                Array.Copy(maps[order[i]], 0, set.Maps, i * width * height, width * height);
                set.Coverage[i] = keys[order[i]];
            }
            lock (glyphLock) lastGlyphs = set;
            return set;
        }
    }

    // ================= 디더링 창 =================

    public class DitherSheet : ToneSheet
    {
        public DitherSettings Value = new DitherSettings();
        readonly StackPanel host = new StackPanel();

        static readonly string[][] StyleNames =
        {
            new[] { L.T("앳킨슨(옛 맥)", "Atkinson (Classic Mac)"), L.T("플로이드–스타인버그", "Floyd–Steinberg") },
            new[] { L.T("베이어 2×2", "Bayer 2×2"), L.T("베이어 4×4", "Bayer 4×4"), L.T("베이어 8×8", "Bayer 8×8") },
            new[] { L.T("망점", "Halftone Dots"), L.T("선 망점", "Halftone Lines"), L.T("다이아몬드 망점", "Halftone Diamonds") },
            new[] { L.T("맥 무늬", "Mac Patterns"), L.T("아스키", "ASCII"), L.T("주사선(CRT)", "Scanlines (CRT)") },
        };

        static readonly string[] StyleNotes =
        {
            L.T("오차를 6/8만 넘긴다. 옛 매킨토시처럼 또렷하고 대비가 세다.", "Passes on only 6/8 of the error. Crisp and contrasty, like the classic Macintosh."),
            L.T("오차를 이웃에 고루 넘긴다. 결이 가장 곱다.", "Spreads the error evenly to neighbors. The finest texture."),
            L.T("2×2 격자 무늬로 찍는다. 거칠고 무늬가 또렷하다.", "Uses a 2×2 grid pattern. Coarse, with a clear pattern."),
            L.T("4×4 격자 무늬로 찍는다. 옛 게임기 화면 느낌.", "Uses a 4×4 grid pattern. Feels like an old game console."),
            L.T("8×8 격자 무늬로 찍는다. 단계가 가장 촘촘하다.", "Uses an 8×8 grid pattern. The smoothest steps."),
            L.T("인쇄물 망점처럼 밝기를 점 크기로 나타낸다.", "Shows brightness as dot size, like print halftones."),
            L.T("망점을 선으로. 각도로 결 방향을 돌린다.", "Halftone as lines. Angle rotates the line direction."),
            L.T("망점을 마름모로 찍는다.", "Halftone with diamond shapes."),
            L.T("옛 맥의 채우기 무늬 17가지 가운데 밝기에 맞는 것을 깐다.", "Fills with whichever of the 17 classic Mac patterns matches the brightness."),
            L.T("칸마다 밝기에 맞는 글자를 고른다. 순서는 상관없다.", "Picks a character to match each cell's brightness. Order doesn't matter."),
            L.T("브라운관처럼 가로줄로 다시 그린다. 밝은 곳일수록 줄이 굵고 환하다.", "Redraws with horizontal lines like a CRT. Brighter areas get thicker, brighter lines."),
        };

        // 두 가지 색 미리 짝지어 둔 것: 이름, 어두운 색, 밝은 색
        static readonly object[][] Presets =
        {
            new object[] { L.T("먹과 종이", "Ink & Paper"), 0x1B1B1F, 0xF4EFE6 },
            new object[] { L.T("게임보이", "Game Boy"), 0x0F380F, 0x9BBC0F },
            new object[] { L.T("초록 모니터", "Green Monitor"), 0x001A08, 0x33FF66 },
            new object[] { L.T("호박색 모니터", "Amber Monitor"), 0x1A0E00, 0xFFB000 },
            new object[] { L.T("청사진", "Blueprint"), 0x0B2A5B, 0xE8F0FF },
            new object[] { L.T("세피아", "Sepia"), 0x2B1B0E, 0xF2E3C6 },
            new object[] { L.T("리소 핑크", "Riso Pink"), 0x2A2A72, 0xFF7AA8 },
        };

        // 칸마다 접었다 폈다 할 묶음들
        StackPanel pixelBox, textSizeBox, scanBox, halftoneBox, charsBox, tonesBox, diffBox, twoBox, shapeBox, lightBox;
        TextBlock note;
        Pills style;
        TextBox darkHex, lightHex;
        Border darkSwatch, lightSwatch;

        public DitherSheet() : base(L.T("디더링", "Dither"), 380)
        {
            var scroll = new ScrollViewer
            {
                Content = host, MaxHeight = 600,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            Body.Children.Add(scroll);
            Footer();
            Build();
        }

        static string Pct(double v) { return Plain(v) + "%"; }
        static string Px(double v) { return Plain(v) + " px"; }
        static string Deg(double v) { return Plain(v) + "°"; }

        static TextBlock Label(string t, double top)
        {
            return new TextBlock { Text = t, Foreground = Theme.BrText, FontSize = 12.5, Margin = new Thickness(0, top, 0, 6) };
        }

        void Build()
        {
            var s = Value;
            host.Children.Clear();

            style = new Pills(StyleNames, (int)s.Style);
            style.Changed = delegate(int k) { Value.Style = (DitherStyle)k; Visible(); Fire(); };
            host.Children.Add(style.Panel);
            note = Note("");
            note.Margin = new Thickness(1, 0, 0, 4);
            host.Children.Add(note);

            pixelBox = Box();
            AddRow(pixelBox, L.T("픽셀 크기", "Pixel size"), 1, 32, s.PixelSize, 2, Px, delegate(double v) { Value.PixelSize = v; Visible(); }, null);

            textSizeBox = Box();
            AddRow(textSizeBox, L.T("글자 크기", "Text size"), 6, 64, s.TextSize, 14, Px, delegate(double v) { Value.TextSize = v; }, null);

            scanBox = Box();
            AddRow(scanBox, L.T("줄 간격", "Line spacing"), 2, 32, s.LineSpacing, 4, Px, delegate(double v) { Value.LineSpacing = v; }, null);
            AddRow(scanBox, L.T("번짐", "Glow"), 0, 100, s.Glow, 35, Pct, delegate(double v) { Value.Glow = v; }, null);
            AddRow(scanBox, L.T("구슬로 끊기", "Break into dots"), 0, 100, s.Dots, 0, Pct, delegate(double v) { Value.Dots = v; }, null);
            AddRow(scanBox, L.T("흔들림", "Wobble"), 0, 64, s.Wobble, 0, Px, delegate(double v) { Value.Wobble = v; }, null);

            halftoneBox = Box();
            AddRow(halftoneBox, L.T("망점 크기", "Dot size"), 4, 64, s.CellSize, 8, Px, delegate(double v) { Value.CellSize = v; }, null);
            AddRow(halftoneBox, L.T("각도", "Angle"), -90, 90, s.Angle, 45, Deg, delegate(double v) { Value.Angle = v; }, null);

            charsBox = Box();
            charsBox.Children.Add(Label(L.T("글자", "Characters"), 10));
            var chars = new TextBox
            {
                Text = s.Characters, FontFamily = Theme.Mono, FontSize = 13, Padding = new Thickness(6, 4, 6, 4),
                ToolTip = L.T("순서는 상관없다. 칸마다 잉크 양이 밝기에 가장 맞는 글자를 고른다.", "Order doesn't matter. Each cell gets the character whose ink coverage best matches its brightness.")
            };
            chars.TextChanged += delegate { Value.Characters = chars.Text; Fire(); };
            charsBox.Children.Add(chars);

            tonesBox = Box();
            AddRow(tonesBox, L.T("톤 수", "Tones"), 2, 8, s.Levels, 2, Plain, delegate(double v) { Value.Levels = v; }, null);

            diffBox = Box();
            AddRow(diffBox, L.T("오차 확산", "Diffusion"), 0, 100, s.Diffusion, 100, Pct, delegate(double v) { Value.Diffusion = v; }, null);

            AddRow(host, L.T("농도", "Density"), -100, 100, s.Density, 0, Signed, delegate(double v) { Value.Density = v; },
                   Grad(Color.FromRgb(0xF2, 0xF2, 0xF2), Color.FromRgb(0x18, 0x18, 0x1C)));
            AddRow(host, L.T("대비", "Contrast"), -100, 100, s.Contrast, 0, Signed, delegate(double v) { Value.Contrast = v; }, null);

            host.Children.Add(Label(L.T("색", "Color"), 14));
            var colors = new Seg(new[] { L.T("흑백", "Mono"), L.T("두 가지 색", "Two colors"), L.T("원래 색", "Original") }, (int)s.Colors);
            colors.Changed = delegate(int k) { Value.Colors = (DitherColors)k; Visible(); Fire(); };
            host.Children.Add(colors.Panel);

            twoBox = Box();
            BuildTwoColors(twoBox);

            shapeBox = Box();
            shapeBox.Children.Add(Label(L.T("픽셀 모양", "Pixel shape"), 4));
            var shape = new Seg(new[] { L.T("네모", "Square"), L.T("둥근 점", "Round") }, (int)s.PixelShape);
            shape.Changed = delegate(int k) { Value.PixelShape = (DitherPixelShape)k; Fire(); };
            shapeBox.Children.Add(shape.Panel);

            lightBox = Box();
            var tg = Toggle(L.T("어두운 바탕에 밝은 무늬", "Light on dark"), s.LightOnDark, delegate(bool on) { Value.LightOnDark = on; });
            tg.Margin = new Thickness(0, 2, 0, 0);
            tg.ToolTip = L.T("무늬가 밝은 쪽을 맡는다. 검은 화면에 빛나는 점처럼.", "The pattern draws the light parts, like glowing dots on a black screen.");
            lightBox.Children.Add(tg);

            Visible();
        }

        StackPanel Box()
        {
            var b = new StackPanel();
            host.Children.Add(b);
            return b;
        }

        void BuildTwoColors(StackPanel box)
        {
            var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            foreach (var p in Presets)
            {
                string name = (string)p[0]; int dk = (int)p[1], lt = (int)p[2];
                var g = new Grid { Width = 30, Height = 18 };
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.ColumnDefinitions.Add(new ColumnDefinition());
                var l = new Border { Background = new SolidColorBrush(Rgb(dk)) };
                var r = new Border { Background = new SolidColorBrush(Rgb(lt)) };
                Grid.SetColumn(r, 1);
                g.Children.Add(l); g.Children.Add(r);
                var chip = new Border
                {
                    Child = g, CornerRadius = new CornerRadius(4), BorderBrush = Theme.BrBorder, BorderThickness = new Thickness(1),
                    Margin = new Thickness(0, 0, 6, 6), Cursor = Cursors.Hand, ToolTip = name, ClipToBounds = true
                };
                chip.MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs a) { a.Handled = true; };
                chip.MouseLeftButtonUp += delegate { darkHex.Text = Hex(dk); lightHex.Text = Hex(lt); };
                wrap.Children.Add(chip);
            }
            box.Children.Add(wrap);

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            darkSwatch = Swatch(Value.Dark);
            darkHex = HexBox(Value.Dark, darkSwatch, delegate(int c) { Value.Dark = c; });
            lightSwatch = Swatch(Value.Light);
            lightHex = HexBox(Value.Light, lightSwatch, delegate(int c) { Value.Light = c; });
            row.Children.Add(new TextBlock { Text = L.T("어두운 색", "Dark"), Foreground = Theme.BrDim, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            row.Children.Add(darkSwatch); row.Children.Add(darkHex);
            row.Children.Add(new TextBlock { Text = L.T("밝은 색", "Light"), Foreground = Theme.BrDim, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 6, 0) });
            row.Children.Add(lightSwatch); row.Children.Add(lightHex);
            box.Children.Add(row);
        }

        static Color Rgb(int c) { return Color.FromRgb((byte)(c >> 16), (byte)(c >> 8), (byte)c); }
        static string Hex(int c) { return "#" + (c & 0xFFFFFF).ToString("X6"); }

        static Border Swatch(int c)
        {
            return new Border
            {
                Width = 20, Height = 20, CornerRadius = new CornerRadius(5), Background = new SolidColorBrush(Rgb(c)),
                BorderBrush = Theme.BrBorder, BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center
            };
        }

        TextBox HexBox(int init, Border swatch, Action<int> set)
        {
            var t = new TextBox
            {
                Text = Hex(init), Width = 74, FontFamily = Theme.Mono, FontSize = 12, Padding = new Thickness(4, 3, 4, 3),
                Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, MaxLength = 7
            };
            t.TextChanged += delegate
            {
                int c;
                if (!TryHex(t.Text, out c)) return;   // 치는 중인 반쪽 값은 무시
                set(c);
                swatch.Background = new SolidColorBrush(Rgb(c));
                Fire();
            };
            return t;
        }

        static bool TryHex(string s, out int c)
        {
            c = 0;
            s = (s ?? "").Trim().TrimStart('#');
            if (s.Length == 3) s = new string(new[] { s[0], s[0], s[1], s[1], s[2], s[2] });
            if (s.Length != 6) return false;
            return int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out c);
        }

        // 고른 스타일에 듣는 조절만 보인다 (Compositor의 ditherControls와 같은 규칙)
        void Visible()
        {
            var st = Value.Style;
            Action<UIElement, bool> show = delegate(UIElement e, bool on) { e.Visibility = on ? Visibility.Visible : Visibility.Collapsed; };
            show(pixelBox, Dither.UsesPixelSize(st));
            show(textSizeBox, st == DitherStyle.Ascii);
            show(scanBox, st == DitherStyle.Scanlines);
            show(halftoneBox, Dither.IsHalftone(st));
            show(charsBox, st == DitherStyle.Ascii);
            show(tonesBox, Dither.HasTones(st));
            show(diffBox, Dither.Diffuses(st));
            show(twoBox, Value.Colors == DitherColors.TwoColors);
            show(shapeBox, Value.PixelSize > 1 && Dither.UsesPixelSize(st));
            show(lightBox, Dither.DrawsMarks(st));
            note.Text = StyleNotes[(int)st];
        }

        public override bool IsIdentity { get { return false; } }

        public override ToneFn Current()
        {
            var c = Value.Clone();
            return delegate(Canvas32 src, Canvas32 dst) { Dither.Apply(c, src, dst); };
        }

        // 고른 스타일은 두고 나머지만 처음 값으로
        protected override void ResetAll()
        {
            Value = new DitherSettings { Style = Value.Style };
            Build();
        }

        // 11가지는 한 줄에 안 들어간다. 묶음마다 한 줄씩, 넘치면 접히는 알약 버튼 (Seg와 같은 모양)
        protected class Pills
        {
            public readonly StackPanel Panel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            readonly List<Border> items = new List<Border>();
            public int Index;
            public Action<int> Changed = delegate { };

            public Pills(string[][] groups, int init)
            {
                Index = init;
                int k = 0;
                foreach (var group in groups)
                {
                    var wrap = new WrapPanel();
                    foreach (var name in group)
                    {
                        int me = k++;
                        var b = new Border
                        {
                            CornerRadius = new CornerRadius(6),
                            Padding = new Thickness(9, 4, 9, 5),
                            Margin = new Thickness(0, 0, 4, 4),
                            BorderThickness = new Thickness(1),
                            Cursor = Cursors.Hand,
                            Child = new TextBlock { Text = name, FontSize = 11.5 }
                        };
                        b.MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs a) { a.Handled = true; };
                        b.MouseLeftButtonUp += delegate { if (Index == me) return; Set(me); Changed(me); };
                        items.Add(b);
                        wrap.Children.Add(b);
                    }
                    Panel.Children.Add(wrap);
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
    }
}
