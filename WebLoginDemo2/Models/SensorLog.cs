using System;
using System.ComponentModel.DataAnnotations;

namespace WebLoginDemo2.Models
{
    public class SensorLog
    {
        [Key]
        public int Id { get; set; }
        public double Temp { get; set; }
        public double Humidity { get; set; }
        public double Light { get; set; }
        public double Soil { get; set; }
        public double CO2 { get; set; }
        public double PH { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}