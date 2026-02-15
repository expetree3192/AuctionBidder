using System;

namespace AuctionBidder.Models
{
    // 參數設定檔：UI 與 邏輯層 共用此物件
    public class TaskConfig
    {
        public string Url { get; set; } = "";
        public string LoginUrl { get; set; } = "";

        // 動態可調參數
        public int TriggerMs { get; set; } = 2000;
        public int SprintStartSec { get; set; } = 60;
        public int SprintFreqMs { get; set; } = 1005;
        public bool RealBid { get; set; } = true;

        // null 代表無上限
        public double? DynamicMaxPrice { get; set; } = null;

        // 台東專用：交貨方式 (託運/自取)
        public string DeliveryPreference { get; set; } = "託運";
        // 🔧 新增：POST 模式設定
        public bool UsePostMethod { get; set; } = false;
    }
}
