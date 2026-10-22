using System.Text.Json;
using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Membership;
using Lakona.Game.ProcessTests.App;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Http;

namespace Lakona.Game.ProcessTests.Hotfix;

[LakonaHttpService("process-probe")]
public sealed class ProbeHttpService(
    ActorAccess actors, IActorDirectory directory, IClusterMembership membership,
    IMembershipTable table, LocalActorNodeIdentity local)
{
    [LakonaHttpEndpoint("POST", "/probe/{action}")]
    public async ValueTask<LakonaHttpResponse> HandleAsync(LakonaHttpCall call)
    {
        using var body = JsonDocument.Parse(call.Request.RawBody);
        var json = body.RootElement;
        var action = call.Request.RouteValues["action"];
        var key = new ProbeId(json.TryGetProperty("key", out var value) ? value.GetString()! : "handoff");
        var actor = ActorIdentity.Create<ProbeActor, ProbeId>(key);
        var token = call.CancellationToken;
        switch (action)
        {
            case "status":
                var snapshot = membership.Current;
                var committed = await table.ReadOrCreateAsync(token);
                return LakonaHttpResponse.Json(new
                {
                    Node = local.NodeId.Value, Pid = Environment.ProcessId,
                    Incarnation = local.Reference!.Incarnation.Value.ToString("N"),
                    View = snapshot.View.Value,
                    Active = snapshot.Members.Where(member => member.State == ClusterMemberState.Active)
                        .Select(member => member.Reference.Node.Value).ToArray(),
                    Table = committed.Entries.Select(entry => new
                    {
                        Node = entry.Reference.Node.Value,
                        Incarnation = entry.Reference.Incarnation.Value.ToString("N"), Status = entry.Status.ToString()
                    }).ToArray()
                });
            case "ensure":
                await actors.Place<ProbeActor>(key).EnsureAsync(token);
                return LakonaHttpResponse.Json(Describe(await directory.ResolveAsync(actor, token)));
            case "lookup":
                // Always consult the authority, never the route cache.
                return LakonaHttpResponse.Json(Describe(await directory.ResolveAsync(actor, token)));
            case "release":
                var expected = new ActorActivationId(Guid.Parse(json.GetProperty("activation").GetString()!));
                return LakonaHttpResponse.Json(new { Released = await directory.ReleaseAsync(actor, expected, token) });
            case "call":
                try
                {
                    return LakonaHttpResponse.Json(await actors.Route<ProbeActor>(key).CallAsync(
                        static behavior => behavior.ExecuteAsync,
                        new ProbeRequest
                        {
                            RequestId = json.GetProperty("requestId").GetString()!,
                            Hold = json.TryGetProperty("hold", out var hold) && hold.GetBoolean()
                        }, token));
                }
                catch (ActorCallException error)
                {
                    return LakonaHttpResponse.Json(new { Error = error.GetType().Name, error.Message }, 503);
                }
            default:
                return LakonaHttpResponse.Text("Unknown probe action", 404);
        }

        static object? Describe(ActorDirectoryRecord? record) => record is null ? null : new
        {
            Actor = record.ActorId.Value, Node = record.Node.Value,
            Incarnation = record.OwnerReference.Incarnation.Value.ToString("N"),
            Activation = record.ActivationId.ToString()
        };
    }
}
