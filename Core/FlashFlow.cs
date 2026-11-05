using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MoKhoaBootloader.Core
{
    public sealed class FlashFlow
    {
        private readonly AdbRunner adb;

        public event Action<string> LogEvent;

        public FlashFlow(AdbRunner runner)
        {
            adb = runner;
        }

        private void Log(string msg) => LogEvent?.Invoke(msg);

        public async Task<bool> BackupCurrentLkAsync(string outputDir, CancellationToken ct = default)
        {
            if (!await adb.CheckRootAsync(ct))
            {
                Log("[Sao lưu LỖI] Chưa xác nhận quyền root UID 0.");
                return false;
            }
            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string[] slots = new string[] { "/dev/block/by-name/lk_a", "/dev/block/by-name/lk_b" };

            foreach (var part in slots)
            {
                string partName = Path.GetFileName(part);
                string remoteDump = $"/data/local/tmp/dump_{partName}.img";
                string localBackup = Path.Combine(outputDir, $"backup_{partName}_{timestamp}.img");

                Log($"[Sao lưu] Đang dump phân vùng {part} ra {remoteDump}...");
                var dumpRes = await adb.SuAsync($"dd if={part} of={remoteDump} bs=4096 conv=fsync && chmod 644 {remoteDump}", 60000, ct);
                if (!dumpRes.Ok)
                {
                    Log($"[Sao lưu LỖI] Không thể dump {part}: {dumpRes.Output}");
                    return false;
                }

                Log($"[Sao lưu] Đang tải {partName} về máy tính ({localBackup})...");
                var pullRes = await adb.PullAsync(remoteDump, localBackup, 120000, ct);
                if (!pullRes.Ok)
                {
                    Log($"[Sao lưu LỖI] Kéo file về máy thất bại: {pullRes.Output}");
                    return false;
                }

                await adb.SuAsync($"rm -f {remoteDump}", 10000, ct);
                Log($"[Sao lưu THÀNH CÔNG] Đã lưu: {localBackup}");
            }

            return true;
        }

        public async Task<bool> FlashPatchedLkAsync(string lkFilePath, CancellationToken ct = default)
        {
            if (!await adb.CheckRootAsync(ct))
            {
                Log("[Nạp LK LỖI] Chưa xác nhận quyền root UID 0.");
                return false;
            }
            Log("=================================================");
            Log("[Nạp LK Đã Vá] BẮT ĐẦU QUY TRÌNH GHI ĐÈ...");
            Log($"[Nạp LK] File nguồn: {lkFilePath}");

            var val = LkImageValidator.Validate(lkFilePath);
            if (!val.isValid)
            {
                Log($"[Nạp LK LỖI] {val.message}");
                return false;
            }
            Log($"[Nạp LK] Checksum SHA256 file nguồn: {val.sha256}");

            string remoteSdcardLk = "/sdcard/lk.bin";
            Log("[Nạp LK] Đang đẩy file lk.bin vào bộ nhớ máy (/sdcard/lk.bin)...");
            var pushRes = await adb.PushAsync(lkFilePath, remoteSdcardLk, 120000, ct);
            if (!pushRes.Ok)
            {
                Log($"[Nạp LK LỖI] Đẩy file vào /sdcard/lk.bin thất bại: {pushRes.Output}");
                return false;
            }

            Log("[Nạp LK] Kiểm tra tính toàn vẹn SHA256 của /sdcard/lk.bin trên thiết bị...");
            var shaRes = await adb.SuAsync($"sha256sum {remoteSdcardLk}", 20000, ct);
            if (!shaRes.Ok || !shaRes.Output.ToUpperInvariant().Contains(val.sha256))
            {
                Log($"[Nạp LK LỖI] Mã băm trên thiết bị không khớp với file nguồn! Hủy lệnh để bảo vệ máy.");
                return false;
            }
            Log("  -> Mã SHA256 trên thiết bị khớp 100%!");

            // 1. Ghi vào lk_a
            Log("[Nạp LK] Thực thi lệnh: dd if=/sdcard/lk.bin of=/dev/block/by-name/lk_a bs=4096");
            var resA = await adb.SuAsync("dd if=/sdcard/lk.bin of=/dev/block/by-name/lk_a bs=4096 conv=fsync", 120000, ct);
            if (!resA.Ok)
            {
                Log($"[Nạp LK LỖI] Ghi vào lk_a thất bại: {resA.Output}");
                return false;
            }
            Log("  -> Ghi thành công vào /dev/block/by-name/lk_a!");

            // 2. Ghi vào lk_b
            Log("[Nạp LK] Thực thi lệnh: dd if=/sdcard/lk.bin of=/dev/block/by-name/lk_b bs=4096");
            var resB = await adb.SuAsync("dd if=/sdcard/lk.bin of=/dev/block/by-name/lk_b bs=4096 conv=fsync", 120000, ct);
            if (!resB.Ok)
            {
                Log($"[Nạp LK LỖI] Ghi vào lk_b thất bại: {resB.Output}");
                return false;
            }
            Log("  -> Ghi thành công vào /dev/block/by-name/lk_b!");

            // Đồng bộ dữ liệu phần cứng
            await adb.SuAsync("sync", 10000, ct);

            // Xác thực đối chiếu cả 2 phân vùng
            Log("[Xác thực] Kiểm tra đối chiếu sau khi ghi...");
            var verifyA = await adb.SuAsync("dd if=/dev/block/by-name/lk_a bs=4096 count=4096 2>/dev/null | sha256sum", 30000, ct);
            var verifyB = await adb.SuAsync("dd if=/dev/block/by-name/lk_b bs=4096 count=4096 2>/dev/null | sha256sum", 30000, ct);

            if (verifyA.Ok && verifyA.Output.ToUpperInvariant().Contains(val.sha256) &&
                verifyB.Ok && verifyB.Output.ToUpperInvariant().Contains(val.sha256))
            {
                Log("  -> Xác thực thành công: Cả 2 slot lk_a và lk_b đều khớp 100% dữ liệu đã vá!");
            }
            else
            {
                Log("[Nạp LK LỖI] Không xác nhận được SHA256 của cả hai slot sau khi ghi.");
                Log("  lk_a: " + verifyA.Output.Trim());
                Log("  lk_b: " + verifyB.Output.Trim());
                return false;
            }

            Log("=================================================");
            Log("[Nạp LK Đã Vá] HOÀN TẤT THÀNH CÔNG RỰC RỠ!");
            Log("Cả hai slot lk_a và lk_b đã được cập nhật bản mở khóa.");
            Log("=================================================");
            return true;
        }

        public async Task RebootToBootloaderAsync(CancellationToken ct = default)
        {
            Log("[Khởi động lại] Đang chuyển thiết bị vào chế độ Fastboot/Bootloader...");
            await adb.RunAdbAsync("reboot bootloader", 20000, ct);
        }

        public async Task WipeDataFastbootAsync(CancellationToken ct = default)
        {
            Log("[Fastboot] Đang thực hiện xóa sạch dữ liệu phân vùng mã hóa (userdata & metadata)...");
            var res1 = await adb.RunFastbootAsync("erase userdata", 60000, ct);
            Log("  -> fastboot erase userdata: " + res1.Output.Trim());

            var res2 = await adb.RunFastbootAsync("erase metadata", 60000, ct);
            Log("  -> fastboot erase metadata: " + res2.Output.Trim());

            Log("[Fastboot] Đang khởi động lại điện thoại...");
            await adb.RunFastbootAsync("reboot", 30000, ct);
        }
    }
}
