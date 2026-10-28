using System.IO;
using IonPackMan.Deployment;
using Microsoft.Win32;
using Xunit;

namespace IonPackMan.Tests;

/// <summary>Tes asli HANYA pada HKCU\Software\IonPackMan.Tests\&lt;guid&gt; (dibersihkan di Dispose). Tidak ada tulis ke HKLM.</summary>
public class RegistryAccessTests : IDisposable
{
    private const string TestRoot = @"Software\IonPackMan.Tests";
    private readonly string _guid = Guid.NewGuid().ToString("N");
    private readonly RegistryAccess _reg = new();
    private string Key => $@"HKCU\{TestRoot}\{_guid}";

    public void Dispose()
    {
        using var hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        hkcu.DeleteSubKeyTree($@"{TestRoot}\{_guid}", throwOnMissingSubKey: false);
    }

    [Fact]
    public void Read_MissingKeyOrValue_ReturnsNull()
    {
        Assert.Null(_reg.Read(Key, "Nope"));
        _reg.Write(Key, "A", RegistryValueType.String, "x");
        Assert.Null(_reg.Read(Key, "Nope"));
    }

    [Fact]
    public void WriteString_ThenRead()
    {
        _reg.Write(Key, "Name", RegistryValueType.String, @"C:\Path\With %PATH%");
        Assert.Equal(new RegistryValue(RegistryValueType.String, @"C:\Path\With %PATH%"), _reg.Read(Key, "Name"));
    }

    [Fact]
    public void WriteDWord_ThenRead()
    {
        _reg.Write(Key, "N", RegistryValueType.DWord, "42");
        Assert.Equal(new RegistryValue(RegistryValueType.DWord, "42"), _reg.Read(Key, "N"));
    }

    [Fact]
    public void WriteDWord_MaxUnsigned_RoundTrips()
    {
        _reg.Write(Key, "Max", RegistryValueType.DWord, "4294967295");
        Assert.Equal("4294967295", _reg.Read(Key, "Max")!.Value);
        using var hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        using var k = hkcu.OpenSubKey($@"{TestRoot}\{_guid}")!;
        Assert.Equal(-1, (int)k.GetValue("Max")!);
        Assert.Equal(RegistryValueKind.DWord, k.GetValueKind("Max"));
    }

    [Fact]
    public void Write_OverwritesAndChangesType()
    {
        _reg.Write(Key, "V", RegistryValueType.String, "a");
        _reg.Write(Key, "V", RegistryValueType.DWord, "7");
        Assert.Equal(new RegistryValue(RegistryValueType.DWord, "7"), _reg.Read(Key, "V"));
    }

    [Fact]
    public void Delete_RemovesOnlyThatValue_KeyRemains()
    {
        _reg.Write(Key, "A", RegistryValueType.String, "1");
        _reg.Write(Key, "B", RegistryValueType.String, "2");
        _reg.Delete(Key, "A");

        Assert.Null(_reg.Read(Key, "A"));
        Assert.Equal("2", _reg.Read(Key, "B")!.Value);

        _reg.Delete(Key, "B");
        using var hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        using var k = hkcu.OpenSubKey($@"{TestRoot}\{_guid}");
        Assert.NotNull(k);
        Assert.Equal(0, k.ValueCount);
    }

    [Fact]
    public void Delete_NotExisting_DoesNotThrow()
    {
        _reg.Delete(Key, "Nope");
        _reg.Write(Key, "A", RegistryValueType.String, "1");
        _reg.Delete(Key, "Nope");
        Assert.Equal("1", _reg.Read(Key, "A")!.Value);
    }

    [Fact]
    public void Write_Registry64_PathIsExactlyAsGiven_NoDoubleWow6432Node()
    {
        var key = $@"{Key}\WOW6432Node\X";
        _reg.Write(key, "V", RegistryValueType.String, "ok");

        using var hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        using var exact = hkcu.OpenSubKey($@"{TestRoot}\{_guid}\WOW6432Node\X");
        Assert.NotNull(exact);
        Assert.Equal("ok", exact.GetValue("V"));
        using var doubled = hkcu.OpenSubKey($@"{TestRoot}\{_guid}\WOW6432Node\WOW6432Node");
        Assert.Null(doubled);
    }

    [Fact]
    public void LongHiveNameAndCase_Accepted()
    {
        var key = $@"hkey_current_user\{TestRoot}\{_guid}";
        _reg.Write(key, "V", RegistryValueType.String, "1");
        Assert.Equal("1", _reg.Read(Key, "V")!.Value);
    }

    [Theory]
    [InlineData(@"HKCR\Foo")]
    [InlineData(@"HKEY_USERS\Foo")]
    [InlineData(@"HKU\Foo")]
    [InlineData(@"Foo\Bar")]
    [InlineData("")]
    [InlineData("HKLM")]
    [InlineData(@"HKLM\")]
    public void UnsupportedHiveOrEmptySubKey_Rejected(string key)
    {
        Assert.Throws<ArgumentException>(() => _reg.Read(key, "V"));
        Assert.Throws<ArgumentException>(() => _reg.Write(key, "V", RegistryValueType.String, "x"));
        Assert.Throws<ArgumentException>(() => _reg.Delete(key, "V"));
    }

    [Fact]
    public void UnsupportedHiveMessage_NamesTheHive()
    {
        var ex = Assert.Throws<ArgumentException>(() => _reg.Read(@"HKCR\Foo", "V"));
        Assert.Contains("HKCR", ex.Message);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("4294967296")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("1.5")]
    [InlineData("0x10")]
    [InlineData("+5")]
    public void BadDWordValue_Rejected_AndNothingWritten(string value)
    {
        var ex = Assert.Throws<ArgumentException>(() => _reg.Write(Key, "N", RegistryValueType.DWord, value));
        Assert.Contains("'N'", ex.Message);
        Assert.Null(_reg.Read(Key, "N"));
    }

    [Fact]
    public void InvalidType_Rejected()
    {
        Assert.Throws<ArgumentException>(() => _reg.Write(Key, "N", (RegistryValueType)99, "1"));
    }

    [Fact]
    public void ParsePath_HklmForms_NoRegistryOpened()
    {
        Assert.Equal((RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\ION"), RegistryAccess.ParsePath(@"HKLM\SOFTWARE\WOW6432Node\ION"));
        Assert.Equal((RegistryHive.LocalMachine, @"SOFTWARE\X"), RegistryAccess.ParsePath(@"HKEY_LOCAL_MACHINE\SOFTWARE\X"));
    }

    [Fact]
    public void Read_ExpandString_NotExpanded_ReportedAsString()
    {
        using (var hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
        using (var k = hkcu.CreateSubKey($@"{TestRoot}\{_guid}"))
            k.SetValue("E", "%TEMP%", RegistryValueKind.ExpandString);
        Assert.Equal(new RegistryValue(RegistryValueType.String, "%TEMP%"), _reg.Read(Key, "E"));
    }
}

public class FakeRegistryTests
{
    private const string K = @"HKLM\SOFTWARE\WOW6432Node\ION";

    [Fact]
    public void ReadWriteDelete_Work_AndKeyIsCaseInsensitive()
    {
        var f = new FakeRegistry();
        Assert.Null(f.Read(K, "A"));
        f.Write(K, "A", RegistryValueType.DWord, "5");
        Assert.Equal(new RegistryValue(RegistryValueType.DWord, "5"), f.Read(K.ToLowerInvariant(), "a"));
        f.Delete(K, "A");
        f.Delete(K, "A");
        Assert.Null(f.Read(K, "A"));
    }

    [Fact]
    public void FailOn_ThrowsIOException_OnWriteAndDelete_NotRead_AndLeavesStateUntouched()
    {
        var f = new FakeRegistry();
        f.Seed(K, "A", RegistryValueType.String, "old");
        f.FailOn(K, "A");
        var before = f.Snapshot();

        Assert.Throws<IOException>(() => f.Write(K, "A", RegistryValueType.String, "new"));
        Assert.Throws<IOException>(() => f.Delete(K, "A"));
        Assert.Equal("old", f.Read(K, "A")!.Value);
        Assert.Equal(before, f.Snapshot());

        f.ClearFailures();
        f.Write(K, "A", RegistryValueType.String, "new");
        Assert.Equal("new", f.Read(K, "A")!.Value);
    }

    [Fact]
    public void Operations_RecordedInOrder_SeedNotRecorded()
    {
        var f = new FakeRegistry();
        f.Seed(K, "S", RegistryValueType.String, "s");
        f.Write(K, "A", RegistryValueType.String, "1");
        f.Read(K, "A");
        f.Delete(K, "A");

        Assert.Equal(new[] { $"Write {K}|A=String:1", $"Read {K}|A", $"Delete {K}|A" }, f.Operations);
    }

    [Fact]
    public void Snapshot_IsIndependentCopy()
    {
        var f = new FakeRegistry();
        f.Write(K, "A", RegistryValueType.String, "1");
        var snap = f.Snapshot();
        f.Write(K, "B", RegistryValueType.String, "2");
        f.Delete(K, "A");

        Assert.Single(snap);
        Assert.Single(f.Snapshot());
    }
}
