using AuctionBidder.Models;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using System;
using System.Text.RegularExpressions;
using System.Threading;

namespace AuctionBidder.Core
{
    public class TaipeiBidder(string name, Action<string, string> logCallback) : BaseBidder(name, logCallback)
    {
        private DateTime? _targetEndTime = null;

        // 保留原有的正則表達式模式，用於特殊情況
#pragma warning disable SYSLIB1045
        private static readonly Regex TimePattern = new(@"(\d+)\s*天\s*(\d+)\s*時\s*(\d+)\s*分\s*(\d+)\s*秒", RegexOptions.Compiled);
        private static readonly Regex NumberPattern = new(@"\d+", RegexOptions.Compiled);
#pragma warning restore SYSLIB1045

        public override bool NavigateToPage(string url)
        {
            try
            {
                if (Driver == null) return false;

                Driver.Navigate().GoToUrl(url);
                Log("Web", "前往台北商品頁");

                // 重新建立解析器
                RefreshParser();

                try
                {
                    WebDriverWait wait = new(Driver, TimeSpan.FromSeconds(20));

                    // 等待倒數計時元素載入
                    wait.Until(d => d.FindElements(By.Id("time_end")).Count > 0);
                    Log("OK", "商品頁載入確認 (偵測到 #time_end)");

                    // *** 新增：儲存網頁內容以供分析 ***
                    var parser = GetParser();
                    parser.SaveAllPageContent($"Taipei_PageContent_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

                    // 等待頁面完全載入和 JavaScript 初始化
                    System.Threading.Thread.Sleep(3000);

                    // 使用解析器取得初始競標資訊
                    var bidInfo = parser.GetBidInfo();

                    if (bidInfo?.RemainingTime.HasValue == true)
                    {
                        Log("Info", $"剩餘時間: {bidInfo.RemainingTime.Value:dd\\天hh\\時mm\\分ss\\秒}");
                    }

                    if (bidInfo?.CurrentPrice.HasValue == true)
                    {
                        Log("Info", $"當前價格: {bidInfo.CurrentPrice:N0}");
                    }
                }
                catch
                {
                    Log("Warn", "找不到 #time_end 倒數計時器，嘗試使用通用解析...");

                    // *** 備用方案也要儲存網頁內容 ***
                    var parser = GetParser();
                    parser.SaveAllPageContent($"Taipei_PageContent_Fallback_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

                    var bidInfo = parser.GetBidInfo();

                    if (bidInfo != null)
                    {
                        Log("OK", "使用通用解析器成功取得頁面資訊");
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Log("Error", ex.Message);
                return false;
            }
        }

        public override void AutoLogin(string loginUrl)
        {
            if (Driver == null) return;

            try
            {
                Log("Web", "前往登入頁...");
                Driver.Navigate().GoToUrl(loginUrl);

                // 重新建立解析器
                RefreshParser();

                WebDriverWait wait = new(Driver, TimeSpan.FromSeconds(15));
                wait.Until(d => d.FindElements(By.XPath("//*[contains(text(), '忘記帳號') or contains(text(), '登入')]")).Count > 0);

                // 使用解析器取得登入元素
                var parser = GetParser();
                var loginElements = parser.GetLoginElements();

                if (!string.IsNullOrEmpty(Config.TAIPEI_USER))
                {
                    Log("Input", "填寫帳密...");
                    try
                    {
                        // 優先使用解析器找到的元素，否則使用原有邏輯
                        IWebElement? userBox = null;
                        IWebElement? passBox = null;
                        IWebElement? captchaBox = null;

                        if (loginElements?.UsernameSelector != null)
                        {
                            userBox = Driver.FindElement(By.XPath(loginElements.UsernameSelector));
                        }
                        else
                        {
                            userBox = Driver.FindElement(By.XPath("//input[contains(@placeholder, '帳號') or @name='ID_NO']"));
                        }

                        if (loginElements?.PasswordSelector != null)
                        {
                            passBox = Driver.FindElement(By.XPath(loginElements.PasswordSelector));
                        }
                        else
                        {
                            passBox = Driver.FindElement(By.XPath("//input[@type='password']"));
                        }

                        userBox.SendKeys(Config.TAIPEI_USER);
                        passBox.SendKeys(Config.TAIPEI_PASS);

                        // 尋找驗證碼輸入框
                        if (loginElements?.CaptchaSelector != null)
                        {
                            captchaBox = Driver.FindElement(By.XPath(loginElements.CaptchaSelector));
                        }
                        else
                        {
                            captchaBox = Driver.FindElement(By.XPath("//input[contains(@placeholder, '驗證碼') or @name='LOGIN_CHECK_NO']"));
                        }

                        captchaBox.Click();
                        Log("Focus", "游標已聚焦驗證碼");
                    }
                    catch (Exception ex)
                    {
                        Log("Warn", $"自動填寫失敗: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log("Error", ex.Message);
            }
        }

        public override void RunMonitor(TaskConfig config, CancellationToken token)
        {
            IsRunning = true;
            _targetEndTime = null;
            int? lastPrice = null;
            long lastRefreshTime = 0;
            long lastLogSecond = 0;

            Log("Set", "開始台北競標監控 (v8.22 Final + WebParser)...");

            while (!token.IsCancellationRequested && IsRunning && Driver != null)
            {
                try
                {
                    DateTime curr = DateTime.Now;

                    // A. 鎖定時間
                    if (_targetEndTime == null)
                    {
                        var sec = SyncTime();

                        if (sec.HasValue)
                        {
                            _targetEndTime = DateTime.Now.AddSeconds(sec.Value);
                            Log("Lock", $"鎖定截標時刻: {_targetEndTime:HH:mm:ss.fff} (剩 {sec.Value} 秒)");
                        }
                        else
                        {
                            Thread.Sleep(100);
                        }
                        continue;
                    }

                    // B. 計算倒數
                    long remainMs = (long)(_targetEndTime.Value - curr).TotalMilliseconds;
                    bool isSprint = remainMs <= (config.SprintStartSec * 1000);

                    // C. 觸發檢查 - 使用動態的 TriggerMs
                    if (remainMs <= config.TriggerMs)
                    {
                        if (remainMs < -5000)
                        {
                            Log("Info", "時間已過期，停止監控。");
                            break;
                        }

                        Log("Trig", $"觸發! 剩 {remainMs} ms (設定: {config.TriggerMs} ms)");
                        var finalP = GetPrice();

                        // 價格限制檢查
                        if (config.DynamicMaxPrice.HasValue && finalP.HasValue && finalP > config.DynamicMaxPrice)
                        {
                            Log("STOP", $"價格 {finalP} 超過上限 {config.DynamicMaxPrice}，放棄競標。");
                            break;
                        }

                        if (config.RealBid)
                        {
                            ((IJavaScriptExecutor)Driver).ExecuteScript("goBid();");
                            Log("STOP", "出價完畢");
                        }
                        else
                        {
                            Log("Safe", "模擬觸發 (未勾選確認)");
                            Thread.Sleep(2000);
                        }
                        break;
                    }

                    // D. 刷新與 Log
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
                                jsExecutor.ExecuteScript("reloadVal = 0; reloadBidInfo();");
                            }
                            Thread.Sleep(150);
                        }

                        var p = GetPrice();
                        if (p.HasValue) lastPrice = p;
                        lastRefreshTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                        string pStr = lastPrice.HasValue ? $"${lastPrice}" : "[NULL]";
                        string tag = isSprint ? "[Dash]" : "[Cruise]";

                        TimeSpan ts = TimeSpan.FromMilliseconds(remainMs);
                        string timeStr = $"{ts.Days:D2}天{ts.Hours:D2}時{ts.Minutes:D2}分{ts.Seconds:D2}秒.{ts.Milliseconds:D3}";

                        // 顯示價格限制和觸發時間資訊
                        string limitStr = config.DynamicMaxPrice.HasValue ? $" (上限:{config.DynamicMaxPrice})" : "";

                        // 計算實際觸發時刻
                        DateTime actualTriggerTime = _targetEndTime.Value.AddMilliseconds(-config.TriggerMs);
                        string targetTimeStr = $" @ {actualTriggerTime:HH:mm:ss.fff}";

                        if (!isSprint || (isSprint && remainMs % 1000 < 300))
                            Log("Time", $"{timeStr} | {pStr}{limitStr} {tag}{targetTimeStr}");
                    }

                    Thread.Sleep(isSprint ? 1 : 50);
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

            Log("Manual", "執行台北手動競標");

            // 檢查當前價格
            var currentPrice = GetPrice();
            if (currentPrice.HasValue)
            {
                Log("Info", $"當前價格: {currentPrice}");
            }

            ExecuteBid(null);
        }

        /// <summary>
        /// 台北特有的競標執行邏輯
        /// </summary>
        protected override void ExecuteBid(TaskConfig? config)
        {
            try
            {
                Log("Bid", "執行台北競標操作");

                // 檢查 Driver 是否可用
                if (Driver == null)
                {
                    Log("Error", "Driver 未初始化，無法執行競標");
                    return;
                }

                // 最後一次價格檢查
                if (config?.DynamicMaxPrice.HasValue == true)
                {
                    var finalPrice = GetPrice();
                    if (finalPrice.HasValue && finalPrice > config.DynamicMaxPrice)
                    {
                        Log("STOP", $"最終價格檢查：{finalPrice} > {config.DynamicMaxPrice}，取消競標");
                        return;
                    }
                }

                // 執行台北網站的競標操作
                if (Driver is IJavaScriptExecutor jsExecutor)
                {
                    jsExecutor.ExecuteScript("goBid();");
                    HasBid = true;
                    Log("OK", "台北競標完成");
                }
                else
                {
                    Log("Error", "Driver 不支援 JavaScript 執行");
                }
            }
            catch (Exception ex)
            {
                Log("Error", $"台北競標執行失敗: {ex.Message}");
            }
        }

        // 保留原有的時間同步方法
        private int? SyncTime()
        {
            try
            {
                if (Driver == null) return null;

                // 優先使用 WebPageParser
                var parser = GetParser();
                var bidInfo = parser.GetBidInfo();

                if (bidInfo?.RemainingTime.HasValue == true)
                {
                    return (int)bidInfo.RemainingTime.Value.TotalSeconds;
                }

                // 備用：使用原有的特定邏輯
                var elements = Driver.FindElements(By.XPath("//*[@id='time_end'] | //*[@class='reciprocal']"));

                foreach (var el in elements)
                {
                    if (!el.Displayed) continue;

                    string txt = el.Text;
                    if (string.IsNullOrWhiteSpace(txt)) txt = el.GetAttribute("textContent") ?? string.Empty;

                    if (string.IsNullOrWhiteSpace(txt)) continue;

                    if (txt.Contains('天') && txt.Contains('秒'))
                    {
                        var sec = ParseTimeSeconds(txt);

                        if (sec.HasValue && sec.Value > 0)
                        {
                            return sec;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log("Error", $"SyncTime 異常: {ex.Message}");
            }
            return null;
        }

        private int? GetPrice()
        {
            try
            {
                if (Driver == null) return null;

                // 方法1: 使用原來可行的 SelectElement 邏輯（最可靠）
                try
                {
                    var selectElement = new SelectElement(Driver.FindElement(By.Id("bidprice")));
                    string txt = selectElement.SelectedOption.Text.Replace(",", "").Replace("元", "");

                    var match = NumberPattern.Match(txt);
                    if (match.Success)
                    {
                        var price = int.Parse(match.Value);
                        // 移除 Debug 訊息
                        return price;
                    }
                }
                catch (Exception ex)
                {
                    // 只在真正失敗時才記錄
                    Log("Debug", $"SelectElement 解析失敗: {ex.Message}");
                }

                // 方法2: 備用 - 使用 WebPageParser 取得當前價格，然後計算下次出價
                var parser = GetParser();
                var bidInfo = parser.GetBidInfo();

                if (bidInfo?.CurrentPrice.HasValue == true)
                {
                    var currentPrice = (int)bidInfo.CurrentPrice.Value;
                    var nextBidPrice = CalculateNextBidPrice(currentPrice);
                    // 移除 Debug 訊息
                    return nextBidPrice;
                }

                return null;
            }
            catch (Exception ex)
            {
                Log("Debug", $"GetPrice 失敗: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 根據台北惜物網規則計算下次出價價格
        /// </summary>
        private static int CalculateNextBidPrice(int currentPrice)
        {
            // 根據台北惜物網的級距規則
            if (currentPrice <= 500)
            {
                return currentPrice + 10;  // 500元以下以10元為級距
            }
            else if (currentPrice <= 1000)
            {
                return currentPrice + 30;  // 501元~1,000元以30元為級距
            }
            else if (currentPrice <= 10000)
            {
                return currentPrice + 100; // 1,001元~10,000元以100元為級距
            }
            else if (currentPrice <= 50000)
            {
                return currentPrice + 500; // 10,001元~50,000元以500元為級距
            }
            else if (currentPrice <= 100000)
            {
                return currentPrice + 1000; // 50,001元~100,000元以1,000元為級距
            }
            else
            {
                return currentPrice + 2000; // 100,001元以上以2,000元為級距
            }
        }

        private static int? ParseTimeSeconds(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            text = text.Trim().Replace("\r", "").Replace("\n", "");

            var matchFull = TimePattern.Match(text);

            if (matchFull.Success)
            {
                int days = int.Parse(matchFull.Groups[1].Value);
                int hours = int.Parse(matchFull.Groups[2].Value);
                int minutes = int.Parse(matchFull.Groups[3].Value);
                int seconds = int.Parse(matchFull.Groups[4].Value);

                return days * 86400 + hours * 3600 + minutes * 60 + seconds;
            }

            var numbers = NumberPattern.Matches(text);
            if (numbers.Count >= 4)
            {
                int days = int.Parse(numbers[0].Value);
                int hours = int.Parse(numbers[1].Value);
                int minutes = int.Parse(numbers[2].Value);
                int seconds = int.Parse(numbers[3].Value);

                return days * 86400 + hours * 3600 + minutes * 60 + seconds;
            }

            return null;
        }
    }
}
