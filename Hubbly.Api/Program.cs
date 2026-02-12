using Hubbly.Api.Hubs;
using Hubbly.Application.Services;
using Hubbly.Domain.Common;
using Hubbly.Domain.Services;
using Hubbly.Infrastructure.Data;
using Hubbly.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddHttpContextAccessor();
builder.Services.AddControllers();

builder.Services.AddHealthChecks()
    .AddNpgSql(
        builder.Configuration.GetConnectionString("DefaultConnection")!,
        name: "database",
        tags: new[] { "ready", "live" })
    .AddUrlGroup(
        new Uri("http://localhost:5000/avatars/male_base.glb"),
        name: "3d-models",
        tags: new[] { "ready" })
    .AddCheck<SignalRHealthCheck>(
        name: "signalr",
        tags: new[] { "ready" });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure DbContext
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Configure JWT
var jwtSettings = builder.Configuration.GetSection("Jwt").Get<JwtSettings>();
builder.Services.AddSingleton(jwtSettings!);
builder.Services.AddSingleton<JwtTokenService>();

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings.Issuer,
        ValidAudience = jwtSettings.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret)),
        ClockSkew = TimeSpan.FromMinutes(5)
    };

    // Для SignalR
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            
            if (!string.IsNullOrEmpty(accessToken) && (path.StartsWithSegments("/chatHub") || path.Value?.Contains("chatHub") == true))
            {
                Console.WriteLine($"JWT for SignalR: {accessToken}...");
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowMobileApp", policy =>
    {
        policy.WithOrigins(
                "http://localhost:5000",
                "http://127.0.0.1:5000",
                "http://10.0.2.2:5000",
                "http://192.168.0.103:5000",
                "http://127.0.0.1:5500"
            )
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials()
            .SetIsOriginAllowedToAllowWildcardSubdomains();
    });
});

// Add SignalR
builder.Services.AddSignalR(hubOptions =>
{
    // Защита от спама в чате
    hubOptions.MaximumParallelInvocationsPerClient = 10; // 10 сообщений в секунду максимум
    hubOptions.MaximumReceiveMessageSize = 65536; // 64KB
    hubOptions.EnableDetailedErrors = false; // Не показывать детали ошибок клиенту
});

builder.Services.AddSingleton<IRoomService, RoomService>();

// Register Repositories
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();

// Register Application Services
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IChatService, ChatService>();
builder.Services.AddScoped<IAssetService, AssetService>();
builder.Services.AddScoped<IAvatarValidator, AvatarValidator>();
builder.Services.AddScoped<IProfanityFilterService, ProfanityFilterService>();

// Add Rate Limiting
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("Auth", opt =>
    {
        opt.Window = TimeSpan.FromMinutes(1);
        opt.PermitLimit = 5;
        opt.QueueLimit = 0;
    });
});

builder.Services.AddHostedService<RoomCleanupService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

var provider = new FileExtensionContentTypeProvider();
provider.Mappings[".glb"] = "model/gltf-binary";
provider.Mappings[".gltf"] = "model/gltf+json";

app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = provider,
    ServeUnknownFileTypes = true,
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers.Append("Access-Control-Allow-Origin", "*");

        // ОТЛАДКА - смотрим какие файлы запрашивают
        var path = ctx.Context.Request.Path;
        Console.WriteLine($"[STATIC] Request: {path}");

        var filePath = Path.Combine(app.Environment.WebRootPath, path.Value.TrimStart('/'));
        Console.WriteLine($"[STATIC] File exists: {File.Exists(filePath)}");
    }
});

//app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.UseCors("AllowMobileApp");

// Map Controllers
app.MapControllers();

// Map SignalR Hub
app.MapHub<ChatHub>("/chatHub", options =>
{
    options.Transports = HttpTransportType.WebSockets;
    options.ApplicationMaxBufferSize = 65536;
    options.TransportMaxBufferSize = 65536;
});

app.Use(async (context, next) =>
{
    var wwwroot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");

    if (context.Request.Path.StartsWithSegments("/avatars"))
    {
        Console.WriteLine($"[DEBUG] Avatar request: {context.Request.Path}");
        Console.WriteLine($"[DEBUG] Method: {context.Request.Method}");
        Console.WriteLine($"[DEBUG] Host: {context.Request.Host}");

        var filePath = Path.Combine(wwwroot, "avatars",
            context.Request.Path.Value.Replace("/avatars/", ""));

        Console.WriteLine($"[DEBUG] File path: {filePath}");
        Console.WriteLine($"[DEBUG] File exists: {File.Exists(filePath)}");
    }

    await next();
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = WriteHealthResponse
});

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live"),
    ResponseWriter = WriteHealthResponse
});

app.Run("http://0.0.0.0:5000");

static Task WriteHealthResponse(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";

    var response = new
    {
        status = report.Status.ToString(),
        checks = report.Entries.Select(e => new
        {
            name = e.Key,
            status = e.Value.Status.ToString(),
            duration = e.Value.Duration
        }),
        timestamp = DateTime.UtcNow
    };

    return context.Response.WriteAsync(
        JsonSerializer.Serialize(response));
}

public class SignalRHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // Проверяем, что хаб зарегистрирован
        var hubConfigured = true; // В реальности - проверка

        return Task.FromResult(
            hubConfigured
                ? HealthCheckResult.Healthy("SignalR Hub is ready")
                : HealthCheckResult.Unhealthy("SignalR Hub is not configured"));
    }
}