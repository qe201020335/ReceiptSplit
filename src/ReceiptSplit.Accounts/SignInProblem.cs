using Microsoft.AspNetCore.Http;

namespace ReceiptSplit.Accounts;

/// <summary>
/// Why a sign-in can't be used, answered to API requests as a problem whose <c>code</c> the client shows a page
/// for, with the proxy's <c>signOutUrl</c> when it has one, since the page can't ask for it. The wording names no
/// provider, so it stays right whichever ones are turned on.
/// </summary>
internal sealed record SignInProblem(int Status, string Code, string Title, string Detail)
{
    public static SignInProblem UnsupportedSignIn { get; } = new(
        StatusCodes.Status403Forbidden,
        "unsupported-sign-in",
        "Sign-in method not supported",
        "This app doesn't support the method you signed in with. Sign out and sign in another way.");

    public static SignInProblem AccountConflict { get; } = new(
        StatusCodes.Status403Forbidden,
        "account-conflict",
        "Email belongs to another account",
        "Your email already belongs to another account here. Ask an admin to release it, then sign in again.");

    public static SignInProblem IdentityUnavailable { get; } = new(
        StatusCodes.Status503ServiceUnavailable,
        "identity-unavailable",
        "Account couldn't be confirmed",
        "Your account couldn't be confirmed right now. Try again in a moment.");

    public Task WriteAsync(HttpContext context, string? signOutUrl) => Results.Problem(
            detail: Detail,
            statusCode: Status,
            title: Title,
            extensions: new Dictionary<string, object?> { ["code"] = Code, ["signOutUrl"] = signOutUrl })
        .ExecuteAsync(context);
}
