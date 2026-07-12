using System;
using System.Threading;
using System.Threading.Tasks;

namespace Lakona.Game.Client
{
    /// <summary>Provides the monotonic retry schedule used by generated game clients.</summary>
    public interface IGameSessionRecoveryScheduler
    {
        DateTimeOffset GetUtcNow();

        TimeSpan GetDelay(int attempt);

        ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default);
    }

    /// <summary>Default bounded exponential recovery scheduler with jitter.</summary>
    public sealed class GameSessionRecoveryScheduler : IGameSessionRecoveryScheduler
    {
        private readonly object _gate = new object();
        private readonly Random _random = new Random();

        public DateTimeOffset GetUtcNow()
        {
            return DateTimeOffset.UtcNow;
        }

        public TimeSpan GetDelay(int attempt)
        {
            var exponent = Math.Min(Math.Max(attempt, 0), 5);
            var baseMilliseconds = Math.Min(100d * Math.Pow(2d, exponent), 2000d);
            double jitter;
            lock (_gate)
            {
                jitter = 0.8d + (_random.NextDouble() * 0.4d);
            }

            return TimeSpan.FromMilliseconds(baseMilliseconds * jitter);
        }

        public ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default)
        {
            return new ValueTask(Task.Delay(delay, cancellationToken));
        }
    }
}
