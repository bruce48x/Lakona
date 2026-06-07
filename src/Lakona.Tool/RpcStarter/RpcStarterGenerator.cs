namespace Lakona.Tool.RpcStarter;

internal sealed record RpcStarterNewOptions(
    string ProjectName,
    string OutputDirectory,
    ClientEngineKind ClientEngine,
    TransportKind Transport,
    SerializerKind Serializer,
    NuGetForUnitySourceKind NuGetForUnitySource);

internal sealed class RpcStarterGenerator
{
    public void Generate(RpcStarterNewOptions options)
    {
        var rootPath = Path.GetFullPath(Path.Combine(options.OutputDirectory, options.ProjectName));
        var versions = NuGetVersionResolver.ResolveVersions(options.Transport, options.Serializer);
        var generator = new StarterTemplateGenerator(ProcessRunner.RunGit);

        StarterOutputManager.GenerateIntoTargetDirectory(
            rootPath,
            stagingRootPath => generator.GenerateTemplate(
                stagingRootPath,
                options.ProjectName,
                options.ClientEngine,
                options.Transport,
                options.Serializer,
                options.NuGetForUnitySource,
                versions));
    }
}
