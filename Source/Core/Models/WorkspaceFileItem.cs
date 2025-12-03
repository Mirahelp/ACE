using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AgentCommandEnvironment.Core.Models;

public sealed class WorkspaceFileItem : INotifyPropertyChanged
{
    private String path = String.Empty;
    private String fullPath = String.Empty;
    private Boolean isFromWorkspaceAuto;

    public String Path
    {
        get { return path; }
        set
        {
            if (!String.Equals(path, value, StringComparison.Ordinal))
            {
                path = value;
                OnPropertyChanged();
            }
        }
    }

    public String FullPath
    {
        get { return fullPath; }
        set
        {
            if (!String.Equals(fullPath, value, StringComparison.Ordinal))
            {
                fullPath = value;
                OnPropertyChanged();
            }
        }
    }

    public Boolean IsFromWorkspaceAuto
    {
        get { return isFromWorkspaceAuto; }
        set
        {
            if (isFromWorkspaceAuto != value)
            {
                isFromWorkspaceAuto = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] String? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

