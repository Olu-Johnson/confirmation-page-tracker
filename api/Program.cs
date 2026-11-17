using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using BookingTracker.Api;
using BookingTracker.Api.Data;
using BookingTracker.Api.Endpoints;
using BookingTracker.Api.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;

builder.Services.Configure<TrackCrestOptions>(cfg.GetSection("TrackCrest"));
builder.Services.Configure<ZyluxOptions>(cfg.GetSection("Zylux"));
builder.Services.Configure<TrackingOptions>(cfg.GetSection("Tracking"));
builder.Services.Configure<AdminOptions>(cfg.GetSection("Admin"));

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(cfg.GetConnectionString("Default") ?? throw new InvalidOperationException("ConnectionStrings:Default is missing")));

builder.Services.AddHttpClient("trackcrest", c =>
{
    c.Timeout = TimeSpan.FromSeconds(cfg.GetValue("TrackCrest:TimeoutSeconds", 10));
    c.DefaultRequestHeaders.UserAgent.ParseAdd("BookingTracker/1.0");
});

builder.Services.AddSingleton<ConversionQueue>();
builder.Services.AddScoped<BookingEventService>();
builder.Services.AddScoped<SiteService>();
builder.Services.AddScoped<ConversionSender>();
builder.Services.AddHostedService<ConversionWorker>();

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// The tracker may be called from any active site's allowed origins (Admin → Sites).
var siteOrigins = new SiteOriginCache();
builder.Services.AddSingleton(siteOrigins);
builder.Services.AddCors(o => o.AddPolicy("tracker", p => p.SetIsOriginAllowed(siteOrigins.IsAllowed).WithMethods("POST").AllowAnyHeader()));

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("tracker", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1) }));
});

builder.Services.Configure<ForwardedHeadersOptions>(o =>
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);

builder.Services.AddOpenApi(o =>
{
    o.AddDocumentTransformer((doc, _, _) =>
    {
        doc.Info = new OpenApiInfo
        {
            Title = "Booking Tracker API",
            Version = "v1",
            Description = "Tracks Zylux Meet bookings that came through TrackCrest links and reports them to TrackCrest. " +
                          "Admin endpoints need the X-Admin-Key header (click Authorize).",
        };
        doc.Components ??= new OpenApiComponents();
        doc.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        doc.Components.SecuritySchemes["AdminKey"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = "X-Admin-Key",
            Description = "Value of Admin:ApiKey from the API settings.",
        };
        return Task.CompletedTask;
    });
    o.AddOperationTransformer((op, ctx, _) =>
    {
        if (ctx.Description.RelativePath?.StartsWith("api/admin", StringComparison.OrdinalIgnoreCase) == true)
        {
            op.Security ??= [];
            op.Security.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("AdminKey", ctx.Document)] = [] });
        }
        return Task.CompletedTask;
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    // Creates the database if it doesn't exist and applies pending migrations.
    var sp = scope.ServiceProvider;
    var db = sp.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    await SiteService.SeedDefaultAsync(db, sp.GetRequiredService<IOptions<TrackCrestOptions>>(), sp.GetRequiredService<IOptions<ZyluxOptions>>(),
        sp.GetRequiredService<IOptions<TrackingOptions>>(), app.Logger, CancellationToken.None);
    await siteOrigins.RefreshAsync(db, CancellationToken.None);
    if (!await db.Sites.AnyAsync(s => s.Active))
        app.Logger.LogWarning("No active sites yet: add one in the admin (Sites) before tracker calls and webhooks are accepted.");
}

if (string.IsNullOrEmpty(cfg["Admin:ApiKey"])) app.Logger.LogWarning("Admin:ApiKey is empty: the admin API will reject every request.");

app.UseForwardedHeaders();
app.UseCors();
app.UseRateLimiter();

// API docs: /openapi/v1.json and Swagger UI at /swagger (Development, or set EnableSwagger=true).
if (app.Environment.IsDevelopment() || cfg.GetValue<bool>("EnableSwagger"))
{
    app.MapOpenApi();
    app.UseSwaggerUI(o =>
    {
        o.SwaggerEndpoint("/openapi/v1.json", "Booking Tracker API v1");
        o.RoutePrefix = "swagger";
        o.DocumentTitle = "Booking Tracker API";
    });
}

app.MapGet("/health", () => Results.Ok(new { ok = true })).WithTags("Health").WithSummary("Liveness check");
app.MapTrackingEndpoints();
app.MapAdminEndpoints();

app.Run();
