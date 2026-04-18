using GlobalJobHunter.Service;
using GlobalJobHunter.Service.Data;
using GlobalJobHunter.Service.Models;
using GlobalJobHunter.Service.Options;
using GlobalJobHunter.Service.Providers;
using GlobalJobHunter.Service.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Telegram.Bot;

var builder = Host.CreateApplicationBuilder(args);

// ─── Strongly-typed Options ───
builder.Services.Configure<TelegramOptions>(builder.Configuration.GetSection(TelegramOptions.SectionName));
builder.Services.Configure<AiOptions>(builder.Configuration.GetSection(AiOptions.SectionName));

// ─── Database (SQLite) ───
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
                       ?? "Data Source=jobs.db";
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(connectionString));

// ─── HttpClient ───
builder.Services.AddHttpClient();

// ─── Playwright Browser (Singleton — one Chromium process for all providers) ───
builder.Services.AddSingleton<IPlaywrightBrowserService, PlaywrightBrowserService>();

// ─── Job Providers (Strategy Pattern) ───
builder.Services.AddTransient<IJobProvider, RemotiveProvider>();
builder.Services.AddTransient<IJobProvider, WeWorkRemotelyProvider>();
builder.Services.AddTransient<IJobProvider, WuzzufProvider>();
builder.Services.AddTransient<IJobProvider, LinkedInProvider>();
builder.Services.AddTransient<IJobProvider, IndeedProvider>();
builder.Services.AddTransient<IJobProvider, WellfoundProvider>();
builder.Services.AddTransient<IJobProvider, OttaProvider>();
builder.Services.AddTransient<IJobProvider, HimalayasProvider>();

// ─── Services ───
builder.Services.AddScoped<IAiEvaluatorService, GroqEvaluatorService>();
builder.Services.AddScoped<ITelegramNotifierService, TelegramNotifierService>();

// ─── Telegram Bot Client ───
builder.Services.AddSingleton<ITelegramBotClient>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var botToken = config.GetSection("Telegram")["BotToken"]
                   ?? throw new InvalidOperationException("Telegram:BotToken is not configured.");
    return new TelegramBotClient(botToken);
});

// ─── Workers ───
builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<TelegramBotWorker>(); // handles /start, /stop commands

var host = builder.Build();

// ─── Ensure database is created + migrate AppUsers table safely ───
using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    // Ensure the directory exists before SQLite tries to create the file.
    // On Railway: /app/data/ (volume mount)
    // On Windows locally: current directory (jobs.db) — no folder needed
    var dbPath = db.Database.GetDbConnection().DataSource;
    if (!string.IsNullOrWhiteSpace(dbPath))
    {
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);
    }

    // Creates the full DB if it doesn't exist yet (new installs)
    await db.Database.EnsureCreatedAsync();

    // For existing DBs (Railway already has jobs.db), create AppUsers if missing.
    // CREATE TABLE IF NOT EXISTS is idempotent — safe to run every startup.
    await db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS "AppUsers" (
            "ChatId"        INTEGER NOT NULL CONSTRAINT "PK_AppUsers" PRIMARY KEY,
            "Username"      TEXT    NULL,
            "FirstName"     TEXT    NULL,
            "IsActive"      INTEGER NOT NULL DEFAULT 1,
            "RegisteredAt"  TEXT    NOT NULL,
            "LastAlertAt"   TEXT    NULL
        );
        CREATE INDEX IF NOT EXISTS "IX_AppUsers_IsActive" ON "AppUsers" ("IsActive");
    """);

    // Auto-seed the admin ChatId so they never need to /start after a fresh deploy.
    // This is a safety net — if Railway volume is configured, users persist anyway.
    var adminChatIdStr = builder.Configuration.GetSection("Telegram")["ChatId"];
    if (long.TryParse(adminChatIdStr, out var adminChatId) && adminChatId != 0)
    {
        var adminExists = await db.AppUsers.AnyAsync(u => u.ChatId == adminChatId);
        if (!adminExists)
        {
            db.AppUsers.Add(new AppUser
            {
                ChatId       = adminChatId,
                Username     = "admin",
                FirstName    = "Admin",
                IsActive     = true,
                RegisteredAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
    }
}

await host.RunAsync();
