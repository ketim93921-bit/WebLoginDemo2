using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;
using WebLoginDemo2.Data;
using WebLoginDemo2.Models;
using WebLoginDemo2.Services;

namespace WebLoginDemo2.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class DataController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly MqttService _mqttService;

        // 注入資料庫與 MQTT 服務
        public DataController(AppDbContext db, MqttService mqttService)
        {
            _db = db;
            _mqttService = mqttService;
        }

        // ==========================================
        // 1. 取得最新一筆數據 (給儀表板初始化用)
        // API: GET /Data/Latest
        // ==========================================
        [HttpGet("Latest")]
        public IActionResult Latest()
        {
            // 直接從 MqttService 的記憶體變數讀取，速度最快
            var latest = _mqttService.GetLatestSensorData();
            return Ok(latest);
        }

        // ==========================================
        // 2. 取得歷史數據 (給 Chart.js 圖表用)
        // API: GET /Data/History?minutes=10
        // ==========================================
        [HttpGet("History")]
        public async Task<IActionResult> History(int minutes = 60)
        {
            // 計算起始時間
            var startTime = DateTime.Now.AddMinutes(-minutes);

            // 從資料庫撈取指定時間內的資料
            var data = await _db.SensorLogs
                .AsNoTracking() // 唯讀查詢，效能較佳
                .Where(s => s.CreatedAt >= startTime)
                .OrderBy(s => s.CreatedAt) // 圖表需要由舊到新排序
                .ToListAsync();

            // 轉換為前端圖表需要的格式 (小寫開頭，方便 JS 讀取)
            var chartData = data.Select(x => new
            {
                time = x.CreatedAt,   // 對應 JS 的 d.time
                temp = x.Temp,        // 對應 JS 的 d.temp
                humidity = x.Humidity,// 對應 JS 的 d.humidity
                light = x.Light,      // 對應 JS 的 d.light
                soil = x.Soil         // 對應 JS 的 d.soil
            });

            return Ok(chartData);
        }

        // ==========================================
        // 3. 匯出 CSV 檔案 (給 Excel 用)
        // API: GET /Data/Export?minutes=60
        // ==========================================
        [HttpGet("Export")]
        public async Task<IActionResult> Export(int minutes = 60)
        {
            // 1. 撈取資料 (跟 History 邏輯類似，但改為由新到舊排序)
            var startTime = DateTime.Now.AddMinutes(-minutes);
            var data = await _db.SensorLogs
                .AsNoTracking()
                .Where(s => s.CreatedAt >= startTime)
                .OrderByDescending(s => s.CreatedAt) // 匯出的報表通常希望最新的在上面
                .ToListAsync();

            // 2. 建立 CSV 內容
            var builder = new StringBuilder();

            // 加入標題列 (Header)
            builder.AppendLine("紀錄時間,溫度(°C),濕度(%),光照(Lux),土壤數值,土壤狀態");

            // 加入資料列
            foreach (var item in data)
            {
                // 簡單的狀態判斷邏輯 (可依據實際傳感器數值調整)
                // 假設 > 0.5 為濕潤 (如果是類比訊號 0-1024，請改成 > 500 之類的)
                string soilStatus = item.Soil > 0.5 ? "濕潤 (Wet)" : "乾燥 (Dry)";

                // 組合一行資料
                builder.AppendLine($"{item.CreatedAt:yyyy-MM-dd HH:mm:ss},{item.Temp},{item.Humidity},{item.Light},{item.Soil},{soilStatus}");
            }

            // 3. 處理編碼 (關鍵步驟：加入 BOM)
            // 如果沒有這段，Excel 打開中文會變亂碼
            var csvBytes = Encoding.UTF8.GetBytes(builder.ToString());
            var bom = Encoding.UTF8.GetPreamble();
            var result = new byte[bom.Length + csvBytes.Length];

            // 將 BOM 放在檔案最前面
            Buffer.BlockCopy(bom, 0, result, 0, bom.Length);
            Buffer.BlockCopy(csvBytes, 0, result, bom.Length, csvBytes.Length);

            // 4. 回傳檔案
            string fileName = $"SensorData_{DateTime.Now:yyyyMMdd_HHmm}.csv";
            return File(result, "text/csv", fileName);
        }
    }
}