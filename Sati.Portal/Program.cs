using System.Net;
using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Azure.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Sati.Data;
using Sati.Portal;
using Sati.Signatures;

var builder = WebApplication.CreateBuilder(args);
// Invitation paths, cookie values, database parameters and provider errors must never become request logs.
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.None);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.None);
builder.Logging.AddFilter("System.Net.Http", LogLevel.None);
var options = builder.Configuration.GetSection("Signatures").Get<SignatureOptions>() ?? new();
options.ExpectedEnvironment = builder.Configuration["Sati:ExpectedEnvironment"] ?? "";
options.ExpectedDatabaseName = builder.Configuration["Sati:ExpectedDatabaseName"] ?? "";
builder.Services.AddSingleton(options);
builder.Services.AddSingleton<SignatureFeature>();
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
var connection = builder.Configuration.GetConnectionString("SignaturePortal") ?? "Server=unconfigured;Database=unconfigured;Integrated Security=true;Encrypt=true";
builder.Services.AddDbContext<SignatureDbContext>(db => db.UseSqlServer(connection)); // Deliberately no automatic execution retries.
builder.Services.AddScoped<DbContext>(services => services.GetRequiredService<SignatureDbContext>());
builder.Services.AddSingleton(new AzureSignatureTransport(new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned)));
if (string.IsNullOrWhiteSpace(options.BlobContainerUri)) builder.Services.AddSingleton<ISignatureBlobStore, UnconfiguredSignatureBlobStore>();
else builder.Services.AddSingleton<ISignatureBlobStore, AzureSignatureBlobStore>();
if (string.IsNullOrWhiteSpace(options.PinKeyUri)) builder.Services.AddSingleton<ISigningPinKeyWrapper, UnconfiguredSigningKeyWrapper>();
else builder.Services.AddSingleton<ISigningPinKeyWrapper, AzureSigningPinKeyWrapper>();
// No mail credentials, outbox decryption key, or worker is ever registered on the public host.
builder.Services.AddSingleton<ISignatureOutboxKeyWrapper, UnconfiguredSigningKeyWrapper>();
builder.Services.AddSingleton<SigningPinProtector>();
builder.Services.AddSingleton<SignatureOutboxProtector>();
builder.Services.AddScoped<SignatureWorkflow>();
builder.Services.AddTransient<PortalSecurityMiddleware>();
builder.Services.AddAntiforgery(a =>
{
    a.HeaderName = "X-Sati-CSRF";
    a.Cookie.Name = "__Host-Sati-Csrf";
    a.Cookie.Path = "/";
    a.Cookie.HttpOnly = true;
    a.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    a.Cookie.SameSite = SameSiteMode.Strict;
});
builder.Services.Configure<ForwardedHeadersOptions>(forwarded =>
{
    var trusted = builder.Configuration.GetSection("Portal:TrustedProxyAddresses").Get<string[]>() ?? [];
    forwarded.ForwardedHeaders = trusted.Length == 0 ? ForwardedHeaders.None : ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor;
    forwarded.ForwardLimit = 1;
    forwarded.KnownIPNetworks.Clear();
    forwarded.KnownProxies.Clear();
    foreach (var value in trusted)
        forwarded.KnownProxies.Add(IPAddress.Parse(value));
});
builder.Services.AddRateLimiter(limits =>
{
    limits.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limits.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
    limits.AddPolicy("pin", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 12, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
    limits.OnRejected = async (context, ct) => await context.HttpContext.Response.WriteAsJsonAsync(new { code = "signature_rate_limited", message = "Please wait a minute before trying again." }, ct);
});
var app = builder.Build();
options = app.Services.GetRequiredService<SignatureOptions>();
if (app.Services.GetRequiredService<SignatureFeature>().Enabled)
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<SignatureDbContext>();
    if (db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite" && app.Environment.IsEnvironment("Testing") && options.ExpectedEnvironment == "Testing" && options.ExpectedDatabaseName == "SatiApiTests")
    { /* Only an injected isolated test provider bypasses the SQL identity check. */ }
    else
    {
        var target = new SqlConnectionStringBuilder(connection);
        if (target.InitialCatalog != options.ExpectedDatabaseName || target.Authentication != SqlAuthenticationMethod.ActiveDirectoryManagedIdentity ||
            !string.IsNullOrEmpty(target.Password) || target.TrustServerCertificate || target.Encrypt == SqlConnectionEncryptOption.Optional)
            throw new InvalidOperationException("The portal requires its separate managed identity and a verified encrypted database target.");
        var identity = await db.SignatureDatabaseEnvironment.AsNoTracking().SingleAsync();
        if (identity.DatabaseName != options.ExpectedDatabaseName || identity.EnvironmentName != options.ExpectedEnvironment)
            throw new InvalidOperationException("The portal database environment does not match its configured target.");
    }
}
app.UseForwardedHeaders();
app.UseMiddleware<PortalSecurityMiddleware>();
app.UseRateLimiter();
app.UseStaticFiles();
app.MapGet("/portal/bootstrap", (IAntiforgery antiforgery, HttpContext context, SignatureFeature feature) =>
    Results.Ok(new { csrfToken = antiforgery.GetAndStoreTokens(context).RequestToken, enabled = feature.Enabled }));
app.MapSignaturePortal();
app.MapGet("/", (IWebHostEnvironment env) => Results.File(Path.Combine(env.WebRootPath, "index.html"), "text/html"));
app.MapGet("/s/{token}", (IWebHostEnvironment env) => Results.File(Path.Combine(env.WebRootPath, "index.html"), "text/html"));
app.MapGet("/r/{token}", (IWebHostEnvironment env) => Results.File(Path.Combine(env.WebRootPath, "index.html"), "text/html"));
app.Run();

public partial class PortalProgram;
