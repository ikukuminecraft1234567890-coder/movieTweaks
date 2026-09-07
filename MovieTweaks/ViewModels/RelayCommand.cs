using System;
using System.Windows.Input;

namespace MovieTweaks.ViewModels
{
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute == null || _canExecute();

        public void Execute(object? parameter) => _execute();
    }

    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T?> _execute;
        private readonly Predicate<T?>? _canExecute;

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public RelayCommand(Action<T?> execute, Predicate<T?>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter)
        {
            if (_canExecute == null) return true;
            return _canExecute(TryConvert(parameter));
        }

        public void Execute(object? parameter)
        {
            _execute(TryConvert(parameter));
        }

        private static T? TryConvert(object? parameter)
        {
            if (parameter is T t) return t;
            if (parameter != null)
            {
                try
                {
                    Type targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
                    return (T?)Convert.ChangeType(parameter, targetType);
                }
                catch { }
            }
            return default;
        }
    }
}
