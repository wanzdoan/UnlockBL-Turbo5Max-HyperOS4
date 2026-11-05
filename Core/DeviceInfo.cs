using System;
using System.Threading;
using System.Threading.Tasks;

namespace MoKhoaBootloader.Core
{
    public sealed class DeviceInfo
    {
        public string Model { get; set; } = "Không xác định";
        public string DeviceName { get; set; } = "Không xác định";
        public string ChipPlatform { get; set; } = "Không xác định";
        public string RomVersion { get; set; } = "Không xác định";
        public string AndroidVersion { get; set; } = "Không xác định";
        public string CurrentSlot { get; set; } = "a";
        public bool HasRoot { get; set; } = false;
        public string VerifiedBootState { get; set; } = "Không rõ";
        public string FlashLocked { get; set; } = "Không rõ";

        public static async Task<DeviceInfo> ReadAsync(AdbRunner adb, CancellationToken ct = default)
        {
            var info = new DeviceInfo();

            info.Model = await adb.GetPropAsync("ro.product.model", ct);
            if (string.IsNullOrWhiteSpace(info.Model))
                info.Model = await adb.GetPropAsync("ro.product.marketname", ct);

            info.DeviceName = await adb.GetPropAsync("ro.product.device", ct);
            info.ChipPlatform = await adb.GetPropAsync("ro.board.platform", ct);
            info.RomVersion = await adb.GetPropAsync("ro.build.version.incremental", ct);
            info.AndroidVersion = await adb.GetPropAsync("ro.build.version.release", ct);

            string slot = await adb.GetPropAsync("ro.boot.slot_suffix", ct);
            if (string.IsNullOrWhiteSpace(slot))
                slot = await adb.GetPropAsync("ro.boot.current_slot", ct);
            info.CurrentSlot = slot.Replace("_", "").Trim();
            if (string.IsNullOrWhiteSpace(info.CurrentSlot))
                info.CurrentSlot = "a";

            info.VerifiedBootState = await adb.GetPropAsync("ro.boot.verifiedbootstate", ct);
            info.FlashLocked = await adb.GetPropAsync("ro.boot.flash.locked", ct);

            info.HasRoot = await adb.CheckRootAsync(ct);

            return info;
        }

        public string ToSummaryString()
        {
            return $"Thiết bị: {Model} ({DeviceName})\n" +
                   $"Nền tảng SoC: {ChipPlatform}\n" +
                   $"Bản ROM: {RomVersion} (Android {AndroidVersion})\n" +
                   $"Khe cắm đang chạy: Slot _{CurrentSlot}\n" +
                   $"Quyền Root (su): {(HasRoot ? "ĐÃ CÓ (UID 0 - Sẵn sàng)" : "CHƯA CÓ (Cần lấy qua GhostKey)")}\n" +
                   $"Khóa Bootloader (flash.locked): {FlashLocked} | VerifiedBootState: {VerifiedBootState}";
        }
    }
}
