using IonPackMan.Ini;
using Xunit;

namespace IonPackMan.Tests;

public class IniFileTests
{
    private static IniFile Parse(string text) => IniFile.Parse(text, "test.ini");

    [Fact]
    public void Parse_ReadsSectionsAndKeys_CaseInsensitive()
    {
        var ini = Parse("[Meta]\r\nSchemaVersion=1.0\r\n\r\n[Machine]\r\nEnvironment = Development\r\n");

        Assert.Equal("1.0", ini.Get("meta", "schemaversion"));
        Assert.Equal("Development", ini.Get("Machine", "Environment"));
        Assert.Null(ini.Get("Machine", "Nope"));
        Assert.Null(ini.Get("Nope", "Key"));
    }

    [Fact]
    public void Parse_IgnoresCommentsAndBlankLines()
    {
        var ini = Parse("; whole line\n# also comment\n\n[A]\nK=V   ; trailing note\nL=x;y\n");

        Assert.Equal("V", ini.Get("A", "K"));
        Assert.Equal("x;y", ini.Get("A", "L")); // ';' tanpa spasi di depannya adalah bagian nilai
    }

    [Fact]
    public void Parse_AllowsEmptyValue_AndEqualsInsideValue()
    {
        var ini = Parse("[A]\nProfile=\nPath=a=b\n");

        Assert.Equal("", ini.Get("A", "Profile"));
        Assert.Equal("a=b", ini.Get("A", "Path"));
    }

    [Fact]
    public void Parse_PreservesBackslashPaths()
    {
        var ini = Parse(@"[A]" + "\n" + @"Dir=D:\ION\Wind" + "\n");
        Assert.Equal(@"D:\ION\Wind", ini.Get("A", "Dir"));
    }

    [Theory]
    [InlineData("[A]\nK=1\n[A]\nL=2\n", 3, "duplicate section")]
    [InlineData("[A]\nK=1\nk=2\n", 3, "duplicate key")]
    [InlineData("K=1\n", 1, "before any [Section]")]
    [InlineData("[A]\nnot a pair\n", 2, "expected 'Key=Value'")]
    [InlineData("[A\nK=1\n", 1, "missing ']'")]
    [InlineData("[]\n", 1, "section name is empty")]
    [InlineData("[A]\n=value\n", 2, "key name is empty")]
    public void Parse_RejectsBadInput_WithLineNumber(string text, int expectedLine, string expectedMessage)
    {
        var ex = Assert.Throws<IniException>(() => Parse(text));

        Assert.Equal(expectedLine, ex.Line);
        Assert.Equal("test.ini", ex.FileName);
        Assert.Contains(expectedMessage, ex.Message);
        Assert.StartsWith($"test.ini, line {expectedLine}:", ex.Message);
    }

    [Fact]
    public void CheckKeys_ReportsMissingAndUnknownKeys()
    {
        var ini = Parse("[Component]\nId=wind\nTypo=1\n");

        var problems = ini.CheckKeys("Component", required: ["Id", "ReleaseVersion"], optional: ["BuildReference"]);

        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, p => p.Contains("missing required key 'ReleaseVersion'"));
        Assert.Contains(problems, p => p.Contains("unknown key 'Typo'"));
    }

    [Fact]
    public void CheckKeys_EmptyRequiredValue_IsReported_ButEmptyOptionalIsFine()
    {
        var ini = Parse("[Component]\nId=\nBuildReference=\n");

        var problems = ini.CheckKeys("Component", required: ["Id"], optional: ["BuildReference"]);

        Assert.Equal("[Component] required key 'Id' is empty.", Assert.Single(problems));
    }

    [Fact]
    public void CheckSections_ReportsSectionsOutsideTheKnownList()
    {
        var ini = Parse("[Meta]\n[Machine]\n[Oops]\n");

        Assert.Equal("unknown section [Oops].", Assert.Single(ini.CheckSections("meta", "Machine")));
    }

    [Fact]
    public void CheckSections_NameEndingWithColon_AllowsAnySectionWithThatPrefix()
    {
        var ini = Parse("[Meta]\n[Component:wind]\n[component:qraptor]\n[Registry:X]\n");

        var problems = ini.CheckSections("Meta", "Component:");

        Assert.Equal("unknown section [Registry:X].", Assert.Single(problems));
    }

    [Fact]
    public void CheckKeys_ValidSection_ReturnsNoProblems()
    {
        var ini = Parse("[Component]\nId=wind\nBuildReference=ci-1\n");
        Assert.Empty(ini.CheckKeys("Component", ["Id"], ["BuildReference"]));
    }

    [Fact]
    public void CheckKeys_MissingSection_IsReported()
    {
        var problems = Parse("[A]\nK=1\n").CheckKeys("B", ["K"], []);
        Assert.Single(problems);
        Assert.Contains("missing section [B]", problems[0]);
    }

    [Fact]
    public void RoundTrip_ToTextThenParse_GivesSameData()
    {
        var ini = new IniFile();
        ini.Set("Meta", "SchemaVersion", "1.0");
        ini.Set("Registry.1", "Path", @"HKLM\SOFTWARE\WOW6432Node\ION");
        ini.Set("Registry.1", "Value", "");

        var again = Parse(ini.ToText());

        Assert.Equal(new[] { "Meta", "Registry.1" }, again.SectionNames);
        Assert.Equal("1.0", again.Get("Meta", "SchemaVersion"));
        Assert.Equal(@"HKLM\SOFTWARE\WOW6432Node\ION", again.Get("Registry.1", "Path"));
        Assert.Equal("", again.Get("Registry.1", "Value"));
    }

    [Theory]
    [InlineData("line1\nline2")]
    [InlineData(" leading")]
    [InlineData("trailing ")]
    [InlineData("a ; b")]
    public void Set_RejectsValuesThatCannotBeReadBack(string value)
    {
        Assert.Throws<ArgumentException>(() => new IniFile().Set("A", "K", value));
    }

    [Theory]
    [InlineData("", "K")]
    [InlineData("A]", "K")]
    [InlineData("A", "K=V")]
    [InlineData("A", "")]
    [InlineData("A", ";K")]
    [InlineData("A", "[K")]
    public void Set_RejectsInvalidNames(string section, string key)
    {
        Assert.Throws<ArgumentException>(() => new IniFile().Set(section, key, "v"));
    }

    [Fact]
    public void Save_WritesFile_CreatesFolders_AndLeavesNoTempFile()
    {
        string dir = Path.Combine(Path.GetTempPath(), "ionpackman-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(dir, "sub", "x.ini");
        try
        {
            var ini = new IniFile();
            ini.Set("A", "K", "V");
            ini.Save(path);

            Assert.Equal("V", IniFile.Load(path).Get("A", "K"));
            Assert.False(File.Exists(path + ".tmp"));
            // UTF-8 tanpa BOM
            Assert.Equal((byte)'[', File.ReadAllBytes(path)[0]);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Save_OverwritesExistingFile_Atomically()
    {
        string dir = Path.Combine(Path.GetTempPath(), "ionpackman-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(dir, "x.ini");
        try
        {
            var first = new IniFile(); first.Set("A", "K", "old"); first.Save(path);
            var second = new IniFile(); second.Set("A", "K", "new"); second.Save(path);

            Assert.Equal("new", IniFile.Load(path).Get("A", "K"));
            Assert.Single(Directory.GetFiles(dir));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Load_ReadsUtf8WithBom()
    {
        string path = Path.Combine(Path.GetTempPath(), "ionpackman-" + Guid.NewGuid().ToString("N") + ".ini");
        try
        {
            File.WriteAllText(path, "[A]\nK=V\n", new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            Assert.Equal("V", IniFile.Load(path).Get("A", "K"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_BadFile_MessageNamesTheFile()
    {
        string path = Path.Combine(Path.GetTempPath(), "ionpackman-" + Guid.NewGuid().ToString("N") + ".ini");
        try
        {
            File.WriteAllText(path, "[A]\nbroken\n");
            var ex = Assert.Throws<IniException>(() => IniFile.Load(path));
            Assert.Contains(path, ex.Message);
            Assert.Equal(2, ex.Line);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
