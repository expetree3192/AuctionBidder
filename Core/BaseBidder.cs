using AuctionBidder.Models;
using OpenQA.Selenium;
using OpenQA.Selenium.Edge;
using WebDriverManager;
using WebDriverManager.DriverConfigs.Impl;
using System;
using System.Threading;

namespace AuctionBidder.Core
{
    public abstract class BaseBidder(string name, Action<string, string> logCallback)
    {
        public IWebDriver? Driver { get; protected set; }
        protected string TaskName = name;
        protected Action<string, string> LogCallback = logCallback;
        protected WebPageParser? Parser { get; private set; }
        public bool IsRunning { get; set; } = false;
        public bool HasBid { get; protected set; } = false;

        protected void Log(string tag, string message)
        {
            LogCallback?.Invoke(tag, message);
        }

        /// <summary>
        /// 取得或建立網頁解析器
        /// </summary>
        protected WebPageParser GetParser()
        {
            if (Parser == null && Driver != null)
            {
                Parser = new WebPageParser(Driver, LogCallback);
            }
            return Parser ?? throw new InvalidOperationException("Parser not available - Driver must be initialized first");
        }

        /// <summary>
        /// 重新建立解析器（當導航到新頁面時使用）
        /// </summary>
        protected void RefreshParser()
        {
            if (Driver != null)
            {
                Parser = new WebPageParser(Driver, LogCallback);
            }
        }

        public bool SetupDriver()
        {
            Log("Init", "初始化瀏覽器...");
            try
            {
                Log("Init", "檢查並下載匹配的 WebDriver...");

                var driverManager = new DriverManager();
                driverManager.SetUpDriver(new EdgeConfig());

                Log("Init", "WebDriver 設置完成，啟動瀏覽器...");

                var options = new EdgeOptions();
                options.AddExcludedArgument("enable-automation");
                options.AddArgument("--disable-blink-features=AutomationControlled");
                options.AddArgument("--no-sandbox");
                options.AddArgument("--disable-infobars");
                options.AddArgument("--disable-dev-shm-usage");

                Driver = new EdgeDriver(options);

                // 初始化解析器
                RefreshParser();

                Log("OK", "瀏覽器啟動成功");
                return true;
            }
            catch (Exception ex)
            {
                Log("Error", $"啟動失敗: {ex.Message}");
                return TryManualDriverSetup();
            }
        }

        private bool TryManualDriverSetup()
        {
            try
            {
                string[] possiblePaths = [
                    @"C:\WebDriver",
                    @"C:\Tools\WebDriver",
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + @"\.cache\selenium\msedgedriver",
                    ""
                ];

                foreach (var path in possiblePaths)
                {
                    try
                    {
                        var options = new EdgeOptions();
                        options.AddExcludedArgument("enable-automation");
                        options.AddArgument("--disable-blink-features=AutomationControlled");
                        options.AddArgument("--no-sandbox");

                        EdgeDriverService? service = null;
                        if (!string.IsNullOrEmpty(path))
                        {
                            service = EdgeDriverService.CreateDefaultService(path);
                            Log("Init", $"嘗試路徑: {path}");
                        }
                        else
                        {
                            Log("Init", "嘗試系統 PATH");
                        }

                        Driver = service != null ? new EdgeDriver(service, options) : new EdgeDriver(options);

                        // 初始化解析器
                        RefreshParser();

                        Log("OK", "瀏覽器啟動成功 (手動路徑)");
                        return true;
                    }
                    catch (Exception ex)
                    {
                        Log("Warn", $"路徑 {path} 失敗: {ex.Message}");
                        continue;
                    }
                }

                Log("Error", "所有路徑都失敗了");
                return false;
            }
            catch (Exception ex)
            {
                Log("Error", $"手動設置失敗: {ex.Message}");
                return false;
            }
        }

        public void Close()
        {
            IsRunning = false;
            if (Driver != null)
            {
                try { Driver.Quit(); } catch { }
                Driver = null;
                Parser = null;
            }
        }

        /// <summary>
        /// 通用的競標監控邏輯
        /// </summary>
        protected void RunGenericMonitor(TaskConfig config, CancellationToken token)
        {
            Log("Monitor", "開始監控競標");
            IsRunning = true;

            try
            {
                var parser = GetParser();

                while (IsRunning && !token.IsCancellationRequested)
                {
                    var bidInfo = parser.GetBidInfo();

                    if (bidInfo?.RemainingTime.HasValue == true)
                    {
                        var totalSeconds = (int)bidInfo.RemainingTime.Value.TotalSeconds;
                        Log("Time", $"剩餘: {bidInfo.RemainingTime.Value:hh\\:mm\\:ss} ({totalSeconds}秒)");

                        // 顯示當前價格資訊
                        if (bidInfo.CurrentPrice.HasValue)
                        {
                            Log("Price", $"當前價格: {bidInfo.CurrentPrice:N0}");
                        }

                        // 修正：使用正確的屬性名稱
                        if (totalSeconds <= config.SprintStartSec && totalSeconds > 0)
                        {
                            Log("Sprint", $"衝刺階段！剩餘 {totalSeconds} 秒");

                            if (config.RealBid && !HasBid && parser.CanBid())
                            {
                                ExecuteBid(config);
                            }
                        }
                        else if (totalSeconds <= 0)
                        {
                            Log("End", "競標已結束");
                            break;
                        }
                    }
                    else
                    {
                        Log("Warn", "無法取得倒數計時資訊");
                    }

                    // 修正：使用正確的屬性名稱
                    Thread.Sleep(Math.Max(1000, config.SprintFreqMs));
                }
            }
            catch (Exception ex)
            {
                Log("Error", $"監控錯誤: {ex.Message}");
            }
            finally
            {
                IsRunning = false;
                Log("Monitor", "監控結束");
            }
        }

        /// <summary>
        /// 通用的手動競標邏輯
        /// </summary>
        protected void RunGenericManualBid()
        {
            if (Driver == null)
            {
                Log("Error", "瀏覽器未啟動");
                return;
            }

            try
            {
                var parser = GetParser();
                var bidInfo = parser.GetBidInfo();

                if (bidInfo != null)
                {
                    Log("Manual", "執行手動競標");

                    if (bidInfo.RemainingTime.HasValue)
                    {
                        Log("Info", $"剩餘時間: {bidInfo.RemainingTime.Value:hh\\:mm\\:ss}");
                    }

                    if (bidInfo.CurrentPrice.HasValue)
                    {
                        Log("Info", $"當前價格: {bidInfo.CurrentPrice:N0}");
                    }

                    if (parser.CanBid())
                    {
                        Log("Bid", "可以競標，執行競標操作...");
                        ExecuteBid(null);
                    }
                    else
                    {
                        Log("Warn", "目前無法競標");
                    }
                }
                else
                {
                    Log("Error", "無法取得競標資訊");
                }
            }
            catch (Exception ex)
            {
                Log("Error", $"手動競標失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 執行競標操作 - 由子類實作具體邏輯
        /// </summary>
        protected virtual void ExecuteBid(TaskConfig? config)
        {
            try
            {
                Log("Bid", "執行競標操作");

                // 子類應該覆寫此方法實作具體的競標邏輯
                // 這裡提供基本的標記
                HasBid = true;
                Log("OK", "競標標記已設置");
            }
            catch (Exception ex)
            {
                Log("Error", $"競標執行失敗: {ex.Message}");
            }
        }

        // 抽象方法 - 由子類實作
        public abstract bool NavigateToPage(string url);
        public abstract void AutoLogin(string loginUrl);
        public abstract void RunMonitor(TaskConfig config, CancellationToken token);
        public abstract void ManualBid();
    }
}
