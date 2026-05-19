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

    [HttpPost]
    public async Task<IActionResult> Post()
    {
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync();

        var secret = _config["Line:ChannelSecret"] ?? "";
        var sig = Request.Headers["X-Line-Signature"].ToString();

        if (!ValidateSignature(secret, body, sig))
            return Unauthorized("Invalid signature");

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

            var eventType = ev.TryGetProperty("type", out var tp)
                ? tp.GetString()
                : "";

            if (ev.TryGetProperty("source", out var src) &&
                src.TryGetProperty("userId", out var uid))
            {
                BoundUserId = uid.GetString();
                _logger.LogInformation("[LINE] Bound userId = {UserId}", BoundUserId);
            }

            if (eventType == "message")
            {
                string? text = null;

                if (ev.TryGetProperty("message", out var msg) &&
                    msg.TryGetProperty("type", out var msgType) &&
                    msgType.GetString() == "text" &&
                    msg.TryGetProperty("text", out var t))
                {
                    text = t.GetString()?.Trim();
                }

                _logger.LogInformation("[LINE] text = {Text}", text);
                await HandleTextCommandAsync(replyToken, text);
            }
            else if (eventType == "postback")
            {
                string? data = null;

                if (ev.TryGetProperty("postback", out var postback) &&
                    postback.TryGetProperty("data", out var d))
                {
                    data = d.GetString();
                }

                _logger.LogInformation("[LINE] postback = {Data}", data);
                await HandlePostbackAsync(replyToken, data);
            }
        }

        return Ok();
    }

    private async Task HandleTextCommandAsync(string replyToken, string? text)
    {
        switch (text)
        {
            case "狀態":
            case "農場狀態":
            case "即時資訊":
                await _line.ReplyMessagesAsync(replyToken, BuildStatusReply());
                break;

            case "控制":
            case "設備控制":
                await _line.ReplyMessagesAsync(replyToken, BuildControlMenuReply());
                break;

            case "Relay6":
            case "D6":
            case "Relay6控制":
            case "D6控制":
                await _line.ReplyMessagesAsync(replyToken, BuildRelay6ControlReply());
                break;

            case "Relay6開":
            case "D6開":
                await _mqtt.PublishRelayCommandAsync(6, true);
                await _line.ReplyMessagesAsync(replyToken, BuildControlResultReply("Relay6 / D6", true));
                break;

            case "Relay6關":
            case "D6關":
                await _mqtt.PublishRelayCommandAsync(6, false);
                await _line.ReplyMessagesAsync(replyToken, BuildControlResultReply("Relay6 / D6", false));
                break;

            case "Relay6定時10":
            case "D6定時10":
                await _mqtt.PublishRelay6TimerCommandAsync(1);
                await _line.ReplyMessagesAsync(replyToken, BuildTimerResultReply("Relay6 / D6", 10));
                break;

            case "Relay6定時20":
            case "D6定時20":
                await _mqtt.PublishRelay6TimerCommandAsync(2);
                await _line.ReplyMessagesAsync(replyToken, BuildTimerResultReply("Relay6 / D6", 20));
                break;

            case "Relay6定時30":
            case "D6定時30":
                await _mqtt.PublishRelay6TimerCommandAsync(3);
                await _line.ReplyMessagesAsync(replyToken, BuildTimerResultReply("Relay6 / D6", 30));
                break;

            case "Relay6取消定時":
            case "D6取消定時":
                await _mqtt.PublishRelay6TimerCommandAsync(0);
                await _line.ReplyMessagesAsync(replyToken, BuildControlResultReply("Relay6 / D6", false));
                break;

            case "步進馬達":
            case "馬達":
            case "步進馬達控制":
                await _line.ReplyMessagesAsync(replyToken, BuildStepperControlReply());
                break;

            case "步進馬達開":
            case "馬達開":
                await _mqtt.PublishStepperCommandAsync(true);
                await _line.ReplyMessagesAsync(replyToken, BuildControlResultReply("步進馬達", true));
                break;

            case "步進馬達關":
            case "馬達關":
                await _mqtt.PublishStepperCommandAsync(false);
                await _line.ReplyMessagesAsync(replyToken, BuildControlResultReply("步進馬達", false));
                break;

            case "步進馬達定時10":
            case "馬達定時10":
                await _mqtt.PublishStepperTimerCommandAsync(1);
                await _line.ReplyMessagesAsync(replyToken, BuildTimerResultReply("步進馬達", 10));
                break;

            case "步進馬達定時20":
            case "馬達定時20":
                await _mqtt.PublishStepperTimerCommandAsync(2);
                await _line.ReplyMessagesAsync(replyToken, BuildTimerResultReply("步進馬達", 20));
                break;

            case "步進馬達定時30":
            case "馬達定時30":
                await _mqtt.PublishStepperTimerCommandAsync(3);
                await _line.ReplyMessagesAsync(replyToken, BuildTimerResultReply("步進馬達", 30));
                break;

            case "步進馬達取消定時":
            case "馬達取消定時":
                await _mqtt.PublishStepperTimerCommandAsync(0);
                await _line.ReplyMessagesAsync(replyToken, BuildControlResultReply("步進馬達", false));
                break;

            case "綁定":
                await _line.ReplyTextAsync(replyToken, $"✅ 綁定成功！你的 userId：{BoundUserId}");
                break;

            case "選單":
            case "menu":
            default:
                await _line.ReplyMessagesAsync(replyToken, BuildMainMenuReply());
                break;
        }
    }

    private async Task HandlePostbackAsync(string replyToken, string? data)
    {
        switch (data)
        {
            case "action=status":
                await _line.ReplyMessagesAsync(replyToken, BuildStatusReply());
                break;

            case "action=control_menu":
                await _line.ReplyMessagesAsync(replyToken, BuildControlMenuReply());
                break;

            case "action=relay6_menu":
                await _line.ReplyMessagesAsync(replyToken, BuildRelay6ControlReply());
                break;

            case "action=relay6_on":
                await _mqtt.PublishRelayCommandAsync(6, true);
                await _line.ReplyMessagesAsync(replyToken, BuildControlResultReply("Relay6 / D6", true));
                break;

            case "action=relay6_off":
                await _mqtt.PublishRelayCommandAsync(6, false);
                await _line.ReplyMessagesAsync(replyToken, BuildControlResultReply("Relay6 / D6", false));
                break;

            case "action=relay6_timer_1":
                await _mqtt.PublishRelay6TimerCommandAsync(1);
                await _line.ReplyMessagesAsync(replyToken, BuildTimerResultReply("Relay6 / D6", 10));
                break;

            case "action=relay6_timer_2":
                await _mqtt.PublishRelay6TimerCommandAsync(2);
                await _line.ReplyMessagesAsync(replyToken, BuildTimerResultReply("Relay6 / D6", 20));
                break;

            case "action=relay6_timer_3":
                await _mqtt.PublishRelay6TimerCommandAsync(3);
                await _line.ReplyMessagesAsync(replyToken, BuildTimerResultReply("Relay6 / D6", 30));
                break;

            case "action=relay6_timer_cancel":
                await _mqtt.PublishRelay6TimerCommandAsync(0);
                await _line.ReplyMessagesAsync(replyToken, BuildControlResultReply("Relay6 / D6", false));
                break;

            case "action=stepper_menu":
                await _line.ReplyMessagesAsync(replyToken, BuildStepperControlReply());
                break;

            case "action=stepper_on":
                await _mqtt.PublishStepperCommandAsync(true);
                await _line.ReplyMessagesAsync(replyToken, BuildControlResultReply("步進馬達", true));
                break;

            case "action=stepper_off":
                await _mqtt.PublishStepperCommandAsync(false);
                await _line.ReplyMessagesAsync(replyToken, BuildControlResultReply("步進馬達", false));
                break;

            case "action=stepper_timer_1":
                await _mqtt.PublishStepperTimerCommandAsync(1);
                await _line.ReplyMessagesAsync(replyToken, BuildTimerResultReply("步進馬達", 10));
                break;

            case "action=stepper_timer_2":
                await _mqtt.PublishStepperTimerCommandAsync(2);
                await _line.ReplyMessagesAsync(replyToken, BuildTimerResultReply("步進馬達", 20));
                break;

            case "action=stepper_timer_3":
                await _mqtt.PublishStepperTimerCommandAsync(3);
                await _line.ReplyMessagesAsync(replyToken, BuildTimerResultReply("步進馬達", 30));
                break;

            case "action=stepper_timer_cancel":
                await _mqtt.PublishStepperTimerCommandAsync(0);
                await _line.ReplyMessagesAsync(replyToken, BuildControlResultReply("步進馬達", false));
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
                                text = "智慧農場",
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
                            new {
                                type = "button",
                                style = "primary",
                                height = "md",
                                action = new {
                                    type = "postback",
                                    label = "農場狀態",
                                    data = "action=status",
                                    displayText = "農場狀態"
                                }
                            },
                            new {
                                type = "button",
                                style = "primary",
                                height = "md",
                                color = "#2563EB",
                                action = new {
                                    type = "postback",
                                    label = "設備控制",
                                    data = "action=control_menu",
                                    displayText = "設備控制"
                                }
                            },
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
        string onlineText = _mqtt.IsMqttConnected ? "正常" : "離線";

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
                                text = "農場狀態",
                                weight = "bold",
                                size = "3xl",
                                align = "center"
                            },
                            BuildLargeInfoRow("設備連線", onlineText),
                            BuildLargeInfoRow("溫度", $"{latest.Temp:F1}°C"),
                            BuildLargeInfoRow("濕度", $"{latest.Humidity:F1}%"),
                            BuildLargeInfoRow("土壤數值", $"{latest.Soil:F0}"),
                            BuildLargeInfoRow("土壤狀態", TranslateSoilState(latest.SoilState)),
                            BuildLargeInfoRow("Relay5 / D5", OnOffText(latest.Relay5)),
                            BuildLargeInfoRow("Relay6 / D6", OnOffText(latest.Relay6)),
                            BuildLargeInfoRow("步進馬達", OnOffText(latest.Stepper)),
                            BuildLargeInfoRow("更新時間", $"{latest.Time:HH:mm:ss}")
                        }
                    },
                    footer = new
                    {
                        type = "box",
                        layout = "vertical",
                        spacing = "md",
                        contents = new object[]
                        {
                            new {
                                type = "button",
                                style = "primary",
                                height = "md",
                                action = new {
                                    type = "postback",
                                    label = "重新整理",
                                    data = "action=status",
                                    displayText = "農場狀態"
                                }
                            },
                            new {
                                type = "button",
                                style = "secondary",
                                height = "md",
                                action = new {
                                    type = "postback",
                                    label = "設備控制",
                                    data = "action=control_menu",
                                    displayText = "設備控制"
                                }
                            }
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
                                text = "設備控制",
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
                            new {
                                type = "button",
                                style = "primary",
                                height = "md",
                                color = "#6366F1",
                                action = new {
                                    type = "postback",
                                    label = "Relay6 / D6 控制",
                                    data = "action=relay6_menu",
                                    displayText = "Relay6 控制"
                                }
                            },
                            new {
                                type = "button",
                                style = "primary",
                                height = "md",
                                color = "#16A34A",
                                action = new {
                                    type = "postback",
                                    label = "步進馬達控制",
                                    data = "action=stepper_menu",
                                    displayText = "步進馬達控制"
                                }
                            }
                        }
                    }
                }
            }
        };
    }

    private IEnumerable<object> BuildRelay6ControlReply()
    {
        return new object[]
        {
            new
            {
                type = "flex",
                altText = "Relay6 / D6 控制",
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
                                text = "Relay6 / D6 控制",
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
                            BuildPostbackButton("開啟", "action=relay6_on", "Relay6開", "#16A34A"),
                            BuildPostbackButton("關閉", "action=relay6_off", "Relay6關"),
                            BuildPostbackButton("定時 10 分鐘", "action=relay6_timer_1", "Relay6定時10", "#2563EB"),
                            BuildPostbackButton("定時 20 分鐘", "action=relay6_timer_2", "Relay6定時20", "#2563EB"),
                            BuildPostbackButton("定時 30 分鐘", "action=relay6_timer_3", "Relay6定時30", "#2563EB"),
                            BuildPostbackButton("取消定時並關閉", "action=relay6_timer_cancel", "Relay6取消定時"),
                            BuildPostbackButton("回設備控制", "action=control_menu", "設備控制")
                        }
                    }
                }
            }
        };
    }

    private IEnumerable<object> BuildStepperControlReply()
    {
        return new object[]
        {
            new
            {
                type = "flex",
                altText = "步進馬達控制",
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
                                text = "步進馬達控制",
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
                            BuildPostbackButton("啟動", "action=stepper_on", "步進馬達開", "#16A34A"),
                            BuildPostbackButton("關閉", "action=stepper_off", "步進馬達關"),
                            BuildPostbackButton("定時 10 分鐘", "action=stepper_timer_1", "步進馬達定時10", "#2563EB"),
                            BuildPostbackButton("定時 20 分鐘", "action=stepper_timer_2", "步進馬達定時20", "#2563EB"),
                            BuildPostbackButton("定時 30 分鐘", "action=stepper_timer_3", "步進馬達定時30", "#2563EB"),
                            BuildPostbackButton("取消定時並關閉", "action=stepper_timer_cancel", "步進馬達取消定時"),
                            BuildPostbackButton("回設備控制", "action=control_menu", "設備控制")
                        }
                    }
                }
            }
        };
    }

    private IEnumerable<object> BuildControlResultReply(string deviceName, bool isOn)
    {
        var text = isOn
            ? $"✅ {deviceName} 已開啟"
            : $"✅ {deviceName} 已關閉";

        return new object[]
        {
            new
            {
                type = "flex",
                altText = text,
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
                                text = text,
                                weight = "bold",
                                size = "3xl",
                                align = "center",
                                wrap = true
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
                            BuildPostbackButton("回設備控制", "action=control_menu", "設備控制"),
                            BuildPostbackButton("查看狀態", "action=status", "農場狀態", "#2563EB")
                        }
                    }
                }
            }
        };
    }

    private IEnumerable<object> BuildTimerResultReply(string deviceName, int minutes)
    {
        var text = $"✅ {deviceName} 已啟動定時 {minutes} 分鐘";

        return new object[]
        {
            new
            {
                type = "flex",
                altText = text,
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
                                text = text,
                                weight = "bold",
                                size = "3xl",
                                align = "center",
                                wrap = true
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
                            BuildPostbackButton("回設備控制", "action=control_menu", "設備控制"),
                            BuildPostbackButton("查看狀態", "action=status", "農場狀態", "#2563EB")
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
        return value ? "ON" : "OFF";
    }

    private static string TranslateSoilState(string? soilState)
    {
        return soilState?.ToUpperInvariant() switch
        {
            "DRY" => "乾燥",
            "MOIST" => "適中",
            "WET" => "濕潤",
            _ => string.IsNullOrWhiteSpace(soilState) ? "未知" : soilState
        };
    }

    private static bool ValidateSignature(string channelSecret, string body, string xLineSignature)
    {
        if (string.IsNullOrWhiteSpace(channelSecret) ||
            string.IsNullOrWhiteSpace(xLineSignature))
        {
            return false;
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(channelSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
        var expected = Convert.ToBase64String(hash);

        var a = Encoding.UTF8.GetBytes(expected);
        var b = Encoding.UTF8.GetBytes(xLineSignature);

        return a.Length == b.Length &&
               CryptographicOperations.FixedTimeEquals(a, b);
    }
}