// Money Shot — 선택 영역
//
// 선택은 자르기 전용이 아니다. 한 번 잡아두면 자르기·지우기·새 레이어로 복사·반전이
// 전부 같은 영역을 쓴다. 그래서 모양(사각형·원형·올가미·자동 올가미)과
// 그 영역으로 무엇을 할지를 갈라 놓았다.
//
// 영역은 두 가지 모습으로 들고 있다.
//   Geom — 화면에 테두리와 딤을 그리는 데 쓴다 (벡터라 확대해도 깨지지 않는다)
//   Mask — 픽셀 연산에 쓴다 (경계가 부드럽게 깎여 있어 톱니가 안 생긴다)
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using D = System.Drawing;

namespace MoneyShot
{
    public enum SelShape { Rect, Ellipse, Lasso, Magnetic, Object, Color }

    public class Selection
    {
        public Geometry Geom;
        public byte[] Mask;              // 문서 크기. 255 = 선택됨
        public int W, H;
        public D.Rectangle Bounds;

        public bool IsEmpty { get { return Mask == null || Bounds.Width <= 0 || Bounds.Height <= 0; } }

        // 도형을 실제 픽셀 마스크로 굽는다.
        // WPF에게 그리게 하면 경계가 부드럽게 깎여 나와서, 잘라낸 자리에 톱니가 생기지 않는다.
        public static Selection FromGeometry(Geometry g, int w, int h)
        {
            if (g == null || w <= 0 || h <= 0) return null;

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
                dc.DrawGeometry(Brushes.White, null, g);

            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);

            var px = new int[w * h];
            rtb.CopyPixels(px, w * 4, 0);

            var s = new Selection { W = w, H = h, Geom = g.CloneCurrentValue() };
            s.Mask = new byte[w * h];
            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    byte a = (byte)((uint)px[row + x] >> 24);
                    s.Mask[row + x] = a;
                    if (a <= 3) continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
            if (maxX < 0)
            {
                // 그래픽 장치가 없는 화면(모니터 없는 서버 등)에서는 WPF가 빈 그림을 내놓는 일이 있다.
                // 모양이 분명 있는데 비었으면 GDI+로 다시 굽는다.
                var gb = g.Bounds;
                if (gb.IsEmpty || gb.Width < 0.5 || gb.Height < 0.5 || !RasterizeGdi(g, s.Mask, w, h)) return null;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        if (s.Mask[y * w + x] <= 3) continue;
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                if (maxX < 0) return null;
            }
            s.Bounds = D.Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
            return s;
        }

        // WPF 도형을 꺾은선으로 펴 GDI+로 칠한다(가장자리 부드럽게, 겹쳐 감긴 곳은 Nonzero 규칙)
        static bool RasterizeGdi(Geometry g, byte[] mask, int w, int h)
        {
            try
            {
                var flat = g.GetFlattenedPathGeometry(0.25, ToleranceType.Absolute);
                using (var path = new System.Drawing.Drawing2D.GraphicsPath(
                    flat.FillRule == FillRule.Nonzero ? System.Drawing.Drawing2D.FillMode.Winding : System.Drawing.Drawing2D.FillMode.Alternate))
                {
                    foreach (var fig in flat.Figures)
                    {
                        var pts = new List<D.PointF> { new D.PointF((float)fig.StartPoint.X, (float)fig.StartPoint.Y) };
                        foreach (var seg in fig.Segments)
                        {
                            var pl = seg as PolyLineSegment;
                            var ln = seg as LineSegment;
                            if (pl != null) foreach (var q in pl.Points) pts.Add(new D.PointF((float)q.X, (float)q.Y));
                            else if (ln != null) pts.Add(new D.PointF((float)ln.Point.X, (float)ln.Point.Y));
                        }
                        if (pts.Count >= 3) path.AddPolygon(pts.ToArray());
                    }
                    using (var bmp = new D.Bitmap(w, h, D.Imaging.PixelFormat.Format32bppArgb))
                    {
                        using (var gr = D.Graphics.FromImage(bmp))
                        {
                            gr.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                            gr.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                            gr.FillPath(D.Brushes.White, path);
                        }
                        var data = bmp.LockBits(new D.Rectangle(0, 0, w, h), D.Imaging.ImageLockMode.ReadOnly, D.Imaging.PixelFormat.Format32bppArgb);
                        try
                        {
                            var row = new int[w];
                            for (int y = 0; y < h; y++)
                            {
                                System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, w);
                                for (int x = 0; x < w; x++) mask[y * w + x] = (byte)((uint)row[x] >> 24);
                            }
                        }
                        finally { bmp.UnlockBits(data); }
                    }
                }
                return true;
            }
            catch { return false; }
        }

        public Selection Invert()
        {
            var full = new RectangleGeometry(new Rect(0, 0, W, H));
            var inv = new CombinedGeometry(GeometryCombineMode.Exclude, full, Geom);
            return FromGeometry(inv, W, H);
        }
    }

    // 자동 올가미가 쓰는 윤곽 세기 지도.
    //
    // 사람이 대충 그린 선을, 근처에서 가장 또렷한 경계로 끌어당긴다.
    // 제대로 된 방식(라이브 와이어)은 매 점마다 최단경로를 풀지만, 화면 캡처에서는
    // 경계가 또렷해서 "수직 방향으로 가장 센 곳을 찾아 붙이는" 것만으로 충분히 달라붙는다.
    public class EdgeMap
    {
        readonly byte[] g;               // 윤곽 세기 0~255 (절반 해상도)
        readonly int w, h;
        const int Div = 2;

        public EdgeMap(Canvas32 src)
        {
            w = Math.Max(1, src.W / Div);
            h = Math.Max(1, src.H / Div);
            var lum = new byte[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int v = src.At(Math.Min(src.W - 1, x * Div), Math.Min(src.H - 1, y * Div));
                    lum[y * w + x] = (byte)((Canvas32.R(v) * 77 + Canvas32.G(v) * 151 + Canvas32.B(v) * 28) >> 8);
                }

            g = new byte[w * h];
            for (int y = 1; y < h - 1; y++)
            {
                int row = y * w;
                for (int x = 1; x < w - 1; x++)
                {
                    int i = row + x;
                    // 소벨
                    int gx = -lum[i - w - 1] - 2 * lum[i - 1] - lum[i + w - 1]
                             + lum[i - w + 1] + 2 * lum[i + 1] + lum[i + w + 1];
                    int gy = -lum[i - w - 1] - 2 * lum[i - w] - lum[i - w + 1]
                             + lum[i + w - 1] + 2 * lum[i + w] + lum[i + w + 1];
                    int m = (Math.Abs(gx) + Math.Abs(gy)) / 4;
                    g[i] = (byte)(m > 255 ? 255 : m);
                }
            }
        }

        int At(int x, int y)
        {
            x /= Div; y /= Div;
            if (x < 0 || y < 0 || x >= w || y >= h) return 0;
            return g[y * w + x];
        }

        // from에서 to로 가는 선분에 수직인 방향으로 훑어, 가장 센 윤곽에 to를 붙인다.
        public Point Snap(Point from, Point to, int radius)
        {
            double dx = to.X - from.X, dy = to.Y - from.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.5) return to;
            // 진행 방향의 수직
            double nx = -dy / len, ny = dx / len;

            int bestScore = -1;
            Point best = to;
            for (int d = -radius; d <= radius; d++)
            {
                int x = (int)Math.Round(to.X + nx * d);
                int y = (int)Math.Round(to.Y + ny * d);
                int score = At(x, y);
                // 멀리 끌려가는 것보다 가까운 쪽을 조금 우대한다 — 안 그러면 선이 튄다
                score -= Math.Abs(d) * 2;
                if (score > bestScore) { bestScore = score; best = new Point(x, y); }
            }
            // 주변에 이렇다 할 경계가 없으면 사람이 그린 자리를 그대로 둔다
            return bestScore < 24 ? to : best;
        }
    }

    public static class SelOps
    {
        // 선택 영역 안(또는 바깥)을 레이어 마스크에서 지운다.
        public static void EraseThrough(byte[] layerMask, int lw, int lh, int lx, int ly,
                                        Selection sel, bool inside)
        {
            for (int y = 0; y < lh; y++)
            {
                int docY = y + ly;
                if (docY < 0 || docY >= sel.H) { if (!inside) ClearRow(layerMask, y, lw); continue; }
                int lrow = y * lw, srow = docY * sel.W;
                for (int x = 0; x < lw; x++)
                {
                    int docX = x + lx;
                    byte cover = (docX < 0 || docX >= sel.W) ? (byte)0 : sel.Mask[srow + docX];
                    int amount = inside ? cover : 255 - cover;
                    if (amount == 0) continue;
                    int cur = layerMask[lrow + x];
                    int nv = cur * (255 - amount) / 255;
                    if (nv < cur) layerMask[lrow + x] = (byte)nv;
                }
            }
        }

        static void ClearRow(byte[] mask, int y, int w)
        {
            int row = y * w;
            for (int x = 0; x < w; x++) mask[row + x] = 0;
        }

        // 선택 영역만 떼어내 새 레이어용 픽셀로 만든다.
        public static Canvas32 Extract(Layer src, Selection sel, out int outX, out int outY)
        {
            var b = sel.Bounds;
            outX = b.X; outY = b.Y;
            var outC = new Canvas32(b.Width, b.Height);

            for (int y = 0; y < b.Height; y++)
            {
                int docY = b.Y + y;
                int sy = docY - src.Y;
                if (sy < 0 || sy >= src.H) continue;
                for (int x = 0; x < b.Width; x++)
                {
                    int docX = b.X + x;
                    int sx = docX - src.X;
                    if (sx < 0 || sx >= src.W) continue;

                    int v = src.PixelsRead.P[sy * src.W + sx];
                    int a = Canvas32.A(v);
                    if (src.MaskRead != null) a = a * src.MaskRead[sy * src.W + sx] / 255;
                    a = a * sel.Mask[docY * sel.W + docX] / 255;
                    if (a == 0) continue;
                    outC.P[y * b.Width + x] = Canvas32.Pack((byte)a, Canvas32.R(v), Canvas32.G(v), Canvas32.B(v));
                }
            }
            return outC;
        }

        // 여러 점을 지나는 닫힌 곡선. 올가미 자국이 각지지 않게 조금 매끄럽게 만든다.
        public static Geometry PolyGeometry(List<Point> pts, bool close)
        {
            if (pts == null || pts.Count < 2) return null;
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(pts[0], true, close);
                for (int i = 1; i < pts.Count; i++) ctx.LineTo(pts[i], true, true);
            }
            g.Freeze();
            return g;
        }
    }

    // 색상 범위 선택: 고른 색들 가까이에 있는 픽셀을 그림 전체에서 고른다.
    // Compositor(MIT, Wonder Assembly LLC)의 ColorRangeSelection·WandPixels.c(color_range_mask)를 옮겼다.
    // 원본은 딱 자르는데, 경계가 계단지지 않게 허용 범위 바로 바깥 몇 단계만 부드럽게 줄인다.
    public static class ColorRange
    {
        // 한 점 둘레 3×3 평균색 (투명한 곳은 null)
        public static int[] Sample(Canvas32 c, int x, int y)
        {
            long r = 0, g = 0, b = 0, a = 0;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int px = x + dx, py = y + dy;
                    if (!c.In(px, py)) continue;
                    int v = c.At(px, py);
                    int al = Canvas32.A(v);
                    r += Canvas32.R(v) * al; g += Canvas32.G(v) * al; b += Canvas32.B(v) * al; a += al;
                }
            if (a == 0) return null;
            return new[] { (int)(r / a), (int)(g / a), (int)(b / a) };
        }

        // 채널마다 차이 중 가장 큰 것이 허용 범위 안이면 그 색과 가깝다
        static int Dist(int v, List<int[]> colors)
        {
            int best = 999;
            int r = Canvas32.R(v), g = Canvas32.G(v), b = Canvas32.B(v);
            foreach (var c in colors)
            {
                int d = Math.Max(Math.Abs(r - c[0]), Math.Max(Math.Abs(g - c[1]), Math.Abs(b - c[2])));
                if (d < best) best = d;
            }
            return best;
        }

        public static byte[] Mask(Canvas32 img, List<int[]> include, List<int[]> exclude, int fuzz, bool invert)
        {
            var m = new byte[img.P.Length];
            const int soft = 6;
            for (int i = 0; i < m.Length; i++)
            {
                int v = img.P[i];
                int k = 0;
                if (Canvas32.A(v) > 0 && include.Count > 0)
                {
                    int d = Dist(v, include);
                    k = d <= fuzz ? 255 : d <= fuzz + soft ? 255 * (fuzz + soft - d) / soft : 0;
                    if (k > 0 && exclude.Count > 0)
                    {
                        int e = Dist(v, exclude);
                        int ke = e <= fuzz ? 255 : e <= fuzz + soft ? 255 * (fuzz + soft - e) / soft : 0;
                        k = k * (255 - ke) / 255;
                    }
                }
                m[i] = (byte)(invert ? 255 - k : k);
            }
            return m;
        }
    }
}
