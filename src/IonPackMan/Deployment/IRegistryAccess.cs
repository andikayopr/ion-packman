namespace IonPackMan.Deployment;

/// <summary>Tipe nilai Registry yang didukung; selaras dengan "String"/"DWORD" di ProfileValidator.</summary>
public enum RegistryValueType { String, DWord }

/// <param name="Value">String apa adanya; DWORD sebagai angka tak bertanda (0..4294967295) dalam bentuk string.</param>
public sealed record RegistryValue(RegistryValueType Type, string Value);

/// <summary>
/// Akses Registry untuk deployment (R7/R11). Dua implementasi: <c>RegistryAccess</c> (asli) dan fake di tes.
/// Path dipakai apa adanya (sudah memuat WOW6432Node bila perlu); tidak ada pengalihan tambahan.
/// </summary>
public interface IRegistryAccess
{
    /// <summary>Null bila key atau value tidak ada.</summary>
    RegistryValue? Read(string key, string valueName);

    /// <summary>Membuat key bila belum ada, lalu menulis value dengan tipe tsb.</summary>
    void Write(string key, string valueName, RegistryValueType type, string value);

    /// <summary>Menghapus HANYA value itu; key tidak pernah dihapus; tidak error bila sudah tidak ada.</summary>
    void Delete(string key, string valueName);
}
