using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Http;
using Server.App.Http.Operations;
using Server.App.Persistence;

namespace Server.Hotfix.Services;

[HotfixService(typeof(IAgarOperationsHttpService))]
public sealed class AgarOperationsHttpService
{
    private readonly IUserStore _users;

    public AgarOperationsHttpService(IUserStore users)
    {
        _users = users;
    }

    public async ValueTask<LakonaHttpResponse> GetUserAsync(LakonaHttpCall call)
    {
        if (!call.Request.RouteValues.TryGetValue("account", out var account)
            || string.IsNullOrWhiteSpace(account)
            || account.Length > PersistedUser.MaximumUserIdLength)
        {
            return LakonaHttpResponse.Json(
                new AgarOperationsErrorResponse
                {
                    Code = "invalid_account",
                    Message =
                        $"Account must contain 1 to {PersistedUser.MaximumUserIdLength} characters."
                },
                statusCode: 400);
        }

        var user = await _users
            .LoadAsync(account, call.CancellationToken)
            .ConfigureAwait(false);
        if (user is null)
        {
            return LakonaHttpResponse.Json(
                new AgarOperationsErrorResponse
                {
                    Code = "user_not_found",
                    Message = "No user exists for this account."
                },
                statusCode: 404);
        }

        return LakonaHttpResponse.Json(
            new AgarUserInfoResponse
            {
                Account = user.UserId,
                LoginCount = user.LoginCount,
                CreatedAtUtc = user.CreatedAtUtc,
                LastLoginAtUtc = user.LastLoginAtUtc,
                WinCount = Math.Max(0, user.WinCount),
                VictoryPoints = Math.Max(0, user.VictoryPoints)
            });
    }
}
