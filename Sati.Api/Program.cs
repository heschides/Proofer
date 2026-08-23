using Azure.Identity;
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
using Sati.Forms;

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
if (authentication.MaxSessionMinutes < authentication.TokenMinutes || authentication.MaxSessionMinutes > 1_440)
    throw new InvalidOperationException("Authentication:MaxSessionMinutes must be between TokenMinutes and 1440.");

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
builder.Services.AddSingleton<DhhsFormFiller>();
builder.Services.AddSingleton<AgencyReleasePdfGenerator>();

// SSN protection. A configured key gives the real Key Vault wrapper; an unconfigured
// one gives a wrapper that fails closed, so an environment that stores no SSNs still
// starts and serves everything else. Validated here rather than on first use so a
// malformed URI is a startup error, not a surprise during a form fill.
var ssnOptions = builder.Configuration.GetSection(SsnProtectionOptions.SectionName)
    .Get<SsnProtectionOptions>() ?? new SsnProtectionOptions();
if (string.IsNullOrWhiteSpace(ssnOptions.KeyUri))
{
    builder.Services.AddSingleton<IKeyWrapper, UnconfiguredKeyWrapper>();
}
else
{
    if (!Uri.TryCreate(ssnOptions.KeyUri, UriKind.Absolute, out var ssnKeyUri))
        throw new InvalidOperationException("Ssn:KeyUri must be an absolute Azure Key Vault key URI.");
    builder.Services.AddSingleton<IKeyWrapper>(
        _ => new KeyVaultKeyWrapper(ssnKeyUri, new DefaultAzureCredential()));
}

builder.Services.AddSingleton<EnvelopeProtector>();
builder.Services.AddSingleton<IncidentAggregator>();
builder.Services.AddSingleton<ApiIncidentRecorder>();
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
    .AddCheck<DatabaseIdentityHealthCheck>("database_identity", tags: ["ready"])
    .AddCheck<SchemaDriftHealthCheck>("schema_drift", tags: ["ready"]);

var app = builder.Build();

app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
    logger.LogError(exception, "Unhandled API error. CorrelationId={CorrelationId}", context.TraceIdentifier);
    if (exception is not null)
    {
        await context.RequestServices.GetRequiredService<ApiIncidentRecorder>()
            .RecordAsync(exception, context, context.RequestAborted);
    }
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

var releaseVersion = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "unknown";
app.MapGet("/health/live", () => Results.Ok(new { status = "live" })).AllowAnonymous();
// contractRotation is the field that matters for compatibility. releaseVersion is a
// release number, bumped when a release is cut rather than when a route is added, so
// on 2026-08-19 it read 1.2.17 on both sides while this server was missing five
// routes the client already called. The fingerprint is derived from the route table
// itself and cannot say "in sync" about a surface that is not.
//
// The fingerprint only — never the route list. An anonymous caller has no business
// being handed a map of the API's surface.
app.MapGet("/health/version", () => Results.Ok(new
{
    product = "Sati.Api",
    releaseVersion,
    contractRevision = ApiSurface.Revision
})).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions()).AllowAnonymous();
app.MapSatiApi();

app.Run();

public partial class Program;
