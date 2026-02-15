namespace AuctionBidder.Controls
{
    partial class BidTaskControl
    {
        private System.ComponentModel.IContainer components = null;
        public System.Windows.Forms.GroupBox grpBox;
        public System.Windows.Forms.TextBox txtUrl;
        public System.Windows.Forms.TextBox txtMs;
        public System.Windows.Forms.NumericUpDown numSprint;
        public System.Windows.Forms.NumericUpDown numFreq;
        public System.Windows.Forms.TextBox txtMaxPrice;
        public System.Windows.Forms.CheckBox chkRealBid;
        public System.Windows.Forms.CheckBox chkPostMode;
        public System.Windows.Forms.Button btnOpen;
        public System.Windows.Forms.Button btnStart;
        public System.Windows.Forms.Button btnStop;
        public System.Windows.Forms.Button btnManual;
        public System.Windows.Forms.Button btnRemove;
        public System.Windows.Forms.Button btnRefreshPrice;
        public System.Windows.Forms.Button btnTestPrice; // 新增：測試價格按鈕
        public System.Windows.Forms.Panel pnlDelivery;
        public System.Windows.Forms.RadioButton rbShip;
        public System.Windows.Forms.RadioButton rbSelf;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.grpBox = new System.Windows.Forms.GroupBox();
            this.txtUrl = new System.Windows.Forms.TextBox();
            this.txtMs = new System.Windows.Forms.TextBox();
            this.numSprint = new System.Windows.Forms.NumericUpDown();
            this.numFreq = new System.Windows.Forms.NumericUpDown();
            this.txtMaxPrice = new System.Windows.Forms.TextBox();
            this.chkRealBid = new System.Windows.Forms.CheckBox();
            this.chkPostMode = new System.Windows.Forms.CheckBox();
            this.btnOpen = new System.Windows.Forms.Button();
            this.btnStart = new System.Windows.Forms.Button();
            this.btnStop = new System.Windows.Forms.Button();
            this.btnManual = new System.Windows.Forms.Button();
            this.btnRemove = new System.Windows.Forms.Button();
            this.btnRefreshPrice = new System.Windows.Forms.Button();
            this.btnTestPrice = new System.Windows.Forms.Button(); // 新增：測試按鈕
            this.pnlDelivery = new System.Windows.Forms.Panel();
            this.rbShip = new System.Windows.Forms.RadioButton();
            this.rbSelf = new System.Windows.Forms.RadioButton();
            ((System.ComponentModel.ISupportInitialize)(this.numSprint)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numFreq)).BeginInit();
            this.grpBox.SuspendLayout();
            this.pnlDelivery.SuspendLayout();
            this.SuspendLayout();

            // grpBox
            this.grpBox.Controls.Add(this.txtUrl);
            this.grpBox.Controls.Add(this.txtMs);
            this.grpBox.Controls.Add(this.numSprint);
            this.grpBox.Controls.Add(this.numFreq);
            this.grpBox.Controls.Add(this.txtMaxPrice);
            this.grpBox.Controls.Add(this.chkRealBid);
            this.grpBox.Controls.Add(this.chkPostMode);
            this.grpBox.Controls.Add(this.btnOpen);
            this.grpBox.Controls.Add(this.btnStart);
            this.grpBox.Controls.Add(this.btnStop);
            this.grpBox.Controls.Add(this.btnManual);
            this.grpBox.Controls.Add(this.btnRemove);
            this.grpBox.Controls.Add(this.btnRefreshPrice);
            this.grpBox.Controls.Add(this.btnTestPrice); // 新增：測試按鈕
            this.grpBox.Controls.Add(this.pnlDelivery);
            this.grpBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.grpBox.Text = "Task";
            this.grpBox.Padding = new System.Windows.Forms.Padding(5);

            // txtUrl (第1行：競標網址輸入框)
            this.txtUrl.Location = new System.Drawing.Point(10, 18);
            this.txtUrl.Width = 480;
            this.txtUrl.Height = 22;

            // txtMs (第2行左邊：觸發時間偏移 ms)
            this.txtMs.Location = new System.Drawing.Point(10, 48);
            this.txtMs.Width = 60;
            this.txtMs.Height = 22;
            this.txtMs.Text = "2000";

            // numSprint (衝刺開始秒數)
            this.numSprint.Location = new System.Drawing.Point(80, 48);
            this.numSprint.Width = 60;
            this.numSprint.Height = 22;
            this.numSprint.Value = 60;

            // numFreq (衝刺頻率)
            this.numFreq.Location = new System.Drawing.Point(150, 48);
            this.numFreq.Width = 60;
            this.numFreq.Height = 22;
            this.numFreq.Maximum = 5000;
            this.numFreq.Value = 1005;

            // txtMaxPrice (價格上限)
            this.txtMaxPrice.Location = new System.Drawing.Point(220, 48);
            this.txtMaxPrice.Width = 70;
            this.txtMaxPrice.Height = 22;
            this.txtMaxPrice.PlaceholderText = "價格上限";

            // chkRealBid (確認出價核取方塊)
            this.chkRealBid.Location = new System.Drawing.Point(300, 50);
            this.chkRealBid.Width = 80;
            this.chkRealBid.Height = 20;
            this.chkRealBid.Text = "確認出價";

            // chkPostMode (POST 模式核取方塊)
            this.chkPostMode.Location = new System.Drawing.Point(390, 50);
            this.chkPostMode.Width = 90;
            this.chkPostMode.Height = 20;
            this.chkPostMode.Text = "POST模式";

            // pnlDelivery (交貨方式選擇面板)
            this.pnlDelivery.Controls.Add(this.rbShip);
            this.pnlDelivery.Controls.Add(this.rbSelf);
            this.pnlDelivery.Location = new System.Drawing.Point(10, 76);
            this.pnlDelivery.Size = new System.Drawing.Size(150, 30);
            this.pnlDelivery.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;

            // rbShip (託運)
            this.rbShip.Text = "託運";
            this.rbShip.Location = new System.Drawing.Point(5, 5);
            this.rbShip.Width = 60;
            this.rbShip.Height = 18;

            // rbSelf (自取)
            this.rbSelf.Text = "自取";
            this.rbSelf.Location = new System.Drawing.Point(70, 5);
            this.rbSelf.Width = 60;
            this.rbSelf.Height = 18;

            // 按鈕配置
            int btnY = 112;
            int btnWidth = 70;
            int manualBtnWidth = 120;

            this.btnOpen.Text = "開啟";
            this.btnOpen.Location = new System.Drawing.Point(10, btnY);
            this.btnOpen.Size = new System.Drawing.Size(btnWidth, 28);
            this.btnOpen.Click += new System.EventHandler(this.btnOpen_Click);

            this.btnStart.Text = "開始";
            this.btnStart.Location = new System.Drawing.Point(90, btnY);
            this.btnStart.Size = new System.Drawing.Size(btnWidth, 28);
            this.btnStart.Click += new System.EventHandler(this.btnStart_Click);

            // 新增 btnRefreshPrice 按鈕 - 放在交貨方式選擇面板右邊
            this.btnRefreshPrice.Text = "刷新價格";
            this.btnRefreshPrice.Location = new System.Drawing.Point(170, 76);
            this.btnRefreshPrice.Size = new System.Drawing.Size(btnWidth, 28);
            this.btnRefreshPrice.Enabled = false;
            this.btnRefreshPrice.Click += new System.EventHandler(this.btnRefreshPrice_Click);

            // 新增測試價格按鈕 btnTestPrice 按鈕 - 放在刷新價格按鈕右邊
            this.btnTestPrice.Text = "測試價格";
            this.btnTestPrice.Location = new System.Drawing.Point(250, 76);
            this.btnTestPrice.Size = new System.Drawing.Size(btnWidth, 28);
            this.btnTestPrice.Enabled = false;
            this.btnTestPrice.Click += new System.EventHandler(this.btnTestPrice_Click);

            this.btnStop.Text = "停止";
            this.btnStop.Location = new System.Drawing.Point(170, btnY);
            this.btnStop.Size = new System.Drawing.Size(btnWidth, 28);
            this.btnStop.Enabled = false;
            this.btnStop.Click += new System.EventHandler(this.btnStop_Click);

            this.btnManual.Text = "手動立即競標";
            this.btnManual.Location = new System.Drawing.Point(250, btnY);
            this.btnManual.Size = new System.Drawing.Size(manualBtnWidth, 28);
            this.btnManual.Click += new System.EventHandler(this.btnManual_Click);

            this.btnRemove.Text = "移除";
            this.btnRemove.Location = new System.Drawing.Point(380, btnY);
            this.btnRemove.Size = new System.Drawing.Size(btnWidth, 28);
            this.btnRemove.Click += new System.EventHandler(this.btnRemove_Click);

            // BidTaskControl
            this.Controls.Add(this.grpBox);
            this.Size = new System.Drawing.Size(500, 155);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;

            ((System.ComponentModel.ISupportInitialize)(this.numSprint)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numFreq)).EndInit();
            this.grpBox.ResumeLayout(false);
            this.grpBox.PerformLayout();
            this.pnlDelivery.ResumeLayout(false);
            this.ResumeLayout(false);
        }
    }
}