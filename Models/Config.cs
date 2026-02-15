namespace AuctionBidder.Models
{
    public static class Config
    {
        // === 帳號密碼設定 (請修改此處) ===
        public const string TAIPEI_USER = "getitem770";  // 台北惜物網 帳號
        public const string TAIPEI_PASS = "ohya0628";  // 台北惜物網 密碼

        public const string TAITUNG_USER = "jeniusduck@yahoo.com.tw"; // 台東E拍網 帳號
        public const string TAITUNG_PASS = "ohya0628"; // 台東E拍網 密碼

        // === 登入網址 (固定) ===
        public const string TAIPEI_LOGIN_URL = "https://shwoo.gov.taipei/shwoo/login/login00/index";
        public const string TAITUNG_LOGIN_URL = "https://epai.taitung.gov.tw/default.asp";

        public static string CAPTCHA_TRAINING_PATH { get; set; } = @".\CaptchaTraining"; // 訓練圖片目錄
        public static bool ENABLE_AUTO_CAPTCHA { get; set; } = true; // 是否啟用自動驗證碼識別
        public static double CAPTCHA_CONFIDENCE_THRESHOLD { get; set; } = 0.7; // 信心度閾值
    }
}