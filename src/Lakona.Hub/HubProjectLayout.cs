namespace Lakona.Hub;

internal static class HubProjectLayout
{
    public const double WideThreshold = 1180;

    public static bool UseWideLayout(double width) => width >= WideThreshold;
}
