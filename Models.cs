using System.Text.Json.Serialization;

namespace DustDesk;

public sealed class AppConfig
{
    public string MainWindowDisplayName { get; set; } = "";
    public bool DesktopWidgetTransparent { get; set; } = true;
    public int DesktopWidgetOpacity { get; set; } = 35;
    public bool DesktopOrganizerShowNames { get; set; }
    public int DesktopOrganizerIconSize { get; set; } = 48;
    public bool DesktopTodoWidgetTransparent { get; set; } = true;
    public bool DesktopProjectWidgetTransparent { get; set; } = true;
    public bool DesktopLauncherWidgetTransparent { get; set; } = true;
    public bool DesktopSystemMonitorWidgetTransparent { get; set; } = true;
    public bool DesktopClipboardWidgetTransparent { get; set; } = true;
    public bool DesktopSystemMonitorShowDownload { get; set; } = true;
    public bool DesktopSystemMonitorShowUpload { get; set; } = true;
    public bool DesktopSystemMonitorShowMemory { get; set; } = true;
    public bool DesktopSystemMonitorShowCpu { get; set; } = true;
    public bool DesktopSystemMonitorShowDiskIo { get; set; } = true;
    public bool DesktopSystemMonitorShowDiskSpace { get; set; } = true;
    public bool DesktopSystemMonitorShowPing { get; set; } = true;
    public bool DesktopSystemMonitorShowUptime { get; set; } = true;
    public bool DesktopLauncherWidgetSnap { get; set; }
    public bool DesktopLauncherWidgetShowNames { get; set; } = true;
    public int DesktopLauncherWidgetIconSize { get; set; } = 48;
    public bool StartHiddenToTray { get; set; }
    public string MainWindowHotKey { get; set; } = "Ctrl+Shift+K";
    public string DesktopOrganizerHotKey { get; set; } = "Ctrl+Shift+D";
    public bool DesktopHotKeyToggleSearch { get; set; }
    public bool DesktopHotKeyToggleOrganizer { get; set; } = true;
    public bool DesktopHotKeyToggleTodo { get; set; }
    public bool DesktopHotKeyToggleNote { get; set; }
    public bool DesktopHotKeyToggleProject { get; set; }
    public bool DesktopHotKeyToggleLauncher { get; set; }
    public bool DesktopHotKeyToggleSystemMonitor { get; set; }
    public bool DesktopHotKeyToggleClipboard { get; set; }
    public bool? SearchAppData { get; set; } = true;
    public bool? SearchDesktopFiles { get; set; } = true;
    public bool? SearchStartMenuApps { get; set; } = true;
    public bool? SearchProjectPaths { get; set; } = true;
    public bool? SearchCustomPaths { get; set; } = true;
    public List<string> SearchCustomRoots { get; set; } = new();
    public bool DesktopSearchWidgetTransparent { get; set; } = true;
    public WidgetPlacement? DesktopOrganizerWidget { get; set; }
    public WidgetPlacement? DesktopTodoWidget { get; set; }
    public WidgetPlacement? DesktopProjectWidget { get; set; }
    public WidgetPlacement? DesktopLauncherWidget { get; set; }
    public WidgetPlacement? DesktopSystemMonitorWidget { get; set; }
    public WidgetPlacement? DesktopSearchWidget { get; set; }
    public WidgetPlacement? DesktopClipboardWidget { get; set; }
    public List<DesktopNoteWidgetPlacement> DesktopNoteWidgets { get; set; } = new();

    public List<DeskCategory> DesktopCategories { get; set; } = new()
    {
        new DeskCategory { Name = "工作" },
        new DeskCategory { Name = "开发" },
        new DeskCategory { Name = "游戏" },
        new DeskCategory { Name = "工具" }
    };
}

public class WidgetPlacement
{
    public bool Visible { get; set; }
    public bool Locked { get; set; }
    public bool TopMost { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool AutoCollapseEnabled { get; set; }
    public bool IsCollapsed { get; set; }
    public int ExpandedWidth { get; set; }
    public int ExpandedHeight { get; set; }
}

public sealed class DesktopNoteWidgetPlacement : WidgetPlacement
{
    public string NoteId { get; set; } = "";
}

public sealed class DeskCategory
{
    public string Name { get; set; } = "";
    public bool IsCollapsed { get; set; }
    public List<string> ItemPaths { get; set; } = new();

    public override string ToString()
    {
        var state = IsCollapsed ? "折叠" : "展开";
        return $"{Name} ({ItemPaths.Count}, {state})";
    }
}

public sealed class TodoData
{
    public List<TodoItem> Items { get; set; } = new();
    public List<TodoTagPreset> TagPresets { get; set; } = new();
}

public sealed class TodoTagPreset
{
    public string Name { get; set; } = "";
    public int ColorArgb { get; set; }

    public override string ToString() => Name;
}

public sealed class NoteData
{
    public List<NoteItem> Items { get; set; } = new();
}

public sealed class NoteItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "note.md";
    public string Text { get; set; } = "";
    public int ColorArgb { get; set; } = unchecked((int)0xFFFFEC8E);
    public int FontColorArgb { get; set; } = unchecked((int)0xFF2C261C);
    public float FontSize { get; set; } = 13F;
    public bool FontBold { get; set; }
    public string? BackgroundImagePath { get; set; }
    public bool ImageOnly { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public override string ToString() => Title;
}

public sealed class TodoItem
{
    public string Text { get; set; } = "";
    public string Tag { get; set; } = "";
    public string Note { get; set; } = "";
    public bool Done { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? ReminderAt { get; set; }
    public DateTime? ReminderNotifiedAt { get; set; }

    public override string ToString() => Text;
}

public sealed class ProjectData
{
    public List<ProjectBoard> Projects { get; set; } = new();
}

public sealed class ProjectBoard
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string ProjectPath { get; set; } = "";
    public List<ProjectItem> Items { get; set; } = new();

    public override string ToString() => Name;
}

public sealed class ProjectItem
{
    public string Title { get; set; } = "";
    public ProjectStatus Status { get; set; } = ProjectStatus.Todo;
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int ProgressPercent { get; set; } = -1;
    public string ProjectPath { get; set; } = "";
    public List<ProjectSubItem> SubItems { get; set; } = new();

    public override string ToString()
    {
        var progress = ProgressPercent >= 0 ? $"  {Math.Clamp(ProgressPercent, 0, 100)}%" : "";
        var date = StartDate.HasValue || EndDate.HasValue
            ? $"  {StartDate?.ToString("MM/dd") ?? "--"}-{EndDate?.ToString("MM/dd") ?? "--"}"
            : "";
        return $"{Title}{progress}{date}";
    }
}

public sealed class ProjectSubItem
{
    public string Title { get; set; } = "";
    public bool Done { get; set; }
    public string FilePath { get; set; } = "";

    public override string ToString() => Title;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProjectStatus
{
    Todo,
    Doing,
    Done
}

public sealed class LaunchData
{
    public List<LaunchItem> Items { get; set; } = new();
}

public sealed class LaunchItem
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";

    public override string ToString() => Name;
}

public sealed class ClipboardData
{
    public List<ClipboardHistoryItem> Items { get; set; } = new();
}

public sealed class ClipboardHistoryItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public ClipboardHistoryKind Kind { get; set; }
    public string Text { get; set; } = "";
    public string ImagePngBase64 { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public bool IsLocked { get; set; }
    public bool IsPinned { get; set; }

    public override string ToString()
    {
        var type = Kind == ClipboardHistoryKind.Image ? "图片" : "文字";
        return $"{type}  {CreatedAt:yyyy-MM-dd HH:mm:ss}";
    }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ClipboardHistoryKind
{
    Text,
    Image
}
