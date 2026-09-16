using Microsoft.Extensions.DependencyInjection;

namespace Lakona.Game.Server.Hotfix;

// One tracker per generation provider. Track registration identity rather than
// service type: multiple registrations of the same service can be legitimate.
internal sealed class HotfixActivationTracker
{
    private readonly AsyncLocal<Frame?> _current = new();

    public IDisposable Enter(ServiceDescriptor registration)
    {
        var previous = _current.Value;
        for (var frame = previous; frame is not null; frame = frame.Parent)
        {
            if (Volatile.Read(ref frame.Active) == 0) continue;
            if (ReferenceEquals(frame.Registration, registration))
            {
                var path = new List<Frame>();
                for (var current = previous; current is not null; current = current.Parent)
                {
                    if (Volatile.Read(ref current.Active) != 0) path.Add(current);
                    if (ReferenceEquals(current, frame)) break;
                }
                path.Reverse();
                var names = path.Select(item => item.Registration.ServiceType.FullName ?? item.Registration.ServiceType.Name)
                    .Append(registration.ServiceType.FullName ?? registration.ServiceType.Name);
                throw new InvalidOperationException(
                    $"Hotfix dependency cycle detected: {string.Join(" -> ", names)}. " +
                    "Remove the circular constructor or factory dependency, or extract a shared dependency.");
            }
        }

        var next = new Frame(this, registration, previous);
        _current.Value = next;
        return next;
    }

    private sealed class Frame(HotfixActivationTracker owner, ServiceDescriptor registration, Frame? parent) : IDisposable
    {
        public readonly ServiceDescriptor Registration = registration;
        public readonly Frame? Parent = parent;
        public int Active = 1;

        public void Dispose()
        {
            // A deferred task may retain an ExecutionContext captured by a constructor.
            // Completed activations must not become false cycles in that context.
            if (Interlocked.Exchange(ref Active, 0) != 0)
                owner._current.Value = Parent;
        }
    }
}
