using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Lakona.Rpc.Analyzers;

[Generator]
public sealed partial class LakonaRpcSourceGenerator : ISourceGenerator
{
    private const string CoreRuntimeUsing = "Lakona.Rpc.Core";
    private const string ClientRuntimeUsing = "Lakona.Rpc.Client";
    private const string ServerRuntimeUsing = "Lakona.Rpc.Server";

    private static readonly DiagnosticDescriptor GenerationFailed = new(
        "LAKONA40001",
        "Lakona.Rpc source generation failed",
        "{0}",
        "Lakona.Rpc.SourceGeneration",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(GeneratorInitializationContext context)
    {
    }

    public void Execute(GeneratorExecutionContext context)
    {
        try
        {
            var compilation = context.Compilation;
            var options = GeneratorOptions.From(context.AnalyzerConfigOptions, compilation);
            if (!options.GenerateClient && !options.GenerateServer && !options.GenerateGameClient)
                options = options.WithAutoDetectedModes(compilation);

            if (!options.GenerateClient && !options.GenerateServer && !options.GenerateGameClient)
                return;

            var services = RpcSymbolReader.FindServices(compilation);
            if (services.Count == 0)
                return;

            if (options.GenerateClient)
                EmitClient(context, services, options);

            if (options.GenerateServer)
                EmitServer(context, services, options.ServerNamespace);
        }
        catch (Exception ex)
        {
            context.ReportDiagnostic(Diagnostic.Create(GenerationFailed, Location.None, ex.Message));
        }
    }

    private static void EmitClient(GeneratorExecutionContext context, List<RpcServiceModel> services, GeneratorOptions options)
    {
        var generatedNamespace = options.ClientNamespace;
        foreach (var service in services)
        {
            context.AddSource(
                $"{Naming.GetClientTypeName(service.InterfaceName)}.g.cs",
                SourceText.From(ClientSourceEmitter.GenerateClient(service, generatedNamespace), Encoding.UTF8));

            if (service.HasNotificationContract)
            {
                context.AddSource(
                    $"{Naming.GetNotificationBinderTypeName(service.NotificationContractInterfaceName!)}.g.cs",
                    SourceText.From(ClientSourceEmitter.GenerateNotificationBinder(service, generatedNamespace), Encoding.UTF8));
            }
        }

        context.AddSource(
            "RpcApi.g.cs",
            SourceText.From(ClientSourceEmitter.GenerateFacade(services, generatedNamespace), Encoding.UTF8));

        if (options.GenerateGameClient)
        {
            context.AddSource(
                "LakonaGameClient.g.cs",
                SourceText.From(
                    ClientSourceEmitter.GenerateGameClientWrapper(
                        services,
                        generatedNamespace),
                    Encoding.UTF8));
        }
    }

    private static void EmitServer(GeneratorExecutionContext context, List<RpcServiceModel> services, string generatedNamespace)
    {
        foreach (var service in services)
        {
            context.AddSource(
                $"{Naming.GetBinderTypeName(service.InterfaceName)}.g.cs",
                SourceText.From(ServerSourceEmitter.GenerateBinder(service, generatedNamespace), Encoding.UTF8));

            if (service.HasNotificationContract)
            {
                context.AddSource(
                    $"{Naming.GetNotificationProxyTypeName(service.NotificationContractInterfaceName!)}.g.cs",
                    SourceText.From(ServerSourceEmitter.GenerateNotificationProxy(service, generatedNamespace), Encoding.UTF8));
            }
        }

        context.AddSource(
            "AllServicesBinder.g.cs",
            SourceText.From(ServerSourceEmitter.GenerateAllServicesBinder(services, generatedNamespace), Encoding.UTF8));
    }
}
