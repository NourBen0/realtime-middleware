using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Events;
using RealtimeMiddleware.Api.Auth;
using RealtimeMiddleware.Api.Middleware;
using RealtimeMiddleware.Application.Interfaces;
using RealtimeMiddleware.Application.Services;
using RealtimeMiddleware.Domain.Interfaces;
using RealtimeMiddleware.Infrastructure.MessageBus;
using RealtimeMiddleware.Infrastructure.Persistence;
using RealtimeMiddleware.Infrastructure.WebSocket;

// ────────────────────────────────────────────────────────────
// Structured logging with Serilog
// ────────────────────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("logs/middleware-.log", rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

// ────────────────────────────────────────────────────────────
// Services Registration
// ────────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Swagger with JWT support
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Realtime Middleware API",
        Version = "v1",
        Description = "High-performance real-time communication middleware"
    });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header. Example: 'Bearer {token}'",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// JWT Authentication
var jwtKey = builder.Configuration["Jwt:Key"] ?? "super-secret-dev-key-32-chars!!";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "RealtimeMiddleware",
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "RealtimeMiddlewareClients",
            ValidateLifetime = true
        };

        // Allow JWT via WebSocket query string
        opts.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var token = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token))
                    context.Token = token;
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// Domain / Application / Infrastructure
builder.Services.AddSingleton<IMessageRepository, InMemoryMessageRepository>();
builder.Services.AddSingleton<IMessageBus, PriorityMessageBus>();
builder.Services.AddSingleton<IWebSocketManager, WebSocketManager>();
builder.Services.AddSingleton<WebSocketHandler>();
builder.Services.AddScoped<IMessageService, MessageService>();
builder.Services.AddSingleton<IAuthService, AuthService>();

// Background services
builder.Services.AddHostedService<RetryBackgroundService>();

// CORS for dev
builder.Services.AddCors(opts => opts.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

// ────────────────────────────────────────────────────────────
// Pipeline
// ────────────────────────────────────────────────────────────
var app = builder.Build();

app.UseMiddleware<ExceptionMiddleware>();
app.UseSerilogRequestLogging();
app.UseCors();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Realtime Middleware v1"));
}

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// ────────────────────────────────────────────────────────────
// WebSocket endpoint
// ────────────────────────────────────────────────────────────
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });

app.Map("/ws", async (HttpContext context) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("WebSocket connection required");
        return;
    }

    var clientId = context.Request.Query["clientId"].FirstOrDefault()
                   ?? Guid.NewGuid().ToString("N")[..8];

    var socket = await context.WebSockets.AcceptWebSocketAsync();
    var handler = context.RequestServices.GetRequiredService<WebSocketHandler>();

    await handler.HandleAsync(socket, clientId, context.RequestAborted);
});

Log.Information("🚀 Realtime Middleware started. WebSocket: ws://localhost:5000/ws | API: http://localhost:5000/api");

app.Run();

public partial class Program { } // for test accessibility
