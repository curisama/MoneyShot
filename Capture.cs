// Money Shot — 화면 캡처 엔진과 이미지 유틸
// 모든 좌표는 물리 픽셀(가상 데스크톱 기준)이다. DPI 보정은 표시 계층에서만 한다.
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace MoneyShot
{
    public static class Cap
    {
        // 화면의 임의 영역을 물리 픽셀 그대로 떠온다.
        public static Bitmap Screen(RECT r, bool withCursor)
        {
            int w = Math.Max(1, r.W), h = Math.Max(1, r.H);
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                IntPtr dstDC = g.GetHdc();
                IntPtr srcDC = Native.GetDC(IntPtr.Zero);
                try
                {
                    // CAPTUREBLT를 함께 줘야 레이어드 창(툴팁, 반투명 UI)까지 잡힌다.
                    Native.BitBlt(dstDC, 0, 0, w, h, srcDC, r.Left, r.Top, Native.SRCCOPY | Native.CAPTUREBLT);
                }
                finally
                {
                    Native.ReleaseDC(IntPtr.Zero, srcDC);
                    g.ReleaseHdc(dstDC);
                }
            }
            if (withCursor) DrawCursor(bmp, r);
            return bmp;
        }

        public static Bitmap Screen(RECT r) { return Screen(r, Settings.Current.includeCursor); }

        public static Bitmap Virtual() { return Screen(Native.GetVirtualScreen(), false); }

        static void DrawCursor(Bitmap bmp, RECT origin)
        {
            try
            {
                var ci = new Native.CURSORINFO();
                ci.cbSize = Marshal.SizeOf(typeof(Native.CURSORINFO));
                if (!Native.GetCursorInfo(ref ci)) return;
                if ((ci.flags & Native.CURSOR_SHOWING) == 0) return;
                using (var g = Graphics.FromImage(bmp))
                {
                    IntPtr dc = g.GetHdc();
                    Native.DrawIconEx(dc, ci.ptScreenPos.X - origin.Left, ci.ptScreenPos.Y - origin.Top,
                                      ci.hCursor, 0, 0, 0, IntPtr.Zero, Native.DI_NORMAL);
                    g.ReleaseHdc(dc);
                }
            }
            catch { }
        }

        // 커서 아래에 있는 최상위 창의 실제 경계.
        public static RECT WindowUnderCursor()
        {
            POINT p;
            Native.GetCursorPos(out p);
            IntPtr h = Native.WindowFromPoint(p);
            if (h == IntPtr.Zero) return Native.GetVirtualScreen();
            IntPtr root = Native.GetAncestor(h, Native.GA_ROOT);
            if (root != IntPtr.Zero) h = root;
            var r = Native.GetRealWindowRect(h);
            return ClampToVirtual(r);
        }

        public static RECT MonitorUnderCursor()
        {
            POINT p;
            Native.GetCursorPos(out p);
            foreach (var m in Native.GetMonitors())
                if (p.X >= m.Left && p.X < m.Right && p.Y >= m.Top && p.Y < m.Bottom) return m;
            return Native.GetVirtualScreen();
        }

        public static RECT ClampToVirtual(RECT r)
        {
            var v = Native.GetVirtualScreen();
            if (r.Left < v.Left) r.Left = v.Left;
            if (r.Top < v.Top) r.Top = v.Top;
            if (r.Right > v.Right) r.Right = v.Right;
            if (r.Bottom > v.Bottom) r.Bottom = v.Bottom;
            return r;
        }

        // ---------- 변환 ----------

        public static BitmapSource ToSource(Bitmap bmp)
        {
            var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
                                    ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var src = BitmapSource.Create(bmp.Width, bmp.Height, 96, 96,
                    System.Windows.Media.PixelFormats.Bgra32, null,
                    data.Scan0, data.Stride * bmp.Height, data.Stride);
                src.Freeze();
                return src;
            }
            finally { bmp.UnlockBits(data); }
        }

        public static Bitmap Clone32(Bitmap src)
        {
            var b = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b))
            {
                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                g.DrawImage(src, 0, 0, src.Width, src.Height);
            }
            return b;
        }

        public static Bitmap Crop(Bitmap src, Rectangle r)
        {
            r.Intersect(new Rectangle(0, 0, src.Width, src.Height));
            if (r.Width <= 0 || r.Height <= 0) return Clone32(src);
            var b = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b))
            {
                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                g.DrawImage(src, new Rectangle(0, 0, r.Width, r.Height), r, GraphicsUnit.Pixel);
            }
            return b;
        }

        public static Bitmap Resize(Bitmap src, int w, int h)
        {
            w = Math.Max(1, w); h = Math.Max(1, h);
            var b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                using (var ia = new ImageAttributes())
                {
                    // 축소 시 가장자리에 인접 픽셀이 번지는 것을 막는다.
                    ia.SetWrapMode(System.Drawing.Drawing2D.WrapMode.TileFlipXY);
                    g.DrawImage(src, new Rectangle(0, 0, w, h), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia);
                }
            }
            return b;
        }

        // ---------- 클립보드 ----------

        // 앱마다 원하는 형식이 달라서 여러 형식을 한 번에 올린다.
        //   PNG          — 크롬, 슬랙, 피그마, 노션 (알파 유지)
        //   CF_BITMAP/DIB — 오피스, 한글, 그림판
        public static bool ToClipboard(Bitmap bmp)
        {
            for (int attempt = 0; attempt < 6; attempt++)
            {
                try
                {
                    var data = new System.Windows.DataObject();

                    var png = new MemoryStream();
                    bmp.Save(png, ImageFormat.Png);
                    png.Position = 0;
                    data.SetData("PNG", png, false);

                    // 알파가 있으면 DIB 계열에서 검게 뭉개지므로 흰 배경에 합성한 판을 따로 올린다.
                    using (var flat = HasAlpha(bmp) ? FlattenOnWhite(bmp) : Clone32(bmp))
                        data.SetImage(ToSource(flat));

                    System.Windows.Clipboard.SetDataObject(data, true);
                    return true;
                }
                catch
                {
                    System.Threading.Thread.Sleep(60);   // 다른 앱이 클립보드를 잡고 있는 중
                }
            }
            return false;
        }

        public static Bitmap FromClipboard()
        {
            try
            {
                if (System.Windows.Clipboard.ContainsData("PNG"))
                {
                    var ms = System.Windows.Clipboard.GetData("PNG") as MemoryStream;
                    if (ms != null) { ms.Position = 0; using (var t = new Bitmap(ms)) return Clone32(t); }
                }
                if (System.Windows.Clipboard.ContainsImage())
                {
                    var src = System.Windows.Clipboard.GetImage();
                    if (src != null) return FromSource(src);
                }
                if (System.Windows.Clipboard.ContainsFileDropList())
                {
                    var files = System.Windows.Clipboard.GetFileDropList();
                    foreach (string f in files)
                        if (IsImageFile(f)) return Load(f);
                }
            }
            catch { }
            return null;
        }

        // 열어볼 만한 확장자. 실제로 되는지는 시스템에 깔린 디코더에 달렸다.
        static readonly string[] ImageExts =
        {
            ".png", ".jpg", ".jpeg", ".jfif", ".bmp", ".dib", ".gif",
            ".tif", ".tiff", ".ico", ".webp", ".heic", ".heif", ".avif", ".jxr", ".wdp", ".dds", ".psd", ".psb"
        };

        public static bool IsImageFile(string p)
        {
            string e = Path.GetExtension(p ?? "").ToLowerInvariant();
            return Array.IndexOf(ImageExts, e) >= 0;
        }

        public static string OpenFilter
        {
            get
            {
                var all = "*" + string.Join(";*", ImageExts);
                return L.T("이미지 (", "Images (") + all + ")|" + all + L.T("|모든 파일|*.*", "|All files|*.*");
            }
        }

        // 그림 파일 한 장 읽기.
        //
        // 먼저 윈도 이미징(WIC)으로 연다. GDI+보다 아는 형식이 많고, WebP나 HEIC처럼
        // 확장 코덱이 깔려 있으면 그것도 그대로 읽는다. 그래도 안 되면 GDI+로 한 번 더 해본다.
        public static Bitmap Load(string path)
        {
            // PSD는 레이어를 합친 한 장으로. 레이어째 열려면 편집창이 Psd.Read를 직접 쓴다.
            if (Psd.IsPsd(path))
                return Psd.Read(path, new System.Collections.Generic.List<string>()).Flatten().ToBitmap();
            Exception first = null;
            try
            {
                using (var fs = File.OpenRead(path))
                {
                    var dec = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat,
                                                   BitmapCacheOption.OnLoad);
                    if (dec.Frames.Count > 0) return FromSource(dec.Frames[0]);
                }
            }
            catch (Exception ex) { first = ex; }

            try { using (var t = new Bitmap(path)) return Clone32(t); }
            catch (Exception ex)
            {
                throw new Exception(L.F("{0} 을(를) 읽지 못했다. 이 형식을 읽을 코덱이 시스템에 없을 수 있다. ({1})",
                    "Couldn't read {0}. Your system may not have a codec for this format. ({1})",
                    Path.GetFileName(path), first != null ? first.Message : ex.Message), ex);
            }
        }

        public static Bitmap FromSource(BitmapSource src)
        {
            var conv = new FormatConvertedBitmap(src, System.Windows.Media.PixelFormats.Bgra32, null, 0);
            var bmp = new Bitmap(conv.PixelWidth, conv.PixelHeight, PixelFormat.Format32bppArgb);
            var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
                                    ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try { conv.CopyPixels(System.Windows.Int32Rect.Empty, data.Scan0, data.Stride * bmp.Height, data.Stride); }
            finally { bmp.UnlockBits(data); }
            return bmp;
        }

        public static bool HasAlpha(Bitmap bmp)
        {
            if ((bmp.PixelFormat & PixelFormat.Alpha) == 0 && bmp.PixelFormat != PixelFormat.Format32bppArgb)
                return false;
            var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
                                    ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                unsafe
                {
                    byte* p = (byte*)data.Scan0;
                    for (int y = 0; y < bmp.Height; y++)
                    {
                        byte* row = p + y * data.Stride;
                        for (int x = 0; x < bmp.Width; x++)
                            if (row[x * 4 + 3] != 255) return true;
                    }
                }
            }
            finally { bmp.UnlockBits(data); }
            return false;
        }

        public static Bitmap FlattenOnWhite(Bitmap src)
        {
            var b = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b))
            {
                g.Clear(Color.White);
                g.DrawImage(src, 0, 0, src.Width, src.Height);
            }
            return b;
        }

        // ---------- 저장 ----------

        public static void Save(Bitmap bmp, string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            if (ext == ".jpg" || ext == ".jpeg")
            {
                using (var flat = HasAlpha(bmp) ? FlattenOnWhite(bmp) : Clone32(bmp))
                {
                    var enc = GetEncoder(ImageFormat.Jpeg);
                    var ps = new EncoderParameters(1);
                    ps.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 92L);
                    flat.Save(path, enc, ps);
                }
            }
            else if (ext == ".bmp") bmp.Save(path, ImageFormat.Bmp);
            else bmp.Save(path, ImageFormat.Png);
        }

        static ImageCodecInfo GetEncoder(ImageFormat fmt)
        {
            foreach (var c in ImageCodecInfo.GetImageEncoders())
                if (c.FormatID == fmt.Guid) return c;
            return null;
        }

        // 드래그 앤 드롭이나 외부 앱 전달용 임시 파일.
        public static string WriteTemp(Bitmap bmp, string hint)
        {
            Directory.CreateDirectory(Settings.TempDir);
            CleanTemp();
            string p = Path.Combine(Settings.TempDir,
                (string.IsNullOrEmpty(hint) ? "MoneyShot" : hint) + "_" +
                DateTime.Now.ToString("HHmmss_fff") + ".png");
            bmp.Save(p, ImageFormat.Png);
            return p;
        }

        static void CleanTemp()
        {
            try
            {
                var cutoff = DateTime.Now.AddHours(-6);
                foreach (var f in Directory.GetFiles(Settings.TempDir, "*.png"))
                    if (File.GetLastWriteTime(f) < cutoff) try { File.Delete(f); } catch { }
            }
            catch { }
        }
    }

}
