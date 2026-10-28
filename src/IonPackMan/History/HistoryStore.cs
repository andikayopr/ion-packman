using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using IonPackMan.Ini;

namespace IonPackMan.History;

/// <summary>
/// Membaca dan menulis History: satu file INI per eksekusi, ditulis sekali (ADR-005).
/// Sengaja tidak ada API untuk mengubah atau menghapus.
/// </summary>
public sealed class HistoryStore
{
    private const string TimeFormat = "yyyy-MM-dd'T'HH:mm:sszzz";
    private static readonly Regex IdPattern = new(@"^[0-9A-Za-z][0-9A-Za-z._-]{0,99}$", RegexOptions.Compiled);

    private readonly string _cacheDir;

    public HistoryStore(string cacheDir) => _cacheDir = cacheDir;

    /// <summary>Menulis satu laporan. Mengembalikan masalah; kosong = tersimpan. Id yang sudah ada ditolak.</summary>
    public List<string> Write(HistoryRecord record)
    {
        var problems = Validate(record);
        if (problems.Count > 0)
            return problems;

        string path = Paths.HistoryIni(_cacheDir, record.Id);
        string duplicate = $"History '{record.Id}' already exists; history is write-once.";
        if (File.Exists(path))
            return [duplicate];

        string tmp = path + ".tmp";
        try
        {
            string text = ToIni(record).ToText();
            Directory.CreateDirectory(Paths.HistoryDir(_cacheDir));
            File.WriteAllText(tmp, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(tmp, path, overwrite: false);   // gagal bila file sudah ada (balapan dengan penulis lain)
            return [];
        }
        catch (ArgumentException ex)
        {
            return [ex.Message];
        }
        catch (IOException) when (File.Exists(path))
        {
            return [duplicate];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [$"Cannot write history '{record.Id}': {ex.Message}"];
        }
        finally
        {
            if (File.Exists(tmp))
                File.Delete(tmp);
        }
    }

    /// <summary>
    /// Semua laporan, terbaru dulu. File yang rusak dilewati dan dilaporkan di Problems;
    /// folder belum ada = daftar kosong tanpa masalah.
    /// </summary>
    public (List<HistoryRecord> Records, List<string> Problems) GetAll()
    {
        var records = new List<HistoryRecord>();
        var problems = new List<string>();
        string dir = Paths.HistoryDir(_cacheDir);
        if (!Directory.Exists(dir))
            return (records, problems);

        string[] files;
        try
        {
            files = Directory.GetFiles(dir, "*.ini");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (records, [$"{dir}: cannot be read ({ex.Message})."]);
        }

        foreach (var file in files.Where(f => f.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal))
        {
            var (record, errors) = Read(file);
            if (record is null)
                problems.AddRange(errors);
            else
                records.Add(record);
        }

        // Id berawalan timestamp, jadi Id dipakai sebagai pemutus seri.
        records = records.OrderByDescending(r => r.StartedAt.UtcDateTime).ThenByDescending(r => r.Id, StringComparer.Ordinal).ToList();
        return (records, problems);
    }

    /// <summary>Laporan terbaru, atau null bila belum ada yang terbaca.</summary>
    public (HistoryRecord? Record, List<string> Problems) GetLatest()
    {
        var (records, problems) = GetAll();
        return (records.FirstOrDefault(), problems);
    }

    // ---------- Validasi ----------

    private static List<string> Validate(HistoryRecord r)
    {
        var problems = new List<string>();
        if (string.IsNullOrEmpty(r.Id) || !IdPattern.IsMatch(r.Id))
            problems.Add($"History Id '{r.Id}' must use only letters, digits, '.', '_' and '-'.");
        if (!Enum.IsDefined(r.Outcome))
            problems.Add($"Outcome '{r.Outcome}' is not a known outcome.");
        if (string.IsNullOrWhiteSpace(r.Operator)) problems.Add("Operator is required.");
        if (string.IsNullOrWhiteSpace(r.Machine)) problems.Add("Machine is required.");
        if (string.IsNullOrWhiteSpace(r.Component)) problems.Add("Component is required.");
        if (r.Operations.Any(o => string.IsNullOrWhiteSpace(o) || o.Contains(',') || o.Contains('\n') || o.Contains('\r')))
            problems.Add("Operations must be non-empty names without ',' or line breaks.");
        return problems;
    }

    // ---------- Tulis ----------

    private static IniFile ToIni(HistoryRecord r)
    {
        var ini = new IniFile();
        ini.Set("Meta", "SchemaVersion", IniSchema.CurrentVersion);
        ini.Set("Execution", "Id", r.Id);
        ini.Set("Execution", "StartedAt", r.StartedAt.ToString(TimeFormat, CultureInfo.InvariantCulture));
        if (r.FinishedAt is { } finished)
            ini.Set("Execution", "FinishedAt", finished.ToString(TimeFormat, CultureInfo.InvariantCulture));
        ini.Set("Execution", "Operator", Escape(r.Operator));
        ini.Set("Execution", "Machine", Escape(r.Machine));
        ini.Set("Execution", "Profile", Escape(r.Profile));
        ini.Set("Execution", "Component", Escape(r.Component));
        ini.Set("Execution", "BuildVersion", Escape(r.BuildVersion));
        ini.Set("Execution", "Operations", Escape(string.Join(",", r.Operations)));
        ini.Set("Execution", "Outcome", r.Outcome.ToString());
        ini.Set("Execution", "BackupDirectory", Escape(r.BackupDirectory));

        // [Error] hanya bila ada isinya (ADR-005: "hanya jika gagal").
        if (r.ErrorMessage.Length > 0 || r.RestoreFailures.Count > 0)
        {
            ini.Set("Error", "Message", Escape(r.ErrorMessage));
            ini.Set("Error", "RestoreFailures", Escape(string.Join("\n", r.RestoreFailures.Select(OneLine))));
        }
        return ini;
    }

    private static string OneLine(string s) => s.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');

    // ---------- Baca ----------

    private static (HistoryRecord? Record, List<string> Errors) Read(string path)
    {
        var (ini, errors) = IniSchema.TryLoad(path);
        if (ini is null)
            return (null, errors);

        errors.AddRange(IniSchema.CheckMeta(ini));
        errors.AddRange(ini.CheckSections("Meta", "Execution", "Error"));
        errors.AddRange(ini.CheckKeys("Execution",
            required: ["Id", "StartedAt", "Operator", "Machine", "Component", "Outcome"],
            optional: ["FinishedAt", "Profile", "BuildVersion", "Operations", "BackupDirectory"]));
        if (ini.HasSection("Error"))
            errors.AddRange(ini.CheckKeys("Error", required: [], optional: ["Message", "RestoreFailures"]));
        if (errors.Count > 0)
            return (null, Prefix(path, errors));

        string Get(string section, string key) => Unescape(ini.Get(section, key) ?? "");

        string id = ini.Get("Execution", "Id")!;
        if (!string.Equals(id, Path.GetFileNameWithoutExtension(path), StringComparison.Ordinal))
            errors.Add($"Id '{id}' does not match the file name.");

        string outcomeText = ini.Get("Execution", "Outcome")!;
        if (!Enum.GetNames<HistoryOutcome>().Contains(outcomeText, StringComparer.Ordinal))
            errors.Add($"Outcome '{outcomeText}' is not one of: {string.Join(", ", Enum.GetNames<HistoryOutcome>())}.");

        var started = ParseTime(ini.Get("Execution", "StartedAt"), "StartedAt", errors);
        DateTimeOffset? finished = null;
        string? finishedText = ini.Get("Execution", "FinishedAt");
        if (!string.IsNullOrEmpty(finishedText))
            finished = ParseTime(finishedText, "FinishedAt", errors);

        if (errors.Count > 0)
            return (null, Prefix(path, errors));

        return (new HistoryRecord
        {
            Id = id,
            StartedAt = started!.Value,
            FinishedAt = finished,
            Operator = Get("Execution", "Operator"),
            Machine = Get("Execution", "Machine"),
            Profile = Get("Execution", "Profile"),
            Component = Get("Execution", "Component"),
            BuildVersion = Get("Execution", "BuildVersion"),
            Operations = Get("Execution", "Operations").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            Outcome = Enum.Parse<HistoryOutcome>(outcomeText),
            BackupDirectory = Get("Execution", "BackupDirectory"),
            ErrorMessage = Get("Error", "Message"),
            RestoreFailures = Get("Error", "RestoreFailures").Split('\n', StringSplitOptions.RemoveEmptyEntries),
        }, []);
    }

    private static DateTimeOffset? ParseTime(string? text, string key, List<string> errors)
    {
        if (DateTimeOffset.TryParseExact(text, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
            return value;
        errors.Add($"{key} '{text}' is not a time like 2026-10-28T10:15:30+07:00.");
        return null;
    }

    private static List<string> Prefix(string path, List<string> errors) =>
        errors.Select(e => e.StartsWith(path, StringComparison.OrdinalIgnoreCase) ? e : $"{path}: {e}").ToList();

    // ---------- Escape ----------
    // Nilai INI harus satu baris, tanpa " ;" dan tanpa spasi di ujung. Escape: \n \r \t \; \s (spasi di ujung)
    // \uXXXX (spasi unicode di ujung) dan \\. "\" hanya digandakan bila akan terbaca sebagai escape,
    // sehingga path seperti D:\ION-PackMan tetap terbaca apa adanya di Notepad.

    private static bool IsEscapeLetter(char c) => c is '\\' or '\n' or '\r' or '\t' or ';' or ' ' or 'n' or 'r' or 't' or 's' or 'u';

    public static string Escape(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            bool edge = i == 0 || i == s.Length - 1;
            switch (c)
            {
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case ';': sb.Append("\\;"); break;
                case ' ' when edge: sb.Append("\\s"); break;
                case '\\':
                    sb.Append(i + 1 < s.Length && IsEscapeLetter(s[i + 1]) ? "\\\\" : "\\");
                    break;
                default:
                    if (edge && char.IsWhiteSpace(c))
                        sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else
                        sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    public static string Unescape(string s)
    {
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c != '\\' || i + 1 >= s.Length)
            {
                sb.Append(c);
                continue;
            }

            switch (s[i + 1])
            {
                case '\\': sb.Append('\\'); i++; break;
                case 'n': sb.Append('\n'); i++; break;
                case 'r': sb.Append('\r'); i++; break;
                case 't': sb.Append('\t'); i++; break;
                case ';': sb.Append(';'); i++; break;
                case 's': sb.Append(' '); i++; break;
                case 'u' when i + 6 <= s.Length
                              && int.TryParse(s.AsSpan(i + 2, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int code):
                    sb.Append((char)code);
                    i += 5;
                    break;
                default: sb.Append('\\'); break;   // bukan escape: "\" apa adanya (mis. D:\ION)
            }
        }
        return sb.ToString();
    }
}
