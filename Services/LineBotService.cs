using System.Text;
using System.Text.Json;

namespace WebLoginDemo2.Services;

public class LineBotService
{
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<LineBotService> _logger;

    public LineBotService(
        IConfiguration config,
        IHttpClientFactory http,
        ILogger<LineBotService> logger)
    {
        _config = config;
        _http = http;
        _logger = logger;
    }

    // 回覆單一文字
    public async Task ReplyTextAsync(string replyToken, string text)
    {
        var messages = new object[]
        {
            new { type = "text", text }
        };

        await ReplyMessagesAsync(replyToken, messages);
    }

    // ✅ 回覆單一 Flex Message
    public async Task ReplyFlexAsync(string replyToken, string altText, object contents)
    {
        var messages = new object[]
        {
            new
            {
                type = "flex",
                altText,
                contents
            }
        };

        await ReplyMessagesAsync(replyToken, messages);
    }

    // 回覆多則訊息（文字 / quick reply / flex message 都可）
    public async Task ReplyMessagesAsync(string replyToken, IEnumerable<object> messages)
    {
        var msgArray = messages.Take(5).ToArray(); // LINE reply 最多 5 則

        var payload = new
        {
            replyToken,
            messages = msgArray
        };

        await SendAsync("https://api.line.me/v2/bot/message/reply", payload, "reply");
    }

    // 主動推播單一文字
    public async Task PushTextAsync(string userId, string text)
    {
        var payload = new
        {
            to = userId,
            messages = new[]
            {
                new { type = "text", text }
            }
        };

        await SendAsync("https://api.line.me/v2/bot/message/push", payload, "push");
    }

    // ✅ 主動推播單一 Flex Message
    public async Task PushFlexAsync(string userId, string altText, object contents)
    {
        var messages = new object[]
        {
            new
            {
                type = "flex",
                altText,
                contents
            }
        };

        await PushMessagesAsync(userId, messages);
    }

    // 主動推播多則訊息（可用來送大字 Flex 警告卡）
    public async Task PushMessagesAsync(string userId, IEnumerable<object> messages)
    {
        var msgArray = messages.Take(5).ToArray(); // LINE push 最多 5 則

        var payload = new
        {
            to = userId,
            messages = msgArray
        };

        await SendAsync("https://api.line.me/v2/bot/message/push", payload, "push");
    }

    // 共用送出方法
    private async Task SendAsync(string url, object payload, string actionName)
    {
        var token = SanitizeToken(_config["Line:ChannelAccessToken"]);

        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Line:ChannelAccessToken 未設定。");

        var client = _http.CreateClient();

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        req.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        var res = await client.SendAsync(req);
        var body = await res.Content.ReadAsStringAsync();

        if (!res.IsSuccessStatusCode)
        {
            _logger.LogError("LINE {Action} 失敗: {StatusCode} {Body}", actionName, (int)res.StatusCode, body);
            throw new Exception($"LINE {actionName} 失敗: {(int)res.StatusCode} {body}");
        }

        _logger.LogInformation("LINE {Action} 成功: {StatusCode}", actionName, (int)res.StatusCode);
    }

    // 清掉 token 中可能的空白 / 換行
    private static string SanitizeToken(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";

        return raw.Trim()
                  .Replace("\r", "")
                  .Replace("\n", "")
                  .Replace("\t", "")
                  .Replace(" ", "");
    }
}