using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AIOrchestrator.Services
{
    /// <summary>Stores an optional remembered login encrypted for the current Windows user.</summary>
    public static class SavedLoginStore
    {
        private const int UiForbidden = 0x1;
        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VinhAI",
            "saved-login.json");

        public static SavedLogin? Load()
        {
            if (!File.Exists(FilePath))
                return null;

            var saved = JsonSerializer.Deserialize<SavedLoginData>(File.ReadAllText(FilePath))
                ?? throw new InvalidDataException("Tệp lưu thông tin đăng nhập không hợp lệ.");
            byte[] protectedPassword = Convert.FromBase64String(saved.ProtectedPassword);
            byte[] password = Unprotect(protectedPassword);
            return new SavedLogin(saved.Identifier, Encoding.UTF8.GetString(password));
        }

        public static void Save(string identifier, string password)
        {
            if (string.IsNullOrWhiteSpace(identifier) || string.IsNullOrEmpty(password))
                throw new ArgumentException("Thông tin đăng nhập không được để trống.");

            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var saved = new SavedLoginData(
                identifier,
                Convert.ToBase64String(Protect(Encoding.UTF8.GetBytes(password))));
            string temporaryPath = FilePath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(saved));
            File.Move(temporaryPath, FilePath, overwrite: true);
        }

        public static void Clear()
        {
            if (File.Exists(FilePath))
                File.Delete(FilePath);
        }

        private static byte[] Protect(byte[] data) => Transform(data, protect: true);

        private static byte[] Unprotect(byte[] data) => Transform(data, protect: false);

        private static byte[] Transform(byte[] data, bool protect)
        {
            var input = new DataBlob { Length = data.Length, Data = Marshal.AllocHGlobal(data.Length) };
            Marshal.Copy(data, 0, input.Data, data.Length);

            try
            {
                DataBlob output;
                bool success;
                if (protect)
                {
                    success = CryptProtectData(
                        ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, out output);
                }
                else
                {
                    success = CryptUnprotectData(
                        ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, out output);
                }

                if (!success)
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                try
                {
                    var result = new byte[output.Length];
                    Marshal.Copy(output.Data, result, 0, output.Length);
                    return result;
                }
                finally
                {
                    LocalFree(output.Data);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(input.Data);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob
        {
            public int Length;
            public IntPtr Data;
        }

        [DllImport("Crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptProtectData(
            ref DataBlob input, string? description, IntPtr optionalEntropy, IntPtr reserved,
            IntPtr prompt, int flags, out DataBlob output);

        [DllImport("Crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptUnprotectData(
            ref DataBlob input, IntPtr description, IntPtr optionalEntropy, IntPtr reserved,
            IntPtr prompt, int flags, out DataBlob output);

        [DllImport("Kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);

        private sealed record SavedLoginData(string Identifier, string ProtectedPassword);

        public sealed record SavedLogin(string Identifier, string Password);
    }
}
