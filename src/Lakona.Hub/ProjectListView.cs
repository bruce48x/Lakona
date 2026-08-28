namespace Lakona.Hub;

internal enum ProjectSortField
{
    Name,
    Engine,
    Lakona,
    LastOpened
}

internal static class ProjectListView
{
    public static IEnumerable<ProjectListItem> Apply(
        IEnumerable<ProjectListItem> projects,
        string? query,
        ProjectSortField sortField,
        bool descending)
    {
        var normalized = query?.Trim();
        var filtered = string.IsNullOrWhiteSpace(normalized)
            ? projects
            : projects.Where(project =>
                Contains(project.Name, normalized) ||
                Contains(project.Client, normalized) ||
                Contains(project.LakonaVersion, normalized) ||
                Contains(project.Path, normalized));

        Func<ProjectListItem, object?> key = sortField switch
        {
            ProjectSortField.Name => project => project.Name,
            ProjectSortField.Engine => project => project.Client,
            ProjectSortField.Lakona => project => project.LakonaVersion,
            _ => project => project.RecentActivityAtUtc
        };

        return descending
            ? filtered.OrderByDescending(key, Comparer<object?>.Create(Compare)).ThenBy(project => project.Name, StringComparer.CurrentCultureIgnoreCase)
            : filtered.OrderBy(key, Comparer<object?>.Create(Compare)).ThenBy(project => project.Name, StringComparer.CurrentCultureIgnoreCase);
    }

    private static bool Contains(string? value, string query) =>
        value?.Contains(query, StringComparison.CurrentCultureIgnoreCase) == true;

    private static int Compare(object? left, object? right)
    {
        if (ReferenceEquals(left, right)) return 0;
        if (left is null) return -1;
        if (right is null) return 1;
        if (left is string leftText && right is string rightText)
            return StringComparer.CurrentCultureIgnoreCase.Compare(leftText, rightText);
        return Comparer<object>.Default.Compare(left, right);
    }
}
