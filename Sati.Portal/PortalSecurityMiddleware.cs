using System.Security.Cryptography;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Sati.Signatures;

namespace Sati.Portal;

/// <summary>One injected owner for private-page response policy, bounded bodies and CSRF validation.</summary>
public sealed class PortalSecurityMiddleware(IAntiforgery antiforgery, SignatureOptions options,
    ILogger<PortalSecurityMiddleware> logger) : IMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        context.Response.Headers.CacheControl = "no-store, max-age=0";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; script-src 'self'; style-src 'self'; connect-src 'self'; img-src 'self'; font-src 'self'; object-src 'none'; frame-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
        context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        if (!context.Request.IsHttps)
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(new { code = "https_required", message = "Use the secure HTTPS address from your invitation." });
            return;
        }
        context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000";
        if (Uri.TryCreate(options.PortalBaseUri, UriKind.Absolute, out var configured) &&
            !string.Equals(context.Request.Host.Value, configured.Authority, StringComparison.OrdinalIgnoreCase))
        { context.Response.StatusCode = 400; return; }
        try
        {
            if (HttpMethods.IsPost(context.Request.Method))
            {
                if (!context.Request.HasJsonContentType()) { context.Response.StatusCode = 415; return; }
                if (context.Request.Headers.Origin.Count > 0 && (!Uri.TryCreate(options.PortalBaseUri, UriKind.Absolute, out var origin) ||
                    context.Request.Headers.Origin.ToString() != origin.GetLeftPart(UriPartial.Authority)))
                { context.Response.StatusCode = 400; return; }
                var bytes = new byte[8193]; var count = 0;
                try
                {
                    while (count < bytes.Length)
                    {
                        var read = await context.Request.Body.ReadAsync(bytes.AsMemory(count), context.RequestAborted);
                        if (read == 0) break; count += read;
                    }
                    if (count > 8192) { context.Response.StatusCode = 413; return; }
                    await using var input = new MemoryStream(bytes, 0, count, false);
                    context.Request.Body = input;
                    await antiforgery.ValidateRequestAsync(context);
                    await next(context);
                }
                finally { CryptographicOperations.ZeroMemory(bytes); }
            }
            else await next(context);
        }
        catch (AntiforgeryValidationException) { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { code = "signature_page_expired", message = "Reload this page before continuing." }); }
        catch (SignatureWorkflowException error) { context.Response.StatusCode = error.StatusCode; await context.Response.WriteAsJsonAsync(new { code = error.Code, message = error.Message }); }
        catch (DbUpdateConcurrencyException) { context.Response.StatusCode = 409; await context.Response.WriteAsJsonAsync(new { code = "signature_changed", message = "This request has changed. Refresh the page before continuing." }); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (Exception error)
        {
            logger.LogWarning("Signing operation failed ({FailureType}). No request details were recorded.", error.GetType().Name);
            context.Response.StatusCode = 503;
            await context.Response.WriteAsJsonAsync(new { code = "signature_unavailable", message = "Signing is temporarily unavailable. Please contact your case manager for help or a paper copy." });
        }
    }
}
