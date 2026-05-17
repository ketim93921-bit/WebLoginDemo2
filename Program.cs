using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WebLoginDemo2.Data;
using WebLoginDemo2.Models;
using WebLoginDemo2.Services;
using WebLoginDemo2.Hubs;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// Render / Docker Port 設定
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// 資料庫
// 注意：這裡要對應 Render Environment 的 ConnectionStrings__Default
var connectionString = builder.Configuration.GetConnectionString("Default");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new Exception("找不到資料庫連線字串 ConnectionStrings:Default，請檢查 appsettings.json 或 Render Environment。");
}

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(
        connectionString,
        new MySqlServerVersion(new Version(8, 0, 31))
    )
);

// Cookie 驗證
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Auth/Login";
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
    });

builder.Services.AddAuthorization();

builder.Services.AddSingleton<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();

// Session
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});

// MVC JSON 設定
builder.Services.AddControllersWithViews()
    .AddJsonOptions(opts =>
        opts.JsonSerializerOptions.PropertyNamingPolicy =
            System.Text.Json.JsonNamingPolicy.CamelCase);

builder.Services.AddSignalR()
    .AddJsonProtocol(opts =>
        opts.PayloadSerializerOptions.PropertyNamingPolicy =
            System.Text.Json.JsonNamingPolicy.CamelCase);

// LINE
builder.Services.AddHttpClient();
builder.Services.AddSingleton<LineBotService>();

// 服務註冊
builder.Services.AddSingleton<NotificationService>();
builder.Services.AddSingleton<MqttService>();
builder.Services.AddHostedService(p => p.GetRequiredService<MqttService>());
builder.Services.AddHostedService<DataPruningService>();

var app = builder.Build();

// 自動套用 Migration，讓 Aiven 自動建立資料表
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// Render 反向代理設定
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders =
        ForwardedHeaders.XForwardedProto |
        ForwardedHeaders.XForwardedFor
});

// Render 外層已經有 HTTPS，這行可以先註解避免警告
// app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();

app.UseSession();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Auth}/{action=Login}/{id?}");

app.MapHub<SensorHub>("/sensorHub");

app.MapControllers();

app.Run();