using System.Globalization;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PdfSharp.Fonts;
using Sati.Api.Data;
using Sati.Api.Endpoints;
using Sati.Api.Infrastructure;
using Sati.Api.Security;
using Sati.Contracts.V1;

var builder = WebApplication.CreateBuilder(args);

if (OperatingSystem.IsWindows())
    GlobalFontSettings.UseWindowsFontsUnderWindows = true;

// Console logs are captured by App Service and avoid Windows Event Log
// permissions on locked-down hosts and developer workstations.
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();

var connectionString = builder.Configuration.GetConnectionString("SatiDemo");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("ConnectionStrings:SatiDemo is required.");

var authentication = builder.Configuration.GetSection(ApiAuthenticationOptions.SectionName).Get<ApiAuthenticationOptions>()
    ?? throw new InvalidOperationException("Authentication configuration is required.");
if (authentication.SigningKey.Length < 32)
    throw new InvalidOperationException("Authentication:SigningKey must contain at least 32 characters.");
if (authentication.TokenMinutes is < 5 or > 60)
    throw new InvalidOperationException("Authentication:TokenMinutes must be between 5 and 60.");

var satiOptions = builder.Configuration.GetSection(SatiApiOptions.SectionName).Get<SatiApiOptions>()
    ?? throw new InvalidOperationException("Sati configuration is required.");
if (satiOptions.AuditRetentionDays is < 365 or > 3_650)
    throw new InvalidOperationException("Sati:AuditRetentionDays must be between 365 and 3650.");
if (satiOptions.EdiReplayRetentionDays is < 30 or > 365)
    throw new InvalidOperationException("Sati:EdiReplayRetentionDays must be between 30 and 365.");

builder.Services.Configure<ApiAuthenticationOptions>(builder.Configuration.GetSection(ApiAuthenticationOptions.SectionName));
builder.Services.Configure<SatiApiOptions>(builder.Configuration.GetSection(SatiApiOptions.SectionName));
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<ApiClock>();
builder.Services.AddSingleton<PasswordVerifier>();
builder.Services.AddSingleton<TokenIssuer>();
builder.Services.AddSingleton<LoginAttemptGuard>();
builder.Services.AddSingleton<DatabaseIdentityValidator>();
builder.Services.AddScoped<ValidatedActorFilter>();
builder.Services.AddScoped<AuditTrail>();
builder.Services.AddScoped<PersonLifecycle>();
builder.Services.AddSingleton<PersonAuditPdfGenerator>();
builder.Services.AddHostedService<DatabaseIdentityHostedService>();
builder.Services.AddDbContextFactory<ApiDbContext>(options =>
    options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure()));
builder.Services.AddScoped(sp =>
    sp.GetRequiredService<IDbContextFactory<ApiDbContext>>().CreateDbContext());

var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(authentication.SigningKey));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = authentication.Issuer,
            ValidateAudience = true,
            ValidAudience = authentication.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = System.Security.Claims.ClaimTypes.Name,
            RoleClaimType = System.Security.Claims.ClaimTypes.Role
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryDelay)
            ? retryDelay
            : TimeSpan.FromMinutes(1);
        var retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));

        context.HttpContext.Response.Headers.RetryAfter =
            retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        await context.HttpContext.Response.WriteAsJsonAsync(
            new ApiErrorDto(
                "rate_limited",
                $"Too many sign-in attempts. Try again in about {retryAfterSeconds} seconds.",
                context.HttpContext.TraceIdentifier),
            cancellationToken);
    };
    options.AddFixedWindowLimiter("login", limiter =>
    {
        limiter.PermitLimit = 120;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
        limiter.AutoReplenishment = true;
    });
});
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseIdentityHealthCheck>("database_identity", tags: ["ready"]);

var app = builder.Build();

app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
    logger.LogError(exception, "Unhandled API error. CorrelationId={CorrelationId}", context.TraceIdentifier);
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(new ApiErrorDto(
        "server_error",
        "The request could not be completed.",
        context.TraceIdentifier));
}));
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Correlation-ID"] = context.TraceIdentifier;
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
});
app.UseHttpsRedirection();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health/live", () => Results.Ok(new { status = "live" })).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions()).AllowAnonymous();
app.MapSatiApi();

app.Run();

public partial class Program;
