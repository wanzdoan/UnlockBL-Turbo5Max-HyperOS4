using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;

namespace MoKhoaBootloader.Core
{
    public static class ResourceManager
    {
        public static string ToolsDirectory { get; private set; }

        public static void Initialize()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            ToolsDirectory = Path.Combine(localAppData, "MoKhoaBootloader_HyperOS4", "bin");

            if (!Directory.Exists(ToolsDirectory))
            {
                Directory.CreateDirectory(ToolsDirectory);
            }

            // Đầy đủ 100% công cụ ngoại vi của tool gốc + exploit + lk
            string[] coreFiles = new string[]
            {
                "adb.exe",
                "AdbWinApi.dll",
                "AdbWinUsbApi.dll",
                "fastboot.exe",
                "fastboot_flash.exe",
                "android_driver_installer.exe",
                "libcve98260.so",
                "profile.blob",
                "lk.bin"
            };

            foreach (string file in coreFiles)
            {
                ExtractEmbeddedFile(file);
            }
        }

        public static string GetFilePath(string fileName)
        {
            string extractedPath = Path.Combine(ToolsDirectory, fileName);
            if (File.Exists(extractedPath)) return extractedPath;

            string localData = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", fileName);
            if (File.Exists(localData)) return localData;

            string localBase = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
            if (File.Exists(localBase)) return localBase;

            return extractedPath;
        }

        private static void ExtractEmbeddedFile(string resourceName)
        {
            string targetPath = Path.Combine(ToolsDirectory, resourceName);
            var assembly = Assembly.GetExecutingAssembly();

            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    string localFallback = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", resourceName);
                    if (File.Exists(localFallback) && !File.Exists(targetPath))
                    {
                        File.Copy(localFallback, targetPath, true);
                    }
                    return;
                }

                if (File.Exists(targetPath))
                {
                    FileInfo fi = new FileInfo(targetPath);
                    if (fi.Length == stream.Length)
                    {
                        // Equal length does not mean the cached binary/profile is current.
                        using (var sha = SHA256.Create())
                        using (var existing = File.OpenRead(targetPath))
                        {
                            string expected = Convert.ToBase64String(sha.ComputeHash(stream));
                            string actual = Convert.ToBase64String(sha.ComputeHash(existing));
                            if (expected == actual) return;
                        }
                        stream.Position = 0;
                    }
                }

                using (FileStream fs = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    stream.CopyTo(fs);
                }
            }
        }
    }
}
