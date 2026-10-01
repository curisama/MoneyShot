// Money Shot — 자동 스크롤 캡처
//
// 화면에 보이는 만큼만 찍고, 휠을 굴려 다시 찍고, 겹치는 부분을 찾아 이어붙인다.
//
// 까다로운 지점은 "얼마나 스크롤됐는지 화면만 봐서는 모른다"는 것이다. 굴린 칸 수는
// 앱마다 픽셀 환산이 다르고, 관성 스크롤이면 더 어긋난다. 그래서 굴린 양을 믿지 않고
// 두 장을 직접 대조해 겹침을 찾는다.
//
// 두 번째 함정은 고정 요소다. 상단 네비게이션 바나 하단 툴바처럼 스크롤해도 그대로인
// 띠가 있으면 매 장에 그게 반복돼 결과물이 엉망이 된다. 그래서 매번 위아래로 얼마만큼이
// 얼어붙어 있는지 먼저 재고, 그 띠를 뺀 가운데 영역만 이어붙인 뒤 머리와 꼬리는 한 번씩만 붙인다.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using D = System.Drawing;

namespace MoneyShot
{
    public static class ScrollCapture
    {
        static bool running;
        static bool detecting;

        public static void Begin(Action<CaptureResult> done)
        {
            if (running || detecting) return;
            detecting = true;
            var ui = Application.Current.Dispatcher;

            // 인식은 실제로 화면을 굴려보는 일이라 잠깐 걸린다. UI를 붙잡아 두지 않는다.
            var probe = new Thread(delegate ()
            {
                RECT? found = null;
                try { found = Detect(); }
                catch (Exception ex) { Log.W("스크롤 영역 자동 인식 실패: " + ex.Message); }

                ui.BeginInvoke(new Action(delegate
                {
                    detecting = false;
                    RegionOverlay.Begin(delegate(CaptureResult pick)
                    {
                        if (pick == null) return;               // 취소
                        Run(pick.Bounds, done);
                    }, true, found);
                }));
            });
            probe.IsBackground = true;
            probe.SetApartmentState(ApartmentState.STA);
            probe.Start();
        }

        // ---------- 스크롤되는 영역 자동 인식 ----------
        //
        // 커서 아래 창을 한 번 굴려보고, 굴리기 전후로 달라진 자리를 찾는다.
        // 그 자리가 곧 스크롤되는 영역이다. 굴린 만큼 되돌려 놓으므로 문서 위치는 그대로다.
        //
        // UIA로 스크롤 가능한 컨트롤을 물어보는 방식과 달리, 창을 무엇으로 그렸는지와 무관하다.
        static RECT? Detect()
        {
            var win = Cap.WindowUnderCursor();
            if (win.W < 240 || win.H < 240) return null;

            var center = new POINT { X = win.Left + win.W / 2, Y = win.Top + win.H / 2 };
            IntPtr h = Native.WindowFromPoint(center);
            if (h != IntPtr.Zero)
            {
                IntPtr root = Native.GetAncestor(h, Native.GA_ROOT);
                if (root != IntPtr.Zero) Native.SetForegroundWindow(root);
            }

            POINT saved; Native.GetCursorPos(out saved);
            Native.SetCursorPos(center.X, center.Y);
            Thread.Sleep(150);

            int delay = Math.Max(220, Settings.Current.scrollDelayMs);
            D.Rectangle changed;
            using (var before = Cap.Screen(win, false))
            {
                Native.ScrollWheel(-2);
                Thread.Sleep(delay);
                using (var after = Cap.Screen(win, false))
                {
                    Native.ScrollWheel(2);          // 굴린 만큼 되돌린다
                    Thread.Sleep(delay);
                    Native.SetCursorPos(saved.X, saved.Y);
                    changed = ChangedRegion(before, after);
                }
            }

            // 너무 작거나 창에 비해 너무 좁으면 스크롤 영역이라 보기 어렵다.
            if (changed.Width < 160 || changed.Height < 160) return null;
            if ((long)changed.Width * changed.Height * 100 < (long)win.W * win.H * 12) return null;

            Log.W("스크롤 영역 자동 인식: 창 " + win.W + "x" + win.H +
                  " 안에서 " + changed.Width + "x" + changed.Height +
                  " (" + changed.X + "," + changed.Y + ")");

            return new RECT
            {
                Left = win.Left + changed.Left,
                Top = win.Top + changed.Top,
                Right = win.Left + changed.Right,
                Bottom = win.Top + changed.Bottom
            };
        }

        // 두 장에서 달라진 부분을 감싸는 사각형.
        // 행·열별로 "얼마나 달라졌는지"를 세고, 충분히 달라진 구간 중 가장 긴 것을 고른다.
        // 깜빡이는 커서나 시계처럼 작게 변하는 것은 비율 문턱을 못 넘어 걸러진다.
        static D.Rectangle ChangedRegion(D.Bitmap beforeBmp, D.Bitmap afterBmp)
        {
            const int Step = 2;                     // 두 픽셀 건너뛰며 본다
            const int Tol = 26;                     // 이만큼 넘게 다르면 '달라졌다'

            var a = Canvas32.From(beforeBmp);
            var b = Canvas32.From(afterBmp);
            int sw = a.W / Step, sh = a.H / Step;
            if (sw < 8 || sh < 8) return D.Rectangle.Empty;

            var diff = new bool[sw * sh];
            for (int y = 0; y < sh; y++)
            {
                int row = y * sw;
                for (int x = 0; x < sw; x++)
                    if (Ops.Dist(a.At(x * Step, y * Step), b.At(x * Step, y * Step)) > Tol)
                        diff[row + x] = true;
            }

            var rowHit = new bool[sh];
            for (int y = 0; y < sh; y++)
            {
                int n = 0, row = y * sw;
                for (int x = 0; x < sw; x++) if (diff[row + x]) n++;
                rowHit[y] = n * 100 > sw * 10;
            }
            int top, bottom;
            if (!LongestRun(rowHit, out top, out bottom)) return D.Rectangle.Empty;

            var colHit = new bool[sw];
            int band = bottom - top + 1;
            for (int x = 0; x < sw; x++)
            {
                int n = 0;
                for (int y = top; y <= bottom; y++) if (diff[y * sw + x]) n++;
                colHit[x] = n * 100 > band * 10;
            }
            int left, right;
            if (!LongestRun(colHit, out left, out right)) return D.Rectangle.Empty;

            return D.Rectangle.FromLTRB(left * Step, top * Step,
                                        (right + 1) * Step, (bottom + 1) * Step);
        }

        // 참이 이어지는 가장 긴 구간. 중간에 잠깐 끊기는 건 이어진 것으로 본다
        // (문단 사이 여백처럼 스크롤해도 안 변하는 줄이 섞이기 때문).
        static bool LongestRun(bool[] hit, out int from, out int to)
        {
            int gapAllow = Math.Max(4, hit.Length / 12);
            from = to = -1;
            int bestLen = 0, curStart = -1, curEnd = -1, gap = 0;

            for (int i = 0; i < hit.Length; i++)
            {
                if (hit[i])
                {
                    if (curStart < 0) curStart = i;
                    curEnd = i;
                    gap = 0;
                }
                else if (curStart >= 0)
                {
                    gap++;
                    if (gap > gapAllow)
                    {
                        if (curEnd - curStart + 1 > bestLen)
                        { bestLen = curEnd - curStart + 1; from = curStart; to = curEnd; }
                        curStart = -1; gap = 0;
                    }
                }
            }
            if (curStart >= 0 && curEnd - curStart + 1 > bestLen)
            { bestLen = curEnd - curStart + 1; from = curStart; to = curEnd; }
            return bestLen > 0;
        }

        static void Run(RECT area, Action<CaptureResult> done)
        {
            running = true;
            var s = Settings.Current;
            var ui = Application.Current.Dispatcher;
            var hud = new ScrollHud(area);
            hud.Show();

            var worker = new Thread(delegate ()
            {
                D.Bitmap result = null;
                string note = null;
                try { result = Loop(area, s, hud, out note); }
                catch (Exception ex) { note = ex.Message; }

                ui.BeginInvoke(new Action(delegate
                {
                    hud.Close();
                    running = false;
                    if (result == null)
                    {
                        if (note != null) App.Notify(L.T("스크롤 캡처를 못 했다", "Scrolling capture failed"), note);
                        return;
                    }
                    if (note != null) App.Notify(L.T("스크롤 캡처 완료", "Scrolling capture done"), note);
                    done(new CaptureResult { Image = result, Bounds = area, Action = PostAction.Copy });
                }));
            });
            worker.SetApartmentState(ApartmentState.STA);
            worker.IsBackground = true;
            worker.Start();
        }

        // 한 칸 굴리고, 화면이 실제로 움직일 때까지 기다린다.
        //
        // 부드러운 스크롤을 쓰는 앱(크롬 등)은 휠을 굴린 직후엔 아직 화면이 그대로다.
        // 한 번 찍어보고 판단하면 바닥에 닿은 것으로 오해해 중간에 멈춘다.
        // 그래서 몇 번 더 기다려 보고, 그래도 그대로면 한 번 더 굴려본 다음에야 바닥으로 친다.
        static bool Advance(RECT area, int clicks, int delay, byte[] prevRows,
                            int top, int len, out D.Bitmap next, out byte[] nextRows)
        {
            next = null; nextRows = null;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                // 첫 시도에 굴리고, 네 번 기다려도 안 움직이면 휠이 먹지 않은 걸로 보고 한 번 더.
                if (attempt == 0 || attempt == 4) Native.ScrollWheel(-clicks);
                Thread.Sleep(delay + attempt * 140);
                if (Escaped()) return false;

                var shot = Cap.Screen(area, false);
                var rows = Sig(shot);
                if (!SameRange(prevRows, rows, top, len))
                {
                    next = shot; nextRows = rows;
                    return true;
                }
                shot.Dispose();
            }
            return false;
        }

        static D.Bitmap Loop(RECT area, Settings s, ScrollHud hud, out string note)
        {
            note = null;
            int W = area.W, H = area.H;
            if (W < 8 || H < 32) { note = L.T("영역이 너무 작다.", "The area is too small."); return null; }

            // 대상 창을 앞으로 꺼내고 커서를 영역 가운데에 둔다. 휠은 커서 아래 창으로 간다.
            var center = new POINT { X = area.Left + W / 2, Y = area.Top + H / 2 };
            IntPtr target = Native.WindowFromPoint(center);
            if (target != IntPtr.Zero)
            {
                IntPtr root = Native.GetAncestor(target, Native.GA_ROOT);
                if (root != IntPtr.Zero) Native.SetForegroundWindow(root);
            }
            POINT saved; Native.GetCursorPos(out saved);
            Native.SetCursorPos(center.X, center.Y);
            Thread.Sleep(200);

            int clicks = Math.Max(1, s.scrollClicks);
            int delay = Math.Max(140, s.scrollDelayMs);

            // 고정 띠를 재려면 스크롤 전후 두 장이 필요하다.
            var first = Cap.Screen(area, false);
            var firstRows = Sig(first);

            D.Bitmap second; byte[] secondRows;
            if (!Advance(area, clicks, delay, firstRows, 0, H, out second, out secondRows))
            {
                first.Dispose();
                Native.SetCursorPos(saved.X, saved.Y);
                note = L.T("화면이 움직이지 않는다. 스크롤되는 안쪽을 지정했는지 확인해라.", "Nothing moved. Make sure you selected the scrolling area inside the window.");
                return null;
            }

            int frozenTop = FrozenTop(firstRows, secondRows, H);
            int frozenBottom = FrozenBottom(firstRows, secondRows, H, frozenTop);
            int midTop = frozenTop, midH = H - frozenTop - frozenBottom;
            if (midH < 16)
            {
                first.Dispose(); second.Dispose();
                Native.SetCursorPos(saved.X, saved.Y);
                note = L.T("움직이는 부분을 못 찾았다. 스크롤 막대가 있는 안쪽을 지정해라.", "Couldn't find the moving part. Select the area that has the scroll bar.");
                return null;
            }

            // 첫 장은 머리 고정 띠까지 통째로 넣는다. 이후로는 새로 드러난 부분만 붙인다.
            var parts = new List<D.Bitmap>();
            parts.Add(Cap.Crop(first, new D.Rectangle(0, 0, W, frozenTop + midH)));
            int totalH = frozenTop + midH;
            first.Dispose();

            var prevRows = firstRows;       // 직전 장
            var last = second;              // 최신 장 (여기서만 잘라낸다)
            var lastRows = secondRows;
            int pages = 1, misses = 0;
            bool calibrated = false;
            bool atBottom = false;

            while (true)
            {
                int shift = FindShift(prevRows, lastRows, midTop, midH);
                if (shift > 0)
                {
                    parts.Add(Cap.Crop(last, new D.Rectangle(0, midTop + midH - shift, W, shift)));
                    totalH += shift;
                    pages++;
                    misses = 0;
                    hud.Report(pages, totalH);

                    // 휠 한 칸이 몇 픽셀인지 이제 안다. 화면의 60%씩 내려가도록 맞춘다.
                    // 너무 조금 굴리면 장수가 폭증하고, 너무 많이 굴리면 겹치는 부분이 사라진다.
                    if (!calibrated)
                    {
                        calibrated = true;
                        double perClick = (double)shift / clicks;
                        if (perClick >= 1)
                        {
                            int want = (int)Math.Round(midH * 0.6 / perClick);
                            int tuned = Math.Max(1, Math.Min(12, want));
                            if (tuned != clicks)
                                Log.W("스크롤 보폭 조정: " + clicks + "칸 → " + tuned + "칸 (한 칸 " +
                                      Math.Round(perClick) + "px, 화면 " + midH + "px)");
                            clicks = tuned;
                        }
                    }
                }
                else
                {
                    // 겹침을 못 찾았다 — 한 번에 너무 많이 굴렸거나 화면이 통째로 바뀌었다.
                    misses++;
                    if (misses == 1 && clicks > 1)
                    {
                        clicks = Math.Max(1, clicks / 2);   // 보폭을 줄여 한 번 더 해본다
                        Log.W("겹침을 놓쳐 보폭을 " + clicks + "칸으로 줄인다");
                    }
                    else if (misses >= 2)
                    {
                        note = L.T("겹치는 부분을 못 찾아 멈췄다. 설정에서 굴린 뒤 기다리는 시간을 늘려봐라.", "Stopped: couldn't find the overlap. Try a longer wait after scrolling in Settings.");
                        break;
                    }
                }

                if (Escaped()) { note = L.T("Esc로 멈췄다. 여기까지만 이어붙였다.", "Stopped with Esc. Stitched what was captured so far."); break; }
                if (pages >= s.scrollMaxPages) { note = L.F("안전 상한 {0}장에서 멈췄다.", "Stopped at the safety limit of {0} pages.", s.scrollMaxPages); break; }
                if (totalH > 40000) { note = L.T("높이 상한 40000px에서 멈췄다.", "Stopped at the 40000px height limit."); break; }

                prevRows = lastRows;
                D.Bitmap nxt; byte[] nxtRows;
                if (!Advance(area, clicks, delay, prevRows, midTop, midH, out nxt, out nxtRows))
                {
                    atBottom = true;
                    break;
                }
                last.Dispose();
                last = nxt; lastRows = nxtRows;
            }

            // 꼬리 고정 띠는 마지막 장에서 한 번만 붙인다.
            if (frozenBottom > 0)
            {
                parts.Add(Cap.Crop(last, new D.Rectangle(0, H - frozenBottom, W, frozenBottom)));
                totalH += frozenBottom;
            }

            var stitched = Stitch(parts, W, totalH);
            foreach (var p in parts) p.Dispose();
            last.Dispose();
            Native.SetCursorPos(saved.X, saved.Y);

            Log.W("스크롤 캡처 끝: " + pages + "장, " + W + "x" + totalH +
                  (atBottom ? ", 바닥까지" : ", 도중 종료") +
                  (note != null ? " — " + note : ""));

            if (atBottom && pages == 1)
                note = L.T("스크롤되지 않아 보이는 만큼만 담았다.", "The page didn't scroll, so only the visible part was captured.");
            else if (note == null && (frozenTop > 0 || frozenBottom > 0))
                note = L.F("고정 영역을 걸러냈다 (위 {0}px, 아래 {1}px).", "Removed fixed areas ({0}px top, {1}px bottom).", frozenTop, frozenBottom);
            return stitched;
        }

        static bool Escaped()
        {
            return (Native.GetAsyncKeyState(Native.VK_ESCAPE) & 0x8000) != 0;
        }

        static D.Bitmap Stitch(List<D.Bitmap> parts, int w, int h)
        {
            var outBmp = new D.Bitmap(w, Math.Max(1, h), D.Imaging.PixelFormat.Format32bppArgb);
            using (var g = D.Graphics.FromImage(outBmp))
            {
                g.CompositingMode = D.Drawing2D.CompositingMode.SourceCopy;
                int y = 0;
                foreach (var p in parts)
                {
                    g.DrawImage(p, new D.Rectangle(0, y, p.Width, p.Height),
                                new D.Rectangle(0, 0, p.Width, p.Height), D.GraphicsUnit.Pixel);
                    y += p.Height;
                }
            }
            return outBmp;
        }

        // ---------- 대조 ----------
        //
        // 줄 하나를 24칸 평균색(RGB)으로 줄인 짧은 지문으로 다룬다.
        // 정확히 같은 값을 요구하면 안 된다 — 화면 배율이 100%가 아니면 스크롤 위치에 따라
        // 글자가 미세하게 다르게 그려져서, 같은 내용인데도 픽셀 값이 달라진다.
        const int Buckets = 24;
        const int SigLen = Buckets * 3;

        // 줄별 지문. 길이 = 높이 * SigLen
        static byte[] Sig(D.Bitmap b)
        {
            int w = b.Width, h = b.Height;
            var data = b.LockBits(new D.Rectangle(0, 0, w, h),
                                  D.Imaging.ImageLockMode.ReadOnly, D.Imaging.PixelFormat.Format32bppArgb);
            var sig = new byte[h * SigLen];
            try
            {
                var row = new int[w];
                for (int y = 0; y < h; y++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, w);
                    int at = y * SigLen;
                    for (int k = 0; k < Buckets; k++)
                    {
                        int x0 = (int)((long)w * k / Buckets);
                        int x1 = (int)((long)w * (k + 1) / Buckets);
                        if (x1 <= x0) x1 = x0 + 1;
                        if (x1 > w) x1 = w;
                        long r = 0, g = 0, bb = 0;
                        int n = x1 - x0;
                        for (int x = x0; x < x1; x++)
                        {
                            int v = row[x];
                            r += (byte)(v >> 16); g += (byte)(v >> 8); bb += (byte)v;
                        }
                        sig[at + k * 3] = (byte)(r / n);
                        sig[at + k * 3 + 1] = (byte)(g / n);
                        sig[at + k * 3 + 2] = (byte)(bb / n);
                    }
                }
            }
            finally { b.UnlockBits(data); }
            return sig;
        }

        // 두 줄의 평균 차이. 0이면 완전히 같다.
        static int RowDiff(byte[] a, int ay, byte[] b, int by)
        {
            int ai = ay * SigLen, bi = by * SigLen, sum = 0;
            for (int i = 0; i < SigLen; i++)
            {
                int d = a[ai + i] - b[bi + i];
                sum += d < 0 ? -d : d;
            }
            return sum / SigLen;
        }

        // 그 줄에 볼 만한 내용이 있는가. 여백만 있는 줄은 어디에나 맞아버려 기준이 못 된다.
        static bool HasContent(byte[] sig, int y)
        {
            int at = y * SigLen, lo = 255, hi = 0;
            for (int i = 0; i < SigLen; i++)
            {
                int v = sig[at + i];
                if (v < lo) lo = v;
                if (v > hi) hi = v;
            }
            return hi - lo > 18;
        }

        const int SameTol = 7;       // 이 정도 차이는 같은 줄로 본다
        const int MatchTol = 9;      // 본보기 띠가 이 정도로 맞으면 겹친 자리로 인정한다

        // 구간이 그대로인가 (화면이 안 움직였는가).
        static bool SameRange(byte[] a, byte[] b, int top, int len)
        {
            int moved = 0;
            for (int i = 0; i < len; i++)
                if (RowDiff(a, top + i, b, top + i) > SameTol) moved++;
            return moved * 100 <= len * 2;      // 2% 미만만 달라졌으면 안 움직인 것
        }

        static int FrozenTop(byte[] a, byte[] b, int h)
        {
            int limit = h * 2 / 5, n = 0;
            while (n < limit && RowDiff(a, n, b, n) <= SameTol) n++;
            return n >= limit ? 0 : n;          // 상한까지 갔다면 고정 띠가 아니라 단색 화면으로 본다
        }

        static int FrozenBottom(byte[] a, byte[] b, int h, int top)
        {
            int limit = h * 2 / 5, n = 0;
            while (n < limit && h - 1 - n > top && RowDiff(a, h - 1 - n, b, h - 1 - n) <= SameTol) n++;
            return n >= limit ? 0 : n;
        }

        // prev가 cur보다 shift 픽셀만큼 위에 있던 관계를 찾는다 (cur[y] == prev[y + shift]).
        //
        // 예전에는 겹치는 구간의 모든 줄이 맞아떨어지길 요구했는데, 한 줄만 어긋나도 실패였다.
        // 지금은 직전 장의 아래쪽 띠를 본보기로 삼아 새 장의 어디에 놓이는지를 점수로 재고,
        // 가장 잘 맞는 자리를 고른다.
        static int FindShift(byte[] prev, byte[] cur, int top, int len)
        {
            int band = Math.Max(24, Math.Min(200, len / 4));

            // 본보기 띠는 내용이 있는 곳으로 잡는다. 아래쪽이 여백이면 위로 올라가며 찾는다.
            int bandStart = top + len - band;
            for (int tryUp = 0; tryUp < 6; tryUp++)
            {
                int active = 0;
                for (int i = 0; i < band; i++) if (HasContent(prev, bandStart + i)) active++;
                if (active * 100 > band * 15) break;
                int next = bandStart - band / 2;
                if (next < top) break;
                bandStart = next;
            }

            int maxShift = bandStart - top;                 // 본보기가 새 장 안에 들어와야 한다
            if (maxShift < 1) return 0;

            int bestShift = 0, bestScore = int.MaxValue;
            for (int shift = 1; shift <= maxShift; shift++)
            {
                int sum = 0, checkedRows = 0;
                // 성긴 검사로 먼저 거른다. 대부분은 몇 줄 만에 탈락한다.
                for (int i = 0; i < band; i += 5)
                {
                    sum += RowDiff(prev, bandStart + i, cur, bandStart - shift + i);
                    checkedRows++;
                    if (sum > MatchTol * checkedRows * 3) break;
                }
                int coarse = sum / Math.Max(1, checkedRows);
                if (coarse > MatchTol * 2) continue;

                sum = 0;
                for (int i = 0; i < band; i++)
                    sum += RowDiff(prev, bandStart + i, cur, bandStart - shift + i);
                int score = sum / band;
                if (score < bestScore) { bestScore = score; bestShift = shift; }
                if (score == 0) break;                      // 더 볼 것 없다
            }

            // 본보기를 어디서 떴든 관계식(cur[y] == prev[y + shift])은 그대로라 보정할 것이 없다.
            return bestScore > MatchTol ? 0 : bestShift;
        }
    }

    // 진행 상황 표시. 캡처 영역 바깥에 놓아야 결과물에 섞이지 않는다.
    public class ScrollHud : Window
    {
        TextBlock line;
        readonly RECT area;

        public ScrollHud(RECT area)
        {
            this.area = area;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = false;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            Background = Theme.BrGlassFallback;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -4000; Top = -4000;
            FontFamily = Theme.UI;

            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(15, 11, 16, 12) };
            var dot = new System.Windows.Shapes.Ellipse
            {
                Width = 8, Height = 8, Fill = Theme.BrAccent,
                VerticalAlignment = VerticalAlignment.Center
            };
            var pulse = Theme.A(1, 0.25, 620, Theme.InOut);
            pulse.AutoReverse = true;
            pulse.RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever;
            dot.BeginAnimation(OpacityProperty, pulse);
            sp.Children.Add(dot);

            line = new TextBlock
            {
                Text = L.T("스크롤 캡처 중…   Esc 중단", "Scrolling capture…   Esc to stop"),
                Foreground = Theme.BrText, FontSize = 12.5,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            sp.Children.Add(line);

            Content = new Border
            {
                CornerRadius = new CornerRadius(12),
                BorderBrush = Theme.BrBorder,
                BorderThickness = new Thickness(1),
                Background = Theme.BrGlassFallback,
                Child = sp
            };
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            Glass.MakeToolWindow(this, true);
            Glass.Apply(this, true);
            Place();
        }

        void Place()
        {
            Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double sc = VisualTreeHelper.GetDpi(this).DpiScaleX;
            int w = (int)Math.Round(DesiredSize.Width * sc);
            int h = (int)Math.Round(DesiredSize.Height * sc);
            var v = Native.GetVirtualScreen();

            // 캡처 영역 위쪽에 자리가 있으면 그 위에, 없으면 아래에 둔다.
            int x = area.Left + (area.W - w) / 2;
            int y = area.Top - h - (int)(14 * sc);
            if (y < v.Top + 8) y = area.Bottom + (int)(14 * sc);
            if (y + h > v.Bottom - 8) y = Math.Max(v.Top + 8, area.Top + (int)(8 * sc));   // 최후엔 영역 안 위쪽
            x = Math.Max(v.Left + 8, Math.Min(x, v.Right - w - 8));

            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            Native.SetWindowPos(hwnd, IntPtr.Zero, x, y, w, h,
                                Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        }

        public void Report(int pages, int height)
        {
            Dispatcher.BeginInvoke(new Action(delegate
            {
                line.Text = L.F("스크롤 캡처 중…  {0}장 · {1}px   Esc 중단", "Scrolling capture…  {0} pages · {1}px   Esc to stop", pages, height);
            }), DispatcherPriority.Background);
        }
    }
}
