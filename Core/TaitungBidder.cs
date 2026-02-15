using AuctionBidder.Models;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AuctionBidder.Core
{
    public class TaitungBidder(string name, Action<string, string> logCallback) : BaseBidder(name, logCallback)
    {
        private ICaptchaRecognizer? _captchaRecognizer;
        private bool _captchaEnabled = false;

        public async Task InitializeCaptchaRecognizerAsync()
        {
            try
            {
                if (!Config.ENABLE_AUTO_CAPTCHA)
                {
                    Log("Config", "自動驗證碼識別已停用");
                    _captchaEnabled = false;
                    return;
                }

                Log("Init", "🤖 初始化驗證碼識別器...");

                _captchaRecognizer = new TaitungCaptchaRecognizer(Log);
                await _captchaRecognizer.LoadTrainingDataAsync(Config.CAPTCHA_TRAINING_PATH);

                var info = _captchaRecognizer.GetInfo();
                if (info.IsReady)
                {
                    _captchaEnabled = true;
                    Log("Init", $"✅ 驗證碼識別器就緒 - {info.Name} (樣本數: {info.TrainingSamples})");
                }
                else
                {
                    _captchaEnabled = false;
                    Log("Warn", "❌ 驗證碼識別器初始化失敗");
                }
            }
            catch (Exception ex)
            {
                _captchaEnabled = false;
                Log("Error", $"初始化驗證碼識別器失敗: {ex.Message}");
            }
        }

        private DateTime? _targetEndTime = null;
        private HttpClient? _httpClient = null;
        private string? _currentUrl = null;
        private Dictionary<string, string>? _lastParsedPayload = null;
        private bool _usePostMethod = false;
        private (string? auid, string? pcode) _cachedUrlParams = (null, null);

        public void EnablePostMode(bool enable = true)
        {
            if (_usePostMethod == enable) return;

            _usePostMethod = enable;

            if (enable && _httpClient == null)
            {
                InitializeHttpClient();
            }

            Log("Config", $"POST 模式: {(enable ? "已啟用" : "已停用")}");
        }

        private void InitializeHttpClient()
        {
            try
            {
                if (!Encoding.GetEncodings().Any(e => e.Name.Equals("big5", StringComparison.OrdinalIgnoreCase)))
                {
                    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                    Log("Debug", "Big5 編碼提供者已註冊完成");
                }

                var handler = new HttpClientHandler() { UseCookies = true };
                _httpClient = new HttpClient(handler);
                _httpClient.DefaultRequestHeaders.Add("User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

                Log("Init", "HttpClient 初始化完成");
            }
            catch (Exception ex)
            {
                Log("Error", $"HttpClient 初始化失敗: {ex.Message}");
            }
        }

        public override bool NavigateToPage(string url)
        {
            try
            {
                if (Driver == null) return false;

                Driver.Navigate().GoToUrl(url);
                _currentUrl = url;

                _cachedUrlParams = ParseUrlParams(url);

                Log("Web", "導航至頁面完成");

                RefreshParser();

                if (!WaitForPageLoad())
                {
                    Log("Warn", "頁面載入超時，但繼續執行");
                }

                var frames = Driver.FindElements(By.TagName("frame"));
                bool frameFound = false;

                foreach (var fr in frames)
                {
                    try
                    {
                        Driver.SwitchTo().Frame(fr);
                        if (Driver.FindElements(By.XPath("//*[contains(text(), '截止時間')]")).Count > 0)
                        {
                            frameFound = true;
                            Log("OK", "成功切換到 Frame");
                            RefreshParser();
                            break;
                        }
                        Driver.SwitchTo().DefaultContent();
                    }
                    catch
                    {
                        Driver.SwitchTo().DefaultContent();
                    }
                }

                if (!frameFound)
                {
                    Log("Info", "沒有找到適用框架");
                }

                if (!WaitForBidContentLoad())
                {
                    Log("Warn", "競標內容載入超時，但繼續執行");
                }

                if (_httpClient == null)
                {
                    Log("Info", "初始化 HttpClient 用於後續HTTP請求");
                    InitializeHttpClient();
                }

                if (_usePostMethod)
                {
                    Log("POST", "POST 模式已啟用，準備解析表單...");
                    ParseBidFormForPost();
                    TransferCookies();
                }

                var parser = GetParser();
                parser.SaveAllPageContent($"Taitung_PageContent_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

                return true;
            }
            catch (Exception ex)
            {
                Log("Error", ex.Message);
                return false;
            }
        }

        private (string? auid, string? pcode) ParseUrlParams(string url)
        {
            try
            {
                var auidMatch = Regex.Match(url, @"auid=([^&]+)");
                var pcodeMatch = Regex.Match(url, @"pcode=([^&]+)");

                return (
                    auidMatch.Success ? auidMatch.Groups[1].Value : null,
                    pcodeMatch.Success ? pcodeMatch.Groups[1].Value : null
                );
            }
            catch (Exception ex)
            {
                Log("Debug", $"解析URL參數錯誤: {ex.Message}");
                return (null, null);
            }
        }

        public async Task<decimal?> RefreshPriceViaHttpAsync(string? auid = null, string? pcode = null)
        {
            if (_httpClient == null)
            {
                InitializeHttpClient();
                if (_httpClient == null)
                {
                    Log("Error", "HttpClient 初始化失敗");
                    return null;
                }
            }

            try
            {
                var currentSeleniumPrice = GetPrice();
                if (currentSeleniumPrice.HasValue)
                {
                    Log("Debug", $"目前Selenium價格: ${currentSeleniumPrice}");
                }

                var targetAuid = auid ?? _cachedUrlParams.auid;
                var targetPcode = pcode ?? _cachedUrlParams.pcode;

                if (string.IsNullOrEmpty(targetAuid) || string.IsNullOrEmpty(targetPcode))
                {
                    Log("Error", "無法取得 auid 或 pcode 參數");
                    return null;
                }

                var rnd = new Random();
                var randValue = rnd.NextDouble().ToString("0.0000000000000000", System.Globalization.CultureInfo.InvariantCulture);
                var url = $"https://epai.taitung.gov.tw/bid.asp?op_=show&auid={targetAuid}&pcode={targetPcode}&{randValue}";

                Encoding big5;
                try
                {
                    big5 = Encoding.GetEncoding("big5");
                }
                catch
                {
                    big5 = Encoding.UTF8;
                }

                Log("Refresh", $"HTTP 刷新價格 (auid={targetAuid})");

                var response = await _httpClient.GetAsync(url);
                var bytes = await response.Content.ReadAsByteArrayAsync();
                var html = big5.GetString(bytes);

                var price = ExtractPriceFromHtml(html);
                if (price.HasValue)
                {
                    if (currentSeleniumPrice.HasValue)
                    {
                        var priceDiff = Math.Abs(price.Value - currentSeleniumPrice.Value);
                        if (priceDiff <= 50)
                        {
                            Log("Price", $"HTTP 刷新成功，目前價格: ${price} (與界面一致)");
                        }
                        else
                        {
                            Log("Price", $"HTTP 刷新成功，目前價格: ${price} (與Selenium差異較大: ${currentSeleniumPrice})");
                        }
                    }
                    else
                    {
                        Log("Price", $"HTTP 刷新成功，目前價格: ${price}");
                    }
                    return price;
                }
                else
                {
                    Log("Info", "HTTP刷新成功但無法解析價格");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Log("Error", $"HTTP 刷新錯誤: {ex.Message}");
                return null;
            }
        }

        private decimal? ExtractPriceFromHtml(string html)
        {
            try
            {
                Log("Debug", $"開始解析HTML，長度: {html.Length} 字符");

                var selectPattern = @"<select[^>]*name=['""]X01456416['""][^>]*>(.*?)</select>";
                var selectMatch = Regex.Match(html, selectPattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);

                if (selectMatch.Success)
                {
                    var selectContent = selectMatch.Groups[1].Value;
                    Log("Debug", "找到價格選擇器 X01456416");

                    var selectedPattern = @"<option[^>]*value=['""](\d+)['""][^>]*selected[^>]*>";
                    var selectedMatch = Regex.Match(selectContent, selectedPattern, RegexOptions.IgnoreCase);

                    if (selectedMatch.Success)
                    {
                        var price = decimal.Parse(selectedMatch.Groups[1].Value);
                        Log("Debug", $"從被選中找到價格: {price}");
                        return price;
                    }

                    var firstOptionPattern = @"<option[^>]*value=['""](\d+)['""][^>]*>";
                    var firstMatch = Regex.Match(selectContent, firstOptionPattern, RegexOptions.IgnoreCase);

                    if (firstMatch.Success)
                    {
                        var price = decimal.Parse(firstMatch.Groups[1].Value);
                        Log("Debug", $"從第一個選項找到價格: {price}");
                        return price;
                    }
                }

                var hiddenFieldPattern = @"<input[^>]*name=['""]X02674328['""][^>]*value=['""]([^'""]*)['""]";
                var hiddenMatch = Regex.Match(html, hiddenFieldPattern, RegexOptions.IgnoreCase);

                if (hiddenMatch.Success)
                {
                    var hiddenValue = hiddenMatch.Groups[1].Value;
                    Log("Debug", $"找到隱藏欄位X02674328: {hiddenValue}");

                    var prices = hiddenValue.Split(',')
                        .Where(s => !string.IsNullOrEmpty(s.Trim()))
                        .Where(s => decimal.TryParse(s.Trim(), out _))
                        .Select(s => decimal.Parse(s.Trim()))
                        .Where(p => p > 0)
                        .ToList();

                    if (prices.Count != 0)
                    {
                        var currentPrice = prices.First();
                        Log("Debug", $"從隱藏欄位解析到目前價格: {currentPrice}");
                        Log("Debug", $"所有可用價格: {string.Join(", ", prices)}");
                        return currentPrice;
                    }
                }

                var anySelectPattern = @"<select[^>]*>(.*?)</select>";
                var anySelectMatches = Regex.Matches(html, anySelectPattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);

                foreach (Match selectMatch2 in anySelectMatches)
                {
                    var selectContent = selectMatch2.Groups[1].Value;

                    var selectedPattern2 = @"<option[^>]*value=['""](\d+)['""][^>]*selected[^>]*>";
                    var selectedMatch2 = Regex.Match(selectContent, selectedPattern2, RegexOptions.IgnoreCase);

                    if (selectedMatch2.Success)
                    {
                        var price = decimal.Parse(selectedMatch2.Groups[1].Value);
                        if (price >= 10 && price <= 10000)
                        {
                            Log("Debug", $"從其他select找到價格: {price}");
                            return price;
                        }
                    }
                }

                var debugFile = $"PriceExtract_Failed_{DateTime.Now:yyyyMMdd_HHmmss}.html";
                System.IO.File.WriteAllText(debugFile, html, Encoding.UTF8);
                Log("Debug", $"價格解析失敗，HTML已儲存至: {debugFile}");

                return null;
            }
            catch (Exception ex)
            {
                Log("Error", $"解析價格的過程中發生錯誤: {ex.Message}");
                return null;
            }
        }

        public void TestPriceExtraction()
        {
            if (Driver == null)
            {
                Log("Error", "Driver 尚未初始化，無法進行測試");
                return;
            }

            try
            {
                Log("Test", "=== 價格解析測試開始 ===");

                var currentHtml = Driver.PageSource;
                Log("Test", $"目前頁面HTML長度: {currentHtml.Length} 字符");

                var extractedPrice = ExtractPriceFromHtml(currentHtml);

                var seleniumPrice = GetPrice();

                Log("Test", $"HTTP解析價格: {extractedPrice?.ToString() ?? "NULL"}");
                Log("Test", $"Selenium價格: {seleniumPrice?.ToString() ?? "NULL"}");

                if (extractedPrice.HasValue && seleniumPrice.HasValue)
                {
                    if (extractedPrice == seleniumPrice)
                    {
                        Log("Test", "✓ 價格解析正確，嘗試一致");
                    }
                    else
                    {
                        var diff = Math.Abs(extractedPrice.Value - seleniumPrice.Value);
                        Log("Test", $"✗ 價格不一致，差異: {diff}");

                        if (diff <= 2)
                        {
                            Log("Test", "✓ 差異在可接受範圍");
                        }
                        else
                        {
                            Log("Test", "✗ 差異過大，需要調整解析邏輯");
                        }
                    }
                }
                else if (extractedPrice.HasValue)
                {
                    Log("Test", "✗ HTTP解析成功但Selenium錯誤");
                }
                else if (seleniumPrice.HasValue)
                {
                    Log("Test", "✗ Selenium解析成功但HTTP錯誤");
                }
                else
                {
                    Log("Test", "✗ 兩種方法都無法解析價格");
                }

                Log("Test", "--- 頁面狀況詳細資訊 ---");

                var selects = Driver.FindElements(By.TagName("select"));
                Log("Test", $"找到 {selects.Count} 個select元素");

                if (selects.Count > 0)
                {
                    var selectElement = new SelectElement(selects[0]);
                    var selectedOption = selectElement.SelectedOption;

                    Log("Test", $"被選中 - value: '{selectedOption.GetAttribute("value")}', text: '{selectedOption.Text}'");

                    var allOptions = selectElement.Options;
                    Log("Test", $"共有 {allOptions.Count} 個選項");

                    for (int i = 0; i < Math.Min(5, allOptions.Count); i++)
                    {
                        var option = allOptions[i];
                        var isSelected = option.Selected ? " [被選中]" : "";
                        Log("Test", $"  選項{i + 1}: value='{option.GetAttribute("value")}', text='{option.Text[..Math.Min(20, option.Text.Length)]}...'{isSelected}");
                    }
                }

                Log("Test", "=== 價格解析測試結束 ===");
            }
            catch (Exception ex)
            {
                Log("Error", $"嘗試價格解析失敗: {ex.Message}");
                Log("Error", $"詳細堆疊追蹤: {ex.StackTrace}");
            }
        }

        private static bool IsLikelyNotPrice(decimal number)
        {
            return number == 2024 || number == 2025 || number == 2026 ||
                   number == 1911 ||
                   number > 50000 ||
                   (number >= 1000 && number <= 9999 && number % 1000 == 0);
        }

        private bool WaitForPageLoad(int timeoutSeconds = 10)
        {
            try
            {
                if (Driver == null) return false;

                var wait = new WebDriverWait(Driver, TimeSpan.FromSeconds(timeoutSeconds));
                wait.Until(driver =>
                {
                    try
                    {
                        var body = driver.FindElement(By.TagName("body"));
                        var bodyText = body.Text;
                        return bodyText.Contains("頭份電子競標系統") ||
                               bodyText.Contains("競標專區") ||
                               bodyText.Contains("標案") ||
                               bodyText.Contains("截止時間") ||
                               bodyText.Length > 100;
                    }
                    catch { return false; }
                });

                Log("OK", "頁面基本載入完成");
                return true;
            }
            catch (WebDriverTimeoutException)
            {
                Log("Warn", "頁面基本載入超時");
                return false;
            }
            catch (Exception ex)
            {
                Log("Debug", $"頁面載入檢查過程錯誤: {ex.Message}");
                return false;
            }
        }

        private bool WaitForBidContentLoad(int timeoutSeconds = 8)
        {
            try
            {
                if (Driver == null) return false;

                var wait = new WebDriverWait(Driver, TimeSpan.FromSeconds(timeoutSeconds));
                wait.Until(driver =>
                {
                    try
                    {
                        var body = driver.FindElement(By.TagName("body"));
                        var bodyText = body.Text;
                        bool hasTimeInfo = bodyText.Contains("現在時間:") && bodyText.Contains("截止時間:");
                        bool hasPriceInfo = bodyText.Contains("競標底價:") || bodyText.Contains("標案");
                        bool hasBasicInfo = bodyText.Contains("項目") || bodyText.Contains("台東縣政府");
                        return hasTimeInfo || hasPriceInfo || hasBasicInfo;
                    }
                    catch { return false; }
                });

                Log("OK", "競標內容載入完成");
                return true;
            }
            catch (WebDriverTimeoutException)
            {
                Log("Debug", "競標內容載入超時，但繼續其他操作");
                Thread.Sleep(2000);
                return false;
            }
            catch (Exception ex)
            {
                Log("Debug", $"競標內容載入檢查過程錯誤: {ex.Message}");
                Thread.Sleep(1000);
                return false;
            }
        }

        public override async void AutoLogin(string loginUrl)
        {
            if (Driver == null) return;

            try
            {
                Log("Web", "🚀 開始智能登入流程...");
                Driver.Navigate().GoToUrl(loginUrl);
                RefreshParser();

                WebDriverWait wait = new(Driver, TimeSpan.FromSeconds(15));
                wait.Until(d => d.FindElements(By.Name("password")).Count > 0);

                if (string.IsNullOrEmpty(Config.TAITUNG_USER) || string.IsNullOrEmpty(Config.TAITUNG_PASS))
                {
                    Log("Config", "❌ 未設定帳號密碼");
                    return;
                }

                Log("Input", "📝 填入帳密...");
                var userField = Driver.FindElement(By.Name("email"));
                var passField = Driver.FindElement(By.Name("password"));

                userField.Clear();
                userField.SendKeys(Config.TAITUNG_USER);

                passField.Clear();
                passField.SendKeys(Config.TAITUNG_PASS);

                // 🔧 修正：使用精確的驗證碼處理
                await HandleCaptchaIntelligentlyAsync();

            }
            catch (Exception ex)
            {
                Log("Error", $"登入失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 🔧 修正：根據 Python 分析結果的精確驗證碼處理
        /// </summary>
        private async Task HandleCaptchaIntelligentlyAsync()
        {
            try
            {
                Log("Captcha", "🤖 開始智能驗證碼處理...");

                // 🎯 精確定位驗證碼輸入框 - 根據分析結果
                IWebElement? captchaInput = null;
                try
                {
                    captchaInput = Driver!.FindElement(By.Name("validcode"));
                    Log("Found", "找到驗證碼輸入框 (By.Name): validcode");
                }
                catch
                {
                    try
                    {
                        captchaInput = Driver!.FindElement(By.Id("validcode"));
                        Log("Found", "找到驗證碼輸入框 (By.Id): validcode");
                    }
                    catch
                    {
                        Log("Error", "❌ 找不到驗證碼輸入框");
                        return;
                    }
                }

                // 🎯 精確定位驗證碼圖片 - 根據分析結果
                IWebElement? captchaImage = null;
                try
                {
                    captchaImage = Driver.FindElement(By.XPath("//img[contains(@src, 'validCode2.asp')]"));
                    Log("Found", "找到驗證碼圖片 (By.XPath): 包含 validCode2.asp");
                }
                catch
                {
                    Log("Error", "❌ 找不到驗證碼圖片");
                    // 手動模式
                    captchaInput.Click();
                    Log("Manual", "👤 請手動輸入驗證碼後點擊登入");
                    return;
                }

                // AI 自動識別
                if (_captchaEnabled && _captchaRecognizer != null)
                {
                    Log("AI", "🤖 嘗試AI自動識別驗證碼...");

                    var result = await _captchaRecognizer.RecognizeFromElementAsync(captchaImage, Driver);

                    if (result != null && result.Confidence >= Config.CAPTCHA_CONFIDENCE_THRESHOLD)
                    {
                        captchaInput.Clear();
                        captchaInput.SendKeys(result.Text);
                        Log("AI", $"✅ AI識別成功: {result.Text} (信心度: {result.Confidence:P1})");

                        // 自動提交登入
                        try
                        {
                            var loginButton = Driver.FindElement(By.XPath("//button[contains(text(), '登入')] | //input[@type='submit'] | //input[@value='登入']"));
                            loginButton.Click();

                            await Task.Delay(3000);
                            if (IsLoginSuccessful())
                            {
                                Log("Success", "🎉 AI自動登入成功！");
                                return;
                            }
                            else
                            {
                                Log("AI", "❌ AI識別可能錯誤，切換到手動模式");
                            }
                        }
                        catch (Exception ex)
                        {
                            Log("Warn", $"自動提交失敗: {ex.Message}");
                        }
                    }
                    else
                    {
                        Log("AI", $"⚠️ AI識別信心度不足 ({result?.Confidence:P1})，切換到手動模式");
                    }
                }

                // 後備方案：手動輸入
                captchaInput.Click();
                Log("Manual", "👤 請手動輸入驗證碼後點擊登入");

            }
            catch (Exception ex)
            {
                Log("Error", $"驗證碼處理失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 🔧 修正：測試驗證碼識別功能 - 使用精確選擇器
        /// </summary>
        public async Task TestCaptchaRecognitionAsync()
        {
            if (_captchaRecognizer == null)
            {
                Log("Error", "❌ 驗證碼識別器未初始化");
                return;
            }

            try
            {
                Log("Test", "🧪 開始測試驗證碼識別...");

                Driver!.Navigate().GoToUrl(Config.TAITUNG_LOGIN_URL);
                await Task.Delay(3000);

                // 🎯 使用精確的選擇器
                IWebElement? captchaImage;
                try
                {
                    captchaImage = Driver.FindElement(By.XPath("//img[contains(@src, 'validCode2.asp')]"));
                    Log("Found", "找到驗證碼圖片: validCode2.asp");
                }
                catch
                {
                    Log("Error", "❌ 找不到驗證碼圖片");
                    return;
                }

                for (int i = 1; i <= 3; i++)
                {
                    Log("Test", $"🔄 第 {i} 次測試...");

                    var result = await _captchaRecognizer.RecognizeFromElementAsync(captchaImage, Driver);

                    if (result != null)
                    {
                        Log("Test", $"✅ 識別結果: {result.Text} | 信心度: {result.Confidence:P1} | 耗時: {result.ProcessTime.TotalMilliseconds}ms | 方法: {result.Method}");
                    }
                    else
                    {
                        Log("Test", "❌ 識別失敗");
                    }

                    if (i < 3)
                    {
                        // 刷新驗證碼 - 點擊圖片或重新載入頁面
                        try
                        {
                            captchaImage.Click();
                            await Task.Delay(2000);
                        }
                        catch
                        {
                            Driver.Navigate().Refresh();
                            await Task.Delay(3000);
                            captchaImage = Driver.FindElement(By.XPath("//img[contains(@src, 'validCode2.asp')]"));
                        }
                    }
                }

                Log("Test", "🏁 測試完成");
            }
            catch (Exception ex)
            {
                Log("Error", $"測試失敗: {ex.Message}");
            }
        }

        private bool IsLoginSuccessful()
        {
            try
            {
                Thread.Sleep(2000);
                var currentUrl = Driver!.Url;

                return !currentUrl.Contains("default.asp") ||
                       Driver.FindElements(By.Name("email")).Count == 0;
            }
            catch
            {
                return false;
            }
        }

        // ... 保留其他所有方法不變 ...

        public override void RunMonitor(TaskConfig config, CancellationToken token)
        {
            IsRunning = true;
            _targetEndTime = null;
            int? lastPrice = null;
            long lastRefreshTime = 0;
            int syncFailCount = 0;
            long lastLogSecond = 0;

            Log("Set", "開始執行頭份競標監控 (v8.26 Final + WebParser)...");

            while (!token.IsCancellationRequested && IsRunning && Driver != null)
            {
                try
                {
                    if (Driver.WindowHandles.Count == 0) break;

                    DateTime curr = DateTime.Now;

                    if (_targetEndTime == null)
                    {
                        if (SyncTime())
                        {
                            if (_targetEndTime.HasValue)
                            {
                                Log("Lock", $"鎖定截止時間: {_targetEndTime.Value:HH:mm:ss.fff} (約 {(long)(_targetEndTime.Value - curr).TotalSeconds} 秒)");
                                syncFailCount = 0;
                            }
                        }
                        else
                        {
                            syncFailCount++;
                            if (syncFailCount > 10)
                            {
                                Log("Warn", "時間同步錯誤過多，嘗試重新整理...");
                                Driver.Navigate().Refresh();
                                syncFailCount = 0;
                                Thread.Sleep(3000);
                            }
                            else Thread.Sleep(1000);
                        }
                        continue;
                    }

                    long remainMs = (long)(_targetEndTime.Value - curr).TotalMilliseconds;
                    bool isSprint = remainMs <= (config.SprintStartSec * 1000);

                    if (remainMs <= config.TriggerMs)
                    {
                        if (remainMs < -5000)
                        {
                            Log("Info", "時間已過，停止監控");
                            break;
                        }

                        Log("Trig", $"觸發! 剩 {remainMs} ms (閾值: {config.TriggerMs} ms)");
                        var finalP = GetPrice();

                        if (config.DynamicMaxPrice.HasValue && finalP.HasValue && finalP > config.DynamicMaxPrice)
                        {
                            Log("STOP", $"價格 {finalP} 超過上限 {config.DynamicMaxPrice}，停止競標");
                            break;
                        }

                        if (config.RealBid)
                        {
                            ExecuteBid(config);
                        }
                        else
                        {
                            Log("Safe", "模擬觸發 (未實際投標)");
                            Thread.Sleep(2000);
                        }
                        break;
                    }

                    bool needRefresh = false;
                    if (isSprint)
                    {
                        if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - lastRefreshTime >= config.SprintFreqMs)
                            needRefresh = true;
                    }
                    else
                    {
                        long currentSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                        if (currentSec % 5 == 0 && currentSec != lastLogSecond)
                        {
                            needRefresh = true;
                            lastLogSecond = currentSec;
                        }
                    }

                    if (needRefresh)
                    {
                        if (isSprint)
                        {
                            if (Driver is IJavaScriptExecutor jsExecutor)
                            {
                                jsExecutor.ExecuteScript("refresh();");
                            }
                            Thread.Sleep(500);
                        }

                        var p = GetPrice();
                        if (p.HasValue) lastPrice = p;

                        if (isSprint)
                        {
                            SyncTime();
                        }

                        lastRefreshTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                        string pStr = lastPrice.HasValue ? $"${lastPrice}" : "[NULL]";
                        string tag = isSprint ? "[Dash]" : "[Cruise]";

                        TimeSpan ts = TimeSpan.FromMilliseconds(remainMs);
                        string timeStr;

                        if (remainMs < 0)
                        {
                            TimeSpan absTs = TimeSpan.FromMilliseconds(Math.Abs(remainMs));
                            timeStr = $"-{absTs.Days:D2}天{absTs.Hours:D2}時{absTs.Minutes:D2}分{absTs.Seconds:D2}秒.{absTs.Milliseconds:D3}";
                        }
                        else
                        {
                            timeStr = $"{ts.Days:D2}天{ts.Hours:D2}時{ts.Minutes:D2}分{ts.Seconds:D2}秒.{ts.Milliseconds:D3}";
                        }

                        string limitStr = config.DynamicMaxPrice.HasValue ? $" (上限:{config.DynamicMaxPrice})" : "";

                        string targetTimeStr = "";
                        if (_targetEndTime.HasValue)
                        {
                            DateTime actualTriggerTime = _targetEndTime.Value.AddMilliseconds(-config.TriggerMs);
                            targetTimeStr = $" @ {actualTriggerTime:HH:mm:ss.fff}";
                        }

                        if (!isSprint || (isSprint && remainMs % 1000 < 300))
                            Log("Time", $"{timeStr} | {pStr}{limitStr} {tag}{targetTimeStr}");
                    }

                    Thread.Sleep(isSprint ? 1 : 100);
                }
                catch (Exception ex)
                {
                    Log("Error", ex.Message);
                    Thread.Sleep(1000);
                }
            }
            IsRunning = false;
        }

        public override void ManualBid()
        {
            if (Driver == null) return;

            Log("Manual", "手動執行頭份投標");

            if (SyncTime())
            {
                Log("Sync", "開始投標前時間同步完成");
            }

            var currentPrice = GetPrice();
            if (currentPrice.HasValue)
            {
                Log("Info", $"目前價格: {currentPrice}");
            }

            if (_usePostMethod)
            {
                Log("Mode", "使用 POST 模式手動投標");
                ExecuteBidViaPost(null);
            }
            else
            {
                Log("Mode", "使用傳統 Selenium 模式手動投標");
                ExecuteBid(null);
            }
        }

        public void ManualBidWithPriceCheck(double? maxPrice)
        {
            if (Driver == null) return;

            Log("Manual", "手動執行頭份投標 (含價格檢查)");

            if (SyncTime())
            {
                Log("Sync", "開始投標前時間同步完成");
            }

            var currentPrice = GetPrice();
            if (currentPrice.HasValue)
            {
                Log("Info", $"目前價格: {currentPrice}");

                if (maxPrice.HasValue && currentPrice > maxPrice)
                {
                    Log("STOP", $"開始投標前價格檢查，{currentPrice} > {maxPrice}，取消投標");
                    return;
                }
            }

            if (_usePostMethod)
            {
                Log("Mode", "使用 POST 模式手動投標");
                ExecuteBidViaPost(null);
            }
            else
            {
                Log("Mode", "使用傳統 Selenium 模式手動投標");
                ExecuteBid(null);
            }
        }

        protected override void ExecuteBid(TaskConfig? config)
        {
            try
            {
                Log("Bid", "手動執行頭份競標流程");

                if (Driver == null)
                {
                    Log("Error", "Driver 尚未初始化，無法執行手動投標");
                    return;
                }

                if (config?.DynamicMaxPrice.HasValue == true)
                {
                    var finalPrice = GetPrice();
                    if (finalPrice.HasValue && finalPrice > config.DynamicMaxPrice)
                    {
                        Log("STOP", $"最終價格檢查，{finalPrice} > {config.DynamicMaxPrice}，取消投標");
                        return;
                    }
                }

                if (config != null)
                {
                    SetDelivery(config.DeliveryPreference);
                }
                else
                {
                    SetDelivery("自取");
                }

                Log("Click", "尋找並點擊競標按鈕...");

                var js = "var b=document.querySelector(\"input[type='submit']\"); if(b) b.click(); else { var b2=document.querySelector(\"button\"); if(b2 && b2.innerText.indexOf('投標')!=-1) b2.click(); }";

                if (Driver is IJavaScriptExecutor jsExecutor)
                {
                    Log("Submit", "手動 JavaScript 點擊投標按鈕");
                    jsExecutor.ExecuteScript(js);
                    Log("Clicked", "投標按鈕點擊完成");
                    HasBid = true;
                    Log("OK", "頭份競標完成");
                }
                else
                {
                    Log("Error", "Driver 不支援 JavaScript 手動");
                }
            }
            catch (Exception ex)
            {
                Log("Error", $"頭份競標手動失敗: {ex.Message}");
            }
        }

        private static DateTime? ParseRocTime(string text)
        {
            var match = Regex.Match(text, @"(\d{3})\.(\d{1,2})\.(\d{1,2})\s+(\d{1,2}):(\d{2}):(\d{2})");
            if (match.Success)
            {
                int year = int.Parse(match.Groups[1].Value) + 1911;
                return new DateTime(
                    year,
                    int.Parse(match.Groups[2].Value),
                    int.Parse(match.Groups[3].Value),
                    int.Parse(match.Groups[4].Value),
                    int.Parse(match.Groups[5].Value),
                    int.Parse(match.Groups[6].Value)
                );
            }
            return null;
        }

        private bool SyncTime()
        {
            try
            {
                if (Driver == null) return false;

                var parser = GetParser();
                var bidInfo = parser.GetBidInfo();

                if (bidInfo?.RemainingTime.HasValue == true)
                {
                    _targetEndTime = DateTime.Now.Add(bidInfo.RemainingTime.Value);
                    return true;
                }

                string body = Driver.FindElement(By.TagName("body")).Text;
                var mNow = Regex.Match(body, @"現在時間:(\d{3}\.\d{1,2}\.\d{1,2}\s+\d{1,2}:\d{2}:\d{2}(?:\.\d+)?)");
                var mEnd = Regex.Match(body, @"截止時間:(\d{3}\.\d{1,2}\.\d{1,2}\s+\d{1,2}:\d{2}:\d{2})");

                if (mNow.Success && mEnd.Success)
                {
                    var now = ParseRocTime(mNow.Groups[1].Value);
                    var end = ParseRocTime(mEnd.Groups[1].Value);
                    if (now.HasValue && end.HasValue)
                    {
                        TimeSpan diff = end.Value - now.Value;
                        _targetEndTime = DateTime.Now.Add(diff);
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Log("Error", $"時間同步錯誤: {ex.Message}");
                return false;
            }
        }

        private int? GetPrice()
        {
            try
            {
                if (Driver == null) return null;

                var parser = GetParser();
                var bidInfo = parser.GetBidInfo();

                if (bidInfo?.CurrentPrice.HasValue == true)
                {
                    return (int)bidInfo.CurrentPrice.Value;
                }

                var els = Driver.FindElements(By.TagName("select"));
                if (els.Count > 0)
                {
                    var sel = new SelectElement(els[0]);
                    string txt = sel.SelectedOption.Text.Replace(",", "");
                    var match = Regex.Match(txt, @"\d+");
                    return match.Success ? int.Parse(match.Value) : (int?)null;
                }

                return null;
            }
            catch (Exception ex)
            {
                Log("Debug", $"GetPrice 錯誤: {ex.Message}");
                return null;
            }
        }

        private void SetDelivery(string pref)
        {
            try
            {
                if (Driver == null) return;

                string js = $"var radios = document.getElementsByName('deliverway'); for(var i=0; i<radios.length; i++) {{ if(radios[i].value.indexOf('{pref}') != -1) radios[i].checked = true; }}";

                if (Driver is IJavaScriptExecutor jsExecutor)
                {
                    jsExecutor.ExecuteScript(js);
                    Log("Delivery", $"設定交貨方式: {pref}");
                }
            }
            catch (Exception ex)
            {
                Log("Warn", $"設定交貨方式失敗: {ex.Message}");
            }
        }

        private void TransferCookies()
        {
            try
            {
                if (Driver == null || _httpClient == null) return;

                var seleniumCookies = Driver.Manage().Cookies.AllCookies;
                Log("Debug", $"尋找到 {seleniumCookies.Count} 個 Cookies");

                var cookieContainer = new System.Net.CookieContainer();
                foreach (var cookie in seleniumCookies)
                {
                    try
                    {
                        var netCookie = new System.Net.Cookie(cookie.Name, cookie.Value, cookie.Path, cookie.Domain);
                        cookieContainer.Add(netCookie);
                        Log("Debug", $"轉移 Cookie: {cookie.Name} = {cookie.Value[..Math.Min(20, cookie.Value.Length)]}...");
                    }
                    catch (Exception ex)
                    {
                        Log("Warn", $"Cookie 轉移錯誤: {cookie.Name} - {ex.Message}");
                    }
                }

                _httpClient?.Dispose();
                var handler = new HttpClientHandler()
                {
                    CookieContainer = cookieContainer,
                    UseCookies = true
                };

                _httpClient = new HttpClient(handler);
                _httpClient.DefaultRequestHeaders.Add("User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

                Log("OK", "Cookies 轉移成功");
            }
            catch (Exception ex)
            {
                Log("Error", $"Cookies 轉移錯誤: {ex.Message}");
            }
        }

        private void ParseBidFormForPost()
        {
            try
            {
                Log("Parse", "開始解析投標表單...");

                if (Driver == null) return;

                Log("JS", "手動 submitOK() 函數解析表單");
                ((IJavaScriptExecutor)Driver).ExecuteScript(@"
                var f = document.getElementById('form1');
                if(f) {
                    f.submit = function() { 
                        console.log('Submit intercepted!'); 
                        return false; 
                    };
                    try { 
                        submitOK(); 
                        console.log('submitOK() executed successfully');
                    } catch(e) { 
                        console.log('submitOK() failed:', e); 
                    }
                } else {
                    console.log('form1 not found');
                }
            ");

                Thread.Sleep(500);

                var payload = new Dictionary<string, string>();
                var doc = new HtmlAgilityPack.HtmlDocument();
                doc.LoadHtml(Driver.PageSource);

                var form = doc.DocumentNode.SelectSingleNode("//form[@id='form1']");
                if (form == null)
                {
                    Log("Error", "找不到 form1 表單");
                    return;
                }

                Log("Debug", "找到競標表單，開始解析欄位...");

                var inputs = form.SelectNodes(".//input | .//select | .//button");
                if (inputs != null)
                {
                    Log("Debug", $"處理 {inputs.Count} 個表單元素");

                    foreach (var element in inputs)
                    {
                        var name = element.GetAttributeValue("name", "");
                        var type = element.GetAttributeValue("type", "");
                        var value = element.GetAttributeValue("value", "");
                        var tagName = element.Name;

                        if (string.IsNullOrEmpty(name)) continue;

                        if (string.Equals(tagName, "select", StringComparison.OrdinalIgnoreCase))
                        {
                            var options = element.SelectNodes(".//option");
                            if (options != null && options.Count > 0)
                            {
                                var firstOption = options[0];
                                var optionValue = firstOption.GetAttributeValue("value", "");
                                var optionText = firstOption.InnerText.Trim();

                                payload[name] = optionValue;
                                Log("Price", $"自動選擇價格: {name} = {optionValue} ({optionText})");
                            }
                        }
                        else if (string.Equals(type, "radio", StringComparison.OrdinalIgnoreCase) &&
                                 string.Equals(name, "deliverway", StringComparison.OrdinalIgnoreCase))
                        {
                            if (value.Contains("自取", StringComparison.OrdinalIgnoreCase))
                            {
                                payload[name] = value;
                                Log("Delivery", $"選擇交貨方式: {value}");
                            }
                        }
                        else if (string.Equals(type, "hidden", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(type, "submit", StringComparison.OrdinalIgnoreCase))
                        {
                            payload[name] = value;
                            if (name.StartsWith('X'))
                            {
                                var hiddenDisplayValue = value.Length > 15 ? value[..15] + "..." : value;
                                Log("Hidden", $"隱藏欄位表單: {name} = {hiddenDisplayValue}");
                            }
                        }
                    }
                }

                if (!payload.ContainsKey("deliverway"))
                {
                    var deliveryInputs = form.SelectNodes(".//input[@name='deliverway']");
                    if (deliveryInputs != null)
                    {
                        foreach (var input in deliveryInputs)
                        {
                            var val = input.GetAttributeValue("value", "");
                            if (val.Contains("自取", StringComparison.OrdinalIgnoreCase))
                            {
                                payload["deliverway"] = val;
                                Log("Delivery", $"開始選擇交貨方式: {val}");
                                break;
                            }
                        }

                        if (!payload.ContainsKey("deliverway") && deliveryInputs.Count > 0)
                        {
                            var fallbackValue = deliveryInputs[0].GetAttributeValue("value", "");
                            payload["deliverway"] = fallbackValue;
                            Log("Delivery", $"其他選擇交貨方式: {fallbackValue}");
                        }
                    }
                }

                _lastParsedPayload = payload;
                Log("OK", $"表單解析完成，共 {payload.Count} 個欄位");
            }
            catch (Exception ex)
            {
                Log("Error", $"表單解析失敗: {ex.Message}");
            }
        }

        private async void ExecuteBidViaPost(TaskConfig? config)
        {
            try
            {
                Log("PostBid", "開始 POST 競標流程");

                if (_httpClient == null)
                {
                    Log("Error", "HttpClient 尚未初始化");
                    return;
                }

                if (_lastParsedPayload == null)
                {
                    Log("Warn", "表單資料尚未解析，嘗試重新解析...");
                    ParseBidFormForPost();
                    if (_lastParsedPayload == null)
                    {
                        Log("Error", "無法取得表單資料");
                        return;
                    }
                }

                if (config?.DynamicMaxPrice.HasValue == true)
                {
                    var finalPrice = GetPrice();
                    if (finalPrice.HasValue && finalPrice > config.DynamicMaxPrice)
                    {
                        Log("STOP", $"最終價格檢查，{finalPrice} > {config.DynamicMaxPrice}，取消投標");
                        return;
                    }
                }

                Encoding big5;
                try
                {
                    big5 = Encoding.GetEncoding("big5");
                }
                catch
                {
                    big5 = Encoding.UTF8;
                }

                var postData = string.Join("&", _lastParsedPayload.Select(kvp =>
                    $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"
                ));

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                _httpClient.DefaultRequestHeaders.Add("Referer", _currentUrl);
                _httpClient.DefaultRequestHeaders.Add("Origin", "https://epai.taitung.gov.tw");

                var content = new StringContent(postData, big5, "application/x-www-form-urlencoded");

                Log("Submit", "發送 POST 請求...");
                var stopwatch = Stopwatch.StartNew();

                var response = await _httpClient.PostAsync("https://epai.taitung.gov.tw/bid.asp", content);

                stopwatch.Stop();
                Log("Clicked", $"POST 請求完成，耗時: {stopwatch.ElapsedMilliseconds}ms");

                var responseBytes = await response.Content.ReadAsByteArrayAsync();
                var responseText = big5.GetString(responseBytes);

                Log("Response", $"回應狀態: {(int)response.StatusCode} ({response.StatusCode})");

                if (responseText.Contains("標價成功"))
                {
                    Log("Success", "確認 POST 競標成功");
                    HasBid = true;
                }
                else if (responseText.Contains("alert("))
                {
                    var alertMsg = ExtractAlertMessage(responseText);
                    Log("Alert", $"系統提示訊息: {alertMsg}");
                }
                else
                {
                    Log("Unknown", "未能確認狀態，回應內容需要進一步分析");
                    var debugFile = $"TaitungBid_Response_{DateTime.Now:yyyyMMdd_HHmmss}.html";
                    await System.IO.File.WriteAllTextAsync(debugFile, responseText, Encoding.UTF8);
                    Log("Debug", $"回應內容已儲存至: {debugFile}");
                }
            }
            catch (Exception ex)
            {
                Log("Error", $"POST 競標失敗: {ex.Message}");
            }
        }

        private static string ExtractAlertMessage(string html)
        {
            try
            {
                var match = Regex.Match(html, @"alert\('([^']+)'\)");
                if (match.Success)
                {
                    var rawMsg = match.Groups[1].Value;
                    if (rawMsg.Contains("請選") || rawMsg.Contains("交貨"))
                        return "請選擇交貨方式";
                    if (rawMsg.Contains("驗證碼"))
                        return "驗證碼錯誤";
                    return rawMsg;
                }
                return "無法解析警告訊息";
            }
            catch
            {
                return "訊息解析失敗";
            }
        }

        public void CleanupResources()
        {
            try
            {
                _httpClient?.Dispose();
                _httpClient = null;
                Log("Cleanup", "POST 資源清理完成");
            }
            catch (Exception ex)
            {
                Log("Error", $"清理資源時發生錯誤: {ex.Message}");
            }
        }

        ~TaitungBidder()
        {
            CleanupResources();
        }
    }
}