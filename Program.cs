using System;
using System.Windows.Forms;
using MoKhoaBootloader.Core;

namespace MoKhoaBootloader
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                // Tự động giải nén toàn bộ công cụ & binary vào LocalAppData nếu chưa có
                ResourceManager.Initialize();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi giải nén tài nguyên nhúng: " + ex.Message, "Lỗi Khởi Động", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            Application.Run(new MainForm());
        }
    }
}
