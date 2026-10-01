// Money Shot — 물체 선택 (AI)
//
// 누른 물체만 고른다. 같은 자리를 다시 누르면 한 단계씩 넓어지고(얼굴 → 사람),
// Shift+클릭은 거기도 포함, Alt+클릭은 거기는 빼라는 뜻으로 점을 더해 다듬는다.
//
// 모델은 MobileSAM(Apache-2.0)을 ONNX로 내보낸 것(Acly/MobileSAM, MIT)이다.
// 그림 한 장을 한 번 읽어 두면(인코더, 약 0.5초) 점을 바꿔 다시 고르는 건(디코더) 몇십 ms라
// 그림이 바뀌기 전까지 읽은 결과를 들고 있는다.
// Compositor는 같은 일을 애플 Vision으로 하는데 윈도우엔 없어서 모델을 따로 쓴다.
// 추론 엔진은 누끼와 같은 것을 같이 쓴다(Cutout.LoadRuntime).
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using D = System.Drawing;

namespace MoneyShot
{
    public static class ObjectSelect
    {
        const string Base = "https://huggingface.co/Acly/MobileSAM/resolve/main/";
        const string EncoderFile = "mobile_sam_image_encoder.onnx";
        const string DecoderFile = "sam_mask_decoder_multi.onnx";
        const int Side = 1024;      // 인코더가 보는 긴 변
        const int Low = 256;        // 디코더가 내는 작은 마스크 한 변

        static object encoder, decoder;
        static float[,,,] embedding;
        static int embW, embH;
        static double scale;
        static long embKey;

        static string Dir { get { return Settings.ModelDir; } }

        public static bool Ready()
        {
            return Cutout.Ready() && File.Exists(Path.Combine(Dir, EncoderFile)) && File.Exists(Path.Combine(Dir, DecoderFile));
        }

        // 처음 쓸 때 모델을 받는다. 쓸 수 있게 되면 true.
        public static bool Prepare(Window owner, Action<string> status)
        {
            if (Ready()) return true;
            var ask = new ObjectAskSheet(!Cutout.Ready());
            ask.Owner = owner;
            if (ask.ShowDialog() != true) return false;

            var sheet = new ProgressSheet(L.T("물체 선택 준비", "Preparing Object Select"), L.T("준비하는 중…", "Preparing…"));
            sheet.Owner = owner;
            var prog = new Progress { Report = sheet.Report, Cancelled = delegate { return sheet.Cancelled; } };
            string err = null;
            var ui = owner.Dispatcher;
            var work = new Thread(delegate ()
            {
                try
                {
                    Directory.CreateDirectory(Dir);
                    try { System.Net.ServicePointManager.SecurityProtocol = (System.Net.SecurityProtocolType)3072; } catch { }
                    if (!Cutout.Ready()) Cutout.Fetch(prog);
                    string[] files = { EncoderFile, DecoderFile };
                    for (int i = 0; i < files.Length && !prog.Cancelled(); i++)
                    {
                        string dest = Path.Combine(Dir, files[i]);
                        if (File.Exists(dest)) continue;
                        Cutout.Download(Base + files[i], dest, prog, L.F("{0} 내려받는 중", "Downloading {0}", i == 0 ? L.T("그림 읽는 모델", "image encoder model") : L.T("고르는 모델", "selection model")),
                                        100.0 * i / files.Length, 100.0 * (i + 1) / files.Length);
                        if (!prog.Cancelled()) Cutout.Verify(dest);
                    }
                }
                catch (Exception ex) { err = Cutout.Explain(ex); }
                ui.BeginInvoke(new Action(delegate { try { sheet.Close(); } catch { } }));
            });
            work.IsBackground = true;
            work.Start();
            sheet.ShowDialog();
            if (err != null) { if (status != null) status(err); return false; }
            return Ready();
        }

        // ---------- 추론 ----------

        internal static object Session(string file)
        {
            var ort = Cutout.LoadRuntime();
            var t = ort.GetType("Microsoft.ML.OnnxRuntime.InferenceSession", true);
            return Activator.CreateInstance(t, new object[] { Path.Combine(Dir, file) });
        }

        // 이름 붙인 입력들로 돌리고, 원하는 출력들을 float 배열로 꺼낸다.
        internal static float[][] Run(object session, string[] names, Array[] values, string[] wants, int[] counts)
        {
            var ort = session.GetType().Assembly;
            var tNamed = ort.GetType("Microsoft.ML.OnnxRuntime.NamedOnnxValue", true);
            var create = tNamed.GetMethod("CreateFromTensor", BindingFlags.Public | BindingFlags.Static).MakeGenericMethod(typeof(float));
            var listType = typeof(List<>).MakeGenericType(tNamed);
            var inputs = (IList)Activator.CreateInstance(listType);
            for (int i = 0; i < names.Length; i++)
                inputs.Add(create.Invoke(null, new object[] { names[i], Cutout.MakeTensor(ort, values[i]) }));

            MethodInfo run = null;
            foreach (var m in session.GetType().GetMethods())
            {
                if (m.Name != "Run") continue;
                var ps = m.GetParameters();
                if (ps.Length == 1 && ps[0].ParameterType.IsAssignableFrom(listType)) { run = m; break; }
            }
            if (run == null) throw new MissingMethodException(L.T("InferenceSession.Run 을 찾지 못했다", "InferenceSession.Run not found"));
            object results = run.Invoke(session, new object[] { inputs });
            var outs = new float[wants.Length][];
            try
            {
                foreach (var r in (IEnumerable)results)
                {
                    var name = (string)r.GetType().GetProperty("Name").GetValue(r, null);
                    int w = Array.IndexOf(wants, name);
                    if (w < 0) continue;
                    var tensor = r.GetType().GetMethod("AsTensor").MakeGenericMethod(typeof(float)).Invoke(r, null);
                    var o = new float[counts[w]];
                    int k = 0;
                    foreach (float f in (IEnumerable)tensor) { if (k >= o.Length) break; o[k++] = f; }
                    outs[w] = o;
                }
            }
            finally { var d = results as IDisposable; if (d != null) d.Dispose(); }
            for (int i = 0; i < outs.Length; i++)
                if (outs[i] == null) throw new InvalidOperationException(L.F("모델이 {0} 를 내놓지 않았다", "The model didn't return {0}", wants[i]));
            return outs;
        }

        // 그림이 바뀌었는지 가늠하는 값. 다 훑지 않고 띄엄띄엄 본다.
        public static long KeyOf(Canvas32 c)
        {
            long h = c.W * 73856093L ^ c.H * 19349663L;
            int step = Math.Max(1, c.P.Length / 40000);
            for (int i = 0; i < c.P.Length; i += step) h = h * 31 + c.P[i];
            return h;
        }

        public static bool Has(long key) { return embedding != null && embKey == key; }

        // 그림을 읽어 둔다(무겁다). 투명한 곳은 흰 바탕으로 본다.
        public static void Encode(Canvas32 img, long key)
        {
            if (encoder == null) encoder = Session(EncoderFile);
            if (decoder == null) decoder = Session(DecoderFile);

            scale = (double)Side / Math.Max(img.W, img.H);
            int nw = Math.Max(1, (int)Math.Round(img.W * scale)), nh = Math.Max(1, (int)Math.Round(img.H * scale));
            Canvas32 small;
            using (var b = img.ToBitmap())
            using (var r = Cap.Resize(b, nw, nh))
                small = Canvas32.From(r);
            var input = new float[nh, nw, 3];
            for (int y = 0; y < nh; y++)
                for (int x = 0; x < nw; x++)
                {
                    int v = small.P[y * nw + x];
                    float a = Canvas32.A(v) / 255f;
                    input[y, x, 0] = Canvas32.R(v) * a + 255 * (1 - a);
                    input[y, x, 1] = Canvas32.G(v) * a + 255 * (1 - a);
                    input[y, x, 2] = Canvas32.B(v) * a + 255 * (1 - a);
                }
            var flat = Run(encoder, new[] { "input_image" }, new Array[] { input }, new[] { "image_embeddings" }, new[] { 256 * 64 * 64 })[0];
            var e = new float[1, 256, 64, 64];
            Buffer.BlockCopy(flat, 0, e, 0, flat.Length * 4);
            embedding = e;
            embW = img.W; embH = img.H; embKey = key;
        }

        // 점들(그림 좌표, positive=true면 포함)로 고를 수 있는 모양들을 작은 것부터 돌려준다(그림 크기, 255 = 고름).
        // 점이 하나면 모델이 내는 세 후보(부분·중간·전체) 중 스스로 자신 있는 것만, 여럿이면 모델이 하나로 정한 것.
        public static List<byte[]> Candidates(List<Point> pts, List<bool> positive)
        {
            if (embedding == null) throw new InvalidOperationException(L.T("그림을 먼저 읽어야 한다", "The image must be analyzed first"));
            int n = pts.Count;
            // 상자 없이 점만 줄 때는 끝에 빈 점(-1)을 하나 붙이는 게 SAM의 약속이다.
            var coords = new float[1, n + 1, 2];
            var labels = new float[1, n + 1];
            for (int i = 0; i < n; i++)
            {
                coords[0, i, 0] = (float)(pts[i].X * scale);
                coords[0, i, 1] = (float)(pts[i].Y * scale);
                labels[0, i] = positive[i] ? 1 : 0;
            }
            labels[0, n] = -1;
            var outs = Run(decoder,
                new[] { "image_embeddings", "point_coords", "point_labels", "mask_input", "has_mask_input", "orig_im_size" },
                new Array[] { embedding, coords, labels, new float[1, 1, Low, Low], new float[1], new float[] { embH, embW } },
                new[] { "low_res_masks", "iou_predictions" }, new[] { 4 * Low * Low, 4 });
            var low = outs[0]; var iou = outs[1];

            var pick = new List<int>();
            if (n > 1) pick.Add(0);
            else
            {
                for (int k = 1; k <= 3; k++) if (iou[k] >= 0.7f) pick.Add(k);
                if (pick.Count == 0) { int best = 1; for (int k = 2; k <= 3; k++) if (iou[k] > iou[best]) best = k; pick.Add(best); }
            }
            var masks = new List<byte[]>();
            var areas = new List<long>();
            foreach (int k in pick)
            {
                long area;
                var m = Upscale(low, k * Low * Low, out area);
                if (area == 0) continue;
                // 거의 같은 모양은 한 번만
                bool dup = false;
                foreach (var a in areas) if (Math.Abs(a - area) < Math.Max(64, area / 50)) dup = true;
                if (dup) continue;
                masks.Add(m); areas.Add(area);
            }
            // 작은 것부터
            for (int a = 0; a < masks.Count; a++)
                for (int b = a + 1; b < masks.Count; b++)
                    if (areas[b] < areas[a])
                    {
                        var t = masks[a]; masks[a] = masks[b]; masks[b] = t;
                        var u = areas[a]; areas[a] = areas[b]; areas[b] = u;
                    }
            return masks;
        }

        // 작은 마스크(1024 정사각형을 4배 줄인 것)를 원래 크기로 이중선형 확대. 0보다 크면 고른 곳.
        static byte[] Upscale(float[] low, int off, out long area)
        {
            var outm = new byte[embW * embH];
            double k = scale / 4;
            area = 0;
            for (int y = 0; y < embH; y++)
            {
                double v = (y + 0.5) * k - 0.5;
                int y0 = (int)Math.Floor(v); double ty = v - y0;
                int y1 = Math.Min(Low - 1, Math.Max(0, y0 + 1)); y0 = Math.Min(Low - 1, Math.Max(0, y0));
                for (int x = 0; x < embW; x++)
                {
                    double u = (x + 0.5) * k - 0.5;
                    int x0 = (int)Math.Floor(u); double tx = u - x0;
                    int x1 = Math.Min(Low - 1, Math.Max(0, x0 + 1)); x0 = Math.Min(Low - 1, Math.Max(0, x0));
                    double a = low[off + y0 * Low + x0] + (low[off + y0 * Low + x1] - low[off + y0 * Low + x0]) * tx;
                    double b = low[off + y1 * Low + x0] + (low[off + y1 * Low + x1] - low[off + y1 * Low + x0]) * tx;
                    double logit = a + (b - a) * ty;
                    // 0을 기준으로 가르고 경계 한두 픽셀만 부드럽게
                    double s = 0.5 + logit * 2;
                    byte vb = (byte)(s <= 0 ? 0 : s >= 1 ? 255 : (int)(s * 255));
                    outm[y * embW + x] = vb;
                    if (vb >= 128) area++;
                }
            }
            return outm;
        }
    }

    // 처음 물체 선택을 쓸 때 한 번 뜨는 안내.
    public class ObjectAskSheet : Sheet
    {
        public ObjectAskSheet(bool needRuntime) : base(L.T("물체 선택을 쓰려면 한 번 받아야 한다", "Object Select needs a one-time download"), 420)
        {
            Body.Children.Add(new TextBlock
            {
                Text = L.En
                       ? "Downloads a model that recognizes the object you click. About 45MB" +
                         (needRuntime ? ", plus the inference engine shared with Cutout (about 100MB)." : ".") +
                         " It's downloaded only once."
                       : "누른 물체를 알아보는 모델을 내려받는다. 약 45MB" +
                         (needRuntime ? "이고, 누끼와 같이 쓰는 추론 엔진(약 100MB)도 함께 받는다." : "이다.") +
                         " 한 번 받으면 다시 받지 않는다.",
                Foreground = Theme.BrText, FontSize = 12.5, TextWrapping = TextWrapping.Wrap
            });
            Body.Children.Add(new TextBlock
            {
                Text = L.T("받는 곳: ", "Source: ") + "huggingface.co/Acly/MobileSAM (MobileSAM · Segment Anything, MIT / Apache-2.0)" +
                       (needRuntime ? L.T("\n추론 엔진: ", "\nInference engine: ") + "github.com/microsoft/onnxruntime · nuget.org (MIT)" : ""),
                Foreground = Theme.BrMuted, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0)
            });
            Body.Children.Add(new TextBlock
            {
                Text = L.T("저장 위치: ", "Saved to: ") + Settings.ModelDir,
                Foreground = Theme.BrMuted, FontSize = 11, FontFamily = Theme.Mono,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0)
            });
            Body.Children.Add(Buttons(L.T("내려받기", "Download")));
        }
    }
}
