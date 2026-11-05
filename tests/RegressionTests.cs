using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MoKhoaBootloader.Core;

internal static class RegressionTests
{
    private static int passed;
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception("FAIL: " + label);
        Console.WriteLine("PASS: " + label);
        passed++;
    }

    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "echo")
        {
            Console.WriteLine(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(args[1])));
            return 0;
        }
        if (args.Length > 0 && args[0] == "sleep")
        {
            Console.WriteLine("started");
            Thread.Sleep(30000);
            return 0;
        }
        try { RunAsync().GetAwaiter().GetResult(); Console.WriteLine($"{passed} checks passed."); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static async Task RunAsync()
    {
        Check(NativeProtocol.IsRoot("uid=0(root) gid=0(root)\n"), "accept real root id");
        Check(!NativeProtocol.IsRoot("uid=2000(shell) groups=0(root)\nnot uid=0(root)"), "reject misleading root text");
        Check(NativeProtocol.IsHex("0x0") && NativeProtocol.IsHex("0xffffff7f80000000"), "accept strict uint64 hex including zero slide");
        Check(!NativeProtocol.IsHex("cat: no such file") && !NativeProtocol.IsHex("0x1;id") && !NativeProtocol.IsHex("0x10000000000000000"), "reject invalid calibration results");
        Check(NativeProtocol.BuildSuWrapper("/tmp/native") == "#!/system/bin/sh\nexec '/tmp/native' su \"$@\"\n", "wrapper has LF, exec, quoted argument forwarding");
        ResourceManager.Initialize();
        string release = NativeProtocol.ReadProfileRelease(ResourceManager.GetFilePath("profile.blob"));
        Check(release == FakeAdb.Release, "read existing length-prefixed v4 profile");
        string cachedProfile = ResourceManager.GetFilePath("profile.blob");
        byte[] originalProfile = File.ReadAllBytes(cachedProfile);
        try
        {
            byte[] changed = (byte[])originalProfile.Clone();
            changed[changed.Length - 1] ^= 1;
            File.WriteAllBytes(cachedProfile, changed);
            ResourceManager.Initialize();
            Check(File.ReadAllBytes(cachedProfile).SequenceEqual(originalProfile), "refresh cached resource even when length is unchanged");
        }
        finally { File.WriteAllBytes(cachedProfile, originalProfile); }
        Check(LkImageValidator.Validate(ResourceManager.GetFilePath("lk.bin")).isValid, "existing LK file passes validation");
        string badBlob = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(badBlob, new byte[312]);
            bool rejected = false;
            try { NativeProtocol.ReadProfileRelease(badBlob); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "reject malformed profile");
            File.WriteAllBytes(badBlob, new byte[4096]);
            Check(!LkImageValidator.Validate(badBlob).isValid, "reject aligned but wrong LK size");
            File.WriteAllBytes(badBlob, new byte[0]);
            Check(!LkImageValidator.Validate(badBlob).isValid, "reject empty LK without throwing");
        }
        finally { File.Delete(badBlob); }

        string self = Process.GetCurrentProcess().MainModule.FileName;
        string[] values = { "a b", "echo \"$@\"", "a\\\"b", "C:\\directory with spaces\\", "", "sh -c 'printf '\\''%s\\n'\\'' \"$rc\"'" };
        foreach (string value in values)
        {
            var echo = await ProcessRunner.RunAsync(self, "echo " + ProcessRunner.QuoteArgument(value), 5000, CancellationToken.None);
            string roundtrip = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(echo.Output.Trim()));
            Check(echo.Ok && roundtrip == value, "Windows argv roundtrip: " + value);
        }
        var watch = Stopwatch.StartNew();
        var timed = await ProcessRunner.RunAsync(self, "sleep", 250, CancellationToken.None);
        Check(timed.ExitCode == -2 && watch.Elapsed.TotalSeconds < 5 && timed.Output.Contains("started"), "timeout interrupts an already running process and preserves output");
        using (var cancel = new CancellationTokenSource(250))
        {
            watch.Restart();
            var stopped = await ProcessRunner.RunAsync(self, "sleep", 0, cancel.Token);
            Check(stopped.ExitCode == -2 && watch.Elapsed.TotalSeconds < 5, "cancellation without a timeout");
        }

        var happy = new FakeAdb();
        Check(await NewFlow(happy).RunNativeTempRootAsync(), "full temproot orchestration against fake ADB");
        Check(happy.Started.SequenceEqual(new[] { "calib-a", "calib-b", "inject" }), "GhostKey stage order");
        Check(happy.WrapperPushed && happy.Commands.Any(x => x.Contains("PLANT_FAST=1")), "wrapper installed after verified root and GhostKey environment supplied");
        Check(!happy.Commands.Any(x => x.Contains("kill") || x.Contains("dd if=") || x.Contains("fastboot")), "temproot never kills native or flashes partitions");
        await CheckShellContractAsync(happy.Commands.First(x => x.Contains("nohup sh -c")));
        var reused = new FakeAdb { AlreadyRoot = true };
        Check(await NewFlow(reused).RunNativeTempRootAsync() && reused.Started.Count == 0, "reuse existing root");
        await Reject(new FakeAdb { Kernel = "different-kernel" }, "kernel mismatch", false);
        await Reject(new FakeAdb { Seccomp = "2" }, "filtered shell", false);
        await Reject(new FakeAdb { Residual = true }, "previous native holder", false);
        await Reject(new FakeAdb { FailPush = true }, "push failure", false);
        await Reject(new FakeAdb { BadSlide = true }, "malformed slide", false);
        await Reject(new FakeAdb { CalibAExit = 2 }, "failed calibration A", false);
        await Reject(new FakeAdb { MissingPageOffset = true }, "missing page offset has no hardcoded fallback", false);
        var fallback = new FakeAdb { CalibBExit = 1 };
        Check(await NewFlow(fallback).RunNativeTempRootAsync(), "accept native profile fallback from calibration B");
        var detached = new FakeAdb { DelayedRootd = true };
        Check(await NewFlow(detached).RunNativeTempRootAsync() && detached.InjectLogReads >= 3,
            "wait for detached child rootd after native parent exits 0");
        await Reject(new FakeAdb { FailedInject = true }, "injection failure", true);
        await Reject(new FakeAdb { NeverRoot = true }, "rootd ready without UID 0", true);
        // First verify is misleading text; second succeeds, exercising the retry rather than a false success.
        var retry = new FakeAdb { MisleadingFirstId = true };
        Check(await NewFlow(retry).RunNativeTempRootAsync() && retry.VerifyCount == 2, "retry and reject embedded uid=0 text");
        var pending = new FakeAdb { PendingCalibration = true };
        var flow = NewFlow(pending);
        using (var cancel = new CancellationTokenSource(200))
        {
            Task<bool> first = flow.RunNativeTempRootAsync(cancel.Token);
            Check(!await flow.RunNativeTempRootAsync(), "prevent overlapping runs");
            bool canceled = false;
            try { await first; } catch (OperationCanceledException) { canceled = true; }
            Check(canceled && !pending.Commands.Any(x => x.Contains("kill")), "cancel monitoring without killing native");
        }
    }

    private static async Task CheckShellContractAsync(string launch)
    {
        string bash = @"C:\Program Files\Git\bin\bash.exe";
        if (!File.Exists(bash)) throw new Exception("Git Bash is required for the POSIX shell contract regression.");
        string dir = Path.Combine(Path.GetTempPath(), "UnlockBL-contract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string unix = "/" + char.ToLowerInvariant(dir[0]) + dir.Substring(2).Replace('\\', '/');
        try
        {
            // Harmless script stand-in: never execute the actual Android native binary on the host.
            File.WriteAllText(Path.Combine(dir, "libcve98260.so"),
                "#!/bin/sh\n/usr/bin/cat > \"$CVE98260_HOME/received.blob\"\nprintf 'stage=%s\\n' \"$1\"\nexit 7\n", new System.Text.UTF8Encoding(false));
            byte[] profile = File.ReadAllBytes(ResourceManager.GetFilePath("profile.blob"));
            File.WriteAllBytes(Path.Combine(dir, "profile.blob"), profile);
            var executable = await ProcessRunner.RunAsync(bash, "-c " + ProcessRunner.QuoteArgument("chmod 755 " + NativeProtocol.QuoteShell(unix + "/libcve98260.so")), 5000, CancellationToken.None);
            Check(executable.Ok, "prepare harmless shell stand-in");
            var started = await ProcessRunner.RunAsync(bash, "-c " + ProcessRunner.QuoteArgument(launch.Replace("/data/local/tmp/cve98260", unix)), 5000, CancellationToken.None);
            Check(started.Ok, "detached stage shell syntax and Windows quoting execute correctly");
            var watch = Stopwatch.StartNew();
            string marker = Path.Combine(dir, "calib-a.exit");
            while (!File.Exists(marker) && watch.Elapsed.TotalSeconds < 5) await Task.Delay(50);
            Check(File.Exists(marker) && File.ReadAllText(marker).Trim() == "7", "preserve native exit code in completion file");
            Check(File.ReadAllBytes(Path.Combine(dir, "received.blob")).SequenceEqual(profile), "shell redirects exact length-prefixed profile to native stdin");
            Check(File.ReadAllText(Path.Combine(dir, "calib-a.log")).Contains("stage=calibrate-a"), "shell writes detached native output to stage log");

            string fake = unix + "/argument-check.sh";
            File.WriteAllText(Path.Combine(dir, "argument-check.sh"), "#!/bin/sh\nprintf '%s\\n' \"$#\" \"$1\" \"$2\" \"$3\"\n", new System.Text.UTF8Encoding(false));
            File.WriteAllText(Path.Combine(dir, "su"), NativeProtocol.BuildSuWrapper(fake), new System.Text.UTF8Encoding(false));
            string command = "chmod 755 " + NativeProtocol.QuoteShell(fake) + "; /bin/sh " + NativeProtocol.QuoteShell(unix + "/su") + " -c " + NativeProtocol.QuoteShell("echo \"quoted words\"; echo '$HOME'");
            var wrapper = await ProcessRunner.RunAsync(bash, "-c " + ProcessRunner.QuoteArgument(command), 5000, CancellationToken.None);
            Check(wrapper.Ok && wrapper.Output.Replace("\r", "") == "3\nsu\n-c\necho \"quoted words\"; echo '$HOME'\n", "su wrapper forwards the full command unchanged through a real POSIX shell");
        }
        finally
        {
            // Every file is test-owned and the directory is a newly allocated explicit temp child.
            foreach (string path in Directory.GetFiles(dir)) File.Delete(path);
            Directory.Delete(dir);
        }
    }

    private static GhostKeyFlow NewFlow(IAdbShell adb) => new GhostKeyFlow(adb, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-logs"));

    private static async Task Reject(FakeAdb adb, string label, bool injectionExpected)
    {
        Check(!await NewFlow(adb).RunNativeTempRootAsync(), "stop on " + label);
        Check(adb.Started.Contains("inject") == injectionExpected, "injection boundary: " + label);
    }

    private sealed class FakeAdb : IAdbShell
    {
        public const string Release = "6.6.118-android15-8-gb9cc6ec16bc8-abogki536571621-4k";
        public string Kernel = Release, Seccomp = "0";
        public bool AlreadyRoot, Residual, FailPush, BadSlide, MissingPageOffset, FailedInject, PendingCalibration, MisleadingFirstId, DelayedRootd, NeverRoot;
        public int CalibAExit, CalibBExit, VerifyCount, InjectLogReads;
        public bool WrapperPushed;
        public readonly List<string> Commands = new List<string>();
        public readonly List<string> Started = new List<string>();
        private bool verified;
        private static CmdResult Result(string text = "", int exit = 0) => new CmdResult { ExitCode = exit, Output = text };
        public Task<bool> CheckRootAsync(CancellationToken ct = default) => Task.FromResult(AlreadyRoot || verified && WrapperPushed);
        public Task<CmdResult> PushAsync(string localPath, string remotePath, int timeoutMs = 120000, CancellationToken ct = default)
        {
            if (remotePath == "/data/local/tmp/su")
            {
                WrapperPushed = File.ReadAllText(localPath) == NativeProtocol.BuildSuWrapper("/data/local/tmp/cve98260/libcve98260.so");
            }
            return Task.FromResult(Result(FailPush ? "push failed" : "", FailPush ? 1 : 0));
        }
        public Task<CmdResult> ShellAsync(string cmd, int timeoutMs = 60000, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Commands.Add(cmd);
            CmdResult result;
            if (cmd == "id -u") result = Result("2000");
            else if (cmd == "uname -r") result = Result(Kernel);
            else if (cmd == "uname -m") result = Result("aarch64");
            else if (cmd == "getconf PAGESIZE") result = Result("4096");
            else if (cmd == "cat /proc/self/status") result = Result("Name:\tcat\nSeccomp:\t" + Seccomp + "\n");
            else if (cmd.StartsWith("pidof ")) result = Result(Residual ? "123" : "", Residual ? 0 : 1);
            else if (cmd.Contains("nohup sh -c"))
            {
                Started.Add(cmd.Contains("calibrate-a") ? "calib-a" : cmd.Contains("calibrate-b") ? "calib-b" : "inject");
                result = Result();
            }
            else if (cmd.StartsWith("if [ -f ") && cmd.Contains(".log"))
            {
                bool inject = cmd.Contains("inject.log");
                if (inject) InjectLogReads++;
                result = Result(inject ? (FailedInject ? "cred escalation not effective" : DelayedRootd && InjectLogReads < 3 ? "main process exiting, child becomes daemon" : "C98260_ROOTD_READY=1") : "calibration log");
            }
            else if (cmd.StartsWith("if [ -f ") && cmd.Contains(".exit"))
                result = Result(PendingCalibration ? "" : cmd.Contains("calib-a") ? CalibAExit.ToString() : CalibBExit.ToString());
            else if (cmd.EndsWith("/slide.txt")) result = Result(BadSlide ? "cat: error" : "0x0");
            else if (cmd.EndsWith("/page_offset.txt")) result = Result(MissingPageOffset ? "cat: missing" : "0xffffff7f80000000", MissingPageOffset ? 1 : 0);
            else if (cmd.EndsWith("/page_offset.score")) result = Result(CalibBExit == 0 ? "99" : "fallback");
            else if (cmd == "cat /proc/loadavg") result = Result("1.0 1.0 1.0 2/1000 123");
            else if (cmd.EndsWith(" su -c id"))
            {
                VerifyCount++;
                verified = !NeverRoot && (!MisleadingFirstId || VerifyCount > 1);
                result = Result(verified ? "uid=0(root) gid=0(root)" : "uid=2000(shell)\n[debug] expected uid=0(root)");
            }
            else if (cmd.StartsWith("mkdir -p ") || cmd.StartsWith("chmod 755 ")) result = Result();
            else throw new Exception("Unexpected fake ADB command: " + cmd);
            return Task.FromResult(result);
        }
    }
}
