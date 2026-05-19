using MQTTnet;
using MQTTnet.Client;
using Microsoft.AspNetCore.SignalR;
using Newtonsoft.Json;
using System.Text;
using WebLoginDemo2.Hubs;
using WebLoginDemo2.Data;
using WebLoginDemo2.Models;

namespace WebLoginDemo2.Services
{
    public class MqttService : IHostedService
    {
        // ================================
        // MQTT Topic 設定
        // 對應新版 Arduino 程式
        // ================================
        private const string MqttTopic = "esp8266/sensor";

        // D6 生長燈控制 Topic
        // Payload:
        // ON  = 立即開啟
        // OFF = 立即關閉
        // 1~6 = 10~60 分鐘定時
        private const string Relay6CommandTopic = "esp8266/relay/d6";

        // Stepper 液肥控制 Topic
        // Payload:
        // ON  = 立即啟動
        // OFF = 立即停止
        // 1~6 = 10~60 分鐘定時
        private const string StepperCommandTopic = "esp8266/stepper";

        private const string MqttServerIp = "broker.hivemq.com";
        private const int MqttServerPort = 1883;

        // Arduino 新程式：1 單位 = 10 分鐘
        private const int TimerUnitMinutes = 10;

        // Arduino 土壤門檻：soil > 950 啟動滴灌
        private const int DefaultSoilLimit = 950;

        // ================================
        // 最新感測資料
        // ================================
        private SensorData _latestData = new SensorData
        {
            Time = DateTime.Now
        };

        public SensorData GetLatestSensorData()
        {
            return new SensorData
            {
                Time = _latestData.Time,
                Temp = _latestData.Temp,
                Humidity = _latestData.Humidity,
                Soil = _latestData.Soil,
                SoilState = _latestData.SoilState,
                Relay5 = _latestData.Relay5,
                Relay6 = _latestData.Relay6,
                Stepper = _latestData.Stepper
            };
        }

        public bool IsMqttConnected => _mqttClient?.IsConnected ?? false;

        public string GetStatusSummaryText()
        {
            var data = GetLatestSensorData();

            string onlineText = IsMqttConnected ? "在線" : "離線";

            return
                $"📡 智慧農場目前狀態\n" +
                $"設備連線：{onlineText}\n" +
                $"溫度：{data.Temp:F1}°C\n" +
                $"濕度：{data.Humidity:F1}%\n" +
                $"土壤數值：{data.Soil:F0}\n" +
                $"土壤狀態：{data.SoilState}\n" +
                $"滴灌：{OnOffText(data.Relay5)}\n" +
                $"生長燈：{OnOffText(data.Relay6)}\n" +
                $"液肥：{OnOffText(data.Stepper)}\n" +
                $"更新時間：{data.Time:yyyy-MM-dd HH:mm:ss}";
        }

        // ================================
        // DI 服務
        // ================================
        private readonly ILogger<MqttService> _logger;
        private readonly IHubContext<SensorHub> _hubContext;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly NotificationService _notificationService;

        // ================================
        // 通知控制
        // ================================
        private DateTime _lastAlertTime = DateTime.MinValue;
        private const int AlertCooldownMinutes = 10;

        private bool _isInAlertState = false;
        private string _lastAlertKey = "";

        // ================================
        // MQTT Client
        // ================================
        private IMqttClient? _mqttClient;
        private MqttClientOptions? _options;

        private readonly SemaphoreSlim _connectLock = new(1, 1);
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

        // ================================
        // 啟動 MQTT
        // ================================
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

        // ================================
        // MQTT 連線
        // ================================
        private async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            if (_mqttClient == null || _options == null)
                return;

            await _connectLock.WaitAsync(cancellationToken);

            try
            {
                while (!_isStopping &&
                       !_mqttClient.IsConnected &&
                       !cancellationToken.IsCancellationRequested)
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
                        _logger.LogWarning(ex, "❌ MQTT 連線失敗，5 秒後重試...");
                        await Task.Delay(5000, cancellationToken);
                    }
                }
            }
            finally
            {
                _connectLock.Release();
            }
        }

        // ================================
        // MQTT 已連線
        // ================================
        private async Task HandleConnectedAsync(MqttClientConnectedEventArgs e)
        {
            if (_mqttClient == null)
                return;

            _logger.LogInformation("✅ MQTT 連線成功！");

            await _mqttClient.SubscribeAsync(MqttTopic);

            _logger.LogInformation("✅ 已訂閱 Topic：{Topic}", MqttTopic);
        }

        // ================================
        // MQTT 斷線重連
        // ================================
        private async Task HandleDisconnectedAsync(MqttClientDisconnectedEventArgs e)
        {
            if (_isStopping)
            {
                _logger.LogInformation("MQTT 正在停止，不進行重連。");
                return;
            }

            _logger.LogWarning("⚠️ MQTT 斷線，5 秒後嘗試重連...");

            await Task.Delay(TimeSpan.FromSeconds(5));
            await ConnectAsync();
        }

        // ================================
        // 接收 MQTT 感測資料
        // ================================
        private async Task HandleMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
        {
            string payload = e.ApplicationMessage.Payload == null
                ? string.Empty
                : Encoding.UTF8.GetString(e.ApplicationMessage.Payload);

            try
            {
                if (string.IsNullOrWhiteSpace(payload) ||
                    !payload.Trim().StartsWith("{"))
                {
                    return;
                }

                _logger.LogInformation("📩 MQTT 收到資料：{Payload}", payload);

                var raw = JsonConvert.DeserializeObject<SensorDataModel>(payload);

                if (raw == null)
                    return;

                var data = new SensorData
                {
                    Time = DateTime.Now,
                    Temp = raw.Temperature,
                    Humidity = raw.Humidity,
                    Soil = raw.Soil,
                    SoilState = GetSoilState(raw.Soil),
                    Relay5 = IsOn(raw.RelayD5),
                    Relay6 = IsOn(raw.RelayD6),
                    Stepper = IsOn(raw.Stepper)
                };

                _latestData = data;

                _logger.LogInformation(
                    "[DATA] Temp={Temp:F1}, Humidity={Humidity:F1}, Soil={Soil:F0}, SoilState={SoilState}, 滴灌={Relay5}, 生長燈={Relay6}, 液肥={Stepper}",
                    data.Temp,
                    data.Humidity,
                    data.Soil,
                    data.SoilState,
                    OnOffText(data.Relay5),
                    OnOffText(data.Relay6),
                    OnOffText(data.Stepper)
                );

                await CheckThresholdsAndNotifyAsync(data);

                await _hubContext.Clients.Group("Dashboard")
                    .SendAsync("ReceiveSensorData", data);

                await SaveToDatabaseAsync(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ MQTT 數據處理錯誤");
            }
        }

        // ================================
        // 生長燈手動控制
        // Relay6 / D6
        // ================================
        public async Task PublishRelayCommandAsync(
            int relayNumber,
            bool on,
            CancellationToken cancellationToken = default)
        {
            if (relayNumber != 6)
            {
                throw new InvalidOperationException(
                    "目前新版 Arduino 只有 Relay6 / D6 支援遠端控制。"
                );
            }

            if (_mqttClient == null)
                throw new InvalidOperationException("MQTT Client 尚未初始化");

            if (!_mqttClient.IsConnected)
                await ConnectAsync(cancellationToken);

            string payload = on ? "ON" : "OFF";

            var message = new MqttApplicationMessageBuilder()
                .WithTopic(Relay6CommandTopic)
                .WithPayload(payload)
                .WithRetainFlag(false)
                .Build();

            await _mqttClient.PublishAsync(message, cancellationToken);

            _latestData.Relay6 = on;
            _latestData.Time = DateTime.Now;

            await _hubContext.Clients.Group("Dashboard")
                .SendAsync("ReceiveSensorData", _latestData, cancellationToken);

            _logger.LogInformation(
                "✅ 已發送生長燈控制指令 Topic={Topic}, Payload={Payload}",
                Relay6CommandTopic,
                payload
            );
        }

        // ================================
        // 生長燈定時控制
        // Relay6 / D6
        //
        // 重要：
        // 這裡只送數字 1~6
        // 不再額外送 ON
        //
        // 因為 ESP8266 收到數字會自行啟動定時
        // 若再送 ON，會覆蓋 timer 變成常亮
        // ================================
        public async Task PublishRelay6TimerCommandAsync(
            int unitCount,
            CancellationToken cancellationToken = default)
        {
            if (unitCount < 0)
                throw new ArgumentOutOfRangeException(nameof(unitCount), "生長燈定時單位不能小於 0。");

            if (unitCount > 144)
                throw new ArgumentOutOfRangeException(nameof(unitCount), "生長燈定時最多 144 單位，也就是 24 小時。");

            if (_mqttClient == null)
                throw new InvalidOperationException("MQTT Client 尚未初始化");

            if (!_mqttClient.IsConnected)
                await ConnectAsync(cancellationToken);

            if (unitCount == 0)
            {
                var offMessage = new MqttApplicationMessageBuilder()
                    .WithTopic(Relay6CommandTopic)
                    .WithPayload("OFF")
                    .WithRetainFlag(false)
                    .Build();

                await _mqttClient.PublishAsync(offMessage, cancellationToken);

                _latestData.Relay6 = false;
            }
            else
            {
                var timerMessage = new MqttApplicationMessageBuilder()
                    .WithTopic(Relay6CommandTopic)
                    .WithPayload(unitCount.ToString())
                    .WithRetainFlag(false)
                    .Build();

                await _mqttClient.PublishAsync(timerMessage, cancellationToken);

                _latestData.Relay6 = true;
            }

            _latestData.Time = DateTime.Now;

            await _hubContext.Clients.Group("Dashboard")
                .SendAsync("ReceiveSensorData", _latestData, cancellationToken);

            _logger.LogInformation(
                "✅ 已發送生長燈定時指令 Topic={Topic}, Unit={Unit}, Minutes={Minutes}",
                Relay6CommandTopic,
                unitCount,
                unitCount * TimerUnitMinutes
            );
        }

        // 保留舊方法，舊 fan/on, fan/off 對應到生長燈
        public async Task PublishFanCommandAsync(
            bool on,
            CancellationToken cancellationToken = default)
        {
            await PublishRelayCommandAsync(6, on, cancellationToken);
        }

        // ================================
        // 液肥手動控制
        // Stepper
        // ================================
        public async Task PublishStepperCommandAsync(
            bool on,
            CancellationToken cancellationToken = default)
        {
            if (_mqttClient == null)
                throw new InvalidOperationException("MQTT Client 尚未初始化");

            if (!_mqttClient.IsConnected)
                await ConnectAsync(cancellationToken);

            string payload = on ? "ON" : "OFF";

            var message = new MqttApplicationMessageBuilder()
                .WithTopic(StepperCommandTopic)
                .WithPayload(payload)
                .WithRetainFlag(false)
                .Build();

            await _mqttClient.PublishAsync(message, cancellationToken);

            _latestData.Stepper = on;
            _latestData.Time = DateTime.Now;

            await _hubContext.Clients.Group("Dashboard")
                .SendAsync("ReceiveSensorData", _latestData, cancellationToken);

            _logger.LogInformation(
                "✅ 已發送液肥控制指令 Topic={Topic}, Payload={Payload}",
                StepperCommandTopic,
                payload
            );
        }

        // ================================
        // 液肥定時控制
        // Stepper
        //
        // 重要：
        // 這裡只送數字 1~6
        // 不再額外送 ON
        // ================================
        public async Task PublishStepperTimerCommandAsync(
            int unitCount,
            CancellationToken cancellationToken = default)
        {
            if (unitCount < 0)
                throw new ArgumentOutOfRangeException(nameof(unitCount), "液肥定時單位不能小於 0。");

            if (unitCount > 144)
                throw new ArgumentOutOfRangeException(nameof(unitCount), "液肥定時最多 144 單位，也就是 24 小時。");

            if (_mqttClient == null)
                throw new InvalidOperationException("MQTT Client 尚未初始化");

            if (!_mqttClient.IsConnected)
                await ConnectAsync(cancellationToken);

            if (unitCount == 0)
            {
                var offMessage = new MqttApplicationMessageBuilder()
                    .WithTopic(StepperCommandTopic)
                    .WithPayload("OFF")
                    .WithRetainFlag(false)
                    .Build();

                await _mqttClient.PublishAsync(offMessage, cancellationToken);

                _latestData.Stepper = false;
            }
            else
            {
                var timerMessage = new MqttApplicationMessageBuilder()
                    .WithTopic(StepperCommandTopic)
                    .WithPayload(unitCount.ToString())
                    .WithRetainFlag(false)
                    .Build();

                await _mqttClient.PublishAsync(timerMessage, cancellationToken);

                _latestData.Stepper = true;
            }

            _latestData.Time = DateTime.Now;

            await _hubContext.Clients.Group("Dashboard")
                .SendAsync("ReceiveSensorData", _latestData, cancellationToken);

            _logger.LogInformation(
                "✅ 已發送液肥定時指令 Topic={Topic}, Unit={Unit}, Minutes={Minutes}",
                StepperCommandTopic,
                unitCount,
                unitCount * TimerUnitMinutes
            );
        }

        // ================================
        // 異常判斷與通知
        // ================================
        private async Task CheckThresholdsAndNotifyAsync(SensorData data)
        {
            var alerts = new List<string>();

            if (data.Temp < 10.0 && data.Temp > 0)
                alerts.Add($"❄️ 溫度過低: {data.Temp:F1}°C (門檻 < 10)");

            if (string.Equals(data.SoilState, "DRY", StringComparison.OrdinalIgnoreCase))
                alerts.Add($"🏜️ 土壤乾燥: {data.Soil:F0} (門檻 > {DefaultSoilLimit})");

            if (data.Humidity < 40.0 && data.Humidity > 0)
                alerts.Add($"💧 環境濕度太低: {data.Humidity:F1}% (門檻 < 40)");

            _logger.LogInformation(
                "[ALERT] count={Count} => {Alerts}",
                alerts.Count,
                string.Join(" | ", alerts)
            );

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
                double minutesSince = (DateTime.Now - _lastAlertTime).TotalMinutes;

                if (minutesSince < AlertCooldownMinutes)
                {
                    _isInAlertState = true;
                    _lastAlertKey = alertKey;
                    return;
                }

                string snapshot =
                    $"📌 目前數值\n" +
                    $"Temp: {data.Temp:F1}°C\n" +
                    $"Humidity: {data.Humidity:F1}%\n" +
                    $"Soil: {data.Soil:F0}\n" +
                    $"SoilState: {data.SoilState}\n" +
                    $"滴灌: {OnOffText(data.Relay5)}\n" +
                    $"生長燈: {OnOffText(data.Relay6)}\n" +
                    $"液肥: {OnOffText(data.Stepper)}";

                string msg =
                    $"🚨【智慧農場警報】\n" +
                    $"⏰ {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n\n" +
                    string.Join("\n", alerts) +
                    "\n\n" +
                    snapshot;

                string alertTitle;
                string alertDetail;

                if (alerts.Any(x => x.Contains("土壤乾燥")))
                {
                    alertTitle = "土壤太乾";
                    alertDetail = "建議盡快澆水，或查看農場狀態";
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

                await _notificationService.SendLineAlertCardAsync(
                    alertTitle,
                    alertDetail,
                    true
                );

                await _notificationService.SendEmailAsync(
                    "農場異常通知",
                    msg.Replace("\n", "<br>")
                );

                _lastAlertTime = DateTime.Now;
                _isInAlertState = true;
                _lastAlertKey = alertKey;

                _logger.LogInformation("✅ 已發送異常警報通知。");

                return;
            }

            _isInAlertState = true;
            _lastAlertKey = alertKey;
        }

        // ================================
        // 儲存到資料庫
        // ================================
        private async Task SaveToDatabaseAsync(SensorData data)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.SensorLogs.Add(new SensorLog
            {
                Temp = data.Temp,
                Humidity = data.Humidity,
                Soil = data.Soil,
                SoilState = data.SoilState,

                Relay5 = data.Relay5,
                Relay6 = data.Relay6,
                Stepper = data.Stepper,

                CreatedAt = data.Time
            });

            await db.SaveChangesAsync();
        }

        // ================================
        // 停止服務
        // ================================
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _isStopping = true;

            if (_mqttClient != null && _mqttClient.IsConnected)
            {
                await _mqttClient.DisconnectAsync(
                    cancellationToken: cancellationToken
                );
            }
        }

        private static string GetSoilState(double soilValue)
        {
            if (soilValue > DefaultSoilLimit)
                return "DRY";

            if (soilValue < 900)
                return "WET";

            return "MOIST";
        }

        private static bool IsOn(string? value)
        {
            return string.Equals(value, "ON", StringComparison.OrdinalIgnoreCase)
                || value == "1"
                || string.Equals(value, "TRUE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "START", StringComparison.OrdinalIgnoreCase);
        }

        private static string OnOffText(bool value)
        {
            return value ? "ON" : "OFF";
        }
    }

    public class SensorDataModel
    {
        [JsonProperty("temperature")]
        public double Temperature { get; set; }

        [JsonProperty("humidity")]
        public double Humidity { get; set; }

        [JsonProperty("soil")]
        public double Soil { get; set; }

        [JsonProperty("relayD5")]
        public string RelayD5 { get; set; } = "OFF";

        [JsonProperty("relayD6")]
        public string RelayD6 { get; set; } = "OFF";

        [JsonProperty("stepper")]
        public string Stepper { get; set; } = "OFF";
    }
}