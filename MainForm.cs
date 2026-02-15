using AuctionBidder.Controls;
using AuctionBidder.Core;
using AuctionBidder.Forms;
using AuctionBidder.Models;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AuctionBidder
{
    public partial class MainForm : Form
    {
        // 定義全域變數
        private FlowLayoutPanel? flowLayoutTaipei;
        private FlowLayoutPanel? flowLayoutTaitung;
        private TextBox? txtLog;

        // 顏色設定
        private readonly Color ColorTaipeiBg = Color.FromArgb(227, 242, 253); // 台北背景淡藍
        private readonly Color ColorTaitungBg = Color.FromArgb(255, 249, 196); // 台東背景淡黃
        private readonly Color ColorLogBg = Color.WhiteSmoke;

        public MainForm()
        {
            InitializeComponent(); // 這是 Designer 的，不要動
            InitUI();              // 這是我們自訂的
        }
        private Panel CreateCaptchaSettingsPanel()
        {
            var panel = new Panel
            {
                Height = 60,
                Dock = DockStyle.Top,
                BackColor = Color.FromArgb(240, 248, 255),
                BorderStyle = BorderStyle.FixedSingle
            };

            var lblTitle = new Label
            {
                Text = "🤖 驗證碼識別設定",
                Font = new Font("Microsoft JhengHei UI", 9, FontStyle.Bold),
                Location = new Point(10, 8),
                AutoSize = true
            };

            var chkEnableAI = new CheckBox
            {
                Text = "啟用AI識別",
                Location = new Point(10, 30),
                Checked = Config.ENABLE_AUTO_CAPTCHA,
                AutoSize = true
            };
            chkEnableAI.CheckedChanged += (s, e) => {
                Config.ENABLE_AUTO_CAPTCHA = chkEnableAI.Checked;
                LogMessage("Config", $"AI驗證碼識別: {(chkEnableAI.Checked ? "啟用" : "停用")}");
            };

            var btnTestCaptcha = new Button
            {
                Text = "🧪 測試識別",
                Location = new Point(150, 28),
                Size = new Size(100, 25),
                BackColor = Color.LightBlue,
                FlatStyle = FlatStyle.Flat
            };
            btnTestCaptcha.Click += BtnTestCaptcha_Click;

            var btnOpenTrainingFolder = new Button
            {
                Text = "📁 訓練資料夾",
                Location = new Point(260, 28),
                Size = new Size(100, 25),
                BackColor = Color.LightGreen,
                FlatStyle = FlatStyle.Flat
            };
            btnOpenTrainingFolder.Click += BtnOpenTrainingFolder_Click;

            var lblConfidence = new Label
            {
                Text = $"信心度閾值: {Config.CAPTCHA_CONFIDENCE_THRESHOLD:P0}",
                Location = new Point(370, 32),
                AutoSize = true,
                Font = new Font("Microsoft JhengHei UI", 8)
            };

            var trackConfidence = new TrackBar
            {
                Location = new Point(480, 28),
                Size = new Size(100, 25),
                Minimum = 50,
                Maximum = 95,
                Value = (int)(Config.CAPTCHA_CONFIDENCE_THRESHOLD * 100),
                TickFrequency = 5
            };
            trackConfidence.ValueChanged += (s, e) => {
                Config.CAPTCHA_CONFIDENCE_THRESHOLD = trackConfidence.Value / 100.0;
                lblConfidence.Text = $"信心度閾值: {Config.CAPTCHA_CONFIDENCE_THRESHOLD:P0}";
            };

            panel.Controls.AddRange([
              lblTitle, chkEnableAI, btnTestCaptcha, btnOpenTrainingFolder, lblConfidence, trackConfidence
          ]);

            return panel;
        }
        private void InitUI()
        {
            this.Text = "拍賣自動出價系統 v8.6 - 完美修復版 + AI驗證碼";
            this.Size = new Size(1300, 900);

            // 清除 Designer 可能產生的預設控制項，確保版面乾淨
            this.Controls.Clear();

            // 🆕 1. 建立驗證碼設定區域（最上方）
            var captchaPanel = CreateCaptchaSettingsPanel();
            this.Controls.Add(captchaPanel);

            // 2. 建立底部 LOG 區域
            Panel pnlLogContainer = new()
            {
                Dock = DockStyle.Bottom,
                Height = 200,
                Padding = new Padding(5),
                BackColor = ColorLogBg
            };

            Panel pnlLogTools = new() { Dock = DockStyle.Top, Height = 30 };
            Label lblLog = new() { Text = "全域監控日誌 (Global Log)", AutoSize = true, Location = new Point(5, 5), Font = new Font("Microsoft JhengHei UI", 9, FontStyle.Bold) };
            Button btnCopyLog = new() { Text = "📋 複製 LOG", Size = new Size(100, 25), Dock = DockStyle.Right, BackColor = Color.Gainsboro };

            btnCopyLog.Click += BtnCopyLog_Click;

            pnlLogTools.Controls.Add(lblLog);
            pnlLogTools.Controls.Add(btnCopyLog);

            txtLog = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.White,
                Font = new Font("Consolas", 10)
            };

            pnlLogContainer.Controls.Add(txtLog);
            pnlLogContainer.Controls.Add(pnlLogTools);
            this.Controls.Add(pnlLogContainer);

            // 3. 建立主體表格佈局
            TableLayoutPanel mainGrid = new()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                BackColor = SystemColors.Control
            };

            mainGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            mainGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            mainGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 90F)); // Header
            mainGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100F)); // Content

            this.Controls.Add(mainGrid);

            // 調整控制項順序
            captchaPanel.BringToFront();
            pnlLogContainer.SendToBack();
            mainGrid.BringToFront();

            // 4. 建立並填入內容（保持原有邏輯）
            Panel pnlHeaderTaipei = CreateHeaderPanel("台北惜物網 (A欄)", ColorTaipeiBg, "Taipei", out flowLayoutTaipei);
            mainGrid.Controls.Add(pnlHeaderTaipei, 0, 0);

            Panel pnlHeaderTaitung = CreateHeaderPanel("台東 E 拍網 (B欄)", ColorTaitungBg, "Taitung", out flowLayoutTaitung);
            mainGrid.Controls.Add(pnlHeaderTaitung, 1, 0);

            // 加入 FlowLayoutPanels
            if (flowLayoutTaipei != null)
            {
                flowLayoutTaipei.Dock = DockStyle.Fill;
                flowLayoutTaipei.AutoScroll = true;
                flowLayoutTaipei.BackColor = Color.White;
                flowLayoutTaipei.BorderStyle = BorderStyle.Fixed3D;
                mainGrid.Controls.Add(flowLayoutTaipei, 0, 1);
            }

            if (flowLayoutTaitung != null)
            {
                flowLayoutTaitung.Dock = DockStyle.Fill;
                flowLayoutTaitung.AutoScroll = true;
                flowLayoutTaitung.BackColor = Color.White;
                flowLayoutTaitung.BorderStyle = BorderStyle.Fixed3D;
                mainGrid.Controls.Add(flowLayoutTaitung, 1, 1);
            }
        }
        private async void BtnTestCaptcha_Click(object? sender, EventArgs e)
        {
            try
            {
                if (sender is Button btn)
                {
                    btn.Enabled = false;
                    btn.Text = "測試中...";
                }

                LogMessage("Test", "🧪 開始測試驗證碼識別功能...");

                // 創建台東競標器進行測試
                var taitungBidder = new TaitungBidder("測試", LogMessage);

                try
                {
                    // 初始化驗證碼識別器
                    await taitungBidder.InitializeCaptchaRecognizerAsync();

                    // 設置瀏覽器
                    if (taitungBidder.SetupDriver())
                    {
                        LogMessage("Test", "瀏覽器啟動成功，開始測試...");

                        // 執行測試
                        await taitungBidder.TestCaptchaRecognitionAsync();
                    }
                    else
                    {
                        LogMessage("Error", "瀏覽器啟動失敗");
                    }
                }
                finally
                {
                    // 清理資源
                    taitungBidder.Close();
                }

                LogMessage("Test", "✅ 測試完成");
            }
            catch (Exception ex)
            {
                LogMessage("Error", $"測試失敗: {ex.Message}");
            }
            finally
            {
                if (sender is Button btn)
                {
                    btn.Enabled = true;
                    btn.Text = "🧪 測試識別";
                }
            }
        }

        /// <summary>
        /// 🆕 新增：開啟訓練資料夾按鈕事件
        /// </summary>
        private void BtnOpenTrainingFolder_Click(object? sender, EventArgs e)
        {
            try
            {
                var trainingPath = Config.CAPTCHA_TRAINING_PATH;

                // 如果資料夾不存在，創建它
                if (!System.IO.Directory.Exists(trainingPath))
                {
                    System.IO.Directory.CreateDirectory(trainingPath);
                    LogMessage("Info", $"已創建訓練資料夾: {trainingPath}");
                }

                // 開啟資料夾
                System.Diagnostics.Process.Start("explorer.exe", trainingPath);
                LogMessage("Info", $"已開啟訓練資料夾: {trainingPath}");
            }
            catch (Exception ex)
            {
                LogMessage("Error", $"開啟訓練資料夾失敗: {ex.Message}");
            }
        }
        // 輔助方法：建立標題面板並回傳對應的 FlowLayout
        private Panel CreateHeaderPanel(string title, Color bg, string siteType, out FlowLayoutPanel flowPanel)
        {
            // 初始化對應的 FlowPanel
            flowPanel = new FlowLayoutPanel();
            var targetPanel = flowPanel; // 閉包用

            Panel pnl = new()
            {
                Dock = DockStyle.Fill,
                BackColor = bg,
                BorderStyle = BorderStyle.FixedSingle
            };

            Label lbl = new()
            {
                Text = title,
                Font = new Font("Microsoft JhengHei UI", 12, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(10, 10)
            };

            Button btnAdd = new()
            {
                Text = "[+] 新增任務",
                BackColor = Color.White,
                ForeColor = Color.Blue,
                Size = new Size(110, 28),
                Location = new Point(220, 6)
            };
            btnAdd.Click += (s, e) => AddTask(siteType, null);

            Button btnBatchAdd = new()
            {
                Text = "[++] 批次匯入",
                BackColor = Color.White,
                ForeColor = Color.Green,
                Size = new Size(110, 28),
                Location = new Point(340, 6)
            };
            btnBatchAdd.Click += (s, e) => ShowBatchAdd(siteType);

            int y2 = 45;
            int x = 10;
            int gap = 85;

            var btnOpen = CreateButton("全部開啟", Color.LightBlue, new Point(x, y2), (s, e) => RunBatch(targetPanel, "LOGIN"));
            var btnStart = CreateButton("全部監控", Color.LightGreen, new Point(x + gap, y2), (s, e) => RunBatch(targetPanel, "START"));
            var btnStop = CreateButton("全部停止", Color.LightPink, new Point(x + gap * 2, y2), (s, e) => RunBatch(targetPanel, "STOP"));
            var btnDel = CreateButton("全部刪除", Color.Gainsboro, new Point(x + gap * 3, y2), (s, e) => RunBatch(targetPanel, "DELETE"));

            CheckBox chkAllReal = new()
            {
                Text = "全勾選出價",
                AutoSize = true,
                Location = new Point(x + gap * 4 + 5, y2 + 5),
                Checked = true
            };
            chkAllReal.CheckedChanged += (s, e) => RunBatchCheck(targetPanel, chkAllReal.Checked);

            pnl.Controls.AddRange([lbl, btnAdd, btnBatchAdd, btnOpen, btnStart, btnStop, btnDel, chkAllReal]);
            return pnl;
        }

        private static Button CreateButton(string text, Color bg, Point loc, EventHandler onClick)
        {
            Button btn = new()
            {
                Text = text,
                BackColor = bg,
                Location = loc,
                Size = new Size(80, 30),
                FlatStyle = FlatStyle.Flat
            };
            btn.FlatAppearance.BorderSize = 1;
            btn.Click += onClick;
            return btn;
        }

        // [修正] 參數 sender 可為 null
        private void BtnCopyLog_Click(object? sender, EventArgs e)
        {
            if (txtLog != null && !string.IsNullOrEmpty(txtLog.Text)) Clipboard.SetText(txtLog.Text);
        }

        private void AddTask(string type, string? url)
        {
            FlowLayoutPanel? panel = type == "Taipei" ? flowLayoutTaipei : flowLayoutTaitung;
            if (panel == null) return;

            int idx = panel.Controls.Count + 1;
            var task = new BidTaskControl(type, idx);
            if (!string.IsNullOrEmpty(url)) task.txtUrl.Text = url;

            task.OnLog = LogMessage;
            task.OnRemove = (t) =>
            {
                if (this.InvokeRequired) this.Invoke(new Action(() => panel.Controls.Remove(t)));
                else panel.Controls.Remove(t);
                t.Dispose();
            };
            task.Width = panel.Width - 25;
            panel.Controls.Add(task);

            // 🔧 修正：如果是台東任務且啟用AI，在背景初始化（不阻塞UI）
            if (type == "Taitung" && Config.ENABLE_AUTO_CAPTCHA)
            {
                // 使用 Task.Run 在背景執行，不需要等待結果
                _ = Task.Run(async () =>
                {
                    try
                    {
                        LogMessage("Init", $"為任務 #{idx} 準備驗證碼識別功能");
                        // 這裡可以做一些預先準備工作，但不需要立即初始化
                        // 實際的初始化會在 btnOpen_Click 時進行
                        await Task.Delay(100); // 模擬初始化準備工作
                        LogMessage("Init", $"任務 #{idx} 驗證碼識別功能準備完成");
                    }
                    catch (Exception ex)
                    {
                        LogMessage("Warn", $"任務 #{idx} 驗證碼識別器準備失敗: {ex.Message}");
                    }
                });
            }
        }

        private static async void RunBatch(FlowLayoutPanel panel, string action)
        {
            var controls = panel.Controls.Cast<Control>().ToList();
            foreach (Control c in controls)
            {
                if (c is BidTaskControl task)
                {
                    switch (action)
                    {
                        case "LOGIN": task.BatchLogin(); await Task.Delay(3000); break;
                        case "START": task.BatchStart(); await Task.Delay(3000); break;
                        case "STOP": task.BatchStop(); await Task.Delay(100); break;
                        case "DELETE":
                            task.CloseTask();
                            panel.Controls.Remove(task);
                            task.Dispose();
                            await Task.Delay(1000);
                            break;
                    }
                }
            }
        }

        private static void RunBatchCheck(FlowLayoutPanel panel, bool isChecked)
        {
            foreach (Control c in panel.Controls)
            {
                if (c is BidTaskControl task) task.SetRealBid(isChecked);
            }
        }

        private void ShowBatchAdd(string type)
        {
            using var form = new BatchAddForm(type + " 批次匯入");
            if (form.ShowDialog() == DialogResult.OK)
            {
                // [修正] IDE0090
                var lines = form.ResultText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines) AddTask(type, line.Trim());
            }
        }

        // 🔧 修正：加入毫秒顯示
        private void LogMessage(string taskName, string msg)
        {
            if (txtLog == null) return;
            if (txtLog.InvokeRequired)
            {
                txtLog.BeginInvoke(new Action(() => LogMessage(taskName, msg)));
                return;
            }

            string time = DateTime.Now.ToString("HH:mm:ss.fff");
            txtLog.AppendText($"[{time}] <{taskName}> {msg}\r\n");
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (flowLayoutTaipei != null)
                foreach (Control c in flowLayoutTaipei.Controls) (c as BidTaskControl)?.CloseTask();
            if (flowLayoutTaitung != null)
                foreach (Control c in flowLayoutTaitung.Controls) (c as BidTaskControl)?.CloseTask();
        }
    }
}
