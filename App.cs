// Money Shot — 진입점
//
// 부팅과 함께 창 없이 뜬다. 창을 한 번도 열지 않아도 단축키는 그 즉시 동작한다.
// (픽픽처럼 "한 번 실행해야 먹는" 상태가 생기지 않도록, 단축키 등록을 UI보다 먼저 한다.)
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using D = System.Drawing;

namespace MoneyShot
{
    public static class App
    {
        const string MutexName = "MoneyShot_SingleInstance";
        const string ShowEventName = "MoneyShot_Show";

        const int HK_REGION = 1, HK_SCROLL = 2, HK_WINDOW = 3, HK_FULL = 4, HK_EDITOR = 5;

        static Mutex mutex;
        static EventWaitHandle showEvent;
        static HwndSource msgWin;
        static System.Windows.Forms.NotifyIcon tray;
        static Application app;
        static D.Bitmap lastShot;
        static string lastShotPath;
        static readonly System.Collections.Generic.List<string> hotkeyFailures =
            new System.Collections.Generic.List<string>();

        [STAThread]
        public static void Main(string[] args)
        {
            // 누끼 경로 점검용. 창을 띄우지 않고 편집창과 똑같은 코드를 태운다.
            //   Money Shot.exe --cutout-test <넣을 그림> <내보낼 png> [다듬기 대비 경계이동]
            //   숫자를 주면 AI 그대로 결과(…-raw.png)와 다듬은 결과를 둘 다 낸다.
            //   Money Shot.exe --heal-test <넣을 그림> <내보낼 접두어> x y w h
            //   그 사각형을 스팟 힐링 세 방식과 내용 채우기로 각각 메워 접두어-*.png 로 낸다.
            if (args.Length >= 7 && args[0] == "--heal-test")
            {
                HealTest(args[1], args[2], int.Parse(args[3]), int.Parse(args[4]), int.Parse(args[5]), int.Parse(args[6]));
                return;
            }
            if (args.Length >= 3 && args[0] == "--cutout-test")
            {
                MatteSettings ms = null;
                if (args.Length >= 6 && args[3] != "precise")
                    ms = new MatteSettings { Refine = double.Parse(args[3]), Contrast = double.Parse(args[4]), Shift = double.Parse(args[5]) };
                cutoutPrecise = Array.IndexOf(args, "precise") >= 0;
                CutoutTest(args[1], args[2], ms);
                return;
            }

            bool createdNew;
            mutex = new Mutex(true, MutexName, out createdNew);
            if (!createdNew)
            {
                // 이미 떠 있다 — 그쪽 설정창을 열어주고 이 프로세스는 물러난다.
                try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { }
                return;
            }
            showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);

            Directory.CreateDirectory(Settings.BaseDir);
            Log.W("=== 시작 " + Process.GetCurrentProcess().MainModule.FileName + " ===");

            app = new Application();
            // UI 스레드에서 터진 예외로 트레이 앱이 통째로 죽는 일을 막는다.
            app.DispatcherUnhandledException += delegate(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs a)
            {
                Log.W("처리 안 된 예외: " + a.Exception);
                Notify(L.T("문제가 생겼다", "Something went wrong"), a.Exception.Message + L.T("  (자세한 내용은 log.txt)", "  (details in log.txt)"));
                a.Handled = true;
            };
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Styles.Apply(app);

            // 단축키부터. UI 구성보다 먼저 해야 부팅 직후 공백이 없다.
            CreateMessageWindow();
            RegisterHotkeys();

            BuildTray();
            WatchShowRequests();

            var s = Settings.Current;
            if (s.autoStart) Autostart.Set(true);

            // 부팅으로 뜬 경우(--tray)에는 무슨 일이 있어도 창을 띄우지 않는다.
            bool fromBoot = Array.IndexOf(args, "--tray") >= 0;
            bool showUi = !fromBoot && Array.IndexOf(args, "--settings") >= 0;

            if (!fromBoot && !s.firstRunDone)
            {
                s.firstRunDone = true;
                s.Save();
                app.Dispatcher.BeginInvoke(new Action(delegate { SettingsWindow.Open(true); }));
            }
            else if (showUi)
            {
                app.Dispatcher.BeginInvoke(new Action(delegate { SettingsWindow.Open(false); }));
            }
            else if (hotkeyFailures.Count > 0)
            {
                Notify(L.T("단축키 일부를 못 잡았다", "Some shortcuts couldn't be registered"), string.Join(", ", hotkeyFailures.ToArray())
                    + L.T(" — 다른 앱이 쓰는 중이다. 트레이 아이콘에서 바꿀 수 있다.", " — another app is using them. Change them from the tray icon."));
            }

            app.Run();
        }

        static void HealTest(string inPath, string prefix, int rx, int ry, int rw, int rh)
        {
            var log = new Action<string>(delegate(string m) { Console.WriteLine(m); Log.W("[점검] " + m); });
            try
            {
                prefix = Path.GetFullPath(prefix);
                Canvas32 src;
                using (var b = new D.Bitmap(inPath))
                using (var norm = Cap.Clone32(b))
                    src = Canvas32.From(norm);
                log("힐링 점검: " + src.W + " x " + src.H + ", 자리 " + rx + "," + ry + " " + rw + "x" + rh);
                var cov = new byte[src.W * src.H];
                for (int y = Math.Max(0, ry); y < Math.Min(src.H, ry + rh); y++)
                    for (int x = Math.Max(0, rx); x < Math.Min(src.W, rx + rw); x++) cov[y * src.W + x] = 255;

                foreach (HealMode m in new[] { HealMode.ContentAware, HealMode.Proximity, HealMode.Smooth })
                {
                    var c = src.Clone();
                    var t = Stopwatch.StartNew();
                    bool ok = Heal.Spot(c, cov, m, 1.0, 12345);
                    log(string.Format("  스팟 {0}: {1}, {2}ms", m, ok, t.ElapsedMilliseconds));
                    using (var o = c.ToBitmap()) Cap.Save(o, prefix + "-" + m + ".png");
                }
                {
                    var c = src.Clone();
                    var t = Stopwatch.StartNew();
                    bool ok = Heal.ContentFill(c, cov);
                    log(string.Format("  내용 채우기: {0}, {1}ms", ok, t.ElapsedMilliseconds));
                    using (var o = c.ToBitmap()) Cap.Save(o, prefix + "-Fill.png");
                }
            }
            catch (Exception ex) { log("실패: " + ex); }
        }

        static bool cutoutPrecise;

        static void CutoutTest(string inPath, string outPath, MatteSettings ms)
        {
            var log = new Action<string>(delegate(string m)
            {
                Console.WriteLine(m);
                Log.W("[점검] " + m);
            });
            try
            {
                outPath = Path.GetFullPath(outPath);    // 저장이 폴더를 만들려 들어 상대 경로면 터진다
                log("입력: " + inPath);
                Canvas32 c;
                using (var src = new D.Bitmap(inPath))
                using (var norm = Cap.Clone32(src))
                    c = Canvas32.From(norm);
                log("크기: " + c.W + " x " + c.H);

                var sw = System.Diagnostics.Stopwatch.StartNew();
                string last = null;
                var prog = new Progress
                {
                    Report = delegate(string line, double pct)
                    {
                        if (line == last) return;
                        last = line;
                        log("  " + line);
                    }
                };
                var keep = Cutout.RunHeadless(c, prog, cutoutPrecise);
                if (keep == null) { log("결과 없음"); return; }

                int opaque = 0, clear = 0;
                foreach (var k in keep) { if (k > 200) opaque++; else if (k < 40) clear++; }
                log(string.Format("마스크: 남김 {0:P1}, 지움 {1:P1}, 걸린 시간 {2:0.0}초",
                    (double)opaque / keep.Length, (double)clear / keep.Length, sw.Elapsed.TotalSeconds));

                if (ms != null)
                {
                    var rawOut = c.Clone();
                    Ops.SetAlpha(rawOut, keep);
                    Ops.Defringe(rawOut, 2);
                    string rawPath = Path.Combine(Path.GetDirectoryName(outPath),
                        Path.GetFileNameWithoutExtension(outPath) + "-raw.png");
                    using (var b = rawOut.ToBitmap()) Cap.Save(b, rawPath);
                    log("AI 그대로: " + rawPath);

                    foreach (int limit in new[] { Matte.PreviewLimit, int.MaxValue })
                    {
                        var t = System.Diagnostics.Stopwatch.StartNew();
                        var r = Matte.Refine(keep, c, ms, limit);
                        log(string.Format("다듬기 {0}: {1}ms", limit == int.MaxValue ? "원본 크기" : "미리보기", t.ElapsedMilliseconds));
                        if (limit == int.MaxValue) keep = r;
                    }
                    int kept = 0;
                    var t2 = System.Diagnostics.Stopwatch.StartNew();
                    var g = Matte.Trace(keep, c.W, c.H, 128, 0, 0);
                    log(string.Format("테두리 따기: {0}ms, 영역 {1}", t2.ElapsedMilliseconds,
                        g == null ? "없음" : ((int)g.Bounds.Width + " x " + (int)g.Bounds.Height)));
                    foreach (var k in keep) if (k > 200) kept++;
                    log(string.Format("다듬은 뒤 남김 {0:P1}", (double)kept / keep.Length));
                }

                Ops.SetAlpha(c, keep);
                Ops.Defringe(c, 2);
                using (var outBmp = c.ToBitmap()) Cap.Save(outBmp, outPath);
                log("저장: " + outPath);
            }
            catch (Exception ex)
            {
                log("실패: " + ex);
            }
        }

        // ---------- 단축키 ----------

        static void CreateMessageWindow()
        {
            var p = new HwndSourceParameters("MoneyShotHotkeys")
            {
                Width = 1,
                Height = 1,
                // WS_OVERLAPPED. 보이지 않지만 메시지는 받는 평범한 최상위 창이어야
                // RegisterHotKey가 WM_HOTKEY를 여기로 보낸다.
                WindowStyle = unchecked((int)0x80000000),
                ParentWindow = IntPtr.Zero
            };
            msgWin = new HwndSource(p);
            msgWin.AddHook(WndProc);
            Log.W("메시지 창 hwnd=0x" + msgWin.Handle.ToString("X"));
        }

        static void RegisterHotkeys()
        {
            hotkeyFailures.Clear();
            var s = Settings.Current;
            Try(HK_REGION, s.hkRegion, L.T("영역 캡처", "Region Capture"));
            Try(HK_SCROLL, s.hkScroll, L.T("스크롤 캡처", "Scrolling Capture"));
            Try(HK_WINDOW, s.hkWindow, L.T("창 캡처", "Window Capture"));
            Try(HK_FULL, s.hkFullscreen, L.T("전체 화면", "Full Screen"));
            Try(HK_EDITOR, s.hkEditor, L.T("편집창 열기", "Open Editor"));
        }

        static void Try(int id, HotkeyDef d, string label)
        {
            if (d == null || !d.enabled || d.vk == 0) return;
            Native.UnregisterHotKey(msgWin.Handle, id);
            bool ok = Native.RegisterHotKey(msgWin.Handle, id, d.mods | Native.MOD_NOREPEAT, d.vk);
            int err = ok ? 0 : System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            Log.W("단축키 " + label + " [" + d + "] mods=" + (d.mods | Native.MOD_NOREPEAT) +
                  " vk=" + d.vk + " → " + (ok ? "등록" : "실패 err=" + err));
            if (!ok) hotkeyFailures.Add(label + "(" + d + ")");
        }

        public static void ReloadHotkeys()
        {
            for (int i = 1; i <= 5; i++) Native.UnregisterHotKey(msgWin.Handle, i);
            RegisterHotkeys();
        }

        public static string[] HotkeyProblems() { return hotkeyFailures.ToArray(); }

        static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wp, IntPtr lp, ref bool handled)
        {
            if (msg != Native.WM_HOTKEY) return IntPtr.Zero;
            handled = true;
            Log.W("단축키 수신 id=" + wp.ToInt32());
            try
            {
            switch (wp.ToInt32())
                {
                case HK_REGION: CaptureRegion(); break;
                case HK_SCROLL: CaptureScrolling(); break;
                case HK_WINDOW: CaptureFixed(Cap.WindowUnderCursor()); break;
                case HK_FULL: CaptureFixed(Cap.MonitorUnderCursor()); break;
                case HK_EDITOR: OpenLastInEditor(); break;
                }
            }
            catch (Exception ex)
            {
                Log.W("단축키 처리 중 예외: " + ex);
                Notify(L.T("문제가 생겼다", "Something went wrong"), ex.Message);
            }
            return IntPtr.Zero;
        }

        // ---------- 캡처 흐름 ----------

        public static void CaptureRegion()
        {
            if (RegionOverlay.IsOpen) return;
            RegionOverlay.Begin(Handle);
        }

        public static void CaptureFixed(RECT r)
        {
            if (r.W <= 0 || r.H <= 0) return;
            var bmp = Cap.Screen(r);
            Handle(new CaptureResult { Image = bmp, Bounds = r, Action = PostAction.Copy });
        }

        public static void CaptureScrolling()
        {
            if (RegionOverlay.IsOpen) return;
            ScrollCapture.Begin(Handle);
        }

        // 캡처 한 건이 끝났을 때의 뒤처리. 어느 경로로 들어왔든 여기로 모인다.
        public static void Handle(CaptureResult r)
        {
            if (r == null || r.Image == null) return;

            var s = Settings.Current;
            if (s.copyToClipboard) Cap.ToClipboard(r.Image);
            Sfx.Shutter();

            string path = null;
            if (s.autoSaveEvery)
            {
                try { path = s.NewFilePath(".png"); Cap.Save(r.Image, path); }
                catch { path = null; }
            }

            Remember(r.Image, path);

            if (r.Action == PostAction.Edit)
            {
                OpenEditor(Cap.Clone32(r.Image), path);
                r.Image.Dispose();
                return;
            }
            if (r.Action == PostAction.Save)
            {
                string p = SaveAsDialog(r.Image);
                if (p != null) { path = p; Remember(r.Image, path); }
                ThumbCard.Show(r.Image, path);
                return;
            }
            ThumbCard.Show(r.Image, path);
        }

        static void Remember(D.Bitmap bmp, string path)
        {
            try { if (lastShot != null) lastShot.Dispose(); } catch { }
            lastShot = Cap.Clone32(bmp);
            lastShotPath = path;
        }

        public static void OpenLastInEditor()
        {
            // 마지막 캡처가 없으면 클립보드에 있는 이미지라도 연다.
            if (lastShot == null)
            {
                var fromClip = Cap.FromClipboard();
                if (fromClip != null) { OpenEditor(fromClip, null); return; }
                Notify(L.T("열 이미지가 없다", "No image to open"), L.T("캡처를 하거나 이미지를 복사한 뒤에 다시 눌러라.", "Take a capture or copy an image, then try again."));
                return;
            }
            OpenEditor(Cap.Clone32(lastShot), lastShotPath);
        }

        public static void OpenEditor(D.Bitmap bmp, string path)
        {
            app.Dispatcher.BeginInvoke(new Action(delegate
            {
                try { EditorWindow.Open(bmp, path); }
                catch (Exception ex) { Notify(L.T("편집창을 열지 못했다", "Couldn't open the editor"), ex.Message); }
            }));
        }

        public static string SaveAsDialog(D.Bitmap bmp) { return SaveAsDialog(bmp, null); }

        // psd를 넘기면 PSD도 고를 수 있다(편집창이 레이어째 쓴다).
        public static string SaveAsDialog(D.Bitmap bmp, Action<string> psd)
        {
            try
            {
                var s = Settings.Current;
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Title = L.T("캡처 저장", "Save Capture"),
                    Filter = L.T("PNG 이미지 (*.png)|*.png|JPEG 이미지 (*.jpg)|*.jpg|비트맵 (*.bmp)|*.bmp", "PNG image (*.png)|*.png|JPEG image (*.jpg)|*.jpg|Bitmap (*.bmp)|*.bmp") +
                             (psd != null ? L.T("|PSD (레이어 유지) (*.psd)|*.psd", "|PSD (keeps layers) (*.psd)|*.psd") : ""),
                    DefaultExt = ".png",
                    FileName = DateTime.Now.ToString(s.fileNamePattern) + ".png",
                    InitialDirectory = Directory.Exists(s.saveDir)
                        ? s.saveDir
                        : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                    OverwritePrompt = true,
                    AddExtension = true
                };
                if (dlg.ShowDialog() != true) return null;
                if (psd != null && Path.GetExtension(dlg.FileName).ToLowerInvariant() == ".psd") psd(dlg.FileName);
                else Cap.Save(bmp, dlg.FileName);
                // 다음부터는 방금 고른 폴더에서 시작한다.
                s.saveDir = Path.GetDirectoryName(dlg.FileName);
                s.Save();
                return dlg.FileName;
            }
            catch (Exception ex) { Notify(L.T("저장하지 못했다", "Couldn't save"), ex.Message); return null; }
        }

        // ---------- 트레이 ----------

        static void BuildTray()
        {
            tray = new System.Windows.Forms.NotifyIcon();
            tray.Text = "Money Shot";
            tray.Icon = LoadIcon();
            tray.Visible = true;

            var m = new System.Windows.Forms.ContextMenuStrip();
            m.Items.Add(L.T("영역 캡처", "Region Capture") + "\t" + Settings.Current.hkRegion, null, delegate { CaptureRegion(); });
            m.Items.Add(L.T("스크롤 캡처", "Scrolling Capture") + "\t" + Settings.Current.hkScroll, null, delegate { CaptureScrolling(); });
            m.Items.Add(L.T("창 캡처", "Window Capture") + "\t" + Settings.Current.hkWindow, null, delegate { CaptureFixed(Cap.WindowUnderCursor()); });
            m.Items.Add(L.T("전체 화면", "Full Screen") + "\t" + Settings.Current.hkFullscreen, null, delegate { CaptureFixed(Cap.MonitorUnderCursor()); });
            m.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            m.Items.Add(L.T("편집창 열기", "Open Editor"), null, delegate { OpenLastInEditor(); });
            m.Items.Add(L.T("저장 폴더 열기", "Open Save Folder"), null, delegate
            {
                try
                {
                    Directory.CreateDirectory(Settings.Current.saveDir);
                    Process.Start("explorer.exe", "\"" + Settings.Current.saveDir + "\"");
                }
                catch { }
            });
            m.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            m.Items.Add(L.T("설정…", "Settings…"), null, delegate { app.Dispatcher.BeginInvoke(new Action(delegate { SettingsWindow.Open(false); })); });
            m.Items.Add(L.T("종료", "Quit"), null, delegate { Quit(); });
            Styles.ApplyTray(m);
            tray.ContextMenuStrip = m;

            tray.MouseClick += delegate(object s, System.Windows.Forms.MouseEventArgs e)
            {
                if (e.Button == System.Windows.Forms.MouseButtons.Left) CaptureRegion();
            };
            tray.DoubleClick += delegate { OpenLastInEditor(); };
        }

        static D.Icon LoadIcon()
        {
            try
            {
                string exe = Process.GetCurrentProcess().MainModule.FileName;
                var ico = D.Icon.ExtractAssociatedIcon(exe);
                if (ico != null) return ico;
            }
            catch { }
            return D.SystemIcons.Application;
        }

        public static void Notify(string title, string body)
        {
            try
            {
                if (tray == null) return;
                tray.BalloonTipTitle = title;
                tray.BalloonTipText = body;
                tray.BalloonTipIcon = System.Windows.Forms.ToolTipIcon.None;
                tray.ShowBalloonTip(4000);
            }
            catch { }
        }

        static void WatchShowRequests()
        {
            var t = new Thread(delegate ()
            {
                while (true)
                {
                    showEvent.WaitOne();
                    app.Dispatcher.BeginInvoke(new Action(delegate { SettingsWindow.Open(false); }));
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        public static void Quit()
        {
            try { for (int i = 1; i <= 5; i++) Native.UnregisterHotKey(msgWin.Handle, i); } catch { }
            try { tray.Visible = false; tray.Dispose(); } catch { }
            try { app.Shutdown(); } catch { }
            Environment.Exit(0);
        }
    }

    // 부팅 시 자동 실행. 관리자 권한이 필요 없는 사용자 레지스트리 Run 키를 쓴다.
    public static class Autostart
    {
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Name = "Money Shot";

        public static bool IsOn()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Key))
                    return k != null && k.GetValue(Name) != null;
            }
            catch { return false; }
        }

        public static void Set(bool on)
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Key, true))
                {
                    if (k == null) return;
                    if (on)
                    {
                        string exe = Process.GetCurrentProcess().MainModule.FileName;
                        // --tray: 부팅 때는 창 없이 알림 영역에만 올라온다.
                        k.SetValue(Name, "\"" + exe + "\" --tray");
                    }
                    else k.DeleteValue(Name, false);
                }
            }
            catch { }
        }
    }

    // 윈도우 11은 기본적으로 PrintScreen을 캡처 도구에 넘긴다.
    // 그 설정이 켜져 있으면 우리 단축키가 먹지 않으므로 확인하고 되돌릴 수 있게 한다.
    public static class PrintScreenKey
    {
        const string Key = @"Control Panel\Keyboard";
        const string Name = "PrintScreenKeyForSnippingEnabled";

        public static bool TakenBySnippingTool()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Key))
                {
                    if (k == null) return false;
                    var v = k.GetValue(Name);
                    return v != null && Convert.ToInt32(v) != 0;
                }
            }
            catch { return false; }
        }

        public static void Release()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Key, true))
                    if (k != null) k.SetValue(Name, 0, Microsoft.Win32.RegistryValueKind.DWord);
            }
            catch { }
        }
    }
}
