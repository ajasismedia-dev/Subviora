using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Subviora.Desktop.Mvvm;

/// <summary>Minimal INotifyPropertyChanged base class.</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>Synchronous delegate command.</summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => _execute();
}

public static class UiTaskExtensions
{
    /// <summary>
    /// Starts observing <paramref name="task"/> on the current (UI) synchronization context. If the task
    /// faults, the exception is re-raised on the dispatcher, where the global handler logs and reports it.
    /// This prevents fire-and-forget failures from becoming silently unobserved.
    /// </summary>
#pragma warning disable VSTHRD100 // async void is intentional: it re-raises on the SynchronizationContext.
    public static async void SurfaceExceptions(this Task task)
#pragma warning restore VSTHRD100
    {
        ArgumentNullException.ThrowIfNull(task);
        await task;
    }
}
