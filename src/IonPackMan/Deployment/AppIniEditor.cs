using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace IonPackMan.Deployment;

/// <summary>
/// Mengubah satu key di INI milik aplikasi ATM (GPFCNFG.ini, WebUIUXCNFG.ini) lewat Win32
/// GetPrivateProfileString / WritePrivateProfileString (ADR-003, System Design §3.7).
/// Hanya key yang dituju yang berubah; komentar, urutan, dan section lain dibiarkan oleh Win32.
///
/// Batasan Win32: hanya ANSI dan UTF-16 LE. Karena itu Write/Delete memeriksa encoding file lebih dulu
/// dan MENOLAK (IOException "encoding not supported") file yang akan rusak: UTF-8 (dengan/tanpa BOM) berisi
/// karakter non-ASCII, atau UTF-16 BE. Nilai yang tidak bisa dinyatakan di code page ANSI juga ditolak
/// (Win32 diam-diam menggantinya dengan '?').
/// Catatan baca: Win32 membuang tanda kutip yang membungkus nilai dan spasi di ujung nilai.
/// Semua kegagalan = IOException atau Win32Exception yang menyebut file/section/key.
/// </summary>
public static class AppIniEditor
{
    private const string Sentinel = "\u0001<ion-packman-missing-9f3c1e>\u0001";

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetPrivateProfileStringW")]
    private static extern uint GetPrivateProfileString(string section, string key, string defaultValue,
        char[] returnedString, uint size, string fileName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "WritePrivateProfileStringW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WritePrivateProfileString(string section, string? key, string? value, string fileName);

    /// <summary>Membaca satu key. Exists=false bila file/section/key tidak ada (nilai kosong tetap Exists=true).</summary>
    public static (bool Exists, string Value) Read(string file, string section, string key)
    {
        Validate(file, section, key);
        if (!File.Exists(file))
            return (false, "");

        // Buffer diperbesar sampai muat; Win32 mengembalikan size-1 bila terpotong.
        for (uint size = 1024; size <= 1 << 20; size *= 4)
        {
            var buffer = new char[size];
            uint len = GetPrivateProfileString(section, key, Sentinel, buffer, size, file);
            if (len >= size - 1)
                continue;
            string value = new(buffer, 0, (int)len);
            return value == Sentinel ? (false, "") : (true, value);
        }
        throw new IOException($"Cannot read {Describe(file, section, key)}: value is too large.");
    }

    /// <summary>Menulis satu key (membuat key/section bila belum ada). Folder induk harus sudah ada.</summary>
    public static void Write(string file, string section, string key, string value)
    {
        Validate(file, section, key);
        ArgumentNullException.ThrowIfNull(value);
        if (HasControlChars(value))
            throw new ArgumentException($"Value for {Describe(file, section, key)} must not contain line breaks or control characters.", nameof(value));

        string? dir = Path.GetDirectoryName(file);
        if (dir is null || !Directory.Exists(dir))
            throw new DirectoryNotFoundException($"Cannot write {Describe(file, section, key)}: folder '{dir}' does not exist.");

        if (!CheckEncoding(file, section, key))
            EnsureAnsiSafe(file, section, key, section, key, value);

        Invoke(() => WritePrivateProfileString(section, key, value, file), "write", file, section, key);
    }

    /// <summary>Menghapus hanya key itu (section dibiarkan). Tidak error bila file/key tidak ada.</summary>
    public static void Delete(string file, string section, string key)
    {
        Validate(file, section, key);
        if (!File.Exists(file))
            return;
        CheckEncoding(file, section, key);
        Invoke(() => WritePrivateProfileString(section, key, null, file), "delete", file, section, key);
    }

    private static void Invoke(Func<bool> call, string verb, string file, string section, string key)
    {
        if (call())
            return;
        int error = Marshal.GetLastWin32Error();
        throw new Win32Exception(error, $"Cannot {verb} {Describe(file, section, key)}: {new Win32Exception(error).Message}");
    }

    private static void Validate(string file, string section, string key)
    {
        if (string.IsNullOrWhiteSpace(file) || !Path.IsPathFullyQualified(file))
            throw new ArgumentException($"INI path must be absolute (Win32 searches the Windows folder for relative paths): '{file}'.", nameof(file));
        if (string.IsNullOrEmpty(section) || HasControlChars(section))
            throw new ArgumentException($"Invalid section name for '{file}'.", nameof(section));
        if (string.IsNullOrEmpty(key) || HasControlChars(key))
            throw new ArgumentException($"Invalid key name for '{file}' [{section}].", nameof(key));
    }

    private static bool HasControlChars(string s) => s.Any(char.IsControl);

    private static string Describe(string file, string section, string key) => $"'{file}' [{section}] {key}";

    /// <summary>Mengembalikan true bila file UTF-16 LE (BOM), false bila ANSI/ASCII/kosong/tidak ada. Menolak yang lain.</summary>
    private static bool CheckEncoding(string file, string section, string key)
    {
        if (!File.Exists(file))
            return false;
        byte[] b = File.ReadAllBytes(file);
        if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE)
            return true;
        if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF)
            throw Unsupported(file, section, key, "UTF-16 big-endian");
        if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF)
            throw Unsupported(file, section, key, "UTF-8 with BOM");
        if (b.Any(x => x >= 0x80) && IsValidUtf8(b))
            throw Unsupported(file, section, key, "UTF-8 without BOM containing non-ASCII characters");
        return false; // ASCII atau ANSI (byte tinggi yang bukan UTF-8 valid)
    }

    private static IOException Unsupported(string file, string section, string key, string what) =>
        new($"Cannot change {Describe(file, section, key)}: encoding not supported ({what}). Only ANSI and UTF-16 LE with BOM can be edited safely; the file was not modified.");

    private static bool IsValidUtf8(byte[] bytes)
    {
        try
        {
            new UTF8Encoding(false, true).GetCharCount(bytes);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static void EnsureAnsiSafe(string file, string section, string key, params string[] texts)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding ansi;
        try
        {
            ansi = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage,
                EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        }
        catch (ArgumentException)
        {
            return; // code page tidak dikenal; biarkan Win32
        }
        foreach (string text in texts)
        {
            try
            {
                ansi.GetBytes(text);
            }
            catch (EncoderFallbackException)
            {
                throw new IOException($"Cannot write {Describe(file, section, key)}: encoding not supported (text has characters outside the ANSI code page and the file is not UTF-16); the file was not modified.");
            }
        }
    }
}
