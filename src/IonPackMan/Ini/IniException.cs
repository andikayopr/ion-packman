namespace IonPackMan.Ini;

/// <summary>File INI tidak bisa dibaca atau isinya melanggar aturan. Pesan sudah memuat nama file dan nomor baris.</summary>
public sealed class IniException : Exception
{
    public string FileName { get; }
    public int Line { get; }

    public IniException(string fileName, int line, string problem)
        : base(line > 0 ? $"{fileName}, line {line}: {problem}" : $"{fileName}: {problem}")
    {
        FileName = fileName;
        Line = line;
    }
}
