using Microsoft.EntityFrameworkCore;
using System.Text;
using WebLoginDemo2.Data;
using WebLoginDemo2.Models;

namespace WebLoginDemo2.Services
{
    public class DataPruningService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<DataPruningService> _logger;

        // 設定每天檢查一次
        private readonly TimeSpan _period = TimeSpan.FromHours(24);

        public DataPruningService(IServiceProvider serviceProvider, ILogger<DataPruningService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("自動備份與清理服務已啟動...");

            // 啟動時先執行一次 (避免開發時要等24小時才看到效果)
            await DoWorkAsync();

            using PeriodicTimer timer = new PeriodicTimer(_period);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await DoWorkAsync();
            }
        }

        private async Task DoWorkAsync()
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                // 定義閥值：7 天前的資料
                var thresholdDate = DateTime.Now.AddDays(-7);

                try
                {
                    // 1. 【查詢】找出即將被刪除的資料
                    // 使用 AsNoTracking 增加效能，因為我們只是要讀取來存檔
                    var dataToArchive = await db.SensorLogs
                        .Where(x => x.CreatedAt < thresholdDate)
                        .OrderBy(x => x.CreatedAt)
                        .AsNoTracking()
                        .ToListAsync();

                    if (dataToArchive.Any())
                    {
                        _logger.LogInformation($"發現 {dataToArchive.Count} 筆過期資料，準備進行自動備份...");

                        // 2. 【備份】寫入 CSV 檔案
                        string backupFolder = Path.Combine(Directory.GetCurrentDirectory(), "Backups");

                        // 如果資料夾不存在，就建立它
                        if (!Directory.Exists(backupFolder))
                        {
                            Directory.CreateDirectory(backupFolder);
                        }

                        // 產生檔名: AutoBackup_yyyyMMdd_HHmmss.csv
                        string fileName = $"AutoBackup_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
                        string filePath = Path.Combine(backupFolder, fileName);

                        var sb = new StringBuilder();
                        sb.AppendLine("時間,溫度,濕度,光照,土壤,CO2,PH"); // 標題列

                        foreach (var item in dataToArchive)
                        {
                            // 簡單的 CSV 格式化
                            string soilText = (item.Soil > 50) ? "Wet" : "Dry";
                            sb.AppendLine($"{item.CreatedAt:yyyy-MM-dd HH:mm:ss},{item.Temp},{item.Humidity},{item.Light},{soilText},{item.CO2},{item.PH}");
                        }

                        // 寫入檔案 (使用 UTF8 BOM 以防 Excel 中文亂碼)
                        await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8);

                        _logger.LogInformation($"✅ 自動備份成功！檔案位置: {filePath}");

                        // 3. 【刪除】確認備份成功後，刪除資料庫紀錄
                        await db.SensorLogs
                            .Where(x => x.CreatedAt < thresholdDate)
                            .ExecuteDeleteAsync();

                        _logger.LogInformation("🗑️ 過期資料已從資料庫安全刪除。");
                    }
                    else
                    {
                        _logger.LogInformation("目前沒有過期資料需要備份。");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ 自動備份與清理失敗！資料庫未更動。");
                }
            }
        }
    }
}