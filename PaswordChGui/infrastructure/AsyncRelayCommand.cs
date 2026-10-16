using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace PaswordChGui.Infrastructure
{
    /// <summary>
    /// Асинхронная команда: Execute возвращает Task, во время выполнения
    /// CanExecute = false и кнопка сама становится неактивной.
    /// </summary>
    public class AsyncRelayCommand : ICommand
    {
        private readonly Func<object, Task> _execute;
        private readonly Func<object, bool> _canExecute;
        private bool _isRunning;

        public AsyncRelayCommand(Func<object, Task> execute, Func<object, bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public AsyncRelayCommand(Func<Task> execute, Func<bool> canExecute = null)
        {
            _execute = _ => execute();
            if (canExecute != null) _canExecute = _ => canExecute();
        }

        public bool CanExecute(object parameter)
        {
            if (_isRunning) return false;
            return _canExecute == null || _canExecute(parameter);
        }

        public async void Execute(object parameter)
        {
            if (!CanExecute(parameter)) return;

            _isRunning = true;
            CommandManager.InvalidateRequerySuggested();

            try
            {
                await _execute(parameter).ConfigureAwait(true);
            }
            finally
            {
                _isRunning = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }
}