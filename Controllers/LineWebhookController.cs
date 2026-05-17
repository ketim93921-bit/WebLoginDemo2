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

            case "風扇":
            case "風扇控制":
            case "Relay1":
            case "relay1":
                await _line.ReplyMessagesAsync(replyToken, BuildRelayControlReply(1, "風扇 / Relay1"));
                break;

            case "風扇開":
            case "Relay1開":
            case "relay1 on":
                await _mqtt.PublishRelayCommandAsync(1, true);
                await _line.ReplyMessagesAsync(replyToken, BuildControlResultReply("風扇 / Relay1", true));
                break;

            case "風扇關":
            case "Relay1關":
            case "relay1 off":
                await _mqtt.PublishRelayCommandAsync(1, false);
                await _line.ReplyMessagesAsync(replyToken, BuildControlResultReply("風扇 / Relay1", false));
                break;

            case "Relay5開":
                await _mqtt.PublishRelayCommandAsync(5, true);
                await _line.ReplyMessagesAsync(replyToken, BuildControlResultReply("Relay5", true));
                break;

            case "Relay5關":
                await _mqtt.PublishRelayCommandAsync(5, false);
                await _line.ReplyMessagesAsync(replyToken, BuildControlResultReply("Relay5", false));
                break;

            case "Relay6開":
                await _mqtt.PublishRelayCommandAsync(6, true);
                await _line.ReplyMessagesAsync(replyToken, BuildControlResultReply("Relay6", true));
                break;

            case "Relay6關":
                await _mqtt.PublishRelayCommandAsync(6, false);
                await _line.ReplyMessagesAsync(replyToken, BuildControlResultReply("Relay6", false));
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

            case "action=relay1_menu":
                await _line.ReplyMessagesAsync(replyToken, BuildRelayControlReply(1, "風扇 / Relay1"));
                break;

            case "action=relay5_menu":
                await _line.ReplyMessagesAsync(replyToken, BuildRelayControlReply(5, "Relay5 土壤控制"));
                break;

            case "action=relay6_menu":
                await _line.ReplyMessagesAsync(replyToken, BuildRelayControlReply(6, "Relay6 定時控制"));
                break;

            case "action=relay1_on":
                await _mqtt.PublishRelayCommandAsync(1, true);
                await _line.ReplyMessagesAsync(replyToken, BuildControlResultReply("風扇 / Relay1", true));
                break;

            case "action=relay1_off":
                await _mqtt.PublishRelayCommandAsync(1, false);
                await _line.ReplyMessagesAsync(replyToken, BuildControlResultReply("風扇 / Relay1", false));
                break;

            case "action=relay5_on":
                await _mqtt.PublishRelayCommandAsync(5, true);
                await _line.ReplyMessagesAsync(replyToken, BuildControlResultReply("Relay5", true));
                break;

            case "action=relay5_off":
                await _mqtt.PublishRelayCommandAsync(5, false);
                await _line.ReplyMessagesAsync(replyToken, BuildControlResultReply("Relay5", false));
                break;

            case "action=relay6_on":
                await _mqtt.PublishRelayCommandAsync(6, true);
                await _line.ReplyMessagesAsync(replyToken, BuildControlResultReply("Relay6", true));
                break;

            case "action=relay6_off":
                await _mqtt.PublishRelayCommandAsync(6, false);
                await _line.ReplyMessagesAsync(replyToken, BuildControlResultReply("Relay6", false));
                break;

            case "action=stepper_on":
                await _mqtt.PublishStepperCommandAsync(true);
                await _line.ReplyMessagesAsync(replyToken, BuildControlResultReply("步進馬達", true));
                break;

            case "action=stepper_off":
                await _mqtt.PublishStepperCommandAsync(false);
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

    // =========================
    // 主選單
    // =========================
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

    // =========================
    // 狀態頁
    // =========================
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
                            BuildLargeInfoRow("溫度門檻", $"{latest.TempLimit:F1}°C"),
                            BuildLargeInfoRow("土壤門檻", $"{latest.SoilLimit}"),
                            BuildLargeInfoRow("溫控自動", OnOffText(latest.TempAuto)),
                            BuildLargeInfoRow("土壤自動", OnOffText(latest.SoilAuto)),
                            BuildLargeInfoRow("Relay1", OnOffText(latest.Relay1)),
                            BuildLargeInfoRow("Relay2", OnOffText(latest.Relay2)),
                            BuildLargeInfoRow("Relay3", OnOffText(latest.Relay3)),
                            BuildLargeInfoRow("Relay4", OnOffText(latest.Relay4)),
                            BuildLargeInfoRow("Relay5", OnOffText(latest.Relay5)),
                            BuildLargeInfoRow("Relay6", OnOffText(latest.Relay6)),
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

    // =========================
    // 控制選單
    // =========================
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
                                color = "#16A34A",
                                action = new {
                                    type = "postback",
                                    label = "風扇 / Relay1",
                                    data = "action=relay1_menu",
                                    displayText = "風扇控制"
                                }
                            },
                            new {
                                type = "button",
                                style = "primary",
                                height = "md",
                                color = "#0EA5E9",
                                action = new {
                                    type = "postback",
                                    label = "Relay5 土壤控制",
                                    data = "action=relay5_menu",
                                    displayText = "Relay5 控制"
                                }
                            },
                            new {
                                type = "button",
                                style = "primary",
                                height = "md",
                                color = "#6366F1",
                                action = new {
                                    type = "postback",
                                    label = "Relay6 定時控制",
                                    data = "action=relay6_menu",
                                    displayText = "Relay6 控制"
                                }
                            },
                            new {
                                type = "button",
                                style = "secondary",
                                height = "md",
                                action = new {
                                    type = "postback",
                                    label = "步進馬達開",
                                    data = "action=stepper_on",
                                    displayText = "步進馬達開"
                                }
                            },
                            new {
                                type = "button",
                                style = "secondary",
                                height = "md",
                                action = new {
                                    type = "postback",
                                    label = "步進馬達關",
                                    data = "action=stepper_off",
                                    displayText = "步進馬達關"
                                }
                            }
                        }
                    }
                }
            }
        };
    }

    // =========================
    // Relay 控制頁
    // =========================
    private IEnumerable<object> BuildRelayControlReply(int relayNumber, string title)
    {
        return new object[]
        {
            new
            {
                type = "flex",
                altText = $"{title} 控制",
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
                                text = $"{title} 控制",
                                weight = "bold",
                                size = "3xl",
                                align = "center",
                                wrap = true
                            },
                            new {
                                type = "text",
                                text = "請選擇操作",
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
                                color = "#16A34A",
                                action = new {
                                    type = "postback",
                                    label = "開啟",
                                    data = $"action=relay{relayNumber}_on",
                                    displayText = $"{title} 開"
                                }
                            },
                            new {
                                type = "button",
                                style = "secondary",
                                height = "md",
                                action = new {
                                    type = "postback",
                                    label = "關閉",
                                    data = $"action=relay{relayNumber}_off",
                                    displayText = $"{title} 關"
                                }
                            },
                            new {
                                type = "button",
                                style = "secondary",
                                height = "md",
                                action = new {
                                    type = "postback",
                                    label = "回設備控制",
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

    // =========================
    // 控制結果
    // =========================
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
                            new {
                                type = "button",
                                style = "secondary",
                                height = "md",
                                action = new {
                                    type = "postback",
                                    label = "回設備控制",
                                    data = "action=control_menu",
                                    displayText = "設備控制"
                                }
                            },
                            new {
                                type = "button",
                                style = "primary",
                                height = "md",
                                action = new {
                                    type = "postback",
                                    label = "查看狀態",
                                    data = "action=status",
                                    displayText = "農場狀態"
                                }
                            }
                        }
                    }
                }
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