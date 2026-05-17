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

            var data = await _db.SensorLogs
                .AsNoTracking()
                .Where(s => s.CreatedAt >= startTime)
                .OrderBy(s => s.CreatedAt)
                .ToListAsync();

            var chartData = data.Select(x => new
            {
                time = x.CreatedAt,

                temp = x.Temp,
                humidity = x.Humidity,
                soil = x.Soil,
                soilState = x.SoilState,

                tempLimit = x.TempLimit,
                soilLimit = x.SoilLimit,

                tempAuto = x.TempAuto,
                soilAuto = x.SoilAuto,

                relay1 = x.Relay1,
                relay2 = x.Relay2,
                relay3 = x.Relay3,
                relay4 = x.Relay4,
                relay5 = x.Relay5,
                relay6 = x.Relay6,

                stepper = x.Stepper
            });

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

            builder.AppendLine(
                "紀錄時間,溫度(°C),濕度(%),土壤數值,土壤狀態,溫度門檻,土壤門檻,溫控自動,土壤自動,Relay1,Relay2,Relay3,Relay4,Relay5,Relay6,Stepper"
            );

            foreach (var item in data)
            {
                builder.AppendLine(
                    $"{item.CreatedAt:yyyy-MM-dd HH:mm:ss}," +
                    $"{item.Temp}," +
                    $"{item.Humidity}," +
                    $"{item.Soil}," +
                    $"{item.SoilState}," +
                    $"{item.TempLimit}," +
                    $"{item.SoilLimit}," +
                    $"{BoolText(item.TempAuto)}," +
                    $"{BoolText(item.SoilAuto)}," +
                    $"{BoolText(item.Relay1)}," +
                    $"{BoolText(item.Relay2)}," +
                    $"{BoolText(item.Relay3)}," +
                    $"{BoolText(item.Relay4)}," +
                    $"{BoolText(item.Relay5)}," +
                    $"{BoolText(item.Relay6)}," +
                    $"{BoolText(item.Stepper)}"
                );
            }

            var utf8WithBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
            var result = utf8WithBom.GetBytes(builder.ToString());

            string fileName = $"SensorData_{DateTime.Now:yyyyMMdd_HHmm}.csv";
            return File(result, "text/csv", fileName);
        }

        private static string BoolText(bool value)
        {
            return value ? "ON" : "OFF";
        }
    }
}