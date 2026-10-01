// Money Shot — 스팟 힐링과 내용 인식 채우기
//
// 스팟 힐링: 칠한 자리를 둘레의 결에 맞춰 메운다. 방식은 셋이다.
//   내용 인식 — 둘레가 가장 닮은 곳을 주변에서 찾아 그 무늬를 가져오고, 색 차이는 매끄럽게 펴서 맞춘다
//   가까운 곳 — 같지만 더 가까운 곳을 더 쳐준다
//   매끄럽게 — 가져올 곳 없이 둘레 색을 안쪽으로 펴고, 둘레만큼의 자글거림을 얹는다
// 내용 인식 채우기: 선택 영역을 바깥의 작은 조각들로 한 픽셀씩 짜 맞춰 채운다(PatchMatch 비슷한 방식).
//
// Compositor(MIT, Wonder Assembly LLC)의 HealPixels.c·ContentFill.c를 옮겼다.
// 원본은 알파를 곱한 RGBA라 색을 알파로 자르는데, 여기 Canvas32는 곱하지 않은 ARGB라 0~255로 자른다.
using System;

namespace MoneyShot
{
    public enum HealMode { ContentAware = 0, Smooth = 1, Proximity = 2 }

    public static class Heal
    {
        const byte Outside = 0, Ring = 1, Hole = 2;

        // 채널 c(0=R,1=G,2=B,3=A)
        static int Ch(int p, int c)
        {
            switch (c)
            {
                case 0: return (p >> 16) & 0xFF;
                case 1: return (p >> 8) & 0xFF;
                case 2: return p & 0xFF;
                default: return (int)((uint)p >> 24);
            }
        }

        static uint Hash(uint x)
        {
            unchecked
            {
                x ^= x >> 16; x *= 0x7feb352dU;
                x ^= x >> 15; x *= 0x846ca68bU;
                x ^= x >> 16;
            }
            return x;
        }
        static double Unit(uint key) { return (Hash(key) >> 8) / 16777216.0; }

        // 칠한 곳을 감싸는 사각형. 없으면 false.
        public static bool Bounds(byte[] cov, int w, int h, out int x0, out int y0, out int x1, out int y1)
        {
            x0 = w; y0 = h; x1 = 0; y1 = 0;
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    if (cov[row + x] == 0) continue;
                    if (x < x0) x0 = x;
                    if (x + 1 > x1) x1 = x + 1;
                    if (y < y0) y0 = y;
                    if (y + 1 > y1) y1 = y + 1;
                }
            }
            return x1 > x0 && y1 > y0;
        }

        // 둘레(ring)와, (dx,dy)만큼 떨어진 곳의 둘레가 얼마나 다른가. 겹치거나 그림 밖으로 나가면 무한대.
        static double Score(int[] P, int W, int H, byte[] role, int wx0, int wy0, int ww, int wh, int dx, int dy)
        {
            if (Math.Abs(dx) < ww && Math.Abs(dy) < wh) return double.PositiveInfinity;
            if (wx0 + dx < 0 || wy0 + dy < 0 || wx0 + ww + dx > W || wy0 + wh + dy > H) return double.PositiveInfinity;
            double sum = 0;
            long n = 0;
            for (int y = 0; y < wh; y++)
            {
                for (int x = 0; x < ww; x++)
                {
                    if (role[y * ww + x] != Ring) continue;
                    int t = P[(wy0 + y) * W + wx0 + x];
                    int s = P[(wy0 + y + dy) * W + wx0 + x + dx];
                    for (int c = 0; c < 4; c++) { double d = Ch(t, c) - Ch(s, c); sum += d * d; }
                    n++;
                }
            }
            return n > 0 ? sum / n : double.PositiveInfinity;
        }

        // 구멍(HOLE) 값을 둘레(RING)에 묶인 채 매끄럽게 푼다. 반 크기에서 먼저 풀어 출발점으로 쓰니
        // 큰 자리도 몇 번 안 돌고 자리 잡는다.
        static void Solve(float[] value, byte[] role, int w, int h, int depth)
        {
            int iterations = 300;
            if (w > 32 && h > 32 && depth < 16)
            {
                int cw = (w + 1) / 2, ch = (h + 1) / 2;
                var coarse = new float[cw * ch * 4];
                var coarseRole = new byte[cw * ch];
                var ks = new float[4]; var hs = new float[4];
                for (int y = 0; y < ch; y++)
                {
                    for (int x = 0; x < cw; x++)
                    {
                        int known = 0, hole = 0;
                        Array.Clear(ks, 0, 4); Array.Clear(hs, 0, 4);
                        for (int j = 0; j < 2; j++)
                            for (int i = 0; i < 2; i++)
                            {
                                int fx = x * 2 + i, fy = y * 2 + j;
                                if (fx >= w || fy >= h) continue;
                                int p = fy * w + fx;
                                if (role[p] == Ring) { known++; for (int c = 0; c < 4; c++) ks[c] += value[p * 4 + c]; }
                                else if (role[p] == Hole) { hole++; for (int c = 0; c < 4; c++) hs[c] += value[p * 4 + c]; }
                            }
                        int q = y * cw + x;
                        if (known > 0) { coarseRole[q] = Ring; for (int c = 0; c < 4; c++) coarse[q * 4 + c] = ks[c] / known; }
                        else if (hole > 0) { coarseRole[q] = Hole; for (int c = 0; c < 4; c++) coarse[q * 4 + c] = hs[c] / hole; }
                    }
                }
                Solve(coarse, coarseRole, cw, ch, depth + 1);
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int p = y * w + x, q = (y / 2) * cw + x / 2;
                        if (role[p] == Hole && coarseRole[q] == Hole) Array.Copy(coarse, q * 4, value, p * 4, 4);
                    }
                iterations = 40;
            }

            const float omega = 1.8f;
            var sum = new float[4];
            for (int it = 0; it < iterations; it++)
            {
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        int p = y * w + x;
                        if (role[p] != Hole) continue;
                        sum[0] = sum[1] = sum[2] = sum[3] = 0;
                        int n = 0;
                        for (int k = 0; k < 4; k++)
                        {
                            int nx = k == 0 ? x - 1 : k == 1 ? x + 1 : x;
                            int ny = k == 2 ? y - 1 : k == 3 ? y + 1 : y;
                            if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                            int q = ny * w + nx;
                            if (role[q] == Outside) continue;
                            for (int c = 0; c < 4; c++) sum[c] += value[q * 4 + c];
                            n++;
                        }
                        if (n == 0) continue;
                        for (int c = 0; c < 4; c++) value[p * 4 + c] += omega * (sum[c] / n - value[p * 4 + c]);
                    }
                }
            }
        }

        // coverage: 그림 크기, 0~255 = 얼마나 고칠지. 고친 곳이 있으면 true.
        public static bool Spot(Canvas32 img, byte[] coverage, HealMode mode, double opacity, uint seed)
        {
            return Spot(img, coverage, mode, opacity, seed, false);
        }

        // needSource: 무늬를 가져올 곳을 못 찾으면 매끄럽게로 넘어가지 말고 손대지 않은 채 false.
        public static bool Spot(Canvas32 img, byte[] coverage, HealMode mode, double opacity, uint seed, bool needSource)
        {
            int W = img.W, H = img.H;
            int[] P = img.P;
            int bx0, by0, bx1, by1;
            if (!Bounds(coverage, W, H, out bx0, out by0, out bx1, out by1)) return false;
            int bw = bx1 - bx0, bh = by1 - by0, size = Math.Max(bw, bh);
            int ring = Math.Max(2, Math.Min(16, size / 8));

            // 작업 상자: 고칠 자리 + 둘레, 그림 안으로 자른다.
            int wx0 = Math.Max(0, bx0 - ring), wy0 = Math.Max(0, by0 - ring);
            int wx1 = Math.Min(W, bx1 + ring), wy1 = Math.Min(H, by1 + ring);
            int ww = wx1 - wx0, wh = wy1 - wy0, wn = ww * wh;

            var role = new byte[wn];
            var near = new byte[wn];
            var prefix = new int[Math.Max(ww, wh) + 1];
            var value = new float[wn * 4];

            for (int y = 0; y < wh; y++)
                for (int x = 0; x < ww; x++)
                    role[y * ww + x] = coverage[(wy0 + y) * W + wx0 + x] != 0 ? Hole : Outside;

            // 둘레: 자리에서 ring 안쪽 픽셀(정사각형 팽창, 가로 한 번 세로 한 번).
            for (int y = 0; y < wh; y++)
            {
                prefix[0] = 0;
                for (int x = 0; x < ww; x++) prefix[x + 1] = prefix[x] + (role[y * ww + x] == Hole ? 1 : 0);
                for (int x = 0; x < ww; x++)
                {
                    int lo = Math.Max(0, x - ring), hi = Math.Min(ww, x + ring + 1);
                    near[y * ww + x] = (byte)(prefix[hi] - prefix[lo] > 0 ? 1 : 0);
                }
            }
            for (int x = 0; x < ww; x++)
            {
                prefix[0] = 0;
                for (int y = 0; y < wh; y++) prefix[y + 1] = prefix[y] + near[y * ww + x];
                for (int y = 0; y < wh; y++)
                {
                    int lo = Math.Max(0, y - ring), hi = Math.Min(wh, y + ring + 1);
                    if (role[y * ww + x] == Outside && prefix[hi] - prefix[lo] > 0) role[y * ww + x] = Ring;
                }
            }
            long ringCount = 0;
            for (int p = 0; p < wn; p++) if (role[p] == Ring) ringCount++;
            if (ringCount == 0) return false;

            // 무늬를 가져올 곳 (내용 인식·가까운 곳)
            int ox = 0, oy = 0;
            bool haveSource = false;
            if (mode != HealMode.Smooth)
            {
                double[] factors = { 1.05, 1.35, 1.75, 2.25, 2.8 };
                int count = mode == HealMode.Proximity ? 2 : 5;
                double best = double.PositiveInfinity;
                for (int f = 0; f < count; f++)
                {
                    for (int a = 0; a < 24; a++)
                    {
                        double angle = a * Math.PI / 12.0;
                        int dx = (int)Math.Round(Math.Cos(angle) * factors[f] * ww);
                        int dy = (int)Math.Round(Math.Sin(angle) * factors[f] * wh);
                        double score = Score(P, W, H, role, wx0, wy0, ww, wh, dx, dy);
                        if (double.IsInfinity(score)) continue;
                        score *= mode == HealMode.Proximity ? 1.0 + 0.6 * f : 1.0 + 0.1 * f;   // 가까운 쪽이 비기면 이긴다
                        if (score < best) { best = score; ox = dx; oy = dy; }
                    }
                }
                if (!double.IsInfinity(best))
                {
                    // 되풀이되는 무늬가 맞물리게 자리를 조금씩 비벼 본다.
                    int cx = ox, cy = oy;
                    double refined = Score(P, W, H, role, wx0, wy0, ww, wh, cx, cy);
                    for (int j = -3; j <= 3; j++)
                        for (int i = -3; i <= 3; i++)
                        {
                            double score = Score(P, W, H, role, wx0, wy0, ww, wh, cx + i, cy + j);
                            if (score < refined) { refined = score; ox = cx + i; oy = cy + j; }
                        }
                    haveSource = true;
                }
            }

            if (needSource && !haveSource) return false;

            // 막: 원본과 가져온 조각의 둘레 차이(매끄럽게면 원본 자체)를 자리 전체로 편다.
            var mean = new double[4];
            var detail = new double[3];
            for (int y = 0; y < wh; y++)
            {
                for (int x = 0; x < ww; x++)
                {
                    int p = y * ww + x;
                    if (role[p] != Ring) { value[p * 4] = value[p * 4 + 1] = value[p * 4 + 2] = value[p * 4 + 3] = 0; continue; }
                    int ix = wx0 + x, iy = wy0 + y;
                    int t = P[iy * W + ix];
                    int s = haveSource ? P[(iy + oy) * W + ix + ox] : 0;
                    for (int c = 0; c < 4; c++)
                    {
                        value[p * 4 + c] = Ch(t, c) - (haveSource ? Ch(s, c) : 0);
                        mean[c] += value[p * 4 + c];
                    }
                    if (!haveSource)
                    {
                        // 둘레의 잔결: 픽셀마다 이웃 평균과의 차이
                        for (int c = 0; c < 3; c++)
                        {
                            double around = 0; int n = 0;
                            for (int k = 0; k < 4; k++)
                            {
                                int nx = k == 0 ? ix - 1 : k == 1 ? ix + 1 : ix;
                                int ny = k == 2 ? iy - 1 : k == 3 ? iy + 1 : iy;
                                if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
                                around += Ch(P[ny * W + nx], c);
                                n++;
                            }
                            if (n > 0) { double d = Ch(t, c) - around / n; detail[c] += d * d; }
                        }
                    }
                }
            }
            for (int c = 0; c < 4; c++) mean[c] /= ringCount;
            for (int p = 0; p < wn; p++)
                if (role[p] == Hole) for (int c = 0; c < 4; c++) value[p * 4 + c] = (float)mean[c];
            Solve(value, role, ww, wh, 0);
            for (int c = 0; c < 3; c++) detail[c] = Math.Sqrt(detail[c] / ringCount) * 0.9;

            var outv = new double[4];
            for (int y = 0; y < wh; y++)
            {
                for (int x = 0; x < ww; x++)
                {
                    int p = y * ww + x;
                    if (role[p] != Hole) continue;
                    int ix = wx0 + x, iy = wy0 + y;
                    int ti = iy * W + ix;
                    int t = P[ti];
                    int s = haveSource ? P[(iy + oy) * W + ix + ox] : 0;
                    double amount = coverage[ti] / 255.0 * opacity;
                    double grain = 0;
                    if (!haveSource)
                    {
                        uint key = Hash(seed ^ Hash((uint)(iy * W + ix)));
                        double u1 = Unit(key), u2 = Unit(key ^ 0x68e31da4U);
                        grain = Math.Sqrt(-2.0 * Math.Log(1.0 - u1)) * Math.Cos(2.0 * Math.PI * u2);
                    }
                    for (int c = 0; c < 4; c++)
                    {
                        double healed = (haveSource ? Ch(s, c) : 0) + value[p * 4 + c] + (c < 3 ? grain * detail[c] : 0);
                        double tc = Ch(t, c);
                        outv[c] = tc + (healed - tc) * amount;
                    }
                    P[ti] = Canvas32.Pack(B(outv[3]), B(outv[0]), B(outv[1]), B(outv[2]));
                }
            }
            return true;
        }

        static byte B(double v) { return (byte)Math.Round(v < 0 ? 0 : v > 255 ? 255 : v); }

        // ---------- 내용 인식 채우기 ----------

        static uint NextRandom(ref uint state) { unchecked { state = state * 1664525u + 1013904223u; } return state; }

        static double Match(int[] P, byte[] known, int w, int h, int p, int q, int radius)
        {
            int px = p % w, py = p / w, qx = q % w, qy = q / w, count = 0;
            double sum = 0;
            for (int dy = -radius; dy <= radius; dy++)
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int x = px + dx, y = py + dy, sx = qx + dx, sy = qy + dy;
                    if (x < 0 || y < 0 || x >= w || y >= h || sx < 0 || sy < 0 || sx >= w || sy >= h || known[y * w + x] == 0) continue;
                    int a = P[y * w + x], b = P[sy * w + sx];
                    for (int c = 0; c < 4; c++) { int d = Ch(a, c) - Ch(b, c); sum += d * d; }
                    count++;
                }
            return count > 0 ? sum / count : double.MaxValue;
        }

        // mask(그림 크기)가 0이 아닌 곳을 채운다. 가져올 픽셀이 하나도 없으면 false.
        // 고르지 않은 불투명 픽셀이 맞춰볼 대상이자 가져올 곳이다. 고르지 않은 투명 픽셀은 둘 다 아니다.
        public static bool ContentFill(Canvas32 img, byte[] mask)
        {
            int w = img.W, h = img.H, n = w * h;
            int[] P = img.P;
            var known = new byte[n];
            var target = new byte[n];
            var valid = new byte[n];
            var queued = new byte[n];
            var donors = new int[n];
            var queue = new int[n];
            var chosen = new int[n];
            int radius = (w >= 5 && h >= 5) ? 2 : 0;
            int missing = 0, donorCount = 0, head = 0, tail = 0, scan = 0;

            for (int p = 0; p < n; p++)
            {
                target[p] = (byte)(mask[p] != 0 ? 1 : 0);
                known[p] = (byte)(target[p] == 0 && (uint)P[p] >> 24 == 255 ? 1 : 0);
                chosen[p] = -1;
                if (target[p] != 0) missing++;
            }
            if (missing == 0) return true;

            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int p = y * w + x;
                    if (known[p] == 0) continue;
                    bool ok = true;
                    for (int dy = -radius; dy <= radius && ok; dy++)
                        for (int dx = -radius; dx <= radius; dx++)
                        {
                            int sx = x + dx, sy = y + dy;
                            if (sx < 0 || sy < 0 || sx >= w || sy >= h || known[sy * w + sx] == 0) { ok = false; break; }
                        }
                    if (ok) { valid[p] = 1; donors[donorCount++] = p; }
                }
            if (donorCount == 0) return false;

            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int p = y * w + x;
                    if (target[p] != 0 && ((x > 0 && known[p - 1] != 0) || (x + 1 < w && known[p + 1] != 0) ||
                                           (y > 0 && known[p - w] != 0) || (y + 1 < h && known[p + w] != 0)))
                    { queue[tail++] = p; queued[p] = 1; }
                }

            uint seed = 0x6d2b79f5;
            var neighbors = new int[4];
            for (;;)
            {
                while (head < tail)
                {
                    int p = queue[head++], x = p % w, y = p / w, best = -1;
                    double score = double.MaxValue;
                    neighbors[0] = x > 0 ? p - 1 : -1;
                    neighbors[1] = x + 1 < w ? p + 1 : -1;
                    neighbors[2] = y > 0 ? p - w : -1;
                    neighbors[3] = y + 1 < h ? p + w : -1;
                    // 이웃이 쓴 자리를 이어받고, 무작위로 몇 군데 더 찔러본다.
                    for (int k = 0; k < 28; k++)
                    {
                        int q = -1;
                        if (k < 4)
                        {
                            int t = neighbors[k];
                            if (t >= 0) q = (chosen[t] >= 0 ? chosen[t] : t) + (p - t);
                        }
                        else q = donors[(int)(NextRandom(ref seed) % (uint)donorCount)];
                        if (q < 0 || q >= n || valid[q] == 0) continue;
                        double s = Match(P, known, w, h, p, q, radius);
                        if (best < 0 || s < score) { score = s; best = q; }
                    }
                    if (best < 0) best = donors[0];
                    for (int r = 64; r >= 1; r /= 2)
                    {
                        int qx = best % w + (int)(NextRandom(ref seed) % (uint)(2 * r + 1)) - r;
                        int qy = best / w + (int)(NextRandom(ref seed) % (uint)(2 * r + 1)) - r;
                        if (qx < 0 || qy < 0 || qx >= w || qy >= h || valid[qy * w + qx] == 0) continue;
                        int q = qy * w + qx;
                        double s = Match(P, known, w, h, p, q, radius);
                        if (s < score) { score = s; best = q; }
                    }
                    P[p] = P[best];
                    known[p] = 1; chosen[p] = best;
                    for (int k = 0; k < 4; k++)
                    {
                        int q = neighbors[k];
                        if (q >= 0 && target[q] != 0 && known[q] == 0 && queued[q] == 0) { queued[q] = 1; queue[tail++] = q; }
                    }
                }
                // 투명한 곳만 닿아 있는 섬은 아무 데서나 시작해 번져 나간다.
                while (scan < n && (target[scan] == 0 || known[scan] != 0)) scan++;
                if (scan >= n) break;
                queue[tail++] = scan; queued[scan] = 1;
            }
            return true;
        }
    }
}
