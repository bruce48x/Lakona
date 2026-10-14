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
            context.RegisterSourceOutput(options, static (output, value) =>
            {
                if (value.GenerateStableRpcServices || value.IsHotfixProject)
                    output.AddSource("LakonaProjectRole.g.cs",
                        "[assembly: global::System.Reflection.AssemblyMetadataAttribute(\"LakonaProjectRole\", \""
                        + (value.IsHotfixProject ? "Hotfix" : "ServerApp") + "\")]\n");
            });
        }
    }
}
