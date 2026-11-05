using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MoKhoaBootloader.Core;

namespace MoKhoaBootloader
{
    public sealed class MainForm : Form
    {
        private readonly AdbRunner adb = new AdbRunner();
        private GhostKeyFlow ghostFlow;
        private FlashFlow flashFlow;

        // UI Controls
        private Label lblTitle;
        private Label lblSubtitle;
        private Label lblStatusBadge;

        // Info Card
        private Panel pnlInfoCard;
        private Label lblInfoDetails;

        // Action Card
        private Panel pnlActionCard;
        private TextBox txtLkPath;
        private Button btnBrowseLk;
        private CheckBox chkAutoBackup;

        // Buttons
        private Button btnDetect;
        private Button btnInstallDriver;
        private Button btnGhostKey;
        private Button btnBackup;
        private Button btnFlashLk;
        private Button btnRebootFb;
        private Button btnWipeFastboot;

        // Log Console
        private TextBox txtLog;
        private ProgressBar progressBar;

        public MainForm()
        {
            InitializeComponent();
            ghostFlow = new GhostKeyFlow(adb);
            flashFlow = new FlashFlow(adb);

            adb.LogEvent += AppendLog;
            ghostFlow.LogEvent += AppendLog;
            flashFlow.LogEvent += AppendLog;

            string defaultLk = ResourceManager.GetFilePath("lk.bin");
            if (File.Exists(defaultLk))
            {
                txtLkPath.Text = defaultLk;
            }
        }

        private void InitializeComponent()
        {
            this.Text = "Công Cụ Mở Khóa Bootloader HyperOS 4 (Đầy Đủ ADB & Fastboot)";
            this.Size = new Size(1140, 800);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(243, 246, 250);
            this.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            // Header Panel
            Panel pnlHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 84,
                BackColor = Color.White,
                Padding = new Padding(24, 14, 24, 14)
            };
            this.Controls.Add(pnlHeader);

            lblTitle = new Label
            {
                Text = "Mở Khóa Bootloader HyperOS 4 (MediaTek)",
                Font = new Font("Segoe UI", 16f, FontStyle.Bold),
                ForeColor = Color.FromArgb(20, 30, 45),
                Location = new Point(24, 14),
                AutoSize = true
            };
            pnlHeader.Controls.Add(lblTitle);

            lblSubtitle = new Label
            {
                Text = "Tích hợp sẵn Full ADB, Fastboot, Driver Installer, Exploit CVE-2026-98260 & lk.bin nhúng 100%",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(26, 48),
                AutoSize = true
            };
            pnlHeader.Controls.Add(lblSubtitle);

            lblStatusBadge = new Label
            {
                Text = "Chờ kết nối thiết bị",
                BackColor = Color.FromArgb(224, 242, 254),
                ForeColor = Color.FromArgb(2, 132, 199),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Size = new Size(190, 36),
                TextAlign = ContentAlignment.MiddleCenter,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(pnlHeader.Width - 220, 24)
            };
            pnlHeader.Controls.Add(lblStatusBadge);

            // Left: Device Info Card
            pnlInfoCard = CreateRoundedCard(24, 100, 380, 310);
            this.Controls.Add(pnlInfoCard);

            Label lblInfoTitle = new Label
            {
                Text = "THÔNG TIN THIẾT BỊ",
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42),
                Location = new Point(18, 14),
                AutoSize = true
            };
            pnlInfoCard.Controls.Add(lblInfoTitle);

            lblInfoDetails = new Label
            {
                Text = "Chưa kết nối thiết bị qua ADB.\n\nHướng dẫn kết nối:\n1. Bật Tùy chọn nhà phát triển trên điện thoại.\n2. Bật 'Gỡ lỗi USB' (USB Debugging).\n3. Cắm cáp kết nối và bấm 'Phát hiện thiết bị'.",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
                ForeColor = Color.FromArgb(51, 65, 85),
                Location = new Point(18, 48),
                Size = new Size(344, 185)
            };
            pnlInfoCard.Controls.Add(lblInfoDetails);

            btnDetect = CreateButton("Phát Hiện Thiết Bị", 18, 248, 165, 44, Color.FromArgb(14, 165, 233), Color.White);
            btnDetect.Click += async (s, e) => await OnDetectDeviceClicked();
            pnlInfoCard.Controls.Add(btnDetect);

            btnInstallDriver = CreateButton("Cài Driver Android", 195, 248, 167, 44, Color.FromArgb(71, 85, 105), Color.White);
            btnInstallDriver.Click += (s, e) => OnInstallDriverClicked();
            pnlInfoCard.Controls.Add(btnInstallDriver);

            // Right: Action Control Card
            pnlActionCard = CreateRoundedCard(420, 100, 680, 310);
            this.Controls.Add(pnlActionCard);

            Label lblActionTitle = new Label
            {
                Text = "THAO TÁC MỞ KHÓA BOOTLOADER",
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42),
                Location = new Point(18, 14),
                AutoSize = true
            };
            pnlActionCard.Controls.Add(lblActionTitle);

            Label lblLkSelect = new Label
            {
                Text = "Đường dẫn file lk.bin đã vá (Đã nhúng sẵn trong Exe):",
                Location = new Point(18, 44),
                AutoSize = true
            };
            pnlActionCard.Controls.Add(lblLkSelect);

            txtLkPath = new TextBox
            {
                Location = new Point(18, 68),
                Size = new Size(520, 27),
                ReadOnly = true,
                BackColor = Color.White
            };
            pnlActionCard.Controls.Add(txtLkPath);

            btnBrowseLk = CreateButton("Chọn Khác...", 548, 66, 110, 30, Color.FromArgb(226, 232, 240), Color.FromArgb(15, 23, 42));
            btnBrowseLk.Click += (s, e) =>
            {
                using (OpenFileDialog ofd = new OpenFileDialog())
                {
                    ofd.Filter = "File LK Bin (*.bin;*.img)|*.bin;*.img|Tất cả file (*.*)|*.*";
                    if (ofd.ShowDialog(this) == DialogResult.OK)
                    {
                        txtLkPath.Text = ofd.FileName;
                        var val = LkImageValidator.Validate(ofd.FileName);
                        if (!val.isValid)
                            MessageBox.Show(this, val.message, "Cảnh báo file LK", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        else
                            AppendLog($"[Chọn file] File LK hợp lệ. SHA256: {val.sha256}");
                    }
                }
            };
            pnlActionCard.Controls.Add(btnBrowseLk);

            chkAutoBackup = new CheckBox
            {
                Text = "Tự động sao lưu phân vùng lk_a và lk_b gốc trước khi nạp (Khuyên dùng)",
                Location = new Point(18, 114),
                AutoSize = true,
                Checked = true
            };
            pnlActionCard.Controls.Add(chkAutoBackup);

            // Operation Buttons Row
            btnGhostKey = CreateButton("1. Lấy Temproot Tự Động", 18, 164, 205, 48, Color.FromArgb(99, 102, 241), Color.White);
            btnGhostKey.Click += async (s, e) => await OnGhostKeyClicked();
            pnlActionCard.Controls.Add(btnGhostKey);

            btnBackup = CreateButton("2. Sao Lưu Phân Vùng LK", 233, 164, 205, 48, Color.FromArgb(100, 116, 139), Color.White);
            btnBackup.Click += async (s, e) => await OnBackupClicked();
            pnlActionCard.Controls.Add(btnBackup);

            btnFlashLk = CreateButton("3. Ghi Đè lk.bin Vào lk_a & lk_b", 448, 164, 215, 48, Color.FromArgb(16, 185, 129), Color.White);
            btnFlashLk.Click += async (s, e) => await OnFlashLkClicked();
            pnlActionCard.Controls.Add(btnFlashLk);

            btnRebootFb = CreateButton("Khởi động vào Fastboot", 18, 232, 310, 44, Color.FromArgb(241, 245, 249), Color.FromArgb(30, 41, 59));
            btnRebootFb.Click += async (s, e) => await OnRebootFastbootClicked();
            pnlActionCard.Controls.Add(btnRebootFb);

            btnWipeFastboot = CreateButton("Xóa Userdata & Khởi động lại (Fastboot)", 338, 232, 325, 44, Color.FromArgb(239, 68, 68), Color.White);
            btnWipeFastboot.Click += async (s, e) => await OnWipeFastbootClicked();
            pnlActionCard.Controls.Add(btnWipeFastboot);

            // Bottom: Log Console
            Panel pnlLog = CreateRoundedCard(24, 424, 1076, 316);
            this.Controls.Add(pnlLog);

            Label lblLogHeader = new Label
            {
                Text = "NHẬT KÝ HOẠT ĐỘNG (LOG CONSOLE)",
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = Color.FromArgb(203, 213, 225),
                Location = new Point(18, 12),
                AutoSize = true
            };
            pnlLog.BackColor = Color.FromArgb(15, 23, 42);
            pnlLog.Controls.Add(lblLogHeader);

            txtLog = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.FromArgb(241, 245, 249),
                BorderStyle = BorderStyle.None,
                Font = new Font("Consolas", 9.5f),
                Location = new Point(18, 38),
                Size = new Size(1040, 260)
            };
            pnlLog.Controls.Add(txtLog);

            progressBar = new ProgressBar
            {
                Dock = DockStyle.Bottom,
                Height = 8,
                Style = ProgressBarStyle.Continuous,
                Value = 0
            };
            this.Controls.Add(progressBar);
        }

        private Panel CreateRoundedCard(int x, int y, int w, int h)
        {
            return new Panel
            {
                Location = new Point(x, y),
                Size = new Size(w, h),
                BackColor = Color.White,
                Padding = new Padding(16)
            };
        }

        private Button CreateButton(string text, int x, int y, int w, int h, Color bg, Color fg)
        {
            Button btn = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, h),
                BackColor = bg,
                ForeColor = fg,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            return btn;
        }

        private void SetStatus(string text, Color bg, Color fg)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => SetStatus(text, bg, fg)));
                return;
            }
            lblStatusBadge.Text = text;
            lblStatusBadge.BackColor = bg;
            lblStatusBadge.ForeColor = fg;
        }

        private void AppendLog(string message)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => AppendLog(message)));
                return;
            }
            txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\r\n");
            txtLog.SelectionStart = txtLog.Text.Length;
            txtLog.ScrollToCaret();
        }

        private void OnInstallDriverClicked()
        {
            try
            {
                string installer = ResourceManager.GetFilePath("android_driver_installer.exe");
                if (File.Exists(installer))
                {
                    AppendLog("Đang mở trình cài đặt Driver Android...");
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = installer,
                        UseShellExecute = true
                    });
                }
                else
                {
                    MessageBox.Show(this, "Không tìm thấy android_driver_installer.exe!", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                AppendLog("Lỗi mở driver installer: " + ex.Message);
            }
        }

        private async Task OnDetectDeviceClicked()
        {
            try
            {
                btnDetect.Enabled = false;
                SetStatus("Đang kiểm tra...", Color.FromArgb(254, 243, 199), Color.FromArgb(180, 83, 9));
                AppendLog("Bắt đầu kiểm tra thiết bị qua ADB...");

                var devices = await adb.RunAdbAsync("devices");
                AppendLog(devices.Output.Trim());

                var info = await DeviceInfo.ReadAsync(adb);
                lblInfoDetails.Text = info.ToSummaryString();

                if (info.HasRoot)
                {
                    SetStatus("Đã có Root (UID 0)", Color.FromArgb(220, 252, 231), Color.FromArgb(22, 101, 52));
                    AppendLog("Phát hiện thiết bị đã có quyền Root (su). Có thể tiến hành ghi đè LK ngay!");
                }
                else
                {
                    SetStatus("Chưa có Root", Color.FromArgb(254, 226, 226), Color.FromArgb(185, 28, 28));
                    AppendLog("Thiết bị chưa có Root. Hãy bấm nút '1. Lấy Temproot Tự Động'.");
                }
            }
            catch (Exception ex)
            {
                AppendLog("Lỗi kiểm tra thiết bị: " + ex.Message);
            }
            finally
            {
                btnDetect.Enabled = true;
            }
        }

        private async Task OnGhostKeyClicked()
        {
            try
            {
                btnGhostKey.Enabled = false;
                btnDetect.Enabled = btnBackup.Enabled = btnFlashLk.Enabled = btnRebootFb.Enabled = btnWipeFastboot.Enabled = false;
                SetStatus("Đang chạy Temproot...", Color.FromArgb(238, 242, 255), Color.FromArgb(79, 70, 229));

                bool ok = await ghostFlow.RunNativeTempRootAsync();
                if (ok)
                {
                    SetStatus("Đã có Root (UID 0)!", Color.FromArgb(220, 252, 231), Color.FromArgb(22, 101, 52));
                    MessageBox.Show(this, "Đã chiếm quyền Root (su) thành công trên thiết bị!\nBây giờ bạn có thể sao lưu và ghi đè lk.bin vào cả 2 slot.", "Thành công", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    await OnDetectDeviceClicked();
                }
                else
                {
                    SetStatus("Chưa lấy được Root", Color.FromArgb(254, 226, 226), Color.FromArgb(185, 28, 28));
                }
            }
            catch (Exception ex)
            {
                AppendLog("Lỗi quy trình Temproot: " + ex.Message);
            }
            finally
            {
                btnGhostKey.Enabled = true;
                btnDetect.Enabled = btnBackup.Enabled = btnFlashLk.Enabled = btnRebootFb.Enabled = btnWipeFastboot.Enabled = true;
            }
        }

        private async Task OnBackupClicked()
        {
            try
            {
                btnBackup.Enabled = false;
                SetStatus("Đang sao lưu LK...", Color.FromArgb(254, 243, 199), Color.FromArgb(180, 83, 9));

                string backupDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backup");
                bool ok = await flashFlow.BackupCurrentLkAsync(backupDir);
                if (ok)
                {
                    SetStatus("Sao lưu thành công", Color.FromArgb(220, 252, 231), Color.FromArgb(22, 101, 52));
                    MessageBox.Show(this, $"Đã sao lưu phân vùng lk_a và lk_b thành công vào thư mục:\n{backupDir}", "Sao lưu hoàn tất", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    SetStatus("Sao lưu thất bại", Color.FromArgb(254, 226, 226), Color.FromArgb(185, 28, 28));
                }
            }
            catch (Exception ex)
            {
                AppendLog("Lỗi sao lưu: " + ex.Message);
            }
            finally
            {
                btnBackup.Enabled = true;
            }
        }

        private async Task OnFlashLkClicked()
        {
            string lkPath = txtLkPath.Text.Trim();
            if (!File.Exists(lkPath))
            {
                MessageBox.Show(this, "Vui lòng chọn file lk.bin đã vá hợp lệ trước!", "Chưa chọn file", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var confirm = MessageBox.Show(this,
                "BẠN CÓ CHẮC CHẮN MUỐN NẠP lk.bin VÀO lk_a VÀ lk_b?\n\n" +
                "Lệnh thực thi:\n" +
                "  1. dd if=/sdcard/lk.bin of=/dev/block/by-name/lk_a bs=4096\n" +
                "  2. dd if=/sdcard/lk.bin of=/dev/block/by-name/lk_b bs=4096\n\n" +
                "Tool kiểm tra SHA256 trước và sau khi ghi.\n" +
                "Tiếp tục thực hiện?",
                "Xác nhận ghi đè Bootloader",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes) return;

            try
            {
                btnFlashLk.Enabled = false;
                SetStatus("Đang nạp lk.bin...", Color.FromArgb(254, 243, 199), Color.FromArgb(180, 83, 9));

                if (chkAutoBackup.Checked)
                {
                    AppendLog("[Tự động sao lưu] Đang tiến hành sao lưu lk_a và lk_b trước khi ghi đè...");
                    string backupDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backup");
                    if (!await flashFlow.BackupCurrentLkAsync(backupDir))
                    {
                        SetStatus("Sao lưu thất bại — đã dừng nạp LK", Color.FromArgb(254, 226, 226), Color.FromArgb(185, 28, 28));
                        return;
                    }
                }

                bool ok = await flashFlow.FlashPatchedLkAsync(lkPath);
                if (ok)
                {
                    SetStatus("Mở Khóa LK Xong!", Color.FromArgb(220, 252, 231), Color.FromArgb(22, 101, 52));
                    MessageBox.Show(this,
                        "ĐÃ GHI ĐÈ lk.bin VÀO lk_a VÀ lk_b THÀNH CÔNG!\n\n" +
                        "Phân vùng Bootloader của máy bạn đã được nạp bản mở khóa.\n" +
                        "Tiếp theo, bạn có thể khởi động vào Fastboot để kiểm tra trạng thái unlock hoặc xóa dữ liệu.",
                        "Ghi đè thành công",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                else
                {
                    SetStatus("Ghi đè thất bại", Color.FromArgb(254, 226, 226), Color.FromArgb(185, 28, 28));
                }
            }
            catch (Exception ex)
            {
                AppendLog("Lỗi nạp LK: " + ex.Message);
            }
            finally
            {
                btnFlashLk.Enabled = true;
            }
        }

        private async Task OnRebootFastbootClicked()
        {
            try
            {
                AppendLog("Khởi động máy vào Bootloader/Fastboot...");
                await flashFlow.RebootToBootloaderAsync();
            }
            catch (Exception ex)
            {
                AppendLog("Lỗi reboot bootloader: " + ex.Message);
            }
        }

        private async Task OnWipeFastbootClicked()
        {
            var confirm = MessageBox.Show(this,
                "CẢNH BÁO: Thao tác này sẽ XÓA SẠCH DỮ LIỆU (erase userdata & metadata) trên điện thoại qua Fastboot và khởi động lại máy.\n\nTiếp tục?",
                "Xác nhận xóa dữ liệu",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (confirm != DialogResult.Yes) return;

            try
            {
                SetStatus("Đang xóa dữ liệu FB...", Color.FromArgb(254, 243, 199), Color.FromArgb(180, 83, 9));
                await flashFlow.WipeDataFastbootAsync();
                SetStatus("Đã gửi lệnh xóa", Color.FromArgb(220, 252, 231), Color.FromArgb(22, 101, 52));
            }
            catch (Exception ex)
            {
                AppendLog("Lỗi Fastboot wipe: " + ex.Message);
            }
        }
    }
}
