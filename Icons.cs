// Money Shot — 아이콘
//
// 아이콘 폰트(Segoe Fluent Icons)를 쓰지 않고 벡터 패스로 직접 그린다.
// 폰트 코드포인트는 윈도 버전에 따라 다른 그림이 나오거나 아예 빈 네모가 뜨는데,
// 도구 막대는 그림이 곧 기능이라 그런 불확실성을 둘 자리가 아니다.
//
// 모든 좌표는 24×24 기준. 표시 크기에 맞춰 비율로 늘린다.
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MoneyShot
{
    public static class Icons
    {
        const double Box = 24;

        struct Def { public string D; public bool Filled; }

        static readonly Dictionary<string, Def> Map = new Dictionary<string, Def>();

        static void Add(string name, string d) { Map[name] = new Def { D = d, Filled = false }; }
        static void AddFill(string name, string d) { Map[name] = new Def { D = d, Filled = true }; }

        static Icons()
        {
            // ---- 도구 ----
            AddFill("select", "M6,3 L6,19.6 L10.3,15.4 L12.9,20.9 L15.4,19.7 L12.8,14.4 L18.6,14.2 Z");
            Add("crop", "M7,2 V17 H22 M2,7 H17 V22");
            Add("rect", "M4,6.5 H20 V17.5 H4 Z");
            Add("ellipse", "M12,5.5 C16.4,5.5 20,8.4 20,12 C20,15.6 16.4,18.5 12,18.5 C7.6,18.5 4,15.6 4,12 C4,8.4 7.6,5.5 12,5.5 Z");
            Add("arrow", "M4,20 L19,5 M19,5 H12.5 M19,5 V11.5");
            Add("line", "M4.5,19.5 L19.5,4.5");
            Add("pen", "M3,21 L4,17 L15.5,5.5 L18.5,8.5 L7,20 Z M13.5,7.5 L16.5,10.5");
            // 형광펜 — 채운 마커와 그어진 자국. 선으로만 그리면 펜과 구분이 안 된다.
            AddFill("highlight", "M8,14.2 L15,7.2 L18.6,10.8 L11.6,17.8 L8,17.8 Z M15.2,7 L17.4,4.8 L21,8.4 L18.8,10.6 Z M3.5,20 H20.5 V22.6 H3.5 Z");
            Add("text", "M5,7 V4.5 H19 V7 M12,4.5 V19.5 M9,19.5 H15");
            Add("counter", "M12,3.5 C16.7,3.5 20.5,7.3 20.5,12 C20.5,16.7 16.7,20.5 12,20.5 C7.3,20.5 3.5,16.7 3.5,12 C3.5,7.3 7.3,3.5 12,3.5 Z M10.3,9.6 L12.6,8 V16.2");
            // 모자이크 — 3×3 바둑판. 큰 픽셀로 뭉갠다는 뜻이 한눈에 읽힌다.
            AddFill("mosaic", "M3,3 H9 V9 H3 Z M15,3 H21 V9 H15 Z M9,9 H15 V15 H9 Z M3,15 H9 V21 H3 Z M15,15 H21 V21 H15 Z");
            // 스팟 힐링 — 비스듬한 반창고
            Add("heal", "M3.8,15.8 L15.8,3.8 A3.1,3.1 0 0 1 20.2,8.2 L8.2,20.2 A3.1,3.1 0 0 1 3.8,15.8 Z M9.9,12 L12,9.9 L14.1,12 L12,14.1 Z");
            // 도장 — 손잡이와 밑판
            Add("clone", "M9.5,3.5 H14.5 V8.5 C14.5,10.3 19,10.6 19,13.6 V15.5 H5 V13.6 C5,10.6 9.5,10.3 9.5,8.5 Z M3.5,19.5 H20.5");
            // 유동화 — 손가락으로 민 물결
            Add("liquify", "M3,15.5 C6,10.5 9,20.5 12,15.5 C15,10.5 18,20.5 21,15.5 M3,9.5 C6,4.5 9,14.5 12,9.5 C15,4.5 18,14.5 21,9.5");
            // 붓 — 비스듬한 붓대와 털
            Add("brush", "M20.5,3.5 L11,13 M11,13 L13,15 M13,15 L22,5.5 L20.5,3.5 M11,13 C8,12.5 6,14.5 6,17 C6,19 4.5,20 3,20.5 C6,21.5 10.5,20.5 12,18 C13,16.5 13,15 13,15");
            // 그라디언트 — 칸이 점점 엷어지는 띠
            AddFill("gradient", "M3,5 H7 V19 H3 Z M8.5,5 H11 V19 H8.5 Z M12.5,5 H14.5 V19 H12.5 Z M16,5 H17.5 V19 H16 Z M19,5 H20 V19 H19 Z");
            Add("blur", "M12,3.2 C12,3.2 19,10 19,14.4 C19,18.1 15.9,20.8 12,20.8 C8.1,20.8 5,18.1 5,14.4 C5,10 12,3.2 12,3.2 Z M8.6,14.8 C8.6,16.8 10.1,18.2 12,18.2");
            // 마술봉 — 막대와 반짝임
            Add("wand", "M3.5,20.5 L13.5,10.5 M17.5,2.6 L18.7,6.3 L22.4,7.5 L18.7,8.7 L17.5,12.4 L16.3,8.7 L12.6,7.5 L16.3,6.3 Z");
            // 배경 지우개 — 지우개 뒤로 투명 체크무늬가 따라온다
            Add("bgerase", "M8.6,19.2 L3.4,14 L12.4,5 L17.6,10.2 L8.6,19.2 Z M6.9,10.7 L11.9,15.7 M15,17 H17.5 V19.5 H15 Z M17.5,19.5 H20 V22 H17.5 Z");
            Add("erase", "M9,20.5 L3.5,15 L13,5.5 L18.5,11 L9,20.5 Z M7.3,11.7 L12.3,16.7 M20.5,20.5 H11");
            // 스포이드
            // 복구 브러시 — 지운 자리를 도로 칠해 되살린다
            Add("restore", "M4,20.5 L9.5,15 L14.5,20 L9,25.5 Z M12.5,12.5 C12.5,8.4 15.9,5 20,5 M20,5 L17,2.4 M20,5 L17,7.8 M6.5,18 L11.5,23");
            Add("movelayer", "M12,3.5 L15,6.5 H13 V11 H17.5 V9 L20.5,12 L17.5,15 V13 H13 V17.5 H15 L12,20.5 L9,17.5 H11 V13 H6.5 V15 L3.5,12 L6.5,9 V11 H11 V6.5 H9 Z");
            Add("layers", "M12,3 L21,7.5 L12,12 L3,7.5 Z M3,12 L12,16.5 L21,12 M3,16.5 L12,21 L21,16.5");
            Add("eye", "M12,5.5 C17,5.5 21,9 22,12 C21,15 17,18.5 12,18.5 C7,18.5 3,15 2,12 C3,9 7,5.5 12,5.5 Z M12,9.2 A2.8,2.8 0 1 1 12,14.8 A2.8,2.8 0 1 1 12,9.2 Z");
            Add("eye-off", "M4,4 L20,20 M9.4,9.6 A2.8,2.8 0 0 0 12,14.8 M6.2,6.6 C4.1,8 2.7,10.1 2,12 C3,15 7,18.5 12,18.5 C14,18.5 15.8,18 17.3,17.2 M9.8,5.9 C10.5,5.6 11.2,5.5 12,5.5 C17,5.5 21,9 22,12 C21.6,13.2 20.8,14.5 19.7,15.6");
            Add("up", "M12,19 V6 M6.5,11.5 L12,6 L17.5,11.5");
            Add("down", "M12,5 V18 M6.5,12.5 L12,18 L17.5,12.5");
            Add("plus", "M12,5 V19 M5,12 H19");
            // 선택 모양
            Add("sel-rect", "M4,4 H20 V20 H4 Z");
            Add("sel-ellipse", "M12,4.5 C16.7,4.5 20.5,7.9 20.5,12 C20.5,16.1 16.7,19.5 12,19.5 C7.3,19.5 3.5,16.1 3.5,12 C3.5,7.9 7.3,4.5 12,4.5 Z");
            Add("sel-lasso", "M12,4 C16.9,4 21,6.9 21,10.5 C21,14.1 16.9,17 12,17 C10.4,17 8.9,16.7 7.6,16.2 M7.6,16.2 C6.6,17 6.2,18.2 6.6,19.3 C7,20.4 8.1,21 9.2,20.8 M7.6,16.2 C4.8,15 3,12.9 3,10.5 C3,6.9 7.1,4 12,4");
            // 물체 선택 — 점선 틀 안의 화살표와 반짝이
            Add("sel-object", "M3,8 V3 H8 M16,3 H21 V8 M21,16 V21 H16 M8,21 H3 V16 M9,8.5 L9,18 L11.6,15.6 L13.3,19.3 L15,18.5 L13.4,15 L16.8,14.8 Z");
            // 색상 범위 — 스포이드와 물방울
            Add("sel-color", "M14.5,4.5 L19.5,9.5 M17,2.5 L21.5,7 L19,9.5 L14.5,5 Z M16.5,8 L8,16.5 L6,17 L5.5,19 L7.5,18.5 L8,16.5 M5,21 C3.6,21 3,20 3,19.2 C3,18.2 5,15.5 5,15.5 C5,15.5 7,18.2 7,19.2 C7,20 6.4,21 5,21 Z");
            Add("sel-magnetic", "M6,4 V12 A6,6 0 0 0 18,12 V4 M6,4 H10 V9 H6 Z M14,4 H18 V9 H14 Z M3,18 L5,19 M8,20.5 L10.5,21 M13.5,21 L16,20.5 M19,19 L21,18");
            // 자유 변형 — 모서리 손잡이가 달린 틀
            Add("transform", "M7,7 H17 V17 H7 Z M4,4 H7 V7 H4 Z M17,4 H20 V7 H17 Z M4,17 H7 V20 H4 Z M17,17 H20 V20 H17 Z");
            Add("invert", "M12,3.5 A8.5,8.5 0 1 1 12,20.5 A8.5,8.5 0 1 1 12,3.5 Z M12,3.5 V20.5 A8.5,8.5 0 0 0 12,3.5 Z");
            Add("picker", "M19.6,4.4 C18.5,3.3 16.8,3.3 15.7,4.4 L13.9,6.2 L12.9,5.2 L11.5,6.6 L17.4,12.5 L18.8,11.1 L17.8,10.1 L19.6,8.3 C20.7,7.2 20.7,5.5 19.6,4.4 Z M11.5,8.2 L4.6,15.1 L4,20 L8.9,19.4 L15.8,12.5");

            // ---- 동작 ----
            Add("undo", "M4.5,9 H14.5 A5.5,5.5 0 1 1 14.5,20 H9 M4.5,9 L9,4.5 M4.5,9 L9,13.5");
            Add("redo", "M19.5,9 H9.5 A5.5,5.5 0 1 0 9.5,20 H15 M19.5,9 L15,4.5 M19.5,9 L15,13.5");
            AddFill("more", "M5.4,10.2 A1.8,1.8 0 1 0 5.4,13.8 A1.8,1.8 0 1 0 5.4,10.2 Z M12,10.2 A1.8,1.8 0 1 0 12,13.8 A1.8,1.8 0 1 0 12,10.2 Z M18.6,10.2 A1.8,1.8 0 1 0 18.6,13.8 A1.8,1.8 0 1 0 18.6,10.2 Z");
            Add("copy", "M8.5,8.5 H20 V20 H8.5 Z M5.5,15.5 H4 V4 H15.5 V5.5");
            Add("save", "M12,3.5 V14.8 M7.6,10.4 L12,14.8 L16.4,10.4 M4,19.5 H20");
            Add("close", "M6.2,6.2 L17.8,17.8 M17.8,6.2 L6.2,17.8");
            // 창 단추 — 윈도 관례를 그대로 따른다
            Add("minimize", "M5,12.5 H19");
            // 화살표 촉 방향
            Add("head-end", "M4,12 H17.5 M12.5,7.6 L17.5,12 L12.5,16.4");
            Add("head-start", "M20,12 H6.5 M11.5,7.6 L6.5,12 L11.5,16.4");
            Add("head-both", "M6.5,12 H17.5 M11,8.2 L6.5,12 L11,15.8 M13,8.2 L17.5,12 L13,15.8");
            // 선 종류
            Add("dash-solid", "M3.5,12 H20.5");
            Add("dash-dash", "M3.5,12 H9 M12,12 H17.5");
            Add("dash-dot", "M4.4,12 H4.5 M8.4,12 H8.5 M12.4,12 H12.5 M16.4,12 H16.5 M20.4,12 H20.5");
            Add("dash-dashdot", "M3.5,12 H9.5 M12.9,12 H13 M16,12 H20.5");
            Add("maximize", "M5.5,5.5 H18.5 V18.5 H5.5 Z");
            Add("restore", "M8.5,8.5 H18.5 V18.5 H8.5 Z M5.5,15.5 V5.5 H15.5");
            Add("check", "M5,12.4 L9.8,17.4 L19,6.6");
            Add("trash", "M4,6.5 H20 M9.5,6.5 V4.3 H14.5 V6.5 M6.4,6.5 L7.4,20.6 H16.6 L17.6,6.5");
            Add("zoomin", "M11,4.6 A6.4,6.4 0 1 1 11,17.4 A6.4,6.4 0 1 1 11,4.6 Z M15.6,15.6 L20.8,20.8 M11,8 V14 M8,11 H14");
            Add("zoomout", "M11,4.6 A6.4,6.4 0 1 1 11,17.4 A6.4,6.4 0 1 1 11,4.6 Z M15.6,15.6 L20.8,20.8 M8,11 H14");
            Add("fit", "M4,9 V4 H9 M15,4 H20 V9 M20,15 V20 H15 M9,20 H4 V15");
            Add("folder", "M3.5,6.5 H10 L12,9 H20.5 V18.5 H3.5 Z");
            Add("settings", "M12,8.6 A3.4,3.4 0 1 1 12,15.4 A3.4,3.4 0 1 1 12,8.6 Z M12,2.8 L13,5.4 M12,21.2 L11,18.6 M2.8,12 L5.4,11 M21.2,12 L18.6,13 M5.4,5.4 L7.6,7 M18.6,18.6 L16.4,17 M18.6,5.4 L16.4,7 M5.4,18.6 L7.6,17");

            Map["edit"] = Map["pen"];
        }

        // 지정한 크기의 아이콘 하나.
        //
        // 확대는 RenderTransform이 아니라 도형 자체에 건다. RenderTransform으로 키우면
        // 선 굵기까지 같이 늘어나 소수점 좌표에 걸리고, 그게 화면에서 흐릿하게 보인다.
        // 도형만 줄여두고 굵기는 화면 단위로 직접 주면 어느 크기에서도 또렷하다.
        public static Path Make(string name, double size, Brush brush)
        {
            Def d;
            if (!Map.TryGetValue(name, out d)) d = Map["rect"];

            var g = Geometry.Parse(d.D).Clone();
            g.Transform = new ScaleTransform(size / Box, size / Box);
            g.Freeze();

            var p = new Path
            {
                Data = g,
                Width = size,
                Height = size,
                Stretch = Stretch.None,
                IsHitTestVisible = false
            };
            if (d.Filled) p.Fill = brush;
            else
            {
                p.Stroke = brush;
                p.StrokeThickness = size <= 15 ? 1.4 : 1.55;
                p.StrokeStartLineCap = PenLineCap.Round;
                p.StrokeEndLineCap = PenLineCap.Round;
                p.StrokeLineJoin = PenLineJoin.Round;
            }
            return p;
        }

        // 아이콘을 정해진 칸 가운데에 놓는다. RenderTransform으로 키우면 배치가 어긋나므로 감싼다.
        public static FrameworkElement Boxed(string name, double size, Brush brush)
        {
            var p = Make(name, size, brush);
            var host = new System.Windows.Controls.Canvas
            {
                Width = size, Height = size,
                Background = Brushes.Transparent,
                IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            host.Children.Add(p);
            return host;
        }

        // 회전 커서. 윈도에 기본으로 없어서 직접 그려 만든다.
        // 어느 배경에서도 보이도록 검은 테를 두른 흰 화살표로 그린다.
        static System.Windows.Input.Cursor rotateCursor;
        static bool rotateCursorTried;

        public static System.Windows.Input.Cursor Rotate
        {
            get
            {
                if (rotateCursorTried) return rotateCursor ?? System.Windows.Input.Cursors.Hand;
                rotateCursorTried = true;
                try { rotateCursor = BuildRotateCursor(); }
                catch { rotateCursor = null; }
                return rotateCursor ?? System.Windows.Input.Cursors.Hand;
            }
        }

        static System.Windows.Input.Cursor BuildRotateCursor()
        {
            const int N = 32;
            using (var bmp = new System.Drawing.Bitmap(N, N, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.Clear(System.Drawing.Color.Transparent);
                    var box = new System.Drawing.RectangleF(7.5f, 7.5f, 17f, 17f);

                    // 검은 테를 먼저 두껍게 깔고 그 위에 흰 선 — 어느 배경에서도 보인다
                    for (int pass = 0; pass < 2; pass++)
                    {
                        var col = pass == 0
                            ? System.Drawing.Color.FromArgb(210, 0, 0, 0)
                            : System.Drawing.Color.White;
                        using (var pen = new System.Drawing.Pen(col, pass == 0 ? 5f : 2.4f))
                        {
                            pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                            pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                            g.DrawArc(pen, box, 35, 265);
                        }
                        using (var br = new System.Drawing.SolidBrush(col))
                        {
                            float cx = box.X + box.Width / 2, cy = box.Y + box.Height / 2;
                            double rad = 35 * Math.PI / 180.0;
                            float ax = cx + (float)(Math.Cos(rad) * box.Width / 2);
                            float ay = cy + (float)(Math.Sin(rad) * box.Height / 2);
                            float t = pass == 0 ? 6.2f : 4.4f;
                            g.FillPolygon(br, new[]
                            {
                                new System.Drawing.PointF(ax + t * 1.1f, ay + t * 0.1f),
                                new System.Drawing.PointF(ax - t * 0.4f, ay - t),
                                new System.Drawing.PointF(ax - t * 0.3f, ay + t)
                            });
                        }
                    }
                }
                return CursorFromBitmap(bmp, N / 2, N / 2);
            }
        }

        // 고전 CUR 형식으로 싼다.
        //
        // PNG를 담는 방식도 요즘 윈도는 읽지만, 안 읽는 경우 예외 없이 '보이지 않는 커서'가
        // 되어버려서 알아채기가 어렵다. 어디서나 되는 DIB로 쓴다.
        // 높이를 두 배로 적는 건 이 형식이 색 그림 아래에 마스크를 이어 붙이기 때문이다.
        static System.Windows.Input.Cursor CursorFromBitmap(System.Drawing.Bitmap bmp, int hotX, int hotY)
        {
            int w = bmp.Width, h = bmp.Height;
            var data = bmp.LockBits(new System.Drawing.Rectangle(0, 0, w, h),
                System.Drawing.Imaging.ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var xor = new byte[w * h * 4];
            try
            {
                // DIB는 아래에서 위로 쌓는다
                for (int y = 0; y < h; y++)
                    System.Runtime.InteropServices.Marshal.Copy(
                        data.Scan0 + (h - 1 - y) * data.Stride, xor, y * w * 4, w * 4);
            }
            finally { bmp.UnlockBits(data); }

            int maskStride = ((w + 31) / 32) * 4;
            var andMask = new byte[maskStride * h];      // 전부 0 = 알파를 그대로 쓴다

            var ms = new System.IO.MemoryStream();
            var bw = new System.IO.BinaryWriter(ms);

            bw.Write((ushort)0);                 // reserved
            bw.Write((ushort)2);                 // 2 = 커서
            bw.Write((ushort)1);                 // 한 장
            bw.Write((byte)w); bw.Write((byte)h);
            bw.Write((byte)0); bw.Write((byte)0);
            bw.Write((ushort)hotX); bw.Write((ushort)hotY);
            int bodyLen = 40 + xor.Length + andMask.Length;
            bw.Write((uint)bodyLen);
            bw.Write((uint)22);                  // 본문 시작

            bw.Write(40);                        // BITMAPINFOHEADER 크기
            bw.Write(w);
            bw.Write(h * 2);                     // 색 그림 + 마스크
            bw.Write((ushort)1);                 // 평면
            bw.Write((ushort)32);                // 비트 심도
            bw.Write(0);                         // 압축 없음
            bw.Write(xor.Length + andMask.Length);
            bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);

            bw.Write(xor);
            bw.Write(andMask);
            bw.Flush();
            ms.Position = 0;
            return new System.Windows.Input.Cursor(ms);
        }

        // 색만 바꿔야 할 때. Boxed가 돌려준 것을 넘긴다.
        public static void Tint(FrameworkElement boxed, Brush brush)
        {
            var host = boxed as System.Windows.Controls.Canvas;
            if (host == null || host.Children.Count == 0) return;
            var p = host.Children[0] as Path;
            if (p == null) return;
            if (p.Fill != null) p.Fill = brush; else p.Stroke = brush;
        }
    }
}
