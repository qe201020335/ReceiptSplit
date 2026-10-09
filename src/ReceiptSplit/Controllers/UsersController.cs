using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReceiptSplit.Accounts;
using ReceiptSplit.Contracts;

namespace ReceiptSplit.Controllers;

[ApiController]
[Route("api")]
public class UsersController(AccountService accounts) : ControllerBase
{
    /// <summary>The signed-in user.</summary>
    [HttpGet("me")]
    [ProducesResponseType<AccountDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AccountDto>> Me(CancellationToken cancellationToken) =>
        await accounts.GetCurrentAsync(User, cancellationToken) is { } account
            ? new AccountDto(account.Id, account.Email, account.Name, account.IsAdmin, account.SignOutUrl)
            : Unauthorized();

    /// <summary>Every user, sorted by name or else email, for admins choosing who owns a receipt.</summary>
    [HttpGet("users")]
    [Authorize(Policy = AccountPolicies.Admin)]
    [ProducesResponseType<IReadOnlyList<UserSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IReadOnlyList<UserSummaryDto>> List(CancellationToken cancellationToken) =>
        [.. (await accounts.ListUsersAsync(cancellationToken)).Select(u => new UserSummaryDto(u.Id, u.Email, u.Name))];

    /// <summary>
    /// Clears an email from the user holding it, who keeps their sign-in and receipts. Someone refused because
    /// their email belonged to that user can sign in as a new user within a minute.
    /// </summary>
    [HttpPost("users/release-email")]
    [Authorize(Policy = AccountPolicies.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReleaseEmail(ReleaseEmailDto request, CancellationToken cancellationToken) =>
        await accounts.ReleaseEmailAsync(request.Email, cancellationToken)
            ? NoContent()
            : Problem(
                title: "Email not found",
                detail: "No user has that email.",
                statusCode: StatusCodes.Status404NotFound);
}
