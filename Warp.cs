// Money Shot — 유동화 (밀기 · 문지르기)
//
// 밀기: 브러시 아래 픽셀이 브러시를 따라 움직인다. 가운데가 가장 많이, 가장자리로 갈수록 덜.
// 문지르기: 브러시가 방금 남긴 색을 들고 다음 자리에 내려놓는다. 손가락으로 물감을 끄는 것처럼.
// (흐리게 칠하기는 도장과 같은 길이라 편집창 쪽에 있다.)
//
// Compositor(MIT, Wonder Assembly LLC)의 SmudgeLiquify.swift(WarpStroke)를 옮겼다.
// 원본은 알파를 곱한 RGBA라 섞어도 투명한 곳 색이 묻어나지 않는다. 여기 Canvas32는 곱하지 않은 ARGB라
// 섞기 전에 곱하고 섞은 뒤에 나눈다.
using System;
using System.Windows;
using D = System.Drawing;

namespace MoneyShot
{
    public class WarpStroke
    {
        readonly Canvas32 img;          // 레이어 픽셀(레이어 좌표). 직접 고친다
        readonly bool smudge;
        readonly double diameter, hardness, strength;
        Point? last;
        float[] carried = new float[0];
        float[] scratch = new float[0];

        public WarpStroke(Canvas32 pixels, bool smudge, double diameter, double hardness01, double strength01)
        {
            img = pixels;
            this.smudge = smudge;
            this.diameter = Math.Max(2, diameter);
            hardness = Math.Min(0.98, Math.Max(0, hardness01));
            strength = Math.Min(1, Math.Max(0.01, strength01));
        }

        int Radius { get { return (int)Math.Ceiling(diameter / 2); } }

        // 중심에서 u(0 가운데 ~ 1 가장자리)만큼 떨어진 곳을 얼마나 움직이나
        float Weight(float u)
        {
            if (u >= 1) return 0;
            float h = (float)hardness;
            if (u <= h) return 1;
            float t = (1 - u) / (1 - h);
            return t * t * (3 - 2 * t);
        }

        // 알파를 곱한 float 넷으로 읽고 쓴다
        void Get(int i, float[] o, int k)
        {
            int v = img.P[i];
            float a = Canvas32.A(v);
            o[k + 3] = a;
            o[k] = Canvas32.R(v) * a / 255f; o[k + 1] = Canvas32.G(v) * a / 255f; o[k + 2] = Canvas32.B(v) * a / 255f;
        }

        void Put(int i, float r, float g, float b, float a)
        {
            if (a <= 0.5f) { img.P[i] = 0; return; }
            float k = 255f / a;
            img.P[i] = Canvas32.Pack(Byte(a), Byte(r * k), Byte(g * k), Byte(b * k));
        }

        static byte Byte(float v) { return (byte)Math.Max(0, Math.Min(255, (int)Math.Round(v))); }

        // 획을 point까지 이어 간다. 고친 곳(레이어 좌표)을 돌려준다. 아무것도 안 했으면 Empty.
        public D.Rectangle Append(Point point)
        {
            if (last == null)
            {
                last = point;
                if (smudge) PickUp(point);
                return D.Rectangle.Empty;
            }
            var from = last.Value;
            double dist = (point - from).Length;
            // 문지르기는 한 픽셀 간격으로 촘촘히 — 띄엄띄엄 찍으면 끈 자국에 메아리가 남는다
            double spacing = Math.Max(1, diameter * (smudge ? 0.005 : 0.025));
            if (dist < spacing) return D.Rectangle.Empty;
            int steps = (int)Math.Ceiling(dist / spacing);
            var prev = from;
            for (int s = 1; s <= steps; s++)
            {
                double t = (double)s / steps;
                var next = new Point(from.X + (point.X - from.X) * t, from.Y + (point.Y - from.Y) * t);
                if (smudge) Smudge(next); else Push(prev, next);
                prev = next;
            }
            last = point;
            int r = Radius + 4 + (int)Math.Ceiling(dist);
            return new D.Rectangle((int)Math.Min(from.X, point.X) - r, (int)Math.Min(from.Y, point.Y) - r,
                                   (int)Math.Abs(point.X - from.X) + r * 2, (int)Math.Abs(point.Y - from.Y) + r * 2);
        }

        void PickUp(Point c)
        {
            int r = Radius, side = 2 * r + 1;
            carried = new float[side * side * 4];
            int cx = (int)Math.Round(c.X), cy = (int)Math.Round(c.Y);
            for (int dy = -r; dy <= r; dy++)
            {
                int y = cy + dy;
                if (y < 0 || y >= img.H) continue;
                for (int dx = -r; dx <= r; dx++)
                {
                    int x = cx + dx;
                    if (x < 0 || x >= img.W) continue;
                    Get(y * img.W + x, carried, ((dy + r) * side + dx + r) * 4);
                }
            }
        }

        void Smudge(Point c)
        {
            int r = Radius, side = 2 * r + 1;
            int cx = (int)Math.Round(c.X), cy = (int)Math.Round(c.Y);
            float keep = (float)strength, invR = (float)(1 / (diameter / 2));
            var under = new float[4];
            for (int dy = -r; dy <= r; dy++)
            {
                int y = cy + dy;
                if (y < 0 || y >= img.H) continue;
                for (int dx = -r; dx <= r; dx++)
                {
                    int x = cx + dx;
                    if (x < 0 || x >= img.W) continue;
                    float w = Weight((float)Math.Sqrt(dx * dx + dy * dy) * invR);
                    if (w <= 0) continue;
                    int i = y * img.W + x, c4 = ((dy + r) * side + dx + r) * 4;
                    Get(i, under, 0);
                    // 지난 자리에서 들고 온 색을 세기만큼 내려놓는다. 그리고 방금 남긴 것만 들고 간다 —
                    // 처음 집은 것을 계속 들고 다니면 찍을 때마다 같은 것이 유령처럼 반복된다.
                    for (int k = 0; k < 4; k++)
                    {
                        float painted = under[k] + (carried[c4 + k] - under[k]) * w * keep;
                        carried[c4 + k] = painted;
                        under[k] = painted;
                    }
                    Put(i, under[0], under[1], under[2], under[3]);
                }
            }
        }

        // 앞으로 밀기: 이번 찍기 전 상태를 잠깐 떠 두고, 브러시가 지나온 뒤쪽에서 이중선형으로 읽어 온다.
        void Push(Point a, Point b)
        {
            int r = Radius;
            float mx = (float)((b.X - a.X) * strength), my = (float)((b.Y - a.Y) * strength);
            int margin = (int)Math.Ceiling(Math.Max(Math.Abs(mx), Math.Abs(my))) + 2;
            int cx = (int)Math.Round(b.X), cy = (int)Math.Round(b.Y);
            int x0 = Math.Max(0, cx - r - margin), x1 = Math.Min(img.W - 1, cx + r + margin);
            int y0 = Math.Max(0, cy - r - margin), y1 = Math.Min(img.H - 1, cy + r + margin);
            if (x0 > x1 || y0 > y1) return;
            int cw = x1 - x0 + 1, ch = y1 - y0 + 1;
            if (scratch.Length < cw * ch * 4) scratch = new float[cw * ch * 4];
            for (int y = 0; y < ch; y++)
                for (int x = 0; x < cw; x++)
                    Get((y + y0) * img.W + x + x0, scratch, (y * cw + x) * 4);
            if (cw < 2 || ch < 2) return;

            float invR = (float)(1 / (diameter / 2));
            for (int dy = -r; dy <= r; dy++)
            {
                int y = cy + dy;
                if (y < y0 || y > y1) continue;
                for (int dx = -r; dx <= r; dx++)
                {
                    int x = cx + dx;
                    if (x < x0 || x > x1) continue;
                    float w = Weight((float)Math.Sqrt(dx * dx + dy * dy) * invR);
                    if (w <= 0) continue;
                    float sx = Math.Min(cw - 1, Math.Max(0, x - x0 - mx * w));
                    float sy = Math.Min(ch - 1, Math.Max(0, y - y0 - my * w));
                    int ix = Math.Min(cw - 2, (int)sx), iy = Math.Min(ch - 2, (int)sy);
                    float fx = sx - ix, fy = sy - iy;
                    int s00 = (iy * cw + ix) * 4, s10 = s00 + 4, s01 = s00 + cw * 4, s11 = s01 + 4;
                    float r0 = Lerp2(s00, s10, s01, s11, 0, fx, fy), g0 = Lerp2(s00, s10, s01, s11, 1, fx, fy);
                    float b0 = Lerp2(s00, s10, s01, s11, 2, fx, fy), a0 = Lerp2(s00, s10, s01, s11, 3, fx, fy);
                    Put(y * img.W + x, r0, g0, b0, a0);
                }
            }
        }

        float Lerp2(int s00, int s10, int s01, int s11, int k, float fx, float fy)
        {
            float top = scratch[s00 + k] + (scratch[s10 + k] - scratch[s00 + k]) * fx;
            float bot = scratch[s01 + k] + (scratch[s11 + k] - scratch[s01 + k]) * fx;
            return top + (bot - top) * fy;
        }
    }
}
