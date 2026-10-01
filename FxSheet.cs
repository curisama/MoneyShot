// Money Shot — 레이어 효과 창
//
// 여섯 효과를 한 창에서 켜고 끈다. 켠 것만 설정이 펼쳐진다. 움직이는 대로 레이어에 바로 보인다.
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace MoneyShot
{
    public class EffectsSheet : Sheet
    {
        public LayerFx Value;
        readonly Action<LayerFx> preview;
        readonly DispatcherTimer wait;

        public EffectsSheet(LayerFx init, Action<LayerFx> preview) : base(L.T("레이어 효과", "Layer Effects"), 340)
        {
            Value = init.Clone();
            this.preview = preview;
            wait = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
            wait.Tick += delegate { wait.Stop(); if (this.preview != null) this.preview(Value.Clone()); };

            var scroll = new ScrollViewer { MaxHeight = 560, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var list = new StackPanel();
            scroll.Content = list;
            Body.Children.Add(scroll);

            Section(list, L.T("그림자", "Drop Shadow"), Value.Shadow, delegate(bool on) { Value.Shadow = on; }, delegate(StackPanel p)
            {
                Swatches(p, Value.ShColor);
                Slider_(p, L.T("불투명도", "Opacity"), 0, 100, Value.ShOpacity * 100, "%", delegate(double v) { Value.ShOpacity = v / 100; });
                Slider_(p, L.T("빛 방향", "Angle"), 0, 360, Value.ShAngle, "°", delegate(double v) { Value.ShAngle = v; });
                Slider_(p, L.T("거리", "Distance"), 0, 200, Value.ShDistance, "px", delegate(double v) { Value.ShDistance = v; });
                Slider_(p, L.T("흐림", "Blur"), 0, 100, Value.ShBlur, "px", delegate(double v) { Value.ShBlur = v; });
            });
            Section(list, L.T("바깥 광선", "Outer Glow"), Value.OuterGlow, delegate(bool on) { Value.OuterGlow = on; }, delegate(StackPanel p)
            {
                Swatches(p, Value.OgColor);
                Slider_(p, L.T("불투명도", "Opacity"), 0, 100, Value.OgOpacity * 100, "%", delegate(double v) { Value.OgOpacity = v / 100; });
                Slider_(p, L.T("크기", "Size"), 1, 100, Value.OgSize, "px", delegate(double v) { Value.OgSize = v; });
            });
            Section(list, L.T("안쪽 그림자", "Inner Shadow"), Value.InnerShadow, delegate(bool on) { Value.InnerShadow = on; }, delegate(StackPanel p)
            {
                Swatches(p, Value.IsColor);
                Slider_(p, L.T("불투명도", "Opacity"), 0, 100, Value.IsOpacity * 100, "%", delegate(double v) { Value.IsOpacity = v / 100; });
                Slider_(p, L.T("빛 방향", "Angle"), 0, 360, Value.IsAngle, "°", delegate(double v) { Value.IsAngle = v; });
                Slider_(p, L.T("거리", "Distance"), 0, 100, Value.IsDistance, "px", delegate(double v) { Value.IsDistance = v; });
                Slider_(p, L.T("흐림", "Blur"), 0, 100, Value.IsBlur, "px", delegate(double v) { Value.IsBlur = v; });
            });
            Section(list, L.T("안쪽 광선", "Inner Glow"), Value.InnerGlow, delegate(bool on) { Value.InnerGlow = on; }, delegate(StackPanel p)
            {
                Swatches(p, Value.IgColor);
                Slider_(p, L.T("불투명도", "Opacity"), 0, 100, Value.IgOpacity * 100, "%", delegate(double v) { Value.IgOpacity = v / 100; });
                Slider_(p, L.T("크기", "Size"), 1, 100, Value.IgSize, "px", delegate(double v) { Value.IgSize = v; });
            });
            Section(list, L.T("색 덮기", "Color Overlay"), Value.Overlay, delegate(bool on) { Value.Overlay = on; }, delegate(StackPanel p)
            {
                Swatches(p, Value.OvColor);
                Slider_(p, L.T("불투명도", "Opacity"), 0, 100, Value.OvOpacity * 100, "%", delegate(double v) { Value.OvOpacity = v / 100; });
            });
            Section(list, L.T("테두리", "Stroke"), Value.Stroke, delegate(bool on) { Value.Stroke = on; }, delegate(StackPanel p)
            {
                Swatches(p, Value.StColor);
                Slider_(p, L.T("두께", "Size"), 1, 60, Value.StSize, "px", delegate(double v) { Value.StSize = v; });
                Slider_(p, L.T("불투명도", "Opacity"), 0, 100, Value.StOpacity * 100, "%", delegate(double v) { Value.StOpacity = v / 100; });
                var where = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
                Border outside = null, inside = null;
                Action paint = delegate
                {
                    Pill(outside, !Value.StInside); Pill(inside, Value.StInside);
                };
                outside = PillBtn(L.T("바깥", "Outside"), delegate { Value.StInside = false; paint(); Fire(); });
                inside = PillBtn(L.T("안쪽", "Inside"), delegate { Value.StInside = true; paint(); Fire(); });
                where.Children.Add(outside); where.Children.Add(inside);
                p.Children.Add(where);
                paint();
            });

            var foot = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            foot.Children.Add(Btn(L.T("취소", "Cancel"), false, delegate { DialogResult = false; }));
            foot.Children.Add(new Border { Width = 8 });
            foot.Children.Add(Btn(L.T("적용", "Apply"), true, delegate { DialogResult = true; }));
            Body.Children.Add(foot);
        }

        void Fire() { wait.Stop(); wait.Start(); }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            PlaceBesideOwner(60);
        }

        protected override void OnClosed(EventArgs e) { wait.Stop(); base.OnClosed(e); }

        // 머리줄(켜기 스위치 + 이름)과, 켰을 때만 펼쳐지는 설정
        void Section(StackPanel host, string name, bool on, Action<bool> set, Action<StackPanel> build)
        {
            var body = new StackPanel { Margin = new Thickness(4, 2, 0, 6), Visibility = on ? Visibility.Visible : Visibility.Collapsed };
            build(body);

            bool state = on;
            var knob = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(7), Background = Brushes.White };
            var track = new Border { Width = 30, Height = 18, CornerRadius = new CornerRadius(9), Padding = new Thickness(2), Child = knob };
            var title = new TextBlock { Text = name, FontSize = 13, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            Action paint = delegate
            {
                track.Background = state ? Theme.BrAccent : Theme.Alpha(Colors.White, 0x22);
                knob.HorizontalAlignment = state ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                title.Foreground = state ? Theme.BrText : Theme.BrDim;
                title.FontWeight = state ? FontWeights.SemiBold : FontWeights.Normal;
            };
            paint();
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(track); head.Children.Add(title);
            var hb = new Border { Child = head, Background = Brushes.Transparent, Cursor = Cursors.Hand, Padding = new Thickness(0, 9, 0, 5) };
            hb.MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs a) { a.Handled = true; };
            hb.MouseLeftButtonUp += delegate
            {
                state = !state; paint(); set(state);
                body.Visibility = state ? Visibility.Visible : Visibility.Collapsed;
                Fire();
            };
            host.Children.Add(hb);
            host.Children.Add(body);
            host.Children.Add(new Border { Height = 1, Background = Theme.Alpha(Colors.White, 0x10), Margin = new Thickness(0, 2, 0, 0) });
        }

        void Slider_(StackPanel host, string label, double min, double max, double init, string unit, Action<double> set)
        {
            var top = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            top.Children.Add(new TextBlock { Text = label, Foreground = Theme.BrDim, FontSize = 11.5 });
            var val = new TextBlock { Text = (int)Math.Round(init) + unit, Foreground = Theme.BrAccent, FontFamily = Theme.Mono, FontSize = 10.5, HorizontalAlignment = HorizontalAlignment.Right };
            top.Children.Add(val);
            host.Children.Add(top);
            var s = new Slider { Minimum = min, Maximum = max, Value = init, IsSnapToTickEnabled = true, TickFrequency = 1, Margin = new Thickness(0, 3, 0, 0), Foreground = Theme.BrAccent };
            s.ValueChanged += delegate { val.Text = (int)Math.Round(s.Value) + unit; set(s.Value); Fire(); };
            host.Children.Add(s);
        }

        static readonly string[] Presets = { "#000000", "#FFFFFF", "#34D399", "#F43F5E", "#FBBF24", "#3B82F6", "#A855F7", "#6B7280" };

        // 자주 쓰는 색 몇 개와 직접 넣는 칸
        void Swatches(StackPanel host, FxColor target)
        {
            var row = new WrapPanel { Margin = new Thickness(0, 4, 0, 2) };
            var chips = new List<Border>();
            var hex = new TextBox
            {
                Width = 74, Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(5, 2, 5, 3),
                Background = Theme.Alpha(Theme.Card, 0xCC), Foreground = Theme.BrText, CaretBrush = Theme.BrAccent,
                BorderBrush = Theme.BrBorder, BorderThickness = new Thickness(1), FontFamily = Theme.Mono, FontSize = 11,
                Text = string.Format("#{0:X2}{1:X2}{2:X2}", target.R, target.G, target.B), VerticalAlignment = VerticalAlignment.Center
            };
            Action paint = delegate
            {
                foreach (var c in chips)
                {
                    var col = ((SolidColorBrush)c.Background).Color;
                    bool on = col.R == target.R && col.G == target.G && col.B == target.B;
                    c.BorderBrush = on ? Theme.BrAccent : Theme.Alpha(Colors.White, 0x30);
                    c.BorderThickness = new Thickness(on ? 2 : 1);
                }
            };
            foreach (var h in Presets)
            {
                var col = Theme.H(h);
                var chip = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 0, 4, 0), Background = new SolidColorBrush(col), Cursor = Cursors.Hand };
                chip.MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs a) { a.Handled = true; };
                chip.MouseLeftButtonUp += delegate
                {
                    target.R = col.R; target.G = col.G; target.B = col.B;
                    hex.Text = h; paint(); Fire();
                };
                chips.Add(chip);
                row.Children.Add(chip);
            }
            hex.TextChanged += delegate
            {
                try
                {
                    var t = hex.Text.Trim();
                    if (!t.StartsWith("#")) t = "#" + t;
                    if (t.Length != 7) return;
                    var col = Theme.H(t);
                    if (col.R == target.R && col.G == target.G && col.B == target.B) return;
                    target.R = col.R; target.G = col.G; target.B = col.B;
                    paint(); Fire();
                }
                catch { }
            };
            row.Children.Add(hex);
            host.Children.Add(row);
            paint();
        }

        Border PillBtn(string t, Action act)
        {
            var b = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 4, 12, 5), Margin = new Thickness(0, 0, 4, 0), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, Child = new TextBlock { Text = t, FontSize = 11.5 } };
            b.MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs a) { a.Handled = true; };
            b.MouseLeftButtonUp += delegate { act(); };
            return b;
        }

        static void Pill(Border b, bool on)
        {
            if (b == null) return;
            b.Background = on ? Theme.Alpha(Theme.Accent, 0x26) : Theme.Alpha(Colors.White, 0x08);
            b.BorderBrush = on ? Theme.BrAccent : Theme.BrBorder;
            ((TextBlock)b.Child).Foreground = on ? Theme.BrAccent : Theme.BrDim;
        }
    }
}
