using IonPackMan.Machine;

namespace IonPackMan.Tests;

/// <summary>Allow-list contoh dari PRD §7.2 untuk test yang membutuhkannya.</summary>
internal static class TestAllowList
{
    public const string WebUiKey = @"HKLM\SOFTWARE\WOW6432Node\HitachiExternalParameter\User\WebUI";
    public const string WebUiIni = @"D:\APCONTAINER\Config\WebUIUXCNFG.ini";

    public static AllowList Create()
    {
        string path = Path.Combine(Path.GetTempPath(), "ionpackman-allow-" + Guid.NewGuid().ToString("N") + ".ini");
        File.WriteAllText(path, $"""
            [Meta]
            SchemaVersion=1.0
            [BaseDirectories]
            ION=D:\ION
            APCONTAINER=D:\APCONTAINER
            [RegistryKeys]
            WebUI={WebUiKey}
            [IniFiles]
            WebUIUXCNFG={WebUiIni}
            [Services]
            WIND=WIND
            """);
        try
        {
            var (list, errors) = AllowList.Load(path);
            if (list is null)
                throw new InvalidOperationException(string.Join("; ", errors));
            return list;
        }
        finally
        {
            File.Delete(path);
        }
    }
}
