// Money Shot — Win32 상호운용 계층
// 화면 캡처, 전역 단축키, DWM 아크릴, 모니터 열거, 입력 합성
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace MoneyShot
{
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int W { get { return Right - Left; } }
        public int H { get { return Bottom - Top; } }
    }

    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    public static class Native
    {
        // ---------- DWM (아크릴 / 라운드 코너 / 다크모드) ----------
        public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
        public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

        // DWM_SYSTEMBACKDROP_TYPE
        public const int DWMSBT_AUTO = 0, DWMSBT_NONE = 1, DWMSBT_MAINWINDOW = 2,
                         DWMSBT_TRANSIENTWINDOW = 3, DWMSBT_TABBEDWINDOW = 4;
        // DWM_WINDOW_CORNER_PREFERENCE
        public const int DWMWCP_DEFAULT = 0, DWMWCP_DONOTROUND = 1, DWMWCP_ROUND = 2, DWMWCP_ROUNDSMALL = 3;

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT value, int size);

        [StructLayout(LayoutKind.Sequential)]
        public struct MARGINS { public int cxLeftWidth, cxRightWidth, cyTopHeight, cyBottomHeight; }
        [DllImport("dwmapi.dll")]
        public static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS m);

        // ---------- 전역 단축키 ----------
        public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;
        public const int WM_HOTKEY = 0x0312;
        public const uint VK_SNAPSHOT = 0x2C;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // ---------- GDI 화면 캡처 ----------
        public const int SRCCOPY = 0x00CC0020, CAPTUREBLT = 0x40000000;

        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h,
                                                                  IntPtr src, int sx, int sy, int rop);

        // ---------- 모니터 ----------
        public delegate bool MonitorEnumProc(IntPtr hMon, IntPtr hdc, ref RECT rc, IntPtr data);
        [DllImport("user32.dll")]
        public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool GetMonitorInfo(IntPtr hMon, ref MONITORINFOEX mi);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        public const uint MONITOR_DEFAULTTONEAREST = 2;

        // 물리 픽셀 기준 모니터 사각형 목록.
        // PerMonitorV2 프로세스에서 GetMonitorInfo는 항상 실제 픽셀 좌표를 반환한다.
        public static List<RECT> GetMonitors()
        {
            var list = new List<RECT>();
            MonitorEnumProc cb = delegate(IntPtr h, IntPtr dc, ref RECT r, IntPtr d)
            {
                var mi = new MONITORINFOEX();
                mi.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
                if (GetMonitorInfo(h, ref mi)) list.Add(mi.rcMonitor); else list.Add(r);
                return true;
            };
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, cb, IntPtr.Zero);
            return list;
        }

        // 모든 모니터를 감싸는 가상 데스크톱 사각형 (물리 픽셀).
        public static RECT GetVirtualScreen()
        {
            var ms = GetMonitors();
            if (ms.Count == 0) return new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
            var r = ms[0];
            foreach (var m in ms)
            {
                if (m.Left < r.Left) r.Left = m.Left;
                if (m.Top < r.Top) r.Top = m.Top;
                if (m.Right > r.Right) r.Right = m.Right;
                if (m.Bottom > r.Bottom) r.Bottom = m.Bottom;
            }
            return r;
        }

        // ---------- 창 ----------
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        public const uint GA_ROOT = 2;
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
        public const int VK_ESCAPE = 0x1B;
        [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after,
                                                                        int x, int y, int cx, int cy, uint flags);
        public const uint SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr hwnd, int index, int val);
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x8000000;

        // 창의 진짜 화면 경계.
        // GetWindowRect는 윈도우 10/11에서 보이지 않는 그림자 여백까지 포함하므로 DWM 확장 프레임을 우선 쓴다.
        public static RECT GetRealWindowRect(IntPtr hwnd)
        {
            RECT r;
            if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out r, Marshal.SizeOf(typeof(RECT))) == 0
                && r.W > 0 && r.H > 0) return r;
            GetWindowRect(hwnd, out r);
            return r;
        }

        // ---------- 최대화 크기 ----------
        [StructLayout(LayoutKind.Sequential)]
        public struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }
        public const int WM_GETMINMAXINFO = 0x0024;

        // 테두리 없는 창이 최대화될 때 작업표시줄을 덮지 않도록 최대 크기를 작업 영역으로 제한한다.
        public static void ClampMaximize(IntPtr hwnd, IntPtr lParam)
        {
            var mon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (mon == IntPtr.Zero) return;
            var mi = new MONITORINFOEX();
            mi.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
            if (!GetMonitorInfo(mon, ref mi)) return;

            var info = (MINMAXINFO)Marshal.PtrToStructure(lParam, typeof(MINMAXINFO));
            info.ptMaxPosition.X = mi.rcWork.Left - mi.rcMonitor.Left;
            info.ptMaxPosition.Y = mi.rcWork.Top - mi.rcMonitor.Top;
            info.ptMaxSize.X = mi.rcWork.W;
            info.ptMaxSize.Y = mi.rcWork.H;
            info.ptMaxTrackSize.X = mi.rcWork.W;
            info.ptMaxTrackSize.Y = mi.rcWork.H;
            Marshal.StructureToPtr(info, lParam, true);
        }

        // ---------- 입력 합성 (스크롤 캡처용) ----------
        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT { public uint type; public MOUSEINPUT mi; }
        public const uint INPUT_MOUSE = 0;
        public const uint MOUSEEVENTF_WHEEL = 0x0800;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint n, INPUT[] inputs, int size);

        public static void ScrollWheel(int clicks)
        {
            var inp = new INPUT[1];
            inp[0].type = INPUT_MOUSE;
            inp[0].mi.dwFlags = MOUSEEVENTF_WHEEL;
            inp[0].mi.mouseData = unchecked((uint)(clicks * 120));
            SendInput(1, inp, Marshal.SizeOf(typeof(INPUT)));
        }

        // ---------- 커서 그리기 ----------
        [StructLayout(LayoutKind.Sequential)]
        public struct CURSORINFO { public int cbSize, flags; public IntPtr hCursor; public POINT ptScreenPos; }
        [DllImport("user32.dll")] public static extern bool GetCursorInfo(ref CURSORINFO ci);
        [DllImport("user32.dll")] public static extern bool DrawIconEx(IntPtr hdc, int x, int y, IntPtr icon,
                                                                      int w, int h, int step, IntPtr brush, int flags);
        public const int DI_NORMAL = 0x0003, CURSOR_SHOWING = 0x0001;
    }
}
