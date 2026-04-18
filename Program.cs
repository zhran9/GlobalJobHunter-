using GlobalJobHunter.Service;
using GlobalJobHunter.Service.Data;
using GlobalJobHunter.Service.Options;
using GlobalJobHunter.Service.Providers;
using GlobalJobHunter.Service.Services;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;

var builder = Host.CreateApplicationBuilder(args);

// ─── Strongly-typed Options ───
builder.Services.Configure<TelegramOptions>(builder.Configuration.GetSection(TelegramOptions.SectionName));
builder.Services.Configure<AiOptions>(builder.Configuration.GetSection(AiOptions.SectionName));

// ─── Database (SQLite) ───
// In production (Docker), the DB lives at /app/data/jobs.db (mounted volume).
// Locally it falls back to jobs.db in the working directory.
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

// ─── Worker ───
builder.Services.AddHostedService<Worker>();

var host = builder.Build();

// ─── Ensure database is created ───
using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
}

await host.RunAsync();
