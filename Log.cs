// Money Shot — 진단 로그
// 평소엔 거의 쓸 일이 없지만, 단축키를 다른 앱이 선점했을 때처럼
// 화면에 아무 일도 안 일어나는 상황은 로그 없이는 원인을 알 수 없다.
using System;
using System.IO;

namespace MoneyShot
{
    public static class Log
    {
        static readonly object gate = new object();
        static string path;

        public static string Path_
        {
            get
            {
                if (path == null)
                    path = System.IO.Path.Combine(Settings.BaseDir, "log.txt");
                return path;
            }
        }

        public static void W(string line)
        {
            try
            {
                lock (gate)
                {
                    Directory.CreateDirectory(Settings.BaseDir);
                    // 너무 커지면 앞부분을 버린다
                    if (File.Exists(Path_) && new FileInfo(Path_).Length > 256 * 1024)
                        File.WriteAllText(Path_, "");
                    File.AppendAllText(Path_,
                        DateTime.Now.ToString("MM-dd HH:mm:ss.fff") + "  " + line + Environment.NewLine);
                }
            }
            catch { }
        }
    }
}
