using Sati.Signatures;

namespace Sati.Portal;

public sealed record PortalAuthenticationRequest(string Token, string Pin, bool Receipt = false);
public sealed record PortalConsentRequest(bool CanAccessAndRetain, bool AcceptsElectronicRecords);
public sealed record PortalSignRequest(string TypedName, bool AgreesToIntent);
public sealed record PortalDecisionRequest(string Decision, string Reason);

public static class PortalEndpoints
{
    public const string SessionCookie = "__Host-Sati-Signing";
    public static void MapSignaturePortal(this WebApplication app)
    {
        app.MapPost("/portal/auth", async (PortalAuthenticationRequest input, SignatureWorkflow workflow, HttpContext context, CancellationToken ct) =>
        {
            if (input.Token?.Length != 64 || input.Pin?.Length is not (>= 8 and <= 12)) throw Unavailable();
            var authentication = input.Receipt ? await workflow.AuthenticateReceiptAsync(input.Token, input.Pin, ct) : await workflow.AuthenticateAsync(input.Token, input.Pin, ct);
            SetCookie(context, authentication.SessionToken, input.Receipt, authentication.ExpiresAtUtc);
            return Results.Ok(await workflow.DetailsAsync(authentication.SessionToken, input.Receipt, ct));
        }).RequireRateLimiting("pin");
        app.MapGet("/portal/state", async (SignatureWorkflow workflow, HttpContext context, CancellationToken ct) =>
        {
            var session = ReadCookie(context);
            CheckBinding(context, session.Token, required: false);
            return Results.Ok(await workflow.DetailsAsync(session.Token, session.Receipt, ct));
        });
        app.MapGet("/portal/document.pdf", async (SignatureWorkflow workflow, HttpContext context, CancellationToken ct) =>
        {
            var session = ReadCookie(context);
            CheckBinding(context, session.Token, document: true);
            var bytes = await workflow.PortalDocumentAsync(session.Token, session.Receipt, ct);
            return Results.File(bytes, "application/pdf", session.Receipt ? "signed-document.pdf" : "document-to-review.pdf", enableRangeProcessing: false);
        });
        app.MapPost("/portal/consent", async (PortalConsentRequest input, SignatureWorkflow workflow, HttpContext context, CancellationToken ct) =>
        {
            var session = SigningCookie(context);
            CheckBinding(context, session);
            await workflow.ConsentAsync(session, input.CanAccessAndRetain, input.AcceptsElectronicRecords, ct);
            return Results.Ok(await workflow.DetailsAsync(session, ct: ct));
        });
        app.MapPost("/portal/sign", async (PortalSignRequest input, SignatureWorkflow workflow, HttpContext context, CancellationToken ct) =>
        {
            var session = SigningCookie(context);
            CheckBinding(context, session);
            var result = await workflow.CompleteAsync(session, input.TypedName, input.AgreesToIntent, ct);
            SetCookie(context, result.SessionToken, true, result.ExpiresAtUtc);
            return Results.Ok(await workflow.DetailsAsync(result.SessionToken, true, ct));
        });
        app.MapPost("/portal/decision", async (PortalDecisionRequest input, SignatureWorkflow workflow, HttpContext context, CancellationToken ct) =>
        {
            var session = SigningCookie(context);
            CheckBinding(context, session);
            await workflow.DecideAsync(session, input.Decision, input.Reason, ct);
            ClearCookie(context);
            return Results.Ok(new { complete = true });
        });
        app.MapPost("/portal/extend", async (SignatureWorkflow workflow, HttpContext context, CancellationToken ct) =>
        {
            var token = SigningCookie(context);
            CheckBinding(context, token);
            var expiry = await workflow.ExtendSessionAsync(token, ct);
            SetCookie(context, token, false, expiry);
            return Results.Ok(new { expiresAtUtc = expiry });
        });
        app.MapPost("/portal/logout", async (SignatureWorkflow workflow, HttpContext context, CancellationToken ct) =>
        {
            try { var session = ReadCookie(context); CheckBinding(context, session.Token); await workflow.EndSessionAsync(session.Token, session.Receipt, ct); }
            catch (SignatureWorkflowException error) when (error.StatusCode == 404) { }
            ClearCookie(context);
            return Results.Ok(new { complete = true });
        });
    }
    private static SignatureWorkflowException Unavailable() => new("signature_link_unavailable", "This signing session is unavailable. Use your invitation or contact your case manager.", 404);
    private static void CheckBinding(HttpContext context, string token, bool required = true, bool document = false)
    {
        var expected = document ? context.Request.Query["session"] : context.Request.Headers["X-Sati-Session"];
        if (!required && expected.Count == 0) return;
        if (expected.Count != 1 || expected[0] != SignatureSecrets.PageBinding(token))
            throw new SignatureWorkflowException("signature_session_changed", "Another signing session was opened. Reopen the intended invitation before continuing.", 409);
    }
    private static (string Token, bool Receipt) ReadCookie(HttpContext context)
    {
        var cookie = context.Request.Cookies[SessionCookie];
        if (cookie is null || cookie.Length != 66 || cookie[1] != '.' || cookie[0] is not ('S' or 'R') || !SignatureSecrets.IsToken(cookie[2..])) throw Unavailable();
        return (cookie[2..], cookie[0] == 'R');
    }
    private static string SigningCookie(HttpContext context)
    {
        var result = ReadCookie(context);
        return result.Receipt ? throw Unavailable() : result.Token;
    }
    private static CookieOptions CookieOptions(DateTime? expiry = null) => new() { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = "/", Expires = expiry, IsEssential = true };
    private static void SetCookie(HttpContext context, string token, bool receipt, DateTime expiry) => context.Response.Cookies.Append(SessionCookie, (receipt ? "R." : "S.") + token, CookieOptions(expiry));
    private static void ClearCookie(HttpContext context) => context.Response.Cookies.Delete(SessionCookie, CookieOptions());
}
