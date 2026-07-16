using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Lakona.Hub;

public sealed class HubStatusSummary : INotifyPropertyChanged
{
    private string sdkStatusText = string.Empty;
    private string environmentSummaryText = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string SdkStatusText
    {
        get => sdkStatusText;
        set => SetField(ref sdkStatusText, value);
    }

    public string EnvironmentSummaryText
    {
        get => environmentSummaryText;
        set => SetField(ref environmentSummaryText, value);
    }

    private void SetField(ref string field, string value, [CallerMemberName] string? propertyName = null)
    {
        if (string.Equals(field, value, StringComparison.Ordinal))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
