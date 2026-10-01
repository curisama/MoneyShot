// Money Shot — 필터
//
// 가우시안 흐림·동작 흐림·노이즈·비네팅·블룸·톤 대비·렌즈 왜곡·노출·그라디언트 맵·그레인.
// 모두 색 보정과 같은 길을 탄다: 원본에서 결과를 새로 만드는 함수(ToneFn)를 창이 넘긴다.
//
// Compositor(MIT, Wonder Assembly LLC)의 Filters.swift·ImageAdjustments.swift와
// NoisePixels.c·LensPixels.c·AdjustPixels.c(grain, vignette, tonal contrast)를 옮겼다.
// 원본이 Core Image로 하던 흐림·동작 흐림·블룸은 같은 뜻의 계산을 직접 한다.
using System;

namespace MoneyShot
{
    public static class Filters
    {
        static double C01(double v) { return v < 0 ? 0 : v > 1 ? 1 : v; }
        static byte B(double v01) { return (byte)Math.Max(0, Math.Min(255, (int)Math.Round(v01 * 255))); }
        static double Rec709(double r, double g, double b) { return 0.2126 * r + 0.7152 * g + 0.0722 * b; }

        static uint Hash(uint x)
        {
            unchecked { x ^= x >> 16; x *= 0x7feb352dU; x ^= x >> 15; x *= 0x846ca68bU; x ^= x >> 16; }
            return x;
        }
        static float Unit(uint key) { return (Hash(key) >> 8) * (1.0f / 16777216.0f); }

        // ---------- 흐림의 바탕: 알파를 곱한 평면 넷 ----------
        // 투명한 곳의 색이 번져 들어오지 않게(검은 테두리) 알파를 곱해 흐리고 다시 나눈다.

        // 가장자리가 전부 불투명하면(사진·캡처) 판 밖을 가장자리 색으로 본다. 그래야 둘레가 비쳐 보이지 않는다.
        // 누끼처럼 투명한 곳이 있으면 판 밖은 투명이다.
        static bool OpaqueEdges(Canvas32 c)
        {
            int W = c.W, H = c.H;
            for (int x = 0; x < W; x++) if (Canvas32.A(c.P[x]) < 250 || Canvas32.A(c.P[(H - 1) * W + x]) < 250) return false;
            for (int y = 0; y < H; y++) if (Canvas32.A(c.P[y * W]) < 250 || Canvas32.A(c.P[y * W + W - 1]) < 250) return false;
            return true;
        }

        static float[][] Planes(Canvas32 c)
        {
            int n = c.P.Length;
            var p = new float[4][];
            for (int k = 0; k < 4; k++) p[k] = new float[n];
            for (int i = 0; i < n; i++)
            {
                int v = c.P[i];
                float a = Canvas32.A(v) / 255f;
                p[3][i] = a;
                p[0][i] = Canvas32.R(v) / 255f * a; p[1][i] = Canvas32.G(v) / 255f * a; p[2][i] = Canvas32.B(v) / 255f * a;
            }
            return p;
        }

        static void FromPlanes(float[][] p, Canvas32 dst)
        {
            for (int i = 0; i < dst.P.Length; i++)
            {
                float a = p[3][i];
                if (a <= 0.0005f) { dst.P[i] = 0; continue; }
                dst.P[i] = Canvas32.Pack(B(a), B(p[0][i] / a), B(p[1][i] / a), B(p[2][i] / a));
            }
        }

        // 한 줄 상자 흐림. clamp면 판 밖을 끝 값으로, 아니면 0으로.
        static void BoxLine(float[] s, float[] d, int start, int step, int len, int r, bool clamp)
        {
            float span = 2 * r + 1, sum = 0;
            for (int k = -r; k <= r; k++)
            {
                int j = k < 0 ? (clamp ? 0 : -1) : k >= len ? (clamp ? len - 1 : -1) : k;
                sum += j < 0 ? 0 : s[start + j * step];
            }
            for (int k = 0; k < len; k++)
            {
                d[start + k * step] = sum / span;
                int o = k - r, i = k + r + 1;
                int jo = o < 0 ? (clamp ? 0 : -1) : o;
                int ji = i >= len ? (clamp ? len - 1 : -1) : i;
                sum -= jo < 0 ? 0 : s[start + jo * step];
                sum += ji < 0 ? 0 : s[start + ji * step];
            }
        }

        public static void BlurPlane(float[] a, int W, int H, double sigma, bool clamp)
        {
            if (sigma < 0.3) return;
            int r = Math.Max(1, (int)Math.Round((Math.Sqrt(4 * sigma * sigma + 1) - 1) / 2));
            var t = new float[a.Length];
            for (int pass = 0; pass < 3; pass++)
            {
                for (int y = 0; y < H; y++) BoxLine(a, t, y * W, 1, W, r, clamp);
                for (int x = 0; x < W; x++) BoxLine(t, a, x, W, H, r, clamp);
            }
        }

        // ---------- 가우시안 흐림 ----------

        public static ToneFn Gaussian(double sigma)
        {
            return delegate(Canvas32 src, Canvas32 dst)
            {
                bool clamp = OpaqueEdges(src);
                var p = Planes(src);
                for (int k = 0; k < 4; k++) BlurPlane(p[k], src.W, src.H, sigma, clamp);
                FromPlanes(p, dst);
            };
        }

        // ---------- 동작 흐림 ----------
        // 포토샵처럼 길이 전체에 고르게 번진다. 각도는 수평에서 반시계, 화면 y는 아래로 자라니 부호를 뒤집는다.

        public static ToneFn Motion(double angleDeg, double distance)
        {
            return delegate(Canvas32 src, Canvas32 dst)
            {
                int W = src.W, H = src.H;
                bool clamp = OpaqueEdges(src);
                var p = Planes(src);
                double rad = angleDeg * Math.PI / 180;
                double ux = Math.Cos(rad), uy = -Math.Sin(rad);
                // 기울기가 작은 축을 따라 한 칸씩 걷는다: 그 축은 정수 자리라 다른 축만 둘 사이를 섞으면 된다.
                bool alongX = Math.Abs(ux) >= Math.Abs(uy);
                double dom = alongX ? Math.Abs(ux) : Math.Abs(uy);
                double slope = alongX ? uy / ux : ux / uy;
                int K = Math.Max(1, (int)Math.Round(distance * dom / 2));
                int count = 2 * K + 1;
                var o = new float[4][];
                for (int k = 0; k < 4; k++) o[k] = new float[W * H];
                var offI = new int[count]; var offF = new int[count]; var frac = new float[count];
                for (int t = 0; t < count; t++)
                {
                    int step = t - K;
                    double other = step * slope;
                    int fl = (int)Math.Floor(other);
                    offI[t] = step; offF[t] = fl; frac[t] = (float)(other - fl);
                }
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        float s0 = 0, s1 = 0, s2 = 0, s3 = 0;
                        for (int t = 0; t < count; t++)
                        {
                            for (int j = 0; j < 2; j++)
                            {
                                float w = j == 0 ? 1 - frac[t] : frac[t];
                                if (w <= 0) continue;
                                int px = alongX ? x + offI[t] : x + offF[t] + j;
                                int py = alongX ? y + offF[t] + j : y + offI[t];
                                if (px < 0 || py < 0 || px >= W || py >= H)
                                {
                                    if (!clamp) continue;
                                    px = px < 0 ? 0 : px >= W ? W - 1 : px; py = py < 0 ? 0 : py >= H ? H - 1 : py;
                                }
                                int q = py * W + px;
                                s0 += p[0][q] * w; s1 += p[1][q] * w; s2 += p[2][q] * w; s3 += p[3][q] * w;
                            }
                        }
                        int d = y * W + x;
                        o[0][d] = s0 / count; o[1][d] = s1 / count; o[2][d] = s2 / count; o[3][d] = s3 / count;
                    }
                FromPlanes(o, dst);
            };
        }

        // ---------- 노이즈 추가 ----------

        public static ToneFn Noise(double amount, bool gaussian, bool mono, uint seed)
        {
            return delegate(Canvas32 src, Canvas32 dst)
            {
                float spread = (float)(amount / 100.0 * 127.5);
                int W = src.W;
                var ch = new int[3];
                for (int i = 0; i < src.P.Length; i++)
                {
                    int v = src.P[i];
                    int a = Canvas32.A(v);
                    if (a == 0) { dst.P[i] = v; continue; }
                    uint px = (uint)(i % W), py = (uint)(i / W);
                    uint bse = Hash(seed ^ Hash(unchecked(px * 0x9e3779b9U) ^ Hash(unchecked(py * 0x85ebca6bU))));
                    for (int c = 0; c < 3; c++)
                    {
                        uint key = mono ? bse : unchecked(bse + (uint)c * 0x9e3779b9U);
                        float n;
                        if (gaussian)
                        {
                            float u1 = Unit(key), u2 = Unit(key ^ 0x68e31da4U);
                            n = (float)(Math.Sqrt(-2.0 * Math.Log(1.0 - u1)) * Math.Cos(6.2831853 * u2)) * spread * (2f / 3f);
                        }
                        else n = (Unit(key) * 2f - 1f) * spread;
                        int sv = c == 0 ? Canvas32.R(v) : c == 1 ? Canvas32.G(v) : Canvas32.B(v);
                        ch[c] = Math.Max(0, Math.Min(255, (int)Math.Round(sv + n)));
                    }
                    dst.P[i] = Canvas32.Pack((byte)a, (byte)ch[0], (byte)ch[1], (byte)ch[2]);
                }
            };
        }

        // ---------- 비네팅 ----------

        static double VignetteMask(double px, double py, double w, double h, double midpoint, double roundness, double feather)
        {
            double nx = px / w * 2 - 1, ny = py / h * 2 - 1;
            double square = Math.Max(Math.Abs(nx), Math.Abs(ny));
            double circle = Math.Sqrt(nx * nx + ny * ny) / Math.Sqrt(2.0);
            double shape = (1 - roundness / 100) * 0.5;
            double dist = circle + (square - circle) * shape;
            double start = midpoint / 100 * 0.85;
            double soft = Math.Max(0.05, feather / 100);
            double t = C01((dist - start) / soft);
            return t * t * (3 - 2 * t);
        }

        public static ToneFn Vignette(double amount, double midpoint, double roundness, double feather, double highlights, FxColor col)
        {
            return delegate(Canvas32 src, Canvas32 dst)
            {
                int W = src.W, H = src.H;
                double strength = C01(amount / 100);
                double cr = col.R / 255.0, cg = col.G / 255.0, cb = col.B / 255.0;
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        int i = y * W + x, v = src.P[i];
                        if (Canvas32.A(v) == 0 || strength <= 0) { dst.P[i] = v; continue; }
                        double m = VignetteMask(x + 0.5, y + 0.5, W, H, midpoint, roundness, feather);
                        if (m <= 0) { dst.P[i] = v; continue; }
                        double r = Canvas32.R(v) / 255.0, g = Canvas32.G(v) / 255.0, b = Canvas32.B(v) / 255.0;
                        double bright = C01((Rec709(r, g, b) - 0.45) / 0.55);
                        double e = strength * m * (1 - highlights / 100 * bright);
                        dst.P[i] = Canvas32.Pack(Canvas32.A(v), B(r + (cr - r) * e), B(g + (cg - g) * e), B(b + (cb - b) * e));
                    }
            };
        }

        // ---------- 블룸 / 광채 ----------
        // 밝은 곳을 크게 흐려 위에 스크린으로 얹는다. 밝을수록 더 번진다.

        public static ToneFn Bloom(double amount, double radius)
        {
            return delegate(Canvas32 src, Canvas32 dst)
            {
                int W = src.W, H = src.H, n = W * H;
                bool clamp = OpaqueEdges(src);
                double k = amount / 50.0;
                var p = new float[3][];
                for (int c = 0; c < 3; c++) p[c] = new float[n];
                for (int i = 0; i < n; i++)
                {
                    int v = src.P[i];
                    float a = Canvas32.A(v) / 255f;
                    float r = Canvas32.R(v) / 255f, g = Canvas32.G(v) / 255f, b = Canvas32.B(v) / 255f;
                    // 밝은 쪽만 빛을 낸다
                    float l = (float)Rec709(r, g, b);
                    float w = Math.Max(0, (l - 0.35f) / 0.65f) * a;
                    p[0][i] = r * w; p[1][i] = g * w; p[2][i] = b * w;
                }
                for (int c = 0; c < 3; c++) BlurPlane(p[c], W, H, radius, clamp);
                for (int i = 0; i < n; i++)
                {
                    int v = src.P[i];
                    int a = Canvas32.A(v);
                    if (a == 0) { dst.P[i] = v; continue; }
                    double r = Canvas32.R(v) / 255.0, g = Canvas32.G(v) / 255.0, b = Canvas32.B(v) / 255.0;
                    double gr = C01(p[0][i] * k), gg = C01(p[1][i] * k), gb = C01(p[2][i] * k);
                    dst.P[i] = Canvas32.Pack((byte)a, B(1 - (1 - r) * (1 - gr)), B(1 - (1 - g) * (1 - gg)), B(1 - (1 - b) * (1 - gb)));
                }
            };
        }

        // ---------- 톤 대비 ----------

        static double Smooth(double lo, double hi, double v) { double t = C01((v - lo) / (hi - lo)); return t * t * (3 - 2 * t); }

        public static ToneFn TonalContrast(double amount, double radius, double shadows, double midtones, double highlights)
        {
            return delegate(Canvas32 src, Canvas32 dst)
            {
                int W = src.W, H = src.H, n = W * H;
                if (amount <= 0 || (shadows == 0 && midtones == 0 && highlights == 0)) { Array.Copy(src.P, dst.P, n); return; }
                bool clamp = OpaqueEdges(src);
                var lum = new float[n];
                for (int i = 0; i < n; i++)
                {
                    int v = src.P[i];
                    lum[i] = (float)Rec709(Canvas32.R(v) / 255.0, Canvas32.G(v) / 255.0, Canvas32.B(v) / 255.0);
                }
                var baseL = (float[])lum.Clone();
                BlurPlane(baseL, W, H, radius, clamp);
                double strength = amount / 50.0;
                for (int i = 0; i < n; i++)
                {
                    int v = src.P[i];
                    if (Canvas32.A(v) == 0) { dst.P[i] = v; continue; }
                    double l = lum[i], bl = baseL[i];
                    double sw = 1 - Smooth(0.15, 0.5, bl), hw = Smooth(0.5, 0.85, bl), mw = 1 - sw - hw;
                    double weight = (shadows * sw + midtones * mw + highlights * hw) / 100;
                    double delta = 0.18 * Math.Tanh((l - bl) * 6) * weight * strength * (4 * l * (1 - l));
                    dst.P[i] = Canvas32.Pack(Canvas32.A(v), B(C01(Canvas32.R(v) / 255.0 + delta)),
                                             B(C01(Canvas32.G(v) / 255.0 + delta)), B(C01(Canvas32.B(v) / 255.0 + delta)));
                }
            };
        }

        // ---------- 렌즈 왜곡 보정 ----------
        // +면 술통형(바깥으로 휜 선)을, -면 실패형(안으로 휜 선)을 편다. ±100에서 모서리가 중심 거리의 35%만큼 움직인다.

        public static ToneFn Lens(double distortion)
        {
            double k = distortion / 100 * 0.35;
            return delegate(Canvas32 src, Canvas32 dst)
            {
                int W = src.W, H = src.H;
                double cx = W * 0.5, cy = H * 0.5, hd2 = cx * cx + cy * cy;
                var s = new double[4];
                for (int y = 0; y < H; y++)
                {
                    double dy = y + 0.5 - cy;
                    for (int x = 0; x < W; x++)
                    {
                        double dx = x + 0.5 - cx;
                        double scale = 1 - k * (dx * dx + dy * dy) / hd2;
                        double sx = cx + dx * scale - 0.5, sy = cy + dy * scale - 0.5;
                        int x0 = (int)Math.Floor(sx), y0 = (int)Math.Floor(sy);
                        double fx = sx - x0, fy = sy - y0;
                        s[0] = s[1] = s[2] = s[3] = 0;
                        // 알파를 곱해 섞는다 — 투명한 곳의 색이 묻어나지 않게
                        for (int j = 0; j < 2; j++)
                        {
                            int row = y0 + j;
                            if (row < 0 || row >= H) continue;
                            double wy = j == 1 ? fy : 1 - fy;
                            for (int i = 0; i < 2; i++)
                            {
                                int col = x0 + i;
                                if (col < 0 || col >= W) continue;
                                double w = wy * (i == 1 ? fx : 1 - fx);
                                if (w <= 0) continue;
                                int v = src.P[row * W + col];
                                double a = Canvas32.A(v) * w;
                                s[0] += Canvas32.R(v) * a; s[1] += Canvas32.G(v) * a; s[2] += Canvas32.B(v) * a; s[3] += a;
                            }
                        }
                        int d = y * W + x;
                        if (s[3] <= 0.01) { dst.P[d] = 0; continue; }
                        dst.P[d] = Canvas32.Pack((byte)Math.Min(255, Math.Round(s[3])),
                            (byte)Math.Round(s[0] / s[3]), (byte)Math.Round(s[1] / s[3]), (byte)Math.Round(s[2] / s[3]));
                    }
                }
            };
        }

        // ---------- 노출 ----------
        // 선형 빛으로 풀어 노출(스톱)을 곱하고 오프셋을 더하고 감마를 건 뒤 다시 sRGB로.

        public static ToneFn Exposure(double stops, double offset, double gamma)
        {
            var t = new byte[256];
            double scale = Math.Pow(2, stops);
            for (int i = 0; i < 256; i++)
            {
                double e = i / 255.0;
                double lin = e <= 0.04045 ? e / 12.92 : Math.Pow((e + 0.055) / 1.055, 2.4);
                lin = Math.Pow(Math.Max(0, lin * scale + offset), 1 / gamma);
                double o = lin <= 0.0031308 ? lin * 12.92 : 1.055 * Math.Pow(lin, 1 / 2.4) - 0.055;
                t[i] = B(C01(o));
            }
            return delegate(Canvas32 s, Canvas32 d) { Tone.ApplyLut(s, d, t, t, t); };
        }

        // ---------- 그라디언트 맵 ----------
        // 밝기에 따라 어두운 색 → 밝은 색으로 바꾼다.

        public static ToneFn GradientMap(FxColor dark, FxColor light, bool reversed)
        {
            var a = reversed ? light : dark;
            var b = reversed ? dark : light;
            var tr = new byte[256]; var tg = new byte[256]; var tb = new byte[256];
            for (int i = 0; i < 256; i++)
            {
                double t = i / 255.0;
                tr[i] = (byte)Math.Round(a.R + (b.R - a.R) * t);
                tg[i] = (byte)Math.Round(a.G + (b.G - a.G) * t);
                tb[i] = (byte)Math.Round(a.B + (b.B - a.B) * t);
            }
            return delegate(Canvas32 src, Canvas32 dst)
            {
                for (int i = 0; i < src.P.Length; i++)
                {
                    int v = src.P[i];
                    int al = Canvas32.A(v);
                    if (al == 0) { dst.P[i] = v; continue; }
                    int level = (2126 * Canvas32.R(v) + 7152 * Canvas32.G(v) + 722 * Canvas32.B(v) + 5000) / 10000;
                    if (level > 255) level = 255;
                    dst.P[i] = Canvas32.Pack((byte)al, tr[level], tg[level], tb[level]);
                }
            };
        }

        // ---------- 그레인 ----------
        // 크기를 따르는 매끄러운 잡음에 거칠기만큼 잔 입자를 섞는다. 필름처럼 중간 밝기에서 가장 잘 보인다.

        static uint Mix32(uint x) { return Hash(x); }

        static float Lattice(long ix, long iy, uint seed)
        {
            uint h;
            unchecked { h = Mix32((uint)ix * 0x9E3779B1U ^ Mix32((uint)iy * 0x85EBCA77U ^ seed)); }
            return (h & 0xFFFF) / 65535f + (h >> 16) / 65535f - 1f;
        }

        static float GrainField(double u, double v, double scale, uint seed)
        {
            double cellX = Math.Floor(u / scale), cellY = Math.Floor(v / scale);
            float tx = (float)(u / scale - cellX), ty = (float)(v / scale - cellY);
            tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty);
            long ix = (long)cellX, iy = (long)cellY;
            float n00 = Lattice(ix, iy, seed), n10 = Lattice(ix + 1, iy, seed);
            float n01 = Lattice(ix, iy + 1, seed), n11 = Lattice(ix + 1, iy + 1, seed);
            float top = n00 + (n10 - n00) * tx, bottom = n01 + (n11 - n01) * tx;
            return (top + (bottom - top) * ty) * 1.6f;
        }

        public static ToneFn Grain(double amount, double size, double roughness, uint seed)
        {
            return delegate(Canvas32 src, Canvas32 dst)
            {
                if (amount <= 0) { Array.Copy(src.P, dst.P, src.P.Length); return; }
                float strength = (float)Math.Min(1, amount / 100) * 0.35f * 255f;
                float rough = (float)C01(roughness / 100);
                uint fineSeed = Mix32(seed ^ 0xA511E9B3U);
                double detail = Math.Max(0.5, size * 0.35);
                int W = src.W, H = src.H;
                for (int y = 0; y < H; y++)
                {
                    double v = y + 0.5;
                    for (int x = 0; x < W; x++)
                    {
                        int i = y * W + x, p = src.P[i];
                        int a = Canvas32.A(p);
                        if (a == 0) { dst.P[i] = p; continue; }
                        double u = x + 0.5;
                        float sm = GrainField(u, v, size, seed), fi = GrainField(u, v, detail, fineSeed);
                        float noise = sm + (fi - sm) * rough;
                        float r = Canvas32.R(p), g = Canvas32.G(p), b = Canvas32.B(p);
                        float level = Math.Min(1f, (0.2126f * r + 0.7152f * g + 0.0722f * b) / 255f);
                        float delta = noise * strength * (0.4f + 2.4f * level * (1 - level));
                        dst.P[i] = Canvas32.Pack((byte)a, (byte)Math.Max(0, Math.Min(255, r + delta + 0.5f)),
                            (byte)Math.Max(0, Math.Min(255, g + delta + 0.5f)), (byte)Math.Max(0, Math.Min(255, b + delta + 0.5f)));
                    }
                }
            };
        }
    }
}
