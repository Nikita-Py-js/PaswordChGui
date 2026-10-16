using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using PaswordChGui.Infrastructure;
using PaswordChGui.Services;

namespace PaswordChGui.ViewModels
{
    public class MainViewModel : ObservableObject
    {
        private readonly PipeClient _pipe = new PipeClient();

        public MainViewModel()
        {
            PingCommand = new AsyncRelayCommand(DoPingAsync, () => !IsBusy);
            ChangeCommand = new AsyncRelayCommand(DoChangeAsync, () => !IsBusy);
            RefreshCommand = new AsyncRelayCommand(DoRefreshAsync, () => !IsBusy);
            RunAllCommand = new AsyncRelayCommand(DoRunAllAsync, () => !IsBusy);

            RunResults = new ObservableCollection<ChangeResult>();

            CurrentUser = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
            SetStatusWarn("Проверка связи со службой...");

            _ = DoRefreshAsync();
        }

        // ==================== Свойства ====================

        public string CurrentUser { get; private set; }

        private string _roleText = "Определяется...";
        public string RoleText { get => _roleText; set => SetProperty(ref _roleText, value); }

        private string _pingStatus = "Не проверено";
        public string PingStatus { get => _pingStatus; set => SetProperty(ref _pingStatus, value); }

        private string _nextRunText = "—";
        public string NextRunText { get => _nextRunText; set => SetProperty(ref _nextRunText, value); }

        private string _intervalDaysText = "—";
        public string IntervalDaysText { get => _intervalDaysText; set => SetProperty(ref _intervalDaysText, value); }

        private string _targetUser = "";
        public string TargetUser { get => _targetUser; set => SetProperty(ref _targetUser, value); }

        private bool _hasResult;
        public bool HasResult { get => _hasResult; set => SetProperty(ref _hasResult, value); }

        private string _resultMessage = "";
        public string ResultMessage { get => _resultMessage; set => SetProperty(ref _resultMessage, value); }

        private string _resultPassword = "";
        public string ResultPassword { get => _resultPassword; set => SetProperty(ref _resultPassword, value); }

        private Brush _resultColor = Brushes.Black;
        public Brush ResultColor { get => _resultColor; set => SetProperty(ref _resultColor, value); }

        private string _statusMessage = "Готово";
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        private Brush _statusDotBrush = Brushes.Gray;
        public Brush StatusDotBrush { get => _statusDotBrush; set => SetProperty(ref _statusDotBrush, value); }

        // --- Массовая смена ---

        public ObservableCollection<ChangeResult> RunResults { get; }

        private bool _hasRunResults;
        public bool HasRunResults { get => _hasRunResults; set => SetProperty(ref _hasRunResults, value); }

        private string _runSummary = "";
        public string RunSummary { get => _runSummary; set => SetProperty(ref _runSummary, value); }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (SetProperty(ref _isBusy, value))
                    CommandManager.InvalidateRequerySuggested();
            }
        }

        // ==================== Команды ====================

        public AsyncRelayCommand PingCommand { get; }
        public AsyncRelayCommand ChangeCommand { get; }
        public AsyncRelayCommand RefreshCommand { get; }
        public AsyncRelayCommand RunAllCommand { get; }

        // ==================== Реализация ====================

        private async Task DoRefreshAsync()
        {
            IsBusy = true;
            SetStatusWarn("Обновление...");
            try
            {
                await Task.Run(() =>
                {
                    try
                    {
                        var ini = FindIni();
                        if (ini != null)
                        {
                            RoleText = DetectRoleFromIni(ini, CurrentUser);
                            ReadNextRunFromIni(ini);
                        }
                        else
                        {
                            RoleText = "my.ini не найден";
                            IntervalDaysText = "—";
                        }
                    }
                    catch (Exception ex)
                    {
                        RoleText = "Ошибка чтения my.ini: " + ex.Message;
                    }
                }).ConfigureAwait(true);

                await DoPingAsync().ConfigureAwait(true);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task DoPingAsync()
        {
            if (!IsBusy) SetStatusWarn("Проверка связи...");
            try
            {
                var res = await _pipe.PingAsync().ConfigureAwait(true);

                if (res.Ok)
                {
                    PingStatus = "Служба отвечает";
                    SetStatusOk("Служба отвечает, всё в порядке");
                }
                else
                {
                    PingStatus = "Служба недоступна";
                    SetStatusError("Служба недоступна: " + res.Error);
                }
            }
            catch (TimeoutException)
            {
                PingStatus = "Служба не запущена";
                SetStatusError("Служба PaswordChService не запущена. " +
                               "Запустите её командой: sc start PaswordChService");
            }
            catch (Exception ex)
            {
                PingStatus = "Ошибка";
                SetStatusError("Ошибка: " + ex.Message);
            }
        }

        private async Task DoChangeAsync()
        {
            IsBusy = true;
            HasResult = false;
            SetStatusWarn("Смена пароля...");
            try
            {
                var target = string.IsNullOrWhiteSpace(TargetUser)
                    ? null
                    : TargetUser.Trim();

                var res = await _pipe.ChangeAsync(target).ConfigureAwait(true);

                if (res.Success)
                {
                    ResultColor = new SolidColorBrush(Color.FromRgb(5, 150, 105));
                    ResultMessage = "Пароль успешно изменён:";
                    ResultPassword = res.NewPassword;
                    SetStatusOk("Пароль успешно изменён");
                }
                else
                {
                    ResultColor = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                    ResultMessage = "Ошибка: " + res.Error;
                    ResultPassword = "";
                    SetStatusError("Ошибка: " + res.Error);
                }
            }
            catch (Exception ex)
            {
                ResultColor = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                ResultMessage = "Ошибка: " + ex.Message;
                ResultPassword = "";
                SetStatusError("Ошибка: " + ex.Message);
            }
            finally
            {
                HasResult = true;
                IsBusy = false;
            }
        }

        private async Task DoRunAllAsync()
        {
            // 1. Подтверждение
            var answer = MessageBox.Show(
                "ВНИМАНИЕ! Массовая смена паролей.\n\n" +
                "Вы собираетесь сменить пароли для ВСЕХ пользователей из emails.csv.\n\n" +
                "Что произойдёт:\n" +
                "• Пароли всех пользователей (кроме защищённых) будут изменены прямо сейчас.\n" +
                "• Каждый пользователь получит письмо с новым паролем или одноразовой ссылкой.\n" +
                "• Результаты будут сохранены в ZIP-архив.\n" +
                "• Администраторы получат отчёт о цикле.\n\n" +
                "Отменить это действие после запуска нельзя.\n\n" +
                "Продолжить?",
                "Подтверждение массовой смены",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (answer != MessageBoxResult.Yes)
            {
                SetStatusWarn("Массовая смена отменена");
                return;
            }

            // 2. Выполнение
            IsBusy = true;
            HasRunResults = false;
            RunResults.Clear();
            RunSummary = "";
            SetStatusWarn("Массовая смена паролей...");

            try
            {
                var run = await _pipe.RunAsync(null).ConfigureAwait(true);

                if (!string.IsNullOrEmpty(run.FatalError))
                {
                    SetStatusError("Ошибка: " + run.FatalError);
                    MessageBox.Show(
                        "Не удалось выполнить массовую смену:\n\n" + run.FatalError,
                        "Ошибка",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                foreach (var r in run.Results)
                    RunResults.Add(r);

                int ok = run.Results.Count(r => r.Success);
                int err = run.Results.Count - ok;

                RunSummary = string.Format("Успешно: {0}   Ошибок: {1}", ok, err);
                HasRunResults = true;

                if (err == 0 && ok > 0)
                    SetStatusOk("Массовая смена завершена. Успешно: " + ok);
                else if (ok > 0)
                    SetStatusWarn(string.Format("Завершено с ошибками. Успешно: {0}, ошибок: {1}", ok, err));
                else
                    SetStatusError("Массовая смена не выполнена");

                // Обновляем инфу о следующей смене — lastrun.txt мог перезаписаться
                await DoRefreshAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                SetStatusError("Ошибка: " + ex.Message);
                MessageBox.Show(
                    "Непредвиденная ошибка при массовой смене:\n\n" + ex.Message,
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        // ==================== Статус ====================

        private void SetStatusOk(string message)
        {
            StatusMessage = message;
            StatusDotBrush = new SolidColorBrush(Color.FromRgb(5, 150, 105));
        }

        private void SetStatusWarn(string message)
        {
            StatusMessage = message;
            StatusDotBrush = new SolidColorBrush(Color.FromRgb(255, 196, 0));
        }

        private void SetStatusError(string message)
        {
            StatusMessage = message;
            StatusDotBrush = new SolidColorBrush(Color.FromRgb(220, 38, 38));
        }

        // ==================== Вспомогательное ====================

        private static string FindIni()
        {
            var candidates = new[]
            {
                @"C:\Program Files\PaswordCh\my.ini",
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "my.ini"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "my.ini"),
            };
            return candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists);
        }

        private static string DetectRoleFromIni(string iniPath, string fullUser)
        {
            var shortUser = fullUser.Contains("\\")
                ? fullUser.Substring(fullUser.IndexOf('\\') + 1)
                : fullUser;

            string managers = "", protectedUsers = "";
            string section = "";

            foreach (var raw in File.ReadAllLines(iniPath))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;

                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }

                if (!string.Equals(section, "Access", StringComparison.OrdinalIgnoreCase)) continue;

                var eq = line.IndexOf('=');
                if (eq <= 0) continue;

                var key = line.Substring(0, eq).Trim();
                var val = line.Substring(eq + 1).Trim();

                if (string.Equals(key, "Managers", StringComparison.OrdinalIgnoreCase)) managers = val;
                if (string.Equals(key, "ProtectedUsers", StringComparison.OrdinalIgnoreCase)) protectedUsers = val;
            }

            bool isManager = Contains(managers, shortUser);
            bool isProtected = Contains(protectedUsers, shortUser);

            if (isManager && isProtected) return "Менеджер (свой пароль защищён)";
            if (isManager) return "Менеджер";
            if (isProtected) return "Защищённый логин";
            return "Обычный пользователь";
        }

        private static bool Contains(string list, string user)
        {
            if (string.IsNullOrWhiteSpace(list)) return false;
            return list.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Any(s => string.Equals(s, user, StringComparison.OrdinalIgnoreCase));
        }

        private void ReadNextRunFromIni(string ini)
        {
            try
            {
                int intervalDays = 30;
                string section = "";
                foreach (var raw in File.ReadAllLines(ini))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;

                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        section = line.Substring(1, line.Length - 2).Trim();
                        continue;
                    }
                    if (!string.Equals(section, "Service", StringComparison.OrdinalIgnoreCase)) continue;

                    var eq = line.IndexOf('=');
                    if (eq <= 0) continue;

                    var key = line.Substring(0, eq).Trim();
                    if (string.Equals(key, "IntervalDays", StringComparison.OrdinalIgnoreCase))
                    {
                        int v;
                        if (int.TryParse(line.Substring(eq + 1).Trim(), out v) && v > 0)
                            intervalDays = v;
                        break;
                    }
                }

                IntervalDaysText = intervalDays.ToString();

                var lastRunPath = FindLastRunFile();
                if (lastRunPath == null || !File.Exists(lastRunPath))
                {
                    NextRunText = "—";
                    return;
                }

                var text = File.ReadAllText(lastRunPath).Trim();
                DateTime lastRun;
                if (!DateTime.TryParse(text, out lastRun))
                {
                    NextRunText = "—";
                    return;
                }

                var next = lastRun.AddDays(intervalDays);
                var left = next - DateTime.Now;

                if (left.TotalSeconds <= 0)
                    NextRunText = "срок наступил";
                else
                    NextRunText = string.Format("{0}  (через {1} дн. {2} ч.)",
                        next.ToString("yyyy-MM-dd HH:mm"),
                        left.Days,
                        left.Hours);
            }
            catch
            {
                NextRunText = "—";
            }
        }

        private static string FindLastRunFile()
        {
            var candidates = new[]
            {
                @"C:\Program Files\PaswordCh\Logs\lastrun.txt",
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "lastrun.txt"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "Logs", "lastrun.txt"),
            };
            return candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists);
        }
    }
}