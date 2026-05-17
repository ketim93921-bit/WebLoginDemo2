using System;
using System.ComponentModel.DataAnnotations;

namespace WebLoginDemo2.Models
{
    public class SensorHourlyStats
    {
        [Key]
        public int Id { get; set; }

        // 統計時間，通常代表某一個小時
        public DateTime Time { get; set; }

        // 每小時平均感測值
        public double AvgTemp { get; set; }
        public double AvgHumidity { get; set; }
        public double AvgSoil { get; set; }
    }
}