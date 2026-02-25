using FluentValidation;
using FluentValidation.AspNetCore;
using Hubbly.Api.Controllers;
using Hubbly.Api.Hubs;
using Hubbly.Api.Middleware;
using Hubbly.Api.Validators;
using Hubbly.Application.Services;
using Hubbly.Domain.Events;
using Hubbly.Domain.Common;
using Hubbly.Domain.Services;
using Hubbly.Application.Config;
using Hubbly.Infrastructure.Data;
using Hubbly.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR.StackExchangeRedis;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using StackExchange.Redis;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;

namespace Hubbly.Api;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var configuration = builder.Configuration;
        var environment = builder.Environment;

        // Configure services
        ConfigureServices(builder.Services, configuration, environment);

        var app = builder.Build();

        // Apply database migrations automatically
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;

        try
        {
            var dbContext = services.GetRequiredService<AppDbContext>();
            var pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync();

            if (pendingMigrations.Any())
            {
                var logger = services.GetRequiredService<ILogger<Program>>();
                logger.LogInformation("Applying database migrations...");
                await dbContext.Database.MigrateAsync();
                logger.LogInformation("Database migrations applied successfully");
            }
        }
        catch (Exception ex)
        {
            var logger = services.GetRequiredService<ILogger<Program>>();
            logger.LogError(ex, "❌ An error occurred while migrating the database");
            throw; // Re-throw to prevent app start with invalid DB
        }

        // Seed initial system room
        using (var seedScope = app.Services.CreateScope())
        {
            var roomService = seedScope.ServiceProvider.GetRequiredService<IRoomService>();
            var logger = seedScope.ServiceProvider.GetRequiredService<ILogger<Program>>();
            await roomService.GetOrCreateRoomForGuestAsync();
            logger.LogInformation("Initial system room created or verified");
            
            // Create additional test rooms
            try
            {
                // Get existing rooms to avoid duplicates
                var existingRooms = await roomService.GetRoomsAsync();
                var existingRoomNames = existingRooms.Select(r => r.RoomName).ToHashSet(StringComparer.OrdinalIgnoreCase);
                
                var testRooms = new[]
                {
                    new { Name = "General Chat", Description = "Public chat for everyone", Type = Hubbly.Domain.Entities.RoomType.Public, MaxUsers = 50 },
                    new { Name = "Gaming Zone", Description = "Discuss games and have fun", Type = Hubbly.Domain.Entities.RoomType.Public, MaxUsers = 30 },
                    new { Name = "Music Lovers", Description = "Share your favorite music", Type = Hubbly.Domain.Entities.RoomType.Public, MaxUsers = 25 },
                    new { Name = "Developers", Description = "Talk about coding", Type = Hubbly.Domain.Entities.RoomType.Public, MaxUsers = 20 }
                };
                
                foreach (var testRoom in testRooms)
                {
                    if (!existingRoomNames.Contains(testRoom.Name))
                    {
                        // Need to create with a dummy user ID - using Guid.Empty for system-created rooms
                        var room = await roomService.CreateUserRoomAsync(
                            testRoom.Name,
                            testRoom.Description,
                            testRoom.Type,
                            Guid.Empty, // System-created test rooms
                            testRoom.MaxUsers
                        );
                        logger.LogInformation("Created test room: {RoomName} (ID: {RoomId})", room.RoomName, room.RoomId);
                    }
                    else
                    {
                        logger.LogInformation("Test room already exists: {RoomName}", testRoom.Name);
                    }
                }
                
                logger.LogInformation("Test room initialization completed");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error creating test rooms");
            }
        }

        // Configure middleware
        ConfigureMiddleware(app, environment);

        await app.RunAsync();
    }

    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddHttpContextAccessor();
        services.AddControllers();

        // FluentValidation - automatic validation
        services.AddFluentValidationAutoValidation();

        // FluentValidation - manual registration
        services.AddSingleton<IValidator<GuestAuthRequest>, GuestAuthRequestValidator>();
        services.AddSingleton<IValidator<RefreshTokenRequest>, RefreshTokenRequestValidator>();
        services.AddSingleton<IValidator<UpdateNicknameRequest>, UpdateNicknameRequestValidator>();
        services.AddSingleton<IValidator<UpdateAvatarRequest>, UpdateAvatarRequestValidator>();

        // Configure health checks
        ConfigureHealthChecks(services, configuration);

        // Swagger with JWT support
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Hubbly API",
                Version = "v1",
                Description = "Hubbly Backend API for 3D chat application",
                Contact = new OpenApiContact
                {
                    Name = "Hubbly Team"
                }
            });

            // JWT Bearer authentication
            var securityScheme = new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Enter JWT Bearer token **only**",
                Reference = new OpenApiReference
                {
                    Id = "Bearer",
                    Type = ReferenceType.SecurityScheme
                }
            };

            c.AddSecurityDefinition("Bearer", securityScheme);
            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                { securityScheme, Array.Empty<string>() }
            });
        });

        // Database with Polly retry policy
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("DefaultConnection"),
                npgsqlOptions =>
                {
                    npgsqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 3,
                        maxRetryDelay: TimeSpan.FromSeconds(10),
                        errorCodesToAdd: null);
                }));

        // JWT
        ConfigureJwt(services, configuration);

        // CORS
        ConfigureCors(services, configuration);

        // SignalR
        ConfigureSignalR(services, configuration);

        // Redis Configuration
        var redisConfig = new ConfigurationOptions
        {
            AbortOnConnectFail = false,
            ConnectRetry = 3,
            ConnectTimeout = 5000,
            SyncTimeout = 5000
        };
        var redisConnectionString = configuration.GetConnectionString("Redis") ?? "localhost:6379";
        redisConfig.EndPoints.Add(redisConnectionString);

        // Redis Connection Multiplexer
        services.AddSingleton<global::StackExchange.Redis.IConnectionMultiplexer>(sp =>
        {
            return global::StackExchange.Redis.ConnectionMultiplexer.Connect(redisConfig);
        });

        // Rate Limiting
        ConfigureRateLimiting(services);

        // Cache with size limit to prevent memory leaks
        services.AddMemoryCache(options =>
        {
            options.SizeLimit = 1024 * 1024; // 1MB limit
            options.ExpirationScanFrequency = TimeSpan.FromMinutes(1);
        });

        // OpenTelemetry Configuration
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName: "Hubbly", serviceVersion: "1.0.0"))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.RecordException = true;
                        options.EnrichWithHttpRequest = (enrich, request) =>
                        {
                            enrich.SetTag("request.protocol", request.Protocol);
                            enrich.SetTag("request.scheme", request.Scheme);
                        };
                    })
                    .AddHttpClientInstrumentation()
                    .AddConsoleExporter()
                    .AddOtlpExporter(); // Send traces to OTLP Collector
            });

        // Options
        services.Configure<RoomServiceOptions>(configuration.GetSection("Rooms"));

        // Repositories
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();

        // Room Repository (Composite with Redis + DB fallback)
        services.AddSingleton<IRoomRepository>(sp =>
        {
            var redis = sp.GetRequiredService<global::StackExchange.Redis.IConnectionMultiplexer>();
            var dbContext = sp.GetRequiredService<AppDbContext>();
            var logger = sp.GetRequiredService<ILogger<RedisRoomRepository>>();
            var dbLogger = sp.GetRequiredService<ILogger<RoomDbRepository>>();
            var compositeLogger = sp.GetRequiredService<ILogger<CompositeRoomRepository>>();

            var redisRepo = new RedisRoomRepository(redis, logger);
            var dbRepo = new RoomDbRepository(dbContext, dbLogger);
            return new CompositeRoomRepository(redisRepo, dbRepo, compositeLogger);
        });

        // Services
        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IChatService, ChatService>();
        services.AddScoped<IAvatarValidator, AvatarValidator>();

        // Domain Events
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.AddScoped<LoggingEventHandler>();

        // Singletons
        services.AddSingleton<IRoomService>(sp =>
        {
            var roomRepository = sp.GetRequiredService<IRoomRepository>();
            var userRepository = sp.GetRequiredService<IUserRepository>();
            var logger = sp.GetRequiredService<ILogger<RoomService>>();
            var options = sp.GetRequiredService<IOptions<RoomServiceOptions>>();
            return new RoomService(roomRepository, userRepository, logger, options);
        });

        // Hosted Services
        services.AddHostedService<RoomCleanupService>();
    }

    private static void ConfigureHealthChecks(IServiceCollection services, IConfiguration configuration)
    {
        services.AddHealthChecks()
            .AddNpgSql(
                configuration.GetConnectionString("DefaultConnection")!,
                name: "database",
                tags: new[] { "ready", "live" })
            .AddUrlGroup(
                new Uri("http://localhost:5000/avatars/male_base.glb"),
                name: "3d-models",
                tags: new[] { "ready" })
            .AddCheck<SignalRHealthCheck>(
                name: "signalr",
                tags: new[] { "ready" });
    }

    private static void ConfigureJwt(IServiceCollection services, IConfiguration configuration)
    {
        var jwtSettings = configuration.GetSection("Jwt").Get<JwtSettings>();
        services.AddSingleton(jwtSettings!);

        services.AddAuthentication(options =>
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
                ValidIssuer = jwtSettings!.Issuer,
                ValidAudience = jwtSettings.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret)),
                ClockSkew = TimeSpan.FromMinutes(5)
            };

            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var accessToken = context.Request.Query["access_token"];
                    var path = context.HttpContext.Request.Path;

                    if (!string.IsNullOrEmpty(accessToken) &&
                        (path.StartsWithSegments("/chatHub") || path.Value?.Contains("chatHub") == true))
                    {
                        context.Token = accessToken;
                    }
                    return Task.CompletedTask;
                }
            };
        });
    }

    private static void ConfigureCors(IServiceCollection services, IConfiguration configuration)
    {
        var corsOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();

        services.AddCors(options =>
        {
            options.AddPolicy("AllowMobileApp", policy =>
            {
                if (corsOrigins != null && corsOrigins.Length > 0)
                {
                    // Use SetIsOriginAllowed for flexible origin matching (including IP ranges)
                    policy.SetIsOriginAllowed(origin =>
                    {
                        // Always allow localhost and 127.0.0.1
                        if (origin.StartsWith("http://localhost:5000") ||
                            origin.StartsWith("http://127.0.0.1:5000") ||
                            origin.StartsWith("http://10.0.2.2:5000") ||
                            origin.StartsWith("http://192.168.0.103:5000"))
                        {
                            return true;
                        }

                        // Check against configured origins (supports wildcards)
                        foreach (var allowedOrigin in corsOrigins)
                        {
                            if (IsOriginAllowed(origin, allowedOrigin))
                            {
                                return true;
                            }
                        }

                        return false;
                    })
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials();
                }
                else
                {
                    // Fallback if configuration is missing - permissive for development
                    policy.SetIsOriginAllowed(_ => true)
                        .AllowAnyMethod()
                        .AllowAnyHeader()
                        .AllowCredentials();
                }
            });
        });
    }

    /// <summary>
    /// Checks if an origin matches an allowed origin pattern (supports wildcards)
    /// </summary>
    private static bool IsOriginAllowed(string origin, string allowedPattern)
    {
        try
        {
            var uri = new Uri(origin);
            var pattern = allowedPattern.TrimEnd('/');

            // Exact match
            if (string.Equals(origin, pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Wildcard subdomain support (e.g., "http://192.168.1.*:5000")
            if (pattern.Contains('*'))
            {
                var patternUri = new Uri(pattern.Replace("*", "0"));
                if (uri.Host.StartsWith(patternUri.Host.Replace("*", ""), StringComparison.OrdinalIgnoreCase))
                {
                    if (uri.Port == patternUri.Port)
                    {
                        return true;
                    }
                }
            }

            // IP range support (e.g., "http://10.0.0.0-10.255.255.255:5000")
            if (pattern.Contains('-'))
            {
                return CheckIpRange(uri, pattern);
            }
        }
        catch
        {
            // If parsing fails, don't allow
        }

        return false;
    }

    /// <summary>
    /// Checks if an IP address falls within a specified range
    /// </summary>
    private static bool CheckIpRange(Uri uri, string pattern)
    {
        try
        {
            // Pattern: "http://10.0.0.0-10.255.255.255:5000"
            var parts = pattern.Replace("http://", "").Split(':');
            var ipRange = parts[0];
            var port = int.Parse(parts[1]);

            if (uri.Port != port) return false;

            var rangeParts = ipRange.Split('-');
            var startIp = ParseIp(rangeParts[0]);
            var endIp = ParseIp(rangeParts[1]);
            var clientIp = ParseIp(uri.Host);

            if (startIp == null || endIp == null || clientIp == null) return false;

            return CompareIp(clientIp, startIp) >= 0 && CompareIp(clientIp, endIp) <= 0;
        }
        catch
        {
            return false;
        }
    }

    private static byte[]? ParseIp(string ip)
    {
        var parts = ip.Split('.');
        if (parts.Length != 4) return null;

        var bytes = new byte[4];
        for (int i = 0; i < 4; i++)
        {
            if (!byte.TryParse(parts[i], out bytes[i])) return null;
        }
        return bytes;
    }

    private static int CompareIp(byte[] ip1, byte[] ip2)
    {
        for (int i = 0; i < 4; i++)
        {
            int diff = ip1[i].CompareTo(ip2[i]);
            if (diff != 0) return diff;
        }
        return 0;
    }

    private static void ConfigureSignalR(IServiceCollection services, IConfiguration configuration)
    {
        var redisConfig = new ConfigurationOptions
        {
            AbortOnConnectFail = false,
            ConnectRetry = 3,
            ConnectTimeout = 5000,
            SyncTimeout = 5000
        };
        var redisConnectionString = configuration.GetConnectionString("Redis") ?? "localhost:6379";

        services.AddSignalR(hubOptions =>
        {
            hubOptions.MaximumParallelInvocationsPerClient = 10;
            hubOptions.MaximumReceiveMessageSize = 65536;
            hubOptions.EnableDetailedErrors = false;
            hubOptions.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
            hubOptions.KeepAliveInterval = TimeSpan.FromSeconds(15);
        })
        .AddStackExchangeRedis(redisConnectionString, options =>
        {
            // Configuration options are set via the connection string and Configuration property
            options.Configuration.AbortOnConnectFail = false;
            options.Configuration.ChannelPrefix = RedisChannel.Literal("HubblySignalR");
        });
    }

    private static void ConfigureRateLimiting(IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
                httpContext => RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.User.Identity?.Name ??
                                   httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
                    factory: partition => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = 100,
                        QueueLimit = 0,
                        Window = TimeSpan.FromMinutes(1)
                    }));

            options.AddFixedWindowLimiter("Auth", opt =>
            {
                opt.Window = TimeSpan.FromMinutes(1);
                opt.PermitLimit = 5;
                opt.QueueLimit = 0;
            });

            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = 429;
                context.HttpContext.Response.ContentType = "application/json";
                await context.HttpContext.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    error = "Too many requests. Please try again later."
                }), cancellationToken);
            };
        });
    }

    private static void ConfigureMiddleware(WebApplication app, IHostEnvironment environment)
    {
        // Swagger
        if (environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        // Static files
        ConfigureStaticFiles(app);

        // Middleware
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseRateLimiter();
        app.UseCors("AllowMobileApp");
        app.UseAuthentication();
        app.UseAuthorization();

        // Global exception handler
        app.UseExceptionHandler(errorApp =>
        {
            errorApp.Run(async context =>
            {
                var exceptionHandlerPathFeature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerPathFeature>();
                var exception = exceptionHandlerPathFeature?.Error;
                var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();

                if (exception != null)
                {
                    logger.LogError(exception, "Unhandled exception occurred on path: {Path}", context.Request.Path);
                }

                context.Response.StatusCode = 500;
                context.Response.ContentType = "application/json";

                var response = new
                {
                    error = "An internal server error occurred. Please try again later.",
                    // Only show details in development
                    details = context.RequestServices.GetService<IHostEnvironment>()?.IsDevelopment() == true && exception != null
                        ? exception.Message
                        : null
                };

                await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(response));
            });
        });

        // Request logging middleware
        app.UseMiddleware<RequestLoggingMiddleware>();

        // Map endpoints
        app.MapControllers();
        app.MapHub<ChatHub>("/chatHub", options =>
        {
            options.Transports = HttpTransportType.WebSockets;
            options.ApplicationMaxBufferSize = 65536;
            options.TransportMaxBufferSize = 65536;
        });

        // Health checks
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
    }

    private static void ConfigureStaticFiles(WebApplication app)
    {
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
            }
        });
    }

    private static Task WriteHealthResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var response = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                duration = e.Value.Duration,
                description = e.Value.Description
            }),
            timestamp = DateTime.UtcNow
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(response));
    }
}

public class SignalRHealthCheck : IHealthCheck
{
    private readonly ILogger<SignalRHealthCheck> _logger;

    public SignalRHealthCheck(ILogger<SignalRHealthCheck> logger)
    {
        _logger = logger;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // You can add real SignalR hub check here
            // For example, check that the hub is registered in routes
            return Task.FromResult(HealthCheckResult.Healthy("SignalR Hub is configured"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SignalR health check failed");
            return Task.FromResult(HealthCheckResult.Unhealthy("SignalR Hub is not responding", ex));
        }
    }
}