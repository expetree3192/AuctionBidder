using AuctionBidder.Core;
using AuctionBidder.Models;
using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AuctionBidder.Controls
{
    public partial class BidTaskControl : UserControl
    {
        private readonly BaseBidder _bidder;
        private readonly TaskConfig _config = new();
        private CancellationTokenSource? _cts;
        private readonly string _siteType;

        private bool _isHandlingPostModeChange = false;
        public Action<string, string>? OnLog;
        public Action<BidTaskControl>? OnRemove;

        public BidTaskControl(string siteType, int index)
        {
            InitializeComponent();
            _siteType = siteType;
            grpBox.Text = $"#{index} {siteType}";

            txtUrl.Text = siteType == "Taipei"
                ? "https://shwoo.gov.taipei/shwoo/newproduct/newproduct00/product?AUID=887846"
                : "https://epai.taitung.gov.tw/bid.asp?op_=show&auid=291928&pcode=2e25342ec3de4f2819efc8bd2b4b5cc3";

            if (siteType == "Taipei")
            {
                pnlDelivery.Visible = false;
                chkPostMode.Visible = false;
                btnRefreshPrice.Visible = false;
                btnTestPrice.Visible = false; // 🎯 新增：台北也隱藏測試按鈕
            }
            else
            {
                pnlDelivery.Visible = true;
                rbShip.Checked = true;
                chkPostMode.Visible = true;
                btnRefreshPrice.Visible = true;
                btnTestPrice.Visible = true; // 🎯 新增：台東顯示測試按鈕
            }

            // 預設勾選
            chkRealBid.Checked = true;
            _config.RealBid = true;

            // 設定預設值
            txtMaxPrice.Text = "50";
            _config.DynamicMaxPrice = 50;

            // 🔧 重要：先建立 Bidder 實例，再綁定事件
            if (siteType == "Taipei") _bidder = new TaipeiBidder(grpBox.Text, LogHandler);
            else _bidder = new TaitungBidder(grpBox.Text, LogHandler);

            // 綁定事件 - 即時更新配置
            txtMaxPrice.TextChanged += (s, e) => {
                if (double.TryParse(txtMaxPrice.Text, out double val))
                {
                    _config.DynamicMaxPrice = val;
                    LogHandler("Config", $"價格上限更新為: {val}");
                }
                else
                {
                    _config.DynamicMaxPrice = null;
                    LogHandler("Config", "價格上限已移除");
                }
            };

            numSprint.ValueChanged += (s, e) => {
                _config.SprintStartSec = (int)numSprint.Value;
                LogHandler("Config", $"衝刺開始時間更新為: {_config.SprintStartSec} 秒");
            };

            numFreq.ValueChanged += (s, e) => {
                _config.SprintFreqMs = (int)numFreq.Value;
                LogHandler("Config", $"衝刺頻率更新為: {_config.SprintFreqMs} ms");
            };

            chkRealBid.CheckedChanged += (s, e) => {
                _config.RealBid = chkRealBid.Checked;
                LogHandler("Config", $"確認出價: {(_config.RealBid ? "啟用" : "停用")}");
            };

            txtMs.TextChanged += (s, e) => {
                if (int.TryParse(txtMs.Text, out int val))
                {
                    _config.TriggerMs = val;
                    LogHandler("Config", $"觸發時間偏移更新為: {val} ms");
                }
            };

            rbShip.CheckedChanged += (s, e) => UpdateDelivery();
            rbSelf.CheckedChanged += (s, e) => UpdateDelivery();

            chkPostMode.CheckedChanged -= chkPostMode_CheckedChanged;

            // 只在代碼中綁定一次，確保事件處理只執行一次
            chkPostMode.CheckedChanged += chkPostMode_CheckedChanged;
        }

        private void UpdateDelivery()
        {
            _config.DeliveryPreference = rbShip.Checked ? "託運" : "自取";
            LogHandler("Config", $"交貨方式: {_config.DeliveryPreference}");
        }

        private void LogHandler(string tag, string msg)
        {
            void action() => OnLog?.Invoke(grpBox.Text, $"[{tag}] {msg}");
            if (InvokeRequired) BeginInvoke(action);
            else action();
        }

        private async void btnOpen_Click(object sender, EventArgs e)
        {
            try
            {
                // 禁用按鈕防止重複點擊
                btnOpen.Enabled = false;
                btnOpen.Text = "初始化中...";

                // 🔧 修正：如果是台東任務，先初始化驗證碼識別器
                if (_bidder is TaitungBidder taitungBidder)
                {
                    LogHandler("Init", "正在初始化驗證碼識別器...");
                    await taitungBidder.InitializeCaptchaRecognizerAsync();
                }

                // 🔧 修正：將 Task.Run 改為 await Task.Run，正確處理異步操作
                await Task.Run(() => {
                    try
                    {
                        if (_bidder.SetupDriver())
                        {
                            string loginUrl = _siteType == "Taipei" ? Config.TAIPEI_LOGIN_URL : Config.TAITUNG_LOGIN_URL;
                            _bidder.AutoLogin(loginUrl);

                            // 在 UI 線程中更新按鈕狀態
                            if (InvokeRequired)
                            {
                                Invoke(new Action(() => {
                                    btnOpen.Text = "開啟瀏覽器";
                                    btnOpen.Enabled = true;
                                }));
                            }
                            else
                            {
                                btnOpen.Text = "開啟瀏覽器";
                                btnOpen.Enabled = true;
                            }
                        }
                        else
                        {
                            // 設置失敗時恢復按鈕
                            if (InvokeRequired)
                            {
                                Invoke(new Action(() => {
                                    btnOpen.Text = "開啟瀏覽器";
                                    btnOpen.Enabled = true;
                                }));
                            }
                            else
                            {
                                btnOpen.Text = "開啟瀏覽器";
                                btnOpen.Enabled = true;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogHandler("Error", $"瀏覽器設置過程發生錯誤: {ex.Message}");

                        // 發生錯誤時恢復按鈕
                        if (InvokeRequired)
                        {
                            Invoke(new Action(() => {
                                btnOpen.Text = "開啟瀏覽器";
                                btnOpen.Enabled = true;
                            }));
                        }
                        else
                        {
                            btnOpen.Text = "開啟瀏覽器";
                            btnOpen.Enabled = true;
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                LogHandler("Error", $"開啟瀏覽器失敗: {ex.Message}");

                // 恢復按鈕狀態
                btnOpen.Text = "開啟瀏覽器";
                btnOpen.Enabled = true;
            }
        }

        private void btnStart_Click(object sender, EventArgs e)
        {
            if (_bidder.Driver == null) { MessageBox.Show("請先開啟瀏覽器"); return; }
            _cts = new CancellationTokenSource();
            btnStart.Enabled = false;
            btnStop.Enabled = true;
            btnRefreshPrice.Enabled = true;
            btnTestPrice.Enabled = true;
            _config.Url = txtUrl.Text;

            Task.Run(() => {
                if (_bidder.NavigateToPage(_config.Url))
                    _bidder.RunMonitor(_config, _cts.Token);
                else if (InvokeRequired) Invoke(new Action(() => {
                    btnStart.Enabled = true;
                    btnStop.Enabled = false;
                    btnRefreshPrice.Enabled = false;
                    btnTestPrice.Enabled = false;
                }));
            });
        }

        private void btnStop_Click(object sender, EventArgs e)
        {
            _cts?.Cancel();
            btnStart.Enabled = true;
            btnStop.Enabled = false;
            btnRefreshPrice.Enabled = false;
            btnTestPrice.Enabled = false;
        }

        // 🔧 btnRefreshPrice 按鈕事件處理 - 刷新價格
        private async void btnRefreshPrice_Click(object sender, EventArgs e)
        {
            try
            {
                if (_bidder == null)
                {
                    MessageBox.Show("請先開啟瀏覽器", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (_bidder is not TaitungBidder taitungBidder)
                {
                    MessageBox.Show("此功能僅適用於台東拍賣", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // 暫時禁用按鈕防止重複點擊
                btnRefreshPrice.Enabled = false;
                btnRefreshPrice.Text = "刷新中...";

                // 🔧 修正：直接 await 異步操作，不使用 Task.Run
                try
                {
                    decimal? price = await taitungBidder.RefreshPriceViaHttpAsync();

                    if (price.HasValue)
                    {
                        LogHandler("Success", $"價格刷新成功: ${price}");
                    }
                    else
                    {
                        LogHandler("Warn", "刷新完成但未能取得價格資訊");
                    }
                }
                catch (Exception ex)
                {
                    LogHandler("Error", $"刷新價格時發生錯誤: {ex.Message}");
                }
                finally
                {
                    // 恢復按鈕狀態
                    btnRefreshPrice.Enabled = true;
                    btnRefreshPrice.Text = "刷新價格";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"刷新價格失敗: {ex.Message}", "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
                btnRefreshPrice.Enabled = true;
                btnRefreshPrice.Text = "刷新價格";
            }
        }

        // 🎯 新增：測試價格提取按鈕事件處理
        private async void btnTestPrice_Click(object sender, EventArgs e)
        {
            try
            {
                if (_bidder == null)
                {
                    MessageBox.Show("請先開啟瀏覽器", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (_bidder is not TaitungBidder taitungBidder)
                {
                    MessageBox.Show("此功能僅適用於台東拍賣", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // 暫時禁用按鈕防止重複點擊
                btnTestPrice.Enabled = false;
                btnTestPrice.Text = "測試中...";

                // 🔧 修正：使用 await Task.Run 正確處理異步操作
                await Task.Run(() =>
                {
                    try
                    {
                        LogHandler("Test", "開始測試價格提取邏輯...");
                        taitungBidder.TestPriceExtraction();
                        LogHandler("Test", "價格提取測試完成");
                    }
                    catch (Exception ex)
                    {
                        LogHandler("Error", $"測試失敗: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"測試失敗: {ex.Message}", "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                // 恢復按鈕狀態
                btnTestPrice.Enabled = true;
                btnTestPrice.Text = "測試價格";
            }
        }

        private async void btnManual_Click(object sender, EventArgs e)
        {
            if (_bidder == null)
            {
                LogHandler("Error", "請先開啟瀏覽器");
                return;
            }

            var config = GetCurrentConfig();

            if (_bidder is TaitungBidder taitungBidder)
            {
                // 🔧 修正：使用 await Task.Run 正確處理異步操作
                await Task.Run(() => {
                    taitungBidder.ManualBidWithPriceCheck(config.DynamicMaxPrice);
                });
            }
            else
            {
                // 其他 Bidder 使用原有方法
                await Task.Run(() => {
                    _bidder.ManualBid();
                });
            }
        }

        private TaskConfig GetCurrentConfig()
        {
            return new TaskConfig
            {
                Url = txtUrl.Text,
                LoginUrl = "https://epai.taitung.gov.tw/default.asp",
                TriggerMs = int.TryParse(txtMs.Text, out int ms) ? ms : 2000,
                SprintStartSec = (int)numSprint.Value,
                SprintFreqMs = (int)numFreq.Value,
                RealBid = chkRealBid.Checked,
                DynamicMaxPrice = double.TryParse(txtMaxPrice.Text, out double price) ? price : null,
                DeliveryPreference = rbShip.Checked ? "託運" : "自取",
                UsePostMethod = chkPostMode.Checked
            };
        }

        // 🔧 POST 模式變更事件處理方法
        private void chkPostMode_CheckedChanged(object? sender, EventArgs e)
        {
            if (_isHandlingPostModeChange)
            {
                return;
            }

            try
            {
                _isHandlingPostModeChange = true;

                if (_config.UsePostMethod == chkPostMode.Checked)
                {
                    return;
                }

                _config.UsePostMethod = chkPostMode.Checked;

                if (_bidder is TaitungBidder taitungBidder)
                {
                    taitungBidder.EnablePostMode(_config.UsePostMethod);
                }
            }
            finally
            {
                _isHandlingPostModeChange = false;
            }
        }

        private void btnRemove_Click(object sender, EventArgs e) { CloseTask(); OnRemove?.Invoke(this); }

        public void CloseTask() { _cts?.Cancel(); _bidder.Close(); }
        public void BatchLogin() => btnOpen.PerformClick();
        public void BatchStart() { if (_bidder.Driver != null && !_bidder.IsRunning) btnStart.PerformClick(); }
        public void BatchStop() => btnStop.PerformClick();
        public void SetRealBid(bool isChecked)
        {
            if (InvokeRequired) Invoke(new Action(() => chkRealBid.Checked = isChecked));
            else chkRealBid.Checked = isChecked;
        }
    }
}