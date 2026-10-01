// Money Shot — 설정 창
// 평소에는 뜨지 않는다. 트레이 메뉴나 첫 실행 때만 나온다.
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace MoneyShot
{
    public class SettingsWindow : Window
    {
        static SettingsWindow open_;
        readonly Settings s = Settings.Current;
        bool glass;
        StackPanel body;
        TextBlock warnText;
        Border warnBox;

        public static void Open(bool firstRun)
        {
            if (open_ != null) { open_.Activate(); open_.Topmost = true; open_.Topmost = false; return; }
            open_ = new SettingsWindow(firstRun);
            open_.Show();
            open_.Activate();
        }

        SettingsWindow(bool firstRun)
        {
            Title = L.T("Money Shot 설정", "Money Shot Settings");
            WindowStyle = WindowStyle.None;
            AllowsTransparency = false;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = true;
            Width = 480; Height = 690;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Theme.BrBg;
            FontFamily = Theme.UI;
            this.firstRun = firstRun;
        }

        readonly bool firstRun;

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            glass = Glass.Apply(this, true);
            Build();
        }

        void Build()
        {
            body = new StackPanel();

            // ---- 제목줄 ----
            var title = new Grid { Height = 46, Background = Brushes.Transparent };
            var tl = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            tl.Children.Add(Theme.AppMark(18));
            tl.Children.Add(new TextBlock
            {
                Text = "Money Shot",
                Foreground = Theme.BrText,
                FontSize = 14.5,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(9, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            title.Children.Add(tl);

            var close = new Button
            {
                Content = "✕",
                Width = 40, Height = 46,
                HorizontalAlignment = HorizontalAlignment.Right,
                Foreground = Theme.BrMuted,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                FontSize = 14
            };
            close.Click += delegate { Close(); };
            title.Children.Add(close);
            title.MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs a)
            { if (!a.Handled && a.ButtonState == MouseButtonState.Pressed) try { DragMove(); } catch { } };

            // ---- 경고 (단축키 충돌 / PrintScreen 가로채기) ----
            warnText = new TextBlock
            {
                Foreground = Theme.BrText, FontSize = 12, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            };
            var warnStack = new StackPanel();
            warnStack.Children.Add(warnText);
            var fixBtn = Primary(L.T("PrintScreen 키 되찾기", "Reclaim PrintScreen Key"), delegate
            {
                PrintScreenKey.Release();
                App.ReloadHotkeys();
                RefreshWarnings();
                App.Notify(L.T("되찾았다", "Reclaimed"), L.T("이제 PrintScreen이 Money Shot으로 온다. 일부 앱은 다시 로그인해야 반영된다.", "PrintScreen now goes to Money Shot. Some apps need you to sign in again to pick it up."));
            });
            fixBtn.HorizontalAlignment = HorizontalAlignment.Left;
            warnStack.Children.Add(fixBtn);
            warnBox = new Border
            {
                Background = Theme.Alpha(Theme.Danger, 0x1A),
                BorderBrush = Theme.Alpha(Theme.Danger, 0x55),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 12, 14, 12),
                Margin = new Thickness(0, 0, 0, 14),
                Child = warnStack,
                Visibility = Visibility.Collapsed
            };

            // ---- 본문 ----
            var content = new StackPanel { Margin = new Thickness(18, 4, 18, 18) };
            content.Children.Add(warnBox);

            if (firstRun)
                content.Children.Add(Note(L.T("트레이에 자리 잡았다. 창을 닫아도 계속 떠 있고, 단축키는 지금 바로 먹는다.", "Money Shot now lives in the tray. It keeps running after you close this window, and the shortcuts work right away.")));

            content.Children.Add(Section(L.T("단축키", "Shortcuts")));
            content.Children.Add(HotkeyRow(L.T("영역 캡처", "Region Capture"), s.hkRegion));
            content.Children.Add(HotkeyRow(L.T("스크롤 캡처", "Scrolling Capture"), s.hkScroll));
            content.Children.Add(HotkeyRow(L.T("창 캡처", "Window Capture"), s.hkWindow));
            content.Children.Add(HotkeyRow(L.T("전체 화면", "Full Screen"), s.hkFullscreen));
            content.Children.Add(HotkeyRow(L.T("편집창 열기", "Open Editor"), s.hkEditor));

            content.Children.Add(Section(L.T("캡처한 다음", "After Capture")));
            content.Children.Add(Check(L.T("클립보드에 바로 복사", "Copy to clipboard"), s.copyToClipboard, delegate(bool v) { s.copyToClipboard = v; }));
            content.Children.Add(Check(L.T("우하단에 썸네일 띄우기", "Show thumbnail in the bottom-right corner"), s.showThumbnail, delegate(bool v) { s.showThumbnail = v; }));
            content.Children.Add(Slider_(L.T("썸네일이 머무는 시간", "Thumbnail stays for"), s.thumbnailSeconds, 2, 20, L.T("초", "s"),
                delegate(double v) { s.thumbnailSeconds = (int)v; }));
            content.Children.Add(Check(L.T("셔터음", "Shutter sound"), s.playSound, delegate(bool v) { s.playSound = v; }));

            content.Children.Add(Section(L.T("영역 선택", "Selection")));
            content.Children.Add(Check(L.T("마우스를 놓는 즉시 캡처", "Capture on mouse release"), s.captureOnRelease,
                delegate(bool v) { s.captureOnRelease = v; },
                L.T("끄면 놓은 뒤 핸들로 다듬고 Enter로 확정한다", "When off, adjust with the handles after releasing and press Enter to confirm")));
            content.Children.Add(Check(L.T("확대경 보이기", "Show magnifier"), s.showMagnifier, delegate(bool v) { s.showMagnifier = v; },
                L.T("커서 주변을 8배로 확대하고 색상값을 읽어준다", "Magnifies the area around the cursor 8x and shows the color value")));
            content.Children.Add(Check(L.T("마우스 커서도 함께 찍기", "Include mouse cursor"), s.includeCursor, delegate(bool v) { s.includeCursor = v; }));

            content.Children.Add(Section(L.T("스크롤 캡처", "Scrolling Capture")));
            content.Children.Add(Slider_(L.T("한 번에 굴릴 양", "Scroll per step"), s.scrollClicks, 1, 8, L.T("칸", " notches"),
                delegate(double v) { s.scrollClicks = (int)v; }));
            content.Children.Add(Slider_(L.T("굴린 뒤 기다리는 시간", "Wait after scrolling"), s.scrollDelayMs, 100, 900, "ms",
                delegate(double v) { s.scrollDelayMs = (int)(Math.Round(v / 20) * 20); },
                L.T("느린 페이지에서 잔상이 섞이면 늘려라", "Increase if slow pages leave ghosting in the result")));

            content.Children.Add(Section(L.T("저장", "Save")));
            content.Children.Add(FolderRow());
            content.Children.Add(NameRow());
            content.Children.Add(Check(L.T("캡처할 때마다 자동으로 보관", "Auto-save every capture"), s.autoSaveEvery,
                delegate(bool v) { s.autoSaveEvery = v; },
                L.T("저장 버튼과 무관하게 모든 캡처가 폴더에 쌓인다", "Every capture goes to the folder, whether or not you press Save")));

            content.Children.Add(Section(L.T("시작", "Startup")));
            content.Children.Add(Check(L.T("컴퓨터를 켜면 자동으로 (창 없이)", "Start with Windows (no window)"), Autostart.IsOn(),
                delegate(bool v) { s.autoStart = v; Autostart.Set(v); }));

            content.Children.Add(Section("언어 · Language"));
            content.Children.Add(LanguageRow());

            content.Children.Add(Section(L.T("정보", "About")));
            content.Children.Add(Note("Money Shot " + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(3) +
                                      L.T(" — 이 앱은 아무것도 수집하거나 보내지 않는다. AI 기능을 처음 쓸 때만 모델을 원래 배포처에서 받는다.", " — This app collects and sends nothing. AI models are downloaded from their original source only the first time you use an AI feature.")));
            var info = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            info.Children.Add(Ghost(L.T("오픈소스 고지", "Open-source notices"), delegate { new NoticesSheet { Owner = this }.ShowDialog(); }));
            content.Children.Add(info);

            content.Children.Add(new Border { Height = 10 });
            var foot = new StackPanel { Orientation = Orientation.Horizontal };
            foot.Children.Add(Ghost(L.T("종료", "Quit"), delegate { App.Quit(); }));
            foot.Children.Add(new Border { Width = 8 });
            foot.Children.Add(Ghost(L.T("저장 폴더 열기", "Open Save Folder"), delegate
            {
                try { Directory.CreateDirectory(s.saveDir); System.Diagnostics.Process.Start("explorer.exe", "\"" + s.saveDir + "\""); }
                catch { }
            }));
            content.Children.Add(foot);

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = content
            };

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(title, 0); root.Children.Add(title);
            Grid.SetRow(scroll, 1); root.Children.Add(scroll);

            root.Background = glass ? Theme.Alpha(Theme.Bg, 0x9E) : Theme.BrBg;
            if (!glass)
            {
                Content = new Border
                {
                    BorderBrush = Theme.BrBorder,
                    BorderThickness = new Thickness(1),
                    Child = root
                };
            }
            else Content = root;

            RefreshWarnings();
            PreviewKeyDown += delegate(object o, KeyEventArgs a)
            { if (a.Key == Key.Escape && capturing == null) Close(); };
        }

        void RefreshWarnings()
        {
            var problems = App.HotkeyProblems();
            bool snip = PrintScreenKey.TakenBySnippingTool();
            if (problems.Length == 0 && !snip) { warnBox.Visibility = Visibility.Collapsed; return; }

            string msg = "";
            if (snip)
                msg = L.T("윈도우가 PrintScreen 키를 자기 캡처 도구에 묶어두고 있다. 그대로 두면 이 앱의 PrintScreen 단축키가 먹지 않는다.", "Windows has bound the PrintScreen key to its own Snipping Tool. Until you change that, this app's PrintScreen shortcut won't work.");
            if (problems.Length > 0)
                msg += (msg.Length > 0 ? "\n\n" : "") + L.T("다른 앱이 선점한 단축키: ", "Shortcuts taken by other apps: ") + string.Join(", ", problems);
            warnText.Text = msg;
            warnBox.Visibility = Visibility.Visible;
        }

        // ---------- 조각 ----------

        // 자동 · 한국어 · English. 이미 그려진 화면은 못 바꾸니 다시 켤 때 적용된다.
        UIElement LanguageRow()
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
            string[] keys = { "auto", "ko", "en" };
            string[] names = { L.T("자동 (윈도우 따라)", "Auto (follow Windows)"), "한국어", "English" };
            var btns = new List<Border>();
            var note = new TextBlock { Foreground = Theme.BrMuted, FontSize = 11, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            Action paint = delegate
            {
                for (int i = 0; i < btns.Count; i++)
                {
                    bool on = s.language == keys[i];
                    btns[i].Background = on ? Theme.Alpha(Theme.Accent, 0x26) : Theme.Alpha(Colors.White, 0x08);
                    btns[i].BorderBrush = on ? Theme.BrAccent : Theme.BrBorder;
                    ((TextBlock)btns[i].Child).Foreground = on ? Theme.BrAccent : Theme.BrDim;
                }
            };
            for (int i = 0; i < keys.Length; i++)
            {
                int k = i;
                var b = new Border
                {
                    CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 4, 10, 5), Margin = new Thickness(0, 0, 4, 0),
                    BorderThickness = new Thickness(1), Cursor = Cursors.Hand, Child = new TextBlock { Text = names[i], FontSize = 12 }
                };
                b.MouseLeftButtonUp += delegate
                {
                    s.language = keys[k];
                    s.Save();
                    paint();
                    note.Text = "다시 켜면 적용된다 · Applies after restart";
                };
                btns.Add(b);
                row.Children.Add(b);
            }
            row.Children.Add(note);
            paint();
            return row;
        }

        static TextBlock Section(string t)
        {
            return new TextBlock
            {
                Text = t,
                Foreground = Theme.BrMuted,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(2, 18, 0, 8)
            };
        }

        static Border Note(string t)
        {
            return new Border
            {
                Background = Theme.Alpha(Theme.Accent, 0x14),
                BorderBrush = Theme.Alpha(Theme.Accent, 0x44),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 11, 14, 12),
                Margin = new Thickness(0, 0, 0, 4),
                Child = new TextBlock
                {
                    Text = t, Foreground = Theme.BrText, FontSize = 12.5, TextWrapping = TextWrapping.Wrap
                }
            };
        }

        static Border Card(UIElement child)
        {
            return new Border
            {
                Background = Theme.Alpha(Theme.Card, 0xB0),
                BorderBrush = Theme.BrBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(13, 10, 13, 11),
                Margin = new Thickness(0, 0, 0, 6),
                Child = child
            };
        }

        Border Check(string label, bool value, Action<bool> set) { return Check(label, value, set, null); }

        Border Check(string label, bool value, Action<bool> set, string hint)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var texts = new StackPanel();
            texts.Children.Add(new TextBlock { Text = label, Foreground = Theme.BrText, FontSize = 13 });
            if (hint != null)
                texts.Children.Add(new TextBlock
                {
                    Text = hint, Foreground = Theme.BrMuted, FontSize = 11,
                    Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap
                });
            Grid.SetColumn(texts, 0); g.Children.Add(texts);

            var sw = new Toggle(value);
            sw.VerticalAlignment = VerticalAlignment.Center;
            sw.Changed += delegate(bool v) { set(v); s.Save(); };
            Grid.SetColumn(sw, 1); g.Children.Add(sw);

            var card = Card(g);
            card.MouseLeftButtonUp += delegate { sw.Value = !sw.Value; };
            card.Cursor = Cursors.Hand;
            return card;
        }

        Border Slider_(string label, double value, double min, double max, string unit, Action<double> set)
        { return Slider_(label, value, min, max, unit, set, null); }

        Border Slider_(string label, double value, double min, double max, string unit, Action<double> set, string hint)
        {
            var st = new StackPanel();
            var top = new Grid();
            top.Children.Add(new TextBlock { Text = label, Foreground = Theme.BrText, FontSize = 13 });
            var val = new TextBlock
            {
                Text = (int)value + unit,
                Foreground = Theme.BrAccent, FontFamily = Theme.Mono, FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            top.Children.Add(val);
            st.Children.Add(top);

            var sl = new Slider
            {
                Minimum = min, Maximum = max, Value = value,
                Margin = new Thickness(0, 6, 0, 0),
                Foreground = Theme.BrAccent,
                IsSnapToTickEnabled = false
            };
            sl.ValueChanged += delegate
            {
                set(sl.Value);
                val.Text = (int)sl.Value + unit;
                s.Save();
            };
            st.Children.Add(sl);
            if (hint != null)
                st.Children.Add(new TextBlock
                {
                    Text = hint, Foreground = Theme.BrMuted, FontSize = 11,
                    Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap
                });
            return Card(st);
        }

        Border FolderRow()
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var texts = new StackPanel();
            texts.Children.Add(new TextBlock { Text = L.T("저장 폴더", "Save folder"), Foreground = Theme.BrText, FontSize = 13 });
            var pathText = new TextBlock
            {
                Text = s.saveDir, Foreground = Theme.BrMuted, FontSize = 11,
                Margin = new Thickness(0, 3, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            texts.Children.Add(pathText);
            Grid.SetColumn(texts, 0); g.Children.Add(texts);

            var btn = Ghost(L.T("변경", "Change"), delegate
            {
                var dlg = new System.Windows.Forms.FolderBrowserDialog();
                dlg.Description = L.T("캡처를 저장할 폴더", "Folder to save captures in");
                dlg.SelectedPath = Directory.Exists(s.saveDir) ? s.saveDir : "";
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    s.saveDir = dlg.SelectedPath; s.Save();
                    pathText.Text = s.saveDir;
                }
            });
            btn.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(btn, 1); g.Children.Add(btn);
            return Card(g);
        }

        // 파일명 규칙. .NET 날짜 서식을 그대로 쓰되 작은따옴표로 묶은 글자는 그대로 들어간다.
        //   yyyy-MM-dd_HHmmss   →  2026-09-11_143022
        //   yyyyMMdd_'money'    →  20260911_money
        Border NameRow()
        {
            var st = new StackPanel();
            st.Children.Add(new TextBlock { Text = L.T("파일 이름 규칙", "File name pattern"), Foreground = Theme.BrText, FontSize = 13 });

            var box = new TextBox
            {
                Text = s.fileNamePattern,
                Margin = new Thickness(0, 7, 0, 0),
                Background = Theme.Alpha(Theme.Bg, 0xCC),
                Foreground = Theme.BrText,
                CaretBrush = Theme.BrAccent,
                BorderBrush = Theme.BrBorder,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 6, 8, 7),
                FontFamily = Theme.Mono,
                FontSize = 12
            };
            st.Children.Add(box);

            var preview = new TextBlock
            {
                Foreground = Theme.BrMuted, FontSize = 11, FontFamily = Theme.Mono,
                Margin = new Thickness(2, 6, 0, 0), TextWrapping = TextWrapping.Wrap
            };
            st.Children.Add(preview);

            TextChangedEventHandler upd = delegate
            {
                string pat = box.Text;
                string shown;
                try
                {
                    shown = DateTime.Now.ToString(pat);
                    if (shown.Length == 0 || shown.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
                        throw new FormatException();
                    s.fileNamePattern = pat;
                    s.Save();
                    preview.Foreground = Theme.BrMuted;
                    preview.Text = "→ " + shown + ".png";
                }
                catch
                {
                    preview.Foreground = Theme.BrDanger;
                    preview.Text = L.T("이 규칙으로는 파일 이름을 만들 수 없다. 글자를 그대로 넣으려면 작은따옴표로 묶어라 — yyyyMMdd_'money'", "This pattern can't make a valid file name. Wrap literal text in single quotes — yyyyMMdd_'money'");
                }
            };
            box.TextChanged += upd;
            upd(null, null);
            return Card(st);
        }

        HotkeyDef capturing;
        Border HotkeyRow(string label, HotkeyDef def)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var lbl = new TextBlock
            {
                Text = label, Foreground = Theme.BrText, FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(lbl, 0); g.Children.Add(lbl);

            var keyText = new TextBlock
            {
                Text = def.ToString(),
                Foreground = Theme.BrAccent, FontFamily = Theme.Mono, FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            var keyBox = new Border
            {
                Background = Theme.Alpha(Theme.Accent, 0x18),
                BorderBrush = Theme.Alpha(Theme.Accent, 0x44),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(10, 5, 10, 6),
                Cursor = Cursors.Hand,
                Child = keyText
            };
            Grid.SetColumn(keyBox, 1); g.Children.Add(keyBox);

            KeyEventHandler grab = null;
            grab = delegate(object o, KeyEventArgs a)
            {
                a.Handled = true;
                var k = a.Key == Key.System ? a.SystemKey : a.Key;
                if (k == Key.LeftCtrl || k == Key.RightCtrl || k == Key.LeftShift || k == Key.RightShift
                    || k == Key.LeftAlt || k == Key.RightAlt || k == Key.LWin || k == Key.RWin) return;

                if (k != Key.Escape)
                {
                    uint mods = 0;
                    if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) mods |= Native.MOD_CONTROL;
                    if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) mods |= Native.MOD_SHIFT;
                    if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) mods |= Native.MOD_ALT;
                    def.mods = mods;
                    def.vk = (uint)KeyInterop.VirtualKeyFromKey(k);
                }
                PreviewKeyDown -= grab;
                capturing = null;
                keyText.Text = def.ToString();
                keyText.Foreground = Theme.BrAccent;
                keyBox.Background = Theme.Alpha(Theme.Accent, 0x18);
                s.Save();
                App.ReloadHotkeys();
                RefreshWarnings();
            };

            keyBox.MouseLeftButtonUp += delegate
            {
                if (capturing != null) return;
                capturing = def;
                keyText.Text = L.T("키를 눌러라", "Press a key");
                keyText.Foreground = Theme.BrDanger;
                keyBox.Background = Theme.Alpha(Theme.Danger, 0x1A);
                PreviewKeyDown += grab;
                Focus();
            };
            return Card(g);
        }

        static Border Primary(string text, Action act) { return Btn(text, act, true); }
        static Border Ghost(string text, Action act) { return Btn(text, act, false); }

        // WPF는 ControlTemplate 안에 완성된 Visual을 넣지 못하게 막는다(템플릿은 재사용 가능한
        // 설계도여야 한다). 버튼 모양을 직접 지정할 땐 Border에 마우스 이벤트를 다는 쪽이 간단하다.
        static Border Btn(string text, Action act, bool primary)
        {
            var bd = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(13, 7, 13, 8),
                Background = primary ? Theme.BrAccent : Theme.Alpha(Colors.White, 0x0E),
                BorderBrush = primary ? Brushes.Transparent : Theme.BrBorder,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = text, FontSize = 12.5,
                    Foreground = primary ? new SolidColorBrush(Theme.H("#08110D")) : Theme.BrText,
                    FontWeight = primary ? FontWeights.SemiBold : FontWeights.Normal
                }
            };
            bd.MouseEnter += delegate { bd.Opacity = 0.86; };
            bd.MouseLeave += delegate { bd.Opacity = 1; };
            bd.MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs a) { a.Handled = true; };
            bd.MouseLeftButtonUp += delegate { act(); };
            return bd;
        }

        protected override void OnClosed(EventArgs e)
        {
            s.Save();
            open_ = null;
            base.OnClosed(e);
        }
    }

    // 맥 계열 토글 스위치.
    public class Toggle : Grid
    {
        readonly Border track;
        readonly Border knob;
        readonly TranslateTransform slide = new TranslateTransform();
        bool value_;

        public event Action<bool> Changed;

        public bool Value
        {
            get { return value_; }
            set { if (value_ != value) { value_ = value; Animate(); if (Changed != null) Changed(value_); } }
        }

        public Toggle(bool initial)
        {
            value_ = initial;
            Width = 42; Height = 24;
            Cursor = Cursors.Hand;

            track = new Border { CornerRadius = new CornerRadius(12), Width = 42, Height = 24 };
            knob = new Border
            {
                Width = 18, Height = 18,
                CornerRadius = new CornerRadius(9),
                Background = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(3, 0, 0, 0),
                RenderTransform = slide,
                Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Opacity = 0.35, Color = Colors.Black }
            };
            Children.Add(track);
            Children.Add(knob);

            track.Background = value_ ? Theme.BrAccent : Theme.Alpha(Colors.White, 0x1E);
            slide.X = value_ ? 18 : 0;

            MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs a) { a.Handled = true; };
            // 뗌도 여기서 끝내야 한다. 안 그러면 스위치를 감싼 카드까지 올라가
            // 카드가 한 번 더 뒤집어 제자리로 돌아온다.
            MouseLeftButtonUp += delegate(object o, MouseButtonEventArgs a)
            {
                a.Handled = true;
                Value = !Value;
            };
        }

        void Animate()
        {
            track.Background = value_ ? Theme.BrAccent : Theme.Alpha(Colors.White, 0x1E);
            slide.BeginAnimation(TranslateTransform.XProperty,
                Theme.A(slide.X, value_ ? 18 : 0, 190, Theme.Spring(0.6)));
        }
    }
}
