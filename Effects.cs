// Money Shot — 레이어 효과와 블렌드 모드
//
// 효과는 레이어에 붙어 다닌다. 픽셀을 굽지 않으니 언제든 바꾸거나 뗄 수 있고,
// 레이어를 고치면 효과도 따라온다. 그리는 순서는 포토샵과 같다:
//   그림자 · 바깥 광선 → (레이어) → 안쪽 그림자 · 안쪽 광선 · 색 덮기 → 테두리
// 블렌드 모드는 레이어(효과 포함)를 밑에 깔린 것과 어떻게 섞을지다.
//
// 효과 정의(기본값·각도 규칙)와 블렌드 모드 목록·묶음은 Compositor(MIT, Wonder Assembly LLC)를 따랐다.
// 원본은 GPU(Metal·Core Image)로 그리는데 여기서는 같은 결과를 CPU로 직접 계산한다.
using System;

namespace MoneyShot
{
    public enum BlendMode
    {
        Normal,
        Darken, Multiply, ColorBurn, LinearBurn,
        Lighten, Screen, ColorDodge, LinearDodge,
        Overlay, SoftLight, HardLight, VividLight, LinearLight, PinLight, HardMix,
        Difference, Exclusion, Subtract, Divide,
        Hue, Saturation, Color, Luminosity
    }

    public static class Blend
    {
        // 메뉴에서 줄을 그어 나눌 묶음: 보통 / 어둡게 / 밝게 / 대비 / 비교 / 성분
        public static readonly BlendMode[][] Groups =
        {
            new[] { BlendMode.Normal },
            new[] { BlendMode.Darken, BlendMode.Multiply, BlendMode.ColorBurn, BlendMode.LinearBurn },
            new[] { BlendMode.Lighten, BlendMode.Screen, BlendMode.ColorDodge, BlendMode.LinearDodge },
            new[] { BlendMode.Overlay, BlendMode.SoftLight, BlendMode.HardLight, BlendMode.VividLight, BlendMode.LinearLight, BlendMode.PinLight, BlendMode.HardMix },
            new[] { BlendMode.Difference, BlendMode.Exclusion, BlendMode.Subtract, BlendMode.Divide },
            new[] { BlendMode.Hue, BlendMode.Saturation, BlendMode.Color, BlendMode.Luminosity },
        };

        public static string Name(BlendMode m)
        {
            switch (m)
            {
                case BlendMode.Normal: return L.T("보통", "Normal");
                case BlendMode.Darken: return L.T("어둡게", "Darken");
                case BlendMode.Multiply: return L.T("곱하기", "Multiply");
                case BlendMode.ColorBurn: return L.T("색상 번", "Color Burn");
                case BlendMode.LinearBurn: return L.T("선형 번", "Linear Burn");
                case BlendMode.Lighten: return L.T("밝게", "Lighten");
                case BlendMode.Screen: return L.T("스크린", "Screen");
                case BlendMode.ColorDodge: return L.T("색상 닷지", "Color Dodge");
                case BlendMode.LinearDodge: return L.T("선형 닷지 (더하기)", "Linear Dodge (Add)");
                case BlendMode.Overlay: return L.T("오버레이", "Overlay");
                case BlendMode.SoftLight: return L.T("소프트 라이트", "Soft Light");
                case BlendMode.HardLight: return L.T("하드 라이트", "Hard Light");
                case BlendMode.VividLight: return L.T("선명한 라이트", "Vivid Light");
                case BlendMode.LinearLight: return L.T("선형 라이트", "Linear Light");
                case BlendMode.PinLight: return L.T("핀 라이트", "Pin Light");
                case BlendMode.HardMix: return L.T("하드 혼합", "Hard Mix");
                case BlendMode.Difference: return L.T("차이", "Difference");
                case BlendMode.Exclusion: return L.T("제외", "Exclusion");
                case BlendMode.Subtract: return L.T("빼기", "Subtract");
                case BlendMode.Divide: return L.T("나누기", "Divide");
                case BlendMode.Hue: return L.T("색조", "Hue");
                case BlendMode.Saturation: return L.T("채도", "Saturation");
                case BlendMode.Color: return L.T("색상", "Color");
                default: return L.T("광도", "Luminosity");
            }
        }

        static double C(double v) { return v < 0 ? 0 : v > 1 ? 1 : v; }

        // 채널 하나씩 섞는 모드. b = 밑(배경), s = 위(레이어), 0~1. 포토샵 공식.
        static double Sep(BlendMode m, double b, double s)
        {
            switch (m)
            {
                case BlendMode.Darken: return Math.Min(b, s);
                case BlendMode.Multiply: return b * s;
                case BlendMode.ColorBurn:
                    if (b >= 1) return 1;
                    if (s <= 0) return 0;
                    return 1 - Math.Min(1, (1 - b) / s);
                case BlendMode.LinearBurn: return C(b + s - 1);
                case BlendMode.Lighten: return Math.Max(b, s);
                case BlendMode.Screen: return b + s - b * s;
                case BlendMode.ColorDodge:
                    if (b <= 0) return 0;
                    if (s >= 1) return 1;
                    return Math.Min(1, b / (1 - s));
                case BlendMode.LinearDodge: return C(b + s);
                case BlendMode.Overlay: return Sep(BlendMode.HardLight, s, b);
                case BlendMode.SoftLight:
                    if (s <= 0.5) return b - (1 - 2 * s) * b * (1 - b);
                    {
                        double d = b <= 0.25 ? ((16 * b - 12) * b + 4) * b : Math.Sqrt(b);
                        return b + (2 * s - 1) * (d - b);
                    }
                case BlendMode.HardLight:
                    return s <= 0.5 ? b * 2 * s : Sep(BlendMode.Screen, b, 2 * s - 1);
                case BlendMode.VividLight:
                    return s <= 0.5 ? Sep(BlendMode.ColorBurn, b, 2 * s) : Sep(BlendMode.ColorDodge, b, 2 * s - 1);
                case BlendMode.LinearLight: return C(b + 2 * s - 1);
                case BlendMode.PinLight:
                    return s <= 0.5 ? Math.Min(b, 2 * s) : Math.Max(b, 2 * s - 1);
                case BlendMode.HardMix: return b + s >= 1 ? 1 : 0;
                case BlendMode.Difference: return Math.Abs(b - s);
                case BlendMode.Exclusion: return b + s - 2 * b * s;
                case BlendMode.Subtract: return C(b - s);
                case BlendMode.Divide: return s <= 0 ? (b > 0 ? 1 : 0) : C(b / s);
                default: return s;
            }
        }

        // 색조·채도·색상·광도는 세 채널을 같이 본다 (W3C 합성 명세의 SetLum/SetSat).
        static double Lum(double r, double g, double b) { return 0.3 * r + 0.59 * g + 0.11 * b; }

        static void ClipColor(ref double r, ref double g, ref double b)
        {
            double l = Lum(r, g, b), n = Math.Min(r, Math.Min(g, b)), x = Math.Max(r, Math.Max(g, b));
            if (n < 0) { r = l + (r - l) * l / (l - n); g = l + (g - l) * l / (l - n); b = l + (b - l) * l / (l - n); }
            if (x > 1) { r = l + (r - l) * (1 - l) / (x - l); g = l + (g - l) * (1 - l) / (x - l); b = l + (b - l) * (1 - l) / (x - l); }
        }

        static void SetLum(ref double r, ref double g, ref double b, double l)
        {
            double d = l - Lum(r, g, b);
            r += d; g += d; b += d;
            ClipColor(ref r, ref g, ref b);
        }

        static double Sat(double r, double g, double b) { return Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)); }

        static void SetSat(ref double r, ref double g, ref double b, double s)
        {
            var v = new[] { r, g, b };
            int mx = 0, mn = 0;
            for (int i = 1; i < 3; i++) { if (v[i] > v[mx]) mx = i; if (v[i] < v[mn]) mn = i; }
            if (mx == mn) { r = g = b = 0; return; }
            int md = 3 - mx - mn;
            double span = v[mx] - v[mn];
            var o = new double[3];
            o[md] = (v[md] - v[mn]) * s / span;
            o[mx] = s;
            o[mn] = 0;
            r = o[0]; g = o[1]; b = o[2];
        }

        // 결과 색(0~1). bR.. = 밑, sR.. = 위.
        public static void Mix(BlendMode m, double bR, double bG, double bB, double sR, double sG, double sB,
                               out double r, out double g, out double b)
        {
            switch (m)
            {
                case BlendMode.Hue:
                    r = sR; g = sG; b = sB;
                    SetSat(ref r, ref g, ref b, Sat(bR, bG, bB));
                    SetLum(ref r, ref g, ref b, Lum(bR, bG, bB));
                    return;
                case BlendMode.Saturation:
                    r = bR; g = bG; b = bB;
                    SetSat(ref r, ref g, ref b, Sat(sR, sG, sB));
                    SetLum(ref r, ref g, ref b, Lum(bR, bG, bB));
                    return;
                case BlendMode.Color:
                    r = sR; g = sG; b = sB;
                    SetLum(ref r, ref g, ref b, Lum(bR, bG, bB));
                    return;
                case BlendMode.Luminosity:
                    r = bR; g = bG; b = bB;
                    SetLum(ref r, ref g, ref b, Lum(sR, sG, sB));
                    return;
                default:
                    r = C(Sep(m, bR, sR)); g = C(Sep(m, bG, sG)); b = C(Sep(m, bB, sB));
                    return;
            }
        }
    }

    // ================= 레이어 효과 =================

    public class FxColor
    {
        public byte R, G, B;
        public FxColor(byte r, byte g, byte b) { R = r; G = g; B = b; }
        public FxColor Clone() { return new FxColor(R, G, B); }
    }

    public class LayerFx
    {
        // 켜짐 여부와 값. 값은 꺼도 남아서 다시 켜면 그대로다.
        public bool Shadow, OuterGlow, InnerShadow, InnerGlow, Overlay, Stroke;

        // 그림자: 빛이 오는 방향(오른쪽에서 반시계, 90 = 바로 위 → 그림자는 아래로)
        public double ShAngle = 90, ShDistance = 20, ShBlur = 20, ShOpacity = 0.5;
        public FxColor ShColor = new FxColor(0, 0, 0);

        public double OgSize = 20, OgOpacity = 0.75;
        public FxColor OgColor = new FxColor(255, 255, 255);

        public double IsAngle = 90, IsDistance = 10, IsBlur = 10, IsOpacity = 0.5;
        public FxColor IsColor = new FxColor(0, 0, 0);

        public double IgSize = 10, IgOpacity = 0.75;
        public FxColor IgColor = new FxColor(255, 255, 255);

        public double OvOpacity = 1;
        public FxColor OvColor = new FxColor(0, 0, 0);

        public double StSize = 4, StOpacity = 1;
        public bool StInside;
        public FxColor StColor = new FxColor(0, 0, 0);

        public bool Any { get { return Shadow || OuterGlow || InnerShadow || InnerGlow || Overlay || Stroke; } }

        public LayerFx Clone()
        {
            var c = (LayerFx)MemberwiseClone();
            c.ShColor = ShColor.Clone(); c.OgColor = OgColor.Clone(); c.IsColor = IsColor.Clone();
            c.IgColor = IgColor.Clone(); c.OvColor = OvColor.Clone(); c.StColor = StColor.Clone();
            return c;
        }

        // 효과가 레이어 밖으로 얼마나 번지나
        public int Margin
        {
            get
            {
                double m = 0;
                if (Shadow) m = Math.Max(m, ShDistance + ShBlur * 1.5);
                if (OuterGlow) m = Math.Max(m, OgSize * 1.5);
                if (Stroke && !StInside) m = Math.Max(m, StSize);
                return (int)Math.Ceiling(m) + 2;
            }
        }
    }

    public static class Fx
    {
        // 레이어(마스크 반영)에 효과를 입힌 그림과, 그 그림이 레이어 왼쪽 위에서 얼마나 바깥으로 나가 있는지.
        public static Canvas32 Render(Canvas32 px, byte[] mask, LayerFx fx, out int margin)
        {
            margin = fx.Margin;
            return RenderWindow(px, mask, fx, margin, 0, 0, px.W + margin * 2, px.H + margin * 2);
        }

        // 레이어의 한 자리(레이어 좌표)가 바뀌었을 때 효과 그림에서 그 영향이 닿는 곳만 다시 그린다.
        // 효과는 모두 가까운 픽셀만 보는 계산이라(흐림·밀기·테두리 거리), 바뀐 자리를 닿는 거리만큼 넓힌 곳이
        // 달라지는 전부이고, 그곳을 바르게 그리려면 거기서 또 닿는 거리만큼 넓혀 읽으면 된다.
        public static void Update(Canvas32 cache, Canvas32 px, byte[] mask, LayerFx fx, int m, System.Drawing.Rectangle changed)
        {
            int R = Reach(fx);
            var full = new System.Drawing.Rectangle(0, 0, cache.W, cache.H);
            var target = new System.Drawing.Rectangle(changed.X + m - R, changed.Y + m - R, changed.Width + R * 2, changed.Height + R * 2);
            target.Intersect(full);
            if (target.Width <= 0 || target.Height <= 0) return;
            var win = new System.Drawing.Rectangle(target.X - R, target.Y - R, target.Width + R * 2, target.Height + R * 2);
            win.Intersect(full);
            var part = RenderWindow(px, mask, fx, m, win.X, win.Y, win.Width, win.Height);
            for (int y = target.Top; y < target.Bottom; y++)
                Array.Copy(part.P, (y - win.Y) * win.Width + (target.X - win.X), cache.P, y * cache.W + target.X, target.Width);
        }

        static int BoxReach(double sigma)
        {
            if (sigma < 0.3) return 0;
            return 3 * Math.Max(1, (int)Math.Round((Math.Sqrt(4 * sigma * sigma + 1) - 1) / 2));
        }

        // 한 픽셀의 변화가 효과 그림에서 닿을 수 있는 가장 먼 거리
        public static int Reach(LayerFx fx)
        {
            int r = 1;
            if (fx.Shadow) r = Math.Max(r, (int)Math.Ceiling(fx.ShDistance) + 1 + BoxReach(fx.ShBlur / 2));
            if (fx.OuterGlow) r = Math.Max(r, BoxReach(fx.OgSize / 2));
            if (fx.InnerShadow) r = Math.Max(r, (int)Math.Ceiling(fx.IsDistance) + 1 + BoxReach(fx.IsBlur / 2));
            if (fx.InnerGlow) r = Math.Max(r, BoxReach(fx.IgSize / 2));
            if (fx.Stroke) r = Math.Max(r, (int)Math.Ceiling(fx.StSize) + 2);
            return r + 1;
        }

        // 효과 그림(레이어 + 사방 m)에서 (x0,y0)부터 W×H 만큼만 그린다.
        static Canvas32 RenderWindow(Canvas32 px, byte[] mask, LayerFx fx, int m, int x0, int y0, int W, int H)
        {
            int n = W * H;

            // 모양(알파 0~1)과 색
            var A = new float[n];
            var col = new int[n];
            for (int y = 0; y < H; y++)
            {
                int ly = y0 + y - m;
                if (ly < 0 || ly >= px.H) continue;
                for (int x = 0; x < W; x++)
                {
                    int lx = x0 + x - m;
                    if (lx < 0 || lx >= px.W) continue;
                    int si = ly * px.W + lx, di = y * W + x;
                    int p = px.P[si];
                    float a = Canvas32.A(p) / 255f;
                    if (mask != null) a *= mask[si] / 255f;
                    A[di] = a;
                    col[di] = p;
                }
            }

            // 아래에서 위로 쌓는다. acc는 곱하지 않은 ARGB float.
            var oR = new float[n]; var oG = new float[n]; var oB = new float[n]; var oA = new float[n];

            if (fx.Shadow && fx.ShOpacity > 0)
            {
                double rad = fx.ShAngle * Math.PI / 180;
                int dx = (int)Math.Round(-Math.Cos(rad) * fx.ShDistance), dy = (int)Math.Round(Math.Sin(rad) * fx.ShDistance);
                var s = Blur(Shift(A, W, H, dx, dy), W, H, fx.ShBlur / 2);
                Over(oR, oG, oB, oA, s, fx.ShColor, fx.ShOpacity);
            }
            if (fx.OuterGlow && fx.OgOpacity > 0)
            {
                // 흐린 모양을 두 배로 끌어올려 가장자리 바깥으로 size 만큼 퍼지게 한다
                var g = Blur(A, W, H, fx.OgSize / 2);
                for (int i = 0; i < n; i++) g[i] = Math.Min(1f, g[i] * 2f);
                Over(oR, oG, oB, oA, g, fx.OgColor, fx.OgOpacity);
            }

            // 레이어 자신
            {
                var lr = new float[n]; var lg = new float[n]; var lb = new float[n];
                for (int i = 0; i < n; i++)
                {
                    int p = col[i];
                    lr[i] = ((p >> 16) & 0xFF) / 255f; lg[i] = ((p >> 8) & 0xFF) / 255f; lb[i] = (p & 0xFF) / 255f;
                }
                // 안쪽 효과는 레이어 색 위에 얹는다 (모양 안에서만)
                if (fx.InnerShadow && fx.IsOpacity > 0)
                {
                    double rad = fx.IsAngle * Math.PI / 180;
                    int dx = (int)Math.Round(-Math.Cos(rad) * fx.IsDistance), dy = (int)Math.Round(Math.Sin(rad) * fx.IsDistance);
                    var inv = new float[n];
                    for (int i = 0; i < n; i++) inv[i] = 1 - A[i];
                    var s = Blur(Shift(inv, W, H, dx, dy, 1f), W, H, fx.IsBlur / 2);
                    Tint(lr, lg, lb, s, fx.IsColor, fx.IsOpacity);
                }
                if (fx.InnerGlow && fx.IgOpacity > 0)
                {
                    var inv = new float[n];
                    for (int i = 0; i < n; i++) inv[i] = 1 - A[i];
                    var g = Blur(inv, W, H, fx.IgSize / 2, 1f);
                    for (int i = 0; i < n; i++) g[i] = Math.Min(1f, g[i] * 2f);
                    Tint(lr, lg, lb, g, fx.IgColor, fx.IgOpacity);
                }
                if (fx.Overlay && fx.OvOpacity > 0)
                {
                    var one = new float[n];
                    for (int i = 0; i < n; i++) one[i] = 1;
                    Tint(lr, lg, lb, one, fx.OvColor, fx.OvOpacity);
                }
                OverColored(oR, oG, oB, oA, A, lr, lg, lb);
            }

            if (fx.Stroke && fx.StOpacity > 0 && fx.StSize > 0)
            {
                var cov = new float[n];
                if (fx.StInside)
                {
                    // 모양 안쪽, 바깥에서 size 안의 띠
                    var d = Distance(A, W, H, false);
                    for (int i = 0; i < n; i++) cov[i] = A[i] * Clamp01((float)(fx.StSize - d[i] + 0.5));
                }
                else
                {
                    // 모양 바깥 size 까지 (안쪽은 레이어가 덮으니 모양 밖만 칠해도 되지만, 반투명 가장자리를 위해 안도 덮는다)
                    var d = Distance(A, W, H, true);
                    for (int i = 0; i < n; i++) cov[i] = Math.Max(Clamp01((float)(fx.StSize - d[i] + 0.5)) * (1 - A[i]), 0);
                }
                Over(oR, oG, oB, oA, cov, fx.StColor, fx.StOpacity);
            }

            var outc = new Canvas32(W, H);
            for (int i = 0; i < n; i++)
            {
                float a = oA[i];
                if (a <= 0.0005f) continue;
                outc.P[i] = Canvas32.Pack(Byte(a), Byte(oR[i]), Byte(oG[i]), Byte(oB[i]));
            }
            return outc;
        }

        static float Clamp01(float v) { return v < 0 ? 0 : v > 1 ? 1 : v; }
        static byte Byte(float v) { return (byte)Math.Max(0, Math.Min(255, (int)(v * 255 + 0.5f))); }

        // 단색을 cov 만큼 올린다 (일반 합성, 곱하지 않은 색)
        static void Over(float[] r, float[] g, float[] b, float[] a, float[] cov, FxColor c, double opacity)
        {
            float cr = c.R / 255f, cg = c.G / 255f, cb = c.B / 255f, op = (float)opacity;
            for (int i = 0; i < a.Length; i++)
            {
                float sa = cov[i] * op;
                if (sa <= 0) continue;
                float da = a[i];
                float oa = sa + da * (1 - sa);
                r[i] = (cr * sa + r[i] * da * (1 - sa)) / oa;
                g[i] = (cg * sa + g[i] * da * (1 - sa)) / oa;
                b[i] = (cb * sa + b[i] * da * (1 - sa)) / oa;
                a[i] = oa;
            }
        }

        static void OverColored(float[] r, float[] g, float[] b, float[] a, float[] sa_, float[] sr, float[] sg, float[] sb)
        {
            for (int i = 0; i < a.Length; i++)
            {
                float sa = sa_[i];
                if (sa <= 0) continue;
                float da = a[i];
                float oa = sa + da * (1 - sa);
                r[i] = (sr[i] * sa + r[i] * da * (1 - sa)) / oa;
                g[i] = (sg[i] * sa + g[i] * da * (1 - sa)) / oa;
                b[i] = (sb[i] * sa + b[i] * da * (1 - sa)) / oa;
                a[i] = oa;
            }
        }

        // 색을 amt 만큼 덮는다 (알파는 그대로)
        static void Tint(float[] r, float[] g, float[] b, float[] amt, FxColor c, double opacity)
        {
            float cr = c.R / 255f, cg = c.G / 255f, cb = c.B / 255f, op = (float)opacity;
            for (int i = 0; i < r.Length; i++)
            {
                float t = amt[i] * op;
                if (t <= 0) continue;
                r[i] += (cr - r[i]) * t; g[i] += (cg - g[i]) * t; b[i] += (cb - b[i]) * t;
            }
        }

        // 밀어낸 자리는 fill 로 채운다
        static float[] Shift(float[] s, int W, int H, int dx, int dy, float fill = 0f)
        {
            var o = new float[s.Length];
            for (int y = 0; y < H; y++)
            {
                int sy = y - dy;
                for (int x = 0; x < W; x++)
                {
                    int sx = x - dx;
                    o[y * W + x] = sx < 0 || sy < 0 || sx >= W || sy >= H ? fill : s[sy * W + sx];
                }
            }
            return o;
        }

        // 가우시안 근사(상자 흐림 세 번). edge: 판 밖을 이 값으로 본다.
        static float[] Blur(float[] src, int W, int H, double sigma, float edge = 0f)
        {
            if (sigma < 0.3) return (float[])src.Clone();
            int r = Math.Max(1, (int)Math.Round((Math.Sqrt(4 * sigma * sigma + 1) - 1) / 2));
            var a = (float[])src.Clone();
            var tmp = new float[a.Length];
            for (int k = 0; k < 3; k++) { BoxH(a, tmp, W, H, r, edge); BoxV(tmp, a, W, H, r, edge); }
            return a;
        }

        static void BoxH(float[] s, float[] d, int W, int H, int r, float edge)
        {
            float span = 2 * r + 1;
            for (int y = 0; y < H; y++)
            {
                int row = y * W;
                float sum = 0;
                for (int x = -r; x <= r; x++) sum += x < 0 || x >= W ? edge : s[row + x];
                for (int x = 0; x < W; x++)
                {
                    d[row + x] = sum / span;
                    int xo = x - r, xi = x + r + 1;
                    sum -= xo < 0 ? edge : s[row + xo];
                    sum += xi >= W ? edge : s[row + xi];
                }
            }
        }

        static void BoxV(float[] s, float[] d, int W, int H, int r, float edge)
        {
            float span = 2 * r + 1;
            for (int x = 0; x < W; x++)
            {
                float sum = 0;
                for (int y = -r; y <= r; y++) sum += y < 0 || y >= H ? edge : s[y * W + x];
                for (int y = 0; y < H; y++)
                {
                    d[y * W + x] = sum / span;
                    int yo = y - r, yi = y + r + 1;
                    sum -= yo < 0 ? edge : s[yo * W + x];
                    sum += yi >= H ? edge : s[yi * W + x];
                }
            }
        }

        // 유클리드 거리 (Felzenszwalb). toInside=true면 모양(알파≥0.5)까지의 거리, false면 모양 밖까지의 거리.
        static double[] Distance(float[] A, int W, int H, bool toInside)
        {
            const double INF = 1e20;
            var f = new double[W * H];
            for (int i = 0; i < f.Length; i++)
            {
                bool inside = A[i] >= 0.5f;
                f[i] = (toInside ? inside : !inside) ? 0 : INF;
            }
            // 판 밖은 모양 밖으로 친다 — 안쪽 테두리가 판 가장자리에서도 생긴다
            int len = Math.Max(W, H);
            var z = new double[len + 1]; var v = new int[len]; var d = new double[len]; var col = new double[len];
            for (int x = 0; x < W; x++)
            {
                for (int y = 0; y < H; y++) col[y] = f[y * W + x];
                Dt1(col, H, d, v, z);
                for (int y = 0; y < H; y++) f[y * W + x] = d[y];
            }
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++) col[x] = f[y * W + x];
                Dt1(col, W, d, v, z);
                for (int x = 0; x < W; x++) f[y * W + x] = Math.Sqrt(d[x]);
            }
            return f;
        }

        static void Dt1(double[] f, int n, double[] d, int[] v, double[] z)
        {
            int k = 0;
            v[0] = 0; z[0] = double.NegativeInfinity; z[1] = double.PositiveInfinity;
            for (int q = 1; q < n; q++)
            {
                double s;
                while (true)
                {
                    s = ((f[q] + (double)q * q) - (f[v[k]] + (double)v[k] * v[k])) / (2.0 * q - 2.0 * v[k]);
                    if (s <= z[k] && k > 0) { k--; continue; }
                    break;
                }
                k++; v[k] = q; z[k] = s; z[k + 1] = double.PositiveInfinity;
            }
            k = 0;
            for (int q = 0; q < n; q++)
            {
                while (z[k + 1] < q) k++;
                double dq = q - v[k];
                d[q] = dq * dq + f[v[k]];
            }
        }
    }
}
