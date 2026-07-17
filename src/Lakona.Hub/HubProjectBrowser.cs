using System.Collections.ObjectModel;
using System.ComponentModel;
using Lakona.Hub.Applications;

namespace Lakona.Hub;

internal sealed class HubProjectBrowser : IDisposable
{
    private string? query;

    public ObservableCollection<ProjectListItem> Projects { get; } = [];

    public ObservableCollection<ProjectListItem> VisibleProjects { get; } = [];

    public ProjectSortField SortField { get; private set; } = ProjectSortField.LastOpened;

    public bool SortDescending { get; private set; } = true;

    public string? Query
    {
        get => query;
        set
        {
            if (string.Equals(query, value, StringComparison.Ordinal))
            {
                return;
            }
            query = value;
            RefreshView();
        }
    }

    public bool HasNoMatches => Projects.Count > 0 && VisibleProjects.Count == 0;

    public event EventHandler? ViewChanged;

    public event EventHandler? PersistentStateChanged;

    public void AddOrReplace(ProjectListItem project)
    {
        var existing = Projects.FirstOrDefault(candidate =>
            string.Equals(candidate.Path, project.Path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            RemoveCore(existing);
        }

        project.PropertyChanged += Project_PropertyChanged;
        Projects.Insert(0, project);
        RefreshView();
    }

    public void AddRestored(ProjectListItem project)
    {
        project.PropertyChanged += Project_PropertyChanged;
        Projects.Add(project);
    }

    public bool Remove(ProjectListItem project)
    {
        if (!Projects.Contains(project))
        {
            return false;
        }

        RemoveCore(project);
        RefreshView();
        return true;
    }

    public void ToggleSort(ProjectSortField field)
    {
        if (SortField == field)
        {
            SortDescending = !SortDescending;
        }
        else
        {
            SortField = field;
            SortDescending = field == ProjectSortField.LastOpened;
        }
        RefreshView();
    }

    public void RefreshApplications(IReadOnlyList<LocalApplicationInstallation> applications)
    {
        foreach (var project in Projects)
        {
            project.RefreshApplications(applications);
        }
    }

    public void RefreshLastOpened()
    {
        foreach (var project in Projects)
        {
            project.RefreshLastOpened();
        }
    }

    public void RefreshView()
    {
        VisibleProjects.Clear();
        foreach (var project in ProjectListView.Apply(Projects, Query, SortField, SortDescending))
        {
            VisibleProjects.Add(project);
        }
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        foreach (var project in Projects)
        {
            project.PropertyChanged -= Project_PropertyChanged;
            project.Dispose();
        }
        Projects.Clear();
        VisibleProjects.Clear();
    }

    private void RemoveCore(ProjectListItem project)
    {
        project.PropertyChanged -= Project_PropertyChanged;
        Projects.Remove(project);
        project.Dispose();
    }

    private void Project_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(ProjectListItem.SelectedServerEditor) or nameof(ProjectListItem.LastOpened)))
        {
            return;
        }

        if (e.PropertyName == nameof(ProjectListItem.LastOpened))
        {
            RefreshView();
        }
        PersistentStateChanged?.Invoke(this, EventArgs.Empty);
    }
}
