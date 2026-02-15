using OpenQA.Selenium;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
// 明確指定使用 HtmlAgilityPack 的命名空間
using HtmlDocument = HtmlAgilityPack.HtmlDocument;
using HtmlNode = HtmlAgilityPack.HtmlNode;

namespace AuctionBidder.Core
{
    /// <summary>
    /// 網站類型枚舉
    /// </summary>
    public enum WebsiteType
    {
        Unknown,
        Taipei,    // 台北惜物網
        Taitung    // 台東E拍網
    }

    /// <summary>
    /// 網頁內容解析器 - 專門處理各種政府採購網站的HTML解析
    /// </summary>
    public class WebPageParser(IWebDriver driver, Action<string, string>? logCallback = null)
    {
        private readonly IWebDriver _driver = driver ?? throw new ArgumentNullException(nameof(driver));
        private readonly Action<string, string>? _logCallback = logCallback;
        private WebsiteType? _cachedWebsiteType = null;

        private void Log(string tag, string message)
        {
            _logCallback?.Invoke(tag, message);
        }

        /// <summary>
        /// 智能判斷當前網站類型
        /// </summary>
        public WebsiteType DetectWebsiteType()
        {
            if (_cachedWebsiteType.HasValue)
            {
                return _cachedWebsiteType.Value;
            }

            try
            {
                var url = _driver.Url;
                var bodyText = _driver.FindElement(By.TagName("body")).Text;
                var pageSource = _driver.PageSource;

                // 根據 URL 判斷
                if (url.Contains("shwoo.gov.taipei"))
                {
                    _cachedWebsiteType = WebsiteType.Taipei;
                    Log("Detect", "偵測到台北惜物網");
                    return WebsiteType.Taipei;
                }

                if (url.Contains("epai.taitung.gov.tw"))
                {
                    _cachedWebsiteType = WebsiteType.Taitung;
                    Log("Detect", "偵測到台東E拍網");
                    return WebsiteType.Taitung;
                }

                // 根據頁面內容判斷
                if (bodyText.Contains("台北惜物網") || bodyText.Contains("臺北惜物網") ||
                    pageSource.Contains("shwoo") || bodyText.Contains("惜物"))
                {
                    _cachedWebsiteType = WebsiteType.Taipei;
                    Log("Detect", "根據內容偵測到台北惜物網");
                    return WebsiteType.Taipei;
                }

                if (bodyText.Contains("台東E拍網") || bodyText.Contains("臺東E拍網") ||
                    bodyText.Contains("現在時間:") || bodyText.Contains("截止時間:"))
                {
                    _cachedWebsiteType = WebsiteType.Taitung;
                    Log("Detect", "根據內容偵測到台東E拍網");
                    return WebsiteType.Taitung;
                }

                // 根據特有元素判斷
                if (pageSource.Contains("time_end") || pageSource.Contains("bidprice"))
                {
                    _cachedWebsiteType = WebsiteType.Taipei;
                    Log("Detect", "根據元素偵測到台北惜物網");
                    return WebsiteType.Taipei;
                }

                _cachedWebsiteType = WebsiteType.Unknown;
                Log("Warn", "無法判斷網站類型");
                return WebsiteType.Unknown;
            }
            catch (Exception ex)
            {
                Log("Error", $"判斷網站類型失敗: {ex.Message}");
                _cachedWebsiteType = WebsiteType.Unknown;
                return WebsiteType.Unknown;
            }
        }

        /// <summary>
        /// 直接抓取網頁所有文字內容並存檔
        /// </summary>
        public void SaveAllPageContent(string filename = "")
        {
            try
            {
                // 等待網頁完全載入
                System.Threading.Thread.Sleep(3000);

                if (string.IsNullOrEmpty(filename))
                {
                    var websiteType = DetectWebsiteType();
                    var prefix = websiteType switch
                    {
                        WebsiteType.Taipei => "Taipei",
                        WebsiteType.Taitung => "Taitung",
                        _ => "Unknown"
                    };
                    filename = $"{prefix}_PageContent_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                }

                var filePath = Path.Combine(Environment.CurrentDirectory, filename);

                using var writer = new StreamWriter(filePath, false, Encoding.UTF8);

                // 基本資訊
                writer.WriteLine($"=== 網頁內容記錄 - {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                writer.WriteLine($"URL: {_driver.Url}");
                writer.WriteLine($"Title: {_driver.Title}");
                writer.WriteLine($"網站類型: {DetectWebsiteType()}");
                writer.WriteLine();

                // 1. 抓取網頁所有可見文字
                writer.WriteLine("=== 網頁所有可見文字 ===");
                var bodyText = _driver.FindElement(By.TagName("body")).Text;
                writer.WriteLine(bodyText);
                writer.WriteLine();

                // 2. 抓取完整的 HTML 原始碼
                writer.WriteLine("=== 完整 HTML 原始碼 ===");
                writer.WriteLine(_driver.PageSource);

                Log("Debug", $"網頁內容已儲存至: {filePath}");
            }
            catch (Exception ex)
            {
                Log("Error", $"儲存網頁內容失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 智能解析倒數計時
        /// </summary>
        public TimeSpan? ParseCountdownTime()
        {
            var websiteType = DetectWebsiteType();

            return websiteType switch
            {
                WebsiteType.Taipei => ParseTaipeiCountdownTime(),
                WebsiteType.Taitung => ParseTaitungCountdownTime(),
                _ => ParseCountdownTimeGeneric()
            };
        }

        /// <summary>
        /// 解析台北網站的倒數計時
        /// </summary>
        private TimeSpan? ParseTaipeiCountdownTime()
        {
            try
            {
                // 方法1: 嘗試從 #time_end 元素取得
                try
                {
                    var timeEndElement = _driver.FindElement(By.Id("time_end"));
                    var timeText = timeEndElement.Text;

                    if (!string.IsNullOrWhiteSpace(timeText))
                    {
                        Log("Parse", $"台北時間元素文字: '{timeText}'");

                        // 檢查是否為初始化狀態（全為0）
                        if (timeText.Contains("00天00時00分00秒"))
                        {
                            Log("Warn", "偵測到初始化狀態，等待倒數計時器啟動...");

                            // 等待一下讓 JavaScript 初始化
                            System.Threading.Thread.Sleep(2000);

                            // 重新取得時間
                            timeText = timeEndElement.Text;
                            Log("Parse", $"重新取得時間文字: '{timeText}'");
                        }

                        var parsedTime = ParseTimeText(timeText);
                        if (parsedTime.HasValue && parsedTime.Value.TotalSeconds > 0)
                        {
                            Log("Success", $"台北時間解析成功: {parsedTime.Value}");
                            return parsedTime.Value;
                        }
                        else if (parsedTime.HasValue && parsedTime.Value.TotalSeconds == 0)
                        {
                            Log("Warn", "解析到0秒，可能是競標已結束或初始化中");
                            return parsedTime.Value;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log("Debug", $"台北 #time_end 解析失敗: {ex.Message}");
                }

                // 方法2: 嘗試從 JavaScript 變數取得時間
                try
                {
                    var jsResult = ((IJavaScriptExecutor)_driver).ExecuteScript(@"
                try {
                    // 嘗試取得各種可能的倒數時間變數
                    if (typeof remainingSeconds !== 'undefined' && remainingSeconds > 0) {
                        return remainingSeconds;
                    }
                    if (typeof timeLeft !== 'undefined' && timeLeft > 0) {
                        return timeLeft;
                    }
                    if (typeof countdown !== 'undefined' && countdown > 0) {
                        return countdown;
                    }
                    return null;
                } catch(e) {
                    return null;
                }
            ");

                    if (jsResult != null && double.TryParse(jsResult.ToString(), out double seconds) && seconds > 0)
                    {
                        var remaining = TimeSpan.FromSeconds(seconds);
                        Log("Success", $"JavaScript 時間解析成功: 剩餘 {remaining}");
                        return remaining;
                    }
                }
                catch (Exception jsEx)
                {
                    Log("Debug", $"JavaScript 時間解析失敗: {jsEx.Message}");
                }

                // 方法3: 通用解析方法
                return ParseCountdownTimeGeneric();
            }
            catch (Exception ex)
            {
                Log("Error", $"台北倒數計時解析失敗: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 解析台東網站的倒數計時
        /// </summary>
        private TimeSpan? ParseTaitungCountdownTime()
        {
            try
            {
                // 方法1: 直接從網頁文字中解析民國時間
                var bodyText = _driver.FindElement(By.TagName("body")).Text;

                var nowMatch = Regex.Match(bodyText, @"現在時間:(\d{3}\.\d{1,2}\.\d{1,2}\s+\d{1,2}:\d{2}:\d{2}(?:\.\d+)?)");
                var endMatch = Regex.Match(bodyText, @"截止時間:(\d{3}\.\d{1,2}\.\d{1,2}\s+\d{1,2}:\d{2}:\d{2})");

                if (nowMatch.Success && endMatch.Success)
                {
                    var nowTime = ParseRocTime(nowMatch.Groups[1].Value);
                    var endTime = ParseRocTime(endMatch.Groups[1].Value);

                    if (nowTime.HasValue && endTime.HasValue)
                    {
                        var remaining = endTime.Value - nowTime.Value;
                        // 移除 LOG 訊息
                        return remaining.TotalSeconds > 0 ? remaining : TimeSpan.Zero;
                    }
                }

                // 方法2: 嘗試從 JavaScript 變數中取得倒數時間
                try
                {
                    var jsResult = ((IJavaScriptExecutor)_driver).ExecuteScript("return typeof difftime !== 'undefined' ? difftime : null;");
                    if (jsResult != null && double.TryParse(jsResult.ToString(), out double seconds))
                    {
                        if (seconds > 0)
                        {
                            var remaining = TimeSpan.FromSeconds(seconds);
                            // 移除 LOG 訊息
                            return remaining;
                        }
                    }
                }
                catch (Exception jsEx)
                {
                    Log("Debug", $"JavaScript 解析失敗: {jsEx.Message}");
                }

                // 方法3: 通用解析方法（備用）
                return ParseCountdownTimeGeneric();
            }
            catch (Exception ex)
            {
                Log("Error", $"台東倒數計時解析失敗: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 智能取得競標資訊
        /// </summary>
        public BidInfo? GetBidInfo()
        {
            var websiteType = DetectWebsiteType();

            return websiteType switch
            {
                WebsiteType.Taipei => GetTaipeiBidInfo(),
                WebsiteType.Taitung => GetTaitungBidInfo(),
                _ => GetGenericBidInfo()
            };
        }

        /// <summary>
        /// 取得台北網站的競標資訊
        /// </summary>
        private BidInfo? GetTaipeiBidInfo()
        {
            try
            {
                var bodyText = _driver.FindElement(By.TagName("body")).Text;

                var bidInfo = new BidInfo
                {
                    // 台北網站的價格解析邏輯（需要根據實際內容調整）
                    CurrentPrice = ExtractTaipeiCurrentPrice(bodyText),
                    StartPrice = ExtractTaipeiStartPrice(bodyText),
                    Status = ExtractTaipeiStatus(bodyText),
                    RemainingTime = ParseCountdownTime()
                };

                Log("Info", $"台北競標資訊: 當前價格={bidInfo.CurrentPrice}, 起標價={bidInfo.StartPrice}, 狀態={bidInfo.Status}");
                return bidInfo;
            }
            catch (Exception ex)
            {
                Log("Error", $"取得台北競標資訊失敗: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 取得台東網站的競標資訊
        /// </summary>
        private BidInfo? GetTaitungBidInfo()
        {
            try
            {
                var bodyText = _driver.FindElement(By.TagName("body")).Text;

                var bidInfo = new BidInfo
                {
                    CurrentPrice = ExtractTaitungCurrentPrice(bodyText),
                    StartPrice = ExtractTaitungStartPrice(bodyText),
                    Status = ExtractTaitungStatus(bodyText),
                    RemainingTime = ParseCountdownTime()
                };

                // 移除 LOG 訊息，只在初始化時顯示
                return bidInfo;
            }
            catch (Exception ex)
            {
                Log("Error", $"取得台東競標資訊失敗: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 通用競標資訊取得
        /// </summary>
        private BidInfo? GetGenericBidInfo()
        {
            try
            {
                var doc = GetHtmlDocument();
                var bidInfo = new BidInfo
                {
                    CurrentPrice = ExtractPrice(doc,
                    [
                        "//td[contains(text(),'目前價格')]/following-sibling::td",
                        "//td[contains(text(),'當前價格')]/following-sibling::td",
                        "//td[contains(text(),'最高價')]/following-sibling::td",
                        "//*[contains(@class,'current-price')]",
                        "//*[@id='currentPrice']"
                    ]),
                    StartPrice = ExtractPrice(doc,
                    [
                        "//td[contains(text(),'起標價')]/following-sibling::td",
                        "//td[contains(text(),'底價')]/following-sibling::td",
                        "//*[contains(@class,'start-price')]"
                    ]),
                    Status = ExtractText(doc,
                    [
                        "//td[contains(text(),'狀態')]/following-sibling::td",
                        "//td[contains(text(),'競標狀態')]/following-sibling::td",
                        "//*[contains(@class,'bid-status')]"
                    ]),
                    RemainingTime = ParseCountdownTime()
                };

                Log("Info", $"通用競標資訊: 當前價格={bidInfo.CurrentPrice}, 起標價={bidInfo.StartPrice}, 狀態={bidInfo.Status}");
                return bidInfo;
            }
            catch (Exception ex)
            {
                Log("Error", $"取得通用競標資訊失敗: {ex.Message}");
                return null;
            }
        }

        #region 台北網站專用方法（待實作）

        private decimal? ExtractTaipeiCurrentPrice(string bodyText)
        {
            try
            {
                // 從 "目前出價 1,070元" 中提取當前最高價
                var currentPriceMatch = Regex.Match(bodyText, @"目前出價\s+([0-9,]+)元");
                if (currentPriceMatch.Success)
                {
                    var priceStr = currentPriceMatch.Groups[1].Value.Replace(",", "");
                    if (decimal.TryParse(priceStr, out decimal price))
                    {
                        // 移除 Debug 訊息，只在初始化時顯示
                        return price;
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                Log("Warn", $"提取台北當前價格失敗: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 根據價格取得級距 (for 台北惜物網)
        /// </summary>
        private static int GetPriceIncrement(int price)
        {
            if (price <= 500) return 10;
            if (price <= 1000) return 30;
            if (price <= 10000) return 100;
            if (price <= 50000) return 500;
            if (price <= 100000) return 1000;
            return 2000;
        }

        private decimal? ExtractTaipeiStartPrice(string bodyText)
        {
            try
            {
                var match = Regex.Match(bodyText, @"底價\s+新台幣\s+([0-9,]+)\s+元");
                if (match.Success)
                {
                    var priceStr = match.Groups[1].Value.Replace(",", "");
                    if (decimal.TryParse(priceStr, out decimal price))
                    {
                        // 移除 Debug 訊息
                        return price;
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                Log("Warn", $"提取台北起標價失敗: {ex.Message}");
                return null;
            }
        }

        private string? ExtractTaipeiStatus(string bodyText)
        {
            try
            {
                // 從出價人數和次數判斷狀態
                var bidInfoMatch = Regex.Match(bodyText, @"目前出價\s+[0-9,]+元\s+/\s+(\d+)\s+人出價");
                if (bidInfoMatch.Success)
                {
                    var bidderCount = int.Parse(bidInfoMatch.Groups[1].Value);
                    var status = $"進行中 ({bidderCount}人出價)";
                    // 移除 Debug 訊息
                    return status;
                }

                // 從出價次數判斷
                var bidCountMatch = Regex.Match(bodyText, @"本案您可出價\d+次，已出價(\d+)次");
                if (bidCountMatch.Success)
                {
                    var usedBids = int.Parse(bidCountMatch.Groups[1].Value);
                    return $"可競標 (已出價{usedBids}次)";
                }

                return "進行中";
            }
            catch (Exception ex)
            {
                Log("Warn", $"提取台北狀態失敗: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region 台東網站專用方法（已實作）

        private decimal? ExtractTaitungCurrentPrice(string bodyText)
        {
            try
            {
                var priceMatch = Regex.Match(bodyText, @"競價價格:\s*(\d+)");
                if (priceMatch.Success && decimal.TryParse(priceMatch.Groups[1].Value, out decimal price))
                {
                    return price;
                }

                var recordMatch = Regex.Match(bodyText, @"1\s+(\d+)元");
                if (recordMatch.Success && decimal.TryParse(recordMatch.Groups[1].Value, out decimal recordPrice))
                {
                    return recordPrice;
                }

                return null;
            }
            catch (Exception ex)
            {
                Log("Warn", $"提取台東當前價格失敗: {ex.Message}");
                return null;
            }
        }

        private decimal? ExtractTaitungStartPrice(string bodyText)
        {
            try
            {
                var match = Regex.Match(bodyText, @"底價\s+新台幣\s*(\d+)\s*元");
                if (match.Success && decimal.TryParse(match.Groups[1].Value, out decimal price))
                {
                    return price;
                }
                return null;
            }
            catch (Exception ex)
            {
                Log("Warn", $"提取台東起標價失敗: {ex.Message}");
                return null;
            }
        }

        private string? ExtractTaitungStatus(string bodyText)
        {
            try
            {
                var match = Regex.Match(bodyText, @"追蹤狀態\s+(.+?)(?:\r|\n|$)");
                if (match.Success)
                {
                    return match.Groups[1].Value.Trim();
                }
                return null;
            }
            catch (Exception ex)
            {
                Log("Warn", $"提取台東狀態失敗: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region 通用解析方法

        /// <summary>
        /// 通用的倒數計時解析方法
        /// </summary>
        private TimeSpan? ParseCountdownTimeGeneric()
        {
            try
            {
                var doc = GetHtmlDocument();

                var timeSelectors = new[]
                {
                    "//td[contains(text(),'剩餘時間')]/following-sibling::td",
                    "//td[contains(text(),'截止時間')]/following-sibling::td",
                    "//td[contains(text(),'結束時間')]/following-sibling::td",
                    "//td[contains(text(),'投標截止')]/following-sibling::td",
                    "//*[@id='countdown']",
                    "//*[@class='countdown']",
                    "//*[contains(@class,'time-remaining')]",
                    "//*[contains(@class,'countdown')]"
                };

                foreach (var selector in timeSelectors)
                {
                    var nodes = doc.DocumentNode.SelectNodes(selector);
                    if (nodes != null)
                    {
                        foreach (var node in nodes)
                        {
                            var timeText = CleanText(node.InnerText);

                            if (IsValidTimeText(timeText))
                            {
                                Log("Parse", $"找到時間文字: '{timeText}'");

                                var parsedTime = ParseTimeText(timeText);
                                if (parsedTime.HasValue)
                                {
                                    Log("Success", $"解析成功: {parsedTime.Value}");
                                    return parsedTime.Value;
                                }
                            }
                        }
                    }
                }

                Log("Warn", "未找到有效的倒數計時");
                return null;
            }
            catch (Exception ex)
            {
                Log("Error", $"通用倒數計時解析失敗: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 取得當前頁面的HTML文檔
        /// </summary>
        public HtmlDocument GetHtmlDocument()
        {
            try
            {
                var pageSource = _driver.PageSource;
                var doc = new HtmlDocument();
                doc.LoadHtml(pageSource);
                return doc;
            }
            catch (Exception ex)
            {
                Log("Error", $"取得HTML文檔失敗: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 解析民國時間格式
        /// </summary>
        private static DateTime? ParseRocTime(string text)
        {
            var match = Regex.Match(text, @"(\d{3})\.(\d{1,2})\.(\d{1,2})\s+(\d{1,2}):(\d{2}):(\d{2})(?:\.(\d+))?");
            if (match.Success)
            {
                int year = int.Parse(match.Groups[1].Value) + 1911;
                int month = int.Parse(match.Groups[2].Value);
                int day = int.Parse(match.Groups[3].Value);
                int hour = int.Parse(match.Groups[4].Value);
                int minute = int.Parse(match.Groups[5].Value);
                int second = int.Parse(match.Groups[6].Value);

                return new DateTime(year, month, day, hour, minute, second);
            }
            return null;
        }

        /// <summary>
        /// 檢查是否為有效的時間文字
        /// </summary>
        private static bool IsValidTimeText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            if (text.Length > 100) return false;

            var excludeKeywords = new[]
            {
                "拍賣案", "出價級距", "定義", "計算", "百分之", "不足", "以上", "規定", "說明",
                "注意", "提醒", "條件", "限制", "方式", "辦法", "規則", "流程", "步驟"
            };

            foreach (var keyword in excludeKeywords)
            {
                if (text.Contains(keyword)) return false;
            }

            var timeKeywords = new[] { "天", "時", "分", "秒", ":", "剩", "截止", "結束" };
            return timeKeywords.Any(keyword => text.Contains(keyword));
        }

        /// <summary>
        /// 解析時間文字 - 統一的時間解析邏輯
        /// </summary>
        public TimeSpan? ParseTimeText(string timeText)
        {
            if (string.IsNullOrWhiteSpace(timeText)) return null;

            // 清理文字
            timeText = timeText.Trim().Replace("結束", "").Replace("剩餘", "").Trim();

            var timePatterns = new[]
            {
        new TimePattern(@"(\d+)天(\d+)時(\d+)分(\d+)秒", (m) => new TimeSpan(
            int.Parse(m.Groups[1].Value),
            int.Parse(m.Groups[2].Value),
            int.Parse(m.Groups[3].Value),
            int.Parse(m.Groups[4].Value))),

        new TimePattern(@"(\d+)時(\d+)分(\d+)秒", (m) => new TimeSpan(
            int.Parse(m.Groups[1].Value),
            int.Parse(m.Groups[2].Value),
            int.Parse(m.Groups[3].Value))),

        new TimePattern(@"(\d+)分(\d+)秒", (m) => new TimeSpan(
            0,
            int.Parse(m.Groups[1].Value),
            int.Parse(m.Groups[2].Value))),

        new TimePattern(@"(\d+)秒", (m) => new TimeSpan(
            0, 0,
            int.Parse(m.Groups[1].Value))),

        new TimePattern(@"(\d+):(\d+):(\d+)", (m) => new TimeSpan(
            int.Parse(m.Groups[1].Value),
            int.Parse(m.Groups[2].Value),
            int.Parse(m.Groups[3].Value)))
    };

            foreach (var pattern in timePatterns)
            {
                var match = Regex.Match(timeText, pattern.Pattern);
                if (match.Success)
                {
                    try
                    {
                        var result = pattern.Parser(match);

                        // 特別處理：如果解析結果為全0，可能是初始化狀態
                        if (result.TotalSeconds == 0)
                        {
                            Log("Debug", $"解析到0秒時間: '{timeText}' - 可能為初始化狀態");
                        }

                        return result;
                    }
                    catch (Exception ex)
                    {
                        Log("Warn", $"解析模式 '{pattern.Pattern}' 失敗: {ex.Message}");
                        continue;
                    }
                }
            }

            if (TryParseDateTime(timeText, out DateTime endTime))
            {
                var remaining = endTime - DateTime.Now;
                return remaining.TotalSeconds > 0 ? remaining : TimeSpan.Zero;
            }

            Log("Warn", $"無法解析時間格式: '{timeText}'");
            return null;
        }

        /// <summary>
        /// 檢查是否可以競標
        /// </summary>
        public bool CanBid()
        {
            try
            {
                var elements = _driver.FindElements(By.XPath("//input[@type='submit' and contains(@value,'送出')]"));
                if (elements.Count > 0)
                {
                    Log("Check", "找到競標按鈕");
                    return true;
                }

                var doc = GetHtmlDocument();
                var bidButtonSelectors = new[]
                {
                    "//input[@type='submit' and contains(@value,'出價')]",
                    "//input[@type='submit' and contains(@value,'競標')]",
                    "//input[@type='submit' and contains(@value,'送出')]",
                    "//button[contains(text(),'出價')]",
                    "//button[contains(text(),'競標')]",
                    "//*[@id='bidButton']",
                    "//*[contains(@class,'bid-button')]"
                };

                foreach (var selector in bidButtonSelectors)
                {
                    var node = doc.DocumentNode.SelectSingleNode(selector);
                    if (node != null)
                    {
                        var disabled = node.GetAttributeValue("disabled", "");
                        if (string.IsNullOrEmpty(disabled))
                        {
                            Log("Check", "找到可用的競標按鈕");
                            return true;
                        }
                    }
                }

                Log("Check", "未找到可用的競標按鈕");
                return false;
            }
            catch (Exception ex)
            {
                Log("Error", $"檢查競標狀態失敗: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 取得登入相關元素
        /// </summary>
        public LoginElements? GetLoginElements()
        {
            try
            {
                var doc = GetHtmlDocument();
                var elements = new LoginElements
                {
                    UsernameSelector = FindElementSelector(doc,
                    [
                        "//input[@type='text' and (contains(@name,'user') or contains(@name,'account'))]",
                        "//input[@id='username']",
                        "//input[@id='account']",
                        "//input[@name='username']"
                    ]),

                    PasswordSelector = FindElementSelector(doc,
                    [
                        "//input[@type='password']",
                        "//input[@name='password']",
                        "//input[@id='password']"
                    ]),

                    CaptchaSelector = FindElementSelector(doc,
                    [
                        "//input[contains(@name,'captcha')]",
                        "//input[contains(@name,'code')]",
                        "//input[contains(@name,'verify')]"
                    ]),

                    LoginButtonSelector = FindElementSelector(doc,
                    [
                        "//input[@type='submit' and contains(@value,'登入')]",
                        "//button[contains(text(),'登入')]",
                        "//input[@type='submit' and contains(@value,'送出')]"
                    ])
                };

                return elements;
            }
            catch (Exception ex)
            {
                Log("Error", $"取得登入元素失敗: {ex.Message}");
                return null;
            }
        }

        private static string CleanText(string text)
        {
            return text?.Trim().Replace("\n", "").Replace("\r", "").Replace("\t", " ") ?? "";
        }

        private static decimal? ExtractPrice(HtmlDocument doc, string[] selectors)
        {
            foreach (var selector in selectors)
            {
                var node = doc.DocumentNode.SelectSingleNode(selector);
                if (node != null)
                {
                    var priceText = CleanText(node.InnerText);
                    var priceMatch = Regex.Match(priceText, @"[\d,]+");
                    if (priceMatch.Success && decimal.TryParse(priceMatch.Value.Replace(",", ""), out decimal price))
                    {
                        return price;
                    }
                }
            }
            return null;
        }

        private static string? ExtractText(HtmlDocument doc, string[] selectors)
        {
            foreach (var selector in selectors)
            {
                var node = doc.DocumentNode.SelectSingleNode(selector);
                if (node != null)
                {
                    return CleanText(node.InnerText);
                }
            }
            return null;
        }

        private static string? FindElementSelector(HtmlDocument doc, string[] selectors)
        {
            foreach (var selector in selectors)
            {
                var node = doc.DocumentNode.SelectSingleNode(selector);
                if (node != null)
                {
                    return selector;
                }
            }
            return null;
        }

        private static bool TryParseDateTime(string dateText, out DateTime dateTime)
        {
            dateTime = default;

            var formats = new[]
            {
                "yyyy-MM-dd HH:mm:ss",
                "yyyy/MM/dd HH:mm:ss",
                "MM/dd/yyyy HH:mm:ss",
                "dd/MM/yyyy HH:mm:ss",
                "yyyy-MM-dd HH:mm",
                "yyyy/MM/dd HH:mm"
            };

            foreach (var format in formats)
            {
                if (DateTime.TryParseExact(dateText, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out dateTime))
                {
                    return true;
                }
            }

            return DateTime.TryParse(dateText, out dateTime);
        }

        #endregion
    }

    #region 資料模型

    /// <summary>
    /// 時間解析模式
    /// </summary>
    public class TimePattern(string pattern, Func<Match, TimeSpan> parser)
    {
        public string Pattern { get; } = pattern;
        public Func<Match, TimeSpan> Parser { get; } = parser;
    }

    /// <summary>
    /// 競標資訊
    /// </summary>
    public class BidInfo
    {
        public decimal? CurrentPrice { get; set; }
        public decimal? StartPrice { get; set; }
        public string? Status { get; set; }
        public TimeSpan? RemainingTime { get; set; }
        public bool CanBid { get; set; }
    }

    /// <summary>
    /// 登入元素
    /// </summary>
    public class LoginElements
    {
        public string? UsernameSelector { get; set; }
        public string? PasswordSelector { get; set; }
        public string? CaptchaSelector { get; set; }
        public string? LoginButtonSelector { get; set; }
    }

    #endregion
}
