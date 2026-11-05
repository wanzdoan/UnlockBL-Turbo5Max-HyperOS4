# UnlockBL-VN — sửa luồng temproot theo GhostKey

Người dùng đã xác nhận temproot thành công trên thiết bị ngày 05/11/2026.

Bản EXE đã build được giữ tại `MoKhoaBootloader.exe` trên máy làm việc và không commit vào Git. Khi clone repo, build để lấy `bin/Release/net48/MoKhoaBootloader.exe`. Source chính ở `Core/GhostKeyFlow.cs`.

## Luồng hiện tại

1. Kiểm tra ADB UID shell/root, dùng lại root nếu đã có.
2. Kiểm tra kernel release khớp profile, ARM64, page size 4096, Seccomp=0. Chặn chạy lại khi còn native từ lần trước.
3. Đẩy nguyên binary và profile, kiểm tra kết quả push/chmod, xóa kết quả calibration cũ.
4. `calibrate-a` (180 giây) → đọc `slide.txt` hợp lệ.
5. `calibrate-b --slide ...` (240 giây) → đọc `page_offset.txt` và `page_offset.score`. Chỉ chấp nhận fallback do native ghi từ profile.
6. Chờ tải máy giảm tối đa 24 giây, chạy `exploit --slide ... --page-offset ... --daemon-exit`, chờ đúng marker `C98260_ROOTD_READY=1` tối đa 300 giây. Native cha thoát 0 chưa đồng nghĩa rootd con đã sẵn sàng.
7. Xác minh `su -c id` tối đa 8 lần, mỗi lần 30 giây. Chỉ xác nhận thành công khi exit=0 và dòng id có UID 0.
8. Đẩy wrapper su đúng LF/UTF-8 không BOM, kiểm tra lại kênh su được phần sao lưu/nạp LK sử dụng.

Đây là phần temproot của luồng GhostKey. Bước KernelSU takeover tùy chọn của APK chưa được port vào bản PC; bản PC sử dụng rootd để thực hiện lệnh root. Không thay đổi native exploit hoặc thuật toán offsets.

## File giữ nguyên

- `data/dash_boot_OS_4.0.3.img`: SHA256 `B9991F92CAA9450B089465E83AF9E792CA3856540AC4227A138F0A3BB6FC8C32`
- `data/lk.bin`: SHA256 `DCF472B97DC8A86D4796377BACB3A20CE894C4EE6BD4D5B6C068D66E4A62BD40`
- `data/libcve98260.so`: SHA256 `CD1CE024BAEEE34DBFCE4E233D5A03CC2F54355F872686C22B4038E022C63518`
- `data/profile.blob`: SHA256 `BC85CFFA2FF5891403C163086789B451DBF1E9FC30F5E67252B3D92F8245678B`

Boot và LK trong `data` trùng SHA256 bản ở `Unlock Bootloader HyperOS4`. Binary trùng byte với `lib/arm64-v8a/libcve98260.so` trong APK GhostKey 1.1.4 đi kèm.

Boot hiện có được giải nén đọc lại, kernel release là:

```text
6.6.118-android15-8-gb9cc6ec16bc8-abogki536571621-4k
```

Release này khớp profile v4 hiện có. Profile chứa payload 308 byte và tiền tố độ dài big-endian 4 byte, đúng giao thức stdin trong `KernelProfileSerializer.kt` và `kernel_table.c`. Boot không được flash trong bước temproot: native nhận profile đã đóng gói từ boot. Kiểm tra release/định dạng không thay thế việc xác nhận toàn bộ offsets trên thiết bị thực tế.

## Các lỗi đã sửa

- Timeout/hủy trước đây chỉ truyền token vào `Task.Run`, không ngắt được `WaitForExit` đang chạy.
- Escape Windows argv và shell có thể làm hỏng lệnh chứa dấu nháy hoặc backslash.
- Nội dung lỗi `cat` có thể bị dùng nhầm làm slide/page_offset; kết quả cũ không được xóa; push profile bị bỏ qua lỗi.
- Chờ rootd quá ngắn, nhận `rootd forked` là sẵn sàng; wrapper su bị sai xuống dòng và escape `$@`.
- Giờ calibration và injection chạy tách nền, lưu log/mã thoát. Dừng theo dõi không gửi kill cho native trên điện thoại, theo yêu cầu giữ page holder trong source GhostKey.
- Cache tài nguyên trước đây chỉ so kích thước; giờ so SHA256 để không dùng binary/profile cũ cùng dung lượng.
- Chọn kênh su bằng lệnh `id` trước, không tự chạy lại một lệnh ghi thất bại trên nhiều kênh root.
- Chặn thao tác sao lưu/nạp LK/reboot/wipe khi temproot đang chạy.
- Nạp LK dừng khi auto-backup thất bại, yêu cầu root, từ chối LK sai kích thước 16 MiB và không báo thành công nếu SHA256 sau ghi không khớp.

## Nguồn đối chiếu

- `GhostKey_src/app/src/main/kotlin/com/GhostKey/app/shizuku/Cve98260UserService.kt`: stage order, timeout, môi trường, xác minh, yêu cầu không kill native còn giữ trang nhớ.
- `GhostKey_src/app/src/main/cpp/main.c`, `calib.c`: giao thức subcommand, file kết quả và fallback calibration B.
- `GhostKey_src/app/src/main/cpp/uaf_plant.c`: `--daemon-exit` trả về trước khi tiến trình con hoàn tất khởi tạo.
- `decompiled/PreloadTool/MainForm.cs`: dùng luồng cloud/preload và `LD_PRELOAD`, không cùng hợp đồng CLI với GhostKey; không trộn hai cơ chế temproot.
- `Unlock Bootloader HyperOS4/Cachdung.txt`: hướng dẫn ghi LK của bộ file tham chiếu.

## Build và kiểm tra

Yêu cầu Windows, .NET SDK có khả năng build target .NET Framework 4.8 và Git LFS. Sau khi clone repo:

```powershell
git lfs install --local
git lfs pull
```

Repo chứa source, tests, profile và các binary được nhúng trong project; boot image được giữ làm đầu vào tham chiếu. APK và ksud trùng bản tham chiếu được chuyển khỏi `data` vì project hiện tại không sử dụng chúng.

```powershell
dotnet build .\MoKhoaBootloader.csproj -c Release
dotnet build .\tests\RegressionTests.csproj -c Release
.\tests\bin\Release\net48\RegressionTests.exe
```

53 kiểm tra đã qua: timeout/hủy tiến trình thật, Windows argv roundtrip, POSIX shell với script thay thế vô hại, truyền profile nguyên byte qua stdin, mã thoát/log, wrapper su, ADB giả lập cho các nhánh thành công/thất bại, parent thoát trước rootd, ngăn thành công giả, cache và LK validator. Log giả lập lưu riêng trong thư mục `test-logs` của test executable.

Kiểm thử tự động sử dụng ADB giả lập và script native thay thế vô hại. Sau bản sửa, người dùng đã xác nhận temproot thành công trên điện thoại. Kết quả này không xác nhận mọi model/kernel hoặc kết quả ghi LK/xóa dữ liệu.

## Log lần chạy thực tế

Tool ghi đường dẫn log vào ô nhật ký ngay khi bắt đầu:

```text
%LOCALAPPDATA%\MoKhoaBootloader_HyperOS4\logs\temproot-yyyyMMdd-HHmmss-fff.log
```

Native log trên điện thoại: `/data/local/tmp/cve98260/calib-a.log`, `calib-b.log`, `inject.log`. Nếu thất bại sau khi native đã chạy, giữ log và khởi động lại điện thoại trước lần thử mới, theo cách xử lý residual của GhostKey.
