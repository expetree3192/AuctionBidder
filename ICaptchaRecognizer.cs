using OpenQA.Selenium;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AuctionBidder.Core
{
    /// <summary>
    /// 驗證碼識別介面
    /// </summary>
    public interface ICaptchaRecognizer
    {
        Task<CaptchaResult?> RecognizeAsync(byte[] imageData);
        Task<CaptchaResult?> RecognizeFromElementAsync(IWebElement captchaElement, IWebDriver driver);
        Task LoadTrainingDataAsync(string trainingDataPath);
        RecognizerInfo GetInfo();
    }

    /// <summary>
    /// 驗證碼識別結果
    /// </summary>
    public class CaptchaResult
    {
        public string Text { get; set; } = "";
        public double Confidence { get; set; }
        public TimeSpan ProcessTime { get; set; }
        public string Method { get; set; } = "";
        public Dictionary<string, object> Metadata { get; set; } = [];
    }

    /// <summary>
    /// 識別器資訊
    /// </summary>
    public class RecognizerInfo
    {
        public string Name { get; set; } = "";
        public string Version { get; set; } = "";
        public bool IsReady { get; set; }
        public int TrainingSamples { get; set; }
    }
}