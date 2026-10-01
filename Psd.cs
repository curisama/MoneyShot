// Money Shot — 포토샵 PSD 열기·저장
//
// 열기: PSD(v1)와 PSB(v2). 8·16비트 RGB·회색조·CMYK, 레이어 없는 인덱스 색.
// 압축은 원본 그대로(0)·PackBits(1)·ZIP(2)·ZIP+예측(3)을 다 푼다.
// 레이어마다 실제 픽셀만 가져온다. 글자·스마트 오브젝트·셰이프는 포토샵이 같이 넣어 둔 래스터를 쓰고,
// 그룹은 풀어서 안의 레이어만 남긴다(숨긴 그룹 안은 숨김, 그룹 불투명도는 안쪽에 곱한다).
// 버리거나 바꾼 것은 notes에 사람이 읽을 말로 남긴다.
//
// 저장: 8비트 RGB PSD(v1). 레이어마다 위치·불투명도·블렌드·숨김·마스크·이름을 쓰고,
// 맨 끝에 합친 그림을 붙인다. 레이어를 못 읽는 뷰어(탐색기 미리보기 등)는 이것만 본다.
// 한글 이름은 파스칼 문자열에 못 넣으니 'luni'(유니코드 이름) 블록에 따로 넣는다.
//
// Compositor(MIT, Wonder Assembly LLC)의 PSDReader·PSDChannelCoder를 옮겼다.
// 저장 쪽은 원본에 없어 어도비 Photoshop File Formats Specification을 보고 새로 짰다.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace MoneyShot
{
    public static class Psd
    {
        // 블렌드 모드 ↔ 포토샵 키. 순서가 BlendMode 열거형과 같아야 한다.
        static readonly string[] Keys = {
            "norm",
            "dark", "mul ", "idiv", "lbrn",
            "lite", "scrn", "div ", "lddg",
            "over", "sLit", "hLit", "vLit", "lLit", "pLit", "hMix",
            "diff", "smud", "fsub", "fdiv",
            "hue ", "sat ", "colr", "lum "
        };

        const int MaxSide = 300000;              // PSB 한도
        const long MaxPixels = 200L * 1000 * 1000; // int[] 하나로 담을 수 있는 선에서 끊는다

        public static bool IsPsd(string path)
        {
            try
            {
                using (var f = File.OpenRead(path))
                {
                    var b = new byte[4];
                    if (f.Read(b, 0, 4) != 4) return false;
                    return b[0] == '8' && b[1] == 'B' && b[2] == 'P' && b[3] == 'S';
                }
            }
            catch { return false; }
        }

        // ================= 읽기 =================

        class Cur
        {
            public byte[] D;
            public long P;
            public bool Psb;
            public Cur(byte[] d) { D = d; }

            void Need(long n) { if (P < 0 || n < 0 || P + n > D.Length) throw new InvalidDataException(L.T("PSD 파일이 중간에 끊겼다.", "The PSD file is truncated.")); }
            public void Skip(long n) { Need(n); P += n; }
            public byte U8() { Need(1); return D[P++]; }
            public int U16() { Need(2); int v = (D[P] << 8) | D[P + 1]; P += 2; return v; }
            public int I16() { return (short)U16(); }
            public uint U32() { Need(4); uint v = ((uint)D[P] << 24) | ((uint)D[P + 1] << 16) | ((uint)D[P + 2] << 8) | D[P + 3]; P += 4; return v; }
            public int I32() { return (int)U32(); }
            public long U64() { long hi = U32(); long lo = U32(); if (hi > int.MaxValue) throw new InvalidDataException(L.T("PSD 길이 값이 너무 크다.", "A PSD length value is too large.")); return (hi << 32) | lo; }
            public long Len() { return Psb ? U64() : (long)U32(); }
            public string Str4() { Need(4); var s = Encoding.ASCII.GetString(D, (int)P, 4); P += 4; return s; }
            public bool At(string s) { return P + 4 <= D.Length && D[P] == s[0] && D[P + 1] == s[1] && D[P + 2] == s[2] && D[P + 3] == s[3]; }
        }

        class Raw
        {
            public string Name = "";
            public int Top, Left, Bottom, Right;
            public List<int> ChId = new List<int>();
            public List<long> ChLen = new List<long>();
            public string Blend = "norm";
            public int Opacity = 255, Fill = 255;
            public bool Clipping, Hidden;
            public bool HasMask, MaskDisabled;
            public int MTop, MLeft, MBottom, MRight, MDefault = 255;
            public bool HasReal;
            public int RTop, RLeft, RBottom, RRight, RDefault = 255;
            public int Section;                  // 0 보통 / 1·2 그룹 머리 / 3 그룹 끝 칸막이
            public HashSet<string> Extra = new HashSet<string>();
            public Dictionary<int, byte[]> Planes = new Dictionary<int, byte[]>();
            public int W { get { return Right - Left; } }
            public int H { get { return Bottom - Top; } }
        }

        // PSB에서 길이를 8바이트로 쓰는 추가 정보 키들(스펙 표 그대로)
        static readonly HashSet<string> LongKeys = new HashSet<string> {
            "LMsk", "Lr16", "Lr32", "Layr", "Mt16", "Mt32", "Mtrn", "Alph", "FMsk", "lnk2", "FEid", "FXid", "PxSD"
        };
        static readonly HashSet<string> AdjKeys = new HashSet<string> {
            "levl", "curv", "hue2", "hue ", "expA", "grdm", "brit", "blnc", "nvrt", "thrs", "post", "mixr",
            "selc", "blwh", "phfl", "vibA", "clrL", "SoCo", "GdFl", "PtFl"
        };

        static void Note(List<string> notes, string s)
        {
            if (notes != null && !notes.Contains(s)) notes.Add(s);
        }

        public static Doc Read(string path, List<string> notes)
        {
            byte[] data = File.ReadAllBytes(path);
            try { return Parse(data, notes); }
            catch (IndexOutOfRangeException) { throw new InvalidDataException(L.T("PSD 파일이 깨졌다.", "The PSD file is corrupted.")); }
            catch (ArgumentException) { throw new InvalidDataException(L.T("PSD 파일이 깨졌다.", "The PSD file is corrupted.")); }
        }

        static Doc Parse(byte[] data, List<string> notes)
        {
            var c = new Cur(data);
            if (c.Str4() != "8BPS") throw new InvalidDataException(L.T("PSD 파일이 아니다.", "Not a PSD file."));
            int ver = c.U16();
            if (ver != 1 && ver != 2) throw new InvalidDataException(L.F("모르는 PSD 버전이다({0}).", "Unknown PSD version ({0}).", ver));
            c.Psb = ver == 2;
            c.Skip(6);
            int nch = c.U16();
            int ch = (int)c.U32(), cw = (int)c.U32();
            int depth = c.U16(), mode = c.U16();
            if (cw <= 0 || ch <= 0 || cw > MaxSide || ch > MaxSide || (long)cw * ch > MaxPixels)
                throw new InvalidDataException(L.F("그림이 너무 크다({0}×{1}).", "The image is too large ({0}×{1}).", cw, ch));
            if (depth == 1) throw new NotSupportedException(L.T("1비트(비트맵) PSD는 열 수 없다.", "Can't open 1-bit (bitmap) PSD files."));
            if (depth == 32) throw new NotSupportedException(L.T("32비트 PSD는 열 수 없다. 포토샵에서 8비트나 16비트로 바꿔 저장해 달라.", "Can't open 32-bit PSD files. Convert to 8 or 16 bits in Photoshop and save again."));
            if (depth != 8 && depth != 16) throw new NotSupportedException(L.F("{0}비트 PSD는 열 수 없다.", "Can't open {0}-bit PSD files.", depth));
            // 1 회색조 · 2 인덱스 · 3 RGB · 4 CMYK · 8 듀오톤(회색조처럼 읽힌다)
            if (mode != 1 && mode != 2 && mode != 3 && mode != 4 && mode != 8)
                throw new NotSupportedException(L.F("이 색 모드({0})의 PSD는 열 수 없다. RGB로 바꿔 저장해 달라.", "Can't open PSD files in this color mode ({0}). Convert to RGB and save again.", ModeName(mode)));
            if (depth == 16) Note(notes, L.T("16비트 그림을 8비트로 줄였다.", "Reduced the 16-bit image to 8 bits."));
            if (mode == 1 || mode == 8) Note(notes, L.T("회색조 그림을 RGB로 바꿨다.", "Converted the grayscale image to RGB."));
            if (mode == 8) Note(notes, L.T("듀오톤 색은 살리지 못하고 회색으로 읽었다.", "Duotone colors couldn't be kept; read as grayscale."));
            if (mode == 4) Note(notes, L.T("CMYK를 RGB로 단순 변환했다. 색이 포토샵과 조금 다를 수 있다.", "Converted CMYK to RGB with a simple conversion. Colors may differ slightly from Photoshop."));

            long cmLen = c.U32();
            byte[] palette = null;
            if (mode == 2 && cmLen >= 768) { palette = new byte[768]; Array.Copy(data, c.P, palette, 0, 768); }
            c.Skip(cmLen);
            c.Skip(c.U32());                         // 이미지 리소스 — 쓰는 것 없음

            long secLen = c.Len();
            long secEnd = c.P + secLen;
            var raws = new List<Raw>();
            bool mergedAlpha = false;
            if (secLen > 0)
            {
                long infoLen = c.Len();
                long infoEnd = c.P + infoLen;
                if (infoLen > 0) mergedAlpha = ReadLayerInfo(c, infoEnd, raws, depth, notes);
                c.P = infoEnd;
                // 16·32비트 문서는 레이어를 여기가 아니라 뒤쪽 Lr16/Lr32 블록에 넣는다
                if (c.P + 4 <= secEnd)
                {
                    long gm = c.U32();
                    c.Skip(gm);
                    while (c.P + 12 <= secEnd)
                    {
                        if (!c.At("8BIM") && !c.At("8B64")) { c.P++; continue; }   // 4바이트 정렬 채움을 건너뛴다
                        string sig = c.Str4();
                        string key = c.Str4();
                        long len = (sig == "8B64" || (c.Psb && LongKeys.Contains(key))) ? c.U64() : c.U32();
                        long start = c.P;
                        if ((key == "Lr16" || key == "Lr32" || key == "Layr") && raws.Count == 0 && len > 0)
                        {
                            if (key == "Lr32") throw new NotSupportedException(L.T("32비트 레이어는 열 수 없다.", "Can't open 32-bit layers."));
                            mergedAlpha = ReadLayerInfo(c, start + len, raws, depth, notes);
                        }
                        c.P = start + len;
                    }
                }
            }
            c.P = secEnd;

            // ----- 그룹 풀기 -----
            // 레코드는 아래 레이어부터 온다. 그룹은 [끝 칸막이(3), 안쪽 레이어들, 그룹 머리(1·2)] 순서라
            // 위에서부터 거꾸로 훑으면 머리를 먼저 만나 그룹의 숨김·불투명도를 안쪽에 내려줄 수 있다.
            var keep = new List<Raw>();
            var opStack = new List<double>();
            var hideStack = new List<bool>();
            double curOp = 1; bool curHide = false;
            int groups = 0, empty = 0, adjust = 0;
            for (int i = raws.Count - 1; i >= 0; i--)
            {
                var r = raws[i];
                if (r.Section == 1 || r.Section == 2)
                {
                    groups++;
                    opStack.Add(curOp); hideStack.Add(curHide);
                    curOp *= r.Opacity / 255.0;
                    curHide = curHide || r.Hidden;
                    if (r.Blend != "pass" && r.Blend != "norm")
                        Note(notes, L.T("그룹에 준 블렌드 모드는 버렸다. 안쪽 레이어는 각자 모드대로 섞인다.", "Dropped group blend modes. Layers inside blend with their own modes."));
                    if (r.Extra.Contains("lfx2") || r.Extra.Contains("lrFX") || r.Extra.Contains("lmfx"))
                        Note(notes, L.T("그룹에 준 레이어 효과는 버렸다.", "Dropped layer effects on groups."));
                    if (r.HasMask) Note(notes, L.T("그룹 마스크는 버렸다.", "Dropped group masks."));
                    continue;
                }
                if (r.Section == 3)
                {
                    if (opStack.Count > 0)
                    {
                        curOp = opStack[opStack.Count - 1]; opStack.RemoveAt(opStack.Count - 1);
                        curHide = hideStack[hideStack.Count - 1]; hideStack.RemoveAt(hideStack.Count - 1);
                    }
                    continue;
                }
                if (r.W <= 0 || r.H <= 0)
                {
                    bool adj = false;
                    foreach (var k in r.Extra) if (AdjKeys.Contains(k)) adj = true;
                    if (adj) adjust++; else empty++;
                    continue;
                }
                r.Hidden = r.Hidden || curHide;
                r.Opacity = (int)Math.Round(r.Opacity * curOp);
                keep.Insert(0, r);
            }
            if (groups > 0) Note(notes, L.F("그룹 {0}개를 풀어 레이어만 남겼다.", "Ungrouped {0} groups, keeping only the layers.", groups));
            if (adjust > 0) Note(notes, L.F("조정·칠 레이어 {0}개는 뺐다(픽셀이 없는 레이어라 옮길 수 없다).", "Left out {0} adjustment/fill layers (they have no pixels to bring over).", adjust));
            if (empty > 0) Note(notes, L.F("빈 레이어 {0}개는 뺐다.", "Left out {0} empty layers.", empty));

            Doc doc = null;
            if (keep.Count > 0)
            {
                foreach (var r in keep)
                {
                    var px = Compose(r, mode, depth);
                    if (doc == null) { doc = new Doc(px, cw, ch); Apply(doc.Layers[0], r, notes); }
                    else Apply(doc.Add(px, r.Name, r.Left, r.Top), r, notes);
                }
            }
            else
            {
                // 레이어가 없으면 맨 끝의 합친 그림을 한 장짜리로 쓴다
                var px = ReadMerged(c, cw, ch, nch, depth, mode, palette, mergedAlpha, notes);
                doc = new Doc(px, cw, ch);
            }
            doc.Active = doc.Layers.Count - 1;
            doc.CompositeAll();
            return doc;
        }

        static string ModeName(int mode)
        {
            switch (mode)
            {
                case 0: return L.T("비트맵", "Bitmap");
                case 7: return L.T("멀티채널", "Multichannel");
                case 9: return "Lab";
                default: return mode.ToString();
            }
        }

        // 레이어 정보 블록 하나를 읽는다. 돌려주는 값: 합친 그림의 첫 알파 채널이 투명도인가
        static bool ReadLayerInfo(Cur c, long end, List<Raw> raws, int depth, List<string> notes)
        {
            int count = c.I16();
            bool alpha = count < 0;
            count = Math.Abs(count);
            for (int i = 0; i < count; i++) raws.Add(ReadRecord(c, notes));
            foreach (var r in raws)
            {
                for (int k = 0; k < r.ChId.Count; k++)
                {
                    long start = c.P, len = r.ChLen[k];
                    int id = r.ChId[k];
                    if (len >= 2 && (id >= -3 && id <= 3))
                    {
                        int comp = c.U16();
                        int w, h;
                        if (id == -2) { w = r.MRight - r.MLeft; h = r.MBottom - r.MTop; }
                        else if (id == -3) { w = r.RRight - r.RLeft; h = r.RBottom - r.RTop; }
                        else { w = r.W; h = r.H; }
                        if (w > 0 && h > 0 && r.Section == 0)
                        {
                            if (w > MaxSide || h > MaxSide || (long)w * h > MaxPixels)
                                throw new InvalidDataException(L.F("레이어가 너무 크다({0}×{1}).", "A layer is too large ({0}×{1}).", w, h));
                            r.Planes[id] = Decode(c.D, c.P, start + len, comp, w, h, depth / 8, 1, c.Psb)[0];
                        }
                    }
                    c.P = start + len;
                }
            }
            return alpha;
        }

        static Raw ReadRecord(Cur c, List<string> notes)
        {
            var r = new Raw();
            r.Top = c.I32(); r.Left = c.I32(); r.Bottom = c.I32(); r.Right = c.I32();
            int n = c.U16();
            if (n > 56) throw new InvalidDataException(L.T("PSD 레이어 채널 수가 이상하다.", "Invalid PSD layer channel count."));
            for (int i = 0; i < n; i++) { r.ChId.Add(c.I16()); r.ChLen.Add(c.Len()); }
            if (c.Str4() != "8BIM") throw new InvalidDataException(L.T("PSD 레이어 레코드가 깨졌다.", "A PSD layer record is corrupted."));
            r.Blend = c.Str4();
            r.Opacity = c.U8();
            r.Clipping = c.U8() != 0;
            int flags = c.U8();
            r.Hidden = (flags & 2) != 0;
            c.Skip(1);
            long extraLen = c.U32();
            long extraEnd = c.P + extraLen;

            // 마스크 블록: 사각형·기본색·플래그 [+ 매개변수] [+ 벡터 마스크가 같이 있을 때의 '진짜' 사용자 마스크]
            long mLen = c.U32();
            long mEnd = c.P + mLen;
            if (mLen >= 18)
            {
                r.HasMask = true;
                r.MTop = c.I32(); r.MLeft = c.I32(); r.MBottom = c.I32(); r.MRight = c.I32();
                r.MDefault = c.U8();
                int mf = c.U8();
                r.MaskDisabled = (mf & 2) != 0;
                if ((mf & 16) != 0) Note(notes, L.T("마스크의 농도·페더 설정은 반영하지 않았다.", "Mask density and feather settings were not applied."));
                // 벡터 마스크와 사용자 마스크가 같이 있으면(-3 채널) '진짜' 마스크 정보가 플래그 바로 뒤에 온다.
                // 매개변수(농도·페더)는 그 다음인데 쓰지 않으니 건너뛰기만 한다.
                if (r.ChId.Contains(-3) && mEnd - c.P >= 18)
                {
                    c.Skip(1);                       // 진짜 마스크 플래그
                    r.RDefault = c.U8();
                    r.RTop = c.I32(); r.RLeft = c.I32(); r.RBottom = c.I32(); r.RRight = c.I32();
                    r.HasReal = true;
                }
            }
            c.P = mEnd;
            c.Skip(c.U32());                         // 블렌딩 범위 — 쓰지 않는다
            int nl = c.U8();
            if (c.P + nl > c.D.Length) throw new InvalidDataException(L.T("PSD 파일이 중간에 끊겼다.", "The PSD file is truncated."));
            r.Name = Encoding.Default.GetString(c.D, (int)c.P, nl);
            c.P += nl;
            c.Skip((4 - ((nl + 1) % 4)) % 4);

            while (c.P + 12 <= extraEnd)
            {
                if (!c.At("8BIM") && !c.At("8B64")) { c.P++; continue; }   // 짝수·4배수 채움을 건너뛴다
                string sig = c.Str4();
                string key = c.Str4();
                long len = (sig == "8B64" || (c.Psb && LongKeys.Contains(key))) ? c.U64() : c.U32();
                long start = c.P;
                if (start + len > extraEnd) break;
                r.Extra.Add(key);
                if (key == "luni" && len >= 4)
                {
                    long cnt = c.U32();
                    if (cnt > 0 && 4 + cnt * 2 <= len)
                    {
                        var sb = new StringBuilder();
                        for (long i = 0; i < cnt; i++)
                        {
                            char ch = (char)c.U16();
                            if (ch != '\0') sb.Append(ch);
                        }
                        r.Name = sb.ToString();
                    }
                }
                else if (key == "iOpa" && len >= 1) r.Fill = c.U8();
                else if ((key == "lsct" || key == "lsdk") && len >= 4)
                {
                    r.Section = (int)c.U32();
                    if (len >= 12 && c.Str4() == "8BIM") r.Blend = c.Str4();
                }
                c.P = start + len;
            }
            c.P = extraEnd;
            return r;
        }

        // 채널 데이터를 8비트 판(plane)들로 푼다. 레이어 채널은 판 1장, 합친 그림은 nch장이 이어 붙어 있다.
        static byte[][] Decode(byte[] d, long pos, long end, int comp, int w, int h, int bpc, int nch, bool psb)
        {
            int rowBytes = w * bpc;
            var outp = new byte[nch][];
            for (int k = 0; k < nch; k++) outp[k] = new byte[(long)w * h];
            var row = new byte[rowBytes];

            if (comp == 0)
            {
                long need = (long)rowBytes * h * nch;
                if (pos + need > end || end > d.Length) throw new InvalidDataException(L.T("PSD 채널 데이터가 모자란다.", "PSD channel data is incomplete."));
                for (int k = 0; k < nch; k++)
                    for (int y = 0; y < h; y++)
                    {
                        Array.Copy(d, pos + ((long)k * h + y) * rowBytes, row, 0, rowBytes);
                        Narrow(row, bpc, outp[k], (long)y * w, w);
                    }
            }
            else if (comp == 1)
            {
                int cb = psb ? 4 : 2;
                long rows = (long)h * nch;
                long p = pos + rows * cb;
                if (p > end) throw new InvalidDataException(L.T("PSD 채널 데이터가 모자란다.", "PSD channel data is incomplete."));
                for (long ri = 0; ri < rows; ri++)
                {
                    long q = pos + ri * cb;
                    long n = psb ? (((long)d[q] << 24) | ((long)d[q + 1] << 16) | ((long)d[q + 2] << 8) | d[q + 3])
                                 : ((d[q] << 8) | d[q + 1]);
                    long rowEnd = p + n;
                    if (rowEnd > end) throw new InvalidDataException(L.T("PSD 채널 데이터가 모자란다.", "PSD channel data is incomplete."));
                    Unpack(d, p, rowEnd, row);
                    Narrow(row, bpc, outp[ri / h], (ri % h) * w, w);
                    p = rowEnd;
                }
            }
            else if (comp == 2 || comp == 3)
            {
                if (end - pos < 2) throw new InvalidDataException(L.T("PSD 채널 데이터가 모자란다.", "PSD channel data is incomplete."));
                long total = (long)rowBytes * h * nch;
                var buf = new byte[total];
                // zlib 머리 2바이트를 떼면 DeflateStream이 읽을 수 있다
                using (var ms = new MemoryStream(d, (int)pos + 2, (int)(end - pos - 2)))
                using (var z = new DeflateStream(ms, CompressionMode.Decompress))
                {
                    long got = 0;
                    while (got < total)
                    {
                        int n = z.Read(buf, (int)got, (int)Math.Min(1 << 20, total - got));
                        if (n <= 0) break;
                        got += n;
                    }
                    if (got < total) throw new InvalidDataException(L.T("PSD ZIP 데이터가 모자란다.", "PSD ZIP data is incomplete."));
                }
                for (int k = 0; k < nch; k++)
                    for (int y = 0; y < h; y++)
                    {
                        Array.Copy(buf, ((long)k * h + y) * rowBytes, row, 0, rowBytes);
                        if (comp == 3)
                        {
                            // 예측: 행마다 앞 값과의 차이만 저장돼 있다
                            if (bpc == 1)
                                for (int x = 1; x < rowBytes; x++) row[x] = (byte)(row[x] + row[x - 1]);
                            else
                                for (int x = 1; x < w; x++)
                                {
                                    int v = ((row[x * 2] << 8) | row[x * 2 + 1]) + ((row[x * 2 - 2] << 8) | row[x * 2 - 1]);
                                    row[x * 2] = (byte)(v >> 8); row[x * 2 + 1] = (byte)v;
                                }
                        }
                        Narrow(row, bpc, outp[k], (long)y * w, w);
                    }
            }
            else throw new NotSupportedException(L.F("모르는 PSD 압축 방식이다({0}).", "Unknown PSD compression method ({0}).", comp));
            return outp;
        }

        // PackBits 한 행
        static void Unpack(byte[] d, long p, long end, byte[] row)
        {
            int n = row.Length, o = 0;
            while (o < n && p < end)
            {
                int hd = (sbyte)d[p++];
                if (hd >= 0)
                {
                    int cnt = hd + 1;
                    if (o + cnt > n || p + cnt > end) throw new InvalidDataException(L.T("PSD 압축 데이터가 깨졌다.", "PSD compressed data is corrupted."));
                    Array.Copy(d, p, row, o, cnt);
                    p += cnt; o += cnt;
                }
                else if (hd != -128)
                {
                    int cnt = 1 - hd;
                    if (o + cnt > n || p >= end) throw new InvalidDataException(L.T("PSD 압축 데이터가 깨졌다.", "PSD compressed data is corrupted."));
                    byte v = d[p++];
                    for (int i = 0; i < cnt; i++) row[o + i] = v;
                    o += cnt;
                }
            }
            for (; o < n; o++) row[o] = 0;           // 모자라면 0으로 메운다(일부 프로그램이 행 끝을 줄여 쓴다)
        }

        static void Narrow(byte[] row, int bpc, byte[] dst, long at, int w)
        {
            if (bpc == 1) { Array.Copy(row, 0, dst, at, w); return; }
            for (int x = 0; x < w; x++)
            {
                int v = (row[x * 2] << 8) | row[x * 2 + 1];
                dst[at + x] = (byte)((v + 128) / 257);
            }
        }

        static Canvas32 Compose(Raw r, int mode, int depth)
        {
            int w = r.W, h = r.H, n = w * h;
            var px = new Canvas32(w, h);
            byte[] a = Get(r, -1, n, 255);
            if (mode == 3)
            {
                byte[] R = Get(r, 0, n, 0), G = Get(r, 1, n, 0), B = Get(r, 2, n, 0);
                for (int i = 0; i < n; i++) px.P[i] = Canvas32.Pack(a[i], R[i], G[i], B[i]);
            }
            else if (mode == 4)
            {
                byte[] C = Get(r, 0, n, 255), M = Get(r, 1, n, 255), Y = Get(r, 2, n, 255), K = Get(r, 3, n, 255);
                for (int i = 0; i < n; i++) px.P[i] = Cmyk(a[i], C[i], M[i], Y[i], K[i]);
            }
            else
            {
                byte[] g = Get(r, 0, n, 0);
                for (int i = 0; i < n; i++) px.P[i] = Canvas32.Pack(a[i], g[i], g[i], g[i]);
            }
            return px;
        }

        // PSD의 CMYK는 거꾸로 저장된다(255 = 잉크 없음). 그래서 곱하기만 하면 된다.
        static int Cmyk(byte a, byte c, byte m, byte y, byte k)
        {
            return Canvas32.Pack(a, (byte)(c * k / 255), (byte)(m * k / 255), (byte)(y * k / 255));
        }

        static byte[] Get(Raw r, int id, int n, byte fill)
        {
            byte[] p;
            if (r.Planes.TryGetValue(id, out p) && p.Length >= n) return p;
            p = new byte[n];
            if (fill != 0) for (int i = 0; i < n; i++) p[i] = fill;
            return p;
        }

        static void Apply(Layer l, Raw r, List<string> notes)
        {
            l.Name = string.IsNullOrEmpty(r.Name) ? L.T("레이어", "Layer") : r.Name;
            l.X = r.Left; l.Y = r.Top;
            l.Visible = !r.Hidden;
            bool fx = r.Extra.Contains("lfx2") || r.Extra.Contains("lrFX") || r.Extra.Contains("lmfx");
            // 칠 불투명도는 효과를 뺀 레이어 몸통에만 걸린다. 효과는 어차피 안 가져오니 그냥 곱한다
            // (칠 0으로 효과만 보이게 한 레이어가 몸통째 드러나지 않게).
            double op = r.Opacity / 255.0 * (r.Fill / 255.0);
            l.Opacity = Math.Max(0, Math.Min(1, op));
            int bi = Array.IndexOf(Keys, r.Blend);
            if (bi >= 0) l.Blend = (BlendMode)bi;
            else
            {
                l.Blend = BlendMode.Normal;
                if (r.Blend != "pass") Note(notes, L.F("모르는 블렌드 모드({0})는 표준으로 바꿨다.", "Unknown blend mode ({0}) changed to Normal.", r.Blend.Trim()));
            }
            if (fx) Note(notes, L.T("레이어 효과(그림자·광선 등)는 가져오지 않았다.", "Layer effects (shadows, glows, etc.) were not imported."));
            if (r.Clipping) Note(notes, L.T("클리핑 마스크는 지원하지 않아 풀었다.", "Clipping masks aren't supported and were released."));
            if (r.Extra.Contains("TySh") || r.Extra.Contains("tySh")) Note(notes, L.T("글자 레이어는 그림으로 바꿨다. 글자를 고칠 수는 없다.", "Converted text layers to pixels. The text can't be edited."));
            if (r.Extra.Contains("SoLd") || r.Extra.Contains("SoLE") || r.Extra.Contains("PlLd")) Note(notes, L.T("스마트 오브젝트는 그림으로 바꿨다.", "Converted smart objects to pixels."));
            if (r.Extra.Contains("vmsk") || r.Extra.Contains("vsms"))
                Note(notes, L.T("벡터 마스크·셰이프는 포토샵이 그려 둔 그림으로 대신했다.", "Replaced vector masks and shapes with Photoshop's rendered pixels."));

            // 마스크: 레이어 크기로 맞춘다. 마스크 사각형 밖은 기본색으로 채운다.
            if (r.HasMask && r.MaskDisabled) { Note(notes, L.T("꺼 둔 레이어 마스크는 버렸다.", "Dropped disabled layer masks.")); return; }
            byte[] m = null;
            if (r.HasMask) m = Fit(r, r.MLeft, r.MTop, r.MRight, r.MBottom, r.MDefault, -2, null);
            if (r.HasReal && r.Planes.ContainsKey(-3)) m = Fit(r, r.RLeft, r.RTop, r.RRight, r.RBottom, r.RDefault, -3, m);
            if (m == null) return;
            bool all = true;
            for (int i = 0; i < m.Length; i++) if (m[i] != 255) { all = false; break; }
            if (all) return;                          // 다 보이는 마스크는 없는 것과 같다
            var dst = l.EditMask();
            Array.Copy(m, dst, m.Length);
        }

        // 마스크 판을 레이어 크기에 맞춰 펼치고, 이미 있는 마스크가 있으면 곱한다
        static byte[] Fit(Raw r, int ml, int mt, int mr, int mb, int def, int id, byte[] prev)
        {
            int w = r.W, h = r.H;
            byte[] plane;
            r.Planes.TryGetValue(id, out plane);
            int mw = mr - ml, mh = mb - mt;
            if (plane == null || plane.Length < (long)Math.Max(0, mw) * Math.Max(0, mh)) { plane = null; mw = 0; mh = 0; }
            var m = new byte[w * h];
            for (int y = 0; y < h; y++)
            {
                int dy = r.Top + y - mt;
                for (int x = 0; x < w; x++)
                {
                    int dx = r.Left + x - ml;
                    int v = (plane != null && dx >= 0 && dy >= 0 && dx < mw && dy < mh) ? plane[dy * mw + dx] : def;
                    int i = y * w + x;
                    m[i] = prev == null ? (byte)v : (byte)(prev[i] * v / 255);
                }
            }
            return m;
        }

        static Canvas32 ReadMerged(Cur c, int w, int h, int nch, int depth, int mode, byte[] palette, bool alphaFirst, List<string> notes)
        {
            int color = mode == 3 ? 3 : mode == 4 ? 4 : 1;
            if (nch < color) throw new InvalidDataException(L.T("PSD 채널 수가 모자란다.", "The PSD has too few channels."));
            int comp = c.U16();
            int use = Math.Min(nch, color + 1);
            // 압축이 행 단위라 앞 채널만 골라 읽을 수 없다. 전부 풀고 필요한 것만 쓴다.
            var planes = Decode(c.D, c.P, c.D.Length, comp, w, h, depth / 8, comp == 1 ? nch : use, c.Psb);
            int n = w * h;
            byte[] a = null;
            if (nch > color && alphaFirst) a = planes[color];
            else if (nch > color) Note(notes, L.T("알파 채널(저장된 선택 영역)은 버렸다.", "Dropped alpha channels (saved selections)."));
            var px = new Canvas32(w, h);
            for (int i = 0; i < n; i++)
            {
                byte al = a == null ? (byte)255 : a[i];
                int v;
                if (mode == 3) v = Canvas32.Pack(al, planes[0][i], planes[1][i], planes[2][i]);
                else if (mode == 4) v = Cmyk(al, planes[0][i], planes[1][i], planes[2][i], planes[3][i]);
                else if (mode == 2 && palette != null)
                {
                    int k = planes[0][i];
                    v = Canvas32.Pack(al, palette[k], palette[256 + k], palette[512 + k]);
                }
                else v = Canvas32.Pack(al, planes[0][i], planes[0][i], planes[0][i]);
                if (a != null) v = Unmatte(v);
                px.P[i] = v;
            }
            if (mode == 2) Note(notes, L.T("인덱스 색 그림을 RGB로 바꿨다.", "Converted the indexed-color image to RGB."));
            return px;
        }

        // 포토샵은 합친 그림의 색을 흰 바탕에 섞어 둔다. 알파로 그걸 되돌린다.
        static int Unmatte(int v)
        {
            int a = Canvas32.A(v);
            if (a == 0) return 0;
            if (a == 255) return v;
            return Canvas32.Pack((byte)a, Um(Canvas32.R(v), a), Um(Canvas32.G(v), a), Um(Canvas32.B(v), a));
        }
        static byte Um(int c, int a)
        {
            int v = ((c - (255 - a)) * 255 + a / 2) / a;
            return (byte)(v < 0 ? 0 : v > 255 ? 255 : v);
        }

        // ================= 쓰기 =================

        public static void Write(Doc doc, string path, List<string> notes)
        {
            if (doc.W > 30000 || doc.H > 30000)
                throw new NotSupportedException(L.T("PSD는 한 변이 30000px까지다. 그림을 줄여서 저장해 달라.", "PSD sides are limited to 30000px. Resize the image and save again."));

            var layers = new List<Layer>();
            int adjCount = 0;
            foreach (var l in doc.Layers)
            {
                if (l.Adjust != null) { adjCount++; continue; }   // 조정 레이어는 PSD 조정 레이어로 옮기지 못한다
                if (l.W > 0 && l.H > 0) layers.Add(l);
            }
            if (adjCount > 0) Note(notes, L.F("조정 레이어 {0}개는 넣지 않았다(합친 그림에는 들어 있다).", "Left out {0} adjustment layers (they are included in the merged image).", adjCount));
            if (doc.Groups.Count > 0) Note(notes, L.T("그룹은 풀어서 저장했다(그룹의 숨김·불투명도는 각 레이어에 반영).", "Ungrouped layers when saving (group visibility and opacity applied to each layer)."));
            if (layers.Count == 0) throw new InvalidOperationException(L.T("저장할 레이어가 없다.", "There are no layers to save."));

            int fxCount = 0;
            foreach (var l in layers) if (l.Fx != null && l.Fx.Any) fxCount++;
            if (fxCount > 0) Note(notes, L.F("레이어 효과는 PSD에 넣지 않았다({0}개 레이어). 효과까지 남기려면 레이어를 합친 뒤 저장해 달라.", "Layer effects were not saved to the PSD ({0} layers). To keep them, merge the layers before saving.", fxCount));

            // 채널마다 미리 압축해 둔다. 레코드에 길이를 먼저 써야 하기 때문이다.
            var chans = new List<byte[][]>();
            foreach (var l in layers)
            {
                var px = l.PixelsRead;
                int w = px.W, h = px.H, n = w * h;
                var pa = new byte[n]; var pr = new byte[n]; var pg = new byte[n]; var pb = new byte[n];
                for (int i = 0; i < n; i++)
                {
                    int v = px.P[i];
                    pa[i] = Canvas32.A(v); pr[i] = Canvas32.R(v); pg[i] = Canvas32.G(v); pb[i] = Canvas32.B(v);
                }
                var list = new List<byte[]>();
                list.Add(PackChannel(pa, w, h)); list.Add(PackChannel(pr, w, h));
                list.Add(PackChannel(pg, w, h)); list.Add(PackChannel(pb, w, h));
                if (l.MaskRead != null) list.Add(PackChannel(l.MaskRead, w, h));
                chans.Add(list.ToArray());
            }

            var info = new MemoryStream();
            W16(info, -layers.Count);                // 음수 = 합친 그림의 첫 알파 채널이 투명도
            for (int li = 0; li < layers.Count; li++)
            {
                var l = layers[li];
                var cs = chans[li];
                int w = l.W, h = l.H;
                W32(info, l.Y); W32(info, l.X); W32(info, l.Y + h); W32(info, l.X + w);
                W16(info, cs.Length);
                int[] ids = { -1, 0, 1, 2, -2 };
                for (int k = 0; k < cs.Length; k++) { W16(info, ids[k]); W32(info, cs[k].Length); }
                Str(info, "8BIM");
                Str(info, Keys[(int)l.Blend]);
                var grp = doc.Group(l.GroupId);
                double op = l.Opacity * (grp != null ? grp.Opacity : 1);
                bool vis = l.Visible && (grp == null || grp.Visible);
                info.WriteByte((byte)Math.Round(Math.Max(0, Math.Min(1, op)) * 255));
                info.WriteByte(0);                   // 클리핑 없음
                info.WriteByte((byte)(8 | (vis ? 0 : 2)));   // 2 = 숨김, 8 = 포토샵 5 이후 형식
                info.WriteByte(0);

                var ex = new MemoryStream();
                if (l.MaskRead != null)
                {
                    W32(ex, 20);
                    W32(ex, l.Y); W32(ex, l.X); W32(ex, l.Y + h); W32(ex, l.X + w);
                    ex.WriteByte(255);               // 사각형 밖 기본색
                    ex.WriteByte(0);
                    ex.WriteByte(0); ex.WriteByte(0);
                }
                else W32(ex, 0);
                // 블렌딩 범위: 회색 + 채널 넷, 전부 '다 통과'
                W32(ex, 40);
                for (int k = 0; k < 10; k++) { ex.WriteByte(0); ex.WriteByte(0); ex.WriteByte(255); ex.WriteByte(255); }
                // 파스칼 이름은 ASCII만. 한글은 아래 luni로 살린다.
                string pn = l.Name ?? "";
                bool ascii = pn.Length <= 255;
                foreach (char chh in pn) if (chh < 32 || chh > 126) { ascii = false; break; }
                if (!ascii) pn = "Layer " + (li + 1);
                var nb = Encoding.ASCII.GetBytes(pn);
                ex.WriteByte((byte)nb.Length);
                ex.Write(nb, 0, nb.Length);
                for (int k = (nb.Length + 1) % 4; k != 0 && k < 4; k++) ex.WriteByte(0);
                // luni: 글자 수(4바이트) + UTF-16BE. 4바이트 배수로 채운다
                string un = l.Name ?? "";
                int ul = 4 + un.Length * 2;
                int upad = (4 - ul % 4) % 4;
                Str(ex, "8BIM"); Str(ex, "luni"); W32(ex, ul + upad);
                W32(ex, un.Length);
                foreach (char chh in un) W16(ex, chh);
                for (int k = 0; k < upad; k++) ex.WriteByte(0);

                W32(info, (int)ex.Length);
                ex.WriteTo(info);
            }
            foreach (var cs in chans) foreach (var b in cs) info.Write(b, 0, b.Length);
            while (info.Length % 4 != 0) info.WriteByte(0);   // 스펙은 짝수, 포토샵은 4배수로 맞춘다

            // 합친 그림: 흰 바탕에 섞은 색 + 알파 (포토샵과 같은 방식이라 레이어를 못 읽는 뷰어도 제대로 보인다)
            var flat = doc.Flatten();
            int fw = flat.W, fh = flat.H, fn = fw * fh;
            var mp = new byte[4][];
            for (int k = 0; k < 4; k++) mp[k] = new byte[fn];
            for (int i = 0; i < fn; i++)
            {
                int v = flat.P[i];
                int a = Canvas32.A(v), wa = 255 - a;
                mp[0][i] = (byte)((Canvas32.R(v) * a + 255 * wa + 127) / 255);
                mp[1][i] = (byte)((Canvas32.G(v) * a + 255 * wa + 127) / 255);
                mp[2][i] = (byte)((Canvas32.B(v) * a + 255 * wa + 127) / 255);
                mp[3][i] = (byte)a;
            }

            string tmp = path + ".tmp";
            using (var f = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
            {
                Str(f, "8BPS"); W16(f, 1);
                for (int k = 0; k < 6; k++) f.WriteByte(0);
                W16(f, 4); W32(f, doc.H); W32(f, doc.W); W16(f, 8); W16(f, 3);
                W32(f, 0);                           // 색 모드 데이터 없음
                W32(f, 0);                           // 이미지 리소스 없음
                W32(f, (int)(4 + info.Length + 4));  // 레이어·마스크 정보 전체
                W32(f, (int)info.Length);
                info.WriteTo(f);
                W32(f, 0);                           // 전역 마스크 없음

                W16(f, 1);                           // PackBits. 행 길이 표가 채널 넷 전부 앞에 모인다
                var rows = new MemoryStream();
                var lens = new int[4 * fh];
                for (int k = 0; k < 4; k++)
                    for (int y = 0; y < fh; y++)
                    {
                        long s = rows.Length;
                        PackRow(mp[k], y * fw, fw, rows);
                        lens[k * fh + y] = (int)(rows.Length - s);
                    }
                foreach (int n in lens) W16(f, n);
                rows.WriteTo(f);
            }
            // 쓰다 실패하면 원래 파일은 그대로 남는다
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        // 레이어 채널 하나: 압축 방식(1) + 행 길이 표 + PackBits 행들
        static byte[] PackChannel(byte[] p, int w, int h)
        {
            var rows = new MemoryStream();
            var lens = new int[h];
            for (int y = 0; y < h; y++)
            {
                long s = rows.Length;
                PackRow(p, y * w, w, rows);
                lens[y] = (int)(rows.Length - s);
            }
            var o = new MemoryStream((int)rows.Length + 2 + h * 2);
            W16(o, 1);
            foreach (int n in lens) W16(o, n);
            rows.WriteTo(o);
            return o.ToArray();
        }

        // PackBits: 같은 값 3개 이상은 반복으로, 나머지는 128개씩 그대로
        static void PackRow(byte[] s, int off, int n, MemoryStream o)
        {
            int i = 0;
            while (i < n)
            {
                int run = 1;
                while (i + run < n && run < 128 && s[off + i + run] == s[off + i]) run++;
                if (run >= 3)
                {
                    o.WriteByte((byte)(1 - run));
                    o.WriteByte(s[off + i]);
                    i += run;
                    continue;
                }
                int start = i;
                while (i < n && i - start < 128)
                {
                    if (i + 2 < n && s[off + i] == s[off + i + 1] && s[off + i] == s[off + i + 2]) break;
                    i++;
                }
                o.WriteByte((byte)(i - start - 1));
                o.Write(s, off + start, i - start);
            }
        }

        static void W16(Stream s, int v) { s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)v); }
        static void W32(Stream s, int v) { s.WriteByte((byte)(v >> 24)); s.WriteByte((byte)(v >> 16)); s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)v); }
        static void Str(Stream s, string t) { for (int i = 0; i < t.Length; i++) s.WriteByte((byte)t[i]); }
    }
}
