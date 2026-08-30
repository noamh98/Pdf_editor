using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PdfEditor.App.ViewModels;

/// <summary>Minimal change-notification base. No MVVM framework package is used.</summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void RaisePropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        RaisePropertyChanged(propertyName);
        return true;
    }

    /// <summary>Raises change notifications for properties computed from another one.</summary>
    protected void RaiseAll(params string[] propertyNames)
    {
        foreach (var name in propertyNames) RaisePropertyChanged(name);
    }
}
