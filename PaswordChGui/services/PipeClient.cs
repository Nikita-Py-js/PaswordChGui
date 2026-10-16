using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PaswordChGui.Services
{
    /// <summary>Асинхронный клиент службы PaswordChService через именованный канал.</summary>
    public class PipeClient
    {
        public const string PipeName = "PaswordChService";
        private const int ConnectTimeoutMs = 5000;

        // ============================================================
        //  Ping
        // ============================================================

        public async Task<PingResult> PingAsync(CancellationToken ct = default)
        {
            try
            {
                var reply = await SendSingleAsync("PING", ct).ConfigureAwait(false);
                if (reply == "PONG")
                    return new PingResult { Ok = true };
                return new PingResult { Ok = false, Error = "Неожиданный ответ: " + reply };
            }
            catch (TimeoutException)
            {
                return new PingResult
                {
                    Ok = false,
                    Error = "Служба PaswordChService не отвечает. Проверьте, что она запущена: sc query PaswordChService"
                };
            }
            catch (Exception ex)
            {
                return new PingResult { Ok = false, Error = ex.Message };
            }
        }

        // ============================================================
        //  Change
        // ============================================================

        public async Task<ChangeResult> ChangeAsync(string targetUser, CancellationToken ct = default)
        {
            try
            {
                var reply = await SendSingleAsync("CHANGE|" + (targetUser ?? ""), ct).ConfigureAwait(false);
                var idx = reply.IndexOf('|');
                var status = idx >= 0 ? reply.Substring(0, idx) : reply;
                var payload = idx >= 0 ? reply.Substring(idx + 1) : "";

                if (status == "OK")
                    return new ChangeResult { Success = true, NewPassword = payload };

                return new ChangeResult { Success = false, Error = payload };
            }
            catch (TimeoutException)
            {
                return new ChangeResult
                {
                    Success = false,
                    Error = "Служба PaswordChService не отвечает. Проверьте, что она запущена: sc query PaswordChService"
                };
            }
            catch (Exception ex)
            {
                return new ChangeResult { Success = false, Error = ex.Message };
            }
        }

        // ============================================================
        //  Run
        // ============================================================

        public async Task<RunResult> RunAsync(string[] users, CancellationToken ct = default)
        {
            var output = new RunResult
            {
                Results = new List<ChangeResult>()
            };

            var payload = users == null || users.Length == 0 ? "" : string.Join(";", users);

            try
            {
                using (var pipe = await ConnectAsync(ct).ConfigureAwait(false))
                using (var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, true) { AutoFlush = true })
                using (var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, true))
                {
                    await writer.WriteLineAsync("ID|" + WindowsIdentity.GetCurrent().Name).ConfigureAwait(false);
                    await writer.WriteLineAsync("RUN|" + payload).ConfigureAwait(false);

                    string line;
                    while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (line == "DONE") break;
                        if (string.IsNullOrEmpty(line)) continue;

                        var idx = line.IndexOf('|');
                        if (idx < 0) continue;

                        var tag = line.Substring(0, idx);
                        var rest = line.Substring(idx + 1);

                        if (tag == "FATAL") { output.FatalError = rest; break; }

                        var p = rest.Split(new[] { '|' }, 3);
                        var r = new ChangeResult
                        {
                            Username = p.Length > 0 ? p[0] : "",
                            Email = p.Length > 1 ? p[1] : ""
                        };
                        if (tag == "OK") { r.Success = true; r.NewPassword = p.Length > 2 ? p[2] : ""; }
                        else { r.Success = false; r.Error = p.Length > 2 ? p[2] : "ошибка"; }

                        output.Results.Add(r);
                    }
                }
            }
            catch (TimeoutException)
            {
                output.FatalError = "Служба PaswordChService не отвечает. Проверьте, что она запущена.";
            }
            catch (Exception ex)
            {
                output.FatalError = ex.Message;
            }

            return output;
        }

        // ============================================================
        //  Приватное
        // ============================================================

        private async Task<string> SendSingleAsync(string command, CancellationToken ct)
        {
            using (var pipe = await ConnectAsync(ct).ConfigureAwait(false))
            using (var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, true) { AutoFlush = true })
            using (var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, true))
            {
                await writer.WriteLineAsync("ID|" + WindowsIdentity.GetCurrent().Name).ConfigureAwait(false);
                await writer.WriteLineAsync(command).ConfigureAwait(false);
                return await reader.ReadLineAsync().ConfigureAwait(false);
            }
        }

        private async Task<NamedPipeClientStream> ConnectAsync(CancellationToken ct)
        {
            var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);

            try
            {
                await Task.Run(() => pipe.Connect(ConnectTimeoutMs), ct).ConfigureAwait(false);
                return pipe;
            }
            catch (TimeoutException)
            {
                try { pipe.Dispose(); } catch { }
                throw new TimeoutException(
                    "Служба PaswordChService не отвечает. " +
                    "Проверьте, что она запущена: sc query PaswordChService");
            }
            catch
            {
                try { pipe.Dispose(); } catch { }
                throw;
            }
        }
    }

    public class PingResult
    {
        public bool Ok { get; set; }
        public string Error { get; set; }
    }

    public class ChangeResult
    {
        public bool Success { get; set; }
        public string Username { get; set; }
        public string Email { get; set; }
        public string NewPassword { get; set; }
        public string Error { get; set; }

        public string StatusText => Success ? "OK" : ("Ошибка: " + (Error ?? "?"));
        public System.Windows.Media.Brush StatusColor
            => Success
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(5, 150, 105))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 38, 38));
    }

    public class RunResult
    {
        public List<ChangeResult> Results { get; set; }
        public string FatalError { get; set; }
    }
}