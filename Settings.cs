// Money Shot — 설정 저장
// %APPDATA%\Money Shot\settings.json
using System;
using System.IO;
using System.Web.Script.Serialization;

namespace MoneyShot
{
    public class HotkeyDef
    {
        public uint mods { get; set; }
        public uint vk { get; set; }
        public bool enabled { get; set; }

        public HotkeyDef() { enabled = true; }
        public HotkeyDef(uint m, uint k) { mods = m; vk = k; enabled = true; }

        public override string ToString()
        {
            string s = "";
            if ((mods & Native.MOD_CONTROL) != 0) s += "Ctrl+";
            if ((mods & Native.MOD_SHIFT) != 0) s += "Shift+";
            if ((mods & Native.MOD_ALT) != 0) s += "Alt+";
            if ((mods & Native.MOD_WIN) != 0) s += "Win+";
            if (vk == Native.VK_SNAPSHOT) return s + "PrintScreen";
            return s + ((System.Windows.Forms.Keys)vk).ToString();
        }
    }

    public class Settings
    {
        // 저장
        public string saveDir { get; set; }
        public bool autoSaveEvery { get; set; }      // 캡처할 때마다 자동 보관
        public string fileNamePattern { get; set; }  // .NET 날짜 서식, 작은따옴표 안은 리터럴

        // 캡처 후 동작
        public bool copyToClipboard { get; set; }
        public bool showThumbnail { get; set; }
        public int thumbnailSeconds { get; set; }
        public bool playSound { get; set; }
        public bool includeCursor { get; set; }

        // 영역 선택 방식
        // true  = 마우스를 놓는 즉시 캡처 (빠름, 프린트스크린 근육기억에 맞음)
        // false = 놓으면 핸들이 생겨 조정 후 Enter로 확정 (정확함, 맥 Cmd+Shift+5 방식)
        public bool captureOnRelease { get; set; }
        public bool showMagnifier { get; set; }

        // 스크롤 캡처
        public int scrollClicks { get; set; }        // 한 번에 굴릴 휠 칸 수
        public int scrollDelayMs { get; set; }       // 굴린 뒤 렌더링 기다리는 시간
        public int scrollMaxPages { get; set; }      // 안전 상한

        // 단축키
        public HotkeyDef hkRegion { get; set; }
        public HotkeyDef hkScroll { get; set; }
        public HotkeyDef hkEditor { get; set; }
        public HotkeyDef hkFullscreen { get; set; }
        public HotkeyDef hkWindow { get; set; }

        // 기타
        public bool autoStart { get; set; }
        public bool firstRunDone { get; set; }
        public double editorW { get; set; }     // 물리 픽셀
        public double editorH { get; set; }
        public double railWidth { get; set; }   // 편집기 도구 레일 폭
        public double layerPanelWidth { get; set; }
        // 설정 파일에 없던 항목(구버전)과 '꺼둠'을 구분해야 해서 nullable이다.
        public bool? showLayerPanel { get; set; }

        // 편집기 기본값
        public int brushSize { get; set; }
        public int tolerance { get; set; }
        public int featherPx { get; set; }
        public int[] matte { get; set; }
        public string language { get; set; }    // auto · ko · en (다시 켜면 적용)        // 누끼 다듬기 마지막 값: 다듬기·대비·경계 이동

        public static string BaseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Money Shot");
        public static string Path_ = Path.Combine(BaseDir, "settings.json");
        public static string ModelDir = Path.Combine(BaseDir, "models");
        public static string TempDir = Path.Combine(BaseDir, "temp");

        static JavaScriptSerializer JS = new JavaScriptSerializer();
        static Settings current;

        public static Settings Current
        {
            get { if (current == null) current = Load(); return current; }
        }

        public static Settings Defaults()
        {
            var s = new Settings();
            s.saveDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Money Shot");
            s.autoSaveEvery = false;
            // 작은따옴표 안은 글자 그대로. 설정에서 언제든 바꿀 수 있다.
            s.fileNamePattern = "yyyyMMdd_'money'";
            s.copyToClipboard = true;
            s.showThumbnail = true;
            s.thumbnailSeconds = 5;
            s.playSound = true;
            s.includeCursor = false;
            s.captureOnRelease = true;
            s.showMagnifier = true;
            s.scrollClicks = 3;
            s.scrollDelayMs = 320;
            s.scrollMaxPages = 400;
            s.hkRegion = new HotkeyDef(Native.MOD_NOREPEAT, Native.VK_SNAPSHOT);
            s.hkScroll = new HotkeyDef(Native.MOD_CONTROL | Native.MOD_NOREPEAT, Native.VK_SNAPSHOT);
            s.hkWindow = new HotkeyDef(Native.MOD_ALT | Native.MOD_NOREPEAT, Native.VK_SNAPSHOT);
            s.hkFullscreen = new HotkeyDef(Native.MOD_SHIFT | Native.MOD_NOREPEAT, Native.VK_SNAPSHOT);
            s.hkEditor = new HotkeyDef(Native.MOD_CONTROL | Native.MOD_SHIFT | Native.MOD_NOREPEAT, 0x45); // E
            s.autoStart = true;
            s.firstRunDone = false;
            s.editorW = 1240; s.editorH = 860;
            s.railWidth = 146;
            s.layerPanelWidth = 212;
            s.showLayerPanel = true;
            s.brushSize = 28;
            s.tolerance = 32;
            s.featherPx = 1;
            s.matte = null;                      // 없으면 다듬기 12 · 대비 25 · 이동 0
            return s;
        }

        public static Settings Load()
        {
            try
            {
                if (File.Exists(Path_))
                {
                    var s = JS.Deserialize<Settings>(File.ReadAllText(Path_));
                    if (s != null) { s.Fill(); return s; }
                }
            }
            catch { }
            var d = Defaults();
            try { d.Save(); } catch { }
            return d;
        }

        // 구버전 설정 파일에 없던 항목을 기본값으로 메운다.
        void Fill()
        {
            var d = Defaults();
            if (string.IsNullOrEmpty(saveDir)) saveDir = d.saveDir;
            if (string.IsNullOrEmpty(fileNamePattern)) fileNamePattern = d.fileNamePattern;
            if (thumbnailSeconds <= 0) thumbnailSeconds = d.thumbnailSeconds;
            if (scrollClicks <= 0) scrollClicks = d.scrollClicks;
            if (scrollDelayMs <= 0) scrollDelayMs = d.scrollDelayMs;
            if (scrollMaxPages < 60) scrollMaxPages = d.scrollMaxPages;
            if (hkRegion == null) hkRegion = d.hkRegion;
            if (hkScroll == null) hkScroll = d.hkScroll;
            if (hkEditor == null) hkEditor = d.hkEditor;
            if (hkWindow == null) hkWindow = d.hkWindow;
            if (hkFullscreen == null) hkFullscreen = d.hkFullscreen;
            if (editorW < 640) editorW = d.editorW;
            if (editorH < 480) editorH = d.editorH;
            if (railWidth < 46 || railWidth > 320) railWidth = d.railWidth;
            if (layerPanelWidth < 150 || layerPanelWidth > 420) layerPanelWidth = d.layerPanelWidth;
            if (showLayerPanel == null) showLayerPanel = true;
            if (brushSize <= 0) brushSize = d.brushSize;
            if (tolerance <= 0) tolerance = d.tolerance;
            if (matte == null || matte.Length != 3) matte = null;
            if (language != "ko" && language != "en") language = "auto";
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(BaseDir);
                File.WriteAllText(Path_, JS.Serialize(this));
            }
            catch { }
        }

        public bool LayerPanelOn { get { return showLayerPanel != false; } }

        public string NewFilePath(string ext)
        {
            Directory.CreateDirectory(saveDir);
            string baseName = DateTime.Now.ToString(fileNamePattern);
            string p = System.IO.Path.Combine(saveDir, baseName + ext);
            int n = 2;
            while (File.Exists(p))
            {
                p = System.IO.Path.Combine(saveDir, baseName + "_" + n + ext);
                n++;
            }
            return p;
        }
    }
}
