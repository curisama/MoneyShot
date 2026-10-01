// Money Shot — 레이어와 합성
//
// 편집기의 그림은 레이어 여러 장을 아래에서 위로 겹친 결과다.
// 레이어마다 자기 픽셀과 마스크를 가진다.
//
// 마스크가 이 구조의 핵심이다. 지우개·마술봉·AI 누끼는 픽셀을 건드리지 않고
// 마스크에만 칠한다. 그래서 너무 많이 지웠으면 복구 브러시로 도로 칠하면 되고,
// AI가 머리카락을 잘라먹어도 그 자리만 되살릴 수 있다. 원본은 끝까지 남아 있다.
//
// 되돌리기는 버퍼를 복사해 쌓지 않는다. 그러면 한 획에 수 MB씩 불어난다.
// 대신 스냅샷은 버퍼를 '가리키기만' 하고, 누군가 그 버퍼를 고치려 할 때
// 비로소 복사한다(기록 시 복사). 획을 안 그으면 비용이 0이다.
using System;
using System.Collections.Generic;
using D = System.Drawing;

namespace MoneyShot
{
    public class Layer
    {
        public string Name;
        public int X, Y;                 // 문서 좌표에서의 왼쪽 위 모서리
        public double Opacity = 1.0;
        public bool Visible = true;
        public BlendMode Blend = BlendMode.Normal;
        public LayerFx Fx;
        public int GroupId;              // 0이면 그룹 밖. 그룹 속성은 Doc.Groups에 있다
        public Adjustment Adjust;        // 조정 레이어면 보정 설정. 픽셀은 투명하고, 마스크가 걸 자리를 정한다               // null이면 효과 없음. 고칠 때는 통째로 바꿔 끼운다(스냅샷과 나눠 쓰니까)

        // 효과를 입힌 그림을 들고 있다가, 픽셀·마스크·효과가 바뀌면 다시 만든다.
        Canvas32 fxCache;
        int fxMargin;
        object fxFor;
        D.Rectangle fxDirty;             // 브러시가 고친 자리(레이어 좌표). 다음 합성 때 거기만 다시 그린다

        Canvas32 px;                     // 레이어 픽셀 (레이어 크기)
        byte[] mask;                     // 255 보임 / 0 숨김. null이면 전부 보임
        bool pxShared, maskShared;

        public int W { get { return px.W; } }
        public int H { get { return px.H; } }
        public Canvas32 PixelsRead { get { return px; } }
        public byte[] MaskRead { get { return mask; } }
        public bool HasMask { get { return mask != null; } }

        public Layer(Canvas32 pixels, string name)
        {
            px = pixels;
            Name = name;
        }

        public D.Rectangle Bounds { get { return new D.Rectangle(X, Y, px.W, px.H); } }

        // 되돌리기용 스냅샷을 찍은 직후에 부른다.
        // 이 시점의 버퍼는 스냅샷과 공유되므로, 고치기 전에 복사해야 한다.
        public void MarkShared() { pxShared = true; maskShared = true; }

        // 합성이 쓸 그림: 효과가 없으면 null(픽셀을 그대로 쓴다).
        public Canvas32 Rendered(out int margin)
        {
            margin = 0;
            if (Fx == null || !Fx.Any || Adjust != null) return null;
            if (fxCache == null || !ReferenceEquals(fxFor, Fx))
            {
                fxCache = MoneyShot.Fx.Render(px, mask, Fx, out fxMargin);
                fxFor = Fx;
                fxDirty = D.Rectangle.Empty;
            }
            else if (!fxDirty.IsEmpty)
            {
                MoneyShot.Fx.Update(fxCache, px, mask, Fx, fxMargin, fxDirty);
                fxDirty = D.Rectangle.Empty;
            }
            margin = fxMargin;
            return fxCache;
        }

        public void Touch() { fxCache = null; }

        // 브러시처럼 좁은 자리만 고칠 때. 효과 그림을 통째로 버리지 않고 그 자리만 다시 그리게 표시한다.
        void Mark(D.Rectangle r)
        {
            if (fxCache == null) return;
            fxDirty = fxDirty.IsEmpty ? r : D.Rectangle.Union(fxDirty, r);
        }

        public Canvas32 EditPixels(D.Rectangle region)
        {
            var c = fxCache; var d = fxDirty;
            var p = EditPixels();
            fxCache = c; fxDirty = d;
            Mark(region);
            return p;
        }

        public byte[] EditMask(D.Rectangle region)
        {
            var c = fxCache; var d = fxDirty;
            bool had = mask != null;
            var m = EditMask();
            fxCache = had ? c : null;        // 마스크가 새로 생기면 모양 전체가 바뀐 셈이다
            fxDirty = d;
            Mark(region);
            return m;
        }

        public Canvas32 EditPixels()
        {
            fxCache = null;
            if (pxShared) { px = px.Clone(); pxShared = false; }
            return px;
        }

        public byte[] EditMask()
        {
            fxCache = null;
            if (mask == null)
            {
                mask = new byte[px.W * px.H];
                for (int i = 0; i < mask.Length; i++) mask[i] = 255;
                maskShared = false;
            }
            else if (maskShared)
            {
                mask = (byte[])mask.Clone();
                maskShared = false;
            }
            return mask;
        }

        public void ClearMask() { mask = null; maskShared = false; fxCache = null; }

        // 픽셀만 바꾼 같은 레이어 — 이름·자리·보이기·불투명도·블렌드·효과를 그대로 잇는다.
        // 새로 만들 때 하나씩 옮겨 적다 보면 꼭 빠뜨린다(변형이 블렌드와 효과를 잃은 적이 있다).
        public Layer Derive(Canvas32 pixels)
        {
            return new Layer(pixels, Name) { X = X, Y = Y, Visible = Visible, Opacity = Opacity, Blend = Blend, Fx = Fx,
                                             GroupId = GroupId, Adjust = Adjust };
        }

        // 스냅샷에 담고 되살릴 때 쓴다.
        public Layer ShallowCopy()
        {
            var c = new Layer(px, Name);
            c.mask = mask;
            c.X = X; c.Y = Y;
            c.Opacity = Opacity; c.Visible = Visible;
            c.Blend = Blend; c.Fx = Fx; c.GroupId = GroupId; c.Adjust = Adjust;
            c.pxShared = true; c.maskShared = true;
            return c;
        }

        // 패널에 보여줄 작은 미리보기. 통째로 줄이지 않고 띄엄띄엄 뽑는다 — 큰 그림에서도 즉시 나온다.
        public Canvas32 Thumb(int tw, int th)
        {
            var t = new Canvas32(Math.Max(1, tw), Math.Max(1, th));
            for (int y = 0; y < t.H; y++)
            {
                int sy = (int)((long)y * px.H / t.H);
                if (sy >= px.H) sy = px.H - 1;
                for (int x = 0; x < t.W; x++)
                {
                    int sx = (int)((long)x * px.W / t.W);
                    if (sx >= px.W) sx = px.W - 1;
                    int i = sy * px.W + sx;
                    int v = px.P[i];
                    int a = Canvas32.A(v);
                    if (mask != null) a = a * mask[i] / 255;
                    t.P[y * t.W + x] = Canvas32.Pack((byte)a, Canvas32.R(v), Canvas32.G(v), Canvas32.B(v));
                }
            }
            return t;
        }

        // 마스크를 알파에 구워 넣은 독립 이미지. 내보내기와 레이어 변형에 쓴다.
        public Canvas32 Flatten() { return Flatten(true); }

        // withOpacity=false: 불투명도는 굽지 않는다(레이어에 불투명도가 그대로 남는 곳 — 굽기·회전·뒤집기)
        public Canvas32 Flatten(bool withOpacity)
        {
            var outC = new Canvas32(px.W, px.H);
            for (int i = 0; i < px.P.Length; i++)
            {
                int v = px.P[i];
                int a = Canvas32.A(v);
                if (mask != null) a = a * mask[i] / 255;
                if (withOpacity && Opacity < 1.0) a = (int)(a * Opacity);
                outC.P[i] = Canvas32.Pack((byte)a, Canvas32.R(v), Canvas32.G(v), Canvas32.B(v));
            }
            return outC;
        }
    }

    // 레이어 한 장의 상태를 되돌리기 위해 기억해 두는 값.
    public class LayerState
    {
        public Layer Ref;
        public static LayerState Of(Layer l) { return new LayerState { Ref = l.ShallowCopy() }; }
    }

    public class Doc
    {
        public int W, H;
        public readonly List<Layer> Layers = new List<Layer>();
        public int Active;

        // 변형하는 동안 잠깐 빼둘 레이어. 그 장은 미리보기로 따로 그려지므로 합성에서 제외한다.
        public int HideIndex = -1;

        // 합쳐진 결과. 화면에 보여주는 건 언제나 이것이다.
        public Canvas32 Comp;

        // 그룹(폴더). 레이어는 GroupId로 가리킨다. 그룹의 보이기·불투명도는 안의 레이어에 곱해진다(통과 합성).
        public readonly List<GroupInfo> Groups = new List<GroupInfo>();
        int nextGroup = 1;

        public GroupInfo Group(int id)
        {
            if (id == 0) return null;
            foreach (var g in Groups) if (g.Id == id) return g;
            return null;
        }

        public GroupInfo NewGroup(string name)
        {
            var g = new GroupInfo { Id = nextGroup++, Name = name };
            Groups.Add(g);
            return g;
        }

        // 아무 레이어도 안 가리키는 그룹은 치운다
        public void PruneGroups()
        {
            Groups.RemoveAll(delegate(GroupInfo g)
            {
                foreach (var l in Layers) if (l.GroupId == g.Id) return false;
                return true;
            });
        }

        public Doc(Canvas32 first, int w, int h)
        {
            W = w; H = h;
            Comp = new Canvas32(w, h);
            Layers.Add(new Layer(first, L.T("배경", "Background")));
            Active = 0;
        }

        public Layer Current
        {
            get
            {
                if (Layers.Count == 0) return null;
                if (Active < 0) Active = 0;
                if (Active >= Layers.Count) Active = Layers.Count - 1;
                return Layers[Active];
            }
        }

        public D.Rectangle Full { get { return new D.Rectangle(0, 0, W, H); } }

        // 지정한 사각형만 다시 합친다.
        // 브러시를 끄는 동안 매번 전체를 합치면 큰 그림에서 바로 버벅인다.
        public void Composite(D.Rectangle dirty)
        {
            dirty.Intersect(Full);
            if (dirty.Width <= 0 || dirty.Height <= 0) return;

            for (int y = dirty.Top; y < dirty.Bottom; y++)
            {
                int row = y * W;
                for (int x = dirty.Left; x < dirty.Right; x++) Comp.P[row + x] = 0;
            }

            for (int li = 0; li < Layers.Count; li++)
            {
                var l = Layers[li];
                if (li == HideIndex) continue;
                if (!l.Visible || l.Opacity <= 0.003) continue;
                double gop = 1;
                var grp = Group(l.GroupId);
                if (grp != null) { if (!grp.Visible) continue; gop = grp.Opacity; if (gop <= 0.003) continue; }

                if (l.Adjust != null) { ApplyAdjustment(l, dirty, l.Opacity * gop); continue; }

                // 효과가 있으면 효과까지 입힌 그림(마스크도 이미 들어 있다)을 레이어 대신 쓴다.
                int fm;
                var fxImg = l.Rendered(out fm);
                Canvas32 lp;
                byte[] lm;
                int lx, ly;
                if (fxImg != null) { lp = fxImg; lm = null; lx = l.X - fm; ly = l.Y - fm; }
                else { lp = l.PixelsRead; lm = l.MaskRead; lx = l.X; ly = l.Y; }

                var r = new D.Rectangle(lx, ly, lp.W, lp.H);
                r.Intersect(dirty);
                if (r.Width <= 0 || r.Height <= 0) continue;

                double op = l.Opacity * gop;
                var mode = l.Blend;

                for (int y = r.Top; y < r.Bottom; y++)
                {
                    int dRow = y * W;
                    int sRow = (y - ly) * lp.W - lx;
                    for (int x = r.Left; x < r.Right; x++)
                    {
                        int sv = lp.P[sRow + x];
                        int sa = Canvas32.A(sv);
                        if (sa == 0) continue;
                        if (lm != null)
                        {
                            sa = sa * lm[sRow + x] / 255;
                            if (sa == 0) continue;
                        }
                        if (op < 1.0) sa = (int)(sa * op);
                        if (sa == 0) continue;

                        int di = dRow + x;
                        int dv = Comp.P[di];
                        int da = Canvas32.A(dv);

                        if (da == 0)
                        {
                            Comp.P[di] = Canvas32.Pack((byte)sa, Canvas32.R(sv), Canvas32.G(sv), Canvas32.B(sv));
                            continue;
                        }

                        int cr = Canvas32.R(sv), cg = Canvas32.G(sv), cb = Canvas32.B(sv);
                        if (mode != BlendMode.Normal)
                        {
                            // 섞은 색을 밑이 깔린 정도만큼 쓴다 (W3C: Cs' = (1-ab)·Cs + ab·B(Cb,Cs))
                            double mr, mg, mb;
                            Blend.Mix(mode, Canvas32.R(dv) / 255.0, Canvas32.G(dv) / 255.0, Canvas32.B(dv) / 255.0,
                                      cr / 255.0, cg / 255.0, cb / 255.0, out mr, out mg, out mb);
                            double ab = da / 255.0;
                            cr = (int)Math.Round((1 - ab) * cr + ab * mr * 255);
                            cg = (int)Math.Round((1 - ab) * cg + ab * mg * 255);
                            cb = (int)Math.Round((1 - ab) * cb + ab * mb * 255);
                        }

                        // 일반 알파 합성 (비프리멀티플라이드)
                        int outA = sa + da * (255 - sa) / 255;
                        if (outA == 0) { Comp.P[di] = 0; continue; }
                        int rr = (cr * sa + Canvas32.R(dv) * da * (255 - sa) / 255) / outA;
                        int gg = (cg * sa + Canvas32.G(dv) * da * (255 - sa) / 255) / outA;
                        int bb = (cb * sa + Canvas32.B(dv) * da * (255 - sa) / 255) / outA;
                        Comp.P[di] = Canvas32.Pack((byte)outA, (byte)Clamp(rr), (byte)Clamp(gg), (byte)Clamp(bb));
                    }
                }
            }
        }

        static int Clamp(int v) { return v < 0 ? 0 : (v > 255 ? 255 : v); }

        // 조정 레이어: 지금까지 쌓인 그림(Comp)의 이 자리에 보정을 걸고, 마스크 × 불투명도만큼 섞는다.
        // 보정은 픽셀마다 따로 계산되는 것만 쓰므로 고친 자리만 다시 해도 결과가 같다.
        void ApplyAdjustment(Layer l, D.Rectangle dirty, double op)
        {
            var r = new D.Rectangle(l.X, l.Y, l.W, l.H);
            r.Intersect(dirty);
            if (r.Width <= 0 || r.Height <= 0) return;
            var fn = l.Adjust.Fn;
            if (fn == null) return;
            var src = new Canvas32(r.Width, r.Height);
            for (int y = 0; y < r.Height; y++) Array.Copy(Comp.P, (r.Y + y) * W + r.X, src.P, y * r.Width, r.Width);
            var dst = new Canvas32(r.Width, r.Height);
            fn(src, dst);
            var m = l.MaskRead;
            for (int y = 0; y < r.Height; y++)
            {
                int crow = (r.Y + y) * W + r.X, lrow = (r.Y + y - l.Y) * l.W + (r.X - l.X);
                for (int x = 0; x < r.Width; x++)
                {
                    int a = src.P[y * r.Width + x], b = dst.P[y * r.Width + x];
                    if (a == b) continue;
                    int t = (int)((m != null ? m[lrow + x] : 255) * op);
                    if (t <= 0) continue;
                    if (t >= 255) { Comp.P[crow + x] = (a & unchecked((int)0xFF000000)) | (b & 0xFFFFFF); continue; }
                    int u = 255 - t;
                    Comp.P[crow + x] = (a & unchecked((int)0xFF000000)) |
                        (((Canvas32.R(a) * u + Canvas32.R(b) * t) / 255) << 16) |
                        (((Canvas32.G(a) * u + Canvas32.G(b) * t) / 255) << 8) |
                        ((Canvas32.B(a) * u + Canvas32.B(b) * t) / 255);
                }
            }
        }

        public void CompositeAll() { Composite(Full); }

        // ---------- 레이어 조작 ----------

        public Layer Add(Canvas32 pixels, string name, int x, int y)
        {
            var l = new Layer(pixels, name);
            l.X = x; l.Y = y;
            Layers.Add(l);
            Active = Layers.Count - 1;
            return l;
        }

        public void Remove(int index)
        {
            if (Layers.Count <= 1) return;           // 마지막 한 장은 남긴다
            Layers.RemoveAt(index);
            if (Active >= Layers.Count) Active = Layers.Count - 1;
        }

        public void Move(int index, int delta)
        {
            int to = index + delta;
            if (index < 0 || index >= Layers.Count || to < 0 || to >= Layers.Count) return;
            var l = Layers[index];
            Layers.RemoveAt(index);
            Layers.Insert(to, l);
            Active = to;
            // 옮긴 자리의 위아래가 같은 그룹이면 그 그룹에 들고, 제 그룹 식구와 더는 붙어 있지 않으면 그룹에서 나온다
            var above = to + 1 < Layers.Count ? Layers[to + 1] : null;
            var below = to - 1 >= 0 ? Layers[to - 1] : null;
            if (above != null && below != null && above.GroupId != 0 && above.GroupId == below.GroupId) l.GroupId = above.GroupId;
            else if (l.GroupId != 0 && (above == null || above.GroupId != l.GroupId) && (below == null || below.GroupId != l.GroupId)) l.GroupId = 0;
            PruneGroups();
        }

        // 보이는 레이어를 전부 합쳐 한 장으로. 내보내기와 판 전체를 바꾸는 변형에 쓴다.
        public Canvas32 Flatten()
        {
            CompositeAll();
            return Comp.Clone();
        }

        // ---------- 판 크기 바꾸기 ----------

        public void Crop(D.Rectangle r)
        {
            r.Intersect(Full);
            if (r.Width <= 0 || r.Height <= 0) return;
            foreach (var l in Layers) { l.X -= r.X; l.Y -= r.Y; }
            W = r.Width; H = r.Height;
            Comp = new Canvas32(W, H);
            CompositeAll();
        }

        // 여러 장을 한 장으로 눌러버린다 (변형 전에 부른다).
        public void FlattenInto(Canvas32 pixels, int w, int h)
        {
            Layers.Clear();
            Groups.Clear();
            Layers.Add(new Layer(pixels, L.T("배경", "Background")));
            Active = 0;
            W = w; H = h;
            Comp = new Canvas32(w, h);
            CompositeAll();
        }

        // ---------- 되돌리기 ----------

        public DocSnap Snapshot()
        {
            var s = new DocSnap { W = W, H = H, Active = Active };
            foreach (var g in Groups) s.Groups.Add(g.Clone());
            foreach (var l in Layers)
            {
                s.Layers.Add(l.ShallowCopy());
                l.MarkShared();          // 이제부터 이 버퍼를 고치려면 복사해야 한다
            }
            return s;
        }

        public void Restore(DocSnap s)
        {
            Layers.Clear();
            foreach (var l in s.Layers)
            {
                var c = l.ShallowCopy();
                c.MarkShared();
                Layers.Add(c);
            }
            Active = s.Active;
            Groups.Clear();
            foreach (var g in s.Groups) { Groups.Add(g.Clone()); if (g.Id >= nextGroup) nextGroup = g.Id + 1; }
            if (W != s.W || H != s.H) { W = s.W; H = s.H; Comp = new Canvas32(W, H); }
            CompositeAll();
        }
    }

    public class DocSnap
    {
        public int W, H, Active;
        public readonly List<Layer> Layers = new List<Layer>();
        public readonly List<GroupInfo> Groups = new List<GroupInfo>();
    }

    public class GroupInfo
    {
        public int Id;
        public string Name;
        public bool Visible = true;
        public double Opacity = 1;
        public bool Collapsed;
        public GroupInfo Clone() { return (GroupInfo)MemberwiseClone(); }
    }

    // 조정 레이어의 보정. 바꿀 때는 통째로 새로 만든다(스냅샷과 나눠 쓰니까).
    public class Adjustment
    {
        public readonly string Kind;      // levels · curves · huesat · balance · bw · invert
        public readonly object Settings;  // 그 보정의 설정 사본
        ToneFn fn;

        public Adjustment(string kind, object settings) { Kind = kind; Settings = settings; }

        public string Title
        {
            get
            {
                switch (Kind)
                {
                    case "levels": return L.T("레벨", "Levels");
                    case "curves": return L.T("커브", "Curves");
                    case "huesat": return L.T("색조/채도", "Hue/Saturation");
                    case "balance": return L.T("컬러 밸런스", "Color Balance");
                    case "bw": return L.T("흑백", "Black & White");
                    default: return L.T("반전", "Invert");
                }
            }
        }

        public ToneFn Fn
        {
            get
            {
                if (fn != null) return fn;
                switch (Kind)
                {
                    case "levels": fn = ((Tone.Levels)Settings).Fn(); break;
                    case "curves": fn = ((Tone.Curves)Settings).Fn(); break;
                    case "huesat": fn = ((Tone.HueSat)Settings).Fn(); break;
                    case "balance": fn = ((Tone.Balance)Settings).Fn(); break;
                    case "bw": fn = ((Tone.BlackWhite)Settings).Fn(); break;
                    default:
                        var t = new byte[256];
                        for (int i = 0; i < 256; i++) t[i] = (byte)(255 - i);
                        fn = delegate(Canvas32 a, Canvas32 b) { Tone.ApplyLut(a, b, t, t, t); };
                        break;
                }
                return fn;
            }
        }
    }
}
