namespace AuctionBidder.Forms
{
    partial class BatchAddForm
    {
        private System.ComponentModel.IContainer components = null;
        public System.Windows.Forms.TextBox txtUrls;
        private System.Windows.Forms.Button btnOk;
        private System.Windows.Forms.Label lblInfo;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null)) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.txtUrls = new System.Windows.Forms.TextBox();
            this.btnOk = new System.Windows.Forms.Button();
            this.lblInfo = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // lblInfo
            // 
            this.lblInfo.AutoSize = true;
            this.lblInfo.Location = new System.Drawing.Point(12, 9);
            this.lblInfo.Name = "lblInfo";
            this.lblInfo.Size = new System.Drawing.Size(200, 15);
            this.lblInfo.TabIndex = 0;
            this.lblInfo.Text = "請貼上網址列表 (一行一個網址)：";
            // 
            // txtUrls
            // 
            this.txtUrls.Location = new System.Drawing.Point(12, 30);
            this.txtUrls.Multiline = true;
            this.txtUrls.Name = "txtUrls";
            this.txtUrls.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtUrls.Size = new System.Drawing.Size(560, 300);
            this.txtUrls.TabIndex = 1;
            // 
            // btnOk
            // 
            this.btnOk.Location = new System.Drawing.Point(250, 340);
            this.btnOk.Name = "btnOk";
            this.btnOk.Size = new System.Drawing.Size(100, 30);
            this.btnOk.TabIndex = 2;
            this.btnOk.Text = "確認匯入";
            this.btnOk.UseVisualStyleBackColor = true;
            this.btnOk.Click += new System.EventHandler(this.BtnOk_Click);
            // 
            // BatchAddForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(584, 381);
            this.Controls.Add(this.btnOk);
            this.Controls.Add(this.txtUrls);
            this.Controls.Add(this.lblInfo);
            this.Name = "BatchAddForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "批次匯入";
            this.ResumeLayout(false);
            this.PerformLayout();
        }
    }
}