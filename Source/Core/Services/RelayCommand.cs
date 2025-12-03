using System.Windows.Input;

namespace AgentCommandEnvironment.Core.Services;

public sealed class RelayCommand<T> : ICommand where T : class?
{
    private readonly Action<T?> execute;

    public RelayCommand(Action<T?> execute)
    {
        this.execute = execute ?? throw new ArgumentNullException(nameof(execute));
    }

    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public Boolean CanExecute(Object? parameter)
    {
        return true;
    }

    public void Execute(Object? parameter)
    {
        T? value = parameter as T;
        execute(value);
    }
}

