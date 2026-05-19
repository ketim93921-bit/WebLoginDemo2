using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using WebLoginDemo2.Services;

namespace WebLoginDemo2.Controllers;

[ApiController]
[Route("api/line/webhook")]
public class LineWebhookController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly LineBotService _line;
    private readonly MqttService _mqtt;
    private readonly ILogger<LineWebhookController> _logger;

    // 先用記憶體保存，正式版可改存 DB
    public static string? BoundUserId { get; private set; }

    public LineWebhookController(
        IConfiguration config,
        LineBotService line,
        MqttService mqtt,
        ILogger<LineWebhookController> logger)
    {
        _config = config;
        _line = line;
        _mqtt = mqtt;
        _logger = logger;
    }

    // =====================================================
    // LINE Webhook
    // URL: /api/line/webhook
    // =====================================================
    [HttpPost]
    public async Task<IActionResult> Post()
    {
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync();

        _logger.LogInformation("[LINE] Raw body = {Body}", body);

        var secret = _config["Line:ChannelSecret"] ?? "";
        var sig = Request.Headers["X-Line-Signature"].ToString();

        if (!ValidateSignature(secret, body, sig))
        {
            _logger.LogWarning("[LINE] Invalid signature");
            return Unauthorized("Invalid signature");
        }

        using var doc = JsonDocument.Parse(body);

        if (!doc.RootElement.TryGetProperty("events", out var events) ||
            events.GetArrayLength() == 0)
        {
            return Ok();
        }

        foreach (var ev in events.EnumerateArray())
        {
            var replyToken = ev.TryGetProperty("replyToken", out var rt)
                ? rt.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(replyToken))
                continue;

            if (ev.TryGetProperty("source", out var src) &&
                src.TryGetProperty("userId", out var uid))
            {
                BoundUserId = uid.GetString();
                _logger.LogInformation("[LINE] Bound userId = {UserId}", BoundUserId);
            }

            var eventType = ev.TryGetProperty("type", out var tp)
                ? tp.GetString()
                : "";

            if (eventType == "message")
            {
                await HandleMessageEventAsync(replyToken, ev);
            }
            else if (eventType == "postback")
            {
                await HandlePostbackEventAsync(replyToken, ev);
            }
        }

        return Ok();
    }

    // =====================================================
    // Message Event
    // =====================================================
    private async Task HandleMessageEventAsync(string replyToken, JsonElement ev)
    {
        string? text = null;

        if (ev.TryGetProperty("message", out var msg) &&
            msg.TryGetProperty("type", out var msgType) &&
            msgType.GetString() == "text" &&
            msg.TryGetProperty("text", out var t))
        {
            text = t.GetString()?.Trim();
        }

        _logger.LogInformation("[LINE] Message text = {Text}", text);

        switch (text)
        {
            case "menu":
            case "選單":
            case "控制":
                await _line.ReplyMessagesAsync(replyToken, BuildMainMenuReply());
                break;

            case "設備控制":
                await _line.ReplyMessagesAsync(replyToken, BuildControlMenuReply());
                break;

            case "狀態":
            case "農場狀態":
            case "即時資訊":
                await _line.ReplyMessagesAsync(replyToken, BuildStatusReply());
                break;

            case "生長燈":
            case "生長燈控制":
            case "D6":
            case "D6控制":
            case "Relay6":
            case "Relay6控制":
                await _line.ReplyMessagesAsync(replyToken, BuildGrowLightControlReply());
                break;

            case "液肥":
            case "液肥控制":
            case "馬達":
            case "馬達控制":
            case "步進馬達":
            case "步進馬達控制":
                await _line.ReplyMessagesAsync(replyToken, BuildFertilizerControlReply());
                break;

            case "生長燈開":
            case "D6開":
            case "Relay6開":
                await ExecuteRelay6Async(replyToken, true);
                break;

            case "生長燈關":
            case "D6關":
            case "Relay6關":
                await ExecuteRelay6Async(replyToken, false);
                break;

            case "生長燈定時10":
            case "D6定時10":
            case "Relay6定時10":
                await ExecuteRelay6TimerAsync(replyToken, 1, 10);
                break;

            case "生長燈定時20":
            case "D6定時20":
            case "Relay6定時20":
                await ExecuteRelay6TimerAsync(replyToken, 2, 20);
                break;

            case "生長燈定時30":
            case "D6定時30":
            case "Relay6定時30":
                await ExecuteRelay6TimerAsync(replyToken, 3, 30);
                break;

            case "生長燈定時40":
            case "D6定時40":
            case "Relay6定時40":
                await ExecuteRelay6TimerAsync(replyToken, 4, 40);
                break;

            case "生長燈定時50":
            case "D6定時50":
            case "Relay6定時50":
                await ExecuteRelay6TimerAsync(replyToken, 5, 50);
                break;

            case "生長燈定時60":
            case "D6定時60":
            case "Relay6定時60":
                await ExecuteRelay6TimerAsync(replyToken, 6, 60);
                break;

            case "生長燈取消定時":
            case "D6取消定時":
            case "Relay6取消定時":
                await ExecuteRelay6TimerCancelAsync(replyToken);
                break;

            case "液肥開":
            case "馬達開":
            case "步進馬達開":
                await ExecuteStepperAsync(replyToken, true);
                break;

            case "液肥關":
            case "馬達關":
            case "步進馬達關":
                await ExecuteStepperAsync(replyToken, false);
                break;

            case "液肥定時10":
            case "馬達定時10":
            case "步進馬達定時10":
                await ExecuteStepperTimerAsync(replyToken, 1, 10);
                break;

            case "液肥定時20":
            case "馬達定時20":
            case "步進馬達定時20":
                await ExecuteStepperTimerAsync(replyToken, 2, 20);
                break;

            case "液肥定時30":
            case "馬達定時30":
            case "步進馬達定時30":
                await ExecuteStepperTimerAsync(replyToken, 3, 30);
                break;

            case "液肥定時40":
            case "馬達定時40":
            case "步進馬達定時40":
                await ExecuteStepperTimerAsync(replyToken, 4, 40);
                break;

            case "液肥定時50":
            case "馬達定時50":
            case "步進馬達定時50":
                await ExecuteStepperTimerAsync(replyToken, 5, 50);
                break;

            case "液肥定時60":
            case "馬達定時60":
            case "步進馬達定時60":
                await ExecuteStepperTimerAsync(replyToken, 6, 60);
                break;

            case "液肥取消定時":
            case "馬達取消定時":
            case "步進馬達取消定時":
                await ExecuteStepperTimerCancelAsync(replyToken);
                break;

            case "綁定":
                await _line.ReplyTextAsync(replyToken, $"✅ 綁定成功！你的 userId：{BoundUserId}");
                break;

            default:
                await _line.ReplyTextAsync(replyToken, "請輸入：menu、設備控制、農場狀態");
                break;
        }
    }

    // =====================================================
    // Postback Event
    // =====================================================
    private async Task HandlePostbackEventAsync(string replyToken, JsonElement ev)
    {
        string? data = null;

        if (ev.TryGetProperty("postback", out var postback) &&
            postback.TryGetProperty("data", out var d))
        {
            data = d.GetString();
        }

        _logger.LogInformation("[LINE] Postback data = {Data}", data);

        switch (data)
        {
            case "action=status":
                await _line.ReplyMessagesAsync(replyToken, BuildStatusReply());
                break;

            case "action=control_menu":
                await _line.ReplyMessagesAsync(replyToken, BuildControlMenuReply());
                break;

            case "action=grow_light_menu":
            case "action=relay6_menu":
                await _line.ReplyMessagesAsync(replyToken, BuildGrowLightControlReply());
                break;

            case "action=fertilizer_menu":
            case "action=stepper_menu":
                await _line.ReplyMessagesAsync(replyToken, BuildFertilizerControlReply());
                break;

            case "action=relay6_on":
                await ExecuteRelay6Async(replyToken, true);
                break;

            case "action=relay6_off":
                await ExecuteRelay6Async(replyToken, false);
                break;

            case "action=relay6_timer_1":
                await ExecuteRelay6TimerAsync(replyToken, 1, 10);
                break;

            case "action=relay6_timer_2":
                await ExecuteRelay6TimerAsync(replyToken, 2, 20);
                break;

            case "action=relay6_timer_3":
                await ExecuteRelay6TimerAsync(replyToken, 3, 30);
                break;

            case "action=relay6_timer_4":
                await ExecuteRelay6TimerAsync(replyToken, 4, 40);
                break;

            case "action=relay6_timer_5":
                await ExecuteRelay6TimerAsync(replyToken, 5, 50);
                break;

            case "action=relay6_timer_6":
                await ExecuteRelay6TimerAsync(replyToken, 6, 60);
                break;

            case "action=relay6_timer_cancel":
            case "action=relay6_cancel":
                await ExecuteRelay6TimerCancelAsync(replyToken);
                break;

            case "action=stepper_on":
                await ExecuteStepperAsync(replyToken, true);
                break;

            case "action=stepper_off":
                await ExecuteStepperAsync(replyToken, false);
                break;

            case "action=stepper_timer_1":
                await ExecuteStepperTimerAsync(replyToken, 1, 10);
                break;

            case "action=stepper_timer_2":
                await ExecuteStepperTimerAsync(replyToken, 2, 20);
                break;

            case "action=stepper_timer_3":
                await ExecuteStepperTimerAsync(replyToken, 3, 30);
                break;

            case "action=stepper_timer_4":
                await ExecuteStepperTimerAsync(replyToken, 4, 40);
                break;

            case "action=stepper_timer_5":
                await ExecuteStepperTimerAsync(replyToken, 5, 50);
                break;

            case "action=stepper_timer_6":
                await ExecuteStepperTimerAsync(replyToken, 6, 60);
                break;

            case "action=stepper_timer_cancel":
            case "action=stepper_cancel":
                await ExecuteStepperTimerCancelAsync(replyToken);
                break;

            case "action=bind":
                await _line.ReplyTextAsync(replyToken, $"✅ 綁定成功！你的 userId：{BoundUserId}");
                break;

            case "action=menu":
            default:
                await _line.ReplyMessagesAsync(replyToken, BuildMainMenuReply());
                break;
        }
    }

    // =====================================================
    // 執行控制
    // =====================================================
    private async Task ExecuteRelay6Async(string replyToken, bool on)
    {
        try
        {
            await _mqtt.PublishRelayCommandAsync(6, on);

            await _line.ReplyTextAsync(
                replyToken,
                on ? "💡 生長燈已開啟" : "🛑 生長燈已關閉"
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LINE] 生長燈控制失敗");

            await _line.ReplyTextAsync(
                replyToken,
                "❌ 生長燈控制失敗，請檢查 MQTT 或設備連線"
            );
        }
    }

    private async Task ExecuteRelay6TimerAsync(string replyToken, int unit, int minutes)
    {
        try
        {
            await _mqtt.PublishRelay6TimerCommandAsync(unit);

            await _line.ReplyTextAsync(
                replyToken,
                $"⏰ 生長燈已啟動定時 {minutes} 分鐘"
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LINE] 生長燈定時失敗");

            await _line.ReplyTextAsync(
                replyToken,
                "❌ 生長燈定時失敗，請檢查 MQTT 或設備連線"
            );
        }
    }

    private async Task ExecuteRelay6TimerCancelAsync(string replyToken)
    {
        try
        {
            await _mqtt.PublishRelay6TimerCommandAsync(0);

            await _line.ReplyTextAsync(
                replyToken,
                "🛑 生長燈定時已取消並關閉"
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LINE] 生長燈取消定時失敗");

            await _line.ReplyTextAsync(
                replyToken,
                "❌ 生長燈取消定時失敗，請檢查 MQTT 或設備連線"
            );
        }
    }

    private async Task ExecuteStepperAsync(string replyToken, bool on)
    {
        try
        {
            await _mqtt.PublishStepperCommandAsync(on);

            await _line.ReplyTextAsync(
                replyToken,
                on ? "🧪 液肥設備已啟動" : "🛑 液肥設備已停止"
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LINE] 液肥控制失敗");

            await _line.ReplyTextAsync(
                replyToken,
                "❌ 液肥控制失敗，請檢查 MQTT 或設備連線"
            );
        }
    }

    private async Task ExecuteStepperTimerAsync(string replyToken, int unit, int minutes)
    {
        try
        {
            await _mqtt.PublishStepperTimerCommandAsync(unit);

            await _line.ReplyTextAsync(
                replyToken,
                $"⏰ 液肥已啟動定時 {minutes} 分鐘"
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LINE] 液肥定時失敗");

            await _line.ReplyTextAsync(
                replyToken,
                "❌ 液肥定時失敗，請檢查 MQTT 或設備連線"
            );
        }
    }

    private async Task ExecuteStepperTimerCancelAsync(string replyToken)
    {
        try
        {
            await _mqtt.PublishStepperTimerCommandAsync(0);

            await _line.ReplyTextAsync(
                replyToken,
                "🛑 液肥定時已取消並關閉"
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LINE] 液肥取消定時失敗");

            await _line.ReplyTextAsync(
                replyToken,
                "❌ 液肥取消定時失敗，請檢查 MQTT 或設備連線"
            );
        }
    }

    // =====================================================
    // LINE Flex Message - 使用手寫 JSON object
    // =====================================================
    private IEnumerable<object> BuildMainMenuReply()
    {
        var dashboardUrl = _config["App:DashboardUrl"] ?? "https://你的公開網址/Dashboard";

        return new object[]
        {
            new
            {
                type = "flex",
                altText = "智慧農場主選單",
                contents = new
                {
                    type = "bubble",
                    size = "mega",
                    body = new
                    {
                        type = "box",
                        layout = "vertical",
                        spacing = "lg",
                        contents = new object[]
                        {
                            new {
                                type = "text",
                                text = "🌱 智慧農場",
                                weight = "bold",
                                size = "4xl",
                                align = "center"
                            },
                            new {
                                type = "text",
                                text = "請選擇功能",
                                size = "xl",
                                align = "center",
                                color = "#666666"
                            }
                        }
                    },
                    footer = new
                    {
                        type = "box",
                        layout = "vertical",
                        spacing = "md",
                        contents = new object[]
                        {
                            BuildPostbackButton("農場狀態", "action=status", "農場狀態", "#16A34A"),
                            BuildPostbackButton("設備控制", "action=control_menu", "設備控制", "#2563EB"),
                            new {
                                type = "button",
                                style = "link",
                                height = "md",
                                action = new {
                                    type = "uri",
                                    label = "監控畫面",
                                    uri = dashboardUrl
                                }
                            }
                        }
                    }
                }
            }
        };
    }

    private IEnumerable<object> BuildStatusReply()
    {
        var latest = _mqtt.GetLatestSensorData();
        string onlineText = _mqtt.IsMqttConnected ? "🟢 正常" : "🔴 離線";

        return new object[]
        {
            new
            {
                type = "flex",
                altText = "智慧農場現在狀態",
                contents = new
                {
                    type = "bubble",
                    size = "mega",
                    body = new
                    {
                        type = "box",
                        layout = "vertical",
                        spacing = "lg",
                        contents = new object[]
                        {
                            new {
                                type = "text",
                                text = "📊 農場狀態",
                                weight = "bold",
                                size = "3xl",
                                align = "center"
                            },
                            BuildLargeInfoRow("📡 設備連線", onlineText),
                            BuildLargeInfoRow("🌡 溫度", $"{latest.Temp:F1}°C"),
                            BuildLargeInfoRow("💧 濕度", $"{latest.Humidity:F1}%"),
                            BuildLargeInfoRow("🌱 土壤數值", $"{latest.Soil:F0}"),
                            BuildLargeInfoRow("🌿 土壤狀態", TranslateSoilState(latest.SoilState)),
                            BuildLargeInfoRow("🚿 滴灌", OnOffText(latest.Relay5)),
                            BuildLargeInfoRow("💡 生長燈", OnOffText(latest.Relay6)),
                            BuildLargeInfoRow("🧪 液肥", OnOffText(latest.Stepper)),
                            BuildLargeInfoRow("🕒 更新時間", $"{latest.Time:HH:mm:ss}")
                        }
                    },
                    footer = new
                    {
                        type = "box",
                        layout = "vertical",
                        spacing = "md",
                        contents = new object[]
                        {
                            BuildPostbackButton("重新整理", "action=status", "農場狀態", "#16A34A"),
                            BuildPostbackButton("設備控制", "action=control_menu", "設備控制", "#2563EB")
                        }
                    }
                }
            }
        };
    }

    private IEnumerable<object> BuildControlMenuReply()
    {
        return new object[]
        {
            new
            {
                type = "flex",
                altText = "設備控制",
                contents = new
                {
                    type = "bubble",
                    size = "mega",
                    body = new
                    {
                        type = "box",
                        layout = "vertical",
                        spacing = "lg",
                        contents = new object[]
                        {
                            new {
                                type = "text",
                                text = "🎛 設備控制",
                                weight = "bold",
                                size = "3xl",
                                align = "center"
                            },
                            new {
                                type = "text",
                                text = "請選擇設備",
                                size = "xl",
                                align = "center",
                                color = "#666666"
                            }
                        }
                    },
                    footer = new
                    {
                        type = "box",
                        layout = "vertical",
                        spacing = "md",
                        contents = new object[]
                        {
                            BuildPostbackButton("生長燈控制", "action=grow_light_menu", "生長燈控制", "#6366F1"),
                            BuildPostbackButton("液肥控制", "action=fertilizer_menu", "液肥控制", "#16A34A"),
                            BuildPostbackButton("農場狀態", "action=status", "農場狀態", "#2563EB")
                        }
                    }
                }
            }
        };
    }

    private IEnumerable<object> BuildGrowLightControlReply()
    {
        return new object[]
        {
            new
            {
                type = "flex",
                altText = "生長燈控制",
                contents = new
                {
                    type = "bubble",
                    size = "mega",
                    body = new
                    {
                        type = "box",
                        layout = "vertical",
                        spacing = "lg",
                        contents = new object[]
                        {
                            new {
                                type = "text",
                                text = "💡 生長燈控制",
                                weight = "bold",
                                size = "3xl",
                                align = "center",
                                wrap = true
                            },
                            new {
                                type = "text",
                                text = "1 單位 = 10 分鐘",
                                size = "lg",
                                align = "center",
                                color = "#666666"
                            }
                        }
                    },
                    footer = new
                    {
                        type = "box",
                        layout = "vertical",
                        spacing = "md",
                        contents = new object[]
                        {
                            BuildPostbackButton("立即開啟", "action=relay6_on", "生長燈開", "#16A34A"),
                            BuildPostbackButton("立即關閉", "action=relay6_off", "生長燈關", "#DC2626"),
                            BuildPostbackButton("定時 10 分鐘", "action=relay6_timer_1", "生長燈定時10", "#2563EB"),
                            BuildPostbackButton("定時 20 分鐘", "action=relay6_timer_2", "生長燈定時20", "#2563EB"),
                            BuildPostbackButton("定時 30 分鐘", "action=relay6_timer_3", "生長燈定時30", "#2563EB"),
                            BuildPostbackButton("定時 40 分鐘", "action=relay6_timer_4", "生長燈定時40", "#2563EB"),
                            BuildPostbackButton("定時 50 分鐘", "action=relay6_timer_5", "生長燈定時50", "#2563EB"),
                            BuildPostbackButton("定時 60 分鐘", "action=relay6_timer_6", "生長燈定時60", "#2563EB"),
                            BuildPostbackButton("取消定時並關閉", "action=relay6_timer_cancel", "生長燈取消定時", "#6B7280"),
                            BuildPostbackButton("回設備控制", "action=control_menu", "設備控制", "#6B7280")
                        }
                    }
                }
            }
        };
    }

    private IEnumerable<object> BuildFertilizerControlReply()
    {
        return new object[]
        {
            new
            {
                type = "flex",
                altText = "液肥控制",
                contents = new
                {
                    type = "bubble",
                    size = "mega",
                    body = new
                    {
                        type = "box",
                        layout = "vertical",
                        spacing = "lg",
                        contents = new object[]
                        {
                            new {
                                type = "text",
                                text = "🧪 液肥控制",
                                weight = "bold",
                                size = "3xl",
                                align = "center",
                                wrap = true
                            },
                            new {
                                type = "text",
                                text = "1 單位 = 10 分鐘",
                                size = "lg",
                                align = "center",
                                color = "#666666"
                            }
                        }
                    },
                    footer = new
                    {
                        type = "box",
                        layout = "vertical",
                        spacing = "md",
                        contents = new object[]
                        {
                            BuildPostbackButton("立即啟動", "action=stepper_on", "液肥開", "#16A34A"),
                            BuildPostbackButton("立即停止", "action=stepper_off", "液肥關", "#DC2626"),
                            BuildPostbackButton("定時 10 分鐘", "action=stepper_timer_1", "液肥定時10", "#2563EB"),
                            BuildPostbackButton("定時 20 分鐘", "action=stepper_timer_2", "液肥定時20", "#2563EB"),
                            BuildPostbackButton("定時 30 分鐘", "action=stepper_timer_3", "液肥定時30", "#2563EB"),
                            BuildPostbackButton("定時 40 分鐘", "action=stepper_timer_4", "液肥定时40", "#2563EB"),
                            BuildPostbackButton("定時 50 分鐘", "action=stepper_timer_5", "液肥定時50", "#2563EB"),
                            BuildPostbackButton("定時 60 分鐘", "action=stepper_timer_6", "液肥定時60", "#2563EB"),
                            BuildPostbackButton("取消定時並關閉", "action=stepper_timer_cancel", "液肥取消定時", "#6B7280"),
                            BuildPostbackButton("回設備控制", "action=control_menu", "設備控制", "#6B7280")
                        }
                    }
                }
            }
        };
    }

    private static object BuildPostbackButton(
        string label,
        string data,
        string displayText,
        string? color = null)
    {
        return new
        {
            type = "button",
            style = "primary",
            height = "md",
            color = color,
            action = new
            {
                type = "postback",
                label,
                data,
                displayText
            }
        };
    }

    private static object BuildLargeInfoRow(string label, string value)
    {
        return new
        {
            type = "box",
            layout = "horizontal",
            contents = new object[]
            {
                new {
                    type = "text",
                    text = label,
                    size = "xl",
                    color = "#555555",
                    flex = 3
                },
                new {
                    type = "text",
                    text = value,
                    size = "xl",
                    weight = "bold",
                    align = "end",
                    flex = 4,
                    wrap = true
                }
            }
        };
    }

    private static string OnOffText(bool value)
    {
        return value ? "🟢 運作中" : "⚫ 已停止";
    }

    private static string TranslateSoilState(string? soilState)
    {
        return soilState?.ToUpperInvariant() switch
        {
            "DRY" => "⚠️ 乾燥",
            "MOIST" => "✅ 適中",
            "WET" => "💧 濕潤",
            _ => string.IsNullOrWhiteSpace(soilState) ? "未知" : soilState
        };
    }

    private static bool ValidateSignature(
        string channelSecret,
        string body,
        string xLineSignature)
    {
        if (string.IsNullOrWhiteSpace(channelSecret) ||
            string.IsNullOrWhiteSpace(xLineSignature))
        {
            return false;
        }

        using var hmac = new HMACSHA256(
            Encoding.UTF8.GetBytes(channelSecret)
        );

        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
        var expected = Convert.ToBase64String(hash);

        var a = Encoding.UTF8.GetBytes(expected);
        var b = Encoding.UTF8.GetBytes(xLineSignature);

        return a.Length == b.Length &&
               CryptographicOperations.FixedTimeEquals(a, b);
    }
}