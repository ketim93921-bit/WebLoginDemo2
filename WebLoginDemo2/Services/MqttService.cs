using MQTTnet;
using MQTTnet.Client;
using Microsoft.AspNetCore.SignalR;
using Newtonsoft.Json;
using System.Text;
using System.Threading;
using System.Linq;
using WebLoginDemo2.Hubs;
using WebLoginDemo2.Data;
using WebLoginDemo2.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace WebLoginDemo2.Services
{
    public class MqttService : IHostedService
    {
        // 感測資料主題
        private const string MqttTopic = "dht11";

        // 風扇控制主題
        private const string FanCommandTopic = "farm/fan/cmd";

        private const string MqttServerIp = "broker.hivemq.com";
        private const int MqttServerPort = 1883;

        private SensorData _latestData = new SensorData { Time = DateTime.Now };

        // 回傳副本，避免外部直接改到內部資料
        public SensorData GetLatestSensorData()
        {
            return new SensorData
            {
                Time = _latestData.Time,
                Temp = _latestData.Temp,
                Humidity = _latestData.Humidity,
                Light = _latestData.Light,
                Soil = _latestData.Soil,
                CO2 = _latestData.CO2,
                PH = _latestData.PH,
                Fan = _latestData.Fan
            };
        }

        // 提供給 LINE / Controller 判斷目前 MQTT 是否在線
        public bool IsMqttConnected => _mqttClient?.IsConnected ?? false;

        // 提供給 LINE 直接組成文字版狀態
        public string GetStatusSummaryText()
        {
            var data = GetLatestSensorData();
            string soilText = data.Soil > 0.5 ? "濕潤" : "乾燥";
            string fanText = data.Fan ? "ON" : "OFF";
            string onlineText = IsMqttConnected ? "在線" : "離線";

            return
                $"📡 智慧農場目前狀態\n" +
                $"設備連線：{onlineText}\n" +
                $"溫度：{data.Temp:F1}°C\n" +
                $"濕度：{data.Humidity:F1}%\n" +
                $"光照：{data.Light:F1}%\n" +
                $"土壤：{soilText}\n" +
                $"風扇：{fanText}\n" +
                $"更新時間：{data.Time:yyyy-MM-dd HH:mm:ss}";
        }

        private readonly ILogger<MqttService> _logger;
        private readonly IHubContext<SensorHub> _hubContext;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly NotificationService _notificationService;

        // 通知控制
        private DateTime _lastAlertTime = DateTime.MinValue;
        private const int AlertCooldownMinutes = 10;

        // 避免同一段異常一直通知
        private bool _isInAlertState = false;
        private string _lastAlertKey = "";

        private IMqttClient? _mqttClient;
        private MqttClientOptions? _options;

        // 避免重複重連
        private readonly SemaphoreSlim _connectLock = new(1, 1);

        // 停機時避免又自動重連
        private bool _isStopping = false;

        public MqttService(
            ILogger<MqttService> logger,
            IHubContext<SensorHub> hubContext,
            IServiceScopeFactory scopeFactory,
            NotificationService notificationService)
        {
            _logger = logger;
            _hubContext = hubContext;
            _scopeFactory = scopeFactory;
            _notificationService = notificationService;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            var factory = new MqttFactory();
            _mqttClient = factory.CreateMqttClient();

            _options = new MqttClientOptionsBuilder()
                .WithTcpServer(MqttServerIp, MqttServerPort)
                .WithClientId("WebClient_" + Guid.NewGuid().ToString("N")[..8])
                .WithCleanSession()
                .Build();

            _mqttClient.ConnectedAsync += HandleConnectedAsync;
            _mqttClient.DisconnectedAsync += HandleDisconnectedAsync;
            _mqttClient.ApplicationMessageReceivedAsync += HandleMessageReceivedAsync;

            _ = ConnectAsync(cancellationToken);
            return Task.CompletedTask;
        }

        private async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            if (_mqttClient == null || _options == null)
                return;

            await _connectLock.WaitAsync(cancellationToken);
            try
            {
                while (!_isStopping && !_mqttClient.IsConnected && !cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        _logger.LogInformation("正在嘗試連線到 MQTT Broker...");
                        await _mqttClient.ConnectAsync(_options, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogInformation("MQTT 連線已取消。");
                        return;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "❌ 連線失敗，5秒後重試...");
                        await Task.Delay(5000, cancellationToken);
                    }
                }
            }
            finally
            {
                _connectLock.Release();
            }
        }

        private async Task HandleConnectedAsync(MqttClientConnectedEventArgs e)
        {
            if (_mqttClient == null) return;

            _logger.LogInformation("✅ MQTT 連線成功！");
            await _mqttClient.SubscribeAsync(MqttTopic);
            _logger.LogInformation("✅ 已訂閱 Topic：{Topic}", MqttTopic);
        }

        private async Task HandleDisconnectedAsync(MqttClientDisconnectedEventArgs e)
        {
            if (_isStopping)
            {
                _logger.LogInformation("MQTT 正在停止，不進行重連。");
                return;
            }

            _logger.LogWarning("⚠️ MQTT 斷線，嘗試重連...");
            await Task.Delay(TimeSpan.FromSeconds(5));
            await ConnectAsync();
        }

        private async Task HandleMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
        {
            string payload = e.ApplicationMessage.Payload == null
                ? string.Empty
                : Encoding.UTF8.GetString(e.ApplicationMessage.Payload);

            try
            {
                if (string.IsNullOrWhiteSpace(payload) || !payload.Trim().StartsWith("{"))
                    return;

                var raw = JsonConvert.DeserializeObject<SensorDataModel>(payload);
                if (raw == null) return;

                var data = new SensorData
                {
                    Time = DateTime.Now,
                    Temp = raw.Temp,
                    Humidity = raw.Humidity,
                    Light = raw.Light,
                    Soil = raw.Soil,
                    CO2 = raw.CO2,
                    PH = raw.PH,
                    Fan = raw.Fan == 1
                };

                _latestData = data;

                _logger.LogInformation(
                    "[DATA] Temp={Temp:F1}, Hum={Hum:F1}, Soil={Soil:F1}, Light={Light:F1}, CO2={CO2:F1}, PH={PH:F1}, Fan={Fan}",
                    data.Temp, data.Humidity, data.Soil, data.Light, data.CO2, data.PH, data.Fan ? "ON" : "OFF");

                await CheckThresholdsAndNotifyAsync(data);

                await _hubContext.Clients.Group("Dashboard")
                    .SendAsync("ReceiveSensorData", data);

                await SaveToDatabaseAsync(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ 數據處理錯誤");
            }
        }

        // 網頁 / LINE 共用：發送風扇控制命令
        public async Task PublishFanCommandAsync(bool on, CancellationToken cancellationToken = default)
        {
            if (_mqttClient == null)
                throw new InvalidOperationException("MQTT Client 尚未初始化");

            if (!_mqttClient.IsConnected)
                await ConnectAsync(cancellationToken);

            var payload = on ? "ON" : "OFF";

            var message = new MqttApplicationMessageBuilder()
                .WithTopic(FanCommandTopic)
                .WithPayload(payload)
                .WithRetainFlag(true)
                .Build();

            await _mqttClient.PublishAsync(message, cancellationToken);

            // 先同步更新記憶體狀態，讓 Dashboard / LINE 立即看到結果
            _latestData.Fan = on;
            _latestData.Time = DateTime.Now;

            await _hubContext.Clients.Group("Dashboard")
                .SendAsync("ReceiveSensorData", _latestData, cancellationToken);

            _logger.LogInformation("✅ 已發送風扇控制指令：{Payload}", payload);
        }

        private async Task CheckThresholdsAndNotifyAsync(SensorData data)
        {
            var alerts = new List<string>();

            if (data.Temp > 35.0) alerts.Add($"🌡️ 溫度過高: {data.Temp:F1}°C (門檻 > 35)");
            if (data.Temp < 10.0) alerts.Add($"❄️ 溫度過低: {data.Temp:F1}°C (門檻 < 10)");

            // ESP Soil: 0=乾, 1=濕
            if (data.Soil < 0.5) alerts.Add($"🏜️ 土壤乾燥: {data.Soil:F1} (0=乾, 1=濕)");

            if (data.Humidity < 40.0) alerts.Add($"💧 環境濕度太低: {data.Humidity:F1}% (門檻 < 40)");

            _logger.LogInformation("[ALERT] count={Count} => {Alerts}", alerts.Count, string.Join(" | ", alerts));

            var alertKey = string.Join("|", alerts);

            if (!alerts.Any())
            {
                if (_isInAlertState)
                    _logger.LogInformation("✅ 異常解除，回到正常狀態。");

                _isInAlertState = false;
                _lastAlertKey = "";
                return;
            }

            bool isNewAlertStart = !_isInAlertState;
            bool isAlertChanged = _isInAlertState && alertKey != _lastAlertKey;

            if (isNewAlertStart || isAlertChanged)
            {
                var minutesSince = (DateTime.Now - _lastAlertTime).TotalMinutes;
                _logger.LogInformation(
                    "[ALERT] lastAlert={LastAlert:HH:mm:ss}, minutesSince={MinutesSince:F1}, cooldown={Cooldown}",
                    _lastAlertTime, minutesSince, AlertCooldownMinutes);

                if (minutesSince < AlertCooldownMinutes)
                {
                    _logger.LogInformation("⏳ 冷卻時間內，跳過通知。");
                    _isInAlertState = true;
                    _lastAlertKey = alertKey;
                    return;
                }

                var snapshot =
                    $"📌 目前數值\n" +
                    $"Temp: {data.Temp:F1}°C\n" +
                    $"Humidity: {data.Humidity:F1}%\n" +
                    $"Soil: {data.Soil:F1}\n" +
                    $"Light: {data.Light:F1}\n" +
                    $"CO2: {data.CO2:F1}\n" +
                    $"PH: {data.PH:F1}\n" +
                    $"Fan: {(data.Fan ? "ON" : "OFF")}";

                string msg =
                    $"🚨【智慧農場警報】\n" +
                    $"⏰ {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n\n" +
                    string.Join("\n", alerts) + "\n\n" +
                    snapshot;

                // 給 LINE 大字警告卡用
                string alertTitle;
                string alertDetail;

                if (alerts.Any(x => x.Contains("土壤乾燥")))
                {
                    alertTitle = "土壤太乾";
                    alertDetail = "建議盡快澆水，或查看農場狀態";
                }
                else if (alerts.Any(x => x.Contains("溫度過高")))
                {
                    alertTitle = "溫度過高";
                    alertDetail = $"目前溫度 {data.Temp:F1}°C，建議先通風或打開風扇";
                }
                else if (alerts.Any(x => x.Contains("溫度過低")))
                {
                    alertTitle = "溫度過低";
                    alertDetail = $"目前溫度 {data.Temp:F1}°C，請留意環境保溫";
                }
                else if (alerts.Any(x => x.Contains("環境濕度太低")))
                {
                    alertTitle = "濕度太低";
                    alertDetail = $"目前濕度 {data.Humidity:F1}%，請留意作物狀態";
                }
                else
                {
                    alertTitle = "農場狀態異常";
                    alertDetail = string.Join("；", alerts);
                }

                // LINE：推播大字 Flex 警告卡
                await _notificationService.SendLineAlertCardAsync(alertTitle, alertDetail, true);

                // Email：保留完整資訊
                await _notificationService.SendEmailAsync("農場異常通知", msg.Replace("\n", "<br>"));

                _lastAlertTime = DateTime.Now;
                _isInAlertState = true;
                _lastAlertKey = alertKey;

                _logger.LogInformation("✅ 已發送異常警報通知。");
                return;
            }

            _logger.LogInformation("⚠️ 異常持續中（未變更），不重複通知。");
            _isInAlertState = true;
            _lastAlertKey = alertKey;
        }

        private async Task SaveToDatabaseAsync(SensorData data)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.SensorLogs.Add(new SensorLog
            {
                Temp = data.Temp,
                Humidity = data.Humidity,
                Light = data.Light,
                Soil = data.Soil,
                CO2 = data.CO2,
                PH = data.PH,
                CreatedAt = data.Time
            });

            await db.SaveChangesAsync();
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _isStopping = true;

            if (_mqttClient != null && _mqttClient.IsConnected)
            {
                await _mqttClient.DisconnectAsync(cancellationToken: cancellationToken);
            }
        }
    }

    public class SensorDataModel
    {
        [JsonProperty("Temp")]
        public double Temp { get; set; }

        [JsonProperty("Humidity")]
        public double Humidity { get; set; }

        [JsonProperty("Light")]
        public double Light { get; set; }

        [JsonProperty("Soil")]
        public double Soil { get; set; }

        [JsonProperty("CO2")]
        public double CO2 { get; set; }

        [JsonProperty("PH")]
        public double PH { get; set; }

        [JsonProperty("Fan")]
        public int Fan { get; set; }
    }
}