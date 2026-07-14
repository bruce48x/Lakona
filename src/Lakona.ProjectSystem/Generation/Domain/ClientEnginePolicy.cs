namespace Lakona.Tool.Domain;

internal static class ClientEnginePolicy
{
    public static bool IsUnityCompatible(ClientEngine engine)
    {
        return engine is ClientEngine.Unity or ClientEngine.Tuanjie;
    }

    public static NuGetForUnitySource GetEffectiveNuGetForUnitySource(
        ClientEngine engine,
        NuGetForUnitySource requestedSource)
    {
        return engine == ClientEngine.Tuanjie
            ? NuGetForUnitySource.Embedded
            : requestedSource;
    }
}
