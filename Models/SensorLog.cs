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

        // 使用者可設定門檻
        public double TempLimit { get; set; }
        public int SoilLimit { get; set; }

        // 自動控制模式
        public bool TempAuto { get; set; }
        public bool SoilAuto { get; set; }

        // Relay 狀態
        public bool Relay1 { get; set; }
        public bool Relay2 { get; set; }
        public bool Relay3 { get; set; }
        public bool Relay4 { get; set; }
        public bool Relay5 { get; set; }
        public bool Relay6 { get; set; }

        // 步進馬達狀態
        public bool Stepper { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}