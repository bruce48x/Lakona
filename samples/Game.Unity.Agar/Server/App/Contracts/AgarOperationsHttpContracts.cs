using Lakona.Game.Server.Http;

namespace Server.App.Http.Operations;

public static class AgarOperationsHttpContractIds
{
    public const int GetUser = 1;
}

[LakonaHttpService("agar-operations")]
public interface IAgarOperationsHttpService
{
    [LakonaHttpEndpoint(
        AgarOperationsHttpContractIds.GetUser,
        "GET",
        "/internal/users/{account}")]
    ValueTask<LakonaHttpResponse> GetUserAsync(LakonaHttpRequest request);
}

public sealed class AgarUserInfoResponse
{
    public string Account { get; set; } = "";

    public int LoginCount { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime LastLoginAtUtc { get; set; }

    public int WinCount { get; set; }

    public int VictoryPoints { get; set; }
}

public sealed class AgarOperationsErrorResponse
{
    public string Code { get; set; } = "";

    public string Message { get; set; } = "";
}
