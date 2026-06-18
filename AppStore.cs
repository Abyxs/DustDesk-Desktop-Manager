using System.Text.Json;
using System.Text.Json.Serialization;

namespace DustDesk;

public sealed class AppStore
{
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

    public AppConfig LoadConfig() => LoadJson(ConfigPath, () => new AppConfig());
    public TodoData LoadTodos() => LoadJson(TodoPath, () => new TodoData());
    public ProjectData LoadProjects() => LoadJson(ProjectPath, () => new ProjectData());
    public LaunchData LoadLaunchers() => LoadJson(LaunchPath, () => new LaunchData());
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
        var target = Path.GetFullPath(directory.Trim());
        return HasDataFiles(target);
    }

    public void SetDataDirectory(string directory, bool copyExistingData = true)
    {
        var target = Path.GetFullPath(directory.Trim());
        if (string.Equals(target, DataDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Directory.CreateDirectory(target);
        if (copyExistingData)
        {
            CopyExistingData(target);
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
                return Path.GetFullPath(configured);
            }
        }

        if (File.Exists(_legacyDataDirectorySettingPath))
        {
            var configured = File.ReadAllText(_legacyDataDirectorySettingPath).Trim();
            if (!string.IsNullOrWhiteSpace(configured))
            {
                var migrated = Path.GetFullPath(configured);
                SaveDataDirectorySetting(migrated);
                return migrated;
            }
        }

        if (Directory.Exists(_legacyDefaultDataDirectory) && !HasDataFiles(DefaultDataDirectory))
        {
            Directory.CreateDirectory(DefaultDataDirectory);
            CopyDataFiles(_legacyDefaultDataDirectory, DefaultDataDirectory);
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
        foreach (var file in Directory.GetFiles(sourceDirectory))
        {
            var target = Path.Combine(targetDirectory, Path.GetFileName(file));
            if (!File.Exists(target))
            {
                File.Copy(file, target);
            }
        }
    }

    private static bool HasDataFiles(string directory)
    {
        return Directory.Exists(directory)
            && new[] { "config.json", "todo.json", "project.json", "launch.json", "note.json", "note.md" }
                .Any(file => File.Exists(Path.Combine(directory, file)));
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
