using System.Linq;
using Microsoft.CodeAnalysis;

namespace Lakona.Game.Server.Hotfix.Generators
{
    [Generator]
    public sealed class HotfixGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var options = context.AnalyzerConfigOptionsProvider.Select(static (provider, cancellationToken) =>
            {
                _ = cancellationToken;
                return HotfixGeneratorOptions.From(provider.GlobalOptions);
            });

            HotfixRpcServiceGenerator.Register(context, options);
            HotfixHttpGenerator.Register(context, options);
            HotfixActorGenerator.Register(context);
            HotfixTimerGenerator.Register(context);
            HotfixComponentGenerator.Register(context);
        }
    }
}
