using System.Text.Json.Serialization;
using NitroCloud.Api;
using NitroCloud.Api.Auth;
using NitroCloud.Api.HealthChecks;
using NitroCloud.Api.Hubs;
using NitroCloud.Api.Realtime;
using NitroCloud.Command;
using NitroCloud.Ingest;
using NitroCloud.Influx;
using NitroCloud.Persistence;
using NitroCloud.Persistence.Sqlite;
using NitroCloud.Storage;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Prometheus;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ── Serilog：清空默认 Provider，统一走 Serilog（Console + File，见 appsettings）──
builder.Logging.ClearProviders();
builder.Host.UseSerilog((context, services, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

// ── Api 选项（ADR-007 离线阈值 / ADR-005 最近值缓存容量）──
builder.Services.Configure<ApiOptions>(builder.Configuration.GetSection("Api"));

// ── 元数据管理约束（ADR-017：默认只读，POST/DELETE 需 AllowManualCreate=true）──
builder.Services.Configure<MetadataOptions>(builder.Configuration.GetSection("Metadata"));

// ── 认证（ADR-015 一层认证：登录态 + 命令下发校验/审计）──
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection("Auth"));
builder.Services.AddSingleton<TokenService>();
builder.Services.AddAuthentication(TokenAuthenticationDefaults.Scheme)
    .AddScheme<AuthenticationSchemeOptions, TokenAuthenticationHandler>(TokenAuthenticationDefaults.Scheme, null);
builder.Services.AddAuthorization();

// ── CORS：仅允许前端 dev 源；SignalR 需要 AllowCredentials ──
builder.Services.AddCors(o => o.AddPolicy("web", p => p
    .WithOrigins("http://localhost:5173")
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

// ── 组合根顺序：先注册 Api 侧基础设施，再注册 Ingest/Command（二者依赖 ILatestValueCache / IRealtimeNotifier）──
builder.Services.AddSingleton<ILatestValueCache, InMemoryLatestValueCache>();
builder.Services.AddSingleton<IRealtimeNotifier, SignalRRealtimeNotifier>();
builder.Services.AddSingleton<OnlineStatusService>();

builder.Services.AddNitroSqlite(builder.Configuration);
builder.Services.AddNitroInflux(builder.Configuration);
builder.Services.AddNitroIngest(builder.Configuration);
builder.Services.AddNitroCommand(builder.Configuration);

// ── 推送/序列化契约：camelCase + 枚举序列化为名称（前端 types.ts 约定）──
builder.Services.AddSignalR().AddJsonProtocol(o =>
{
    o.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks()
    .AddCheck<SqliteHealthCheck>("sqlite", tags: ["db", "ready"]);

var app = builder.Build();

// ── 播种引导管理员（迁移已在 AddNitroSqlite 注册期执行，此处在迁移后）──
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var authOptions = scope.ServiceProvider.GetRequiredService<IOptions<AuthOptions>>().Value;
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("AuthSeeding");
    AuthSeeding.EnsureAdmin(db, authOptions, logger);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("web");
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/healthz", new() { Predicate = _ => true });
app.MapMetrics();
app.MapControllers();
app.MapHub<NitroCloudHub>("/hubs/cloud");

app.Run();

/// <summary>供 IntegrationTests（WebApplicationFactory）引用程序集入口</summary>
public partial class Program { }
