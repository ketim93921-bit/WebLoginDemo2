using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;
using WebLoginDemo2.Data;
using WebLoginDemo2.Services;

namespace WebLoginDemo2.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class DataController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly MqttService _mqttService;

        public DataController(AppDbContext db, MqttService mqttService)
        {
            _db = db;
            _mqttService = mqttService;
        }

        // ==========================================
        // 1. 取得最新一筆數據
        // API: GET /Data/Latest
        // ==========================================
        [HttpGet("Latest")]
        public IActionResult Latest()
        {
            var latest = _mqttService.GetLatestSensorData();
            return Ok(latest);
        }

        // ==========================================
        // 2. 取得歷史數據
        // API: GET /Data/History?minutes=10
        // ==========================================
        [HttpGet("History")]
        public async Task<IActionResult> History(int minutes = 60)
        {
            minutes = Math.Clamp(minutes, 1, 1440);

            var startTime = DateTime.Now.AddMinutes(-minutes);

            var rawData = await _db.SensorLogs
                .AsNoTracking()
                .Where(s => s.CreatedAt >= startTime)
                .Where(s =>
                    s.Temp > 0 &&
                    s.Temp < 60 &&
                    s.Humidity > 0 &&
                    s.Humidity <= 100 &&
                    s.Soil >= 0 &&
                    s.Soil <= 1024)
                .OrderBy(s => s.CreatedAt)
                .ToListAsync();

            var chartData = rawData
                .GroupBy(x => new
                {
                    x.CreatedAt.Year,
                    x.CreatedAt.Month,
                    x.CreatedAt.Day,
                    x.CreatedAt.Hour,
                    x.CreatedAt.Minute
                })
                .Select(g =>
                {
                    var last = g.OrderBy(x => x.CreatedAt).Last();

                    return new
                    {
                        time = new DateTime(
                            g.Key.Year,
                            g.Key.Month,
                            g.Key.Day,
                            g.Key.Hour,
                            g.Key.Minute,
                            0
                        ),

                        temp = Math.Round(g.Average(x => x.Temp), 1),
                        humidity = Math.Round(g.Average(x => x.Humidity), 1),
                        soil = Math.Round(g.Average(x => x.Soil), 0),

                        soilState = last.SoilState,
                        relay5 = last.Relay5,
                        relay6 = last.Relay6,
                        stepper = last.Stepper
                    };
                })
                .OrderBy(x => x.time)
                .ToList();

            return Ok(chartData);
        }

        // ==========================================
        // 3. 匯出 CSV 檔案
        // API: GET /Data/Export?minutes=60
        // ==========================================
        [HttpGet("Export")]
        public async Task<IActionResult> Export(int minutes = 60)
        {
            minutes = Math.Clamp(minutes, 1, 1440);

            var startTime = DateTime.Now.AddMinutes(-minutes);

            var data = await _db.SensorLogs
                .AsNoTracking()
                .Where(s => s.CreatedAt >= startTime)
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();

            var builder = new StringBuilder();

            // Excel 專用：指定逗號作為分隔符，並搭配 UTF-8 BOM 避免中文亂碼
            builder.AppendLine("sep=,");

            builder.AppendLine(
                "紀錄時間,溫度(°C),濕度(%),土壤數值,土壤狀態,滴灌,生長燈,液肥"
            );

            foreach (var item in data)
            {
                builder.AppendLine(
                    $"{item.CreatedAt:yyyy-MM-dd HH:mm:ss}," +
                    $"{item.Temp}," +
                    $"{item.Humidity}," +
                    $"{item.Soil}," +
                    $"{TranslateSoilState(item.SoilState)}," +
                    $"{BoolText(item.Relay5)}," +
                    $"{BoolText(item.Relay6)}," +
                    $"{BoolText(item.Stepper)}"
                );
            }

            var utf8WithBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
            var result = utf8WithBom.GetBytes(builder.ToString());

            string fileName = $"SensorData_{DateTime.Now:yyyyMMdd_HHmm}.csv";

            return File(result, "text/csv; charset=utf-8", fileName);
        }

        private static string BoolText(bool value)
        {
            return value ? "ON" : "OFF";
        }

        private static string TranslateSoilState(string? soilState)
        {
            return soilState?.ToUpperInvariant() switch
            {
                "DRY" => "乾燥",
                "MOIST" => "適中",
                "WET" => "濕潤",
                _ => soilState ?? string.Empty
            };
        }
    }
}