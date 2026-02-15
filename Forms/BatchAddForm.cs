using System;
using System.Windows.Forms;

namespace AuctionBidder.Forms
{
    public partial class BatchAddForm : Form
    {
        public string ResultText { get; private set; } = string.Empty;

        public BatchAddForm(string title)
        {
            InitializeComponent();
            this.Text = title;
        }

        // 注意：這裡也使用小寫開頭以配合 Designer
        private void BtnOk_Click(object sender, EventArgs e)
        {
            ResultText = txtUrls.Text;
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}