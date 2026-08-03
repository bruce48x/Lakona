using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Lakona.Hub.Applications;

namespace Lakona.Hub;

public sealed class ServerEditorSelection : INotifyPropertyChanged
{
    private string? preferredExecutablePath;
    private LocalApplicationInstallation? selectedEditor;
    private bool isRefreshing;

    public ServerEditorSelection(string? preferredExecutablePath = null) =>
        this.preferredExecutablePath = preferredExecutablePath;

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? SelectionChanged;

    public ObservableCollection<LocalApplicationInstallation> Editors { get; } = [];

    public LocalApplicationInstallation? SelectedEditor
    {
        get => selectedEditor;
        set
        {
            if (isRefreshing && value is null)
            {
                return;
            }

            if (ReferenceEquals(selectedEditor, value))
            {
                return;
            }

            selectedEditor = value;
            preferredExecutablePath = value?.ExecutablePath;
            OnPropertyChanged();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Refresh(IReadOnlyList<LocalApplicationInstallation> applications)
    {
        var preferredPath = SelectedEditor?.ExecutablePath ?? preferredExecutablePath;
        isRefreshing = true;
        try
        {
            Editors.Clear();
            foreach (var editor in InstalledApplicationCatalog.ServerEditors(applications))
            {
                Editors.Add(editor);
            }
        }
        finally
        {
            isRefreshing = false;
        }

        SelectedEditor = Editors.FirstOrDefault(editor =>
                             string.Equals(editor.ExecutablePath, preferredPath, StringComparison.OrdinalIgnoreCase)) ??
                         Editors.FirstOrDefault();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
