using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace WebLoginDemo2.Services
{
    public class NotificationService
    {
        private readonly ILogger<NotificationService> _logger;
        private readonly IHttpClientFactory _httpFactory;
        private readonly IConfiguration _config;
        private readonly LineBotService _lineBot;

        // Email (Gmail)
        private const string SmtpServer = "smtp.gmail.com";
        private const int SmtpPort = 587;

        public NotificationService(
            ILogger<NotificationService> logger,
            IHttpClientFactory httpFactory,
            IConfiguration config,
            LineBotService lineBot)
        {
            _logger = logger;
            _httpFactory = httpFactory;
            _config = config;
            _lineBot = lineBot;
        }

        // ================= LINE 純文字推播 =================
        public async Task SendLineAsync(string message)
        {
            var userId = _config["Line:AdminUserId"];

            if (string.IsNullOrWhiteSpace(userId))
            {
                _logger.LogError("❌ LINE AdminUserId 未設定");
                return;
            }

            try
            {
                await _lineBot.PushTextAsync(userId.Trim(), message);
                _logger.LogInformation("✅ LINE 純文字推播成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ LINE 純文字推播失敗");
            }
        }

        // ================= LINE 大字異常警告卡 =================
        public async Task SendLineAlertCardAsync(string title, string detail, bool showControlButton = true)
        {
            var userId = _config["Line:AdminUserId"];

            if (string.IsNullOrWhiteSpace(userId))
            {
                _logger.LogError("❌ LINE AdminUserId 未設定");
                return;
            }

            try
            {
                var messages = new object[]
                {
                    new
                    {
                        type = "flex",
                        altText = "農場異常警告",
                        contents = new
                        {
                            type = "bubble",
                            size = "mega",
                            header = new
                            {
                                type = "box",
                                layout = "vertical",
                                backgroundColor = "#DC2626",
                                paddingAll = "20px",
                                contents = new object[]
                                {
                                    new
                                    {
                                        type = "text",
                                        text = "⚠️ 農場異常",
                                        weight = "bold",
                                        size = "xl",
                                        color = "#FFFFFF",
                                        align = "center"
                                    }
                                }
                            },
                            body = new
                            {
                                type = "box",
                                layout = "vertical",
                                spacing = "lg",
                                paddingAll = "20px",
                                contents = new object[]
                                {
                                    new
                                    {
                                        type = "text",
                                        text = title,
                                        weight = "bold",
                                        size = "xxl",
                                        wrap = true,
                                        align = "center",
                                        color = "#111827"
                                    },
                                    new
                                    {
                                        type = "text",
                                        text = detail,
                                        size = "lg",
                                        wrap = true,
                                        align = "center",
                                        color = "#4B5563"
                                    }
                                }
                            },
                            footer = new
                            {
                                type = "box",
                                layout = "vertical",
                                spacing = "md",
                                contents = showControlButton
                                    ? new object[]
                                    {
                                        new
                                        {
                                            type = "button",
                                            style = "primary",
                                            height = "md",
                                            color = "#16A34A",
                                            action = new
                                            {
                                                type = "message",
                                                label = "查看農場狀態",
                                                text = "農場狀態"
                                            }
                                        },
                                        new
                                        {
                                            type = "button",
                                            style = "secondary",
                                            height = "md",
                                            action = new
                                            {
                                                type = "message",
                                                label = "設備控制",
                                                text = "設備控制"
                                            }
                                        }
                                    }
                                    : new object[]
                                    {
                                        new
                                        {
                                            type = "button",
                                            style = "primary",
                                            height = "md",
                                            color = "#16A34A",
                                            action = new
                                            {
                                                type = "message",
                                                label = "查看農場狀態",
                                                text = "農場狀態"
                                            }
                                        }
                                    }
                            }
                        }
                    }
                };

                await _lineBot.PushMessagesAsync(userId.Trim(), messages);
                _logger.LogInformation("✅ LINE 異常警告卡推播成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ LINE 異常警告卡推播失敗");
            }
        }

        // ================= Email =================
        public async Task SendEmailAsync(string subject, string bodyHtml)
        {
            var senderEmail = _config["Email:SenderEmail"];
            var appPassword = _config["Email:AppPassword"];

            if (string.IsNullOrWhiteSpace(senderEmail) || string.IsNullOrWhiteSpace(appPassword))
            {
                _logger.LogError("❌ Email 設定缺失：請在 appsettings.json 填 Email:SenderEmail 與 Email:AppPassword");
                return;
            }

            try
            {
                using var client = new SmtpClient(SmtpServer, SmtpPort)
                {
                    EnableSsl = true,
                    Credentials = new NetworkCredential(senderEmail, appPassword)
                };

                var mail = new MailMessage
                {
                    From = new MailAddress(senderEmail),
                    Subject = subject,
                    Body = bodyHtml,
                    IsBodyHtml = true
                };

                mail.To.Add(senderEmail);

                await client.SendMailAsync(mail);
                _logger.LogInformation("✅ Email 發送成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Email 發送失敗（Gmail AppPassword/網路）");
            }
        }
    }
}