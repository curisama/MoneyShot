// Money Shot 설치 프로그램
//
// 앱 본체를 리소스로 품고 있는 단일 exe다.
//
// 왜 이렇게 만드는가: 흔한 설치파일 제작기들은 자가추출 패커 구조를 쓰는데, 그 구조 자체가
// 백신 휴리스틱에 걸린다(실제 악성이 아니라 전형적인 오탐이다). 예전에 ps2exe로 만든 실행 파일이
// 알약에 잡혔던 것도 같은 이유였다. 그래서 여기서도 패커를 거치지 않고, 윈도에 기본으로 들어 있는
// C# 컴파일러로 평범한 관리형 어셈블리를 만든다.
//
// 설치 위치는 사용자 폴더다. 관리자 권한을 묻지 않으므로 권한이 없는 PC에도 그냥 깔린다.
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace MoneyShotSetup
{
    public static class Setup
    {
        public const string AppName = "Money Shot";
        public const string ExeName = "Money Shot.exe";
        public const string RegKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\MoneyShot";
        public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string Payload = "payload";       // 품고 있는 본체 리소스 이름

        public static string InstallDir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Programs", AppName);
            }
        }

        public static string InstalledExe { get { return Path.Combine(InstallDir, ExeName); } }
        public static string UninstallerPath { get { return Path.Combine(InstallDir, "Uninstall.exe"); } }
        // 1.x 설치본이 쓰던 이름. 덮어 깔 때 지운다.
        static string LegacyUninstaller { get { return Path.Combine(InstallDir, "제거.exe"); } }

        public static string StartMenuLink
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk");
            }
        }

        public static string DesktopLink
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk");
            }
        }

        public static string DataDir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);
            }
        }

        public static bool IsInstalled { get { return File.Exists(InstalledExe); } }

        public static string InstalledVersion
        {
            get
            {
                try { return FileVersionInfo.GetVersionInfo(InstalledExe).FileVersion; }
                catch { return null; }
            }
        }

        [STAThread]
        public static void Main(string[] args)
        {
            bool uninstall = Has(args, "--uninstall");
            bool silent = Has(args, "/S") || Has(args, "--silent");

            // 제거는 자기가 있는 폴더를 지워야 해서, 임시 폴더로 옮겨 붙은 다음에 진행한다.
            if (uninstall && !Has(args, "--relocated"))
            {
                if (Relocate(silent)) return;
            }

            // 옮겨 붙은 쪽은 원본이 완전히 끝날 때까지 기다린다.
            // 원본이 살아 있는 동안은 설치 폴더의 파일을 붙들고 있어서 지워지지 않는다.
            WaitForParent(args);

            // 조용한 설치. 여러 대에 한꺼번에 뿌릴 때 쓴다.
            //   Money Shot 1.0 설치.exe /S              깔기
            //   Money Shot 1.0 설치.exe /S --no-autorun 자동 실행 없이
            //   Money Shot 1.0 설치.exe /S --no-desktop 바탕화면 바로가기 없이
            //   Uninstall.exe --uninstall /S                 지우기 (--purge 면 설정과 모델까지)
            if (silent)
            {
                try
                {
                    if (uninstall) Uninstall(Has(args, "--purge"), delegate { });
                    else Install(!Has(args, "--no-desktop"), !Has(args, "--no-autorun"), delegate { });
                    Environment.Exit(0);
                }
                catch (Exception ex)
                {
                    try { Console.Error.WriteLine(ex.Message); } catch { }
                    Environment.Exit(1);
                }
                return;
            }

            var app = new Application();
            app.ShutdownMode = ShutdownMode.OnLastWindowClose;
            app.Run(new SetupWindow(uninstall));
        }

        static bool Has(string[] args, string flag)
        {
            foreach (var a in args)
                if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // 제거 프로그램을 임시 폴더로 복사해 거기서 다시 띄운다.
        static bool Relocate(bool silent)
        {
            try
            {
                string me = Process.GetCurrentProcess().MainModule.FileName;
                if (!me.StartsWith(InstallDir, StringComparison.OrdinalIgnoreCase)) return false;

                string tmp = Path.Combine(Path.GetTempPath(), "MoneyShot-uninstall-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".exe");
                File.Copy(me, tmp, true);
                // 내 프로세스 번호를 넘겨, 저쪽이 내가 끝나기를 기다리게 한다.
                var argv = "--uninstall --relocated --wait " + Process.GetCurrentProcess().Id
                           + (silent ? " /S" : "");
                Process.Start(new ProcessStartInfo(tmp, argv) { UseShellExecute = true });
                return true;
            }
            catch { return false; }
        }

        static void WaitForParent(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (!string.Equals(args[i], "--wait", StringComparison.OrdinalIgnoreCase)) continue;
                int pid;
                if (!int.TryParse(args[i + 1], out pid)) return;
                try
                {
                    var p = Process.GetProcessById(pid);
                    p.WaitForExit(15000);
                }
                catch { }          // 이미 끝났으면 그만이다
                System.Threading.Thread.Sleep(250);
                return;
            }
        }

        // ---------- 실제 작업 ----------

        public static void StopRunning()
        {
            try
            {
                foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExeName)))
                {
                    try { p.Kill(); p.WaitForExit(4000); } catch { }
                }
            }
            catch { }
            System.Threading.Thread.Sleep(300);
        }

        public static void Install(bool desktopShortcut, bool autoStart, Action<string> log)
        {
            log(L.T("실행 중이면 내리는 중…", "Closing Money Shot if it’s running…"));
            StopRunning();

            log(L.T("파일 푸는 중…", "Extracting files…"));
            Directory.CreateDirectory(InstallDir);
            using (var src = Assembly.GetExecutingAssembly().GetManifestResourceStream(Payload))
            {
                if (src == null) throw new Exception(L.T("설치 파일 안에 본체가 없다", "The installer is missing the app payload"));
                using (var dst = File.Create(InstalledExe)) src.CopyTo(dst);
            }

            // 오픈소스 고지를 앱 옆에 둔다
            try
            {
                using (var n = Assembly.GetExecutingAssembly().GetManifestResourceStream("notices"))
                    if (n != null) using (var dst = File.Create(Path.Combine(InstallDir, "THIRD_PARTY_NOTICES.txt"))) n.CopyTo(dst);
            }
            catch { }

            // 제거 프로그램은 이 설치 파일 자신이다
            try { File.Copy(Process.GetCurrentProcess().MainModule.FileName, UninstallerPath, true); }
            catch { }
            try { if (File.Exists(LegacyUninstaller)) File.Delete(LegacyUninstaller); }
            catch { }

            log(L.T("바로가기 만드는 중…", "Creating shortcuts…"));
            MakeShortcut(StartMenuLink, InstalledExe, L.T("화면 캡처 + 이미지 편집", "Screen capture + image editor"));
            if (desktopShortcut) MakeShortcut(DesktopLink, InstalledExe, L.T("화면 캡처 + 이미지 편집", "Screen capture + image editor"));
            else Delete(DesktopLink);

            log(L.T("등록하는 중…", "Registering…"));
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (k != null)
                {
                    // --tray: 부팅 때는 창 없이 알림 영역에만 올라온다
                    if (autoStart) k.SetValue(AppName, "\"" + InstalledExe + "\" --tray");
                    else k.DeleteValue(AppName, false);
                }
            }

            long size = 0;
            try { size = new FileInfo(InstalledExe).Length / 1024; } catch { }
            using (var k = Registry.CurrentUser.CreateSubKey(RegKey))
            {
                if (k != null)
                {
                    k.SetValue("DisplayName", AppName);
                    k.SetValue("DisplayVersion", Info.Version);
                    k.SetValue("Publisher", "curisama");
                    k.SetValue("DisplayIcon", InstalledExe);
                    k.SetValue("InstallLocation", InstallDir);
                    k.SetValue("UninstallString", "\"" + UninstallerPath + "\" --uninstall");
                    k.SetValue("EstimatedSize", (int)size, RegistryValueKind.DWord);
                    k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                }
            }
            log(L.T("끝났다", "Done"));
        }

        public static void Uninstall(bool alsoData, Action<string> log)
        {
            log(L.T("실행 중이면 내리는 중…", "Closing Money Shot if it’s running…"));
            StopRunning();

            log(L.T("바로가기 지우는 중…", "Removing shortcuts…"));
            Delete(StartMenuLink);
            Delete(DesktopLink);

            log(L.T("등록 지우는 중…", "Removing registry entries…"));
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                    if (k != null) k.DeleteValue(AppName, false);
            }
            catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(RegKey, false); } catch { }

            log(L.T("파일 지우는 중…", "Deleting files…"));
            // 막 끝난 프로세스가 파일을 놓는 데 한 박자 걸릴 수 있어 몇 번 더 해본다.
            for (int attempt = 0; attempt < 6; attempt++)
            {
                try
                {
                    if (!Directory.Exists(InstallDir)) break;
                    Directory.Delete(InstallDir, true);
                    break;
                }
                catch
                {
                    try { foreach (var f in Directory.GetFiles(InstallDir)) TryDelete(f); } catch { }
                    System.Threading.Thread.Sleep(400);
                }
            }

            if (alsoData)
            {
                log(L.T("설정과 모델 지우는 중…", "Deleting settings and models…"));
                try { if (Directory.Exists(DataDir)) Directory.Delete(DataDir, true); } catch { }
            }
            log(L.T("끝났다", "Done"));
        }

        static void TryDelete(string p) { try { File.Delete(p); } catch { } }
        static void Delete(string p) { try { if (File.Exists(p)) File.Delete(p); } catch { } }

        // 바로가기는 COM으로 만든다. 늦은 바인딩이라 참조를 걸 필요가 없다.
        static void MakeShortcut(string linkPath, string target, string desc)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(linkPath));
                var t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) return;
                dynamic shell = Activator.CreateInstance(t);
                dynamic link = shell.CreateShortcut(linkPath);
                link.TargetPath = target;
                link.WorkingDirectory = Path.GetDirectoryName(target);
                link.IconLocation = target + ",0";
                link.Description = desc;
                link.Save();
            }
            catch { }
        }

        public static long PayloadSize
        {
            get
            {
                try
                {
                    using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(Payload))
                        return s == null ? 0 : s.Length;
                }
                catch { return 0; }
            }
        }
    }
}
