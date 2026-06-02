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

        private const int MaxHistoryMinutes = 10080; // 7 天

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
        // API: GET /Data/History?minutes=10080
        //
        // 10 / 30 / 60 分鐘：每 1 分鐘平均
        // 1 天：每 5 分鐘平均
        // 1 週：每 60 分鐘平均
        // ==========================================
        [HttpGet("History")]
        public async Task<IActionResult> History(int minutes = 60)
        {
            minutes = Math.Clamp(minutes, 1, MaxHistoryMinutes);

            var startTimeUtc = DateTime.UtcNow.AddMinutes(-minutes);

            var rawData = await _db.SensorLogs
                .AsNoTracking()
                .Where(s => s.CreatedAt >= startTimeUtc)
                .Where(s =>
                    s.Temp > 0 &&
                    s.Temp < 60 &&
                    s.Humidity > 0 &&
                    s.Humidity <= 100 &&
                    s.Soil >= 0 &&
                    s.Soil <= 1024)
                .OrderBy(s => s.CreatedAt)
                .ToListAsync();

            int bucketMinutes = GetBucketMinutes(minutes);
            long bucketTicks = TimeSpan.FromMinutes(bucketMinutes).Ticks;

            var chartData = rawData
                .GroupBy(x =>
                {
                    var utcTime = NormalizeAsUtc(x.CreatedAt);
                    long bucket = utcTime.Ticks / bucketTicks;
                    return bucket;
                })
                .Select(g =>
                {
                    var last = g
                        .OrderBy(x => NormalizeAsUtc(x.CreatedAt))
                        .Last();

                    var bucketUtc = new DateTime(
                        g.Key * bucketTicks,
                        DateTimeKind.Utc
                    );

                    return new
                    {
                        // 回傳標準 UTC ISO 時間
                        // 前端用 Asia/Taipei 顯示
                        time = bucketUtc.ToString("O"),

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
        // API: GET /Data/Export?minutes=10080
        // ==========================================
        [HttpGet("Export")]
        public async Task<IActionResult> Export(int minutes = 60)
        {
            minutes = Math.Clamp(minutes, 1, MaxHistoryMinutes);

            var startTimeUtc = DateTime.UtcNow.AddMinutes(-minutes);

            var data = await _db.SensorLogs
                .AsNoTracking()
                .Where(s => s.CreatedAt >= startTimeUtc)
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();

            var builder = new StringBuilder();

            // Excel 專用：指定逗號作為分隔符
            builder.AppendLine("sep=,");

            builder.AppendLine(
                "紀錄時間,溫度(°C),濕度(%),土壤數值,土壤狀態,滴灌,生長燈,液肥"
            );

            foreach (var item in data)
            {
                var taipeiTime = ToTaipeiTime(item.CreatedAt);

                builder.AppendLine(
                    $"{taipeiTime:yyyy-MM-dd HH:mm:ss}," +
                    $"{item.Temp}," +
                    $"{item.Humidity}," +
                    $"{item.Soil}," +
                    $"{TranslateSoilState(item.SoilState)}," +
                    $"{BoolText(item.Relay5)}," +
                    $"{BoolText(item.Relay6)}," +
                    $"{BoolText(item.Stepper)}"
                );
            }

            // Windows Excel 直接開啟 CSV 時，Big5 對繁體中文最穩定
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            var big5 = Encoding.GetEncoding("big5");
            var result = big5.GetBytes(builder.ToString());

            string fileName = $"SensorData_{DateTime.Now:yyyyMMdd_HHmm}.csv";

            return File(result, "text/csv; charset=big5", fileName);
        }

        private static int GetBucketMinutes(int minutes)
        {
            if (minutes <= 60)
                return 1;

            if (minutes <= 1440)
                return 5;

            return 60;
        }

        private static DateTime NormalizeAsUtc(DateTime value)
        {
            return value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),

                // Render / 雲端常見資料會是 Unspecified，但實際是 UTC
                _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
            };
        }

        private static DateTime ToTaipeiTime(DateTime value)
        {
            var utc = NormalizeAsUtc(value);
            var taipeiZone = GetTaipeiTimeZone();

            return TimeZoneInfo.ConvertTimeFromUtc(utc, taipeiZone);
        }

        private static TimeZoneInfo GetTaipeiTimeZone()
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Asia/Taipei");
            }
            catch
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Taipei Standard Time");
            }
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
                "WET" => "潮濕",
                _ => soilState ?? string.Empty
            };
        }
    }
}