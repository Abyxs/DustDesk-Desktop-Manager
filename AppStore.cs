using System.Text.Json;
using System.Text.Json.Serialization;

namespace DustDesk;

public sealed class AppStore
{
    private static readonly string[] ManagedDataFiles =
    {
        "config.json",
        "todo.json",
        "project.json",
        "launch.json",
        "note.json",
        "note.md",
        "clipboard.json"
    };

    private static readonly string[] ManagedDataDirectories =
    {
        "DesktopOrganizer",
        "Launchers"
    };

    private readonly string _appDataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DustDesk");
    private readonly string _legacyDataDirectorySettingPath = Path.Combine(AppContext.BaseDirectory, "data-path.txt");
    private readonly string _legacyDefaultDataDirectory = Path.Combine(AppContext.BaseDirectory, "Data");
    private string DataDirectorySettingPath => Path.Combine(_appDataRoot, "data-path.txt");
    private string DefaultDataDirectory => Path.Combine(_appDataRoot, "Data");
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public AppStore()
    {
        DataDirectory = LoadDataDirectorySetting();
        Directory.CreateDirectory(DataDirectory);
    }

    public string DataDirectory { get; private set; }

    public string ConfigPath => Path.Combine(DataDirectory, "config.json");
    public string TodoPath => Path.Combine(DataDirectory, "todo.json");
    public string ProjectPath => Path.Combine(DataDirectory, "project.json");
    public string LaunchPath => Path.Combine(DataDirectory, "launch.json");
    public string NotePath => Path.Combine(DataDirectory, "note.md");
    public string NotesPath => Path.Combine(DataDirectory, "note.json");
    public string ClipboardPath => Path.Combine(DataDirectory, "clipboard.json");

    public AppConfig LoadConfig() => LoadJson(ConfigPath, () => new AppConfig());
    public TodoData LoadTodos() => LoadJson(TodoPath, () => new TodoData());
    public ProjectData LoadProjects() => LoadJson(ProjectPath, () => new ProjectData());
    public LaunchData LoadLaunchers() => LoadJson(LaunchPath, () => new LaunchData());
    public ClipboardData LoadClipboard() => LoadJson(ClipboardPath, () => new ClipboardData());
    public NoteData LoadNotes()
    {
        if (File.Exists(NotesPath))
        {
            var notes = LoadJson(NotesPath, CreateDefaultNotes);
            EnsureDefaultNote(notes);
            return notes;
        }

        var migrated = CreateDefaultNotes();
        migrated.Items[0].Text = File.Exists(NotePath) ? File.ReadAllText(NotePath) : "";
        SaveNotes(migrated);
        return migrated;
    }

    public void SaveConfig(AppConfig data) => SaveJson(ConfigPath, data);
    public void SaveTodos(TodoData data) => SaveJson(TodoPath, data);
    public void SaveProjects(ProjectData data) => SaveJson(ProjectPath, data);
    public void SaveLaunchers(LaunchData data) => SaveJson(LaunchPath, data);
    public void SaveClipboard(ClipboardData data) => SaveJson(ClipboardPath, data);
    public void SaveNotes(NoteData data)
    {
        EnsureDefaultNote(data);
        SaveJson(NotesPath, data);
        File.WriteAllText(NotePath, data.Items[0].Text);
    }

    public string LoadNote()
    {
        return LoadNotes().Items[0].Text;
    }

    public void SaveNote(string text)
    {
        var notes = LoadNotes();
        notes.Items[0].Text = text;
        notes.Items[0].UpdatedAt = DateTime.Now;
        SaveNotes(notes);
    }

    public bool HasDataDirectory(string directory)
    {
        var target = ResolveDataDirectory(directory);
        return HasDataFiles(target);
    }

    public string ResolveDataDirectory(string directory)
    {
        var target = Path.GetFullPath(directory.Trim());
        if (TryResolveAppResourceDirectory(target, out var appDataDirectory))
        {
            return appDataDirectory;
        }

        var dataChild = Path.Combine(target, "Data");
        if (HasDataFiles(dataChild) && (IsLikelyAppDirectory(target) || !HasDataFiles(target)))
        {
            return Path.GetFullPath(dataChild);
        }

        return HasDataFiles(target) ? target : HasDataFiles(dataChild) ? Path.GetFullPath(dataChild) : target;
    }

    public void SetDataDirectory(string directory, bool copyExistingData = true)
    {
        var target = ResolveDataDirectory(directory);
        if (string.Equals(target, DataDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Directory.CreateDirectory(target);
        var source = DataDirectory;
        if (copyExistingData)
        {
            CopyExistingData(target);
            RemapCopiedDataDirectoryReferences(target, source);
        }

        SaveDataDirectorySetting(target);
        DataDirectory = target;
    }

    private string LoadDataDirectorySetting()
    {
        Directory.CreateDirectory(_appDataRoot);
        if (File.Exists(DataDirectorySettingPath))
        {
            var configured = File.ReadAllText(DataDirectorySettingPath).Trim();
            if (!string.IsNullOrWhiteSpace(configured))
            {
                var resolved = ResolveDataDirectory(configured);
                if (!string.Equals(Path.GetFullPath(configured), resolved, StringComparison.OrdinalIgnoreCase))
                {
                    SaveDataDirectorySetting(resolved);
                }

                return resolved;
            }
        }

        if (File.Exists(_legacyDataDirectorySettingPath))
        {
            var configured = File.ReadAllText(_legacyDataDirectorySettingPath).Trim();
            if (!string.IsNullOrWhiteSpace(configured))
            {
                var migrated = ResolveDataDirectory(configured);
                SaveDataDirectorySetting(migrated);
                return migrated;
            }
        }

        if (Directory.Exists(_legacyDefaultDataDirectory) && !HasDataFiles(DefaultDataDirectory))
        {
            Directory.CreateDirectory(DefaultDataDirectory);
            CopyDataFiles(_legacyDefaultDataDirectory, DefaultDataDirectory);
            RemapCopiedDataDirectoryReferences(DefaultDataDirectory, _legacyDefaultDataDirectory);
        }

        SaveDataDirectorySetting(DefaultDataDirectory);
        return DefaultDataDirectory;
    }

    private void CopyExistingData(string targetDirectory)
    {
        if (!Directory.Exists(DataDirectory))
        {
            return;
        }

        CopyDataFiles(DataDirectory, targetDirectory);
    }

    private void SaveDataDirectorySetting(string directory)
    {
        Directory.CreateDirectory(_appDataRoot);
        File.WriteAllText(DataDirectorySettingPath, Path.GetFullPath(directory));
    }

    private static void CopyDataFiles(string sourceDirectory, string targetDirectory)
    {
        var sourceRoot = TrimPathEnd(Path.GetFullPath(sourceDirectory));
        var targetRoot = TrimPathEnd(Path.GetFullPath(targetDirectory));
        if (string.Equals(sourceRoot, targetRoot, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Directory.CreateDirectory(targetDirectory);
        foreach (var fileName in ManagedDataFiles)
        {
            var fullPath = Path.GetFullPath(Path.Combine(sourceRoot, fileName));
            if (!File.Exists(fullPath))
            {
                continue;
            }

            if (IsSubPathOf(fullPath, targetRoot))
            {
                continue;
            }

            var target = Path.Combine(targetRoot, fileName);
            if (!File.Exists(target))
            {
                File.Copy(fullPath, target);
            }
        }

        foreach (var directoryName in ManagedDataDirectories)
        {
            CopyManagedDirectory(
                Path.Combine(sourceRoot, directoryName),
                Path.Combine(targetRoot, directoryName),
                targetRoot);
        }
    }

    private static void CopyManagedDirectory(string sourceDirectory, string targetDirectory, string targetRoot)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            return;
        }

        Directory.CreateDirectory(targetDirectory);
        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var fullPath = Path.GetFullPath(directory);
            if (IsSubPathOf(fullPath, targetRoot))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(sourceDirectory, fullPath);
            Directory.CreateDirectory(Path.Combine(targetDirectory, relativePath));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var fullPath = Path.GetFullPath(file);
            if (IsSubPathOf(fullPath, targetRoot))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(sourceDirectory, fullPath);
            if (relativePath.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relativePath))
            {
                continue;
            }

            var target = Path.Combine(targetDirectory, relativePath);
            var directory = Path.GetDirectoryName(target);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (!File.Exists(target))
            {
                File.Copy(fullPath, target);
            }
        }
    }

    private void RemapCopiedDataDirectoryReferences(string targetDirectory, string sourceDirectory)
    {
        var configPath = Path.Combine(targetDirectory, "config.json");
        if (File.Exists(configPath))
        {
            var config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(configPath), _jsonOptions);
            if (config is not null && RemapConfigDataPaths(config, sourceDirectory, targetDirectory))
            {
                SaveJsonFile(configPath, config);
            }
        }

        var launchPath = Path.Combine(targetDirectory, "launch.json");
        if (File.Exists(launchPath))
        {
            var launchers = JsonSerializer.Deserialize<LaunchData>(File.ReadAllText(launchPath), _jsonOptions);
            if (launchers is not null && RemapLauncherDataPaths(launchers, sourceDirectory, targetDirectory))
            {
                SaveJsonFile(launchPath, launchers);
            }
        }
    }

    private static bool RemapConfigDataPaths(AppConfig config, string sourceDirectory, string targetDirectory)
    {
        var changed = false;
        foreach (var category in config.DesktopCategories)
        {
            for (var i = 0; i < category.ItemPaths.Count; i++)
            {
                category.ItemPaths[i] = RemapDataPath(category.ItemPaths[i], sourceDirectory, targetDirectory, ref changed);
            }
        }

        return changed;
    }

    private static bool RemapLauncherDataPaths(LaunchData launchers, string sourceDirectory, string targetDirectory)
    {
        var changed = false;
        foreach (var item in launchers.Items)
        {
            item.Path = RemapDataPath(item.Path, sourceDirectory, targetDirectory, ref changed);
        }

        return changed;
    }

    private static string RemapDataPath(string path, string sourceDirectory, string targetDirectory, ref bool changed)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        var sourceRoot = TrimPathEnd(Path.GetFullPath(sourceDirectory));
        var targetRoot = TrimPathEnd(Path.GetFullPath(targetDirectory));
        var fullPath = Path.GetFullPath(path);
        if (!IsSubPathOf(fullPath, sourceRoot))
        {
            return path;
        }

        var relativePath = Path.GetRelativePath(sourceRoot, fullPath);
        var remapped = Path.GetFullPath(Path.Combine(targetRoot, relativePath));
        if (!string.Equals(path, remapped, StringComparison.OrdinalIgnoreCase))
        {
            changed = true;
        }

        return remapped;
    }

    private void SaveJsonFile<T>(string path, T data)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{path}.tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(data, _jsonOptions));
        File.Move(tempPath, path, overwrite: true);
    }

    private static bool IsSubPathOf(string path, string directory)
    {
        var root = TrimPathEnd(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(path);
        return target.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static string TrimPathEnd(string path)
    {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool HasDataFiles(string directory)
    {
        return Directory.Exists(directory)
            && ManagedDataFiles.Any(file => File.Exists(Path.Combine(directory, file)));
    }

    private static bool IsLikelyAppDirectory(string directory)
    {
        return File.Exists(Path.Combine(directory, "DustDesk.exe"))
            || File.Exists(Path.Combine(directory, "DustDesk.dll"))
            || Directory.Exists(Path.Combine(directory, "images"));
    }

    private static bool TryResolveAppResourceDirectory(string directory, out string dataDirectory)
    {
        dataDirectory = "";
        var current = new DirectoryInfo(Path.GetFullPath(directory));
        while (current is not null)
        {
            if (string.Equals(current.Name, "images", StringComparison.OrdinalIgnoreCase))
            {
                var appRoot = current.Parent?.FullName;
                if (string.IsNullOrWhiteSpace(appRoot))
                {
                    return false;
                }

                var siblingDataDirectory = Path.Combine(appRoot, "Data");
                if (!HasDataFiles(siblingDataDirectory))
                {
                    return false;
                }

                dataDirectory = Path.GetFullPath(siblingDataDirectory);
                return true;
            }

            current = current.Parent;
        }

        return false;
    }

    private static NoteData CreateDefaultNotes() => new()
    {
        Items = { new NoteItem { Title = "note.md" } }
    };

    private static void EnsureDefaultNote(NoteData data)
    {
        if (data.Items.Count == 0)
        {
            data.Items.Add(new NoteItem { Title = "note.md" });
        }
    }

    private T LoadJson<T>(string path, Func<T> fallback) where T : class
    {
        if (!File.Exists(path))
        {
            var created = fallback();
            SaveJson(path, created);
            return created;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), _jsonOptions) ?? fallback();
        }
        catch (JsonException)
        {
            var backupPath = $"{path}.broken-{DateTime.Now:yyyyMMddHHmmss}";
            File.Copy(path, backupPath, overwrite: true);
            var created = fallback();
            SaveJson(path, created);
            return created;
        }
    }

    private void SaveJson<T>(string path, T data)
    {
        Directory.CreateDirectory(DataDirectory);
        var tempPath = $"{path}.tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(data, _jsonOptions));
        File.Move(tempPath, path, overwrite: true);
    }
}
