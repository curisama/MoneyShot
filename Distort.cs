// Money Shot — 원근 왜곡
//
// 레이어의 네 모서리를 따로 끌어 사다리꼴 등 아무 볼록 사각형으로 편다. 간판·화면을 정면으로 펴거나
// 반대로 비스듬히 붙일 때 쓴다. 사각형 → 사각형 대응(호모그래피)이라 직선은 직선으로 남는다.
//
// Compositor(MIT, Wonder Assembly LLC)의 Distort.swift(DistortWarp.homography)를 따랐다.
// 원본은 Core Image(CIPerspectiveTransform)로 그리는데, 여기서는 역대응으로 원본 자리를 찾아 이중선형으로 읽는다.
using System;
using System.Windows;

namespace MoneyShot
{
    public static class Distort
    {
        // 단위 정사각형 (0,0)(1,0)(1,1)(0,1) → q[0..3](왼위·오위·오아래·왼아래)로 보내는 3×3 행렬(행 우선)
        public static double[] Homography(Point[] q)
        {
            double x0 = q[0].X, y0 = q[0].Y, x1 = q[1].X, y1 = q[1].Y, x2 = q[2].X, y2 = q[2].Y, x3 = q[3].X, y3 = q[3].Y;
            double dx1 = x1 - x2, dx2 = x3 - x2, dx3 = x0 - x1 + x2 - x3;
            double dy1 = y1 - y2, dy2 = y3 - y2, dy3 = y0 - y1 + y2 - y3;
            double g, h;
            if (Math.Abs(dx3) < 1e-12 && Math.Abs(dy3) < 1e-12) { g = 0; h = 0; }   // 평행사변형
            else
            {
                double det = dx1 * dy2 - dx2 * dy1;
                if (Math.Abs(det) < 1e-12) det = 1e-12;
                g = (dx3 * dy2 - dx2 * dy3) / det;
                h = (dx1 * dy3 - dx3 * dy1) / det;
            }
            return new[]
            {
                x1 - x0 + g * x1, x3 - x0 + h * x3, x0,
                y1 - y0 + g * y1, y3 - y0 + h * y3, y0,
                g, h, 1
            };
        }

        public static double[] Invert(double[] m)
        {
            double a = m[0], b = m[1], c = m[2], d = m[3], e = m[4], f = m[5], g = m[6], h = m[7], i = m[8];
            double A = e * i - f * h, B = -(d * i - f * g), C = d * h - e * g;
            double det = a * A + b * B + c * C;
            if (Math.Abs(det) < 1e-18) det = 1e-18;
            return new[]
            {
                A / det, -(b * i - c * h) / det, (b * f - c * e) / det,
                B / det, (a * i - c * g) / det, -(a * f - c * d) / det,
                C / det, -(a * h - b * g) / det, (a * e - b * d) / det
            };
        }

        public static double BoundsArea(Point[] q)
        {
            double minX = Math.Min(Math.Min(q[0].X, q[1].X), Math.Min(q[2].X, q[3].X));
            double minY = Math.Min(Math.Min(q[0].Y, q[1].Y), Math.Min(q[2].Y, q[3].Y));
            double maxX = Math.Max(Math.Max(q[0].X, q[1].X), Math.Max(q[2].X, q[3].X));
            double maxY = Math.Max(Math.Max(q[0].Y, q[1].Y), Math.Max(q[2].Y, q[3].Y));
            return (maxX - minX + 1) * (maxY - minY + 1);
        }

        // 네 꼭짓점이 볼록하고 같은 방향으로 돌아야 펼 수 있다(꼬이거나 오목하면 뒤집힌다)
        public static bool IsConvex(Point[] q)
        {
            int sign = 0;
            for (int i = 0; i < 4; i++)
            {
                var a = q[i]; var b = q[(i + 1) % 4]; var c = q[(i + 2) % 4];
                double cr = (b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X);
                if (Math.Abs(cr) < 1e-9) return false;
                int s = cr > 0 ? 1 : -1;
                if (sign == 0) sign = s; else if (s != sign) return false;
            }
            return true;
        }

        // src(마스크 포함)를 문서 좌표 사각형 q로 편다. scale < 1이면 그만큼 줄여 그린다(미리보기).
        // 결과의 (0,0)은 문서 좌표 (x0, y0)이고, 결과 한 칸은 문서 1/scale 칸이다.
        public static void Warp(Canvas32 src, byte[] mask, Point[] q, double scale,
                                out Canvas32 dst, out byte[] dmask, out int x0, out int y0)
        {
            double minX = Math.Min(Math.Min(q[0].X, q[1].X), Math.Min(q[2].X, q[3].X));
            double minY = Math.Min(Math.Min(q[0].Y, q[1].Y), Math.Min(q[2].Y, q[3].Y));
            double maxX = Math.Max(Math.Max(q[0].X, q[1].X), Math.Max(q[2].X, q[3].X));
            double maxY = Math.Max(Math.Max(q[0].Y, q[1].Y), Math.Max(q[2].Y, q[3].Y));
            x0 = (int)Math.Floor(minX); y0 = (int)Math.Floor(minY);
            int w = Math.Max(1, (int)Math.Ceiling((Math.Ceiling(maxX) - x0) * scale));
            int h = Math.Max(1, (int)Math.Ceiling((Math.Ceiling(maxY) - y0) * scale));
            dst = new Canvas32(w, h);
            dmask = mask != null ? new byte[w * h] : null;
            var inv = Invert(Homography(q));
            int W = src.W, H = src.H;
            var S = src.P;
            for (int j = 0; j < h; j++)
            {
                double dy = y0 + (j + 0.5) / scale;
                for (int i = 0; i < w; i++)
                {
                    double dx = x0 + (i + 0.5) / scale;
                    double zz = inv[6] * dx + inv[7] * dy + inv[8];
                    if (Math.Abs(zz) < 1e-12) continue;
                    double u = (inv[0] * dx + inv[1] * dy + inv[2]) / zz;
                    double v = (inv[3] * dx + inv[4] * dy + inv[5]) / zz;
                    if (u < 0 || v < 0 || u > 1 || v > 1) continue;
                    double sx = u * W - 0.5, sy = v * H - 0.5;
                    int ix = (int)Math.Floor(sx), iy = (int)Math.Floor(sy);
                    double fx = sx - ix, fy = sy - iy;
                    double sr = 0, sg = 0, sb = 0, sa = 0, sm = 0, wsum = 0;
                    for (int b = 0; b < 2; b++)
                    {
                        int yy = Math.Min(H - 1, Math.Max(0, iy + b));
                        double wy = b == 1 ? fy : 1 - fy;
                        for (int a = 0; a < 2; a++)
                        {
                            int xx = Math.Min(W - 1, Math.Max(0, ix + a));
                            double wt = wy * (a == 1 ? fx : 1 - fx);
                            if (wt <= 0) continue;
                            int k = yy * W + xx, p = S[k];
                            double al = Canvas32.A(p) * wt;
                            sr += Canvas32.R(p) * al; sg += Canvas32.G(p) * al; sb += Canvas32.B(p) * al; sa += al;
                            if (mask != null) sm += mask[k] * wt;
                            wsum += wt;
                        }
                    }
                    int o = j * w + i;
                    if (dmask != null) dmask[o] = (byte)Math.Round(sm / Math.Max(1e-9, wsum));
                    if (sa <= 0.01) continue;
                    dst.P[o] = Canvas32.Pack((byte)Math.Min(255, Math.Round(sa)), (byte)Math.Round(sr / sa),
                                             (byte)Math.Round(sg / sa), (byte)Math.Round(sb / sa));
                }
            }
        }
    }
}
