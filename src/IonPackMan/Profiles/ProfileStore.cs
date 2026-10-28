using System.IO;

namespace IonPackMan.Profiles;

/// <summary>Membaca dan menyimpan Profile di folder rilis (Profiles\&lt;Id&gt;.ini). Profile yang tidak valid tidak bisa disimpan.</summary>
public sealed class ProfileStore
{
    private readonly string _releaseDir;
    private readonly ProfileValidator _validator;

    public ProfileStore(string releaseDir, ProfileValidator validator)
    {
        _releaseDir = releaseDir;
        _validator = validator;
    }

    /// <summary>Id semua Profile yang tersimpan (dari nama file), urut abjad.</summary>
    public List<string> ListIds()
    {
        string dir = Paths.ProfilesDir(_releaseDir);
        if (!Directory.Exists(dir))
            return [];
        return Directory.GetFiles(dir, "*.ini")
            .Select(f => Path.GetFileNameWithoutExtension(f)!)
            .Where(ProfileValidator.IsValidProfileId)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Membaca satu Profile (bentuk file saja). Profile yang terbaca belum tentu valid:
    /// panggil <see cref="ProfileValidator.Validate"/> sebelum dipakai.
    /// </summary>
    public (Profile? Profile, List<string> Errors) Load(string profileId)
    {
        if (!ProfileValidator.IsValidProfileId(profileId))
            return (null, [$"'{profileId}' is not a valid profile Id."]);

        string path = Paths.ProfileIni(_releaseDir, profileId);
        var (profile, errors) = Profile.LoadFile(path);
        if (profile is not null && !string.Equals(profile.Id, profileId, StringComparison.Ordinal))
            return (null, [$"{path}: file declares profile '{profile.Id}' but its name says '{profileId}'."]);
        return (profile, errors);
    }

    /// <summary>Validasi lalu simpan. Mengembalikan masalah; kosong = tersimpan.</summary>
    public List<string> Save(Profile profile)
    {
        var problems = _validator.Validate(profile);
        if (problems.Count > 0)
            return problems;

        try
        {
            profile.ToIni().Save(Paths.ProfileIni(_releaseDir, profile.Id));
            return [];
        }
        catch (ArgumentException ex)
        {
            return [ex.Message];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [$"Cannot save profile {profile.Id}: {ex.Message}"];
        }
    }

    /// <summary>Import file INI dari luar; hanya disimpan bila valid.</summary>
    public (Profile? Profile, List<string> Errors) Import(string path)
    {
        var (profile, errors) = Profile.LoadFile(path);
        if (profile is null)
            return (null, errors);

        var saveErrors = Save(profile);
        return saveErrors.Count > 0 ? (null, saveErrors) : (profile, []);
    }
}
