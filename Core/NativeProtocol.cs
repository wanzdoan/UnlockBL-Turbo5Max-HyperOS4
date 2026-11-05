using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace MoKhoaBootloader.Core
{
    public static class NativeProtocol
    {
        public static bool IsHex(string value) =>
            value != null && Regex.IsMatch(value, @"\A0x[0-9a-fA-F]{1,16}\z");

        public static bool IsRoot(string output) =>
            Regex.IsMatch(output ?? "", @"(?m)^uid=0(?:\(|\s|$)");

        public static string QuoteShell(string value) => "'" + value.Replace("'", "'\\''") + "'";

        public static string BuildSuWrapper(string binary) =>
            "#!/system/bin/sh\nexec " + QuoteShell(binary) + " su \"$@\"\n";

        // On disk the payload has the same big-endian length prefix as writeBlob in GhostKey.
        public static string ReadProfileRelease(string path)
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length != 312 || bytes[0] != 0 || bytes[1] != 0 ||
                bytes[2] != 1 || bytes[3] != 52 || Encoding.ASCII.GetString(bytes, 4, 4) != "C982" ||
                bytes[8] != 4 || bytes[9] != 0)
                throw new InvalidDataException("profile.blob không đúng định dạng v4 (308 byte + tiền tố độ dài).");
            string release = Encoding.ASCII.GetString(bytes, 92, 128).Split('\0')[0];
            if (string.IsNullOrWhiteSpace(release)) throw new InvalidDataException("Profile thiếu kernel release.");
            return release;
        }
    }
}
