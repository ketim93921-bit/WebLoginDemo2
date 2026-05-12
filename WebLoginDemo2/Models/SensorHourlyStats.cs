using System;
using System.ComponentModel.DataAnnotations;

namespace WebLoginDemo2.Models
{
    public class SensorHourlyStats
    {
        [Key]
        public int Id { get; set; }
        public DateTime Time { get; set; }
        public double AvgTemp { get; set; }
        public double AvgHumidity { get; set; }
        public double AvgLight { get; set; }
        public double AvgSoil { get; set; }
        public double AvgCO2 { get; set; }
        public double AvgPH { get; set; }
    }
}