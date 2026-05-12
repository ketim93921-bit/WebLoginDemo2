using System;

namespace WebLoginDemo2.Models
{
    public class SensorData
    {
        public DateTime Time { get; set; }
        public double Temp { get; set; }
        public double Humidity { get; set; }
        public double Light { get; set; }
        public double Soil { get; set; }
        public double CO2 { get; set; }
        public double PH { get; set; }

        // ✅ 新增：風扇狀態
        public bool Fan { get; set; }
    }
}