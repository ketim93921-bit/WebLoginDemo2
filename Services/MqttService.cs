using MQTTnet;
using MQTTnet.Client;
using Microsoft.AspNetCore.SignalR;
using Newtonsoft.Json;
using System.Text;
using WebLoginDemo2.Hubs;
using WebLoginDemo2.Data;
using WebLoginDemo2.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace WebLoginDemo2.Services
{
    public class MqttService : IHostedService
    {
        // ================================
        // MQTT Topic 設定
        // ================================

        // ESP8266 新版感測資料 JSON Topic
        private const string MqttTopic = "greenhouse/status/json";

        // Relay / Stepper 控制 Topic
        private const string Relay1CommandTopic = "greenhouse/set/relay1";
        private const string Relay2CommandTopic = "greenhouse/set/relay2";
        private const string Relay3CommandTopic = "greenhouse/set/relay3";
        private const string Relay4CommandTopic = "greenhouse/set/relay4";
        private const string Relay5CommandTopic = "greenhouse/set/relay5";
        private const string Relay6CommandTopic = "greenhouse/set/relay6";
        private const string StepperCommandTopic = "greenhouse/set/stepper";

        private const string MqttServerIp = "broker.hivemq.com";
        private const int MqttServerPort = 1883;

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

                TempLimit = _latestData.TempLimit,
                SoilLimit = _latestData.SoilLimit,

                TempAuto = _latestData.TempAuto,
                SoilAuto = _latestData.SoilAuto,

                Relay1 = _latestData.Relay1,
                Relay2 = _latestData.Relay2,
                Relay3 = _latestData.Relay3,
                Relay4 = _latestData.Relay4,
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
                $"溫度門檻：{data.TempLimit:F1}°C\n" +
                $"土壤門檻：{data.SoilLimit}\n" +
                $"溫控自動：{OnOffText(data.TempAuto)}\n" +
                $"土壤自動：{OnOffText(data.SoilAuto)}\n" +
                $"Relay1：{OnOffText(data.Relay1)}\n" +
                $"Relay2：{OnOffText(data.Relay2)}\n" +
                $"Relay3：{OnOffText(data.Relay3)}\n" +
                $"Relay4：{OnOffText(data.Relay4)}\n" +
                $"Relay5：{OnOffText(data.Relay5)}\n" +
                $"Relay6：{OnOffText(data.Relay6)}\n" +
                $"步進馬達：{OnOffText(data.Stepper)}\n" +
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

                    Temp = raw.Temp,
                    Humidity = raw.Humidity,
                    Soil = raw.Soil,
                    SoilState = raw.SoilState,

                    TempLimit = raw.TempLimit,
                    SoilLimit = raw.SoilLimit,

                    TempAuto = IsOn(raw.TempAuto),
                    SoilAuto = IsOn(raw.SoilAuto),

                    Relay1 = IsOn(raw.Relay1),
                    Relay2 = IsOn(raw.Relay2),
                    Relay3 = IsOn(raw.Relay3),
                    Relay4 = IsOn(raw.Relay4),
                    Relay5 = IsOn(raw.Relay5),
                    Relay6 = IsOn(raw.Relay6),

                    Stepper = IsOn(raw.Stepper)
                };

                _latestData = data;

                _logger.LogInformation(
                    "[DATA] Temp={Temp:F1}, Humidity={Humidity:F1}, Soil={Soil:F0}, SoilState={SoilState}, Relay1={Relay1}, Relay2={Relay2}, Relay3={Relay3}, Relay4={Relay4}, Relay5={Relay5}, Relay6={Relay6}, Stepper={Stepper}",
                    data.Temp,
                    data.Humidity,
                    data.Soil,
                    data.SoilState,
                    OnOffText(data.Relay1),
                    OnOffText(data.Relay2),
                    OnOffText(data.Relay3),
                    OnOffText(data.Relay4),
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
        // Relay 控制
        // ================================
        public async Task PublishRelayCommandAsync(
            int relayNumber,
            bool on,
            CancellationToken cancellationToken = default)
        {
            if (relayNumber < 1 || relayNumber > 6)
                throw new ArgumentOutOfRangeException(nameof(relayNumber), "Relay 編號必須是 1 到 6。");

            if (_mqttClient == null)
                throw new InvalidOperationException("MQTT Client 尚未初始化");

            if (!_mqttClient.IsConnected)
                await ConnectAsync(cancellationToken);

            string topic = relayNumber switch
            {
                1 => Relay1CommandTopic,
                2 => Relay2CommandTopic,
                3 => Relay3CommandTopic,
                4 => Relay4CommandTopic,
                5 => Relay5CommandTopic,
                6 => Relay6CommandTopic,
                _ => throw new ArgumentOutOfRangeException(nameof(relayNumber))
            };

            string payload = on ? "ON" : "OFF";

            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithRetainFlag(true)
                .Build();

            await _mqttClient.PublishAsync(message, cancellationToken);

            switch (relayNumber)
            {
                case 1:
                    _latestData.Relay1 = on;
                    break;
                case 2:
                    _latestData.Relay2 = on;
                    break;
                case 3:
                    _latestData.Relay3 = on;
                    break;
                case 4:
                    _latestData.Relay4 = on;
                    break;
                case 5:
                    _latestData.Relay5 = on;
                    break;
                case 6:
                    _latestData.Relay6 = on;
                    break;
            }

            _latestData.Time = DateTime.Now;

            await _hubContext.Clients.Group("Dashboard")
                .SendAsync("ReceiveSensorData", _latestData, cancellationToken);

            _logger.LogInformation(
                "✅ 已發送 Relay{RelayNumber} 控制指令 Topic={Topic}, Payload={Payload}",
                relayNumber,
                topic,
                payload
            );
        }

        // 保留舊方法，避免 ControlController / LineWebhookController 還沒改時編譯錯誤
        // 這裡先把「風扇」對應成 Relay1
        public async Task PublishFanCommandAsync(
            bool on,
            CancellationToken cancellationToken = default)
        {
            await PublishRelayCommandAsync(1, on, cancellationToken);
        }

        // ================================
        // Stepper 控制
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
                .WithRetainFlag(true)
                .Build();

            await _mqttClient.PublishAsync(message, cancellationToken);

            _latestData.Stepper = on;
            _latestData.Time = DateTime.Now;

            await _hubContext.Clients.Group("Dashboard")
                .SendAsync("ReceiveSensorData", _latestData, cancellationToken);

            _logger.LogInformation(
                "✅ 已發送步進馬達控制指令 Topic={Topic}, Payload={Payload}",
                StepperCommandTopic,
                payload
            );
        }

        // ================================
        // 異常判斷與通知
        // ================================
        private async Task CheckThresholdsAndNotifyAsync(SensorData data)
        {
            var alerts = new List<string>();

            if (data.Temp > data.TempLimit && data.TempLimit > 0)
                alerts.Add($"🌡️ 溫度過高: {data.Temp:F1}°C (門檻 > {data.TempLimit:F1})");

            if (data.Temp < 10.0 && data.Temp > 0)
                alerts.Add($"❄️ 溫度過低: {data.Temp:F1}°C (門檻 < 10)");

            if (string.Equals(data.SoilState, "DRY", StringComparison.OrdinalIgnoreCase))
                alerts.Add($"🏜️ 土壤乾燥: {data.Soil:F0} (門檻 > {data.SoilLimit})");

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

                _logger.LogInformation(
                    "[ALERT] lastAlert={LastAlert:HH:mm:ss}, minutesSince={MinutesSince:F1}, cooldown={Cooldown}",
                    _lastAlertTime,
                    minutesSince,
                    AlertCooldownMinutes
                );

                if (minutesSince < AlertCooldownMinutes)
                {
                    _logger.LogInformation("⏳ 冷卻時間內，跳過通知。");

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
                    $"TempLimit: {data.TempLimit:F1}°C\n" +
                    $"SoilLimit: {data.SoilLimit}\n" +
                    $"Relay1: {OnOffText(data.Relay1)}\n" +
                    $"Relay2: {OnOffText(data.Relay2)}\n" +
                    $"Relay3: {OnOffText(data.Relay3)}\n" +
                    $"Relay4: {OnOffText(data.Relay4)}\n" +
                    $"Relay5: {OnOffText(data.Relay5)}\n" +
                    $"Relay6: {OnOffText(data.Relay6)}\n" +
                    $"Stepper: {OnOffText(data.Stepper)}";

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
                else if (alerts.Any(x => x.Contains("溫度過高")))
                {
                    alertTitle = "溫度過高";
                    alertDetail = $"目前溫度 {data.Temp:F1}°C，建議先通風或啟動降溫設備";
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

            _logger.LogInformation("⚠️ 異常持續中（未變更），不重複通知。");

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

                TempLimit = data.TempLimit,
                SoilLimit = data.SoilLimit,

                TempAuto = data.TempAuto,
                SoilAuto = data.SoilAuto,

                Relay1 = data.Relay1,
                Relay2 = data.Relay2,
                Relay3 = data.Relay3,
                Relay4 = data.Relay4,
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

    // ================================
    // ESP8266 JSON 對應模型
    // ================================
    public class SensorDataModel
    {
        [JsonProperty("Temp")]
        public double Temp { get; set; }

        [JsonProperty("Humidity")]
        public double Humidity { get; set; }

        [JsonProperty("Soil")]
        public double Soil { get; set; }

        [JsonProperty("SoilState")]
        public string SoilState { get; set; } = string.Empty;

        [JsonProperty("TempLimit")]
        public double TempLimit { get; set; }

        [JsonProperty("SoilLimit")]
        public int SoilLimit { get; set; }

        [JsonProperty("TempAuto")]
        public string TempAuto { get; set; } = "OFF";

        [JsonProperty("SoilAuto")]
        public string SoilAuto { get; set; } = "OFF";

        [JsonProperty("Relay1")]
        public string Relay1 { get; set; } = "OFF";

        [JsonProperty("Relay2")]
        public string Relay2 { get; set; } = "OFF";

        [JsonProperty("Relay3")]
        public string Relay3 { get; set; } = "OFF";

        [JsonProperty("Relay4")]
        public string Relay4 { get; set; } = "OFF";

        [JsonProperty("Relay5")]
        public string Relay5 { get; set; } = "OFF";

        [JsonProperty("Relay6")]
        public string Relay6 { get; set; } = "OFF";

        [JsonProperty("Stepper")]
        public string Stepper { get; set; } = "OFF";
    }
}