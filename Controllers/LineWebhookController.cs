using Line.Messaging;
using Microsoft.AspNetCore.Mvc;
using WebLoginDemo2.Services;

namespace WebLoginDemo2.Controllers
{
    [ApiController]
    [Route("api/line")]
    public class LineWebhookController : ControllerBase
    {
        private readonly LineMessagingClient _line;
        private readonly MqttService _mqtt;

        // 綁定使用者
        public static string BoundUserId = "";

        public LineWebhookController(
            LineMessagingClient line,
            MqttService mqtt)
        {
            _line = line;
            _mqtt = mqtt;
        }

        // =====================================================
        // Webhook
        // =====================================================
        [HttpPost("webhook")]
        public async Task<IActionResult> Webhook([FromBody] dynamic req)
        {
            try
            {
                foreach (var ev in req.events)
                {
                    string type = ev.type?.ToString() ?? "";

                    // =================================================
                    // Message Event
                    // =================================================
                    if (type == "message")
                    {
                        string replyToken =
                            ev.replyToken?.ToString() ?? "";

                        string userId =
                            ev.source?.userId?.ToString() ?? "";

                        string messageType =
                            ev.message?.type?.ToString() ?? "";

                        if (messageType != "text")
                            continue;

                        string text =
                            ev.message?.text?.ToString()?.Trim() ?? "";

                        BoundUserId = userId;

                        Console.WriteLine($"LINE: {text}");

                        // =============================================
                        // 主選單
                        // =============================================
                        if (text == "menu" ||
                            text == "選單" ||
                            text == "控制")
                        {
                            await _line.ReplyMessageAsync(
                                replyToken,
                                new List<ISendMessage>()
                                {
                                    BuildMainMenuReply()
                                }
                            );

                            continue;
                        }

                        // =============================================
                        // 設備控制
                        // =============================================
                        if (text == "設備控制")
                        {
                            await _line.ReplyMessageAsync(
                                replyToken,
                                new List<ISendMessage>()
                                {
                                    BuildDeviceMenuReply()
                                }
                            );

                            continue;
                        }

                        // =============================================
                        // 生長燈控制
                        // =============================================
                        if (text == "生長燈控制")
                        {
                            await _line.ReplyMessageAsync(
                                replyToken,
                                new List<ISendMessage>()
                                {
                                    BuildRelay6ControlReply()
                                }
                            );

                            continue;
                        }

                        // =============================================
                        // 液肥控制
                        // =============================================
                        if (text == "液肥控制")
                        {
                            await _line.ReplyMessageAsync(
                                replyToken,
                                new List<ISendMessage>()
                                {
                                    BuildStepperControlReply()
                                }
                            );

                            continue;
                        }

                        // =============================================
                        // 即時狀態
                        // =============================================
                        if (text == "狀態" ||
                            text == "農場狀態")
                        {
                            await _line.ReplyMessageAsync(
                                replyToken,
                                new List<ISendMessage>()
                                {
                                    BuildStatusReply()
                                }
                            );

                            continue;
                        }

                        // =============================================
                        // 預設
                        // =============================================
                        await _line.ReplyMessageAsync(
                            replyToken,
                            new List<ISendMessage>()
                            {
                                new TextMessage(
                                    "請輸入：\nmenu\n選單\n控制"
                                )
                            }
                        );
                    }

                    // =================================================
                    // Postback Event
                    // =================================================
                    else if (type == "postback")
                    {
                        string replyToken =
                            ev.replyToken?.ToString() ?? "";

                        string data =
                            ev.postback?.data?.ToString() ?? "";

                        Console.WriteLine($"Postback: {data}");

                        switch (data)
                        {
                            // =========================================
                            // 生長燈 ON
                            // =========================================
                            case "action=relay6_on":

                                await _mqtt.PublishRelayCommandAsync(
                                    6,
                                    true
                                );

                                await _line.ReplyMessageAsync(
                                    replyToken,
                                    new List<ISendMessage>()
                                    {
                                        new TextMessage(
                                            "💡 生長燈已開啟"
                                        )
                                    }
                                );

                                break;

                            // =========================================
                            // 生長燈 OFF
                            // =========================================
                            case "action=relay6_off":

                                await _mqtt.PublishRelayCommandAsync(
                                    6,
                                    false
                                );

                                await _line.ReplyMessageAsync(
                                    replyToken,
                                    new List<ISendMessage>()
                                    {
                                        new TextMessage(
                                            "🛑 生長燈已關閉"
                                        )
                                    }
                                );

                                break;

                            // =========================================
                            // 生長燈定時
                            // 1 單位 = 10 分鐘
                            // =========================================
                            case "action=relay6_timer_1":

                                await _mqtt.PublishRelay6TimerCommandAsync(1);

                                await _line.ReplyMessageAsync(
                                    replyToken,
                                    new List<ISendMessage>()
                                    {
                                        new TextMessage(
                                            "⏰ 生長燈已啟動 10 分鐘"
                                        )
                                    }
                                );

                                break;

                            case "action=relay6_timer_2":

                                await _mqtt.PublishRelay6TimerCommandAsync(2);

                                await _line.ReplyMessageAsync(
                                    replyToken,
                                    new List<ISendMessage>()
                                    {
                                        new TextMessage(
                                            "⏰ 生長燈已啟動 20 分鐘"
                                        )
                                    }
                                );

                                break;

                            case "action=relay6_timer_3":

                                await _mqtt.PublishRelay6TimerCommandAsync(3);

                                await _line.ReplyMessageAsync(
                                    replyToken,
                                    new List<ISendMessage>()
                                    {
                                        new TextMessage(
                                            "⏰ 生長燈已啟動 30 分鐘"
                                        )
                                    }
                                );

                                break;

                            // =========================================
                            // 生長燈取消
                            // =========================================
                            case "action=relay6_cancel":

                                await _mqtt.PublishRelayCommandAsync(
                                    6,
                                    false
                                );

                                await _line.ReplyMessageAsync(
                                    replyToken,
                                    new List<ISendMessage>()
                                    {
                                        new TextMessage(
                                            "🛑 生長燈已停止"
                                        )
                                    }
                                );

                                break;

                            // =========================================
                            // 液肥 ON
                            // =========================================
                            case "action=stepper_on":

                                await _mqtt.PublishStepperCommandAsync(true);

                                await _line.ReplyMessageAsync(
                                    replyToken,
                                    new List<ISendMessage>()
                                    {
                                        new TextMessage(
                                            "🧪 液肥設備已啟動"
                                        )
                                    }
                                );

                                break;

                            // =========================================
                            // 液肥 OFF
                            // =========================================
                            case "action=stepper_off":

                                await _mqtt.PublishStepperCommandAsync(false);

                                await _line.ReplyMessageAsync(
                                    replyToken,
                                    new List<ISendMessage>()
                                    {
                                        new TextMessage(
                                            "🛑 液肥設備已停止"
                                        )
                                    }
                                );

                                break;

                            // =========================================
                            // 液肥定時
                            // =========================================
                            case "action=stepper_timer_1":

                                await _mqtt.PublishStepperTimerCommandAsync(1);

                                await _line.ReplyMessageAsync(
                                    replyToken,
                                    new List<ISendMessage>()
                                    {
                                        new TextMessage(
                                            "⏰ 液肥已啟動 10 分鐘"
                                        )
                                    }
                                );

                                break;

                            case "action=stepper_timer_2":

                                await _mqtt.PublishStepperTimerCommandAsync(2);

                                await _line.ReplyMessageAsync(
                                    replyToken,
                                    new List<ISendMessage>()
                                    {
                                        new TextMessage(
                                            "⏰ 液肥已啟動 20 分鐘"
                                        )
                                    }
                                );

                                break;

                            case "action=stepper_timer_3":

                                await _mqtt.PublishStepperTimerCommandAsync(3);

                                await _line.ReplyMessageAsync(
                                    replyToken,
                                    new List<ISendMessage>()
                                    {
                                        new TextMessage(
                                            "⏰ 液肥已啟動 30 分鐘"
                                        )
                                    }
                                );

                                break;

                            // =========================================
                            // 液肥取消
                            // =========================================
                            case "action=stepper_cancel":

                                await _mqtt.PublishStepperCommandAsync(false);

                                await _line.ReplyMessageAsync(
                                    replyToken,
                                    new List<ISendMessage>()
                                    {
                                        new TextMessage(
                                            "🛑 液肥已停止"
                                        )
                                    }
                                );

                                break;
                        }
                    }
                }

                return Ok();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return Ok();
            }
        }

        // =====================================================
        // 主選單
        // =====================================================
        private ISendMessage BuildMainMenuReply()
        {
            return new TemplateMessage(
                "主選單",
                new ButtonsTemplate(
                    title: "智慧農場",
                    text: "請選擇功能",
                    actions: new List<ITemplateAction>()
                    {
                        new MessageTemplateAction(
                            "📊 農場狀態",
                            "農場狀態"
                        ),

                        new MessageTemplateAction(
                            "🎛 設備控制",
                            "設備控制"
                        )
                    }
                )
            );
        }

        // =====================================================
        // 設備控制
        // =====================================================
        private ISendMessage BuildDeviceMenuReply()
        {
            return new TemplateMessage(
                "設備控制",
                new ButtonsTemplate(
                    title: "設備控制",
                    text: "請選擇設備",
                    actions: new List<ITemplateAction>()
                    {
                        new MessageTemplateAction(
                            "💡 生長燈控制",
                            "生長燈控制"
                        ),

                        new MessageTemplateAction(
                            "🧪 液肥控制",
                            "液肥控制"
                        ),

                        new MessageTemplateAction(
                            "📊 即時狀態",
                            "農場狀態"
                        )
                    }
                )
            );
        }

        // =====================================================
        // 生長燈控制
        // =====================================================
        private ISendMessage BuildRelay6ControlReply()
        {
            return new TemplateMessage(
                "生長燈控制",
                new ButtonsTemplate(
                    title: "生長燈控制",
                    text: "請選擇功能",
                    actions: new List<ITemplateAction>()
                    {
                        new PostbackTemplateAction(
                            "立即開啟",
                            "action=relay6_on"
                        ),

                        new PostbackTemplateAction(
                            "立即關閉",
                            "action=relay6_off"
                        ),

                        new PostbackTemplateAction(
                            "定時10分鐘",
                            "action=relay6_timer_1"
                        ),

                        new PostbackTemplateAction(
                            "定時20分鐘",
                            "action=relay6_timer_2"
                        )
                    }
                )
            );
        }

        // =====================================================
        // 液肥控制
        // =====================================================
        private ISendMessage BuildStepperControlReply()
        {
            return new TemplateMessage(
                "液肥控制",
                new ButtonsTemplate(
                    title: "液肥控制",
                    text: "請選擇功能",
                    actions: new List<ITemplateAction>()
                    {
                        new PostbackTemplateAction(
                            "立即啟動",
                            "action=stepper_on"
                        ),

                        new PostbackTemplateAction(
                            "立即停止",
                            "action=stepper_off"
                        ),

                        new PostbackTemplateAction(
                            "定時10分鐘",
                            "action=stepper_timer_1"
                        ),

                        new PostbackTemplateAction(
                            "定時20分鐘",
                            "action=stepper_timer_2"
                        )
                    }
                )
            );
        }

        // =====================================================
        // 即時狀態
        // =====================================================
        private ISendMessage BuildStatusReply()
        {
            var latest = _mqtt.GetLatestSensorData();

            string text =
                $"📡 設備狀態\n\n" +
                $"🌡 溫度：{latest.Temp:F1}°C\n" +
                $"💧 濕度：{latest.Humidity:F1}%\n" +
                $"🌱 土壤：{latest.Soil:F0}\n\n" +
                $"🚿 滴灌：{OnOffText(latest.Relay5)}\n" +
                $"💡 生長燈：{OnOffText(latest.Relay6)}\n" +
                $"🧪 液肥：{OnOffText(latest.Stepper)}";

            return new TextMessage(text);
        }

        // =====================================================
        // ON/OFF
        // =====================================================
        private static string OnOffText(bool value)
        {
            return value
                ? "🟢 運作中"
                : "⚫ 已停止";
        }
    }
}