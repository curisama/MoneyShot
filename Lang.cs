// Money Shot — 한국어 / English
//
// 문구는 한국어와 영어를 나란히 적는다: L.T("자르기", "Crop"). 번역 키 파일을 따로 두면
// 고칠 때마다 두 곳을 오가야 하고 어느 화면의 문구인지 놓친다.
// 언어는 설정(자동·한국어·English)을 따르고, 자동이면 윈도우 표시 언어가 한국어일 때만 한국어다.
// 처음 정한 뒤로는 바뀌지 않는다 — 이미 그려진 화면을 다 다시 만들 수 없으니 다시 켤 때 적용된다.
using System;
using System.Globalization;

namespace MoneyShot
{
    public static class L
    {
        static int en = -1;

        public static bool En
        {
            get
            {
                if (en < 0) en = Decide() ? 1 : 0;
                return en == 1;
            }
        }

        static bool Decide()
        {
            string pref = null;
            try { pref = Settings.Current.language; } catch { }
            if (pref == "ko") return false;
            if (pref == "en") return true;
            return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "ko";
        }

        public static string T(string ko, string en) { return En ? en : ko; }

        // 말 순서가 다른 문장은 자리표시로: L.F("{0}장을 올렸다", "Added {0} images", n)
        public static string F(string ko, string en, params object[] args) { return string.Format(T(ko, en), args); }
    }
}
