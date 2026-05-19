using System;
using System.ComponentModel.DataAnnotations;

namespace WebLoginDemo2.Models
{
    public class SensorLog
    {
        [Key]
        public int Id { get; set; }

        // 感測資料
        public double Temp { get; set; }
        public double Humidity { get; set; }
        public double Soil { get; set; }

        // 土壤狀態：DRY / MOIST / WET
        [MaxLength(20)]
        public string SoilState { get; set; } = string.Empty;

        // D5 土壤自動繼電器狀態
        public bool Relay5 { get; set; }

        // D6 人工 / 定時繼電器狀態
        public bool Relay6 { get; set; }

        // 步進馬達狀態
        public bool Stepper { get; set; }

        // 紀錄時間
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}