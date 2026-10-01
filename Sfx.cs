// Money Shot — 셔터음
// 외부 파일 없이 메모리에서 WAV를 합성한다. 짧은 노이즈 버스트 두 번(막 열림/닫힘).
using System;
using System.IO;
using System.Media;

namespace MoneyShot
{
    public static class Sfx
    {
        static SoundPlayer shutter;

        public static void Shutter()
        {
            if (!Settings.Current.playSound) return;
            try
            {
                if (shutter == null) shutter = new SoundPlayer(BuildShutter());
                shutter.Play();
            }
            catch { }
        }

        // 44.1kHz 16bit 모노. 감쇠하는 노이즈 두 번을 8ms 간격으로 찍는다.
        static MemoryStream BuildShutter()
        {
            int rate = 44100;
            double totalSec = 0.085;
            int n = (int)(rate * totalSec);
            var pcm = new short[n];
            var rnd = new Random(7);

            AddBurst(pcm, rnd, rate, 0.000, 0.022, 0.55);   // 막이 열림
            AddBurst(pcm, rnd, rate, 0.034, 0.040, 0.40);   // 막이 닫힘

            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            int dataBytes = n * 2;
            w.Write(new char[] { 'R', 'I', 'F', 'F' });
            w.Write(36 + dataBytes);
            w.Write(new char[] { 'W', 'A', 'V', 'E' });
            w.Write(new char[] { 'f', 'm', 't', ' ' });
            w.Write(16);                 // fmt 청크 크기
            w.Write((short)1);           // PCM
            w.Write((short)1);           // 모노
            w.Write(rate);
            w.Write(rate * 2);           // 바이트/초
            w.Write((short)2);           // 블록 정렬
            w.Write((short)16);          // 비트 심도
            w.Write(new char[] { 'd', 'a', 't', 'a' });
            w.Write(dataBytes);
            for (int i = 0; i < n; i++) w.Write(pcm[i]);
            w.Flush();
            ms.Position = 0;
            return ms;
        }

        static void AddBurst(short[] buf, Random rnd, int rate, double startSec, double lenSec, double gain)
        {
            int s = (int)(startSec * rate), len = (int)(lenSec * rate);
            double lp = 0;
            for (int i = 0; i < len && s + i < buf.Length; i++)
            {
                double t = (double)i / len;
                double env = Math.Exp(-7.0 * t);                 // 빠른 감쇠
                double noise = rnd.NextDouble() * 2 - 1;
                lp += (noise - lp) * 0.35;                        // 1차 저역통과 — 금속성 딸깍
                double v = lp * env * gain;
                int acc = buf[s + i] + (int)(v * short.MaxValue);
                buf[s + i] = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, acc));
            }
        }
    }
}
