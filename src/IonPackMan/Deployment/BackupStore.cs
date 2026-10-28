using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using IonPackMan.Ini;
using IonPackMan.Machine;

namespace IonPackMan.Deployment;

/// <summary>
/// Backup dan restore satu eksekusi (ADR-004, System Design §3.5). Komponen paling kritis keselamatan:
/// ia mengembalikan mesin ATM ke kondisi semula, termasuk setelah crash atau mati listrik.
///
/// Aturan yang dijaga di sini:
///   1. <see cref="Create"/> memeriksa dan membaca semuanya SEBELUM menulis apa pun, lalu hanya menulis di folder backup
///      eksekusi itu. <c>backup.ini</c> (Status=InProgress) ditulis TERAKHIR dan sebelum Create mengembalikan sukses.
///   2. <see cref="Restore(string)"/> selalu membaca <c>backup.ini</c> dari DISK (jalur yang sama untuk gagal biasa
///      dan pemulihan setelah crash), mengembalikan seluruh isinya, tidak berhenti pada kegagalan pertama, dan aman diulang.
///   3. <c>backup.ini</c> berada di folder yang bisa diubah sedangkan aplikasi berjalan sebagai admin, maka Restore
///      memeriksa SETIAP entri terhadap allow-list sebelum menyentuh apa pun. Entri di luar allow-list tidak disentuh
///      dan dihitung sebagai kegagalan restore.
///
/// Salinan file yang diganti disimpan di <c>files\&lt;huruf drive&gt;\&lt;path tanpa drive&gt;</c> (mis. D:\APCONTAINER\WIND\WIND.exe
/// menjadi <c>files\D\APCONTAINER\WIND\WIND.exe</c>), diturunkan dari path target yang sudah lolos allow-list,
/// sehingga <c>backup.ini</c> cukup memuat <c>1=path</c> dan tidak ada path relatif yang bisa dimanipulasi (path traversal).
/// </summary>
public sealed class BackupStore
{
    private const string BackupFileName = "backup.ini";
    private const string FilesFolderName = "files";
    private const string RegistryPrefix = "Registry:";
    private const string IniPrefix = "Ini:";
    private static readonly TimeSpan ServiceTimeout = TimeSpan.FromSeconds(30);

    private readonly string _cacheDir;
    private readonly IRegistryAccess _registry;
    private readonly IServiceControl _services;
    private readonly AllowList _allowList;

    /// <summary>
    /// Hanya untuk tes: dipanggil setelah satu file selesai disalin ke backup, dengan (path target, path salinan),
    /// tepat sebelum hash salinan dibandingkan dengan file asli. Tes memakainya untuk mensimulasikan file target
    /// yang berubah di tengah backup. Kode produksi tidak mengisinya.
    /// </summary>
    public Action<string, string>? AfterFileCopied { get; set; }

    public BackupStore(string cacheDir, IRegistryAccess registry, IServiceControl services, AllowList allowList)
    {
        _cacheDir = cacheDir;
        _registry = registry;
        _services = services;
        _allowList = allowList;
    }

    // =====================================================================================
    // Create
    // =====================================================================================

    /// <summary>
    /// Dipanggil SEBELUM perubahan apa pun pada mesin. Gagal (Backup null + Errors) bila ada yang tidak aman atau
    /// tidak bisa dipulihkan; dalam kasus itu folder backup yang baru dibuat dihapus dan mesin tidak disentuh.
    /// </summary>
    public (Backup? Backup, List<string> Errors) Create(DeploymentPlan plan)
    {
        // ---- Tahap A: hanya memeriksa dan membaca. Tidak ada yang ditulis di disk. ----
        if (!IsSafeExecId(plan.ExecId))
            return Fail($"ExecId '{plan.ExecId}' is not a valid backup folder name (letters, digits, '-', '_' and '.' only).");
        if (string.IsNullOrWhiteSpace(_cacheDir) || !Path.IsPathFullyQualified(_cacheDir))
            return Fail($"Local cache directory '{_cacheDir}' must be an absolute path.");

        string backupDir = Paths.BackupDir(_cacheDir, plan.ExecId);
        if (Directory.Exists(backupDir) || File.Exists(backupDir))
            return Fail($"Backup folder '{backupDir}' already exists (duplicate ExecId); it is never overwritten.");

        var errors = new List<string>();
        Backup backup;
        string text;
        try
        {
            backup = Snapshot(plan, backupDir, errors);
            if (errors.Count > 0)
                return (null, errors);

            // Uji dulu dalam memori: backup.ini harus bisa ditulis DAN dibaca kembali menjadi isi yang sama.
            // Dengan begitu nilai yang tidak bisa disimpan INI (baris baru, spasi di ujung, " ;") ditolak SEBELUM perubahan apa pun.
            text = ToIni(backup).ToText();
            var (parsed, parseErrors) = Parse(IniFile.Parse(text, BackupFileName), plan.ExecId, BackupFileName, backupDir);
            if (parsed is null || ToIni(parsed).ToText() != text)
                return (null, [.. parseErrors.Prepend("The plan cannot be recorded in backup.ini so that it can be read back; nothing was changed:")]);
        }
        catch (ArgumentException ex)
        {
            return Fail($"The plan cannot be recorded in backup.ini; nothing was changed: {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or TimeoutException)
        {
            return Fail($"Cannot read the current machine state for the backup; nothing was changed: {ex.Message}");
        }

        // ---- Tahap B: menulis, hanya di dalam backupDir. Gagal di mana pun = hapus HANYA folder ini. ----
        try
        {
            Directory.CreateDirectory(Paths.BackupsDir(_cacheDir));
            Directory.CreateDirectory(backupDir);

            foreach (string target in backup.ReplacedFiles)
            {
                string copy = CopyPathFor(backupDir, target);
                Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                File.Copy(target, copy, overwrite: false);
                AfterFileCopied?.Invoke(target, copy);

                if (!File.Exists(target) || HashOf(copy) != HashOf(target))
                {
                    errors.Add($"Backup copy of '{target}' does not match the original (the file changed while it was being backed up). Execution was cancelled; nothing was changed.");
                    return Abort(backupDir, errors);
                }
            }

            // backup.ini ditulis TERAKHIR (atomik lewat IniFile): ada backup.ini berarti semua salinan sudah terverifikasi.
            string iniPath = Path.Combine(backupDir, BackupFileName);
            ToIni(backup).Save(iniPath);
            if (IniFile.Load(iniPath).ToText() != text)
            {
                errors.Add($"'{iniPath}' was written but reads back differently. Execution was cancelled; nothing was changed.");
                return Abort(backupDir, errors);
            }
            return (backup, []);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or IniException)
        {
            errors.Add($"Backup could not be completed ({ex.Message}). Execution was cancelled; nothing was changed.");
            return Abort(backupDir, errors);
        }
    }

    private static (Backup?, List<string>) Fail(string error) => (null, [error]);

    private (Backup?, List<string>) Abort(string backupDir, List<string> errors)
    {
        try
        {
            Directory.Delete(backupDir, recursive: true); // folder ini baru dibuat oleh Create; tidak ada yang lain disentuh
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            errors.Add($"The incomplete backup folder '{backupDir}' could not be removed ({ex.Message}); it has no backup.ini and the machine was not changed, so it can be deleted manually.");
        }
        return (null, errors);
    }

    /// <summary>Memeriksa plan terhadap allow-list dan keadaan mesin sekarang, lalu membaca nilai lama. Hanya membaca.</summary>
    private Backup Snapshot(DeploymentPlan plan, string backupDir, List<string> errors)
    {
        var replaced = new List<string>();
        var newFiles = new List<string>();
        var seenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in plan.Files.Where(f => f.Change != FileChange.Same))
        {
            if (!_allowList.IsUnderBaseDirectory(file.TargetPath))
            {
                errors.Add($"File '{file.TargetPath}' is outside the allowed base directories.");
                continue;
            }
            string full = Path.GetFullPath(file.TargetPath);
            if (!seenTargets.Add(full))
            {
                errors.Add($"File '{full}' appears more than once in the plan.");
                continue;
            }

            if (file.Change == FileChange.Replace)
            {
                if (File.Exists(full))
                    replaced.Add(full);
                else
                    errors.Add($"File '{full}' should be replaced but no longer exists (the machine changed after Preview). Run Preview again.");
            }
            else // New
            {
                // Bila sudah ada, Restore akan menghapus file milik orang lain. Jangan pernah mencatatnya sebagai "baru".
                if (File.Exists(full) || Directory.Exists(full))
                    errors.Add($"'{full}' should be a new file but already exists (the machine changed after Preview). Run Preview again.");
                else
                    newFiles.Add(full);
            }
        }

        // Direktori yang belum ada dan akan dibuat oleh copy. Berhenti di direktori pertama yang sudah ada
        // atau yang bukan di bawah base directory (base directory sendiri tidak pernah dihapus).
        var newDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in newFiles)
        {
            string? dir = Path.GetDirectoryName(file);
            while (dir is not null && !Directory.Exists(dir) && _allowList.IsUnderBaseDirectory(dir))
            {
                newDirs.Add(dir);
                dir = Path.GetDirectoryName(dir);
            }
        }

        string serviceName = plan.ServiceName ?? "";
        if (serviceName.Length > 0 && !_allowList.IsServiceAllowed(serviceName))
            errors.Add($"Service '{serviceName}' is not in the allow-list.");

        var registryEntries = new List<BackupRegistryEntry>();
        foreach (var change in plan.RegistryChanges)
        {
            if (!_allowList.IsRegistryKeyAllowed(change.Key))
            {
                errors.Add($"Registry key '{change.Key}' (entry '{change.Name}') is not in the allow-list.");
                continue;
            }
            // Baca ulang; CurrentValue di plan bisa basi.
            var current = _registry.Read(change.Key, change.ValueName);
            registryEntries.Add(new BackupRegistryEntry(change.Name, change.Key, change.ValueName,
                Existed: current is not null, Type: current?.Type, OldValue: current?.Value ?? ""));
        }

        var iniEntries = new List<BackupIniEntry>();
        foreach (var change in plan.IniChanges)
        {
            if (!_allowList.IsIniFileAllowed(change.File))
            {
                errors.Add($"INI file '{change.File}' (entry '{change.Name}') is not in the allow-list.");
                continue;
            }
            var (exists, value) = AppIniEditor.Read(change.File, change.Section, change.Key);
            iniEntries.Add(new BackupIniEntry(change.Name, change.File, change.Section, change.Key, exists, exists ? value : ""));
        }

        CheckUniqueNames(registryEntries.Select(e => e.Name), "Registry", errors);
        CheckUniqueNames(iniEntries.Select(e => e.Name), "INI", errors);

        bool wasRunning = serviceName.Length > 0 && errors.Count == 0 && _services.GetState(serviceName) == ServiceState.Running;

        return new Backup
        {
            ExecId = plan.ExecId,
            BackupDir = backupDir,
            Status = BackupStatus.InProgress,
            ServiceName = serviceName,
            ServiceWasRunning = wasRunning,
            ReplacedFiles = replaced,
            NewFiles = newFiles,
            NewDirectories = DeepestFirst(newDirs),
            RegistryEntries = registryEntries,
            IniEntries = iniEntries,
        };
    }

    private static void CheckUniqueNames(IEnumerable<string> names, string kind, List<string> errors)
    {
        foreach (var dup in names.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            errors.Add($"{kind} change name '{dup.Key}' is used more than once; names must be unique because each one becomes a backup.ini section.");
    }

    // =====================================================================================
    // Restore
    // =====================================================================================

    /// <summary>Restore berdasarkan backup yang sudah ada; yang dipakai tetap isi <c>backup.ini</c> di disk.</summary>
    public List<string> Restore(Backup backup) => Restore(backup.ExecId);

    /// <summary>
    /// Mengembalikan SELURUH isi backup. Mengembalikan daftar kegagalan: kosong berarti <c>Status=Restored</c>;
    /// bila tidak kosong, <c>Status=RestoreIncomplete</c> dan tiap kegagalan ada di daftar.
    /// Backup berstatus Done ditolak (eksekusi itu sukses; mengembalikannya diam-diam akan membatalkan perubahan yang sah).
    /// </summary>
    public List<string> Restore(string execId)
    {
        var (backup, loadErrors) = Load(execId);
        if (backup is null)
            return [.. loadErrors.Prepend($"Backup '{execId}' cannot be restored because backup.ini is not usable; nothing was touched:")];
        if (backup.Status == BackupStatus.Done)
            return [$"Backup '{execId}' has Status=Done (the execution succeeded); it is not restored. Nothing was touched."];

        var failures = new List<string>();

        // ---- Periksa SEMUA entri terhadap allow-list sebelum menyentuh apa pun. ----
        var replaced = backup.ReplacedFiles.Where(p => AllowedFile(p, "replaced file", failures)).ToList();
        var newFiles = backup.NewFiles.Where(p => AllowedFile(p, "new file", failures)).ToList();
        var newDirs = DeepestFirst(backup.NewDirectories.Where(p => AllowedFile(p, "new directory", failures)));
        var registryEntries = backup.RegistryEntries.Where(e => AllowedRegistry(e, failures)).ToList();
        var iniEntries = backup.IniEntries.Where(e => AllowedIni(e, failures)).ToList();

        string service = backup.ServiceName;
        bool serviceOk = service.Length > 0;
        if (serviceOk && !_allowList.IsServiceAllowed(service))
        {
            failures.Add($"Skipped service '{service}': not in the allow-list.");
            serviceOk = false;
        }

        // (i) Stop service bila file akan dikembalikan/dihapus dan service masih aktif.
        if (serviceOk && (replaced.Count > 0 || newFiles.Count > 0))
            Attempt(failures, $"stop service '{service}'", () =>
            {
                if (IsActive(_services.GetState(service)))
                    _services.Stop(service, ServiceTimeout);
            });

        // (ii) File yang diganti: salin balik dari files\.
        foreach (string target in replaced)
            Attempt(failures, $"restore file '{target}'", () => RestoreFile(backup, target));

        // (iii) File baru dihapus; direktori baru hanya bila kosong (isi folder milik orang lain tidak disentuh).
        foreach (string file in newFiles)
            Attempt(failures, $"delete new file '{file}'", () =>
            {
                if (File.Exists(file))
                    File.Delete(file);
            });
        foreach (string dir in newDirs)
            Attempt(failures, $"remove new directory '{dir}'", () =>
            {
                if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                    Directory.Delete(dir, recursive: false);
            });

        // (iv) Registry: tulis nilai lama, atau hapus value bila dulu belum ada. Tidak ditulis bila sudah sama.
        foreach (var entry in registryEntries)
            Attempt(failures, $"restore registry '{entry.Key}' value '{entry.ValueName}'", () => RestoreRegistry(entry));

        // (v) INI aplikasi: tulis nilai lama, atau hapus key bila dulu belum ada. Tidak ditulis bila sudah sama
        //     (file yang tidak berubah tidak disentuh sama sekali).
        foreach (var entry in iniEntries)
            Attempt(failures, $"restore INI '{entry.File}' [{entry.Section}] {entry.Key}", () => RestoreIni(entry));

        // (vi) Service kembali ke state awal.
        if (serviceOk)
            Attempt(failures, $"return service '{service}' to its original state", () =>
            {
                var state = _services.GetState(service);
                if (backup.ServiceWasRunning && state != ServiceState.Running)
                    _services.StartAndWaitRunning(service, ServiceTimeout);
                else if (!backup.ServiceWasRunning && IsActive(state))
                    _services.Stop(service, ServiceTimeout);
            });

        // Status ditulis terakhir. Bila gagal ditulis, status lama (InProgress/RestoreIncomplete) tetap sehingga restore bisa diulang.
        try
        {
            WriteStatus(backup.BackupDir, failures.Count == 0 ? BackupStatus.Restored : BackupStatus.RestoreIncomplete);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or IniException)
        {
            failures.Add($"Could not update the status in backup.ini ({ex.Message}); the backup stays pending and Restore can be run again.");
        }
        return failures;
    }

    private void RestoreFile(Backup backup, string target)
    {
        string filesRoot = Path.Combine(backup.BackupDir, FilesFolderName);
        string copy = CopyPathFor(backup.BackupDir, target);
        if (!IsInside(filesRoot, copy))
            throw new IOException("backup copy path is outside the backup folder.");
        if (!File.Exists(copy))
            throw new FileNotFoundException($"backup copy '{copy}' is missing.");

        string expected = HashOf(copy);
        if (File.Exists(target) && HashOf(target) == expected)
            return; // sudah sama: jangan menulis (file yang dikunci service tidak perlu gagal tanpa alasan)

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(copy, target, overwrite: true);
        if (HashOf(target) != expected)
            throw new IOException("file was copied back but its content does not match the backup.");
    }

    private void RestoreRegistry(BackupRegistryEntry entry)
    {
        var current = _registry.Read(entry.Key, entry.ValueName);
        if (entry.Existed)
        {
            var old = new RegistryValue(entry.Type!.Value, entry.OldValue);
            if (current != old)
                _registry.Write(entry.Key, entry.ValueName, old.Type, old.Value);
        }
        else if (current is not null)
        {
            _registry.Delete(entry.Key, entry.ValueName);
        }
    }

    private static void RestoreIni(BackupIniEntry entry)
    {
        var (exists, value) = AppIniEditor.Read(entry.File, entry.Section, entry.Key);
        if (entry.Existed)
        {
            if (!exists || value != entry.OldValue)
                AppIniEditor.Write(entry.File, entry.Section, entry.Key, entry.OldValue);
        }
        else if (exists)
        {
            AppIniEditor.Delete(entry.File, entry.Section, entry.Key);
        }
    }

    private bool AllowedFile(string path, string what, List<string> failures)
    {
        if (_allowList.IsUnderBaseDirectory(path))
            return true;
        failures.Add($"Skipped {what} '{path}': not under an allowed base directory (backup.ini may have been altered).");
        return false;
    }

    private bool AllowedRegistry(BackupRegistryEntry entry, List<string> failures)
    {
        if (_allowList.IsRegistryKeyAllowed(entry.Key))
            return true;
        failures.Add($"Skipped registry key '{entry.Key}' (entry '{entry.Name}'): not in the allow-list.");
        return false;
    }

    private bool AllowedIni(BackupIniEntry entry, List<string> failures)
    {
        if (_allowList.IsIniFileAllowed(entry.File))
            return true;
        failures.Add($"Skipped INI file '{entry.File}' (entry '{entry.Name}'): not in the allow-list.");
        return false;
    }

    /// <summary>Menjalankan satu langkah; kegagalan dicatat dan langkah berikutnya tetap berjalan.</summary>
    private static void Attempt(List<string> failures, string what, Action step)
    {
        try
        {
            step();
        }
        catch (Exception ex)
        {
            failures.Add($"Could not {what}: {ex.Message}");
        }
    }

    private static bool IsActive(ServiceState state) =>
        state is ServiceState.Running or ServiceState.StartPending or ServiceState.Paused;

    // =====================================================================================
    // MarkDone, FindUnfinished, Load
    // =====================================================================================

    /// <summary>
    /// Menandai eksekusi sukses (<c>Status=Done</c>). Hanya dari status InProgress di disk. Melempar bila gagal;
    /// pemanggil (Deployer) lalu masuk ke jalur Restore, sehingga kegagalan di sini tidak meninggalkan mesin setengah jadi.
    /// </summary>
    public void MarkDone(Backup backup)
    {
        var (current, errors) = Load(backup.ExecId);
        if (current is null)
            throw new IOException($"Cannot mark backup '{backup.ExecId}' as done: {string.Join(" ", errors)}");
        if (current.Status != BackupStatus.InProgress)
            throw new InvalidOperationException($"Cannot mark backup '{backup.ExecId}' as done: its status is {current.Status}, expected InProgress.");
        WriteStatus(current.BackupDir, BackupStatus.Done);
    }

    /// <summary>
    /// Semua backup berstatus InProgress atau RestoreIncomplete. Folder backup yang tidak bisa dibaca TIDAK diabaikan:
    /// ia muncul di Problems (pengguna harus tahu; mungkin ada mesin yang belum dipulihkan).
    /// </summary>
    public (List<Backup> Unfinished, List<string> Problems) FindUnfinished()
    {
        var unfinished = new List<Backup>();
        var problems = new List<string>();
        string root = Paths.BackupsDir(_cacheDir);
        if (!Directory.Exists(root))
            return (unfinished, problems);

        string[] folders;
        try
        {
            folders = Directory.GetDirectories(root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problems.Add($"Cannot list backup folder '{root}': {ex.Message}");
            return (unfinished, problems);
        }

        foreach (string folder in folders.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            string name = Path.GetFileName(folder);
            if (!File.Exists(Path.Combine(folder, BackupFileName)))
            {
                problems.Add($"Backup folder '{folder}' has no backup.ini. If PackMan stopped during Create the machine was not changed and the folder can be deleted manually; otherwise inspect it manually.");
                continue;
            }
            var (backup, errors) = Load(name);
            if (backup is null)
                problems.Add($"Backup '{name}' has an unreadable backup.ini and must be inspected manually: {string.Join(" ", errors)}");
            else if (backup.Status is BackupStatus.InProgress or BackupStatus.RestoreIncomplete)
                unfinished.Add(backup);
        }
        return (unfinished, problems);
    }

    /// <summary>Membaca dan memeriksa <c>backup.ini</c> satu eksekusi. Backup null bila file hilang, rusak, atau tidak valid.</summary>
    public (Backup? Backup, List<string> Errors) Load(string execId)
    {
        if (!IsSafeExecId(execId))
            return (null, [$"ExecId '{execId}' is not a valid backup folder name."]);

        string dir = Paths.BackupDir(_cacheDir, execId);
        string path = Path.Combine(dir, BackupFileName);
        var (ini, errors) = IniSchema.TryLoad(path);
        return ini is null ? (null, errors) : Parse(ini, execId, path, dir);
    }

    // =====================================================================================
    // backup.ini: tulis dan baca (format System Design §3.5)
    // =====================================================================================

    private static IniFile ToIni(Backup b)
    {
        var ini = new IniFile();
        Put(ini, "Meta", "SchemaVersion", IniSchema.CurrentVersion, "Meta");
        Put(ini, "Backup", "ExecId", b.ExecId, "ExecId");
        Put(ini, "Backup", "Status", b.Status.ToString(), "Status");
        Put(ini, "Backup", "ServiceName", b.ServiceName, "ServiceName");
        Put(ini, "Backup", "ServiceWasRunning", b.ServiceWasRunning ? "true" : "false", "ServiceWasRunning");

        PutList(ini, "ReplacedFiles", b.ReplacedFiles);
        PutList(ini, "NewFiles", b.NewFiles);
        PutList(ini, "NewDirectories", b.NewDirectories);

        foreach (var e in b.RegistryEntries)
        {
            string s = RegistryPrefix + e.Name, what = $"registry entry '{e.Name}'";
            Put(ini, s, "Key", e.Key, what);
            Put(ini, s, "ValueName", e.ValueName, what);
            if (e.Existed)
                Put(ini, s, "Type", e.Type!.Value.ToString(), what);
            Put(ini, s, "Existed", e.Existed ? "true" : "false", what);
            Put(ini, s, "OldValue", e.OldValue, what);
        }
        foreach (var e in b.IniEntries)
        {
            string s = IniPrefix + e.Name, what = $"INI entry '{e.Name}'";
            Put(ini, s, "File", e.File, what);
            Put(ini, s, "Section", e.Section, what);
            Put(ini, s, "Key", e.Key, what);
            Put(ini, s, "Existed", e.Existed ? "true" : "false", what);
            Put(ini, s, "OldValue", e.OldValue, what);
        }
        return ini;
    }

    private static void PutList(IniFile ini, string section, IReadOnlyList<string> values)
    {
        for (int i = 0; i < values.Count; i++)
            Put(ini, section, (i + 1).ToString(CultureInfo.InvariantCulture), values[i], $"[{section}] item {i + 1}");
    }

    private static void Put(IniFile ini, string section, string key, string value, string what)
    {
        try
        {
            ini.Set(section, key, value);
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException($"{what} ('{value}'): {ex.Message}");
        }
    }

    private static (Backup?, List<string>) Parse(IniFile ini, string folderExecId, string source, string backupDir)
    {
        var problems = new List<string>();
        problems.AddRange(IniSchema.CheckMeta(ini));
        problems.AddRange(ini.CheckSections("Meta", "Backup", "ReplacedFiles", "NewFiles", "NewDirectories", RegistryPrefix, IniPrefix));
        problems.AddRange(ini.CheckKeys("Backup", ["ExecId", "Status", "ServiceWasRunning"], ["ServiceName"]));

        string? execId = ini.Get("Backup", "ExecId");
        if (execId is not null && !string.Equals(execId, folderExecId, StringComparison.OrdinalIgnoreCase))
            problems.Add($"[Backup] ExecId '{execId}' does not match the backup folder '{folderExecId}'.");

        BackupStatus status = default;
        string? statusText = ini.Get("Backup", "Status");
        if (statusText is not null && !TryParseStatus(statusText, out status))
            problems.Add($"[Backup] Status '{statusText}' is not one of InProgress, Done, Restored, RestoreIncomplete.");

        bool wasRunning = false;
        string? runningText = ini.Get("Backup", "ServiceWasRunning");
        if (runningText is not null && !TryParseBool(runningText, out wasRunning))
            problems.Add($"[Backup] ServiceWasRunning '{runningText}' must be true or false.");

        var replaced = ReadList(ini, "ReplacedFiles", problems);
        var newFiles = ReadList(ini, "NewFiles", problems);
        var newDirs = ReadList(ini, "NewDirectories", problems);

        var registryEntries = new List<BackupRegistryEntry>();
        var iniEntries = new List<BackupIniEntry>();
        foreach (string section in ini.SectionNames)
        {
            if (section.StartsWith(RegistryPrefix, StringComparison.OrdinalIgnoreCase))
                ParseRegistry(ini, section, registryEntries, problems);
            else if (section.StartsWith(IniPrefix, StringComparison.OrdinalIgnoreCase))
                ParseIniEntry(ini, section, iniEntries, problems);
        }

        if (problems.Count > 0)
            return (null, [.. problems.Select(p => $"{source}: {p}")]);

        return (new Backup
        {
            ExecId = execId!,
            BackupDir = backupDir,
            Status = status,
            ServiceName = ini.Get("Backup", "ServiceName") ?? "",
            ServiceWasRunning = wasRunning,
            ReplacedFiles = replaced,
            NewFiles = newFiles,
            NewDirectories = newDirs,
            RegistryEntries = registryEntries,
            IniEntries = iniEntries,
        }, []);
    }

    private static List<string> ReadList(IniFile ini, string section, List<string> problems)
    {
        var items = new List<(int Index, string Value)>();
        foreach (var (key, value) in ini.Section(section))
        {
            if (!int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out int index) || index < 1)
                problems.Add($"[{section}] key '{key}' must be a positive number.");
            else if (value.Length == 0)
                problems.Add($"[{section}] item {key} is empty.");
            else
                items.Add((index, value));
        }
        return items.OrderBy(i => i.Index).Select(i => i.Value).ToList();
    }

    private static void ParseRegistry(IniFile ini, string section, List<BackupRegistryEntry> entries, List<string> problems)
    {
        int before = problems.Count;
        problems.AddRange(ini.CheckKeys(section, ["Key", "ValueName", "Existed"], ["Type", "OldValue"]));
        if (!TryParseBool(ini.Get(section, "Existed") ?? "", out bool existed))
            problems.Add($"[{section}] Existed must be true or false.");

        RegistryValueType? type = null;
        string old = ini.Get(section, "OldValue") ?? "";
        if (existed)
        {
            string? typeText = ini.Get(section, "Type");
            if (typeText is null)
                problems.Add($"[{section}] Type is required when Existed=true.");
            else if (typeText.Equals("String", StringComparison.OrdinalIgnoreCase))
                type = RegistryValueType.String;
            else if (typeText.Equals("DWord", StringComparison.OrdinalIgnoreCase))
                type = RegistryValueType.DWord;
            else
                problems.Add($"[{section}] Type '{typeText}' must be String or DWord.");

            if (ini.Get(section, "OldValue") is null)
                problems.Add($"[{section}] OldValue is required when Existed=true.");
            else if (type == RegistryValueType.DWord && !uint.TryParse(old, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                problems.Add($"[{section}] OldValue '{old}' is not a valid DWORD.");
        }

        if (problems.Count == before)
            entries.Add(new BackupRegistryEntry(section[RegistryPrefix.Length..], ini.Get(section, "Key")!, ini.Get(section, "ValueName")!, existed, type, existed ? old : ""));
    }

    private static void ParseIniEntry(IniFile ini, string section, List<BackupIniEntry> entries, List<string> problems)
    {
        int before = problems.Count;
        problems.AddRange(ini.CheckKeys(section, ["File", "Section", "Key", "Existed"], ["OldValue"]));
        if (!TryParseBool(ini.Get(section, "Existed") ?? "", out bool existed))
            problems.Add($"[{section}] Existed must be true or false.");
        if (existed && ini.Get(section, "OldValue") is null)
            problems.Add($"[{section}] OldValue is required when Existed=true.");

        if (problems.Count == before)
            entries.Add(new BackupIniEntry(section[IniPrefix.Length..], ini.Get(section, "File")!, ini.Get(section, "Section")!,
                ini.Get(section, "Key")!, existed, existed ? ini.Get(section, "OldValue")! : ""));
    }

    private static bool TryParseBool(string text, out bool value)
    {
        value = text.Equals("true", StringComparison.OrdinalIgnoreCase);
        return value || text.Equals("false", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseStatus(string text, out BackupStatus status)
    {
        // Enum.TryParse menerima angka ("7"); di sini hanya nama yang dikenal.
        status = default;
        foreach (var candidate in Enum.GetValues<BackupStatus>())
            if (candidate.ToString().Equals(text, StringComparison.OrdinalIgnoreCase))
            {
                status = candidate;
                return true;
            }
        return false;
    }

    /// <summary>Menulis ulang HANYA Status di backup.ini yang ada (dibaca dari disk, disimpan atomik).</summary>
    private static void WriteStatus(string backupDir, BackupStatus status)
    {
        string path = Path.Combine(backupDir, BackupFileName);
        var ini = IniFile.Load(path);
        ini.Set("Backup", "Status", status.ToString());
        ini.Save(path);
    }

    // =====================================================================================
    // Helper path dan hash
    // =====================================================================================

    private static bool IsSafeExecId(string? id) =>
        !string.IsNullOrEmpty(id) && id.Length <= 100
        && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')
        && !id.StartsWith('.') && !id.EndsWith('.');

    /// <summary>D:\APCONTAINER\WIND\WIND.exe -> &lt;backupDir&gt;\files\D\APCONTAINER\WIND\WIND.exe (target harus sudah lolos allow-list).</summary>
    private static string CopyPathFor(string backupDir, string targetFullPath)
    {
        string full = Path.GetFullPath(targetFullPath);
        string root = Path.GetPathRoot(full) ?? "";
        string drive = root.TrimEnd('\\', ':');
        return Path.Combine(backupDir, FilesFolderName, drive, full[root.Length..]);
    }

    private static bool IsInside(string root, string path) =>
        Path.GetFullPath(path).StartsWith(Path.GetFullPath(root).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    private static List<string> DeepestFirst(IEnumerable<string> dirs) =>
        dirs.OrderByDescending(d => d.Count(c => c == '\\')).ThenBy(d => d, StringComparer.OrdinalIgnoreCase).ToList();

    private static string HashOf(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
