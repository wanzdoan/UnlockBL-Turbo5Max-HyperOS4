using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MoKhoaBootloader.Core
{
    public interface IAdbShell
    {
        Task<CmdResult> ShellAsync(string command, int timeoutMs = 60000, CancellationToken ct = default);
        Task<CmdResult> PushAsync(string localPath, string remotePath, int timeoutMs = 120000, CancellationToken ct = default);
        Task<bool> CheckRootAsync(CancellationToken ct = default);
    }

    public sealed class AdbRunner : IAdbShell
    {
        public string AdbPath => ResourceManager.GetFilePath("adb.exe");
        public string FastbootPath => ResourceManager.GetFilePath("fastboot.exe");
        public string Serial { get; set; } = string.Empty;

        public event Action<string> LogEvent;

        public AdbRunner()
        {
        }

        private void Log(string msg) => LogEvent?.Invoke(msg);

        public async Task<CmdResult> RunAdbAsync(string args, int timeoutMs = 60000, CancellationToken ct = default)
        {
            string finalArgs = string.IsNullOrWhiteSpace(Serial) ? args : $"-s {ProcessRunner.QuoteArgument(Serial)} {args}";
            return await ProcessRunner.RunAsync(AdbPath, finalArgs, timeoutMs, ct, Path.GetDirectoryName(AdbPath));
        }

        public async Task<CmdResult> RunFastbootAsync(string args, int timeoutMs = 60000, CancellationToken ct = default)
        {
            string finalArgs = string.IsNullOrWhiteSpace(Serial) ? args : $"-s {ProcessRunner.QuoteArgument(Serial)} {args}";
            return await ProcessRunner.RunAsync(FastbootPath, finalArgs, timeoutMs, ct, Path.GetDirectoryName(FastbootPath));
        }

        public async Task<CmdResult> ShellAsync(string command, int timeoutMs = 60000, CancellationToken ct = default)
        {
            return await RunAdbAsync("shell " + ProcessRunner.QuoteArgument(command), timeoutMs, ct);
        }

        public async Task<CmdResult> SuAsync(string command, int timeoutMs = 60000, CancellationToken ct = default)
        {
            string escaped = command.Replace("'", "'\\''");
            // Thử su hệ thống trước, nếu không có thì thử wrapper /data/local/tmp/su hoặc client exploit
            // Select a provider using id first. Never repeat a failed write command on another provider.
            string suCmd = $"if su -c id 2>/dev/null | grep -q '^uid=0('; then su -c '{escaped}'; " +
                $"elif /data/local/tmp/su -c id 2>/dev/null | grep -q '^uid=0('; then /data/local/tmp/su -c '{escaped}'; " +
                $"else /data/local/tmp/cve98260/libcve98260.so su -c '{escaped}'; fi";
            return await ShellAsync(suCmd, timeoutMs, ct);
        }

        public async Task<bool> CheckRootAsync(CancellationToken ct = default)
        {
            var res = await SuAsync("id", 60000, ct);
            return res.Ok && NativeProtocol.IsRoot(res.Output);
        }

        public async Task<string> GetPropAsync(string propName, CancellationToken ct = default)
        {
            var res = await ShellAsync($"getprop {propName}", 10000, ct);
            return res.Output?.Trim() ?? string.Empty;
        }

        public async Task<CmdResult> PushAsync(string localPath, string remotePath, int timeoutMs = 120000, CancellationToken ct = default)
        {
            return await RunAdbAsync("push " + ProcessRunner.QuoteArgument(localPath) + " " + ProcessRunner.QuoteArgument(remotePath), timeoutMs, ct);
        }

        public async Task<CmdResult> PullAsync(string remotePath, string localPath, int timeoutMs = 120000, CancellationToken ct = default)
        {
            return await RunAdbAsync("pull " + ProcessRunner.QuoteArgument(remotePath) + " " + ProcessRunner.QuoteArgument(localPath), timeoutMs, ct);
        }
    }
}
