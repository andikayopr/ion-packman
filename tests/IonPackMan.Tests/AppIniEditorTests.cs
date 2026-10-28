using System.Text;
using IonPackMan.Deployment;
using Xunit;

namespace IonPackMan.Tests;

/// <summary>Semua file hanya dibuat di folder sementara di bawah Path.GetTempPath() dan dihapus di Dispose.</summary>
public sealed class AppIniEditorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ionpackman-t20-" + Guid.NewGuid().ToString("N"));

    public AppIniEditorTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static readonly Encoding Ansi1252 = CodePagesProvider.Get1252();

    private string Make(string text, Encoding? enc = null, bool bom = false)
    {
        string path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".ini");
        File.WriteAllBytes(path, Bytes(text, enc ?? Encoding.ASCII, bom));
        return path;
    }

    private static byte[] Bytes(string text, Encoding enc, bool bom) =>
        bom ? enc.GetPreamble().Concat(enc.GetBytes(text)).ToArray() : enc.GetBytes(text);

    private const string Plain =
        "; GPF config - komentar\r\n" +
        "[Zeta]\r\n" +
        "Last=9\r\n" +
        "\r\n" +
        "[Alpha]\r\n" +
        "# catatan lain\r\n" +
        "Name=Old\r\n" +
        "  Spaced Key = x y \r\n" +
        "Empty=\r\n" +
        "\r\n" +
        "[Beta (1)]\r\n" +
        "Key [a]=1\r\n";

    // ---------- Read ----------

    [Fact]
    public void Read_FileMissing_ReturnsNotExists_AndDoesNotCreateFile()
    {
        string path = Path.Combine(_dir, "nope.ini");
        Assert.Equal((false, ""), AppIniEditor.Read(path, "A", "K"));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Read_DistinguishesMissingKeyAndSectionFromEmptyValue()
    {
        string f = Make(Plain);
        Assert.Equal((true, "Old"), AppIniEditor.Read(f, "Alpha", "Name"));
        Assert.Equal((true, ""), AppIniEditor.Read(f, "Alpha", "Empty"));
        Assert.Equal((false, ""), AppIniEditor.Read(f, "Alpha", "Missing"));
        Assert.Equal((false, ""), AppIniEditor.Read(f, "NoSection", "Name"));
        Assert.Equal((true, "x y"), AppIniEditor.Read(f, "Alpha", "Spaced Key"));
    }

    [Fact]
    public void Read_SectionAndKeyWithBracketsAndSpaces()
    {
        string f = Make(Plain);
        Assert.Equal((true, "1"), AppIniEditor.Read(f, "Beta (1)", "Key [a]"));
    }

    // ---------- Write / Delete: sisa file identik ----------

    [Fact]
    public void Write_ExistingKey_ChangesOnlyThatLine()
    {
        string f = Make(Plain);
        AppIniEditor.Write(f, "Alpha", "Name", "New");
        Assert.Equal(Plain.Replace("Name=Old", "Name=New"), File.ReadAllText(f, Encoding.ASCII));
        Assert.Equal((true, "New"), AppIniEditor.Read(f, "Alpha", "Name"));
    }

    [Fact]
    public void Write_NewKeyInExistingSection_KeepsEverythingElseByteIdentical()
    {
        string f = Make(Plain);
        byte[] before = File.ReadAllBytes(f);
        AppIniEditor.Write(f, "Alpha", "Added", "1");
        byte[] after = File.ReadAllBytes(f);

        // Setiap baris asli masih ada, urutan relatif sama, CRLF tetap.
        string text = Encoding.ASCII.GetString(after);
        Assert.Contains("Added=1\r\n", text);
        Assert.Equal(Plain, text.Replace("Added=1\r\n", ""));
        Assert.True(after.Length > before.Length);
        Assert.Equal((true, "1"), AppIniEditor.Read(f, "Alpha", "Added"));
    }

    [Fact]
    public void Write_NewSection_AppendsAndKeepsOriginalPrefixIdentical()
    {
        string f = Make(Plain);
        byte[] before = File.ReadAllBytes(f);
        AppIniEditor.Write(f, "Fresh", "K", "V");
        byte[] after = File.ReadAllBytes(f);

        Assert.Equal(before, after.Take(before.Length).ToArray());
        Assert.Equal((true, "V"), AppIniEditor.Read(f, "Fresh", "K"));
    }

    [Fact]
    public void Write_CreatesFile_WhenParentFolderExists()
    {
        string f = Path.Combine(_dir, "new.ini");
        AppIniEditor.Write(f, "S", "K", "V");
        Assert.True(File.Exists(f));
        Assert.Equal((true, "V"), AppIniEditor.Read(f, "S", "K"));
    }

    [Fact]
    public void Delete_RemovesOnlyThatKey_RestByteIdentical()
    {
        string f = Make(Plain);
        AppIniEditor.Delete(f, "Alpha", "Name");
        Assert.Equal(Plain.Replace("Name=Old\r\n", ""), File.ReadAllText(f, Encoding.ASCII));
        Assert.Equal((false, ""), AppIniEditor.Read(f, "Alpha", "Name"));
        Assert.Equal((true, ""), AppIniEditor.Read(f, "Alpha", "Empty"));
    }

    [Fact]
    public void Delete_MissingKeyOrFile_IsHarmless()
    {
        string f = Make(Plain);
        byte[] before = File.ReadAllBytes(f);
        AppIniEditor.Delete(f, "Alpha", "Nope");
        AppIniEditor.Delete(f, "Nope", "Nope");
        Assert.Equal(before, File.ReadAllBytes(f));

        string missing = Path.Combine(_dir, "missing.ini");
        AppIniEditor.Delete(missing, "A", "K");
        Assert.False(File.Exists(missing));
    }

    [Fact]
    public void Write_EmptyValue_IsReadBackAsExistingEmpty()
    {
        string f = Make(Plain);
        AppIniEditor.Write(f, "Alpha", "Name", "");
        Assert.Equal((true, ""), AppIniEditor.Read(f, "Alpha", "Name"));
    }

    [Theory]
    [InlineData(@"D:\ION\Wind\Data")]
    [InlineData("a=b;c")]
    [InlineData("x # y")]
    [InlineData("[not a section]")]
    [InlineData("with  inner   spaces")]
    public void Write_ValueWithSpecialCharacters_RoundTrips(string value)
    {
        string f = Make(Plain);
        AppIniEditor.Write(f, "Alpha", "Special", value);
        Assert.Equal((true, value), AppIniEditor.Read(f, "Alpha", "Special"));
        // Bagian lain tidak berubah.
        Assert.Equal(Plain, File.ReadAllText(f, Encoding.ASCII).Replace("Special=" + value + "\r\n", ""));
    }

    [Fact]
    public void Write_KeyAndSectionWithBracketsAndSpaces_RoundTrips()
    {
        string f = Make(Plain);
        AppIniEditor.Write(f, "Beta (1)", "New Key (x)", "v");
        Assert.Equal((true, "v"), AppIniEditor.Read(f, "Beta (1)", "New Key (x)"));
        Assert.Equal((true, "1"), AppIniEditor.Read(f, "Beta (1)", "Key [a]"));
    }

    [Fact]
    public void Write_LongValue_RoundTrips()
    {
        string f = Make(Plain);
        string value = new('x', 5000);
        AppIniEditor.Write(f, "Alpha", "Long", value);
        Assert.Equal((true, value), AppIniEditor.Read(f, "Alpha", "Long"));
    }

    [Fact]
    public void Write_ValueWithLineBreak_IsRejected_FileUntouched()
    {
        string f = Make(Plain);
        byte[] before = File.ReadAllBytes(f);
        Assert.Throws<ArgumentException>(() => AppIniEditor.Write(f, "Alpha", "Name", "a\r\nb=c"));
        Assert.Equal(before, File.ReadAllBytes(f));
    }

    // ---------- path ----------

    [Theory]
    [InlineData("GPFCNFG.ini")]
    [InlineData(@"sub\GPFCNFG.ini")]
    [InlineData(@"\GPFCNFG.ini")]
    [InlineData("")]
    public void RelativePath_IsRejected_ForAllOperations(string path)
    {
        Assert.Throws<ArgumentException>(() => AppIniEditor.Read(path, "A", "K"));
        Assert.Throws<ArgumentException>(() => AppIniEditor.Write(path, "A", "K", "V"));
        Assert.Throws<ArgumentException>(() => AppIniEditor.Delete(path, "A", "K"));
    }

    [Fact]
    public void Write_ParentFolderMissing_Throws_AndDoesNotCreateFolder_MessageNamesTarget()
    {
        string folder = Path.Combine(_dir, "nofolder");
        string f = Path.Combine(folder, "x.ini");
        var ex = Assert.Throws<DirectoryNotFoundException>(() => AppIniEditor.Write(f, "Sec", "Key", "V"));
        Assert.Contains("x.ini", ex.Message);
        Assert.Contains("Sec", ex.Message);
        Assert.Contains("Key", ex.Message);
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void Write_ReadOnlyFile_ThrowsWithTargetInMessage_FileUntouched()
    {
        string f = Make(Plain);
        byte[] before = File.ReadAllBytes(f);
        File.SetAttributes(f, FileAttributes.ReadOnly);
        try
        {
            var ex = Assert.ThrowsAny<Exception>(() => AppIniEditor.Write(f, "Alpha", "Name", "New"));
            Assert.True(ex is IOException or System.ComponentModel.Win32Exception);
            Assert.Contains("Name", ex.Message);
            Assert.Contains(Path.GetFileName(f), ex.Message);
            Assert.Equal(before, File.ReadAllBytes(f));
        }
        finally
        {
            File.SetAttributes(f, FileAttributes.Normal);
        }
    }

    // ---------- encoding: HASIL NYATA ----------
    // Ringkasan hasil yang diamati (Windows 11, code page ANSI 1252):
    //  (a) ANSI/Windows-1252 + karakter non-ASCII di bagian lain: byte lain IDENTIK setelah Write/Delete;
    //      tetap ANSI tanpa BOM. AMAN.
    //  (b) UTF-16 LE dengan BOM: BOM terjaga, file tetap UTF-16 LE; teks lain identik (dibandingkan sebagai teks
    //      terdekode). AMAN.
    //  (c) UTF-8 tanpa BOM + non-ASCII: percobaan mentah ke WritePrivateProfileString (tanpa guard) ternyata
    //      MEMPERTAHANKAN byte baris lain (disalin mentah sebagai ANSI), tetapi nilai non-ASCII yang kita tulis/baca
    //      ditafsirkan sebagai code page ANSI (mojibake/'?'), dan Read mengembalikan teks salah. Karena file
    //      campuran seperti itu tidak bisa dijamin aman, AppIniEditor MENOLAK Write/Delete (IOException
    //      "encoding not supported"); byte file tidak berubah. Read tetap berjalan (nilai ASCII benar).
    //  Tambahan: UTF-8 dengan BOM dan UTF-16 BE juga ditolak; nilai di luar code page ANSI ditolak untuk file ANSI.

    private const string NonAscii =
        "; Caf\u00e9 \u00fcber \u00b0C \u2013 komentar\r\n" +
        "[Text]\r\n" +
        "Title=Caf\u00e9 \u00e0 la carte\r\n" +
        "[Alpha]\r\n" +
        "Name=Old\r\n" +
        "[Omega]\r\n" +
        "Unit=\u00b0C\r\n";

    [Fact]
    public void Encoding_Ansi1252_NonAsciiElsewhere_IsPreservedByteForByte()
    {
        string f = Make(NonAscii, Ansi1252);
        byte[] before = File.ReadAllBytes(f);

        AppIniEditor.Write(f, "Alpha", "Name", "New");
        byte[] after = File.ReadAllBytes(f);
        Assert.Equal(Bytes(NonAscii.Replace("Name=Old", "Name=New"), Ansi1252, false), after);
        Assert.NotEqual(before, after);
        Assert.False(after.Length >= 2 && after[0] == 0xFF && after[1] == 0xFE); // tidak berubah jadi UTF-16

        AppIniEditor.Write(f, "Alpha", "Name", "Old");
        Assert.Equal(before, File.ReadAllBytes(f));

        AppIniEditor.Delete(f, "Alpha", "Name");
        Assert.Equal(Bytes(NonAscii.Replace("Name=Old\r\n", ""), Ansi1252, false), File.ReadAllBytes(f));
        Assert.Equal((true, "Caf\u00e9 \u00e0 la carte"), AppIniEditor.Read(f, "Text", "Title"));
    }

    [Fact]
    public void Encoding_Ansi1252_WritingNonAnsiValue_IsRejected()
    {
        string f = Make(NonAscii, Ansi1252);
        byte[] before = File.ReadAllBytes(f);
        var ex = Assert.Throws<IOException>(() => AppIniEditor.Write(f, "Alpha", "Name", "\u4e2d\u6587"));
        Assert.Contains("encoding not supported", ex.Message);
        Assert.Equal(before, File.ReadAllBytes(f));
    }

    [Fact]
    public void Encoding_Utf16LeWithBom_PreservesBomAndEncoding()
    {
        string f = Make(NonAscii, Encoding.Unicode, bom: true);
        byte[] before = File.ReadAllBytes(f);

        AppIniEditor.Write(f, "Alpha", "Name", "New \u00e9\u4e2d"); // UTF-16 mendukung karakter ini
        byte[] after = File.ReadAllBytes(f);

        Assert.Equal(0xFF, after[0]);
        Assert.Equal(0xFE, after[1]); // BOM terjaga
        string text = Encoding.Unicode.GetString(after, 2, after.Length - 2);
        Assert.Equal(NonAscii.Replace("Name=Old", "Name=New \u00e9\u4e2d"), text);
        Assert.Equal((true, "New \u00e9\u4e2d"), AppIniEditor.Read(f, "Alpha", "Name"));

        AppIniEditor.Write(f, "Alpha", "Name", "Old");
        Assert.Equal(before, File.ReadAllBytes(f));

        AppIniEditor.Delete(f, "Alpha", "Name");
        string afterDelete = Encoding.Unicode.GetString(File.ReadAllBytes(f), 2, File.ReadAllBytes(f).Length - 2);
        Assert.Equal(NonAscii.Replace("Name=Old\r\n", ""), afterDelete);
    }

    [Fact]
    public void Encoding_Utf8WithoutBom_NonAscii_IsRejected_FileUntouched()
    {
        string f = Make(NonAscii, new UTF8Encoding(false));
        byte[] before = File.ReadAllBytes(f);

        var write = Assert.Throws<IOException>(() => AppIniEditor.Write(f, "Alpha", "Name", "New"));
        Assert.Contains("encoding not supported", write.Message);
        Assert.Contains("Name", write.Message);
        var delete = Assert.Throws<IOException>(() => AppIniEditor.Delete(f, "Alpha", "Name"));
        Assert.Contains("encoding not supported", delete.Message);

        Assert.Equal(before, File.ReadAllBytes(f)); // tidak ada byte yang berubah
        Assert.Equal((true, "Old"), AppIniEditor.Read(f, "Alpha", "Name")); // baca tetap boleh
    }

    [Fact]
    public void Encoding_Utf8WithoutBom_AsciiOnly_IsIndistinguishableFromAnsi_AndWorks()
    {
        string text = "[A]\r\nK=1\r\n[B]\r\nL=2\r\n";
        string f = Make(text, new UTF8Encoding(false));
        AppIniEditor.Write(f, "A", "K", "9");
        Assert.Equal(text.Replace("K=1", "K=9"), File.ReadAllText(f, Encoding.ASCII));
    }

    [Fact]
    public void Encoding_Utf8WithBom_IsRejected_FileUntouched()
    {
        string f = Make("[A]\r\nK=1\r\n", new UTF8Encoding(true), bom: true);
        byte[] before = File.ReadAllBytes(f);
        var ex = Assert.Throws<IOException>(() => AppIniEditor.Write(f, "A", "K", "2"));
        Assert.Contains("encoding not supported", ex.Message);
        Assert.Equal(before, File.ReadAllBytes(f));
    }

    [Fact]
    public void Encoding_Utf16BigEndian_IsRejected_FileUntouched()
    {
        string f = Make("[A]\r\nK=1\r\n", Encoding.BigEndianUnicode, bom: true);
        byte[] before = File.ReadAllBytes(f);
        var ex = Assert.Throws<IOException>(() => AppIniEditor.Write(f, "A", "K", "2"));
        Assert.Contains("encoding not supported", ex.Message);
        Assert.Equal(before, File.ReadAllBytes(f));
    }
}

internal static class CodePagesProvider
{
    public static Encoding Get1252()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252);
    }
}
