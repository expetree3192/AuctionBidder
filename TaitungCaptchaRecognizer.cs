using OpenQA.Selenium;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace AuctionBidder.Core
{
    /// <summary>
    /// 台東E拍網專用驗證碼識別器
    /// </summary>
    public class TaitungCaptchaRecognizer : ICaptchaRecognizer
    {
        private readonly Action<string, string> _logCallback;
        private readonly Dictionary<string, byte[]> _trainingTemplates = [];
        private bool _isReady = false;

        public TaitungCaptchaRecognizer(Action<string, string> logCallback)
        {
            _logCallback = logCallback;
        }

        /// <summary>
        /// 載入訓練數據（從你的圖片檔案）
        /// </summary>
        public async Task LoadTrainingDataAsync(string trainingDataPath)
        {
            try
            {
                _logCallback("ML", $"🤖 開始載入訓練數據: {trainingDataPath}");

                if (!Directory.Exists(trainingDataPath))
                {
                    _logCallback("Error", $"訓練數據目錄不存在: {trainingDataPath}");
                    return;
                }

                var imageFiles = Directory.GetFiles(trainingDataPath, "*.png")
                    .Concat(Directory.GetFiles(trainingDataPath, "*.jpg"))
                    .Concat(Directory.GetFiles(trainingDataPath, "*.jpeg"))
                    .ToArray();

                _trainingTemplates.Clear();

                foreach (var imagePath in imageFiles)
                {
                    try
                    {
                        // 從檔名提取答案（去掉副檔名）
                        var fileName = Path.GetFileNameWithoutExtension(imagePath);

                        // 驗證檔名是否為6位數字
                        if (fileName.Length == 6 && fileName.All(char.IsDigit))
                        {
                            var imageData = await File.ReadAllBytesAsync(imagePath);
                            _trainingTemplates[fileName] = imageData;
                            _logCallback("ML", $"✅ 載入樣本: {fileName}");
                        }
                        else
                        {
                            _logCallback("Warn", $"⚠️ 跳過無效檔名: {fileName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logCallback("Error", $"載入圖片失敗 {imagePath}: {ex.Message}");
                    }
                }

                _isReady = _trainingTemplates.Count > 0;
                _logCallback("ML", $"🎯 訓練數據載入完成，共 {_trainingTemplates.Count} 個樣本");
            }
            catch (Exception ex)
            {
                _logCallback("Error", $"載入訓練數據失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 從網頁元素識別驗證碼
        /// </summary>
        public async Task<CaptchaResult?> RecognizeFromElementAsync(IWebElement captchaElement, IWebDriver driver)
        {
            try
            {
                _logCallback("OCR", "🔍 開始從網頁元素擷取驗證碼...");

                // 嘗試多種方式獲取圖片
                byte[]? imageData = await TryGetImageFromElement(captchaElement, driver);

                if (imageData == null)
                {
                    _logCallback("Error", "無法獲取驗證碼圖片");
                    return null;
                }

                // 儲存除錯圖片
                var debugPath = $"captcha_debug_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                await File.WriteAllBytesAsync(debugPath, imageData);
                _logCallback("Debug", $"驗證碼圖片已儲存: {debugPath}");

                return await RecognizeAsync(imageData);
            }
            catch (Exception ex)
            {
                _logCallback("Error", $"從元素識別失敗: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 識別驗證碼（核心方法）
        /// </summary>
        public async Task<CaptchaResult?> RecognizeAsync(byte[] imageData)
        {
            if (!_isReady)
            {
                _logCallback("Error", "識別器未就緒，請先載入訓練數據");
                return null;
            }

            var stopwatch = Stopwatch.StartNew();

            try
            {
                _logCallback("OCR", "🎯 開始模板匹配識別...");

                // 預處理輸入圖片
                using var inputImage = PreprocessImage(imageData);

                var bestMatch = "";
                var bestSimilarity = 0.0;

                // 與每個訓練樣本進行比較
                foreach (var template in _trainingTemplates)
                {
                    try
                    {
                        using var templateImage = PreprocessImage(template.Value);
                        var similarity = CalculateImageSimilarity(inputImage, templateImage);

                        if (similarity > bestSimilarity)
                        {
                            bestSimilarity = similarity;
                            bestMatch = template.Key;
                        }

                        _logCallback("Debug", $"與 {template.Key} 相似度: {similarity:P2}");
                    }
                    catch (Exception ex)
                    {
                        _logCallback("Debug", $"比較模板 {template.Key} 失敗: {ex.Message}");
                    }
                }

                stopwatch.Stop();

                if (bestSimilarity > 0.6) // 相似度閾值
                {
                    var result = new CaptchaResult
                    {
                        Text = bestMatch,
                        Confidence = bestSimilarity,
                        ProcessTime = stopwatch.Elapsed,
                        Method = "TemplateMatching",
                        Metadata = new Dictionary<string, object>
                        {
                            ["TotalTemplates"] = _trainingTemplates.Count,
                            ["BestSimilarity"] = bestSimilarity,
                            ["ProcessedAt"] = DateTime.Now
                        }
                    };

                    _logCallback("OCR", $"✅ 識別成功: {bestMatch} (相似度: {bestSimilarity:P2})");
                    return result;
                }
                else
                {
                    _logCallback("OCR", $"❌ 識別失敗，最高相似度僅 {bestSimilarity:P2}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logCallback("Error", $"識別過程發生錯誤: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 從網頁元素獲取圖片數據
        /// </summary>
        private async Task<byte[]?> TryGetImageFromElement(IWebElement element, IWebDriver driver)
        {
            // 方法1: 直接下載圖片
            var imageData = await TryDownloadImage(element);
            if (imageData != null) return imageData;

            // 方法2: 螢幕截圖
            imageData = TryScreenshotElement(element, driver);
            if (imageData != null) return imageData;

            // 方法3: JavaScript 截圖
            imageData = TryJavaScriptScreenshot(element, driver);
            return imageData;
        }

        private async Task<byte[]?> TryDownloadImage(IWebElement element)
        {
            try
            {
                var src = element.GetAttribute("src");
                if (string.IsNullOrEmpty(src)) return null;

                using var httpClient = new HttpClient();
                return await httpClient.GetByteArrayAsync(src);
            }
            catch
            {
                return null;
            }
        }

        private byte[]? TryScreenshotElement(IWebElement element, IWebDriver driver)
        {
            try
            {
                var screenshot = ((ITakesScreenshot)driver).GetScreenshot();
                using var fullImage = Image.FromStream(new MemoryStream(screenshot.AsByteArray));

                var location = element.Location;
                var size = element.Size;

                if (location.X < 0 || location.Y < 0 || size.Width <= 0 || size.Height <= 0)
                    return null;

                using var croppedImage = new Bitmap(size.Width, size.Height);
                using var graphics = Graphics.FromImage(croppedImage);

                var srcRect = new Rectangle(location.X, location.Y, size.Width, size.Height);
                var destRect = new Rectangle(0, 0, size.Width, size.Height);

                graphics.DrawImage(fullImage, destRect, srcRect, GraphicsUnit.Pixel);

                using var stream = new MemoryStream();
                croppedImage.Save(stream, ImageFormat.Png);
                return stream.ToArray();
            }
            catch
            {
                return null;
            }
        }

        private byte[]? TryJavaScriptScreenshot(IWebElement element, IWebDriver driver)
        {
            try
            {
                var script = @"
                  var element = arguments[0];
                  var canvas = document.createElement('canvas');
                  var context = canvas.getContext('2d');
                  canvas.width = element.offsetWidth;
                  canvas.height = element.offsetHeight;
                  if (element.tagName === 'IMG') {
                      context.drawImage(element, 0, 0);
                      return canvas.toDataURL('image/png');
                  }
                  return null;";

                var base64 = ((IJavaScriptExecutor)driver).ExecuteScript(script, element) as string;
                if (string.IsNullOrEmpty(base64)) return null;

                var base64Data = base64[(base64.IndexOf(',') + 1)..];
                return Convert.FromBase64String(base64Data);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 圖片預處理
        /// </summary>
        private Bitmap PreprocessImage(byte[] imageData)
        {
            using var originalImage = Image.FromStream(new MemoryStream(imageData)) as Bitmap;
            if (originalImage == null) throw new ArgumentException("無效的圖片數據");

            // 放大圖片以提高識別精度
            var scaleFactor = 3;
            var scaledWidth = originalImage.Width * scaleFactor;
            var scaledHeight = originalImage.Height * scaleFactor;

            var processedImage = new Bitmap(scaledWidth, scaledHeight);

            using (var graphics = Graphics.FromImage(processedImage))
            {
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(originalImage, 0, 0, scaledWidth, scaledHeight);
            }

            // 轉換為黑白圖片
            for (int x = 0; x < scaledWidth; x++)
            {
                for (int y = 0; y < scaledHeight; y++)
                {
                    var pixel = processedImage.GetPixel(x, y);
                    var gray = (int)(pixel.R * 0.299 + pixel.G * 0.587 + pixel.B * 0.114);
                    var threshold = 128;
                    var newColor = gray > threshold ? Color.White : Color.Black;
                    processedImage.SetPixel(x, y, newColor);
                }
            }

            return processedImage;
        }

        /// <summary>
        /// 計算兩張圖片的相似度
        /// </summary>
        private double CalculateImageSimilarity(Bitmap img1, Bitmap img2)
        {
            if (img1.Width != img2.Width || img1.Height != img2.Height)
            {
                // 調整圖片大小使其一致
                var targetWidth = Math.Min(img1.Width, img2.Width);
                var targetHeight = Math.Min(img1.Height, img2.Height);

                using var resized1 = new Bitmap(img1, targetWidth, targetHeight);
                using var resized2 = new Bitmap(img2, targetWidth, targetHeight);
                return CalculatePixelSimilarity(resized1, resized2);
            }

            return CalculatePixelSimilarity(img1, img2);
        }

        private double CalculatePixelSimilarity(Bitmap img1, Bitmap img2)
        {
            int totalPixels = img1.Width * img1.Height;
            int matchingPixels = 0;

            for (int x = 0; x < img1.Width; x++)
            {
                for (int y = 0; y < img1.Height; y++)
                {
                    var pixel1 = img1.GetPixel(x, y);
                    var pixel2 = img2.GetPixel(x, y);

                    // 簡單的像素比較（黑白圖片）
                    if (pixel1.R == pixel2.R && pixel1.G == pixel2.G && pixel1.B == pixel2.B)
                    {
                        matchingPixels++;
                    }
                }
            }

            return (double)matchingPixels / totalPixels;
        }

        public RecognizerInfo GetInfo()
        {
            return new RecognizerInfo
            {
                Name = "台東E拍網驗證碼識別器",
                Version = "1.0.0",
                IsReady = _isReady,
                TrainingSamples = _trainingTemplates.Count
            };
        }
    }
}