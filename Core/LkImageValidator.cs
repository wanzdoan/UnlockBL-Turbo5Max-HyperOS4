using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MoKhoaBootloader.Core
{
    public sealed class LkImageValidator
    {
        public static (bool isValid, string message, string sha256) Validate(string filePath)
        {
            if (!File.Exists(filePath))
                return (false, "File LK không tồn tại: " + filePath, string.Empty);

            FileInfo fi = new FileInfo(filePath);
            if (fi.Length != 16777216)
            {
                return (false, $"Kích thước file không hợp lệ ({fi.Length} bytes). Phân vùng LK MTK phải đúng 16MB (16777216 bytes).", string.Empty);
            }

            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var br = new BinaryReader(fs))
            {
                uint magic = br.ReadUInt32();
                // 0x58881688 MTK header magic
                if (magic != 0x58881688)
                {
                    return (false, $"Header file không phải định dạng MTK hợp lệ (Magic: 0x{magic:X8}, yêu cầu 0x58881688).", string.Empty);
                }

                uint dsize = br.ReadUInt32();
                byte[] nameBytes = br.ReadBytes(32);
                string imgName = Encoding.ASCII.GetString(nameBytes).TrimEnd('\0');

                if (!imgName.StartsWith("lk", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, $"Tên phân vùng trong header file là '{imgName}', không khớp với phân vùng 'lk'.", string.Empty);
                }
            }

            string hash;
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(filePath))
            {
                byte[] hashBytes = sha.ComputeHash(stream);
                StringBuilder sb = new StringBuilder();
                foreach (byte b in hashBytes) sb.Append(b.ToString("X2"));
                hash = sb.ToString();
            }

            return (true, "File LK MediaTek hợp lệ.", hash);
        }
    }
}
