using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using WebLoginDemo2.Services;
using System.Linq;

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

        // LINE Verify 時 events 可能為空
        if (!doc.RootElement.TryGetProperty("events", out var events) || events.GetArrayLength() == 0)
            return Ok();

        foreach (var ev in events.EnumerateArray())
        {
            var replyToken = ev.TryGetProperty("replyToken", out var rt) ? rt.GetString() : null;
            if (string.IsNullOrWhiteSpace(replyToken))
                continue;

            var eventType = ev.TryGetProperty("type", out var tp) ? tp.GetString() : "";

            // 綁定 userId
            if (ev.TryGetProperty("source", out var src) && src.TryGetProperty("userId", out var uid))
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
                await HandleTextCommandAsync(replyToken!, text);
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
                await HandlePostbackAsync(replyToken!, data);
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
                await _line.ReplyMessagesAsync(replyToken, BuildFanControlReply());
                break;

            case "風扇開":
                await _mqtt.PublishFanCommandAsync(true);
                await _line.ReplyMessagesAsync(replyToken, BuildControlResultReply(true));
                break;

            case "風扇關":
                await _mqtt.PublishFanCommandAsync(false);
                await _line.ReplyMessagesAsync(replyToken, BuildControlResultReply(false));
                break;

            case "綁定":
                await _line.ReplyTextAsync(replyToken, $"✅ 綁定成功！你的 userId：{BoundUserId}");
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

            case "action=fan_menu":
                await _line.ReplyMessagesAsync(replyToken, BuildFanControlReply());
                break;

            case "action=fan_on":
                await _mqtt.PublishFanCommandAsync(true);
                await _line.ReplyMessagesAsync(replyToken, BuildControlResultReply(true));
                break;

            case "action=fan_off":
                await _mqtt.PublishFanCommandAsync(false);
                await _line.ReplyMessagesAsync(replyToken, BuildControlResultReply(false));
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
    // 狀態頁（只看，不控制）
    // =========================
    private IEnumerable<object> BuildStatusReply()
    {
        var latest = _mqtt.GetLatestSensorData();

        string soilText = latest.Soil > 0.5 ? "濕潤" : "乾燥";
        string fanText = latest.Fan ? "開啟" : "關閉";
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
                            BuildLargeInfoRow("光照", $"{latest.Light:F1}%"),
                            BuildLargeInfoRow("土壤", soilText),
                            BuildLargeInfoRow("風扇", fanText),
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
                          
                        }
                    }
                }
            }
        };
    }

    // =========================
    // 控制選單（未來可擴充）
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
                                color = "#17A34A",
                                action = new {
                                    type = "postback",
                                    label = "風扇控制",
                                    data = "action=fan_menu",
                                    displayText = "風扇控制"
                                }
                            },
                            new {
                                type = "button",
                                style = "secondary",
                                height = "md",
                                action = new {
                                    type = "message",
                                    label = "水泵（未來增加）",
                                    text = "水泵功能尚未開放"
                                }
                            },
                            new {
                                type = "button",
                                style = "secondary",
                                height = "md",
                                action = new {
                                    type = "message",
                                    label = "燈光（未來增加）",
                                    text = "燈光功能尚未開放"
                                }
                            },
                            
                        }
                    }
                }
            }
        };
    }

    // =========================
    // 風扇控制頁
    // =========================
    private IEnumerable<object> BuildFanControlReply()
    {
        return new object[]
        {
            new
            {
                type = "flex",
                altText = "風扇控制",
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
                                text = "風扇控制",
                                weight = "bold",
                                size = "3xl",
                                align = "center"
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
                                    label = "打開風扇",
                                    data = "action=fan_on",
                                    displayText = "風扇開"
                                }
                            },
                            new {
                                type = "button",
                                style = "secondary",
                                height = "md",
                                action = new {
                                    type = "postback",
                                    label = "關閉風扇",
                                    data = "action=fan_off",
                                    displayText = "風扇關"
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
    private IEnumerable<object> BuildControlResultReply(bool isOn)
    {
        var text = isOn ? "✅ 風扇已打開" : "✅ 風扇已關閉";

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
                                align = "center"
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
                    flex = 4
                }
            }
        };
    }

    private static bool ValidateSignature(string channelSecret, string body, string xLineSignature)
    {
        if (string.IsNullOrWhiteSpace(channelSecret) || string.IsNullOrWhiteSpace(xLineSignature))
            return false;

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(channelSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
        var expected = Convert.ToBase64String(hash);

        var a = Encoding.UTF8.GetBytes(expected);
        var b = Encoding.UTF8.GetBytes(xLineSignature);

        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}