// Money Shot — 픽셀 연산
//
// 편집기의 파괴적 도구(모자이크, 지우개 3종, 효과)가 전부 여기로 내려온다.
// 모든 연산은 BGRA 32비트 int 배열 하나 위에서 직접 일어난다. 마우스를 끄는 동안
// 매 프레임 호출되므로 중간 객체를 만들지 않는 것이 중요하다.
using System;
using D = System.Drawing;

namespace MoneyShot
{
    // 픽셀 버퍼 한 장. int 하나가 BGRA 한 픽셀(비프리멀티플라이드)이다.
    public class Canvas32
    {
        public int[] P;
        public int W, H;

        public Canvas32(int w, int h) { W = w; H = h; P = new int[w * h]; }
        public Canvas32(int[] p, int w, int h) { P = p; W = w; H = h; }

        public Canvas32 Clone()
        {
            var c = new Canvas32(W, H);
            Array.Copy(P, c.P, P.Length);
            return c;
        }

        public bool In(int x, int y) { return x >= 0 && y >= 0 && x < W && y < H; }
        public int At(int x, int y) { return P[y * W + x]; }

        public static byte A(int c) { return (byte)((uint)c >> 24); }
        public static byte R(int c) { return (byte)(c >> 16); }
        public static byte G(int c) { return (byte)(c >> 8); }
        public static byte B(int c) { return (byte)c; }
        public static int Pack(byte a, byte r, byte g, byte b)
        { return (a << 24) | (r << 16) | (g << 8) | b; }

        public static Canvas32 From(D.Bitmap bmp)
        {
            var c = new Canvas32(bmp.Width, bmp.Height);
            var data = bmp.LockBits(new D.Rectangle(0, 0, bmp.Width, bmp.Height),
                                    D.Imaging.ImageLockMode.ReadOnly, D.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < bmp.Height; y++)
                    System.Runtime.InteropServices.Marshal.Copy(
                        data.Scan0 + y * data.Stride, c.P, y * bmp.Width, bmp.Width);
            }
            finally { bmp.UnlockBits(data); }
            return c;
        }

        public D.Bitmap ToBitmap()
        {
            var bmp = new D.Bitmap(W, H, D.Imaging.PixelFormat.Format32bppArgb);
            var data = bmp.LockBits(new D.Rectangle(0, 0, W, H),
                                    D.Imaging.ImageLockMode.WriteOnly, D.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < H; y++)
                    System.Runtime.InteropServices.Marshal.Copy(
                        P, y * W, data.Scan0 + y * data.Stride, W);
            }
            finally { bmp.UnlockBits(data); }
            return bmp;
        }
    }

    public static class Ops
    {
        // 두 색의 거리. 0~441 사이. 알파가 다르면 그만큼 더 멀게 본다.
        public static int Dist(int c1, int c2)
        {
            int dr = Canvas32.R(c1) - Canvas32.R(c2);
            int dg = Canvas32.G(c1) - Canvas32.G(c2);
            int db = Canvas32.B(c1) - Canvas32.B(c2);
            int da = Canvas32.A(c1) - Canvas32.A(c2);
            int d = (int)Math.Sqrt(dr * dr + dg * dg + db * db);
            return d + Math.Abs(da);
        }

        // 슬라이더 0~100을 실제 색 거리로. 낮은 쪽을 촘촘하게 둬야 미세 조정이 된다.
        public static int ToleranceToDistance(int pct)
        {
            double t = Math.Max(0, Math.Min(100, pct)) / 100.0;
            return (int)Math.Round(Math.Pow(t, 1.7) * 330);
        }

        // ---------- 모자이크 / 흐리기 ----------

        public static void Pixelate(Canvas32 c, D.Rectangle r, int cell)
        {
            r.Intersect(new D.Rectangle(0, 0, c.W, c.H));
            if (r.Width <= 0 || r.Height <= 0) return;
            cell = Math.Max(2, cell);

            for (int by = r.Top; by < r.Bottom; by += cell)
            {
                int bh = Math.Min(cell, r.Bottom - by);
                for (int bx = r.Left; bx < r.Right; bx += cell)
                {
                    int bw = Math.Min(cell, r.Right - bx);
                    long sa = 0, sr = 0, sg = 0, sb = 0;
                    int n = bw * bh;
                    for (int y = by; y < by + bh; y++)
                    {
                        int row = y * c.W;
                        for (int x = bx; x < bx + bw; x++)
                        {
                            int v = c.P[row + x];
                            sa += Canvas32.A(v); sr += Canvas32.R(v);
                            sg += Canvas32.G(v); sb += Canvas32.B(v);
                        }
                    }
                    int avg = Canvas32.Pack((byte)(sa / n), (byte)(sr / n), (byte)(sg / n), (byte)(sb / n));
                    for (int y = by; y < by + bh; y++)
                    {
                        int row = y * c.W;
                        for (int x = bx; x < bx + bw; x++) c.P[row + x] = avg;
                    }
                }
            }
        }

        // 박스 블러 3회 = 가우시안에 준하는 결과를 훨씬 싸게 얻는다.
        //
        // 두 가지를 조심해야 한다.
        //  1. 선택 영역 안에서만 평균을 내면, 반경이 영역 높이에 근접하는 순간 모든 픽셀이
        //     거의 같은 값을 평균하게 되어 판이 통째로 단색(회색)으로 주저앉는다.
        //     그래서 주변까지 넉넉히 끌어와 계산하고 가운데만 되돌려 쓴다.
        //  2. 반경 자체도 영역 크기에 비해 너무 크면 의미가 없다. 위아래로 묶어둔다.
        public static void Blur(Canvas32 c, D.Rectangle r, int radius)
        {
            var full = new D.Rectangle(0, 0, c.W, c.H);
            r.Intersect(full);
            if (r.Width <= 0 || r.Height <= 0 || radius < 1) return;

            int cap = Math.Max(1, Math.Min(r.Width, r.Height) / 4);
            radius = Math.Max(1, Math.Min(radius, cap));

            // 흐림이 번지는 거리만큼 바깥을 함께 읽는다.
            int pad = radius * 3;
            var ext = D.Rectangle.FromLTRB(r.Left - pad, r.Top - pad, r.Right + pad, r.Bottom + pad);
            ext.Intersect(full);

            var sub = new Canvas32(ext.Width, ext.Height);
            for (int y = 0; y < ext.Height; y++)
                Array.Copy(c.P, (ext.Top + y) * c.W + ext.Left, sub.P, y * sub.W, sub.W);

            var whole = new D.Rectangle(0, 0, sub.W, sub.H);
            for (int pass = 0; pass < 3; pass++)
            {
                BoxH(sub, whole, radius);
                BoxV(sub, whole, radius);
            }

            // 가운데(원래 고른 영역)만 되돌려 쓴다. 바깥은 건드리지 않는다.
            int ox = r.Left - ext.Left, oy = r.Top - ext.Top;
            for (int y = 0; y < r.Height; y++)
                Array.Copy(sub.P, (oy + y) * sub.W + ox, c.P, (r.Top + y) * c.W + r.Left, r.Width);
        }

        static void BoxH(Canvas32 c, D.Rectangle r, int rad)
        {
            var line = new int[r.Width];
            for (int y = r.Top; y < r.Bottom; y++)
            {
                int row = y * c.W;
                for (int i = 0; i < r.Width; i++) line[i] = c.P[row + r.Left + i];
                for (int i = 0; i < r.Width; i++)
                {
                    long a = 0, rr = 0, g = 0, b = 0; int n = 0;
                    int from = Math.Max(0, i - rad), to = Math.Min(r.Width - 1, i + rad);
                    for (int j = from; j <= to; j++)
                    {
                        int v = line[j];
                        a += Canvas32.A(v); rr += Canvas32.R(v); g += Canvas32.G(v); b += Canvas32.B(v); n++;
                    }
                    c.P[row + r.Left + i] = Canvas32.Pack((byte)(a / n), (byte)(rr / n), (byte)(g / n), (byte)(b / n));
                }
            }
        }

        static void BoxV(Canvas32 c, D.Rectangle r, int rad)
        {
            var col = new int[r.Height];
            for (int x = r.Left; x < r.Right; x++)
            {
                for (int i = 0; i < r.Height; i++) col[i] = c.P[(r.Top + i) * c.W + x];
                for (int i = 0; i < r.Height; i++)
                {
                    long a = 0, rr = 0, g = 0, b = 0; int n = 0;
                    int from = Math.Max(0, i - rad), to = Math.Min(r.Height - 1, i + rad);
                    for (int j = from; j <= to; j++)
                    {
                        int v = col[j];
                        a += Canvas32.A(v); rr += Canvas32.R(v); g += Canvas32.G(v); b += Canvas32.B(v); n++;
                    }
                    c.P[(r.Top + i) * c.W + x] = Canvas32.Pack((byte)(a / n), (byte)(rr / n), (byte)(g / n), (byte)(b / n));
                }
            }
        }

        // ---------- 지우개 3종 ----------

        // 마술봉: 찍은 자리의 색이 번져나가며 지워진다. 배경이 한 덩어리로 평평할 때 쓴다.
        // contiguous가 거짓이면 이어져 있지 않아도 같은 색이면 전부 지운다.
        public static void MagicErase(Canvas32 c, int sx, int sy, int tolerance, bool contiguous, int feather)
        {
            if (!c.In(sx, sy)) return;
            int seed = c.At(sx, sy);
            int maxD = ToleranceToDistance(tolerance);
            var mask = new byte[c.W * c.H];

            if (contiguous)
            {
                var stack = new int[c.W * c.H];
                int top = 0;
                stack[top++] = sy * c.W + sx;
                mask[sy * c.W + sx] = 255;
                while (top > 0)
                {
                    int idx = stack[--top];
                    int x = idx % c.W, y = idx / c.W;
                    // 4방향으로만 번진다. 8방향이면 대각선으로 새어나가 경계가 지저분해진다.
                    TryPush(c, mask, stack, ref top, x - 1, y, seed, maxD);
                    TryPush(c, mask, stack, ref top, x + 1, y, seed, maxD);
                    TryPush(c, mask, stack, ref top, x, y - 1, seed, maxD);
                    TryPush(c, mask, stack, ref top, x, y + 1, seed, maxD);
                }
            }
            else
            {
                for (int i = 0; i < c.P.Length; i++)
                    if (Dist(c.P[i], seed) <= maxD) mask[i] = 255;
            }

            if (feather > 0) BlurMask(mask, c.W, c.H, feather);
            ApplyMask(c, mask);
        }

        static void TryPush(Canvas32 c, byte[] mask, int[] stack, ref int top, int x, int y, int seed, int maxD)
        {
            if (x < 0 || y < 0 || x >= c.W || y >= c.H) return;
            int i = y * c.W + x;
            if (mask[i] != 0) return;
            if (Dist(c.P[i], seed) > maxD) return;
            mask[i] = 255;
            stack[top++] = i;
        }

        // 배경지우개: 브러시 한가운데에 걸린 색을 매번 다시 뽑아, 반경 안에서 그 색과
        // 비슷한 픽셀만 지운다. 경계선에 브러시를 물려 끌면 배경만 깎이고 피사체는 남는다.
        // 포토샵 배경 지우개와 같은 방식이다.
        public static void BackgroundErase(Canvas32 c, int cx, int cy, int radius, int tolerance, int hardnessPct)
        {
            if (!c.In(cx, cy)) return;
            int sample = c.At(cx, cy);
            if (Canvas32.A(sample) == 0) return;          // 이미 비어 있는 곳을 기준색으로 삼지 않는다
            EraseRadial(c, cx, cy, radius, hardnessPct, sample, ToleranceToDistance(tolerance));
        }

        // 일반 지우개: 색과 무관하게 반경 안을 지운다.
        public static void Erase(Canvas32 c, int cx, int cy, int radius, int hardnessPct)
        {
            EraseRadial(c, cx, cy, radius, hardnessPct, 0, -1);
        }

        // maxD가 음수면 색을 따지지 않는다.
        static void EraseRadial(Canvas32 c, int cx, int cy, int radius, int hardnessPct, int sample, int maxD)
        {
            radius = Math.Max(1, radius);
            int x0 = Math.Max(0, cx - radius), x1 = Math.Min(c.W - 1, cx + radius);
            int y0 = Math.Max(0, cy - radius), y1 = Math.Min(c.H - 1, cy + radius);
            double hard = Math.Max(0, Math.Min(100, hardnessPct)) / 100.0;
            double soft = 1.0 - hard;
            double r2 = (double)radius * radius;

            for (int y = y0; y <= y1; y++)
            {
                int row = y * c.W;
                int dy = y - cy;
                for (int x = x0; x <= x1; x++)
                {
                    int dx = x - cx;
                    double d2 = dx * dx + dy * dy;
                    if (d2 > r2) continue;

                    int i = row + x;
                    int cur = c.P[i];
                    byte a = Canvas32.A(cur);
                    if (a == 0) continue;

                    // 가장자리 감쇠
                    double t = Math.Sqrt(d2) / radius;
                    double strength = soft <= 0 ? 1.0 : Math.Min(1.0, (1.0 - t) / Math.Max(0.001, soft));
                    if (strength <= 0) continue;

                    // 색을 가리는 모드면 거리만큼 약하게 지운다 — 경계가 갑자기 끊기지 않는다
                    if (maxD >= 0)
                    {
                        int d = Dist(cur, sample);
                        if (d > maxD) continue;
                        if (maxD > 0) strength *= 1.0 - (double)d / maxD * 0.35;
                    }

                    int na = (int)Math.Round(a * (1.0 - strength));
                    if (na < 0) na = 0;
                    if (na < a) c.P[i] = Canvas32.Pack((byte)na, Canvas32.R(cur), Canvas32.G(cur), Canvas32.B(cur));
                }
            }
        }

        // 지울 곳 마스크를 부드럽게 만들어 계단을 없앤다.
        static void BlurMask(byte[] mask, int w, int h, int radius)
        {
            radius = Math.Max(1, Math.Min(8, radius));
            var tmp = new byte[mask.Length];
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    int sum = 0, n = 0;
                    int from = Math.Max(0, x - radius), to = Math.Min(w - 1, x + radius);
                    for (int j = from; j <= to; j++) { sum += mask[row + j]; n++; }
                    tmp[row + x] = (byte)(sum / n);
                }
            }
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    int sum = 0, n = 0;
                    int from = Math.Max(0, y - radius), to = Math.Min(h - 1, y + radius);
                    for (int j = from; j <= to; j++) { sum += tmp[j * w + x]; n++; }
                    mask[y * w + x] = (byte)(sum / n);
                }
            }
        }

        public static void ApplyMask(Canvas32 c, byte[] mask)
        {
            for (int i = 0; i < c.P.Length; i++)
            {
                byte m = mask[i];
                if (m == 0) continue;
                int v = c.P[i];
                byte a = Canvas32.A(v);
                if (a == 0) continue;
                int na = a * (255 - m) / 255;
                c.P[i] = Canvas32.Pack((byte)na, Canvas32.R(v), Canvas32.G(v), Canvas32.B(v));
            }
        }

        // 알파 마스크를 그대로 씌운다 (AI 누끼 결과 적용용). mask는 남길 정도 0~255.
        public static void SetAlpha(Canvas32 c, byte[] keep)
        {
            for (int i = 0; i < c.P.Length; i++)
            {
                int v = c.P[i];
                byte a = Canvas32.A(v);
                byte na = (byte)(a * keep[i] / 255);
                c.P[i] = Canvas32.Pack(na, Canvas32.R(v), Canvas32.G(v), Canvas32.B(v));
            }
        }

        // 반투명 가장자리에 남은 원래 배경색 기운을 걷어낸다.
        // 알파가 낮을수록 이웃한 불투명 픽셀의 색으로 당겨온다.
        public static void Defringe(Canvas32 c, int radius)
        {
            radius = Math.Max(1, Math.Min(4, radius));
            var src = (int[])c.P.Clone();
            for (int y = 0; y < c.H; y++)
            {
                for (int x = 0; x < c.W; x++)
                {
                    int i = y * c.W + x;
                    byte a = Canvas32.A(src[i]);
                    if (a == 0 || a == 255) continue;

                    long sr = 0, sg = 0, sb = 0; int n = 0;
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        int yy = y + dy; if (yy < 0 || yy >= c.H) continue;
                        for (int dx = -radius; dx <= radius; dx++)
                        {
                            int xx = x + dx; if (xx < 0 || xx >= c.W) continue;
                            int v = src[yy * c.W + xx];
                            if (Canvas32.A(v) < 250) continue;
                            sr += Canvas32.R(v); sg += Canvas32.G(v); sb += Canvas32.B(v); n++;
                        }
                    }
                    if (n == 0) continue;
                    c.P[i] = Canvas32.Pack(a, (byte)(sr / n), (byte)(sg / n), (byte)(sb / n));
                }
            }
        }

        // ---------- 마스크에 칠하기 ----------
        //
        // 아래 함수들은 픽셀을 건드리지 않는다. "얼마나 보일지"를 담은 마스크에만 칠한다.
        // 그래서 지운 자리를 나중에 도로 칠해 되살릴 수 있다.

        // 브러시 한 번. erase면 0쪽으로, 아니면 255쪽으로 민다.
        public static void MaskBrush(byte[] mask, int w, int h,
                                     int cx, int cy, int radius, int hardnessPct, bool erase)
        {
            PaintMask(mask, null, w, h, cx, cy, radius, hardnessPct, erase, 0, -1);
        }

        // 배경 지우개. 브러시 한가운데 색을 매번 다시 뽑아, 반경 안에서 그 색과 비슷한 곳만 민다.
        public static void MaskBackgroundErase(Canvas32 px, byte[] mask, int cx, int cy,
                                               int radius, int tolerance, int hardnessPct, bool erase)
        {
            if (!px.In(cx, cy)) return;
            int sample = px.At(cx, cy);
            PaintMask(mask, px, px.W, px.H, cx, cy, radius, hardnessPct, erase,
                      sample, ToleranceToDistance(tolerance));
        }

        // maxD가 음수면 색을 따지지 않는다.
        static void PaintMask(byte[] mask, Canvas32 px, int w, int h,
                              int cx, int cy, int radius, int hardnessPct, bool erase,
                              int sample, int maxD)
        {
            radius = Math.Max(1, radius);
            int x0 = Math.Max(0, cx - radius), x1 = Math.Min(w - 1, cx + radius);
            int y0 = Math.Max(0, cy - radius), y1 = Math.Min(h - 1, cy + radius);
            double hard = Math.Max(0, Math.Min(100, hardnessPct)) / 100.0;
            double soft = 1.0 - hard;
            double r2 = (double)radius * radius;

            for (int y = y0; y <= y1; y++)
            {
                int row = y * w;
                int dy = y - cy;
                for (int x = x0; x <= x1; x++)
                {
                    int dx = x - cx;
                    double d2 = dx * dx + dy * dy;
                    if (d2 > r2) continue;

                    int i = row + x;
                    int cur = mask[i];
                    if (erase ? cur == 0 : cur == 255) continue;

                    double t = Math.Sqrt(d2) / radius;
                    double strength = soft <= 0 ? 1.0 : Math.Min(1.0, (1.0 - t) / Math.Max(0.001, soft));
                    if (strength <= 0) continue;

                    if (maxD >= 0 && px != null)
                    {
                        int d = Dist(px.P[i], sample);
                        if (d > maxD) continue;
                        if (maxD > 0) strength *= 1.0 - (double)d / maxD * 0.35;
                    }

                    int target = erase ? 0 : 255;
                    int nv = (int)Math.Round(cur + (target - cur) * strength);
                    if (erase ? nv < cur : nv > cur) mask[i] = (byte)Math.Max(0, Math.Min(255, nv));
                }
            }
        }

        // 마술봉. 찍은 자리의 색이 번져나가는 범위를 마스크에서 지운다.
        public static void MaskMagicErase(Canvas32 px, byte[] mask,
                                          int sx, int sy, int tolerance, bool contiguous, int feather)
        {
            if (!px.In(sx, sy)) return;
            int seed = px.At(sx, sy);
            int maxD = ToleranceToDistance(tolerance);
            var hit = new byte[px.W * px.H];

            if (contiguous)
            {
                var stack = new int[px.W * px.H];
                int top = 0;
                stack[top++] = sy * px.W + sx;
                hit[sy * px.W + sx] = 255;
                while (top > 0)
                {
                    int idx = stack[--top];
                    int x = idx % px.W, y = idx / px.W;
                    TryPush(px, hit, stack, ref top, x - 1, y, seed, maxD);
                    TryPush(px, hit, stack, ref top, x + 1, y, seed, maxD);
                    TryPush(px, hit, stack, ref top, x, y - 1, seed, maxD);
                    TryPush(px, hit, stack, ref top, x, y + 1, seed, maxD);
                }
            }
            else
            {
                for (int i = 0; i < px.P.Length; i++)
                    if (Dist(px.P[i], seed) <= maxD) hit[i] = 255;
            }

            if (feather > 0) BlurMask(hit, px.W, px.H, feather);
            for (int i = 0; i < mask.Length; i++)
            {
                if (hit[i] == 0) continue;
                int nv = mask[i] * (255 - hit[i]) / 255;
                if (nv < mask[i]) mask[i] = (byte)nv;
            }
        }

        // AI 누끼 결과를 마스크에 곱한다. keep은 픽셀마다 남길 정도 0~255.
        public static void MaskApplyKeep(byte[] mask, byte[] keep)
        {
            for (int i = 0; i < mask.Length && i < keep.Length; i++)
                mask[i] = (byte)(mask[i] * keep[i] / 255);
        }

        public static void MaskFill(byte[] mask, byte value)
        {
            for (int i = 0; i < mask.Length; i++) mask[i] = value;
        }

        // 반투명 가장자리에 남은 배경색 기운을 걷어낸다. 무엇이 가장자리인지는 마스크가 안다.
        public static void Defringe(Canvas32 c, byte[] mask, int radius)
        {
            radius = Math.Max(1, Math.Min(4, radius));
            var src = (int[])c.P.Clone();
            for (int y = 0; y < c.H; y++)
            {
                for (int x = 0; x < c.W; x++)
                {
                    int i = y * c.W + x;
                    int a = mask != null ? mask[i] : Canvas32.A(src[i]);
                    if (a == 0 || a >= 250) continue;

                    long sr = 0, sg = 0, sb = 0; int n = 0;
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        int yy = y + dy; if (yy < 0 || yy >= c.H) continue;
                        for (int dx = -radius; dx <= radius; dx++)
                        {
                            int xx = x + dx; if (xx < 0 || xx >= c.W) continue;
                            int j = yy * c.W + xx;
                            int ja = mask != null ? mask[j] : Canvas32.A(src[j]);
                            if (ja < 250) continue;
                            sr += Canvas32.R(src[j]); sg += Canvas32.G(src[j]); sb += Canvas32.B(src[j]); n++;
                        }
                    }
                    if (n == 0) continue;
                    c.P[i] = Canvas32.Pack(Canvas32.A(c.P[i]), (byte)(sr / n), (byte)(sg / n), (byte)(sb / n));
                }
            }
        }

        // 마스크에서 아직 보이는 부분을 감싸는 사각형.
        public static D.Rectangle VisibleBounds(byte[] mask, int w, int h, int threshold)
        {
            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    if (mask[row + x] <= threshold) continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
            if (maxX < 0) return new D.Rectangle(0, 0, w, h);
            return D.Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
        }

        // ---------- 색 조절 ----------
        //
        // 한 번 훑으면서 전부 적용한다. 여러 번 나눠 돌리면 그때마다 0~255로 잘려서
        // 계단이 생긴다.
        //
        // cover가 있으면 그 값만큼만 섞는다. 선택 영역의 부드러운 가장자리가 그대로 살아서,
        // 조절한 곳과 안 한 곳의 경계가 티 나지 않는다.
        public class Adjust
        {
            public double Brightness;    // -100 ~ 100
            public double Contrast;      // -100 ~ 100
            public double Saturation;    // -100 ~ 100
            public double Temperature;   // -100(차갑게) ~ 100(따뜻하게)
            public double Shadows;       // -100 ~ 100  어두운 쪽만
            public double Highlights;    // -100 ~ 100  밝은 쪽만
            public double Gamma = 1.0;   // 0.2 ~ 3.0

            public bool IsIdentity
            {
                get
                {
                    return Math.Abs(Brightness) < 0.01 && Math.Abs(Contrast) < 0.01
                        && Math.Abs(Saturation) < 0.01 && Math.Abs(Temperature) < 0.01
                        && Math.Abs(Shadows) < 0.01 && Math.Abs(Highlights) < 0.01
                        && Math.Abs(Gamma - 1.0) < 0.001;
                }
            }

            public Adjust Copy() { return (Adjust)MemberwiseClone(); }
        }

        public static void ApplyAdjust(Canvas32 dst, Canvas32 src, byte[] cover, Adjust a)
        {
            if (a == null) return;

            // 밝기·대비·감마는 값만 보면 되니 미리 256칸 표로 만들어 둔다
            var tone = new byte[256];
            double cf = (259.0 * (a.Contrast * 2.55 + 255.0)) / (255.0 * (259.0 - a.Contrast * 2.55));
            double add = a.Brightness * 2.55;
            double invG = 1.0 / Math.Max(0.05, a.Gamma);
            for (int i = 0; i < 256; i++)
            {
                double v = i + add;
                v = cf * (v - 128.0) + 128.0;
                v = 255.0 * Math.Pow(Math.Max(0, Math.Min(255, v)) / 255.0, invG);
                tone[i] = (byte)Math.Max(0, Math.Min(255, Math.Round(v)));
            }

            // 밝기에 따라 얼마나 건드릴지도 표로. 어두운 쪽과 밝은 쪽을 따로 민다.
            var shW = new double[256];
            var hiW = new double[256];
            for (int i = 0; i < 256; i++)
            {
                double t = i / 255.0;
                double s = 1.0 - t;
                shW[i] = s * s * s;
                hiW[i] = t * t * t;
            }

            double sat = 1.0 + a.Saturation / 100.0;
            double temp = a.Temperature / 100.0 * 34.0;
            double shAmt = a.Shadows * 1.6;
            double hiAmt = a.Highlights * 1.6;

            for (int i = 0; i < src.P.Length; i++)
            {
                int sv = src.P[i];
                byte al = Canvas32.A(sv);
                if (al == 0) { dst.P[i] = sv; continue; }

                int cv = cover != null ? cover[i] : 255;
                if (cv == 0) { dst.P[i] = sv; continue; }

                int r0 = Canvas32.R(sv), g0 = Canvas32.G(sv), b0 = Canvas32.B(sv);

                double r = tone[r0], g = tone[g0], b = tone[b0];

                int lum = (int)((r * 77 + g * 151 + b * 28) / 256.0);
                if (lum < 0) lum = 0; else if (lum > 255) lum = 255;
                double d = shAmt * shW[lum] + hiAmt * hiW[lum];
                r += d; g += d; b += d;

                if (Math.Abs(sat - 1.0) > 0.001)
                {
                    double l2 = (r * 77 + g * 151 + b * 28) / 256.0;
                    r = l2 + (r - l2) * sat;
                    g = l2 + (g - l2) * sat;
                    b = l2 + (b - l2) * sat;
                }

                if (Math.Abs(temp) > 0.01) { r += temp; b -= temp; }

                int ri = (int)Math.Round(r), gi = (int)Math.Round(g), bi = (int)Math.Round(b);
                if (ri < 0) ri = 0; else if (ri > 255) ri = 255;
                if (gi < 0) gi = 0; else if (gi > 255) gi = 255;
                if (bi < 0) bi = 0; else if (bi > 255) bi = 255;

                if (cv < 255)
                {
                    ri = r0 + (ri - r0) * cv / 255;
                    gi = g0 + (gi - g0) * cv / 255;
                    bi = b0 + (bi - b0) * cv / 255;
                }
                dst.P[i] = Canvas32.Pack(al, (byte)ri, (byte)gi, (byte)bi);
            }
        }

        // 자동 보정. 가장 어두운 쪽과 밝은 쪽을 끝까지 당겨 편다.
        // 양 끝 0.5%는 버린다 — 점 하나 때문에 전체가 어긋나는 걸 막는다.
        public static Adjust AutoLevels(Canvas32 src, byte[] cover)
        {
            var hist = new int[256];
            int n = 0;
            for (int i = 0; i < src.P.Length; i++)
            {
                if (Canvas32.A(src.P[i]) == 0) continue;
                if (cover != null && cover[i] < 128) continue;
                int v = src.P[i];
                int lum = (Canvas32.R(v) * 77 + Canvas32.G(v) * 151 + Canvas32.B(v) * 28) >> 8;
                hist[lum < 0 ? 0 : (lum > 255 ? 255 : lum)]++;
                n++;
            }
            var a = new Adjust();
            if (n < 100) return a;

            int cut = Math.Max(1, n / 200);
            int lo = 0, hi = 255, acc = 0;
            for (int i = 0; i < 256; i++) { acc += hist[i]; if (acc > cut) { lo = i; break; } }
            acc = 0;
            for (int i = 255; i >= 0; i--) { acc += hist[i]; if (acc > cut) { hi = i; break; } }
            if (hi - lo < 8) return a;

            // lo..hi 를 0..255 로 펴는 것을 밝기와 대비로 바꿔 표현한다
            double scale = 255.0 / (hi - lo);
            double contrast = (scale - 1.0) / (scale + 1.0) * 255.0;
            a.Contrast = Math.Max(-95, Math.Min(95, contrast / 2.55 * 100.0 / 100.0 * 60));
            double mid = (lo + hi) / 2.0;
            a.Brightness = Math.Max(-80, Math.Min(80, (128.0 - mid) / 2.55));
            return a;
        }

        // ---------- 자유 변형 리샘플링 ----------
        //
        // 목적지 픽셀마다 거꾸로 짚어가며 원본에서 값을 뽑는다(역매핑).
        // 원본에서 목적지로 밀면 늘렸을 때 구멍이 숭숭 뚫린다.
        //
        // 색은 알파를 곱한 상태로 섞는다. 그냥 섞으면 투명한 곳의 색까지 끌려와
        // 가장자리에 거무튀튀한 테가 생긴다.
        //
        // inv는 목적지 좌표를 원본 좌표로 되돌리는 행렬이다(WPF Matrix 규약).
        public static void Resample(Canvas32 src, byte[] srcMask, int dw, int dh,
                                    double m11, double m12, double m21, double m22,
                                    double offX, double offY,
                                    out Canvas32 dst, out byte[] dstMask)
        {
            dst = new Canvas32(Math.Max(1, dw), Math.Max(1, dh));
            dstMask = srcMask != null ? new byte[dst.W * dst.H] : null;

            for (int y = 0; y < dst.H; y++)
            {
                double fy = y + 0.5;
                int drow = y * dst.W;
                for (int x = 0; x < dst.W; x++)
                {
                    double fx = x + 0.5;
                    double sxf = fx * m11 + fy * m21 + offX - 0.5;
                    double syf = fx * m12 + fy * m22 + offY - 0.5;

                    int x0 = (int)Math.Floor(sxf), y0 = (int)Math.Floor(syf);
                    if (x0 < -1 || y0 < -1 || x0 > src.W - 1 || y0 > src.H - 1) continue;

                    double tx = sxf - x0, ty = syf - y0;
                    int x1 = x0 + 1, y1 = y0 + 1;

                    double wa = 0, wr = 0, wg = 0, wb = 0, wm = 0;
                    Accum(src, srcMask, x0, y0, (1 - tx) * (1 - ty), ref wa, ref wr, ref wg, ref wb, ref wm);
                    Accum(src, srcMask, x1, y0, tx * (1 - ty), ref wa, ref wr, ref wg, ref wb, ref wm);
                    Accum(src, srcMask, x0, y1, (1 - tx) * ty, ref wa, ref wr, ref wg, ref wb, ref wm);
                    Accum(src, srcMask, x1, y1, tx * ty, ref wa, ref wr, ref wg, ref wb, ref wm);

                    if (wa <= 0.001) continue;
                    byte a = (byte)Math.Min(255, Math.Round(wa));
                    dst.P[drow + x] = Canvas32.Pack(a,
                        (byte)Math.Min(255, Math.Round(wr / wa)),
                        (byte)Math.Min(255, Math.Round(wg / wa)),
                        (byte)Math.Min(255, Math.Round(wb / wa)));
                    if (dstMask != null) dstMask[drow + x] = (byte)Math.Min(255, Math.Round(wm));
                }
            }
        }

        static void Accum(Canvas32 src, byte[] mask, int x, int y, double w,
                          ref double wa, ref double wr, ref double wg, ref double wb, ref double wm)
        {
            if (w <= 0) return;
            if (x < 0) x = 0; else if (x >= src.W) x = src.W - 1;
            if (y < 0) y = 0; else if (y >= src.H) y = src.H - 1;
            int i = y * src.W + x;
            int v = src.P[i];
            double a = Canvas32.A(v);
            wa += a * w;
            wr += Canvas32.R(v) * a * w;
            wg += Canvas32.G(v) * a * w;
            wb += Canvas32.B(v) * a * w;
            if (mask != null) wm += mask[i] * w;
        }

        // ---------- 형태 ----------

        public static D.Bitmap Rotate(D.Bitmap src, int degrees)
        {
            var b = Cap.Clone32(src);
            switch (((degrees % 360) + 360) % 360)
            {
                case 90: b.RotateFlip(D.RotateFlipType.Rotate90FlipNone); break;
                case 180: b.RotateFlip(D.RotateFlipType.Rotate180FlipNone); break;
                case 270: b.RotateFlip(D.RotateFlipType.Rotate270FlipNone); break;
            }
            return b;
        }

        public static D.Bitmap Flip(D.Bitmap src, bool horizontal)
        {
            var b = Cap.Clone32(src);
            b.RotateFlip(horizontal ? D.RotateFlipType.RotateNoneFlipX : D.RotateFlipType.RotateNoneFlipY);
            return b;
        }

        public static D.Bitmap Pad(D.Bitmap src, int pad, D.Color fill)
        {
            var b = new D.Bitmap(src.Width + pad * 2, src.Height + pad * 2, D.Imaging.PixelFormat.Format32bppArgb);
            using (var g = D.Graphics.FromImage(b))
            {
                if (fill.A > 0) g.Clear(fill);
                g.DrawImage(src, pad, pad, src.Width, src.Height);
            }
            return b;
        }

        public static D.Bitmap Outline(D.Bitmap src, int width, D.Color color)
        {
            var b = Cap.Clone32(src);
            using (var g = D.Graphics.FromImage(b))
            using (var pen = new D.Pen(color, width))
            {
                pen.Alignment = D.Drawing2D.PenAlignment.Inset;
                g.DrawRectangle(pen, width / 2f, width / 2f,
                                b.Width - width, b.Height - width);
            }
            return b;
        }

        // 바깥으로 번지는 그림자. 캔버스를 키우고 실루엣을 흐려 깔아준다.
        public static D.Bitmap Shadow(D.Bitmap src, int blur, int offsetY, double opacity)
        {
            int pad = blur * 2 + Math.Abs(offsetY) + 6;
            int w = src.Width + pad * 2, h = src.Height + pad * 2;

            var shadowLayer = new Canvas32(w, h);
            var s = Canvas32.From(src);
            // 실루엣만 검게 옮겨 심는다
            for (int y = 0; y < s.H; y++)
                for (int x = 0; x < s.W; x++)
                {
                    byte a = Canvas32.A(s.At(x, y));
                    if (a == 0) continue;
                    int ty = y + pad + offsetY, tx = x + pad;
                    if (tx < 0 || ty < 0 || tx >= w || ty >= h) continue;
                    shadowLayer.P[ty * w + tx] = Canvas32.Pack((byte)(a * opacity), 0, 0, 0);
                }
            Blur(shadowLayer, new D.Rectangle(0, 0, w, h), Math.Max(1, blur / 2));

            var outBmp = shadowLayer.ToBitmap();
            using (var g = D.Graphics.FromImage(outBmp))
            {
                g.CompositingMode = D.Drawing2D.CompositingMode.SourceOver;
                g.DrawImage(src, pad, pad, src.Width, src.Height);
            }
            return outBmp;
        }

        // 여러 캡처를 한 장으로. 짧은 쪽은 가운데 정렬하고 빈 곳은 배경색으로 채운다.
        public static D.Bitmap Join(D.Bitmap[] imgs, bool vertical, int gap, D.Color fill)
        {
            int w = 0, h = 0;
            foreach (var i in imgs)
            {
                if (vertical) { w = Math.Max(w, i.Width); h += i.Height; }
                else { h = Math.Max(h, i.Height); w += i.Width; }
            }
            if (vertical) h += gap * (imgs.Length - 1); else w += gap * (imgs.Length - 1);

            var b = new D.Bitmap(Math.Max(1, w), Math.Max(1, h), D.Imaging.PixelFormat.Format32bppArgb);
            using (var g = D.Graphics.FromImage(b))
            {
                if (fill.A > 0) g.Clear(fill);
                int pos = 0;
                foreach (var i in imgs)
                {
                    if (vertical) { g.DrawImage(i, (w - i.Width) / 2, pos, i.Width, i.Height); pos += i.Height + gap; }
                    else { g.DrawImage(i, pos, (h - i.Height) / 2, i.Width, i.Height); pos += i.Width + gap; }
                }
            }
            return b;
        }

        // 알파가 있는 가장자리를 잘라낸다 (누끼 후 여백 정리).
        public static D.Rectangle OpaqueBounds(Canvas32 c, int threshold)
        {
            int minX = c.W, minY = c.H, maxX = -1, maxY = -1;
            for (int y = 0; y < c.H; y++)
            {
                int row = y * c.W;
                for (int x = 0; x < c.W; x++)
                {
                    if (Canvas32.A(c.P[row + x]) <= threshold) continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
            if (maxX < 0) return new D.Rectangle(0, 0, c.W, c.H);
            return D.Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
        }
    }
}
