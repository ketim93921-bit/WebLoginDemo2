using Microsoft.EntityFrameworkCore;
using System.Text;
using WebLoginDemo2.Data;

namespace WebLoginDemo2.Services
{
    public class DataPruningService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<DataPruningService> _logger;

        // 設定每天檢查一次
        private readonly TimeSpan _period = TimeSpan.FromHours(24);

        public DataPruningService(
            IServiceProvider serviceProvider,
            ILogger<DataPruningService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("自動備份與清理服務已啟動...");

            await DoWorkAsync();

            using PeriodicTimer timer = new PeriodicTimer(_period);

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await DoWorkAsync();
            }
        }

        private async Task DoWorkAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // 定義閥值：7 天前的資料
            var thresholdDate = DateTime.Now.AddDays(-7);

            try
            {
                var dataToArchive = await db.SensorLogs
                    .Where(x => x.CreatedAt < thresholdDate)
                    .OrderBy(x => x.CreatedAt)
                    .AsNoTracking()
                    .ToListAsync();

                if (!dataToArchive.Any())
                {
                    _logger.LogInformation("目前沒有過期資料需要備份。");
                    return;
                }

                _logger.LogInformation(
                    "發現 {Count} 筆過期資料，準備進行自動備份...",
                    dataToArchive.Count
                );

                string backupFolder = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "Backups"
                );

                if (!Directory.Exists(backupFolder))
                {
                    Directory.CreateDirectory(backupFolder);
                }

                string fileName = $"AutoBackup_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
                string filePath = Path.Combine(backupFolder, fileName);

                var sb = new StringBuilder();

                sb.AppendLine(
                    "時間,溫度,濕度,土壤數值,土壤狀態,滴灌,生長燈,液肥"
                );

                foreach (var item in dataToArchive)
                {
                    sb.AppendLine(
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

                // 使用 UTF-8 BOM，避免 Excel 開啟中文亂碼
                var utf8WithBom = new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: true
                );

                await File.WriteAllTextAsync(
                    filePath,
                    sb.ToString(),
                    utf8WithBom
                );

                _logger.LogInformation("✅ 自動備份成功！檔案位置: {FilePath}", filePath);

                await db.SensorLogs
                    .Where(x => x.CreatedAt < thresholdDate)
                    .ExecuteDeleteAsync();

                _logger.LogInformation("🗑️ 過期資料已從資料庫安全刪除。");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ 自動備份與清理失敗！資料庫未更動。");
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
                "WET" => "濕潤",
                _ => soilState ?? string.Empty
            };
        }
    }
}