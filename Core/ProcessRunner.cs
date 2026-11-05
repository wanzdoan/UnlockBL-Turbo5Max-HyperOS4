using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MoKhoaBootloader.Core
{
    public sealed class CmdResult
    {
        public int ExitCode { get; set; }
        public string Output { get; set; } = string.Empty;
        public string Command { get; set; } = string.Empty;

        public bool Ok => ExitCode == 0;

        public override string ToString() => $"exit={ExitCode} :: {Command}";
    }

    /// <summary>
    /// Chạy tiến trình ẩn cửa sổ, gom stdout+stderr (UTF-8), hỗ trợ timeout và huỷ.
    /// </summary>
    public static class ProcessRunner
    {
        // Windows CommandLineToArgvW quoting, including backslashes before quotes.
        public static string QuoteArgument(string value)
        {
            var sb = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in value)
            {
                if (c == '\\') { slashes++; continue; }
                sb.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
                sb.Append(c);
                slashes = 0;
            }
            sb.Append('\\', slashes * 2);
            return sb.Append('"').ToString();
        }

        public static async Task<CmdResult> RunAsync(
            string fileName,
            string arguments,
            int timeoutMs,
            CancellationToken ct,
            string workingDirectory = null)
        {
            ct.ThrowIfCancellationRequested();
            var result = new CmdResult { Command = Path.GetFileName(fileName) + " " + arguments };

            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            if (!string.IsNullOrEmpty(workingDirectory))
                psi.WorkingDirectory = workingDirectory;

            using (var proc = new Process { StartInfo = psi, EnableRaisingEvents = true })
            {
                var sb = new StringBuilder();
                var stdoutClosed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var stderrClosed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                proc.OutputDataReceived += (s, e) =>
                {
                    if (e.Data != null) lock (sb) sb.AppendLine(e.Data);
                    else stdoutClosed.TrySetResult(true);
                };
                proc.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data != null) lock (sb) sb.AppendLine(e.Data);
                    else stderrClosed.TrySetResult(true);
                };

                try
                {
                    proc.Start();
                }
                catch (Exception ex)
                {
                    result.ExitCode = -1;
                    result.Output = "Không chạy được tiến trình: " + ex.Message;
                    return result;
                }

                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    if (timeoutMs > 0) linked.CancelAfter(timeoutMs);

                    try
                    {
                        // Passing a token to Task.Run does not cancel WaitForExit once running.
                        var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                        proc.Exited += (s, e) => exited.TrySetResult(true);
                        if (proc.HasExited) exited.TrySetResult(true);
                        using (linked.Token.Register(() => exited.TrySetCanceled()))
                            await exited.Task.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        try { if (!proc.HasExited) proc.Kill(); } catch { /* bỏ qua */ }
                        lock (sb) sb.AppendLine("[hết thời gian chờ hoặc đã huỷ]");
                        result.ExitCode = -2;
                        await Task.WhenAny(Task.WhenAll(stdoutClosed.Task, stderrClosed.Task), Task.Delay(2000)).ConfigureAwait(false);
                        lock (sb) result.Output = sb.ToString();
                        return result;
                    }
                }

                // đợi luồng đọc kết thúc
                await Task.WhenAny(Task.WhenAll(stdoutClosed.Task, stderrClosed.Task), Task.Delay(2000)).ConfigureAwait(false);

                result.ExitCode = proc.ExitCode;
                lock (sb) result.Output = sb.ToString();
                return result;
            }
        }
    }
}
