# Third-party notices · 오픈소스 고지

Money Shot itself is released under the MIT License (see LICENSE).
Money Shot은 MIT 라이선스로 공개한다(LICENSE 참고).

---

## 1. Code included in this app · 앱에 들어 있는 코드

### Compositor
https://github.com/robbietilton/Compositor — MIT License

Parts of the editor were ported from Compositor to C#: cutout refinement (`Matte.cs`), spot healing and
content-aware fill (`Heal.cs`), Levels/Curves/Hue-Saturation/Color Balance/Black & White (`Tone.cs`),
filters (`Filters.cs`), dithering (`Dither.cs`), liquify (`Warp.cs`), the PSD reader (`Psd.cs`),
the RAW develop panel (`CameraRaw.cs`), perspective distort (`Distort.cs`), and the definitions of
layer effects, blend modes, color range selection, rulers and guides, gradients, layer groups and
adjustment layers. Effects that Compositor renders with Metal/Core Image were re-implemented on the CPU.
The PSD writer was written from Adobe's published file format specification.

편집 기능 일부를 Compositor에서 C#으로 옮겼다(누끼 다듬기·스팟 힐링·내용 채우기·레벨·커브·색조/채도·컬러 밸런스·흑백·필터·
디더링·유동화·PSD 읽기·RAW 보정·원근 왜곡과, 레이어 효과·블렌드 모드·색상 범위·눈금자와 가이드·그라디언트·
레이어 그룹·조정 레이어의 동작 정의). Metal·Core Image로 그리던 것은 CPU로 새로 짰다. PSD 쓰기는 Adobe 공개 명세로 새로 짰다.

```
MIT License

Copyright (c) 2026 Wonder Assembly LLC

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

## 2. Components downloaded on first use (not bundled) · 처음 쓸 때 내려받는 것 (앱에 들어 있지 않음)

The AI features (cutout, precise cutout, subject/object selection) download the files below from their
original publishers the first time they are used, after asking the user. Nothing is downloaded otherwise.
Every file is checked against a fixed SHA-256 before use. Each file is governed by its own license.

AI 기능(누끼·정밀 누끼·피사체/물체 선택)을 처음 쓸 때, 사용자에게 묻고 아래 파일을 원래 배포처에서 받는다.
그 밖에는 아무것도 받지 않는다. 받은 파일은 정해 둔 SHA-256과 맞을 때만 쓴다. 각 파일은 저마다의 라이선스를 따른다.

| File | Source | License |
|---|---|---|
| onnxruntime.dll, Microsoft.ML.OnnxRuntime.dll (1.16.3) | github.com/microsoft/onnxruntime, nuget.org | MIT |
| System.Memory, System.Buffers, System.Numerics.Vectors, System.Runtime.CompilerServices.Unsafe | nuget.org (Microsoft) | MIT |
| silueta.onnx (cutout) | github.com/danielgatis/rembg releases — derived from U-2-Net (github.com/xuebinqin/U-2-Net) | rembg: MIT; weights: see the original source (U-2-Net is Apache-2.0) |
| birefnet_lite.onnx (precise cutout) | huggingface.co/onnx-community/BiRefNet_lite-ONNX — BiRefNet (github.com/ZhengPeng7/BiRefNet) | MIT |
| mobile_sam_image_encoder.onnx, sam_mask_decoder_multi.onnx (object select) | huggingface.co/Acly/MobileSAM — MobileSAM (github.com/ChaoningZhang/MobileSAM), Segment Anything (Meta) | MIT (export), Apache-2.0 (MobileSAM / SAM) |

---

## 3. Trademarks · 상표

Adobe, Photoshop and Camera Raw are trademarks of Adobe. Apple and macOS are trademarks of Apple Inc.
Windows is a trademark of Microsoft. Money Shot is not affiliated with or endorsed by any of them.
"PSD" refers only to file format compatibility.

Adobe·Photoshop·Camera Raw는 Adobe의, Apple·macOS는 Apple의, Windows는 Microsoft의 상표다.
Money Shot은 이들과 관계가 없다. "PSD"는 파일 형식 호환을 가리킬 뿐이다.
