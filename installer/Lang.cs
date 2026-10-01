// 설치 프로그램의 한국어 / English — 윈도우 표시 언어가 한국어일 때만 한국어
using System.Globalization;

namespace MoneyShotSetup
{
    public static class L
    {
        static readonly bool en = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "ko";
        public static bool En { get { return en; } }
        public static string T(string ko, string enText) { return en ? enText : ko; }
        public static string F(string ko, string enText, params object[] args) { return string.Format(T(ko, enText), args); }
    }
}
