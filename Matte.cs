// Money Shot — 누끼 다듬기
//
// AI가 내놓은 마스크는 머리카락·털을 뭉텅 잘라먹고, 얇은 곳엔 배경이 비치는 안개가 끼고,
// 테두리에 배경색 띠가 남는다. 모델에는 조절할 것이 없으니 결과를 슬라이더 셋으로 손본다.
//   다듬기  — 마스크를 원본 사진의 경계에 다시 맞춘다(guided filter). 머리카락이 여기서 돌아온다
//   대비    — 반쯤 투명한 회색을 양쪽으로 밀어 안개를 걷는다
//   경계 이동 — 마스크를 안으로 줄이거나 밖으로 늘린다. 보통 안으로 깎아 배경색 띠를 뗀다
//
// Compositor(MIT, Wonder Assembly LLC)의 SubjectRemoval·GuidedMatte·MaskTracing을 옮겼다.
// 거기서 Core Image로 하던 흐림·색 행렬은 같은 계산을 직접 한다.
using System;
using System.Windows;
using System.Windows.Media;

namespace MoneyShot
{
    public class MatteSettings
    {
        public double Refine = 12;      // 0~40 px
        public double Contrast = 25;    // 0~100 %
        public double Shift = 0;        // -10~10 px, 음수 = 안으로

        public bool IsRaw { get { return Refine <= 0 && Contrast <= 0 && Shift == 0; } }
        public MatteSettings Clone() { return (MatteSettings)MemberwiseClone(); }
    }

    public static class Matte
    {
        // 미리보기는 긴 변이 이 크기를 넘지 않는 사본으로 계산한다. 슬라이더를 끄는 동안 버벅이지 않게.
        public const int PreviewLimit = 1400;

        // raw: AI 마스크(0~255, 그림 크기). guide: 그 그림. limit: 계산할 사본의 긴 변 상한.
        // 결과는 늘 그림 크기다.
        public static byte[] Refine(byte[] raw, Canvas32 guide, MatteSettings s, int limit)
        {
            int W = guide.W, H = guide.H;
            if (s == null || s.IsRaw) return (byte[])raw.Clone();

            double factor = Math.Min(1.0, (double)limit / Math.Max(W, H));
            int w = Math.Max(1, (int)Math.Round(W * factor)), h = Math.Max(1, (int)Math.Round(H * factor));

            float[] m = ScaleMask(raw, W, H, w, h);

            if (s.Refine > 0)
            {
                int steps = Math.Max(1, (int)Math.Round(s.Refine * factor));
                m = Guided(m, Gray(guide, w, h), w, h, steps, 1e-4f);
            }

            if (s.Shift != 0)
            {
                // 흐린 뒤 한 높이에서 자르면 흐림이 닿는 만큼 경계가 움직인다.
                // 원본은 폭 0.001로 딱 자르는데, 그러면 가장자리 계단이 그대로 드러나 폭을 조금 준다.
                double reach = Math.Abs(s.Shift) * factor;
                m = Gaussian(m, w, h, reach / 2);
                float level = s.Shift < 0 ? 0.75f : 0.25f;
                const float band = 0.04f;
                for (int i = 0; i < m.Length; i++)
                    m[i] = Clamp01((m[i] - (level - band)) / (2 * band));
            }

            if (s.Contrast > 0)
            {
                // 0은 그대로, 100은 한가운데서 딱 자른다.
                double strength = s.Contrast / 100.0;
                float slope = (float)(1.0 / Math.Max(0.02, 1 - strength * 0.98));
                float bias = (1 - slope) / 2;
                for (int i = 0; i < m.Length; i++) m[i] = Clamp01(m[i] * slope + bias);
            }

            return Upscale(m, w, h, W, H);
        }

        // ---------- guided filter (He, Sun & Tang) ----------

        // (2r+1)² 정사각형 평균. 누적합 두 번이라 반경이 커져도 비용이 그대로다.
        public static float[] Box(float[] src, int width, int height, int radius)
        {
            float span = radius * 2 + 1;
            var pass = new float[width * height];
            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                float sum = 0;
                for (int x = -radius; x <= radius; x++) sum += src[row + Math.Min(width - 1, Math.Max(0, x))];
                for (int x = 0; x < width; x++)
                {
                    pass[row + x] = sum / span;
                    sum -= src[row + Math.Min(width - 1, Math.Max(0, x - radius))];
                    sum += src[row + Math.Min(width - 1, Math.Max(0, x + radius + 1))];
                }
            }
            var result = new float[width * height];
            for (int x = 0; x < width; x++)
            {
                float sum = 0;
                for (int y = -radius; y <= radius; y++) sum += pass[Math.Min(height - 1, Math.Max(0, y)) * width + x];
                for (int y = 0; y < height; y++)
                {
                    result[y * width + x] = sum / span;
                    sum -= pass[Math.Min(height - 1, Math.Max(0, y - radius)) * width + x];
                    sum += pass[Math.Min(height - 1, Math.Max(0, y + radius + 1)) * width + x];
                }
            }
            return result;
        }

        // mask를 guide(둘 다 0~1, 같은 크기)의 경계에 맞춘다. epsilon이 작을수록 가는 결까지 따라간다.
        public static float[] Guided(float[] mask, float[] guide, int width, int height, int radius, float epsilon)
        {
            int count = width * height;
            var meanGuide = Box(guide, width, height, radius);
            var meanMask = Box(mask, width, height, radius);
            var squares = new float[count];
            var products = new float[count];
            for (int i = 0; i < count; i++) { squares[i] = guide[i] * guide[i]; products[i] = guide[i] * mask[i]; }
            var meanSquares = Box(squares, width, height, radius);
            var meanProducts = Box(products, width, height, radius);
            var slope = squares;            // 다 쓴 버퍼를 다시 쓴다
            var offset = products;
            for (int i = 0; i < count; i++)
            {
                float variance = meanSquares[i] - meanGuide[i] * meanGuide[i];
                float covariance = meanProducts[i] - meanGuide[i] * meanMask[i];
                slope[i] = covariance / (variance + epsilon);
                offset[i] = meanMask[i] - slope[i] * meanGuide[i];
            }
            var meanSlope = Box(slope, width, height, radius);
            var meanOffset = Box(offset, width, height, radius);
            var result = new float[count];
            for (int i = 0; i < count; i++) result[i] = Clamp01(meanSlope[i] * guide[i] + meanOffset[i]);
            return result;
        }

        // 상자 흐림 세 번이면 가우시안과 거의 같다. 폭 w 상자 셋의 분산 3(w²-1)/12 = σ².
        public static float[] Gaussian(float[] src, int width, int height, double sigma)
        {
            if (sigma <= 0.01) return src;
            int r = Math.Max(1, (int)Math.Round((Math.Sqrt(4 * sigma * sigma + 1) - 1) / 2));
            var m = src;
            for (int k = 0; k < 3; k++) m = Box(m, width, height, r);
            return m;
        }

        // ---------- 크기 맞추기 ----------

        // 그림의 밝기(0~1)를 w×h로. 줄일 때는 칸 평균이라 잔결이 깨지지 않는다.
        static float[] Gray(Canvas32 c, int w, int h)
        {
            var lum = new float[c.W * c.H];
            for (int i = 0; i < lum.Length; i++)
            {
                int p = c.P[i];
                lum[i] = (0.299f * Canvas32.R(p) + 0.587f * Canvas32.G(p) + 0.114f * Canvas32.B(p)) / 255f;
            }
            return Area(lum, c.W, c.H, w, h);
        }

        static float[] ScaleMask(byte[] m, int W, int H, int w, int h)
        {
            var f = new float[W * H];
            for (int i = 0; i < f.Length; i++) f[i] = m[i] / 255f;
            return Area(f, W, H, w, h);
        }

        // 칸 평균으로 줄인다(같은 크기면 그대로).
        static float[] Area(float[] src, int W, int H, int w, int h)
        {
            if (w == W && h == H) return src;
            var dst = new float[w * h];
            var cnt = new int[w * h];
            for (int y = 0; y < H; y++)
            {
                int dy = Math.Min(h - 1, (int)((long)y * h / H));
                for (int x = 0; x < W; x++)
                {
                    int dx = Math.Min(w - 1, (int)((long)x * w / W));
                    dst[dy * w + dx] += src[y * W + x];
                    cnt[dy * w + dx]++;
                }
            }
            for (int i = 0; i < dst.Length; i++) if (cnt[i] > 0) dst[i] /= cnt[i];
            return dst;
        }

        static byte[] Upscale(float[] m, int mw, int mh, int w, int h)
        {
            var o = new byte[w * h];
            if (mw == w && mh == h)
            {
                for (int i = 0; i < o.Length; i++) o[i] = ToByte(m[i]);
                return o;
            }
            double sx = (double)mw / w, sy = (double)mh / h;
            for (int y = 0; y < h; y++)
            {
                double fy = (y + 0.5) * sy - 0.5;
                int y0 = (int)Math.Floor(fy); float ty = (float)(fy - y0);
                int y1 = Math.Min(mh - 1, Math.Max(0, y0 + 1)); y0 = Math.Min(mh - 1, Math.Max(0, y0));
                for (int x = 0; x < w; x++)
                {
                    double fx = (x + 0.5) * sx - 0.5;
                    int x0 = (int)Math.Floor(fx); float tx = (float)(fx - x0);
                    int x1 = Math.Min(mw - 1, Math.Max(0, x0 + 1)); x0 = Math.Min(mw - 1, Math.Max(0, x0));
                    float top = m[y0 * mw + x0] + (m[y0 * mw + x1] - m[y0 * mw + x0]) * tx;
                    float bot = m[y1 * mw + x0] + (m[y1 * mw + x1] - m[y1 * mw + x0]) * tx;
                    o[y * w + x] = ToByte(top + (bot - top) * ty);
                }
            }
            return o;
        }

        static float Clamp01(float v) { return v < 0 ? 0 : v > 1 ? 1 : v; }
        static byte ToByte(float v) { return (byte)Math.Max(0, Math.Min(255, (int)(v * 255 + 0.5f))); }

        // ---------- 마스크 → 선택 테두리 ----------

        const int East = 1, South = 2, West = 4, North = 8;
        static int TurnRight(int d) { return d == North ? East : d << 1; }
        static int TurnLeft(int d) { return d == East ? North : d >> 1; }

        // mask에서 threshold 이상인 픽셀의 테두리를 픽셀 경계 그대로 딴다.
        // 바깥 윤곽은 시계 방향, 구멍은 반대라 Nonzero 채우기로 딴 모양이 정확히 나온다.
        // (ox, oy)만큼 옮겨 문서 좌표로 낸다. 아무것도 없으면 null.
        public static Geometry Trace(byte[] mask, int width, int height, byte threshold, int ox, int oy)
        {
            if (width <= 0 || height <= 0) return null;
            int stride = width + 1;
            var outgoing = new byte[stride * (height + 1)];
            bool any = false;
            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    if (mask[row + x] < threshold) continue;
                    any = true;
                    if (y == 0 || mask[row - width + x] < threshold) outgoing[y * stride + x] |= East;
                    if (x + 1 == width || mask[row + x + 1] < threshold) outgoing[y * stride + x + 1] |= South;
                    if (y + 1 == height || mask[row + width + x] < threshold) outgoing[(y + 1) * stride + x + 1] |= West;
                    if (x == 0 || mask[row + x - 1] < threshold) outgoing[(y + 1) * stride + x] |= North;
                }
            }
            if (!any) return null;

            var geo = new StreamGeometry { FillRule = FillRule.Nonzero };
            var pts = new System.Collections.Generic.List<Point>();
            using (var ctx = geo.Open())
            {
                for (int start = 0; start < outgoing.Length; start++)
                {
                    while (outgoing[start] != 0)
                    {
                        pts.Clear();
                        int v = start, heading = 0, initial = 0;
                        do
                        {
                            int bits = outgoing[v], d;
                            // 두 고리가 한 꼭짓점에서 만나면 오른쪽으로 꺾어야 서로 안 섞인다.
                            if (heading == 0) d = bits & -bits;
                            else if ((bits & TurnRight(heading)) != 0) d = TurnRight(heading);
                            else if ((bits & heading) != 0) d = heading;
                            else if ((bits & TurnLeft(heading)) != 0) d = TurnLeft(heading);
                            else d = bits & -bits;
                            if (d == 0) break;
                            outgoing[v] &= (byte)~d;
                            if (d != heading) pts.Add(new Point(v % stride + ox, v / stride + oy));
                            if (heading == 0) initial = d;
                            heading = d;
                            v = d == East ? v + 1 : d == West ? v - 1 : d == South ? v + stride : v - stride;
                        } while (v != start);
                        // 떠날 때와 같은 방향으로 돌아왔으면 출발점은 꼭짓점이 아니다.
                        if (heading == initial && pts.Count > 0) pts.RemoveAt(0);
                        if (pts.Count < 3) continue;
                        ctx.BeginFigure(pts[0], true, true);
                        ctx.PolyLineTo(pts.GetRange(1, pts.Count - 1), false, false);
                    }
                }
            }
            geo.Freeze();
            return geo;
        }
    }
}
