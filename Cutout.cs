// Money Shot — 누끼 (AI 배경 제거)
//
// 모델과 추론 런타임은 exe에 넣지 않는다. 평소엔 필요 없는 50MB를 늘 지고 다닐 이유가 없고,
// 단일 exe 배포도 깨진다. 그래서 처음 누끼를 누를 때만 내려받아 %APPDATA%에 캐시한다.
//
// 런타임(ONNX Runtime)은 빌드 시점에 참조할 수 없으므로 리플렉션으로 부른다.
// 타입 이름과 시그니처가 버전에 묶이므로 버전을 고정해서 받는다.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MoneyShot
{
    // 진행 상황을 어디에 보여줄지는 부르는 쪽이 정한다.
    // 편집창은 진행 창에, 명령줄 점검은 콘솔에 찍는다.
    public class Progress
    {
        public Action<string, double> Report = delegate { };
        public Func<bool> Cancelled = delegate { return false; };
    }

    public static class Cutout
    {
        // 고정 버전. 리플렉션으로 부르므로 마음대로 올리면 안 된다.
        const string OrtVersion = "1.16.3";
        const string ModelName = "silueta.onnx";
        const string ModelUrl = "https://github.com/danielgatis/rembg/releases/download/v0.0.0/silueta.onnx";
        const int Side = 320;                    // u2net 계열 입력 크기

        // 받아야 할 것들. Entry가 null이면 파일을 그대로 쓰고, 아니면 zip에서 그 경로만 꺼낸다.
        struct Asset
        {
            public string Url, Entry, Out, Label;
            public Asset(string url, string entry, string outName, string label)
            { Url = url; Entry = entry; Out = outName; Label = label; }
        }

        const string NugetBase = "https://api.nuget.org/v3-flatcontainer/";

        static readonly Asset[] Assets =
        {
            new Asset(ModelUrl, null, ModelName, L.T("AI 모델", "AI model")),

            // 네이티브 추론 엔진.
            // 누겟(Microsoft.ML.OnnxRuntime)은 ARM 바이너리까지 담고 있어 108MB인데,
            // GitHub 릴리스 zip은 x64만 들어 있어 절반이다.
            new Asset("https://github.com/microsoft/onnxruntime/releases/download/v" + OrtVersion +
                      "/onnxruntime-win-x64-" + OrtVersion + ".zip",
                      "onnxruntime-win-x64-" + OrtVersion + "/lib/onnxruntime.dll",
                      "onnxruntime.dll", L.T("추론 엔진", "Inference engine")),

            // 관리형 래퍼와 .NET Framework에서 필요한 보조 어셈블리들
            new Asset(NugetBase + "microsoft.ml.onnxruntime.managed/" + OrtVersion +
                      "/microsoft.ml.onnxruntime.managed." + OrtVersion + ".nupkg",
                      "lib/netstandard2.0/Microsoft.ML.OnnxRuntime.dll",
                      "Microsoft.ML.OnnxRuntime.dll", L.T("런타임", "Runtime")),
            new Asset(NugetBase + "system.memory/4.5.5/system.memory.4.5.5.nupkg",
                      "lib/netstandard2.0/System.Memory.dll",
                      "System.Memory.dll", L.T("런타임", "Runtime")),
            new Asset(NugetBase + "system.runtime.compilerservices.unsafe/6.0.0/system.runtime.compilerservices.unsafe.6.0.0.nupkg",
                      "lib/netstandard2.0/System.Runtime.CompilerServices.Unsafe.dll",
                      "System.Runtime.CompilerServices.Unsafe.dll", L.T("런타임", "Runtime")),
            new Asset(NugetBase + "system.buffers/4.5.1/system.buffers.4.5.1.nupkg",
                      "lib/netstandard2.0/System.Buffers.dll",
                      "System.Buffers.dll", L.T("런타임", "Runtime")),
            new Asset(NugetBase + "system.numerics.vectors/4.5.0/system.numerics.vectors.4.5.0.nupkg",
                      "lib/netstandard2.0/System.Numerics.Vectors.dll",
                      "System.Numerics.Vectors.dll", L.T("런타임", "Runtime")),
        };

        // 받은 파일의 SHA-256. 이 값과 다르면 쓰지 않는다 — 특히 DLL은 이 앱이 직접 불러 실행하는 코드라
        // 받는 길이 오염되면 남의 코드가 돈다. 모델이 바뀌어도 결과를 믿을 수 없으니 함께 막는다.
        // 값은 2026-10-01 각 원 배포처에서 받은 파일로 쟀다(두 기기에서 같았다).
        internal static readonly Dictionary<string, string> Sha = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "silueta.onnx", "75DA6C8D2F8096EC743D071951BE73B4A8BC7B3E51D9A6625D63644F90FFEEDB" },
            { "onnxruntime.dll", "6135D3C08003AFB23B7C8997BED44D15D860E9BF8408AAA87E44FC8E4FE2FA48" },
            { "Microsoft.ML.OnnxRuntime.dll", "9A18A07E2CA8811A222517CBE2AA94611ED19129A2A659DE39F6BC1A09FB29C4" },
            { "System.Memory.dll", "11590D8BB3B12F29F4202B3EF8593229A5CD6DEBB61E76CBA9AC5493A82EE382" },
            { "System.Runtime.CompilerServices.Unsafe.dll", "01748200F2400C742AA689F1F5101BD6298EFDFD92C00C18F4FA473847235BA9" },
            { "System.Buffers.dll", "C65FFF603B283DC966D1A8B730C11D5E5E750E8021BD24640612F6CC3F2C6FB7" },
            { "System.Numerics.Vectors.dll", "17924E5DC87E0D6229D2DD0BCFC1FDFABD820901B13A68BAA89FCB80C4D1A67F" },
            { "mobile_sam_image_encoder.onnx", "580F5FB648EA1062C0AABC26217AED56921985F03F0CBBD852BBA81D760CC749" },
            { "sam_mask_decoder_multi.onnx", "8976B90A87BA50A6A72217A5FF994F7D25CE16F2229FCC1ED259E1294C622FFE" },
            { "birefnet_lite.onnx", "5600024376F572A557870A5EB0AFB1E5961636BEF4E1E22132025467D0F03333" },
        };

        // 해시가 다르면 지우고 멈춘다
        internal static void Verify(string path)
        {
            string want;
            if (!Sha.TryGetValue(Path.GetFileName(path), out want)) return;
            string got;
            using (var f = File.OpenRead(path))
            using (var h = System.Security.Cryptography.SHA256.Create())
                got = BitConverter.ToString(h.ComputeHash(f)).Replace("-", "");
            if (string.Equals(got, want, StringComparison.OrdinalIgnoreCase)) return;
            try { File.Delete(path); } catch { }
            throw new InvalidDataException(L.T("받은 파일이 예상과 달라 지웠다 (" + Path.GetFileName(path) + "). 다시 시도해도 같으면 받는 곳이 바뀐 것이다.",
                                                "A downloaded file did not match the expected checksum and was deleted (" + Path.GetFileName(path) + "). If this keeps happening, the source has changed."));
        }

        static bool runtimeChecked;

        static bool resolverHooked;
        static object session;              // InferenceSession (리플렉션으로만 다룬다)
        static string inputName;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr LoadLibrary(string path);

        static string Dir { get { return Settings.ModelDir; } }
        static string ModelPath { get { return Path.Combine(Dir, ModelName); } }

        internal static bool Ready()
        {
            foreach (var a in Assets)
                if (!File.Exists(Path.Combine(Dir, a.Out))) return false;
            return true;
        }

        // ---------- 정밀 누끼 (BiRefNet) ----------
        //
        // silueta(44MB)는 빠르지만(약 0.7초) 머리카락·털과 복잡한 배경에 약하다. BiRefNet(MIT)은 공개 모델 중
        // 경계가 가장 좋은 축인데 무겁다 — 경량판도 224MB, CPU에서 장당 4~9초. 그래서 따로 두고 처음 쓸 때만 받는다.
        // 32비트판을 쓴다: 16비트판이 절반 크기지만 CPU에서는 오히려 두 배 가까이 느렸다(8.3초 대 4.8초).
        const string PreciseName = "birefnet_lite.onnx";
        const string PreciseUrl = "https://huggingface.co/onnx-community/BiRefNet_lite-ONNX/resolve/main/onnx/model.onnx";
        const int PreciseSide = 1024;
        static object preciseSession;

        static string PrecisePath { get { return Path.Combine(Dir, PreciseName); } }
        public static bool PreciseReady() { return Ready() && File.Exists(PrecisePath); }

        static byte[] InferPrecise(Canvas32 img)
        {
            if (preciseSession == null) preciseSession = ObjectSelect.Session(PreciseName);
            // 비율을 무시하고 1024 정사각형으로 편다(모델이 그렇게 배웠다). ImageNet 평균·표준편차로 정규화.
            var small = Resize(img, PreciseSide, PreciseSide);
            var input = new float[1, 3, PreciseSide, PreciseSide];
            float[] mean = { 0.485f, 0.456f, 0.406f };
            float[] std = { 0.229f, 0.224f, 0.225f };
            for (int y = 0; y < PreciseSide; y++)
                for (int x = 0; x < PreciseSide; x++)
                {
                    int v = small.P[y * PreciseSide + x];
                    // 투명한 곳은 흰 바탕으로 본다
                    float a = Canvas32.A(v) / 255f;
                    input[0, 0, y, x] = ((Canvas32.R(v) * a + 255 * (1 - a)) / 255f - mean[0]) / std[0];
                    input[0, 1, y, x] = ((Canvas32.G(v) * a + 255 * (1 - a)) / 255f - mean[1]) / std[1];
                    input[0, 2, y, x] = ((Canvas32.B(v) * a + 255 * (1 - a)) / 255f - mean[2]) / std[2];
                }
            var outp = ObjectSelect.Run(preciseSession, new[] { "input_image" }, new Array[] { input },
                                        new[] { "output_image" }, new[] { PreciseSide * PreciseSide })[0];
            for (int i = 0; i < outp.Length; i++) outp[i] = 1f / (1f + (float)Math.Exp(-outp[i]));
            return UpscaleMask(outp, PreciseSide, PreciseSide, img.W, img.H);
        }

        // 편집기에서 호출한다. keep은 픽셀마다 "남길 정도" 0~255.
        public static void Run(Window owner, Canvas32 img, Action<byte[], string> done)
        {
            Run(owner, img, false, done);
        }

        public static void Run(Window owner, Canvas32 img, bool precise, Action<byte[], string> done)
        {
            if (precise ? !PreciseReady() : !Ready())
            {
                var ask = new AskSheet(precise, !Ready());
                ask.Owner = owner;
                if (ask.ShowDialog() != true) { done(null, L.T("누끼를 취소했다", "Cutout canceled")); return; }
            }

            var sheet = new ProgressSheet(precise ? L.T("정밀 누끼 따는 중", "Precise cutout in progress") : L.T("누끼 따는 중", "Cutout in progress"), L.T("준비하는 중…", "Preparing…"));
            sheet.Owner = owner;
            var prog = new Progress
            {
                Report = sheet.Report,
                Cancelled = delegate { return sheet.Cancelled; }
            };

            var ui = owner.Dispatcher;
            var work = new Thread(delegate ()
            {
                byte[] keep = null;
                string err = null;
                try { keep = RunHeadless(img, prog, precise); }
                catch (Exception ex) { err = Explain(ex); }

                ui.BeginInvoke(new Action(delegate
                {
                    try { sheet.Close(); } catch { }
                    done(keep, keep == null ? (err ?? L.T("누끼를 따지 못했다", "Couldn't cut out the subject")) : null);
                }));
            });
            work.IsBackground = true;
            work.SetApartmentState(ApartmentState.MTA);
            work.Start();

            sheet.ShowDialog();
        }

        // 창 없이 도는 본체. 편집창과 명령줄 점검이 똑같이 이 길을 탄다.
        public static byte[] RunHeadless(Canvas32 img, Progress prog) { return RunHeadless(img, prog, false); }

        public static byte[] RunHeadless(Canvas32 img, Progress prog, bool precise)
        {
            if (prog == null) prog = new Progress();
            if (!Ready()) Fetch(prog);
            if (prog.Cancelled()) return null;
            if (precise)
            {
                if (!File.Exists(PrecisePath))
                {
                    try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; } catch { }
                    Download(PreciseUrl, PrecisePath, prog, L.T("정밀 모델 내려받는 중", "Downloading precise model"), 0, 100);
                    if (prog.Cancelled()) return null;
                    Verify(PrecisePath);
                }
                prog.Report(L.T("정밀 모델을 읽는 중…", "Loading precise model…"), -1);
                Cutout.LoadRuntime();
                prog.Report(L.T("분석하는 중… (몇 초 걸린다)", "Analyzing… (takes a few seconds)"), -1);
                return InferPrecise(img);
            }

            prog.Report(L.T("모델을 읽는 중…", "Loading model…"), -1);
            EnsureSession();
            prog.Report(L.T("분석하는 중…", "Analyzing…"), -1);
            return Infer(img);
        }

        internal static string Explain(Exception ex)
        {
            var e = ex;
            while (e.InnerException != null) e = e.InnerException;
            if (e is WebException || e is System.Net.Sockets.SocketException)
                return L.T("내려받지 못했다 — 인터넷 연결을 확인해라. 마술봉과 배경 지우개는 그대로 쓸 수 있다.", "Download failed — check your internet connection. Magic Wand and Background Eraser still work.");
            if (e is InvalidDataException) return e.Message;
            if (e is BadImageFormatException)
                return L.T("런타임이 이 시스템과 맞지 않는다 (64비트 필요).", "The runtime doesn't match this system (64-bit required).");
            return L.T("누끼 실패: ", "Cutout failed: ") + e.Message;
        }

        // ---------- 내려받기 ----------

        internal static void Fetch(Progress prog)
        {
            Directory.CreateDirectory(Dir);
            // .NET 4.x 기본값은 TLS 1.0이라 요즘 서버에 그대로는 못 붙는다.
            try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; } catch { }

            // 이미 있는 건 건너뛴다. 중간에 끊겨도 다음에 이어서 받는 셈이 된다.
            var todo = new List<Asset>();
            foreach (var a in Assets)
                if (!File.Exists(Path.Combine(Dir, a.Out))) todo.Add(a);

            for (int i = 0; i < todo.Count; i++)
            {
                if (prog.Cancelled()) return;
                var a = todo[i];
                double from = 100.0 * i / todo.Count, to = 100.0 * (i + 1) / todo.Count;
                string dest = Path.Combine(Dir, a.Out);

                if (a.Entry == null)
                {
                    Download(a.Url, dest, prog, L.F("{0} 내려받는 중", "Downloading {0}", a.Label), from, to);
                    if (prog.Cancelled()) return;
                    Verify(dest);
                    continue;
                }

                string tmp = Path.Combine(Dir, "_dl.zip");
                Download(a.Url, tmp, prog, L.F("{0} 내려받는 중", "Downloading {0}", a.Label), from, to);
                if (prog.Cancelled()) return;

                prog.Report(L.F("{0} 푸는 중…", "Extracting {0}…", a.Label), to);
                using (var zip = ZipFile.OpenRead(tmp))
                {
                    var entry = zip.GetEntry(a.Entry);
                    if (entry == null)
                        throw new FileNotFoundException(L.F("내려받은 묶음 안에 {0} 가 없다", "{0} is missing from the downloaded package", a.Entry));
                    using (var src = entry.Open())
                    using (var dst = File.Create(dest))
                        src.CopyTo(dst);
                }
                try { File.Delete(tmp); } catch { }
                Verify(dest);
            }
        }

        internal static void Download(string url, string dest, Progress prog, string label, double from, double to)
        {
            string tmp = dest + ".part";
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "MoneyShot";
            req.AllowAutoRedirect = true;
            req.Timeout = 30000;
            req.ReadWriteTimeout = 60000;

            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var s = resp.GetResponseStream())
            using (var f = File.Create(tmp))
            {
                long total = resp.ContentLength;
                long got = 0;
                var buf = new byte[81920];
                int r;
                while ((r = s.Read(buf, 0, buf.Length)) > 0)
                {
                    if (prog.Cancelled()) { f.Close(); try { File.Delete(tmp); } catch { } return; }
                    f.Write(buf, 0, r);
                    got += r;
                    double pct = total > 0 ? from + (to - from) * got / total : -1;
                    prog.Report(label + "   " + (got / 1048576) + " / " +
                                (total > 0 ? (total / 1048576).ToString() : "?") + " MB", pct);
                }
            }
            if (File.Exists(dest)) File.Delete(dest);
            File.Move(tmp, dest);
        }

        // ---------- 추론 ----------

        static Assembly runtime;

        // 추론 엔진을 불러 둔다. 누끼와 물체 선택이 같이 쓴다.
        internal static Assembly LoadRuntime()
        {
            if (runtime != null) return runtime;
            // 이 앱이 실행할 코드다. 예전 버전이 검증 없이 받아 둔 것일 수도 있으니 불러오기 전에 한 번 확인한다.
            if (!runtimeChecked)
            {
                foreach (var dll in new[] { "onnxruntime.dll", "Microsoft.ML.OnnxRuntime.dll", "System.Memory.dll",
                                            "System.Runtime.CompilerServices.Unsafe.dll", "System.Buffers.dll", "System.Numerics.Vectors.dll" })
                    Verify(Path.Combine(Dir, dll));
                runtimeChecked = true;
            }
            if (!resolverHooked)
            {
                // 관리형 DLL은 exe 옆이 아니라 캐시 폴더에 있다.
                AppDomain.CurrentDomain.AssemblyResolve += delegate(object s, ResolveEventArgs a)
                {
                    string name = new AssemblyName(a.Name).Name + ".dll";
                    string p = Path.Combine(Dir, name);
                    return File.Exists(p) ? Assembly.LoadFrom(p) : null;
                };
                resolverHooked = true;
            }

            // 네이티브 onnxruntime.dll을 먼저 붙들어야 관리형 래퍼가 찾을 수 있다.
            IntPtr h = LoadLibrary(Path.Combine(Dir, "onnxruntime.dll"));
            if (h == IntPtr.Zero)
                throw new DllNotFoundException(L.T("onnxruntime.dll 을 불러오지 못했다", "Couldn't load onnxruntime.dll"));

            runtime = Assembly.LoadFrom(Path.Combine(Dir, "Microsoft.ML.OnnxRuntime.dll"));
            return runtime;
        }

        static void EnsureSession()
        {
            if (session != null) return;
            var ort = LoadRuntime();
            var tSession = ort.GetType("Microsoft.ML.OnnxRuntime.InferenceSession", true);
            session = Activator.CreateInstance(tSession, new object[] { ModelPath });

            var meta = tSession.GetProperty("InputMetadata").GetValue(session, null) as IDictionary;
            foreach (DictionaryEntry e in meta) { inputName = (string)e.Key; break; }
            if (inputName == null) throw new InvalidOperationException(L.T("모델 입력 이름을 읽지 못했다", "Couldn't read the model's input name"));
        }

        static byte[] Infer(Canvas32 img)
        {
            var ort = session.GetType().Assembly;
            var tNamed = ort.GetType("Microsoft.ML.OnnxRuntime.NamedOnnxValue", true);

            // 1) 320×320으로 줄이고 정규화해 CHW 4차원 배열로 만든다.
            var small = Resize(img, Side, Side);
            var input = new float[1, 3, Side, Side];
            float[] mean = { 0.485f, 0.456f, 0.406f };
            float[] std = { 0.229f, 0.224f, 0.225f };

            // rembg와 같게 최대 채널값으로 나눈다 (보통 255).
            float maxV = 1e-6f;
            for (int i = 0; i < small.P.Length; i++)
            {
                int v = small.P[i];
                maxV = Math.Max(maxV, Math.Max(Canvas32.R(v), Math.Max(Canvas32.G(v), Canvas32.B(v))));
            }
            for (int y = 0; y < Side; y++)
                for (int x = 0; x < Side; x++)
                {
                    int v = small.At(x, y);
                    input[0, 0, y, x] = (Canvas32.R(v) / maxV - mean[0]) / std[0];
                    input[0, 1, y, x] = (Canvas32.G(v) / maxV - mean[1]) / std[1];
                    input[0, 2, y, x] = (Canvas32.B(v) / maxV - mean[2]) / std[2];
                }

            // 2) 텐서 → NamedOnnxValue → Run
            //
            //    DenseTensor의 생성자는 전부 ReadOnlySpan<int>를 받는다. Span은 박싱이 안 되는
            //    타입이라 리플렉션으로는 부를 수가 없다. 대신 배열을 그대로 받는 확장 메서드
            //    ArrayTensorExtensions.ToTensor(Array, bool)이 있어 그쪽으로 만든다.
            object tensor = MakeTensor(ort, input);

            var create = tNamed.GetMethod("CreateFromTensor", BindingFlags.Public | BindingFlags.Static)
                               .MakeGenericMethod(typeof(float));
            object named = create.Invoke(null, new object[] { inputName, tensor });

            var listType = typeof(List<>).MakeGenericType(tNamed);
            var inputs = (IList)Activator.CreateInstance(listType);
            inputs.Add(named);

            MethodInfo run = null;
            foreach (var m in session.GetType().GetMethods())
            {
                if (m.Name != "Run") continue;
                var ps = m.GetParameters();
                if (ps.Length == 1 && ps[0].ParameterType.IsAssignableFrom(listType)) { run = m; break; }
            }
            if (run == null) throw new MissingMethodException(L.T("InferenceSession.Run 을 찾지 못했다", "InferenceSession.Run not found"));

            object results = run.Invoke(session, new object[] { inputs });

            // 3) 첫 출력 텐서를 float 배열로 뽑는다.
            object first = null;
            foreach (var r in (IEnumerable)results) { first = r; break; }
            if (first == null) throw new InvalidOperationException(L.T("모델이 아무것도 내놓지 않았다", "The model returned no output"));

            var asTensor = first.GetType().GetMethod("AsTensor").MakeGenericMethod(typeof(float));
            object outTensor = asTensor.Invoke(first, null);

            var pred = new float[Side * Side];
            int k = 0;
            foreach (float f in (IEnumerable)outTensor)
            {
                if (k >= pred.Length) break;
                pred[k++] = f;
            }
            try { (results as IDisposable).Dispose(); } catch { }

            // 4) 0~1로 펴고 원래 크기로 되돌린다.
            float lo = float.MaxValue, hi = float.MinValue;
            for (int i = 0; i < pred.Length; i++) { if (pred[i] < lo) lo = pred[i]; if (pred[i] > hi) hi = pred[i]; }
            float span = Math.Max(1e-6f, hi - lo);
            for (int i = 0; i < pred.Length; i++) pred[i] = (pred[i] - lo) / span;

            return UpscaleMask(pred, Side, Side, img.W, img.H);
        }

        // 다차원 float 배열을 Tensor<float>로. 모양(1×3×320×320)은 배열이 그대로 들고 간다.
        internal static object MakeTensor(Assembly ort, Array data)
        {
            var ext = ort.GetType("Microsoft.ML.OnnxRuntime.Tensors.ArrayTensorExtensions", true);
            MethodInfo best = null, fallback = null;
            foreach (var m in ext.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (m.Name != "ToTensor") continue;
                var ps = m.GetParameters();
                if (ps.Length != 2) continue;
                if (ps[0].ParameterType == typeof(Array)) { best = m; break; }
                if (ps[0].ParameterType.IsArray && ps[0].ParameterType.GetArrayRank() == data.Rank)
                    fallback = m;
            }
            var use = best ?? fallback;
            if (use == null) throw new MissingMethodException(L.T("ToTensor 를 찾지 못했다", "ToTensor not found"));
            if (use.IsGenericMethodDefinition) use = use.MakeGenericMethod(typeof(float));
            return use.Invoke(null, new object[] { data, false });
        }

        // ---------- 크기 조정 (이중선형) ----------

        static Canvas32 Resize(Canvas32 src, int w, int h)
        {
            var dst = new Canvas32(w, h);
            double sx = (double)src.W / w, sy = (double)src.H / h;
            for (int y = 0; y < h; y++)
            {
                double fy = (y + 0.5) * sy - 0.5;
                int y0 = (int)Math.Floor(fy); double ty = fy - y0;
                int y1 = Math.Min(src.H - 1, Math.Max(0, y0 + 1)); y0 = Math.Min(src.H - 1, Math.Max(0, y0));
                for (int x = 0; x < w; x++)
                {
                    double fx = (x + 0.5) * sx - 0.5;
                    int x0 = (int)Math.Floor(fx); double tx = fx - x0;
                    int x1 = Math.Min(src.W - 1, Math.Max(0, x0 + 1)); x0 = Math.Min(src.W - 1, Math.Max(0, x0));

                    int c00 = src.At(x0, y0), c10 = src.At(x1, y0), c01 = src.At(x0, y1), c11 = src.At(x1, y1);
                    dst.P[y * w + x] = Canvas32.Pack(
                        Lerp2(Canvas32.A(c00), Canvas32.A(c10), Canvas32.A(c01), Canvas32.A(c11), tx, ty),
                        Lerp2(Canvas32.R(c00), Canvas32.R(c10), Canvas32.R(c01), Canvas32.R(c11), tx, ty),
                        Lerp2(Canvas32.G(c00), Canvas32.G(c10), Canvas32.G(c01), Canvas32.G(c11), tx, ty),
                        Lerp2(Canvas32.B(c00), Canvas32.B(c10), Canvas32.B(c01), Canvas32.B(c11), tx, ty));
                }
            }
            return dst;
        }

        static byte Lerp2(byte a, byte b, byte c, byte d, double tx, double ty)
        {
            double top = a + (b - a) * tx;
            double bot = c + (d - c) * tx;
            double v = top + (bot - top) * ty;
            return (byte)Math.Max(0, Math.Min(255, Math.Round(v)));
        }

        static byte[] UpscaleMask(float[] m, int mw, int mh, int w, int h)
        {
            var outMask = new byte[w * h];
            double sx = (double)mw / w, sy = (double)mh / h;
            for (int y = 0; y < h; y++)
            {
                double fy = (y + 0.5) * sy - 0.5;
                int y0 = (int)Math.Floor(fy); double ty = fy - y0;
                int y1 = Math.Min(mh - 1, Math.Max(0, y0 + 1)); y0 = Math.Min(mh - 1, Math.Max(0, y0));
                for (int x = 0; x < w; x++)
                {
                    double fx = (x + 0.5) * sx - 0.5;
                    int x0 = (int)Math.Floor(fx); double tx = fx - x0;
                    int x1 = Math.Min(mw - 1, Math.Max(0, x0 + 1)); x0 = Math.Min(mw - 1, Math.Max(0, x0));

                    double top = m[y0 * mw + x0] + (m[y0 * mw + x1] - m[y0 * mw + x0]) * tx;
                    double bot = m[y1 * mw + x0] + (m[y1 * mw + x1] - m[y1 * mw + x0]) * tx;
                    double v = top + (bot - top) * ty;
                    outMask[y * w + x] = (byte)Math.Max(0, Math.Min(255, Math.Round(v * 255)));
                }
            }
            return outMask;
        }
    }

    // 처음 누끼를 누를 때 한 번 뜨는 안내.
    public class AskSheet : Sheet
    {
        public AskSheet() : this(false, true) { }

        public AskSheet(bool precise, bool needRuntime) : base(precise ? L.T("정밀 누끼를 쓰려면 한 번 받아야 한다", "Precise cutout needs a one-time download") : L.T("AI 누끼를 쓰려면 한 번 받아야 한다", "AI cutout needs a one-time download"), 420)
        {
            Body.Children.Add(new TextBlock
            {
                Text = precise
                    ? (L.En
                        ? "Downloads a model (BiRefNet) that handles hair, fur and busy backgrounds well. About 224MB" +
                          (needRuntime ? ", plus the inference engine (about 55MB)." : ".") +
                          " It's downloaded only once. Each image takes a few seconds."
                        : "머리카락·털과 복잡한 배경에 강한 모델(BiRefNet)을 내려받는다. 약 224MB" +
                          (needRuntime ? "이고, 추론 엔진(약 55MB)도 함께 받는다." : "이다.") +
                          " 한 번 받으면 다시 받지 않는다. 한 장에 몇 초 걸린다.")
                    : L.T("배경을 자동으로 알아내는 모델과 추론 엔진을 내려받는다. " +
                          "약 100MB이고, 한 번 받으면 다시 받지 않는다.",
                          "Downloads a model that detects the background automatically, plus the inference engine. " +
                          "About 100MB, downloaded only once."),
                Foreground = Theme.BrText, FontSize = 12.5, TextWrapping = TextWrapping.Wrap
            });
            Body.Children.Add(new TextBlock
            {
                Text = precise
                    ? L.T("받는 곳: ", "Source: ") + "huggingface.co/onnx-community/BiRefNet_lite-ONNX (BiRefNet, MIT)"
                    : L.T("받는 곳: github.com/danielgatis/rembg (silueta — U-2-Net 계열, 가중치 라이선스는 원 배포처 기준)", "Source: github.com/danielgatis/rembg (silueta — U-2-Net family; weights licensed per the original distributor)") +
                      (needRuntime ? L.T("\n추론 엔진: ", "\nInference engine: ") + "github.com/microsoft/onnxruntime · nuget.org (MIT)" : ""),
                Foreground = Theme.BrMuted, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0)
            });
            Body.Children.Add(new TextBlock
            {
                Text = L.T("저장 위치: ", "Saved to: ") + Settings.ModelDir,
                Foreground = Theme.BrMuted, FontSize = 11, FontFamily = Theme.Mono,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0)
            });
            Body.Children.Add(new TextBlock
            {
                Text = L.T("받기 싫으면 마술봉과 배경 지우개로도 배경이 단순한 이미지는 충분히 딸 수 있다.", "Rather not download? Magic Wand and Background Eraser work fine for images with simple backgrounds."),
                Foreground = Theme.BrMuted, FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0)
            });
            Body.Children.Add(Buttons(L.T("내려받기", "Download")));
        }
    }
}
