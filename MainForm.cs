using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DustDesk;

public sealed class MainForm : Form
{
    private const int MaxLaunchers = 5;

    private static readonly Color[] TodoTagPalette =
    {
        Color.FromArgb(26, 135, 84),
        Color.FromArgb(13, 110, 253),
        Color.FromArgb(220, 53, 69),
        Color.FromArgb(255, 193, 7),
        Color.FromArgb(111, 66, 193),
        Color.FromArgb(32, 201, 151),
        Color.FromArgb(253, 126, 20),
        Color.FromArgb(108, 117, 125)
    };

    private const int WmSetRedraw = 0x000B;
    private const int ResizeBorder = 8;
    private const int WmNchittest = 0x0084;
    private const int HtClient = 1;
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;
    private const double WindowOpacity = 1.0;
    private const int WsSysMenu = 0x00080000;
    private const int WsMinimizeBox = 0x00020000;
    private const int WmHotKey = 0x0312;
    private const int MainWindowHotKeyId = 0x4444;
    private const string SearchSettingsIconPath = @"D:\APP\fuzhu\xiangmu\DustDesk\images\Menu\sousuo.png";
    private const string SettingsCenterIconPath = @"D:\APP\fuzhu\xiangmu\DustDesk\images\Menu\shezhizhognxin.png";
    private const uint RedrawInvalidate = 0x0001;
    private const uint RedrawInternalPaint = 0x0002;
    private const uint RedrawErase = 0x0004;
    private const uint RedrawAllChildren = 0x0080;
    private const uint RedrawUpdateNow = 0x0100;
    private const uint RedrawFrame = 0x0400;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;

    private readonly AppStore _store = new();
    private readonly AppConfig _config;
    private readonly TodoData _todos;
    private readonly NoteData _notes;
    private readonly ProjectData _projects;
    private readonly LaunchData _launchers;

    private readonly Panel _content = new BufferedPanel();
    private readonly SidebarMenu _nav = new();
    private readonly Dictionary<int, Image> _menuIcons = new();
    private readonly List<DesktopNoteWidgetForm> _desktopNoteWidgets = new();
    private readonly NotifyIcon _trayIcon;
    private readonly Icon _appIcon;
    private DesktopOrganizerWidgetForm? _desktopOrganizerWidget;
    private DesktopTodoWidgetForm? _desktopTodoWidget;
    private DesktopProjectWidgetForm? _desktopProjectWidget;
    private DesktopLauncherWidgetForm? _desktopLauncherWidget;
    private DesktopSearchWidgetForm? _desktopSearchWidget;
    private readonly List<DesktopOrganizerWidgetForm> _desktopOrganizerSplitWidgets = new();
    private readonly HashSet<DeskCategory> _splitDesktopCategories = new();
    private readonly List<DesktopProjectWidgetForm> _desktopProjectSplitWidgets = new();
    private readonly HashSet<string> _splitProjectIds = new(StringComparer.Ordinal);
    private ResizeMessageFilter? _resizeFilter;
    private System.Windows.Forms.Timer? _noteSaveTimer;
    private TextBox? _activeNoteBox;
    private NoteItem? _activeNoteItem;
    private NoteItem? _pendingNoteSelection;
    private FormWindowState _lastWindowState;
    private bool _closingApp;
    private bool _exitRequested;

    private static readonly Color BackColorMain = Color.FromArgb(22, 30, 42);
    private static readonly Color PanelColor = Color.FromArgb(46, 56, 72);
    private static readonly Color CardColor = Color.FromArgb(34, 43, 57);
    private static readonly Color CardBorderColor = Color.FromArgb(72, 90, 112);
    private static readonly Color TextColorMain = Color.FromArgb(238, 243, 249);
    private static readonly Color TextColorSubtle = Color.FromArgb(155, 168, 186);
    private static readonly Color AccentColor = Color.FromArgb(35, 107, 238);

    public MainForm()
    {
        Text = "DustDesk";
        FormBorderStyle = FormBorderStyle.None;
        Size = new Size(1680, 1050);
        MinimumSize = new Size(1680, 1050);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = BackColorMain;
        Opacity = WindowOpacity;
        DoubleBuffered = true;
        _appIcon = CreateAppIcon();
        Icon = _appIcon;
        _trayIcon = CreateTrayIcon();

        _config = _store.LoadConfig();
        _todos = _store.LoadTodos();
        if (EnsureTodoTagPresets())
        {
            SaveTodos();
        }
        _notes = _store.LoadNotes();
        _store.SaveNotes(_notes);
        _projects = _store.LoadProjects();
        if (RemoveEmptyDefaultProject())
        {
            _store.SaveProjects(_projects);
        }
        _launchers = _store.LoadLaunchers();

        LoadMenuIcons();
        BuildShell();
        ShowPage(0);
        RestoreDesktopWidgets();
        _resizeFilter = new ResizeMessageFilter(this);
        Application.AddMessageFilter(_resizeFilter);
        if (_config.StartHiddenToTray)
        {
            Shown += (_, _) => BeginInvoke(new Action(HideToTray));
        }
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.Style |= WsSysMenu | WsMinimizeBox;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeGlass.EnableAcrylic(Handle, Color.FromArgb(245, 18, 26, 38));
        RegisterMainWindowHotKey();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_exitRequested && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        _closingApp = true;
        FlushNote();
        if (_resizeFilter is not null)
        {
            Application.RemoveMessageFilter(_resizeFilter);
            _resizeFilter = null;
        }
        UnregisterHotKey(Handle, MainWindowHotKeyId);
        foreach (var icon in _menuIcons.Values)
        {
            icon.Dispose();
        }
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _appIcon.Dispose();
        if (_desktopOrganizerWidget is not null && !_desktopOrganizerWidget.IsDisposed)
        {
            _desktopOrganizerWidget.Close();
        }
        if (_desktopTodoWidget is not null && !_desktopTodoWidget.IsDisposed)
        {
            _desktopTodoWidget.Close();
        }
        if (_desktopProjectWidget is not null && !_desktopProjectWidget.IsDisposed)
        {
            _desktopProjectWidget.Close();
        }
        if (_desktopLauncherWidget is not null && !_desktopLauncherWidget.IsDisposed)
        {
            _desktopLauncherWidget.Close();
        }
        if (_desktopSearchWidget is not null && !_desktopSearchWidget.IsDisposed)
        {
            _desktopSearchWidget.Close();
        }
        foreach (var widget in _desktopOrganizerSplitWidgets.ToArray())
        {
            if (!widget.IsDisposed)
            {
                widget.Close();
            }
        }
        foreach (var widget in _desktopProjectSplitWidgets.ToArray())
        {
            if (!widget.IsDisposed)
            {
                widget.Close();
            }
        }
        foreach (var widget in _desktopNoteWidgets.ToArray())
        {
            if (!widget.IsDisposed)
            {
                widget.Close();
            }
        }
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        Application.ExitThread();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_lastWindowState == FormWindowState.Minimized && WindowState != FormWindowState.Minimized)
        {
            ForceFullRedrawSoon();
        }

        _lastWindowState = WindowState;
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        if (WindowState != FormWindowState.Minimized)
        {
            ForceFullRedrawSoon();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.K)
        {
            ShowQuickSearch();
            e.SuppressKeyPress = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotKey && m.WParam.ToInt32() == MainWindowHotKeyId)
        {
            RestoreFromTray();
            m.Result = IntPtr.Zero;
            return;
        }

        base.WndProc(ref m);

        if (m.Msg != WmNchittest || m.Result != (IntPtr)HtClient || WindowState == FormWindowState.Maximized)
        {
            return;
        }

        var hit = GetResizeHit(ClientSize, PointToClient(GetScreenPoint(m.LParam)));
        if (hit != HtClient)
        {
            m.Result = (IntPtr)hit;
        }
    }

    private static Point GetScreenPoint(IntPtr lParamPtr)
    {
        var lParam = lParamPtr.ToInt64();
        return new Point((short)(lParam & 0xffff), (short)((lParam >> 16) & 0xffff));
    }

    private static int GetResizeHit(Size clientSize, Point point)
    {
        var left = point.X <= ResizeBorder;
        var right = point.X >= clientSize.Width - ResizeBorder;
        var top = point.Y <= ResizeBorder;
        var bottom = point.Y >= clientSize.Height - ResizeBorder;

        return (left, right, top, bottom) switch
        {
            (true, false, true, false) => HtTopLeft,
            (false, true, true, false) => HtTopRight,
            (true, false, false, true) => HtBottomLeft,
            (false, true, false, true) => HtBottomRight,
            (true, false, false, false) => HtLeft,
            (false, true, false, false) => HtRight,
            (false, false, true, false) => HtTop,
            (false, false, false, true) => HtBottom,
            _ => HtClient
        };
    }

    private sealed class ResizeMessageFilter : IMessageFilter
    {
        private readonly MainForm _form;

        public ResizeMessageFilter(MainForm form)
        {
            _form = form;
        }

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != WmNchittest || _form.IsDisposed || _form.WindowState == FormWindowState.Maximized)
            {
                return false;
            }

            var control = Control.FromHandle(m.HWnd);
            if (control is null || !BelongsToForm(control))
            {
                return false;
            }

            var hit = GetResizeHit(_form.ClientSize, _form.PointToClient(GetScreenPoint(m.LParam)));
            if (hit == HtClient)
            {
                return false;
            }

            m.Result = (IntPtr)hit;
            return true;
        }

        private bool BelongsToForm(Control control)
        {
            Control? current = control;
            while (current is not null)
            {
                if (ReferenceEquals(current, _form))
                {
                    return true;
                }

                current = current.Parent;
            }

            return false;
        }
    }

    private void BuildShell()
    {
        var root = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent
        };

        var statusBar = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            BackColor = Color.FromArgb(24, 34, 48),
            Padding = new Padding(28, 0, 28, 0)
        };
        statusBar.Controls.Add(new Label
        {
            Text = $"数据存储位置：{_store.DataDirectory}",
            Dock = DockStyle.Left,
            Width = 520,
            ForeColor = TextColorSubtle,
            TextAlign = ContentAlignment.MiddleLeft
        });
        statusBar.Controls.Add(new Label
        {
            Text = "● 运行中",
            Dock = DockStyle.Right,
            Width = 120,
            ForeColor = Color.FromArgb(58, 214, 122),
            TextAlign = ContentAlignment.MiddleRight
        });
        statusBar.Controls.Add(new Label
        {
            Text = "版本：v1.0.0",
            Dock = DockStyle.Right,
            Width = 150,
            ForeColor = TextColorSubtle,
            TextAlign = ContentAlignment.MiddleRight
        });

        var sidebarColor = Color.FromArgb(20, 29, 42);
        var sidebar = new GlassPanel
        {
            Dock = DockStyle.Left,
            Width = 220,
            Radius = 0,
            BorderColor = sidebarColor,
            BackColor = sidebarColor,
            Padding = new Padding(14, 26, 14, 16)
        };

        var brand = new Label
        {
            Text = "◇  DustDesk\r\n   桌面管理工具",
            Dock = DockStyle.Top,
            Height = 78,
            ForeColor = TextColorMain,
            Font = new Font(Font.FontFamily, 10.5F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };

        _nav.Dock = DockStyle.Fill;
        _nav.BackColor = sidebarColor;
        _nav.ForeColor = TextColorMain;
        _nav.Font = new Font(Font.FontFamily, 10.5F, FontStyle.Bold);
        _nav.ItemHeight = 56;
        _nav.Icons = _menuIcons;
        _nav.SetItems(new[] { "主页", "桌面收纳", "工作记录", "便签", "项目管理", "快捷启动", "统计分析", "搜索设置", "设置中心" });
        _nav.SelectedIndexChanged += (_, _) =>
        {
            if (_nav.SelectedIndex >= 0)
            {
                ShowPage(_nav.SelectedIndex);
            }
        };

        sidebar.Controls.Add(_nav);
        sidebar.Controls.Add(brand);

        _content.Dock = DockStyle.Fill;
        _content.BackColor = BackColorMain;
        _content.Padding = new Padding(34, 0, 28, 26);

        var chrome = new Panel
        {
            Dock = DockStyle.Top,
            Height = 44,
            BackColor = Color.Transparent
        };
        chrome.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                NativeGlass.BeginMove(Handle);
            }
        };
        chrome.Controls.Add(CreateWindowDot(Color.FromArgb(255, 95, 86), (_, _) => HideToTray()));
        chrome.Controls.Add(CreateWindowDot(Color.FromArgb(255, 189, 46), (_, _) => WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized));
        chrome.Controls.Add(CreateWindowDot(Color.FromArgb(39, 201, 63), (_, _) => WindowState = FormWindowState.Minimized));

        var main = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent
        };
        main.Controls.Add(_content);
        main.Controls.Add(chrome);

        root.Controls.Add(main);
        root.Controls.Add(sidebar);
        root.Controls.Add(statusBar);
        Controls.Add(root);

        var resizeGrip = new ResizeGripControl
        {
            Size = new Size(24, 24),
            Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
            Left = ClientSize.Width - 28,
            Top = ClientSize.Height - 28
        };
        resizeGrip.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left && WindowState != FormWindowState.Maximized)
            {
                NativeGlass.BeginResize(Handle, HtBottomRight);
            }
        };
        Resize += (_, _) =>
        {
            resizeGrip.Left = ClientSize.Width - 28;
            resizeGrip.Top = ClientSize.Height - 28;
            resizeGrip.Visible = WindowState != FormWindowState.Maximized;
        };
        Controls.Add(resizeGrip);
        resizeGrip.BringToFront();
    }

    private void LoadMenuIcons()
    {
        string?[] files =
        {
            "zhuye.png",
            "zhuomianshouna.png",
            "gongzuojilu.png",
            "bianqian.png",
            "xiangmuguanli.png",
            "kuaijieqidong.png",
            "tongjifenxi.png",
            "sousuo.png",
            "shezhizhognxin.png"
        };

        for (var i = 0; i < files.Length; i++)
        {
            var path = i == 7 && File.Exists(SearchSettingsIconPath)
                ? SearchSettingsIconPath
                : i == 8 && File.Exists(SettingsCenterIconPath)
                ? SettingsCenterIconPath
                : files[i] is null
                    ? null
                    : FindMenuIconPath(files[i]!);
            if (path is null)
            {
                continue;
            }

            using var stream = new MemoryStream(File.ReadAllBytes(path));
            using var image = Image.FromStream(stream);
            _menuIcons[i] = new Bitmap(image);
        }
    }

    private static string? FindMenuIconPath(string fileName)
    {
        var roots = new[]
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory()
        };

        foreach (var root in roots)
        {
            var current = new DirectoryInfo(root);
            for (var depth = 0; current is not null && depth < 5; depth++, current = current.Parent)
            {
                var path = Path.Combine(current.FullName, "images", "Menu", fileName);
                if (File.Exists(path))
                {
                    return path;
                }
            }
        }

        return null;
    }

    private Control CreateSidebarLine(string text, bool enabled, int top)
    {
        var panel = new Panel
        {
            Height = 38,
            Dock = DockStyle.Top,
            Top = top,
            BackColor = Color.Transparent
        };
        panel.Controls.Add(new Label
        {
            Text = enabled ? "●" : "○",
            Dock = DockStyle.Right,
            Width = 30,
            ForeColor = enabled ? Color.FromArgb(58, 144, 255) : TextColorSubtle,
            TextAlign = ContentAlignment.MiddleRight
        });
        panel.Controls.Add(new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            ForeColor = TextColorSubtle,
            TextAlign = ContentAlignment.MiddleLeft
        });
        return panel;
    }

    private Control CreateWindowDot(Color color, EventHandler action)
    {
        var button = new Panel
        {
            Dock = DockStyle.Right,
            Width = 40,
            BackColor = Color.Transparent,
            Cursor = Cursors.Hand
        };
        button.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(color);
            e.Graphics.FillEllipse(brush, 8, 10, 24, 24);
        };
        button.Click += action;
        return button;
    }

    private static Icon CreateAppIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var backBrush = new LinearGradientBrush(new Rectangle(0, 0, 32, 32), Color.FromArgb(48, 132, 255), Color.FromArgb(23, 41, 66), 45F);
            g.FillEllipse(backBrush, new Rectangle(3, 3, 26, 26));
            using var borderPen = new Pen(Color.FromArgb(190, 225, 255), 2F);
            g.DrawEllipse(borderPen, new Rectangle(4, 4, 24, 24));
            using var markBrush = new SolidBrush(Color.White);
            var diamond = new[]
            {
                new Point(16, 8),
                new Point(23, 16),
                new Point(16, 24),
                new Point(9, 16)
            };
            g.FillPolygon(markBrush, diamond);
            using var innerBrush = new SolidBrush(Color.FromArgb(48, 132, 255));
            g.FillEllipse(innerBrush, new Rectangle(13, 13, 6, 6));
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private NotifyIcon CreateTrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("打开", null, (_, _) => RestoreFromTray());
        menu.Items.Add("退出", null, (_, _) => ExitApplication());

        var notifyIcon = new NotifyIcon
        {
            Icon = _appIcon,
            Text = "DustDesk",
            ContextMenuStrip = menu,
            Visible = true
        };
        notifyIcon.DoubleClick += (_, _) => RestoreFromTray();
        return notifyIcon;
    }

    private void HideToTray()
    {
        _trayIcon.Visible = true;
        Hide();
        ShowInTaskbar = false;
    }

    private void RestoreFromTray()
    {
        ShowInTaskbar = true;
        WindowState = FormWindowState.Normal;
        Show();
        BringToFront();
        Activate();
    }

    private void RegisterMainWindowHotKey()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        UnregisterHotKey(Handle, MainWindowHotKeyId);
        if (!TryParseHotKey(_config.MainWindowHotKey, out var modifiers, out var key))
        {
            _config.MainWindowHotKey = "Ctrl+Shift+K";
            modifiers = ModControl | ModShift;
            key = Keys.K;
            _store.SaveConfig(_config);
        }

        RegisterHotKey(Handle, MainWindowHotKeyId, modifiers, (uint)key);
    }

    private static bool TryParseHotKey(string? text, out uint modifiers, out Keys key)
    {
        modifiers = 0;
        key = Keys.None;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        foreach (var part in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL":
                    modifiers |= ModControl;
                    break;
                case "SHIFT":
                    modifiers |= ModShift;
                    break;
                case "ALT":
                    modifiers |= ModAlt;
                    break;
                case "WIN":
                case "WINDOWS":
                    modifiers |= ModWin;
                    break;
                default:
                    if (!Enum.TryParse(part, ignoreCase: true, out key))
                    {
                        return false;
                    }
                    break;
            }
        }

        return modifiers != 0 && key != Keys.None;
    }

    private void ExitApplication()
    {
        _exitRequested = true;
        _closingApp = true;
        _trayIcon.Visible = false;
        Close();
        Application.ExitThread();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private void ShowPage(int index)
    {
        FlushNote();
        RunWithoutRedraw(_content, () =>
        {
            var oldControls = _content.Controls.Cast<Control>().ToArray();
            var oldSet = oldControls.ToHashSet();
            var layoutSuspended = false;
            try
            {
                _content.SuspendLayout();
                layoutSuspended = true;

                switch (index)
                {
                    case 0:
                        BuildHomePage();
                        break;
                    case 1:
                        BuildDesktopPage();
                        break;
                    case 2:
                        BuildTodoPage();
                        break;
                    case 3:
                        BuildNotePage();
                        break;
                    case 4:
                        BuildProjectPage();
                        break;
                    case 5:
                        BuildLauncherPage();
                        break;
                    case 6:
                        BuildStatsPage();
                        break;
                    case 7:
                        BuildSearchSettingsPage();
                        break;
                    case 8:
                        BuildSettingsPage();
                        break;
                }

                var newControls = _content.Controls.Cast<Control>().Where(control => !oldSet.Contains(control)).ToArray();
                foreach (var control in newControls)
                {
                    control.BringToFront();
                }

                foreach (var control in oldControls)
                {
                    _content.Controls.Remove(control);
                    control.Dispose();
                }

                _content.ResumeLayout(true);
                layoutSuspended = false;
            }
            finally
            {
                if (layoutSuspended)
                {
                    _content.ResumeLayout(true);
                }
            }
        });
        if (_nav.SelectedIndex != index)
        {
            _nav.SelectedIndex = index;
        }
    }

    private static void RunWithoutRedraw(Control control, Action action)
    {
        if (!control.IsHandleCreated)
        {
            action();
            return;
        }

        SendMessage(control.Handle, WmSetRedraw, IntPtr.Zero, IntPtr.Zero);
        try
        {
            action();
        }
        finally
        {
            SendMessage(control.Handle, WmSetRedraw, new IntPtr(1), IntPtr.Zero);
            control.Invalidate(true);
            control.Update();
        }
    }

    [DllImport("user32.dll")]
    private static extern nint SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private void ForceFullRedrawSoon()
    {
        if (!IsHandleCreated || IsDisposed)
        {
            return;
        }

        BeginInvoke(new Action(() =>
        {
            if (!IsHandleCreated || IsDisposed || WindowState == FormWindowState.Minimized)
            {
                return;
            }

            RedrawWindow(
                Handle,
                IntPtr.Zero,
                IntPtr.Zero,
                RedrawInvalidate | RedrawInternalPaint | RedrawErase | RedrawAllChildren | RedrawFrame | RedrawUpdateNow);
            Invalidate(true);
            Update();
        }));
    }

    private void BuildHomePage()
    {
        var dashboard = new DashboardCanvas(_config, _todos, _projects, _launchers, _notes)
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent
        };
        dashboard.Navigate += index => _nav.SelectedIndex = index;
        dashboard.AddTodo += () =>
        {
            AddTodoFromPrompt();
            ShowPage(0);
        };
        dashboard.TodoChanged += () =>
        {
            SaveTodos();
        };
        dashboard.PinTodo += () =>
        {
            ShowDesktopTodoWidget();
        };
        dashboard.AddLauncher += () =>
        {
            if (AddLauncher())
            {
                ShowPage(0);
            }
        };
        dashboard.OrganizeDesktop += () =>
        {
            ShowDesktopOrganizerWidget();
        };
        dashboard.SearchRequested += ShowQuickSearch;
        _content.Controls.Add(dashboard);
    }

    private void ShowQuickSearch()
    {
        FlushNote();
        using var form = new QuickSearchForm(BuildQuickSearchEntries())
        {
            Icon = Icon,
            StartPosition = FormStartPosition.CenterParent
        };
        form.ShowDialog(this);
    }

    private void ShowDesktopSearchWidget(bool centerOnScreen = false, bool minimizeMain = false)
    {
        if (_desktopSearchWidget is null || _desktopSearchWidget.IsDisposed)
        {
            _desktopSearchWidget = new DesktopSearchWidgetForm(BuildQuickSearchEntries, SaveDesktopSearchPlacement, () => _config.DesktopSearchWidgetTransparent);
            _desktopSearchWidget.FormClosed += (_, _) =>
            {
                if (!_closingApp && _config.DesktopSearchWidget is not null)
                {
                    _config.DesktopSearchWidget.Visible = false;
                    _store.SaveConfig(_config);
                }

                _desktopSearchWidget = null;
            };
        }

        _config.DesktopSearchWidget ??= new WidgetPlacement();
        if (centerOnScreen || _config.DesktopSearchWidget.Width <= 0 || _config.DesktopSearchWidget.Height <= 0)
        {
            CenterSearchWidgetPlacement(_config.DesktopSearchWidget);
        }

        _config.DesktopSearchWidget.Visible = true;
        _store.SaveConfig(_config);
        _desktopSearchWidget.ShowAsDesktopWidget(_config.DesktopSearchWidget);
        if (minimizeMain)
        {
            BeginInvoke(new Action(() => WindowState = FormWindowState.Minimized));
        }
    }

    private void CenterSearchWidgetPlacement(WidgetPlacement placement)
    {
        var width = placement.Width > 0 ? placement.Width : 460;
        var height = 64;
        var work = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        placement.X = work.Left + Math.Max(24, (work.Width - width) / 2);
        placement.Y = work.Top + Math.Max(24, (work.Height - height) / 2);
        placement.Width = width;
        placement.Height = height;
    }

    private void CloseDesktopSearchWidget()
    {
        if (_config.DesktopSearchWidget is not null)
        {
            _config.DesktopSearchWidget.Visible = false;
            _store.SaveConfig(_config);
        }

        if (_desktopSearchWidget is not null && !_desktopSearchWidget.IsDisposed)
        {
            _desktopSearchWidget.Close();
        }
    }

    private void CloseDesktopOrganizerWidget()
    {
        if (_config.DesktopOrganizerWidget is not null)
        {
            _config.DesktopOrganizerWidget.Visible = false;
            _store.SaveConfig(_config);
        }

        foreach (var widget in _desktopOrganizerSplitWidgets.ToArray())
        {
            if (!widget.IsDisposed)
            {
                widget.Close();
            }
        }

        if (_desktopOrganizerWidget is not null && !_desktopOrganizerWidget.IsDisposed)
        {
            _desktopOrganizerWidget.Close();
        }
    }

    private void CloseDesktopTodoWidget()
    {
        if (_config.DesktopTodoWidget is not null)
        {
            _config.DesktopTodoWidget.Visible = false;
            _store.SaveConfig(_config);
        }

        if (_desktopTodoWidget is not null && !_desktopTodoWidget.IsDisposed)
        {
            _desktopTodoWidget.Close();
        }
    }

    private void CloseDesktopProjectWidget()
    {
        if (_config.DesktopProjectWidget is not null)
        {
            _config.DesktopProjectWidget.Visible = false;
            _store.SaveConfig(_config);
        }

        foreach (var widget in _desktopProjectSplitWidgets.ToArray())
        {
            if (!widget.IsDisposed)
            {
                widget.Close();
            }
        }

        if (_desktopProjectWidget is not null && !_desktopProjectWidget.IsDisposed)
        {
            _desktopProjectWidget.Close();
        }
    }

    private void CloseDesktopLauncherWidget()
    {
        if (_config.DesktopLauncherWidget is not null)
        {
            _config.DesktopLauncherWidget.Visible = false;
            _store.SaveConfig(_config);
        }

        if (_desktopLauncherWidget is not null && !_desktopLauncherWidget.IsDisposed)
        {
            _desktopLauncherWidget.Close();
        }
    }

    private List<QuickSearchEntry> BuildQuickSearchEntries()
    {
        var results = new List<QuickSearchEntry>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var includeAppData = SearchEnabled(_config.SearchAppData);
        var includeDesktopFiles = SearchEnabled(_config.SearchDesktopFiles);
        var includeStartMenuApps = SearchEnabled(_config.SearchStartMenuApps);
        var includeProjectPaths = SearchEnabled(_config.SearchProjectPaths);

        void AddEntry(string title, string type, string subtitle, Action open)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return;
            }

            results.Add(new QuickSearchEntry(title.Trim(), type, subtitle, open));
        }

        void AddPath(string path, string type)
        {
            if (string.IsNullOrWhiteSpace(path) || (!File.Exists(path) && !Directory.Exists(path)))
            {
                return;
            }

            var fullPath = Path.GetFullPath(path);
            if (!seenPaths.Add(fullPath))
            {
                return;
            }

            var title = Path.GetFileNameWithoutExtension(fullPath);
            if (string.IsNullOrWhiteSpace(title))
            {
                title = fullPath;
            }

            AddEntry(title, type, fullPath, () => OpenPath(fullPath));
        }

        if (includeAppData)
        {
            foreach (var launcher in _launchers.Items)
            {
                AddEntry(launcher.Name, "快捷启动", launcher.Path, () => OpenPath(launcher.Path));
            }

            foreach (var category in _config.DesktopCategories)
            {
                AddEntry(category.Name, "桌面分类", $"{category.ItemPaths.Count} 项", () => _nav.SelectedIndex = 1);
            }

            foreach (var todo in _todos.Items)
            {
                AddEntry(todo.Text, "工作记录", string.IsNullOrWhiteSpace(todo.Note) ? todo.Tag : todo.Note, OpenTodoManager);
            }

            foreach (var note in _notes.Items)
            {
                AddEntry(note.Title, "便签", FirstLine(note.Text), () => OpenNoteManager(note));
            }
        }

        foreach (var project in _projects.Projects)
        {
            if (includeAppData)
            {
                AddEntry(project.Name, "项目", project.ProjectPath, () =>
                {
                    if (Directory.Exists(project.ProjectPath))
                    {
                        OpenPath(project.ProjectPath);
                    }
                    else
                    {
                        OpenProjectManager();
                    }
                });
            }

            if (includeProjectPaths)
            {
                AddPath(project.ProjectPath, "项目路径");
            }

            foreach (var item in project.Items)
            {
                if (includeAppData)
                {
                    AddEntry(item.Title, "项目阶段", $"{project.Name}  {StatusText(item.Status)}", () =>
                    {
                        if (Directory.Exists(item.ProjectPath) || File.Exists(item.ProjectPath))
                        {
                            OpenPath(item.ProjectPath);
                        }
                        else
                        {
                            OpenProjectManager();
                        }
                    });
                }

                if (includeProjectPaths)
                {
                    AddPath(item.ProjectPath, "项目文件");
                }

                foreach (var subItem in item.SubItems)
                {
                    if (includeAppData)
                    {
                        AddEntry(subItem.Title, "项目事项", $"{project.Name} / {item.Title}", () =>
                        {
                            if (File.Exists(subItem.FilePath) || Directory.Exists(subItem.FilePath))
                            {
                                OpenPath(subItem.FilePath);
                            }
                            else
                            {
                                OpenProjectManager();
                            }
                        });
                    }

                    if (includeProjectPaths)
                    {
                        AddPath(subItem.FilePath, "项目文件");
                    }
                }
            }
        }

        foreach (var root in QuickSearchRoots())
        {
            AddPathsFromRoot(root);
        }

        return results;

        IEnumerable<string> QuickSearchRoots()
        {
            if (includeDesktopFiles)
            {
                yield return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
                yield return Path.Combine(_store.DataDirectory, "DesktopOrganizer");
            }

            if (includeStartMenuApps)
            {
                yield return Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
                yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);
                yield return Path.Combine(_store.DataDirectory, "Launchers");
            }

            if (includeProjectPaths)
            {
                foreach (var path in _projects.Projects.Select(project => project.ProjectPath)
                             .Concat(_projects.Projects.SelectMany(project => project.Items.Select(item => item.ProjectPath))))
                {
                    if (Directory.Exists(path))
                    {
                        yield return path;
                    }
                }
            }
        }

        void AddPathsFromRoot(string root)
        {
            const int maxIndexedPaths = 8000;
            const int maxScanMilliseconds = 1200;
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root) || seenPaths.Count >= maxIndexedPaths)
            {
                return;
            }

            var watch = Stopwatch.StartNew();
            foreach (var path in EnumerateFileSystemEntries(root))
            {
                AddPath(path, IsStartMenuPath(path) ? "应用" : "文件");
                if (seenPaths.Count >= maxIndexedPaths || watch.ElapsedMilliseconds >= maxScanMilliseconds)
                {
                    break;
                }
            }
        }

        static bool IsStartMenuPath(string path)
        {
            return string.Equals(Path.GetExtension(path), ".lnk", StringComparison.OrdinalIgnoreCase);
        }

        static IEnumerable<string> EnumerateFileSystemEntries(string root)
        {
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                var directory = pending.Pop();
                IEnumerable<string> entries;
                try
                {
                    entries = Directory.EnumerateFileSystemEntries(directory);
                }
                catch
                {
                    continue;
                }

                foreach (var entry in entries)
                {
                    yield return entry;
                    if (Directory.Exists(entry) && !IsReparsePoint(entry))
                    {
                        pending.Push(entry);
                    }
                }
            }
        }

        static bool IsReparsePoint(string path)
        {
            try
            {
                return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
            }
            catch
            {
                return true;
            }
        }

        static string FirstLine(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "";
            }

            var line = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).FirstOrDefault() ?? "";
            return line.Length <= 80 ? line : line[..80];
        }
    }

    private Control CreateHomeDesktopCard()
    {
        var card = CreateHomeCard("▣  桌面收纳", out var body, "管理", (_, _) => _nav.SelectedIndex = 1);
        body.Padding = new Padding(10, 8, 10, 10);

        var list = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            AutoScroll = true,
            BackColor = Color.Transparent
        };

        var categories = _config.DesktopCategories.Take(4).ToArray();
        for (var i = 0; i < categories.Length; i++)
        {
            list.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
            list.Controls.Add(CreateDesktopCategoryRow(categories[i]), 0, i);
        }

        var actions = CreateActionBar();
        actions.Height = 48;
        actions.Dock = DockStyle.Bottom;
        actions.Padding = new Padding(6, 8, 6, 0);
        var organizeButton = CreateButton("添加到桌面");
        var addButton = CreateSecondaryButton("添加分类");
        organizeButton.Click += (_, _) =>
        {
            ShowDesktopOrganizerWidget();
        };
        addButton.Click += (_, _) =>
        {
            var name = Prompt("新建分类", "分类名称");
            if (!string.IsNullOrWhiteSpace(name))
            {
                _config.DesktopCategories.Add(new DeskCategory { Name = name.Trim() });
                _store.SaveConfig(_config);
                ShowPage(0);
            }
        };
        actions.Controls.Add(organizeButton);
        actions.Controls.Add(addButton);

        body.Controls.Add(list);
        body.Controls.Add(actions);
        return card;
    }

    private void ShowDesktopOrganizerWidget(bool minimizeMain = false)
    {
        if (_desktopOrganizerWidget is null || _desktopOrganizerWidget.IsDisposed)
        {
            _desktopOrganizerWidget = new DesktopOrganizerWidgetForm(
                _config,
                _store,
                SaveDesktopOrganizerPlacement,
                () => _config.DesktopCategories.Where(category => !_splitDesktopCategories.Contains(category)));
            _desktopOrganizerWidget.ManageRequested += () =>
            {
                Show();
                WindowState = FormWindowState.Normal;
                BringToFront();
                Activate();
                _nav.SelectedIndex = 1;
            };
            _desktopOrganizerWidget.SplitRequested += SplitDesktopOrganizerWidget;
            _desktopOrganizerWidget.FormClosed += (_, _) =>
            {
                if (!_closingApp && _config.DesktopOrganizerWidget is not null)
                {
                    _config.DesktopOrganizerWidget.Visible = false;
                    _store.SaveConfig(_config);
                }
                _desktopOrganizerWidget = null;
            };
        }

        _config.DesktopOrganizerWidget ??= new WidgetPlacement();
        _config.DesktopOrganizerWidget.Visible = true;
        _store.SaveConfig(_config);
        _desktopOrganizerWidget.ShowAsDesktopWidget(_config.DesktopOrganizerWidget);
        if (minimizeMain)
        {
            BeginInvoke(new Action(() =>
            {
                WindowState = FormWindowState.Minimized;
            }));
        }
    }

    private void SplitDesktopOrganizerWidget(DeskCategory category)
    {
        if (_splitDesktopCategories.Contains(category))
        {
            return;
        }

        _splitDesktopCategories.Add(category);
        var splitWidget = new DesktopOrganizerWidgetForm(
            _config,
            _store,
            _ => { },
            () => _config.DesktopCategories.Where(item => ReferenceEquals(item, category)),
            isSplit: true);
        splitWidget.ManageRequested += () =>
        {
            Show();
            WindowState = FormWindowState.Normal;
            BringToFront();
            Activate();
            _nav.SelectedIndex = 1;
        };
        splitWidget.MergeRequested += () => splitWidget.Close();
        splitWidget.FormClosed += (_, _) =>
        {
            _desktopOrganizerSplitWidgets.Remove(splitWidget);
            if (!_closingApp)
            {
                _splitDesktopCategories.Remove(category);
                _desktopOrganizerWidget?.RefreshWidget();
            }
        };
        _desktopOrganizerSplitWidgets.Add(splitWidget);
        splitWidget.ShowAsDesktopWidget();
        _desktopOrganizerWidget?.RefreshWidget();
    }

    private void ShowDesktopTodoWidget(bool minimizeMain = false)
    {
        if (_desktopTodoWidget is null || _desktopTodoWidget.IsDisposed)
        {
            _desktopTodoWidget = new DesktopTodoWidgetForm(_todos, AddTodoFromDesktopWidget, SaveTodos, OpenTodoManager, SaveDesktopTodoPlacement, _config.DesktopTodoWidgetTransparent, value =>
            {
                _config.DesktopTodoWidgetTransparent = value;
                _store.SaveConfig(_config);
            });
            _desktopTodoWidget.FormClosed += (_, _) =>
            {
                if (!_closingApp && _config.DesktopTodoWidget is not null)
                {
                    _config.DesktopTodoWidget.Visible = false;
                    _store.SaveConfig(_config);
                }

                _desktopTodoWidget = null;
            };
        }

        _config.DesktopTodoWidget ??= new WidgetPlacement();
        _config.DesktopTodoWidget.Visible = true;
        _store.SaveConfig(_config);
        _desktopTodoWidget.ShowAsDesktopWidget(_config.DesktopTodoWidget);
        if (minimizeMain)
        {
            BeginInvoke(new Action(() =>
            {
                WindowState = FormWindowState.Minimized;
            }));
        }
    }

    private void ShowDesktopProjectWidget(bool minimizeMain = false)
    {
        if (_desktopProjectWidget is null || _desktopProjectWidget.IsDisposed)
        {
            _desktopProjectWidget = new DesktopProjectWidgetForm(
                () => _projects.Projects.Where(project => !_splitProjectIds.Contains(project.Id)),
                SaveDesktopProjectPlacement,
                _config.DesktopProjectWidgetTransparent,
                value =>
                {
                    _config.DesktopProjectWidgetTransparent = value;
                    _store.SaveConfig(_config);
                },
                SaveProjectsFromWidget);
            _desktopProjectWidget.ManageRequested += OpenProjectManager;
            _desktopProjectWidget.SplitRequested += SplitDesktopProjectWidget;
            _desktopProjectWidget.FormClosed += (_, _) =>
            {
                if (!_closingApp && _config.DesktopProjectWidget is not null)
                {
                    _config.DesktopProjectWidget.Visible = false;
                    _store.SaveConfig(_config);
                }

                _desktopProjectWidget = null;
            };
        }

        _config.DesktopProjectWidget ??= new WidgetPlacement();
        _config.DesktopProjectWidget.Visible = true;
        _store.SaveConfig(_config);
        _desktopProjectWidget.ShowAsDesktopWidget(_config.DesktopProjectWidget);
        if (minimizeMain)
        {
            BeginInvoke(new Action(() =>
            {
                WindowState = FormWindowState.Minimized;
            }));
        }
    }

    private void SplitDesktopProjectWidget(ProjectBoard project)
    {
        if (string.IsNullOrWhiteSpace(project.Id) || _splitProjectIds.Contains(project.Id))
        {
            return;
        }

        _splitProjectIds.Add(project.Id);
        var splitWidget = new DesktopProjectWidgetForm(
            () => _projects.Projects.Where(item => string.Equals(item.Id, project.Id, StringComparison.Ordinal)),
            _ => { },
            _config.DesktopProjectWidgetTransparent,
            _ => { },
            SaveProjectsFromWidget);
        splitWidget.ManageRequested += OpenProjectManager;
        splitWidget.FormClosed += (_, _) =>
        {
            _desktopProjectSplitWidgets.Remove(splitWidget);
            if (!_closingApp)
            {
                _splitProjectIds.Remove(project.Id);
                RefreshDesktopProjectWidget();
            }
        };
        _desktopProjectSplitWidgets.Add(splitWidget);
        splitWidget.ShowAsDesktopWidget();
        RefreshDesktopProjectWidget();
    }

    private void OpenProjectManager()
    {
        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }

        Show();
        BringToFront();
        Activate();
        _nav.SelectedIndex = 4;
    }

    private void SaveProjectsFromWidget()
    {
        _store.SaveProjects(_projects);
        RefreshDesktopProjectWidget();
    }

    private void ShowDesktopLauncherWidget(bool minimizeMain = false)
    {
        if (_desktopLauncherWidget is null || _desktopLauncherWidget.IsDisposed)
        {
            _desktopLauncherWidget = new DesktopLauncherWidgetForm(
                _launchers,
                SaveDesktopLauncherPlacement,
                AddLauncherFromPath,
                _config.DesktopLauncherWidgetTransparent,
                value =>
                {
                    _config.DesktopLauncherWidgetTransparent = value;
                    _store.SaveConfig(_config);
                },
                _config.DesktopLauncherWidgetSnap,
                value =>
                {
                    _config.DesktopLauncherWidgetSnap = value;
                    _store.SaveConfig(_config);
                });
            _desktopLauncherWidget.ManageRequested += OpenLauncherManager;
            _desktopLauncherWidget.FormClosed += (_, _) =>
            {
                if (!_closingApp && _config.DesktopLauncherWidget is not null)
                {
                    _config.DesktopLauncherWidget.Visible = false;
                    _store.SaveConfig(_config);
                }

                _desktopLauncherWidget = null;
            };
        }

        _config.DesktopLauncherWidget ??= new WidgetPlacement();
        _config.DesktopLauncherWidget.Visible = true;
        _store.SaveConfig(_config);
        _desktopLauncherWidget.ShowAsDesktopWidget(_config.DesktopLauncherWidget);
        if (minimizeMain)
        {
            BeginInvoke(new Action(() =>
            {
                WindowState = FormWindowState.Minimized;
            }));
        }
    }

    private void OpenLauncherManager()
    {
        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }

        Show();
        BringToFront();
        Activate();
        _nav.SelectedIndex = 5;
    }

    private void OpenTodoManager()
    {
        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }

        Show();
        BringToFront();
        Activate();
        _nav.SelectedIndex = 2;
    }

    private void AddTodoFromDesktopWidget(IWin32Window? owner)
    {
        var item = ShowTodoEditor(null, owner);
        if (item is null)
        {
            return;
        }

        _todos.Items.Add(item);
        SaveTodos();
    }

    private Control CreateDesktopCategoryRow(DeskCategory category)
    {
        var previewItems = GetCategoryPreviewItems(category).ToArray();
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            BackColor = Color.FromArgb(30, 39, 52),
            Margin = new Padding(0, 0, 0, 8),
            Padding = new Padding(10, 6, 10, 6)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));

        row.Controls.Add(new Label
        {
            Text = $"■ {category.Name}\r\n   {previewItems.Length}个",
            Dock = DockStyle.Fill,
            ForeColor = TextColorMain,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        var apps = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            AutoScroll = true,
            BackColor = Color.Transparent
        };
        foreach (var item in previewItems)
        {
            apps.Controls.Add(CreateSmallAppTile(item));
        }
        row.Controls.Add(apps, 1, 0);

        var add = CreateIconButton("+");
        add.Click += (_, _) => _nav.SelectedIndex = 1;
        row.Controls.Add(add, 2, 0);
        return row;
    }

    private Control CreateHomeTodoCard()
    {
        var card = CreateHomeCard("▣  今日工作记录", out var body, "+", (_, _) =>
        {
            AddTodoFromPrompt();
            ShowPage(0);
        }, Color.FromArgb(82, 160, 255));
        body.Padding = new Padding(10, 8, 10, 10);

        var list = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            AutoScroll = true,
            BackColor = Color.Transparent
        };

        var active = _todos.Items.Where(item => !item.Done).Take(5).ToArray();
        if (active.Length == 0)
        {
            list.Controls.Add(CreateEmptyHint("暂无待办任务"), 0, 0);
        }
        else
        {
            for (var i = 0; i < active.Length; i++)
            {
                list.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
                list.Controls.Add(CreateTodoRow(active[i]), 0, i);
            }
        }

        var completed = _todos.Items.Count(item => item.Done);
        var footer = new Label
        {
            Text = $"已完成（{completed}）",
            Dock = DockStyle.Bottom,
            Height = 34,
            ForeColor = TextColorSubtle,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 0, 0)
        };
        body.Controls.Add(list);
        body.Controls.Add(footer);
        return card;
    }

    private Control CreateTodoRow(TodoItem item)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            BackColor = Color.FromArgb(28, 38, 52),
            Margin = new Padding(0, 0, 0, 6),
            Padding = new Padding(8, 5, 8, 5)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78));

        var check = new CheckBox
        {
            Dock = DockStyle.Fill,
            Checked = item.Done,
            BackColor = Color.Transparent
        };
        check.CheckedChanged += (_, _) =>
        {
            item.Done = check.Checked;
            SaveTodos();
            ShowPage(0);
        };
        row.Controls.Add(check, 0, 0);
        row.Controls.Add(new Label
        {
            Text = item.Text,
            Dock = DockStyle.Fill,
            ForeColor = item.Done ? TextColorSubtle : TextColorMain,
            TextAlign = ContentAlignment.MiddleLeft
        }, 1, 0);
        row.Controls.Add(new Label
        {
            Text = item.CreatedAt.ToString("HH:mm"),
            Dock = DockStyle.Fill,
            ForeColor = TextColorSubtle,
            TextAlign = ContentAlignment.MiddleRight
        }, 2, 0);
        row.Controls.Add(CreateBadge(GetTodoTagText(item), GetTodoTagColor(item.Tag)), 3, 0);
        BindTodoDetailInteraction(row, item);
        return row;
    }

    private Control CreateHomeNoteCard()
    {
        var card = CreateHomeCard("▣  快捷便签", out var body, "+", (_, _) => _nav.SelectedIndex = 3, Color.FromArgb(255, 190, 70));
        body.Padding = new Padding(12);
        var noteItem = EnsureNoteItem();

        var note = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            BorderStyle = BorderStyle.None,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Color.FromArgb(noteItem.ColorArgb),
            ForeColor = Color.FromArgb(60, 48, 20),
            Font = new Font(Font.FontFamily, 10.5F),
            Text = string.IsNullOrWhiteSpace(noteItem.Text) ? "灵感记录\r\n\r\n- " : noteItem.Text
        };
        _activeNoteBox = note;
        _activeNoteItem = noteItem;
        _noteSaveTimer = new System.Windows.Forms.Timer { Interval = 700 };
        _noteSaveTimer.Tick += (_, _) =>
        {
            _noteSaveTimer.Stop();
            SaveActiveNote();
        };
        note.TextChanged += (_, _) =>
        {
            _noteSaveTimer.Stop();
            _noteSaveTimer.Start();
        };

        body.Controls.Add(note);
        return card;
    }

    private Control CreateHomeProjectCard()
    {
        var card = CreateHomeCard("▣  项目管理", out var body, "管理", (_, _) => _nav.SelectedIndex = 4);
        body.Padding = new Padding(14, 8, 14, 14);

        var project = _projects.Projects.FirstOrDefault();
        if (project is null)
        {
            body.Controls.Add(CreateEmptyHint("暂无项目"));
            return card;
        }

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            BackColor = Color.Transparent
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));

        var projects = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            BackColor = Color.Transparent
        };
        var shownProjects = _projects.Projects.Take(4).ToArray();
        for (var i = 0; i < shownProjects.Length; i++)
        {
            projects.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            projects.Controls.Add(CreateProjectTab(shownProjects[i], shownProjects[i] == project), 0, i);
        }

        grid.Controls.Add(projects, 0, 0);
        grid.Controls.Add(CreateProjectStatusColumn(project, "进行中", ProjectStatus.Doing), 1, 0);
        grid.Controls.Add(CreateProjectStatusColumn(project, "待完成", ProjectStatus.Todo), 2, 0);
        grid.Controls.Add(CreateProjectStatusColumn(project, "已完成", ProjectStatus.Done), 3, 0);
        body.Controls.Add(grid);
        return card;
    }

    private Control CreateProjectTab(ProjectBoard project, bool selected)
    {
        return new Label
        {
            Text = $"  {project.Name}    {project.Items.Count}",
            Dock = DockStyle.Fill,
            BackColor = selected ? AccentColor : Color.Transparent,
            ForeColor = selected ? Color.White : TextColorMain,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 8, 6)
        };
    }

    private Control CreateProjectStatusColumn(ProjectBoard project, string title, ProjectStatus status)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            BackColor = Color.Transparent,
            Padding = new Padding(8, 0, 0, 0)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        panel.Controls.Add(new Label
        {
            Text = $"{title}（{project.Items.Count(item => item.Status == status)}）",
            Dock = DockStyle.Fill,
            ForeColor = TextColorSubtle,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        var items = project.Items.Where(item => item.Status == status).Take(4).ToArray();
        for (var i = 0; i < items.Length; i++)
        {
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            panel.Controls.Add(CreateProjectItemRow(items[i]), 0, i + 1);
        }
        return panel;
    }

    private Control CreateProjectItemRow(ProjectItem item)
    {
        var color = item.Status switch
        {
            ProjectStatus.Doing => Color.FromArgb(197, 135, 22),
            ProjectStatus.Done => Color.FromArgb(26, 135, 84),
            _ => Color.FromArgb(35, 107, 238)
        };
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            BackColor = Color.Transparent
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
        row.Controls.Add(new Label
        {
            Text = $"□  {item.Title}  {ProjectProgressPercent(item, item.Status)}%{ProjectDateRangeText(item)}",
            Dock = DockStyle.Fill,
            ForeColor = TextColorMain,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        row.Controls.Add(CreateBadge(StatusText(item.Status), color), 1, 0);
        return row;
    }

    private Control CreateHomeStatsCard()
    {
        var card = CreateHomeCard("▣  本周统计", out var body, "+", (_, _) => _nav.SelectedIndex = 6);
        body.Padding = new Padding(16, 8, 16, 14);

        var total = Math.Max(1, _todos.Items.Count + _projects.Projects.SelectMany(p => p.Items).Count() + _launchers.Items.Count);
        body.Controls.Add(CreateProgressLine("工作记录", _todos.Items.Count, total, AccentColor));
        body.Controls.Add(CreateProgressLine("项目事项", _projects.Projects.SelectMany(p => p.Items).Count(), total, Color.FromArgb(197, 135, 22)));
        body.Controls.Add(CreateProgressLine("快捷启动", _launchers.Items.Count, total, Color.FromArgb(58, 214, 122)));
        body.Controls.Add(new Label
        {
            Text = $"总计    {total} 项",
            Dock = DockStyle.Bottom,
            Height = 34,
            ForeColor = TextColorMain,
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font(Font.FontFamily, 10F, FontStyle.Bold)
        });
        return card;
    }

    private Control CreateProgressLine(string title, int value, int total, Color color)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 30,
            ColumnCount = 3,
            BackColor = Color.Transparent
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
        row.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            ForeColor = TextColorMain,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        row.Controls.Add(new ProgressBar
        {
            Dock = DockStyle.Fill,
            Maximum = Math.Max(1, total),
            Value = Math.Min(value, Math.Max(1, total)),
            ForeColor = color
        }, 1, 0);
        row.Controls.Add(new Label
        {
            Text = value.ToString(),
            Dock = DockStyle.Fill,
            ForeColor = TextColorSubtle,
            TextAlign = ContentAlignment.MiddleRight
        }, 2, 0);
        return row;
    }

    private Control CreateHomeLauncherCard()
    {
        var card = CreateHomeCard("▣  快捷启动", out var body, "⚙", (_, _) => _nav.SelectedIndex = 5);
        body.Padding = new Padding(14, 10, 14, 14);

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 2,
            BackColor = Color.Transparent
        };
        for (var i = 0; i < 4; i++)
        {
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        }
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        var items = _launchers.Items.Take(MaxLaunchers).ToArray();
        for (var i = 0; i < items.Length; i++)
        {
            grid.Controls.Add(CreateLauncherTile(items[i]), i % 4, i / 4);
        }
        if (_launchers.Items.Count < MaxLaunchers)
        {
            grid.Controls.Add(CreateAddLauncherTile(), items.Length % 4, items.Length / 4);
        }

        body.Controls.Add(grid);
        return card;
    }

    private Control CreateLauncherTile(LaunchItem item)
    {
        var button = new Button
        {
            Text = $"●\r\n{item.Name}",
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.Transparent,
            ForeColor = TextColorMain,
            Font = new Font(Font.FontFamily, 9F),
            Cursor = Cursors.Hand,
            Margin = new Padding(2)
        };
        button.FlatAppearance.BorderSize = 0;
        button.Click += (_, _) => OpenPath(item.Path);
        return button;
    }

    private Control CreateAddLauncherTile()
    {
        var button = new Button
        {
            Text = "",
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.Transparent,
            ForeColor = TextColorSubtle,
            Font = new Font(Font.FontFamily, 10F),
            Cursor = Cursors.Hand,
            Margin = new Padding(2)
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(96, 118, 140);
        button.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var icon = new Rectangle(button.Width / 2 - 15, 8, 30, 30);
            DrawTinajiaIcon(e.Graphics, icon, Color.FromArgb(85, 214, 130));
            TextRenderer.DrawText(e.Graphics, "添加", Font, new Rectangle(0, 42, button.Width, 22), TextColorSubtle, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        };
        button.Click += (_, _) =>
        {
            if (AddLauncher())
            {
                ShowPage(0);
            }
        };
        return button;
    }

    private GlassPanel CreateHomeCard(string title, out Panel body, string? actionText = null, EventHandler? action = null, Color? actionIconColor = null)
    {
        var card = new GlassPanel
        {
            Dock = DockStyle.Fill,
            Radius = 10,
            BackColor = Color.FromArgb(118, 30, 40, 56),
            BorderColor = CardBorderColor,
            Padding = new Padding(1),
            Margin = new Padding(0, 0, 18, 16)
        };

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 50,
            BackColor = Color.Transparent,
            Padding = new Padding(16, 0, 12, 0)
        };
        header.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            ForeColor = TextColorMain,
            Font = new Font(Font.FontFamily, 12.5F, FontStyle.Regular),
            TextAlign = ContentAlignment.MiddleLeft
        });
        if (!string.IsNullOrWhiteSpace(actionText))
        {
            var actionButton = CreateHeaderButton(actionText, actionIconColor);
            if (action is not null)
            {
                actionButton.Click += action;
            }
            header.Controls.Add(actionButton);
        }

        body = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent
        };
        card.Controls.Add(body);
        card.Controls.Add(header);
        return card;
    }

    private bool RemoveEmptyDefaultProject()
    {
        return _projects.Projects.RemoveAll(project =>
            string.Equals(project.Name, "道心无尘", StringComparison.Ordinal)
            && string.IsNullOrWhiteSpace(project.ProjectPath)
            && project.Items.Count == 0) > 0;
    }

    private static string GetGreeting()
    {
        var hour = DateTime.Now.Hour;
        return hour < 12 ? "上午好" : hour < 18 ? "下午好" : "晚上好";
    }

    private static string GetWeekText(DayOfWeek day)
    {
        return day switch
        {
            DayOfWeek.Monday => "星期一",
            DayOfWeek.Tuesday => "星期二",
            DayOfWeek.Wednesday => "星期三",
            DayOfWeek.Thursday => "星期四",
            DayOfWeek.Friday => "星期五",
            DayOfWeek.Saturday => "星期六",
            _ => "星期日"
        };
    }

    private IEnumerable<string> GetCategoryPreviewItems(DeskCategory category)
    {
        return category.ItemPaths
            .Where(path => File.Exists(path) || Directory.Exists(path))
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>();
    }

    private Control CreateSmallAppTile(string text)
    {
        return new Label
        {
            Text = $"●\r\n{text}",
            Width = 58,
            Height = 54,
            ForeColor = TextColorMain,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(Font.FontFamily, 8.5F),
            Margin = new Padding(0, 0, 10, 0)
        };
    }

    private void AddTodoFromPrompt()
    {
        var item = ShowTodoEditor();
        if (item is null)
        {
            return;
        }

        _todos.Items.Add(item);
        SaveTodos();
    }

    private void SaveTodos()
    {
        _store.SaveTodos(_todos);
        RefreshDesktopTodoWidget();
    }

    private bool EnsureTodoTagPresets()
    {
        var changed = false;
        foreach (var item in _todos.Items)
        {
            var tag = item.Tag.Trim();
            if (item.Tag != tag)
            {
                item.Tag = tag;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(tag) || _todos.TagPresets.Any(p => string.Equals(p.Name, tag, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            _todos.TagPresets.Add(new TodoTagPreset { Name = tag, ColorArgb = NextTodoTagColorArgb() });
            changed = true;
        }

        return changed;
    }

    private int NextTodoTagColorArgb()
    {
        return TodoTagPalette[_todos.TagPresets.Count % TodoTagPalette.Length].ToArgb();
    }

    private TodoTagPreset GetOrCreateTodoTagPreset(string tag)
    {
        var normalized = tag.Trim();
        var preset = _todos.TagPresets.FirstOrDefault(item => string.Equals(item.Name, normalized, StringComparison.OrdinalIgnoreCase));
        if (preset is not null)
        {
            return preset;
        }

        preset = new TodoTagPreset
        {
            Name = normalized,
            ColorArgb = NextTodoTagColorArgb()
        };
        _todos.TagPresets.Add(preset);
        return preset;
    }

    private Color GetTodoTagColor(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return Color.FromArgb(70, 82, 100);
        }

        var preset = _todos.TagPresets.FirstOrDefault(item => string.Equals(item.Name, tag.Trim(), StringComparison.OrdinalIgnoreCase));
        return Color.FromArgb((preset?.ColorArgb).GetValueOrDefault(TodoTagPalette[0].ToArgb()));
    }

    private static string GetTodoTagText(TodoItem item)
    {
        return string.IsNullOrWhiteSpace(item.Tag) ? "未分类" : item.Tag.Trim();
    }

    private static string FormatTodoListDisplay(TodoItem item)
    {
        return $"{item.Text}    {item.CreatedAt:MM-dd HH:mm}    [{GetTodoTagText(item)}]";
    }

    private void ShowTodoDetails(TodoItem item)
    {
        var note = string.IsNullOrWhiteSpace(item.Note) ? "无" : item.Note.Trim();
        MessageBox.Show(this,
            $"任务名称：{item.Text}\n标签：{GetTodoTagText(item)}\n创建时间：{item.CreatedAt:yyyy-MM-dd HH:mm}\n\n备注：\n{note}",
            "任务详情",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void BindTodoDetailInteraction(Control control, TodoItem item)
    {
        var menu = new ContextMenuStrip { ShowImageMargin = false };
        menu.Items.Add("查看详情", null, (_, _) => ShowTodoDetails(item));

        ToolTip? tip = null;
        if (!string.IsNullOrWhiteSpace(item.Note))
        {
            tip = new ToolTip
            {
                AutomaticDelay = 250,
                ReshowDelay = 100,
                AutoPopDelay = 8000
            };
        }

        void Attach(Control current)
        {
            current.ContextMenuStrip = menu;
            if (tip is not null)
            {
                tip.SetToolTip(current, item.Note);
            }

            foreach (Control child in current.Controls)
            {
                Attach(child);
            }
        }

        Attach(control);
    }

    private TodoItem? ShowTodoEditor(TodoItem? source = null, IWin32Window? owner = null)
    {
        using var form = new Form
        {
            Text = source is null ? "新增任务" : "编辑任务",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.None,
            MinimizeBox = false,
            MaximizeBox = false,
            ClientSize = new Size(460, 380),
            BackColor = Color.FromArgb(17, 24, 39),
            Font = new Font("Microsoft YaHei UI", 9F),
            ForeColor = Color.FromArgb(241, 245, 249),
            ShowInTaskbar = false
        };
        form.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(71, 85, 105));
            e.Graphics.DrawRectangle(pen, 0, 0, form.ClientSize.Width - 1, form.ClientSize.Height - 1);
        };
        var chrome = new Panel
        {
            Dock = DockStyle.Top,
            Height = 44,
            BackColor = Color.FromArgb(15, 23, 42)
        };
        chrome.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                NativeGlass.BeginMove(form.Handle);
            }
        };
        var titleLabel = new Label
        {
            Text = form.Text,
            Dock = DockStyle.Fill,
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Regular),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(18, 0, 0, 0)
        };
        titleLabel.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                NativeGlass.BeginMove(form.Handle);
            }
        };
        var closeButton = new Button
        {
            Text = "×",
            Dock = DockStyle.Right,
            Width = 44,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(15, 23, 42),
            ForeColor = Color.FromArgb(226, 232, 240),
            DialogResult = DialogResult.Cancel,
            Font = new Font("Microsoft YaHei UI", 14F)
        };
        closeButton.FlatAppearance.BorderSize = 0;
        chrome.Controls.Add(titleLabel);
        chrome.Controls.Add(closeButton);
        var separator = new Panel
        {
            Left = 0,
            Top = 44,
            Width = form.ClientSize.Width,
            Height = 1,
            BackColor = Color.FromArgb(51, 65, 85),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        Label CreateLabel(string text, int top)
        {
            return new Label
            {
                Text = text,
                Left = 24,
                Top = top + 44,
                Width = 410,
                Height = 22,
                ForeColor = Color.FromArgb(203, 213, 225)
            };
        }

        TextBox CreateInput(int top, string value)
        {
            return new TextBox
            {
                Left = 24,
                Top = top + 44,
                Width = 410,
                Height = 30,
                Text = value,
                BackColor = Color.FromArgb(248, 250, 252),
                ForeColor = Color.FromArgb(15, 23, 42),
                BorderStyle = BorderStyle.FixedSingle
            };
        }

        var nameLabel = CreateLabel("任务名称", 20);
        var nameBox = CreateInput(46, source?.Text ?? "");

        var tagLabel = CreateLabel("标签", 86);
        var tagBox = new ComboBox
        {
            Left = 24,
            Top = 156,
            Width = 320,
            DropDownStyle = ComboBoxStyle.DropDown,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(248, 250, 252),
            ForeColor = Color.FromArgb(15, 23, 42)
        };
        foreach (var preset in _todos.TagPresets.OrderBy(item => item.Name))
        {
            tagBox.Items.Add(preset.Name);
        }
        tagBox.Text = source?.Tag ?? "";

        var tagPreview = new Panel
        {
            Left = 360,
            Top = 156,
            Width = 74,
            Height = 28,
            BackColor = GetTodoTagColor(tagBox.Text)
        };

        var noteLabel = CreateLabel("备注（可选）", 152);
        var noteBox = new TextBox
        {
            Left = 24,
            Top = 222,
            Width = 410,
            Height = 90,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Text = source?.Note ?? "",
            BackColor = Color.FromArgb(248, 250, 252),
            ForeColor = Color.FromArgb(15, 23, 42),
            BorderStyle = BorderStyle.FixedSingle
        };

        void RefreshTagPreview()
        {
            tagPreview.BackColor = GetTodoTagColor(tagBox.Text);
        }

        tagBox.TextChanged += (_, _) => RefreshTagPreview();
        tagBox.SelectedIndexChanged += (_, _) => RefreshTagPreview();

        var okButton = new Button
        {
            Text = "确定",
            Left = 264,
            Top = 330,
            Width = 80,
            Height = 32,
            DialogResult = DialogResult.None,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(37, 99, 235),
            ForeColor = Color.White
        };
        okButton.FlatAppearance.BorderSize = 0;
        var cancelButton = new Button
        {
            Text = "取消",
            Left = 354,
            Top = 330,
            Width = 80,
            Height = 32,
            DialogResult = DialogResult.Cancel,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(51, 65, 85),
            ForeColor = Color.FromArgb(226, 232, 240)
        };
        cancelButton.FlatAppearance.BorderSize = 0;

        TodoItem? result = null;
        okButton.Click += (_, _) =>
        {
            var name = nameBox.Text.Trim();
            var tag = tagBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show(form, "请填写任务名称。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (string.IsNullOrWhiteSpace(tag))
            {
                MessageBox.Show(form, "请填写标签。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            GetOrCreateTodoTagPreset(tag);
            result = new TodoItem
            {
                Text = name,
                Tag = tag,
                Note = noteBox.Text.Trim(),
                Done = source?.Done ?? false,
                CreatedAt = source?.CreatedAt ?? DateTime.Now
            };
            form.DialogResult = DialogResult.OK;
            form.Close();
        };

        form.Controls.AddRange(new Control[] { chrome, separator, nameLabel, nameBox, tagLabel, tagBox, tagPreview, noteLabel, noteBox, okButton, cancelButton });
        form.AcceptButton = okButton;
        form.CancelButton = cancelButton;
        form.Shown += (_, _) =>
        {
            nameBox.Focus();
            nameBox.SelectAll();
            RefreshTagPreview();
        };

        return form.ShowDialog(owner ?? this) == DialogResult.OK ? result : null;
    }

    private bool AddLauncher()
    {
        if (!CanAddLauncher())
        {
            return false;
        }

        using var dialog = new OpenFileDialog
        {
            Filter = "程序或快捷方式|*.exe;*.lnk;*.bat;*.cmd|所有文件|*.*",
            Title = "选择要收藏的软件"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return false;
        }

        var defaultName = Path.GetFileNameWithoutExtension(dialog.FileName);
        var name = Prompt("添加快捷启动", "显示名称", defaultName);
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        return AddLauncherFromPath(dialog.FileName, name.Trim(), showLimitMessage: false);
    }

    private bool AddLauncherFromPath(string sourcePath, string? displayName = null, bool showLimitMessage = true)
    {
        if (!CanAddLauncher(showLimitMessage))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(sourcePath) || (!File.Exists(sourcePath) && !Directory.Exists(sourcePath)))
        {
            return false;
        }

        var name = string.IsNullOrWhiteSpace(displayName)
            ? Path.GetFileNameWithoutExtension(sourcePath)
            : displayName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            name = Path.GetFileName(sourcePath);
        }

        var launcherPath = PersistLauncherPath(sourcePath);
        _launchers.Items.Add(new LaunchItem { Name = name, Path = launcherPath });
        _store.SaveLaunchers(_launchers);
        return true;
    }

    private bool CanAddLauncher(bool showMessage = true)
    {
        if (_launchers.Items.Count < MaxLaunchers)
        {
            return true;
        }

        if (showMessage)
        {
            MessageBox.Show(this, $"快捷启动最多添加 {MaxLaunchers} 个。", "DustDesk", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        return false;
    }

    private string PersistLauncherPath(string sourcePath)
    {
        if (!File.Exists(sourcePath))
        {
            return sourcePath;
        }

        if (!ShouldCopyLauncherSource(sourcePath))
        {
            return sourcePath;
        }

        try
        {
            var folder = Path.Combine(_store.DataDirectory, "Launchers");
            Directory.CreateDirectory(folder);
            var target = Path.Combine(folder, SanitizeFileName(Path.GetFileNameWithoutExtension(sourcePath)) + Path.GetExtension(sourcePath));
            File.Copy(sourcePath, target, overwrite: true);
            return target;
        }
        catch
        {
            return sourcePath;
        }
    }

    private bool ShouldCopyLauncherSource(string sourcePath)
    {
        var extension = Path.GetExtension(sourcePath);
        if (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".url", StringComparison.OrdinalIgnoreCase)
            || IsUnderDirectory(sourcePath, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory))
            || IsUnderDirectory(sourcePath, Path.Combine(_store.DataDirectory, "DesktopOrganizer")))
        {
            return true;
        }

        return false;
    }

    private static bool IsUnderDirectory(string path, string directory)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var fullDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return fullPath.StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string? GetDroppedLauncherPath(DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true
            && e.Data.GetData(DataFormats.FileDrop) is string[] files
            && files.Length > 0)
        {
            return files[0];
        }

        return e.Data?.GetDataPresent(DataFormats.Text) == true && e.Data.GetData(DataFormats.Text) is string path
            ? path
            : null;
    }

    private Control CreateBadge(string text, Color color)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            BackColor = color,
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleCenter,
            Margin = new Padding(6, 8, 0, 8)
        };
    }

    private static string StatusText(ProjectStatus status)
    {
        return status switch
        {
            ProjectStatus.Doing => "进行中",
            ProjectStatus.Done => "已完成",
            _ => "待开始"
        };
    }

    private Button CreateHeaderButton(string text, Color? iconColor = null)
    {
        var button = new Button
        {
            Text = iconColor.HasValue ? "" : text,
            Dock = DockStyle.Right,
            Width = 58,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.Transparent,
            ForeColor = TextColorSubtle,
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderSize = 0;
        if (iconColor.HasValue)
        {
            button.Paint += (_, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                DrawTinajiaIcon(e.Graphics, new Rectangle(button.Width / 2 - 9, button.Height / 2 - 9, 18, 18), iconColor.Value);
            };
        }
        return button;
    }

    private static void DrawTinajiaIcon(Graphics g, Rectangle rect, Color color)
    {
        FillRoundedRectangle(g, rect, color, Math.Max(4, rect.Width / 5));
        using var plusBrush = new SolidBrush(Color.White);
        var bar = Math.Max(2, rect.Width / 5);
        var span = Math.Max(8, rect.Width * 3 / 5);
        var centerX = rect.X + rect.Width / 2;
        var centerY = rect.Y + rect.Height / 2;
        FillRoundedRectangle(g, new Rectangle(centerX - span / 2, centerY - bar / 2, span, bar), Color.White, Math.Max(1, bar / 2));
        FillRoundedRectangle(g, new Rectangle(centerX - bar / 2, centerY - span / 2, bar, span), Color.White, Math.Max(1, bar / 2));
    }

    private static void FillRoundedRectangle(Graphics g, Rectangle rect, Color color, int radius)
    {
        using var brush = new SolidBrush(color);
        using var path = RoundedRectanglePath(rect, radius);
        g.FillPath(brush, path);
    }

    private static GraphicsPath RoundedRectanglePath(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Max(1, radius * 2);
        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private Button CreateSecondaryButton(string text)
    {
        var button = CreateButton(text);
        button.BackColor = Color.FromArgb(58, 70, 88);
        button.ForeColor = TextColorMain;
        return button;
    }

    private Button CreateIconButton(string text)
    {
        var button = new Button
        {
            Text = text,
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.Transparent,
            ForeColor = TextColorSubtle,
            Font = new Font(Font.FontFamily, 14F),
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(96, 118, 140);
        return button;
    }

    private Control CreateEmptyHint(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            ForeColor = TextColorSubtle,
            TextAlign = ContentAlignment.MiddleCenter
        };
    }

    private ListView CreateDesktopEntryGrid()
    {
        var images = new ImageList
        {
            ColorDepth = ColorDepth.Depth32Bit,
            ImageSize = new Size(40, 40)
        };
        var list = new ListView
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            BackColor = PanelColor,
            ForeColor = TextColorMain,
            Font = new Font(Font.FontFamily, 9F),
            View = View.LargeIcon,
            LargeImageList = images,
            HideSelection = false,
            MultiSelect = false,
            LabelWrap = true,
            ShowItemToolTips = true,
            AllowDrop = true
        };
        list.Disposed += (_, _) =>
        {
            foreach (Image image in images.Images)
            {
                image.Dispose();
            }

            images.Dispose();
        };
        return list;
    }

    private static void AddDesktopGridItem(ListView list, string path)
    {
        var imageKey = path;
        if (list.LargeImageList is not null && !list.LargeImageList.Images.ContainsKey(imageKey))
        {
            var icon = ShellIconLoader.LoadLargeIcon(path) ?? CreateFallbackDesktopIcon(path);
            list.LargeImageList.Images.Add(imageKey, icon);
        }

        list.Items.Add(new ListViewItem("")
        {
            Tag = new DesktopEntry(path),
            ImageKey = imageKey,
            ToolTipText = Path.GetFileName(path)
        });
    }

    private static Image CreateFallbackDesktopIcon(string path)
    {
        var bitmap = new Bitmap(40, 40);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(Directory.Exists(path) ? Color.FromArgb(255, 190, 70) : Color.FromArgb(58, 126, 246));
        g.FillRectangle(brush, new Rectangle(6, 6, 28, 28));
        return bitmap;
    }

    private static DesktopEntry? SelectedDesktopEntry(ListView list)
    {
        return list.SelectedItems.Count == 0 ? null : list.SelectedItems[0].Tag as DesktopEntry;
    }

    private static IEnumerable<DesktopEntry> SelectedDesktopEntries(ListView list)
    {
        return list.SelectedItems.Cast<ListViewItem>().Select(item => item.Tag).OfType<DesktopEntry>();
    }

    private static void RestoreDesktopEntrySelection(ListView list, IEnumerable<string> selectedPaths)
    {
        var selectedSet = selectedPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (ListViewItem item in list.Items)
        {
            if (item.Tag is DesktopEntry entry && selectedSet.Contains(entry.Path))
            {
                item.Selected = true;
            }
        }
    }

    private void BuildDesktopPage()
    {
        var page = CreatePage("桌面收纳");
        var actions = CreateActionBar();
        var refreshButton = CreateButton("刷新桌面");
        var addButton = CreateButton("新建分类");
        var deleteButton = CreateButton("删除分类");
        var renameButton = CreateButton("重命名");
        var toggleButton = CreateButton("折叠/展开");
        var organizeButton = CreateButton("自动整理");
        var pinButton = CreateButton("添加到桌面");
        actions.Controls.AddRange(new Control[] { refreshButton, addButton, deleteButton, renameButton, toggleButton, organizeButton, pinButton });
        page.Controls.Add(actions, 0, 1);

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));

        var categoryList = CreateListBox();
        var desktopList = CreateDesktopEntryGrid();
        var itemList = CreateDesktopEntryGrid();
        categoryList.AllowDrop = true;
        itemList.AllowDrop = true;
        itemList.MultiSelect = true;

        var addToCategoryButton = CreateButton("归入分类");
        var removeFromCategoryButton = CreateButton("移出分类");
        var openButton = CreateButton("打开");

        grid.Controls.Add(CreateGroup("分类", categoryList), 0, 0);
        grid.Controls.Add(CreateGroup("桌面项目", desktopList, addToCategoryButton), 1, 0);
        grid.Controls.Add(CreateGroup("分类内容", itemList, openButton, removeFromCategoryButton), 2, 0);
        page.Controls.Add(grid, 0, 2);
        addToCategoryButton.Enabled = false;
        removeFromCategoryButton.Enabled = false;
        openButton.Enabled = false;

        void RefreshCategories()
        {
            var selectedCategory = categoryList.SelectedItem as DeskCategory;
            var selectedCategoryName = selectedCategory?.Name;
            categoryList.Items.Clear();
            foreach (var category in _config.DesktopCategories)
            {
                categoryList.Items.Add(category);
            }

            if (selectedCategory is not null && _config.DesktopCategories.Contains(selectedCategory))
            {
                categoryList.SelectedItem = selectedCategory;
            }
            else if (!string.IsNullOrWhiteSpace(selectedCategoryName))
            {
                foreach (var item in categoryList.Items)
                {
                    if (item is DeskCategory category && string.Equals(category.Name, selectedCategoryName, StringComparison.Ordinal))
                    {
                        categoryList.SelectedItem = category;
                        break;
                    }
                }
            }

            if (categoryList.Items.Count > 0 && categoryList.SelectedIndex < 0)
            {
                categoryList.SelectedIndex = 0;
            }
        }

        void RefreshDesktopItems()
        {
            var selectedPaths = SelectedDesktopEntries(desktopList).Select(entry => entry.Path).ToArray();
            var assigned = _config.DesktopCategories
                .SelectMany(c => c.ItemPaths)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var assignedNames = assigned
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            desktopList.Items.Clear();
            foreach (var path in GetDesktopEntries().Where(path => !assigned.Contains(path) && !assignedNames.Contains(Path.GetFileName(path))))
            {
                AddDesktopGridItem(desktopList, path);
            }
            RestoreDesktopEntrySelection(desktopList, selectedPaths);
        }

        void RefreshCategoryItems()
        {
            var selectedPaths = SelectedDesktopEntries(itemList).Select(entry => entry.Path).ToArray();
            itemList.Items.Clear();
            if (categoryList.SelectedItem is not DeskCategory category)
            {
                return;
            }

            if (category.IsCollapsed)
            {
                itemList.Items.Add(new ListViewItem($"已折叠：{category.ItemPaths.Count} 项"));
                return;
            }

            foreach (var path in category.ItemPaths.ToArray())
            {
                if (File.Exists(path) || Directory.Exists(path))
                {
                    AddDesktopGridItem(itemList, path);
                }
                else
                {
                    category.ItemPaths.Remove(path);
                }
            }

            _store.SaveConfig(_config);
            RestoreDesktopEntrySelection(itemList, selectedPaths);
        }

        void RefreshAll()
        {
            RemoveMissingDesktopCategoryItems();
            DesktopOrganizerStorage.RemoveDesktopDuplicateOrganizerReferences(_config);
            _store.SaveConfig(_config);
            RefreshCategories();
            RefreshDesktopItems();
            RefreshCategoryItems();
            UpdateDesktopButtons();
        }

        DeskCategory? CurrentCategory() => categoryList.SelectedItem as DeskCategory;

        void UpdateDesktopButtons()
        {
            addToCategoryButton.Enabled = SelectedDesktopEntry(desktopList) is not null && CurrentCategory() is not null;
            openButton.Enabled = SelectedDesktopEntry(itemList) is not null;
            removeFromCategoryButton.Enabled = SelectedDesktopEntries(itemList).Any();
        }

        void AddPathToCurrentCategory(string path)
        {
            var category = CurrentCategory();
            if (category is null || string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var target = DesktopOrganizerStorage.MoveIntoCategory(_store, category, path);
            if (target is null)
            {
                return;
            }

            DesktopOrganizerStorage.RemoveOrganizerReferences(_config, path, target);

            category.ItemPaths.Add(target);
            _store.SaveConfig(_config);
            RefreshAll();
        }

        categoryList.SelectedIndexChanged += (_, _) =>
        {
            RefreshCategoryItems();
            UpdateDesktopButtons();
        };
        desktopList.SelectedIndexChanged += (_, _) => UpdateDesktopButtons();
        itemList.SelectedIndexChanged += (_, _) => UpdateDesktopButtons();
        refreshButton.Click += (_, _) => RefreshAll();
        addButton.Click += (_, _) =>
        {
            var name = Prompt("新建分类", "分类名称");
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            _config.DesktopCategories.Add(new DeskCategory { Name = name.Trim() });
            _store.SaveConfig(_config);
            RefreshAll();
        };
        renameButton.Click += (_, _) =>
        {
            var category = CurrentCategory();
            if (category is null)
            {
                return;
            }

            var name = Prompt("重命名分类", "分类名称", category.Name);
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            category.Name = name.Trim();
            _store.SaveConfig(_config);
            RefreshAll();
        };
        deleteButton.Click += (_, _) =>
        {
            var category = CurrentCategory();
            if (category is null)
            {
                return;
            }

            _config.DesktopCategories.Remove(category);
            _store.SaveConfig(_config);
            RefreshAll();
        };
        toggleButton.Click += (_, _) =>
        {
            var category = CurrentCategory();
            if (category is null)
            {
                return;
            }

            category.IsCollapsed = !category.IsCollapsed;
            _store.SaveConfig(_config);
            RefreshAll();
        };
        addToCategoryButton.Click += (_, _) =>
        {
            if (SelectedDesktopEntry(desktopList) is { } entry)
            {
                AddPathToCurrentCategory(entry.Path);
            }
        };
        removeFromCategoryButton.Click += (_, _) =>
        {
            var category = CurrentCategory();
            if (category is null || itemList.SelectedItems.Count == 0)
            {
                return;
            }

            var selected = SelectedDesktopEntries(itemList).ToArray();
            foreach (var entry in selected)
            {
                DesktopOrganizerStorage.MoveToDesktopAndRemove(_config, entry.Path);
            }

            _store.SaveConfig(_config);
            RefreshAll();
        };
        openButton.Click += (_, _) =>
        {
            if (SelectedDesktopEntry(itemList) is { } entry)
            {
                OpenPath(entry.Path);
            }
        };
        organizeButton.Click += (_, _) =>
        {
            RemoveMissingDesktopCategoryItems();
            _store.SaveConfig(_config);
            RefreshAll();
        };
        pinButton.Click += (_, _) =>
        {
            ShowDesktopOrganizerWidget();
        };

        DesktopEntry? pendingDesktopDrag = null;
        Point pendingDesktopDragStart = Point.Empty;
        DesktopEntry? pendingItemDrag = null;
        Point pendingItemDragStart = Point.Empty;
        string? activeDesktopPageDragPath = null;
        bool handledDesktopPageDrop = false;

        static DataObject CreateDesktopEntryDragData(string path)
        {
            var data = new DataObject();
            data.SetData(DataFormats.Text, path);
            data.SetData(DataFormats.FileDrop, new[] { path });
            return data;
        }

        static DragDropEffects DoDesktopEntryDrag(Control source, string path)
        {
            using var preview = new DragPreviewForm(path);
            void MovePreview() => preview.MoveToCursor(Cursor.Position);
            GiveFeedbackEventHandler giveFeedback = (_, e) =>
            {
                e.UseDefaultCursors = true;
                MovePreview();
            };
            QueryContinueDragEventHandler queryContinue = (_, _) => MovePreview();

            try
            {
                preview.Show();
                MovePreview();
                source.GiveFeedback += giveFeedback;
                source.QueryContinueDrag += queryContinue;
                return source.DoDragDrop(CreateDesktopEntryDragData(path), DragDropEffects.Move | DragDropEffects.Copy);
            }
            finally
            {
                source.GiveFeedback -= giveFeedback;
                source.QueryContinueDrag -= queryContinue;
                preview.Close();
            }
        }

        desktopList.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            pendingDesktopDrag = desktopList.GetItemAt(e.X, e.Y)?.Tag as DesktopEntry;
            pendingDesktopDragStart = e.Location;
        };
        desktopList.MouseMove += (_, e) =>
        {
            if (pendingDesktopDrag is null || e.Button != MouseButtons.Left)
            {
                return;
            }

            if (Math.Abs(e.X - pendingDesktopDragStart.X) >= SystemInformation.DragSize.Width / 2
                || Math.Abs(e.Y - pendingDesktopDragStart.Y) >= SystemInformation.DragSize.Height / 2)
            {
                var entry = pendingDesktopDrag;
                pendingDesktopDrag = null;
                activeDesktopPageDragPath = entry.Path;
                handledDesktopPageDrop = false;
                DoDesktopEntryDrag(desktopList, entry.Path);
                activeDesktopPageDragPath = null;
            }
        };
        desktopList.MouseUp += (_, _) => pendingDesktopDrag = null;
        itemList.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            pendingItemDrag = itemList.GetItemAt(e.X, e.Y)?.Tag as DesktopEntry;
            pendingItemDragStart = e.Location;
        };
        itemList.MouseMove += (_, e) =>
        {
            if (pendingItemDrag is null || e.Button != MouseButtons.Left)
            {
                return;
            }

            if (Math.Abs(e.X - pendingItemDragStart.X) >= SystemInformation.DragSize.Width / 2
                || Math.Abs(e.Y - pendingItemDragStart.Y) >= SystemInformation.DragSize.Height / 2)
            {
                var entry = pendingItemDrag;
                pendingItemDrag = null;
                activeDesktopPageDragPath = entry.Path;
                handledDesktopPageDrop = false;
                var effect = DoDesktopEntryDrag(itemList, entry.Path);
                if (effect != DragDropEffects.None && !handledDesktopPageDrop)
                {
                    DesktopOrganizerStorage.MoveToDesktopAndRemove(_config, entry.Path);
                    _store.SaveConfig(_config);
                    RefreshAll();
                    _desktopOrganizerWidget?.RefreshWidget();
                }

                activeDesktopPageDragPath = null;
            }
        };
        itemList.MouseUp += (_, _) => pendingItemDrag = null;
        categoryList.DragEnter += (_, e) => SetDragEffect(e);
        itemList.DragEnter += (_, e) => SetDragEffect(e);
        categoryList.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.Text) is string path)
            {
                if (!string.IsNullOrWhiteSpace(activeDesktopPageDragPath)
                    && string.Equals(path, activeDesktopPageDragPath, StringComparison.OrdinalIgnoreCase))
                {
                    handledDesktopPageDrop = true;
                }

                AddPathToCurrentCategory(path);
            }
        };
        itemList.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.Text) is string path)
            {
                if (!string.IsNullOrWhiteSpace(activeDesktopPageDragPath)
                    && string.Equals(path, activeDesktopPageDragPath, StringComparison.OrdinalIgnoreCase))
                {
                    handledDesktopPageDrop = true;
                }

                AddPathToCurrentCategory(path);
            }
        };

        RefreshAll();
        _content.Controls.Add(page);
    }

    private void BuildTodoPage()
    {
        var page = CreatePage("今日任务");
        var actions = CreateActionBar();
        var addButton = CreateButton("新增");
        var editButton = CreateButton("编辑");
        var deleteButton = CreateButton("删除");
        actions.Controls.AddRange(new Control[] { addButton, editButton, deleteButton });
        page.Controls.Add(actions, 0, 1);

        var list = new CheckedListBox
        {
            Dock = DockStyle.Fill,
            CheckOnClick = true,
            BorderStyle = BorderStyle.None,
            Font = new Font(Font.FontFamily, 11F),
            BackColor = PanelColor,
            ForeColor = TextColorMain,
            HorizontalScrollbar = true
        };
        var detailTip = new ToolTip
        {
            AutomaticDelay = 250,
            ReshowDelay = 100,
            AutoPopDelay = 8000
        };
        page.Controls.Add(CreateGroup("任务", list), 0, 2);

        TodoItem? CurrentItem() => list.SelectedIndex >= 0 ? _todos.Items.ElementAtOrDefault(list.SelectedIndex) : null;

        void RefreshTodos()
        {
            list.Items.Clear();
            foreach (var item in _todos.Items)
            {
                list.Items.Add(FormatTodoListDisplay(item), item.Done);
            }
        }

        addButton.Click += (_, _) =>
        {
            var item = ShowTodoEditor();
            if (item is null)
            {
                return;
            }

            _todos.Items.Add(item);
            SaveTodos();
            RefreshTodos();
        };
        editButton.Click += (_, _) =>
        {
            var item = CurrentItem();
            if (item is null)
            {
                return;
            }

            var edited = ShowTodoEditor(item);
            if (edited is null)
            {
                return;
            }

            item.Text = edited.Text;
            item.Tag = edited.Tag;
            item.Note = edited.Note;
            item.Done = edited.Done;
            item.CreatedAt = edited.CreatedAt;
            SaveTodos();
            RefreshTodos();
        };
        deleteButton.Click += (_, _) =>
        {
            var item = CurrentItem();
            if (item is null)
            {
                return;
            }

            _todos.Items.Remove(item);
            SaveTodos();
            RefreshTodos();
        };
        list.ItemCheck += (_, e) =>
        {
            BeginInvoke((Action)(() =>
            {
                if (_todos.Items.ElementAtOrDefault(e.Index) is TodoItem item)
                {
                    item.Done = e.NewValue == CheckState.Checked;
                    SaveTodos();
                }
            }));
        };
        list.DoubleClick += (_, _) =>
        {
            var item = CurrentItem();
            if (item is not null)
            {
                ShowTodoDetails(item);
            }
        };
        list.MouseMove += (_, e) =>
        {
            var index = list.IndexFromPoint(e.Location);
            if (index >= 0 && _todos.Items.ElementAtOrDefault(index) is TodoItem item && !string.IsNullOrWhiteSpace(item.Note))
            {
                detailTip.SetToolTip(list, item.Note);
                list.Cursor = Cursors.Hand;
            }
            else
            {
                detailTip.SetToolTip(list, string.Empty);
                list.Cursor = Cursors.Default;
            }
        };
        list.MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Right)
            {
                return;
            }

            var index = list.IndexFromPoint(e.Location);
            if (index < 0)
            {
                return;
            }

            list.SelectedIndex = index;
            if (_todos.Items.ElementAtOrDefault(index) is not TodoItem item)
            {
                return;
            }

            var menu = new ContextMenuStrip { ShowImageMargin = false };
            menu.Items.Add("查看详情", null, (_, _) => ShowTodoDetails(item));
            menu.Show(list, e.Location);
        };

        RefreshTodos();
        _content.Controls.Add(page);
    }
    private void BuildNotePage()
    {
        EnsureNoteItem();

        var page = CreatePage("便签");
        var actions = CreateActionBar();
        var addButton = CreateButton("添加便签");
        var deleteButton = CreateButton("删除便签");
        var renameButton = CreateButton("重命名");
        var colorButton = CreateButton("颜色");
        var transparentColorButton = CreateSecondaryButton("透明颜色");
        var fontColorButton = CreateButton("字色");
        var fontSizeDownButton = CreateSecondaryButton("A-");
        var fontSizeUpButton = CreateButton("A+");
        var fontBoldButton = CreateButton("加粗");
        var imageButton = CreateButton("背景图片");
        var clearImageButton = CreateSecondaryButton("清除背景");
        var imageOnlyButton = CreateButton("仅显示图片");
        var pinNoteButton = CreateButton("添加到桌面");
        actions.Controls.AddRange(new Control[] { addButton, deleteButton, renameButton, colorButton, transparentColorButton, fontColorButton, fontSizeDownButton, fontSizeUpButton, fontBoldButton, imageButton, clearImageButton, imageOnlyButton, pinNoteButton });
        page.Controls.Add(actions, 0, 1);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var noteList = CreateListBox();
        var addFromListButton = CreateButton("添加便签");
        noteList.DrawMode = DrawMode.OwnerDrawFixed;
        noteList.ItemHeight = 70;
        noteList.DrawItem += (_, e) =>
        {
            e.DrawBackground();
            if (e.Index < 0 || e.Index >= noteList.Items.Count || noteList.Items[e.Index] is not NoteItem item)
            {
                return;
            }

            var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            using var fill = new SolidBrush(selected ? AccentColor : Color.FromArgb(42, 51, 67));
            e.Graphics.FillRectangle(fill, e.Bounds);
            using var swatch = new SolidBrush(Color.FromArgb(item.ColorArgb).A == 0 ? Color.FromArgb(42, 51, 67) : Color.FromArgb(item.ColorArgb));
            e.Graphics.FillRectangle(swatch, e.Bounds.X + 10, e.Bounds.Y + 12, 9, e.Bounds.Height - 24);
            TextRenderer.DrawText(e.Graphics, item.Title, new Font(Font.FontFamily, 10F, FontStyle.Bold), new Rectangle(e.Bounds.X + 28, e.Bounds.Y + 10, e.Bounds.Width - 38, 22), Color.White, TextFormatFlags.EndEllipsis);
            var preview = string.IsNullOrWhiteSpace(item.Text) ? "空白便签" : item.Text.Replace("\r", " ").Replace("\n", " ").Trim();
            TextRenderer.DrawText(e.Graphics, preview, Font, new Rectangle(e.Bounds.X + 28, e.Bounds.Y + 36, e.Bounds.Width - 38, 22), selected ? Color.FromArgb(218, 230, 255) : TextColorSubtle, TextFormatFlags.EndEllipsis);
        };

        var editorHost = new NoteEditorPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            BackColor = Color.Transparent
        };
        var textBox = new NoteTextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            AcceptsTab = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.None,
            Font = new Font("Microsoft YaHei UI", 13F),
            ForeColor = Color.FromArgb(44, 38, 28)
        };
        editorHost.Controls.Add(textBox);

        var statusLabel = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 26,
            ForeColor = TextColorSubtle,
            TextAlign = ContentAlignment.MiddleLeft
        };
        editorHost.Controls.Add(statusLabel);

        layout.Controls.Add(CreateGroup("便签列表", noteList, addFromListButton), 0, 0);
        layout.Controls.Add(CreateGroup("编辑", editorHost), 1, 0);
        page.Controls.Add(layout, 0, 2);

        void RefreshNotes(NoteItem? selected = null)
        {
            noteList.Items.Clear();
            foreach (var item in _notes.Items)
            {
                noteList.Items.Add(item);
            }

            noteList.SelectedItem = selected ?? noteList.SelectedItem ?? _notes.Items.FirstOrDefault();
            RefreshEditor();
        }

        void RefreshEditor()
        {
            if (noteList.SelectedItem is not NoteItem item)
            {
                textBox.Enabled = false;
                textBox.Text = "";
                statusLabel.Text = "无便签";
                return;
            }

            textBox.Enabled = true;
            _activeNoteItem = item;
            _activeNoteBox = textBox;
            textBox.TextChanged -= NoteTextChanged;
            textBox.Text = item.Text;
            var noteColor = Color.FromArgb(item.ColorArgb);
            textBox.BackColor = noteColor.A == 0 ? Color.FromArgb(28, 38, 54) : noteColor;
            item.FontColorArgb = NoteStyle.NormalizeTextColorArgb(item.FontColorArgb);
            textBox.ForeColor = item.ImageOnly ? textBox.BackColor : NoteStyle.TextColor(item);
            textBox.Font = new Font("Microsoft YaHei UI", Math.Clamp(item.FontSize, 8F, 42F), item.FontBold ? FontStyle.Bold : FontStyle.Regular);
            textBox.ScrollBars = item.ImageOnly ? ScrollBars.None : ScrollBars.Vertical;
            textBox.ReadOnly = item.ImageOnly;
            textBox.SetBackground(item.BackgroundImagePath, item.ImageOnly);
            editorHost.SetBackground(null);
            imageOnlyButton.Enabled = !string.IsNullOrWhiteSpace(item.BackgroundImagePath);
            imageOnlyButton.BackColor = item.ImageOnly ? Color.FromArgb(26, 135, 84) : AccentColor;
            fontBoldButton.BackColor = item.FontBold ? Color.FromArgb(26, 135, 84) : AccentColor;
            statusLabel.Text = string.IsNullOrWhiteSpace(item.BackgroundImagePath)
                ? $"自动保存 · {item.UpdatedAt:HH:mm:ss}"
                : $"自动保存 · 图片背景 · {item.UpdatedAt:HH:mm:ss}";
            textBox.TextChanged += NoteTextChanged;
        }

        void NoteTextChanged(object? sender, EventArgs e)
        {
            statusLabel.Text = "保存中";
            _noteSaveTimer?.Stop();
            _noteSaveTimer?.Start();
            noteList.Invalidate();
        }

        _noteSaveTimer = new System.Windows.Forms.Timer { Interval = 700 };
        _noteSaveTimer.Tick += (_, _) =>
        {
            _noteSaveTimer.Stop();
            SaveActiveNote();
            if (_activeNoteItem is not null)
            {
                RefreshDesktopNoteWidgets(_activeNoteItem);
            }
            statusLabel.Text = $"已保存 {DateTime.Now:HH:mm:ss}";
            noteList.Invalidate();
        };

        noteList.SelectedIndexChanged += (_, _) =>
        {
            SaveActiveNote();
            RefreshEditor();
        };
        void AddNote()
        {
            SaveActiveNote();
            var item = new NoteItem { Title = $"便签 {_notes.Items.Count + 1}" };
            _notes.Items.Add(item);
            _store.SaveNotes(_notes);
            RefreshNotes(item);
        }

        addButton.Click += (_, _) => AddNote();
        addFromListButton.Click += (_, _) => AddNote();
        deleteButton.Click += (_, _) =>
        {
            SaveActiveNote();
            if (noteList.SelectedItem is not NoteItem item)
            {
                return;
            }

            _notes.Items.Remove(item);
            if (_notes.Items.Count == 0)
            {
                _notes.Items.Add(new NoteItem { Title = "note.md" });
            }

            _store.SaveNotes(_notes);
            RefreshNotes(_notes.Items.FirstOrDefault());
        };
        renameButton.Click += (_, _) =>
        {
            SaveActiveNote();
            if (noteList.SelectedItem is not NoteItem item)
            {
                return;
            }

            var title = Prompt("重命名便签", "便签名称", item.Title);
            if (string.IsNullOrWhiteSpace(title))
            {
                return;
            }

            item.Title = title.Trim();
            item.UpdatedAt = DateTime.Now;
            _store.SaveNotes(_notes);
            RefreshDesktopNoteWidgets(item);
            RefreshNotes(item);
        };
        colorButton.Click += (_, _) =>
        {
            SaveActiveNote();
            if (noteList.SelectedItem is not NoteItem item)
            {
                return;
            }

            using var dialog = new ColorDialog
            {
                Color = Color.FromArgb(item.ColorArgb),
                FullOpen = true
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            item.ColorArgb = dialog.Color.ToArgb();
            item.UpdatedAt = DateTime.Now;
            _store.SaveNotes(_notes);
            RefreshDesktopNoteWidgets(item);
            RefreshNotes(item);
        };
        transparentColorButton.Click += (_, _) =>
        {
            SaveActiveNote();
            if (noteList.SelectedItem is not NoteItem item)
            {
                return;
            }

            item.ColorArgb = Color.Transparent.ToArgb();
            item.UpdatedAt = DateTime.Now;
            _store.SaveNotes(_notes);
            RefreshDesktopNoteWidgets(item);
            RefreshNotes(item);
        };
        fontColorButton.Click += (_, _) =>
        {
            SaveActiveNote();
            if (noteList.SelectedItem is not NoteItem item)
            {
                return;
            }

            using var dialog = new ColorDialog
            {
                Color = Color.FromArgb(item.FontColorArgb),
                FullOpen = true
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            item.FontColorArgb = NoteStyle.NormalizeTextColorArgb(dialog.Color.ToArgb());
            item.UpdatedAt = DateTime.Now;
            _store.SaveNotes(_notes);
            RefreshDesktopNoteWidgets(item);
            RefreshNotes(item);
        };
        fontSizeDownButton.Click += (_, _) => ChangeNoteFontSize(-1F);
        fontSizeUpButton.Click += (_, _) => ChangeNoteFontSize(1F);
        fontBoldButton.Click += (_, _) =>
        {
            SaveActiveNote();
            if (noteList.SelectedItem is not NoteItem item)
            {
                return;
            }

            item.FontBold = !item.FontBold;
            item.UpdatedAt = DateTime.Now;
            _store.SaveNotes(_notes);
            RefreshDesktopNoteWidgets(item);
            RefreshNotes(item);
        };
        void ChangeNoteFontSize(float delta)
        {
            SaveActiveNote();
            if (noteList.SelectedItem is not NoteItem item)
            {
                return;
            }

            item.FontSize = Math.Clamp(item.FontSize + delta, 8F, 42F);
            item.UpdatedAt = DateTime.Now;
            _store.SaveNotes(_notes);
            RefreshDesktopNoteWidgets(item);
            RefreshNotes(item);
        }
        imageButton.Click += (_, _) =>
        {
            SaveActiveNote();
            if (noteList.SelectedItem is not NoteItem item)
            {
                return;
            }

            using var dialog = new OpenFileDialog
            {
                Title = "选择便签背景图片",
                Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有文件|*.*"
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            item.BackgroundImagePath = dialog.FileName;
            item.ImageOnly = false;
            item.UpdatedAt = DateTime.Now;
            _store.SaveNotes(_notes);
            RefreshDesktopNoteWidgets(item);
            RefreshNotes(item);
        };
        imageOnlyButton.Click += (_, _) =>
        {
            SaveActiveNote();
            if (noteList.SelectedItem is not NoteItem item || string.IsNullOrWhiteSpace(item.BackgroundImagePath))
            {
                return;
            }

            item.ImageOnly = !item.ImageOnly;
            item.UpdatedAt = DateTime.Now;
            _store.SaveNotes(_notes);
            RefreshDesktopNoteWidgets(item);
            RefreshNotes(item);
        };
        pinNoteButton.Click += (_, _) =>
        {
            SaveActiveNote();
            if (noteList.SelectedItem is not NoteItem item)
            {
                return;
            }

            var existing = _desktopNoteWidgets.FirstOrDefault(widget => !widget.IsDisposed && widget.Displays(item));
            if (existing is not null)
            {
                existing.FocusWidget();
                return;
            }

            var widget = CreateDesktopNoteWidget(item);
            _desktopNoteWidgets.Add(widget);
            widget.ShowAsDesktopWidget(EnsureDesktopNotePlacement(item));
        };
        clearImageButton.Click += (_, _) =>
        {
            SaveActiveNote();
            if (noteList.SelectedItem is not NoteItem item)
            {
                return;
            }

            item.BackgroundImagePath = null;
            item.ImageOnly = false;
            item.UpdatedAt = DateTime.Now;
            _store.SaveNotes(_notes);
            RefreshDesktopNoteWidgets(item);
            RefreshNotes(item);
        };

        var initialSelection = _pendingNoteSelection is not null && _notes.Items.Contains(_pendingNoteSelection)
            ? _pendingNoteSelection
            : _notes.Items.FirstOrDefault();
        _pendingNoteSelection = null;
        RefreshNotes(initialSelection);
        _content.Controls.Add(page);
    }

    private void BuildProjectPage()
    {
        var page = CreatePage("项目管理");
        var actions = CreateActionBar();
        var addProjectButton = CreateButton("新建项目");
        var renameProjectButton = CreateButton("重命名项目");
        var deleteProjectButton = CreateButton("删除项目");
        var pathProjectButton = CreateButton("添加项目路径");
        var pinProjectButton = CreateButton("添加到桌面");
        actions.Controls.AddRange(new Control[] { addProjectButton, renameProjectButton, deleteProjectButton, pathProjectButton, pinProjectButton });
        page.Controls.Add(actions, 0, 1);

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));

        var projectList = CreateListBox();
        var itemList = CreateListBox();
        var subItemList = new CheckedListBox
        {
            Dock = DockStyle.Fill,
            CheckOnClick = false,
            BorderStyle = BorderStyle.None,
            Font = new Font(Font.FontFamily, 9.5F),
            BackColor = PanelColor,
            ForeColor = TextColorMain,
            HorizontalScrollbar = true
        };
        var refreshingSubItems = false;
        ConfigureProjectList(projectList);
        ConfigureProjectItemList(itemList);
        ConfigureProjectSubItemList(subItemList);
        ConfigureProjectListMenu(projectList, RefreshProjects, RefreshDesktopProjectWidget);

        grid.Controls.Add(CreateGroup("项目", projectList), 0, 0);
        grid.Controls.Add(CreateProjectItemsGroup(itemList), 1, 0);
        grid.Controls.Add(CreateProjectSubItemsGroup(subItemList), 2, 0);
        page.Controls.Add(grid, 0, 2);

        Panel CreateProjectItemsGroup(ListBox list)
        {
            var addButton = CreateButton("新增");
            var editButton = CreateButton("编辑");
            var deleteButton = CreateButton("删除");
            var pathButton = CreateButton("添加路径");
            var group = CreateGroup("项目阶段", list, addButton, editButton, deleteButton, pathButton);

            addButton.Click += (_, _) => AddProjectItem();
            editButton.Click += (_, _) => EditProjectItem(list);
            deleteButton.Click += (_, _) => DeleteProjectItem(list);
            pathButton.Click += (_, _) => SetProjectItemPath(list);
            ConfigureProjectItemMenu(list, RefreshItems, RefreshDesktopProjectWidget);
            return group;
        }

        Panel CreateProjectSubItemsGroup(CheckedListBox list)
        {
            var addButton = CreateButton("新增");
            var editButton = CreateButton("编辑");
            var deleteButton = CreateButton("删除");
            var pathButton = CreateButton("添加文件");
            var group = CreateGroup("文件或事件预设", list, addButton, editButton, deleteButton, pathButton);

            addButton.Click += (_, _) => AddProjectSubItem();
            editButton.Click += (_, _) => EditProjectSubItem(list);
            deleteButton.Click += (_, _) => DeleteProjectSubItem(list);
            pathButton.Click += (_, _) => SetProjectSubItemPath(list);
            return group;
        }

        ProjectBoard? CurrentProject() => projectList.SelectedItem as ProjectBoard;
        ProjectItem? CurrentProjectItem() => itemList.SelectedItem as ProjectItem;

        void RefreshProjects()
        {
            projectList.Items.Clear();
            foreach (var project in _projects.Projects)
            {
                projectList.Items.Add(project);
            }

            if (projectList.Items.Count > 0 && projectList.SelectedIndex < 0)
            {
                projectList.SelectedIndex = 0;
            }
        }

        void RefreshItems()
        {
            var selected = itemList.SelectedItem as ProjectItem;
            itemList.Items.Clear();

            var project = CurrentProject();
            if (project is null)
            {
                RefreshSubItems();
                return;
            }

            foreach (var item in project.Items)
            {
                itemList.Items.Add(item);
            }

            if (selected is not null && project.Items.Contains(selected))
            {
                itemList.SelectedItem = selected;
            }
            else if (itemList.Items.Count > 0 && itemList.SelectedIndex < 0)
            {
                itemList.SelectedIndex = 0;
            }

            RefreshSubItems();
        }

        void RefreshSubItems()
        {
            refreshingSubItems = true;
            try
            {
                subItemList.Items.Clear();
                var item = CurrentProjectItem();
                if (item is null)
                {
                    return;
                }

                foreach (var subItem in item.SubItems)
                {
                    subItemList.Items.Add(subItem, subItem.Done);
                }
            }
            finally
            {
                refreshingSubItems = false;
            }
        }

        void AddProjectItem()
        {
            var project = CurrentProject();
            if (project is null)
            {
                return;
            }

            var input = ShowProjectItemDialog("新增阶段", ProjectStatus.Todo);
            if (input is null)
            {
                return;
            }

            project.Items.Add(new ProjectItem
            {
                Title = input.Value.Title,
                Status = ProjectStatus.Todo,
                StartDate = input.Value.StartDate,
                EndDate = input.Value.EndDate,
                ProgressPercent = -1
            });
            _store.SaveProjects(_projects);
            RefreshItems();
            RefreshDesktopProjectWidget();
        }

        void EditProjectItem(ListBox list)
        {
            if (list.SelectedItem is not ProjectItem item)
            {
                return;
            }

            var input = ShowProjectItemDialog("编辑阶段", item.Status, item);
            if (input is null)
            {
                return;
            }

            item.Title = input.Value.Title;
            item.StartDate = input.Value.StartDate;
            item.EndDate = input.Value.EndDate;
            item.ProgressPercent = -1;
            _store.SaveProjects(_projects);
            RefreshItems();
            RefreshDesktopProjectWidget();
        }

        void DeleteProjectItem(ListBox list)
        {
            var project = CurrentProject();
            if (project is null || list.SelectedItem is not ProjectItem item)
            {
                return;
            }

            project.Items.Remove(item);
            _store.SaveProjects(_projects);
            RefreshItems();
            RefreshDesktopProjectWidget();
        }

        void SetProjectItemPath(ListBox list)
        {
            if (list.SelectedItem is not ProjectItem item)
            {
                return;
            }

            var path = ChooseProjectPath(item.ProjectPath);
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            item.ProjectPath = path;
            _store.SaveProjects(_projects);
            RefreshItems();
            RefreshDesktopProjectWidget();
        }

        void AddProjectSubItem()
        {
            var item = CurrentProjectItem();
            if (item is null)
            {
                return;
            }

            var name = Prompt("新增预设", "名称");
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            item.SubItems.Add(new ProjectSubItem { Title = name.Trim() });
            _store.SaveProjects(_projects);
            RefreshItems();
            RefreshDesktopProjectWidget();
        }

        void EditProjectSubItem(CheckedListBox list)
        {
            if (list.SelectedItem is not ProjectSubItem subItem)
            {
                return;
            }

            var name = Prompt("编辑预设", "名称", subItem.Title);
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            subItem.Title = name.Trim();
            _store.SaveProjects(_projects);
            RefreshSubItems();
            RefreshDesktopProjectWidget();
        }

        void DeleteProjectSubItem(CheckedListBox list)
        {
            var item = CurrentProjectItem();
            if (item is null || list.SelectedItem is not ProjectSubItem subItem)
            {
                return;
            }

            item.SubItems.Remove(subItem);
            _store.SaveProjects(_projects);
            RefreshItems();
            RefreshDesktopProjectWidget();
        }

        void SetProjectSubItemPath(CheckedListBox list)
        {
            if (list.SelectedItem is not ProjectSubItem subItem)
            {
                return;
            }

            var path = ChooseProjectFilePath(subItem.FilePath);
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            subItem.FilePath = path;
            _store.SaveProjects(_projects);
            RefreshItems();
            RefreshDesktopProjectWidget();
        }

        projectList.SelectedIndexChanged += (_, _) => RefreshItems();
        itemList.SelectedIndexChanged += (_, _) => RefreshSubItems();
        subItemList.ItemCheck += (_, e) =>
        {
            if (refreshingSubItems || e.Index < 0 || e.Index >= subItemList.Items.Count || subItemList.Items[e.Index] is not ProjectSubItem subItem)
            {
                return;
            }

            subItem.Done = e.NewValue == CheckState.Checked;
            _store.SaveProjects(_projects);
            BeginInvoke(new Action(() =>
            {
                RefreshItems();
                RefreshDesktopProjectWidget();
            }));
        };
        subItemList.MouseDown += (_, e) =>
        {
            var index = subItemList.IndexFromPoint(e.Location);
            if (index < 0 || index >= subItemList.Items.Count || subItemList.Items[index] is not ProjectSubItem subItem)
            {
                return;
            }

            subItemList.SelectedIndex = index;
            var checkRect = new Rectangle(10, index * subItemList.ItemHeight + 13, 22, 22);
            if (checkRect.Contains(e.Location))
            {
                subItemList.SetItemChecked(index, !subItemList.GetItemChecked(index));
                return;
            }

            if (!string.IsNullOrWhiteSpace(subItem.FilePath))
            {
                OpenPath(subItem.FilePath);
            }
        };
        pathProjectButton.Click += (_, _) =>
        {
            var project = CurrentProject();
            if (project is null)
            {
                return;
            }

            var path = ChooseProjectPath(project.ProjectPath);
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            project.ProjectPath = path;
            _store.SaveProjects(_projects);
            RefreshProjects();
            RefreshDesktopProjectWidget();
        };
        pinProjectButton.Click += (_, _) => ShowDesktopProjectWidget();
        addProjectButton.Click += (_, _) =>
        {
            var name = Prompt("新建项目", "项目名称");
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            _projects.Projects.Add(new ProjectBoard { Name = name.Trim() });
            _store.SaveProjects(_projects);
            RefreshProjects();
            RefreshDesktopProjectWidget();
        };
        renameProjectButton.Click += (_, _) =>
        {
            var project = CurrentProject();
            if (project is null)
            {
                return;
            }

            var name = Prompt("重命名项目", "项目名称", project.Name);
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            project.Name = name.Trim();
            _store.SaveProjects(_projects);
            RefreshProjects();
            RefreshDesktopProjectWidget();
        };
        deleteProjectButton.Click += (_, _) =>
        {
            var project = CurrentProject();
            if (project is null)
            {
                return;
            }

            _projects.Projects.Remove(project);
            _store.SaveProjects(_projects);
            RefreshProjects();
            RefreshItems();
            RefreshDesktopProjectWidget();
        };

        RefreshProjects();
        RefreshItems();
        _content.Controls.Add(page);
    }

    private void ConfigureProjectItemList(ListBox list)
    {
        list.DrawMode = DrawMode.OwnerDrawFixed;
        list.ItemHeight = 92;
        list.DrawItem += (_, e) =>
        {
            if (e.Index < 0 || e.Index >= list.Items.Count)
            {
                return;
            }

            var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            using var background = new SolidBrush(selected ? Color.FromArgb(54, 112, 210) : list.BackColor);
            e.Graphics.FillRectangle(background, e.Bounds);

            if (list.Items[e.Index] is not ProjectItem item)
            {
                return;
            }

            var row = new Rectangle(e.Bounds.X + 12, e.Bounds.Y + 10, e.Bounds.Width - 24, e.Bounds.Height - 18);
            var titleColor = selected ? Color.White : TextColorMain;
            var subColor = selected ? Color.FromArgb(226, 238, 255) : TextColorSubtle;
            using var titleFont = new Font(list.Font.FontFamily, 10.5F, FontStyle.Regular);
            TextRenderer.DrawText(e.Graphics, item.Title, titleFont, new Rectangle(row.X, row.Y, row.Width - 70, 26), titleColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            var percent = ProjectProgressPercent(item, item.Status);
            TextRenderer.DrawText(e.Graphics, $"{percent}%", titleFont, new Rectangle(row.Right - 66, row.Y, 66, 26), subColor, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(e.Graphics, ProjectDateRangeText(item).Trim(), list.Font, new Rectangle(row.X, row.Y + 30, row.Width, 24), subColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            var track = new Rectangle(row.X, row.Bottom - 14, row.Width, 8);
            using var trackBrush = new SolidBrush(Color.FromArgb(67, 80, 104));
            e.Graphics.FillRectangle(trackBrush, track);
            if (percent > 0)
            {
                using var fillBrush = new SolidBrush(ProjectProgressGreen);
                e.Graphics.FillRectangle(fillBrush, new Rectangle(track.X, track.Y, Math.Max(8, track.Width * percent / 100), track.Height));
            }

            if (item.SubItems.Count > 1)
            {
                using var tickPen = new Pen(Color.FromArgb(154, 176, 198), 1F);
                for (var i = 1; i < item.SubItems.Count; i++)
                {
                    var x = track.X + track.Width * i / item.SubItems.Count;
                    e.Graphics.DrawLine(tickPen, x, track.Y - 2, x, track.Bottom + 2);
                }
            }

            var thumbX = track.X + track.Width * percent / 100;
            using var thumb = new SolidBrush(Color.White);
            e.Graphics.FillEllipse(thumb, new Rectangle(thumbX - 5, track.Y - 4, 11, 15));
        };
    }

    private void ConfigureProjectSubItemList(CheckedListBox list)
    {
        list.DrawMode = DrawMode.OwnerDrawFixed;
        list.ItemHeight = 44;
        list.DrawItem += (_, e) =>
        {
            if (e.Index < 0 || e.Index >= list.Items.Count)
            {
                return;
            }

            var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            using var background = new SolidBrush(selected ? Color.FromArgb(54, 112, 210) : list.BackColor);
            e.Graphics.FillRectangle(background, e.Bounds);

            if (list.Items[e.Index] is not ProjectSubItem item)
            {
                return;
            }

            var check = new Rectangle(e.Bounds.X + 10, e.Bounds.Y + 13, 16, 16);
            var completed = ProjectSubItemCompleted(item);
            using (var borderPen = new Pen(selected ? Color.White : TextColorSubtle))
            {
                e.Graphics.DrawRectangle(borderPen, check);
            }
            if (completed)
            {
                using var pen = new Pen(ProjectProgressGreen, 1.8F);
                e.Graphics.DrawLine(pen, check.X + 3, check.Y + 9, check.X + 7, check.Y + 13);
                e.Graphics.DrawLine(pen, check.X + 7, check.Y + 13, check.Right - 3, check.Y + 4);
            }

            var title = string.IsNullOrWhiteSpace(item.Title) ? "未命名" : item.Title;
            var suffix = string.IsNullOrWhiteSpace(item.FilePath) ? "" : "  已设置文件";
            TextRenderer.DrawText(
                e.Graphics,
                title + suffix,
                list.Font,
                new Rectangle(e.Bounds.X + 36, e.Bounds.Y, e.Bounds.Width - 48, e.Bounds.Height),
                selected ? Color.White : TextColorMain,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        };
    }

    private void ConfigureProjectList(ListBox list)
    {
        list.DrawMode = DrawMode.OwnerDrawFixed;
        list.ItemHeight = 48;
        list.DrawItem += (_, e) =>
        {
            if (e.Index < 0 || e.Index >= list.Items.Count)
            {
                return;
            }

            var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            using var background = new SolidBrush(selected ? Color.FromArgb(54, 112, 210) : list.BackColor);
            e.Graphics.FillRectangle(background, e.Bounds);

            if (list.Items[e.Index] is not ProjectBoard project)
            {
                return;
            }

            TextRenderer.DrawText(
                e.Graphics,
                project.Name,
                list.Font,
                new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 16, e.Bounds.Height),
                selected ? Color.White : TextColorMain,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        };
    }

    private void ConfigureProjectListMenu(ListBox list, Action refresh, Action desktopRefresh)
    {
        var menu = new ContextMenuStrip { ShowImageMargin = false };
        var pathActionItem = menu.Items.Add("设置路径");
        pathActionItem.Click += (_, _) =>
        {
            if (list.SelectedItem is not ProjectBoard project)
            {
                return;
            }

            if (Directory.Exists(project.ProjectPath))
            {
                OpenPath(project.ProjectPath);
                return;
            }

            var path = ChooseProjectPath(project.ProjectPath);
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            project.ProjectPath = path;
            _store.SaveProjects(_projects);
            refresh();
            desktopRefresh();
        };
        menu.Opening += (_, e) =>
        {
            var project = list.SelectedItem as ProjectBoard;
            if (project is null)
            {
                e.Cancel = true;
                return;
            }

            pathActionItem.Text = Directory.Exists(project.ProjectPath) ? "打开项目路径" : "设置路径";
        };
        list.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right)
            {
                return;
            }

            var index = list.IndexFromPoint(e.Location);
            if (index >= 0)
            {
                list.SelectedIndex = index;
            }
            else
            {
                list.ClearSelected();
            }
        };
        list.ContextMenuStrip = menu;
    }

    private void ConfigureProjectItemMenu(ListBox list, Action refresh, Action desktopRefresh)
    {
        var menu = new ContextMenuStrip { ShowImageMargin = false };
        var pathActionItem = menu.Items.Add("设置路径");
        pathActionItem.Click += (_, _) =>
        {
            if (list.SelectedItem is not ProjectItem item)
            {
                return;
            }

            if (Directory.Exists(item.ProjectPath))
            {
                OpenPath(item.ProjectPath);
                return;
            }

            var path = ChooseProjectPath(item.ProjectPath);
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            item.ProjectPath = path;
            _store.SaveProjects(_projects);
            refresh();
            desktopRefresh();
        };
        menu.Opening += (_, e) =>
        {
            var item = list.SelectedItem as ProjectItem;
            if (item is null)
            {
                e.Cancel = true;
                return;
            }

            pathActionItem.Text = Directory.Exists(item.ProjectPath) ? "打开项目路径" : "设置路径";
        };
        list.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right)
            {
                return;
            }

            var index = list.IndexFromPoint(e.Location);
            if (index >= 0)
            {
                list.SelectedIndex = index;
            }
            else
            {
                list.ClearSelected();
            }
        };
        list.ContextMenuStrip = menu;
    }

    private string? ChooseProjectPath(string currentPath)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择项目文件夹",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(currentPath) ? currentPath : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
        };

        return dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath)
            ? dialog.SelectedPath
            : null;
    }

    private string? ChooseProjectFilePath(string currentPath)
    {
        var chooseFolder = MessageBox.Show(this, "是否选择文件夹？\n\n选择“否”则选择文件。", "添加文件路径", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (chooseFolder == DialogResult.Cancel)
        {
            return null;
        }

        if (chooseFolder == DialogResult.Yes)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "选择文件夹",
                UseDescriptionForTitle = true,
                SelectedPath = Directory.Exists(currentPath) ? currentPath : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
            };

            return dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath)
                ? dialog.SelectedPath
                : null;
        }

        using (var dialog = new OpenFileDialog
        {
            Title = "选择文件",
            Filter = "所有文件|*.*",
            FileName = File.Exists(currentPath) ? currentPath : ""
        })
        {
            return dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.FileName)
                ? dialog.FileName
                : null;
        }
    }

    private static Color ProjectProgressGreen => Color.FromArgb(58, 214, 122);

    private (string Title, DateTime StartDate, DateTime EndDate)? ShowProjectItemDialog(string title, ProjectStatus status, ProjectItem? item = null)
    {
        using var form = new Form
        {
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.None,
            ClientSize = new Size(460, 340),
            Font = new Font("Microsoft YaHei UI", 9F),
            BackColor = Color.FromArgb(246, 248, 252),
            ShowInTaskbar = false,
            Padding = new Padding(1)
        };
        using var formPath = RoundedRectanglePath(new Rectangle(0, 0, form.Width, form.Height), 12);
        form.Region = new Region(formPath);

        var chrome = new Panel
        {
            Dock = DockStyle.Top,
            Height = 48,
            BackColor = Color.White
        };
        chrome.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                NativeGlass.BeginMove(form.Handle);
            }
        };

        var titleLabel = new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(20, 28, 40),
            Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Regular),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(20, 0, 0, 0)
        };
        chrome.Controls.Add(titleLabel);

        var closeButton = new Button
        {
            Text = "×",
            Dock = DockStyle.Right,
            Width = 48,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = Color.FromArgb(80, 90, 106),
            Cursor = Cursors.Hand
        };
        closeButton.FlatAppearance.BorderSize = 0;
        closeButton.Click += (_, _) => form.DialogResult = DialogResult.Cancel;
        chrome.Controls.Add(closeButton);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(24, 18, 24, 18),
            BackColor = Color.FromArgb(246, 248, 252)
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.FromArgb(246, 248, 252)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(chrome, 0, 0);
        root.Controls.Add(content, 0, 1);
        form.Controls.Add(root);

        var textBox = new TextBox
        {
            Text = item?.Title ?? "",
            Dock = DockStyle.Top,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White,
            ForeColor = Color.FromArgb(18, 25, 38),
            Height = 28,
            Margin = new Padding(0, 7, 0, 0)
        };
        var startPicker = CreateProjectDatePicker(item?.StartDate ?? DateTime.Today);
        var endPicker = CreateProjectDatePicker(item?.EndDate ?? DateTime.Today.AddDays(7));

        content.Controls.Add(CreateProjectDialogLabel("阶段名称"), 0, 0);
        content.Controls.Add(textBox, 1, 0);
        content.Controls.Add(CreateProjectDialogLabel("开始日期"), 0, 1);
        content.Controls.Add(startPicker, 1, 1);
        content.Controls.Add(CreateProjectDialogLabel("截止日期"), 0, 2);
        content.Controls.Add(endPicker, 1, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 44,
            BackColor = Color.Transparent,
            WrapContents = false
        };
        var okButton = new Button
        {
            Text = "保存",
            Width = 88,
            Height = 34,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(20, 28, 40),
            ForeColor = Color.White,
            Cursor = Cursors.Hand
        };
        okButton.FlatAppearance.BorderSize = 0;
        var cancelButton = new Button
        {
            Text = "取消",
            Width = 88,
            Height = 34,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = Color.FromArgb(20, 28, 40),
            Cursor = Cursors.Hand,
            Margin = new Padding(8, 0, 0, 0)
        };
        cancelButton.FlatAppearance.BorderColor = Color.FromArgb(210, 216, 226);
        cancelButton.Click += (_, _) => form.DialogResult = DialogResult.Cancel;
        okButton.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(textBox.Text))
            {
                MessageBox.Show(form, "请输入阶段名称", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (endPicker.Value.Date < startPicker.Value.Date)
            {
                MessageBox.Show(form, "截止日期不能早于开始日期", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            form.DialogResult = DialogResult.OK;
        };
        buttons.Controls.Add(okButton);
        buttons.Controls.Add(cancelButton);
        content.SetColumnSpan(buttons, 2);
        content.Controls.Add(buttons, 0, 3);

        form.AcceptButton = okButton;
        form.CancelButton = cancelButton;

        return form.ShowDialog(this) == DialogResult.OK
            ? (textBox.Text.Trim(), startPicker.Value.Date, endPicker.Value.Date)
            : null;
    }

    private static Label CreateProjectDialogLabel(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(64, 74, 90),
            TextAlign = ContentAlignment.MiddleLeft
        };
    }

    private static DateTimePicker CreateProjectDatePicker(DateTime value)
    {
        return new DateTimePicker
        {
            Dock = DockStyle.Fill,
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "yyyy-MM-dd",
            Value = value.Date
        };
    }

    private static int DefaultProjectProgress(ProjectStatus status)
    {
        return status switch
        {
            ProjectStatus.Done => 100,
            ProjectStatus.Doing => 50,
            _ => 0
        };
    }

    private static int ProjectProgressPercent(ProjectItem? item, ProjectStatus status)
    {
        if (item?.SubItems.Count > 0)
        {
            var completed = item.SubItems.Count(ProjectSubItemCompleted);
            return (int)Math.Round(completed * 100D / item.SubItems.Count, MidpointRounding.AwayFromZero);
        }

        if (item is not null && item.ProgressPercent >= 0)
        {
            return Math.Clamp(item.ProgressPercent, 0, 100);
        }

        return DefaultProjectProgress(item?.Status ?? status);
    }

    private static bool ProjectSubItemCompleted(ProjectSubItem item)
    {
        return item.Done;
    }

    private static string ProjectDateRangeText(ProjectItem item)
    {
        return item.StartDate.HasValue || item.EndDate.HasValue
            ? $"开始 {item.StartDate?.ToString("yyyy/MM/dd") ?? "----/--/--"}    截止 {item.EndDate?.ToString("yyyy/MM/dd") ?? "----/--/--"}"
            : "开始 ----/--/--    截止 ----/--/--";
    }

    private void BuildLauncherPage()
    {
        var page = CreatePage("快捷启动");
        var actions = CreateActionBar();
        var addButton = CreateButton("添加");
        var editButton = CreateButton("编辑");
        var deleteButton = CreateButton("删除");
        var openButton = CreateButton("启动");
        var pinButton = CreateButton("添加到桌面");
        actions.Controls.AddRange(new Control[] { addButton, editButton, deleteButton, openButton, pinButton });
        page.Controls.Add(actions, 0, 1);

        var list = CreateListBox();
        list.AllowDrop = true;
        ConfigureLauncherList(list);
        page.Controls.Add(CreateGroup("常用软件", list), 0, 2);

        void RefreshLaunchers()
        {
            list.Items.Clear();
            foreach (var item in _launchers.Items)
            {
                list.Items.Add(item);
            }
        }

        addButton.Click += (_, _) =>
        {
            if (AddLauncher())
            {
                RefreshLaunchers();
                RefreshDesktopLauncherWidget();
            }
        };
        editButton.Click += (_, _) =>
        {
            if (list.SelectedItem is not LaunchItem item)
            {
                return;
            }

            var name = Prompt("编辑快捷启动", "显示名称", item.Name);
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            item.Name = name.Trim();
            _store.SaveLaunchers(_launchers);
            RefreshLaunchers();
            RefreshDesktopLauncherWidget();
        };
        deleteButton.Click += (_, _) =>
        {
            if (list.SelectedItem is not LaunchItem item)
            {
                return;
            }

            _launchers.Items.Remove(item);
            _store.SaveLaunchers(_launchers);
            RefreshLaunchers();
            RefreshDesktopLauncherWidget();
        };
        pinButton.Click += (_, _) => ShowDesktopLauncherWidget();
        openButton.Click += (_, _) =>
        {
            if (list.SelectedItem is LaunchItem item)
            {
                OpenPath(item.Path);
            }
        };
        list.DoubleClick += (_, _) =>
        {
            if (list.SelectedItem is LaunchItem item)
            {
                OpenPath(item.Path);
            }
        };
        list.DragEnter += (_, e) =>
        {
            e.Effect = GetDroppedLauncherPath(e) is null || _launchers.Items.Count >= MaxLaunchers
                ? DragDropEffects.None
                : DragDropEffects.Copy;
        };
        list.DragOver += (_, e) =>
        {
            e.Effect = GetDroppedLauncherPath(e) is null || _launchers.Items.Count >= MaxLaunchers
                ? DragDropEffects.None
                : DragDropEffects.Copy;
        };
        list.DragDrop += (_, e) =>
        {
            var path = GetDroppedLauncherPath(e);
            if (path is not null && AddLauncherFromPath(path))
            {
                RefreshLaunchers();
                RefreshDesktopLauncherWidget();
            }
        };

        RefreshLaunchers();
        _content.Controls.Add(page);
    }

    private void ConfigureLauncherList(ListBox list)
    {
        var iconCache = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        list.DrawMode = DrawMode.OwnerDrawFixed;
        list.ItemHeight = 86;
        list.DrawItem += (_, e) =>
        {
            if (e.Index < 0 || e.Index >= list.Items.Count)
            {
                return;
            }

            var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            using var background = new SolidBrush(selected ? Color.FromArgb(54, 112, 210) : list.BackColor);
            e.Graphics.FillRectangle(background, e.Bounds);

            if (list.Items[e.Index] is not LaunchItem item)
            {
                return;
            }

            var row = new Rectangle(e.Bounds.X + 14, e.Bounds.Y + 10, e.Bounds.Width - 28, e.Bounds.Height - 18);
            var iconRect = new Rectangle(row.X, row.Y + 8, 42, 42);
            var icon = GetLauncherIcon(item.Path, iconCache);
            if (icon is not null)
            {
                e.Graphics.DrawImage(icon, iconRect);
            }
            else
            {
                using var iconBrush = new SolidBrush(Color.FromArgb(58, 126, 246));
                e.Graphics.FillRectangle(iconBrush, iconRect);
                TextRenderer.DrawText(e.Graphics, string.IsNullOrWhiteSpace(item.Name) ? "+" : item.Name[..1], list.Font, iconRect, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            TextRenderer.DrawText(e.Graphics, item.Name, new Font(list.Font.FontFamily, 11F, FontStyle.Bold), new Rectangle(row.X + 56, row.Y + 16, row.Width - 56, 28), selected ? Color.White : TextColorMain, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        };
        list.Disposed += (_, _) =>
        {
            foreach (var image in iconCache.Values)
            {
                image.Dispose();
            }
        };
    }

    private static Image? GetLauncherIcon(string path, Dictionary<string, Image> cache)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (cache.TryGetValue(path, out var cached))
        {
            return cached;
        }

        var icon = ShellIconLoader.LoadLargeIcon(path);
        if (icon is not null)
        {
            cache[path] = icon;
        }

        return icon;
    }

    private void BuildStatsPage()
    {
        var page = CreatePage("统计分析");
        var summary = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 2,
            BackColor = Color.Transparent
        };
        summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
        summary.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        summary.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        summary.Controls.Add(CreateMetricCard("桌面分类", _config.DesktopCategories.Count.ToString()), 0, 0);
        summary.Controls.Add(CreateMetricCard("任务总数", _todos.Items.Count.ToString()), 1, 0);
        summary.Controls.Add(CreateMetricCard("已完成", _todos.Items.Count(item => item.Done).ToString()), 2, 0);
        summary.Controls.Add(CreateMetricCard("项目数量", _projects.Projects.Count.ToString()), 0, 1);
        summary.Controls.Add(CreateMetricCard("项目事项", _projects.Projects.SelectMany(project => project.Items).Count().ToString()), 1, 1);
        summary.Controls.Add(CreateMetricCard("快捷启动", _launchers.Items.Count.ToString()), 2, 1);
        page.Controls.Add(summary, 0, 2);
        _content.Controls.Add(page);
    }

    private void BuildSearchSettingsPage()
    {
        var page = CreatePage("搜索设置");
        var actions = CreateActionBar();
        var pinButton = CreateButton("添加到桌面");
        var openButton = CreateButton("打开搜索");
        actions.Controls.AddRange(new Control[] { pinButton, openButton });
        page.Controls.Add(actions, 0, 1);

        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(118, 30, 40, 56),
            Padding = new Padding(24),
            Margin = new Padding(0, 0, 10, 0)
        };
        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 454,
            ColumnCount = 1,
            RowCount = 7,
            BackColor = Color.Transparent
        };
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        for (var i = 1; i < 7; i++)
        {
            stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        }

        stack.Controls.Add(new Label
        {
            Text = "搜索方式",
            Dock = DockStyle.Fill,
            ForeColor = TextColorMain,
            Font = new Font(Font.FontFamily, 11F, FontStyle.Regular),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        stack.Controls.Add(CreateSearchSourceRow("应用内数据", "快捷启动、桌面分类、工作记录、便签、项目条目", _config.SearchAppData, value => _config.SearchAppData = value), 0, 1);
        stack.Controls.Add(CreateSearchSourceRow("桌面文件", "系统桌面和桌面收纳中的文件、文件夹", _config.SearchDesktopFiles, value => _config.SearchDesktopFiles = value), 0, 2);
        stack.Controls.Add(CreateSearchSourceRow("开始菜单应用", "开始菜单、公共开始菜单和快捷启动路径", _config.SearchStartMenuApps, value => _config.SearchStartMenuApps = value), 0, 3);
        stack.Controls.Add(CreateSearchSourceRow("项目路径", "项目、阶段、子任务关联的文件夹和文件", _config.SearchProjectPaths, value => _config.SearchProjectPaths = value), 0, 4);
        stack.Controls.Add(CreateSearchTransparentRow(), 0, 5);
        stack.Controls.Add(CreateSettingRow("桌面组件", "点击上方“添加到桌面”会显示胶囊搜索框"), 0, 6);
        panel.Controls.Add(stack);
        page.Controls.Add(panel, 0, 2);

        pinButton.Click += (_, _) => ShowDesktopSearchWidget(centerOnScreen: true, minimizeMain: true);
        openButton.Click += (_, _) => ShowQuickSearch();
        _content.Controls.Add(page);
    }

    private Control CreateSearchTransparentRow()
    {
        var row = CreateSettingShell("透明配色", out var content);
        var toggle = new CheckBox
        {
            Text = _config.DesktopSearchWidgetTransparent ? "已开启" : "已关闭",
            Checked = _config.DesktopSearchWidgetTransparent,
            AutoSize = true,
            ForeColor = TextColorMain,
            Cursor = Cursors.Hand,
            Location = new Point(0, 12)
        };
        var hint = new Label
        {
            Text = "桌面搜索组件使用半透明深色配色",
            Location = new Point(112, 10),
            Size = new Size(680, 34),
            ForeColor = TextColorSubtle,
            TextAlign = ContentAlignment.MiddleLeft
        };
        toggle.CheckedChanged += (_, _) =>
        {
            _config.DesktopSearchWidgetTransparent = toggle.Checked;
            _store.SaveConfig(_config);
            _desktopSearchWidget?.RefreshSearch();
            toggle.Text = toggle.Checked ? "已开启" : "已关闭";
        };
        content.Controls.Add(toggle);
        content.Controls.Add(hint);
        return row;
    }

    private Control CreateSearchSourceRow(string title, string detail, bool? current, Action<bool> changed)
    {
        var row = CreateSettingShell(title, out var content);
        var toggle = new CheckBox
        {
            Text = SearchEnabled(current) ? "已开启" : "已关闭",
            Checked = SearchEnabled(current),
            AutoSize = true,
            ForeColor = TextColorMain,
            Cursor = Cursors.Hand,
            Location = new Point(0, 12)
        };
        var hint = new Label
        {
            Text = detail,
            Location = new Point(112, 10),
            Size = new Size(680, 34),
            ForeColor = TextColorSubtle,
            TextAlign = ContentAlignment.MiddleLeft
        };
        toggle.CheckedChanged += (_, _) =>
        {
            changed(toggle.Checked);
            _store.SaveConfig(_config);
            _desktopSearchWidget?.RefreshSearch();
            toggle.Text = toggle.Checked ? "已开启" : "已关闭";
        };
        content.Controls.Add(toggle);
        content.Controls.Add(hint);
        return row;
    }

    private static bool SearchEnabled(bool? value) => value ?? true;

    private void BuildSettingsPage()
    {
        var page = CreatePage("设置中心");
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(118, 30, 40, 56),
            Padding = new Padding(24),
            Margin = new Padding(0, 0, 10, 0),
            AutoScroll = true
        };
        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 780,
            ColumnCount = 1,
            RowCount = 11,
            BackColor = Color.Transparent
        };
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 98));
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        stack.Controls.Add(new Label
        {
            Text = "基础设置",
            Dock = DockStyle.Fill,
            ForeColor = TextColorMain,
            Font = new Font(Font.FontFamily, 11F, FontStyle.Regular),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        stack.Controls.Add(CreateStartupSettingRow(), 0, 1);
        stack.Controls.Add(CreateStartHiddenSettingRow(), 0, 2);
        stack.Controls.Add(CreateDesktopWidgetsSettingRow(), 0, 3);
        stack.Controls.Add(CreateHotKeySettingRow(), 0, 4);
        stack.Controls.Add(CreateDataPathSettingRow(), 0, 5);
        stack.Controls.Add(CreateRestoreDesktopSettingRow(), 0, 6);
        stack.Controls.Add(CreateProjectExportSettingRow(), 0, 7);
        stack.Controls.Add(CreateOperationIntroSettingRow(), 0, 8);
        stack.Controls.Add(CreateAboutSettingRow(), 0, 9);
        stack.Controls.Add(CreateResetDataSettingRow(), 0, 10);
        panel.Controls.Add(stack);
        page.Controls.Add(panel, 0, 2);
        _content.Controls.Add(page);
    }

    private Control CreateMetricCard(string title, string value)
    {
        var card = CreateHomeCard(title, out var body);
        body.Controls.Add(new Label
        {
            Text = value,
            Dock = DockStyle.Fill,
            ForeColor = TextColorMain,
            Font = new Font(Font.FontFamily, 34F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter
        });
        return card;
    }

    private Control CreateSettingRow(string title, string detail)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            BackColor = Color.Transparent,
            Padding = new Padding(14, 4, 14, 4)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            ForeColor = TextColorMain,
            Font = new Font(Font.FontFamily, 10F, FontStyle.Regular),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        row.Controls.Add(new Label
        {
            Text = detail,
            Dock = DockStyle.Fill,
            ForeColor = TextColorSubtle,
            TextAlign = ContentAlignment.MiddleLeft
        }, 1, 0);
        return row;
    }

    private Control CreateStartupSettingRow()
    {
        var row = CreateSettingShell("开机自动启动", out var content);
        var toggle = new CheckBox
        {
            Text = IsAutoStartEnabled() ? "已开启" : "已关闭",
            AutoSize = true,
            Checked = IsAutoStartEnabled(),
            ForeColor = TextColorMain,
            Cursor = Cursors.Hand,
            Location = new Point(0, 12)
        };
        toggle.CheckedChanged += (_, _) =>
        {
            SetAutoStart(toggle.Checked);
            toggle.Text = toggle.Checked ? "已开启" : "已关闭";
        };
        content.Controls.Add(toggle);
        return row;
    }

    private Control CreateDataPathSettingRow()
    {
        var row = CreateSettingShell("文件保存路径", out var content);
        var pathLabel = new Label
        {
            Text = _store.DataDirectory,
            AutoSize = false,
            Location = new Point(0, 11),
            Size = new Size(720, 30),
            ForeColor = TextColorSubtle,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
        var chooseButton = CreateButton("选择");
        chooseButton.AutoSize = false;
        chooseButton.SetBounds(740, 10, 82, 32);
        chooseButton.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "选择文件保存路径",
                SelectedPath = Directory.Exists(_store.DataDirectory) ? _store.DataDirectory : AppContext.BaseDirectory,
                UseDescriptionForTitle = true
            };
            if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
            {
                return;
            }

            _store.SetDataDirectory(dialog.SelectedPath);
            SaveAllData();
            pathLabel.Text = _store.DataDirectory;
        };
        content.Controls.Add(pathLabel);
        content.Controls.Add(chooseButton);
        return row;
    }

    private Control CreateStartHiddenSettingRow()
    {
        var row = CreateSettingShell("启动隐藏到托盘", out var content);
        var toggle = new CheckBox
        {
            Text = _config.StartHiddenToTray ? "已开启" : "已关闭",
            AutoSize = true,
            Checked = _config.StartHiddenToTray,
            ForeColor = TextColorMain,
            Cursor = Cursors.Hand,
            Location = new Point(0, 12)
        };
        toggle.CheckedChanged += (_, _) =>
        {
            _config.StartHiddenToTray = toggle.Checked;
            _store.SaveConfig(_config);
            toggle.Text = toggle.Checked ? "已开启" : "已关闭";
        };
        content.Controls.Add(toggle);
        return row;
    }

    private Control CreateDesktopWidgetsSettingRow()
    {
        var row = CreateSettingShell("桌面组件显示", out var content);
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 8, 0, 0)
        };

        flow.Controls.Add(CreateWidgetToggle("搜索", _config.DesktopSearchWidget?.Visible == true, () => ShowDesktopSearchWidget(centerOnScreen: true, minimizeMain: true), CloseDesktopSearchWidget));
        flow.Controls.Add(CreateWidgetToggle("桌面收纳", _config.DesktopOrganizerWidget?.Visible == true, () => ShowDesktopOrganizerWidget(), CloseDesktopOrganizerWidget));
        flow.Controls.Add(CreateWidgetToggle("工作记录", _config.DesktopTodoWidget?.Visible == true, () => ShowDesktopTodoWidget(), CloseDesktopTodoWidget));
        flow.Controls.Add(CreateWidgetToggle("项目管理", _config.DesktopProjectWidget?.Visible == true, () => ShowDesktopProjectWidget(), CloseDesktopProjectWidget));
        flow.Controls.Add(CreateWidgetToggle("快捷启动", _config.DesktopLauncherWidget?.Visible == true, () => ShowDesktopLauncherWidget(), CloseDesktopLauncherWidget));
        content.Controls.Add(flow);
        BeginInvoke(new Action(EnsureVisibleDesktopWidgets));
        return row;
    }

    private void EnsureVisibleDesktopWidgets()
    {
        if (_config.DesktopSearchWidget?.Visible == true && (_desktopSearchWidget is null || _desktopSearchWidget.IsDisposed))
        {
            ShowDesktopSearchWidget();
        }

        if (_config.DesktopOrganizerWidget?.Visible == true && (_desktopOrganizerWidget is null || _desktopOrganizerWidget.IsDisposed))
        {
            ShowDesktopOrganizerWidget();
        }

        if (_config.DesktopTodoWidget?.Visible == true && (_desktopTodoWidget is null || _desktopTodoWidget.IsDisposed))
        {
            ShowDesktopTodoWidget();
        }

        if (_config.DesktopProjectWidget?.Visible == true && (_desktopProjectWidget is null || _desktopProjectWidget.IsDisposed))
        {
            ShowDesktopProjectWidget();
        }

        if (_config.DesktopLauncherWidget?.Visible == true && (_desktopLauncherWidget is null || _desktopLauncherWidget.IsDisposed))
        {
            ShowDesktopLauncherWidget();
        }
    }

    private Control CreateWidgetToggle(string text, bool isVisible, Action show, Action close)
    {
        var toggle = new CheckBox
        {
            Text = isVisible ? $"{text}：显示" : $"{text}：关闭",
            Checked = isVisible,
            AutoSize = true,
            ForeColor = TextColorMain,
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 22, 10)
        };
        toggle.CheckedChanged += (_, _) =>
        {
            if (toggle.Checked)
            {
                show();
            }
            else
            {
                close();
            }

            toggle.Text = toggle.Checked ? $"{text}：显示" : $"{text}：关闭";
        };
        return toggle;
    }

    private Control CreateHotKeySettingRow()
    {
        var row = CreateSettingShell("打开主窗口快捷指令", out var content);
        var input = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(_config.MainWindowHotKey) ? "Ctrl+Shift+K" : _config.MainWindowHotKey,
            Location = new Point(0, 12),
            Size = new Size(180, 30),
            BackColor = Color.FromArgb(42, 54, 72),
            ForeColor = TextColorMain,
            BorderStyle = BorderStyle.FixedSingle
        };
        var saveButton = CreateButton("保存");
        saveButton.AutoSize = false;
        saveButton.SetBounds(192, 10, 82, 32);
        var hint = new Label
        {
            Text = "格式示例：Ctrl+Shift+K、Ctrl+Alt+Space",
            Location = new Point(290, 10),
            Size = new Size(460, 34),
            ForeColor = TextColorSubtle,
            TextAlign = ContentAlignment.MiddleLeft
        };
        saveButton.Click += (_, _) =>
        {
            var value = input.Text.Trim();
            if (!TryParseHotKey(value, out _, out _))
            {
                MessageBox.Show(this, "快捷指令格式不正确。", "DustDesk", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _config.MainWindowHotKey = value;
            _store.SaveConfig(_config);
            RegisterMainWindowHotKey();
            MessageBox.Show(this, "快捷指令已保存。", "DustDesk", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        content.Controls.Add(input);
        content.Controls.Add(saveButton);
        content.Controls.Add(hint);
        return row;
    }

    private Control CreateOperationIntroSettingRow()
    {
        var row = CreateSettingShell("操作简介", out var content);
        var button = CreateButton("查看");
        button.AutoSize = false;
        button.SetBounds(0, 12, 100, 34);
        button.Click += (_, _) => MessageBox.Show(
            this,
            "1. 桌面收纳：创建分类，把桌面文件拖入分类；从收纳拖出可还原。\n2. 工作记录、便签、项目、快捷启动都可添加为桌面组件。\n3. 首页搜索或 Ctrl+K 可快速检索文件、应用、项目和记录。\n4. 全局快捷指令默认 Ctrl+Shift+K，用于唤起主窗口。",
            "操作简介",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
        var hint = new Label
        {
            Text = "查看常用操作和快捷入口",
            Location = new Point(120, 13),
            Size = new Size(520, 32),
            ForeColor = TextColorSubtle,
            TextAlign = ContentAlignment.MiddleLeft
        };
        content.Controls.Add(button);
        content.Controls.Add(hint);
        return row;
    }

    private Control CreateAboutSettingRow()
    {
        var row = CreateSettingShell("关于我的", out var content);
        var link = new LinkLabel
        {
            Text = "关注抖音 Aby081298",
            LinkColor = Color.FromArgb(112, 170, 255),
            ActiveLinkColor = Color.White,
            VisitedLinkColor = Color.FromArgb(112, 170, 255),
            Location = new Point(0, 13),
            Size = new Size(180, 30),
            TextAlign = ContentAlignment.MiddleLeft
        };
        link.LinkClicked += (_, _) => OpenUrl("https://www.douyin.com/search/Aby081298");
        var feedback = new Label
        {
            Text = "反馈问题和咨询",
            Location = new Point(190, 13),
            Size = new Size(320, 30),
            ForeColor = TextColorSubtle,
            TextAlign = ContentAlignment.MiddleLeft
        };
        content.Controls.Add(link);
        content.Controls.Add(feedback);
        return row;
    }

    private Control CreateRestoreDesktopSettingRow()
    {
        var row = CreateSettingShell("恢复桌面布局", out var content);
        var restoreButton = CreateButton("恢复到桌面");
        restoreButton.AutoSize = false;
        restoreButton.SetBounds(0, 12, 120, 34);
        restoreButton.Click += (_, _) => RestoreAllDesktopItems();
        var hint = new Label
        {
            Text = "将桌面收纳中的所有项目移回系统桌面",
            Location = new Point(140, 13),
            Size = new Size(520, 32),
            ForeColor = TextColorSubtle,
            TextAlign = ContentAlignment.MiddleLeft
        };
        content.Controls.Add(restoreButton);
        content.Controls.Add(hint);
        return row;
    }

    private Control CreateProjectExportSettingRow()
    {
        var row = CreateSettingShell("导出项目管理", out var content);
        var exportButton = CreateButton("导出 Excel");
        exportButton.AutoSize = false;
        exportButton.SetBounds(0, 12, 120, 34);
        exportButton.Click += (_, _) => ExportProjectsToExcel();
        var hint = new Label
        {
            Text = "导出 xlsx 表格，项目、事项、子任务路径会写入超链接",
            Location = new Point(140, 13),
            Size = new Size(620, 32),
            ForeColor = TextColorSubtle,
            TextAlign = ContentAlignment.MiddleLeft
        };
        content.Controls.Add(exportButton);
        content.Controls.Add(hint);
        return row;
    }

    private Control CreateResetDataSettingRow()
    {
        var row = CreateSettingShell("重置所有数据", out var content);
        var resetButton = CreateButton("重置");
        resetButton.AutoSize = false;
        resetButton.SetBounds(0, 12, 120, 34);
        resetButton.BackColor = Color.FromArgb(180, 56, 64);
        resetButton.Click += (_, _) => ResetAllData();
        var hint = new Label
        {
            Text = "清空所有应用数据，收纳内容会先恢复到桌面",
            Location = new Point(140, 13),
            Size = new Size(620, 32),
            ForeColor = TextColorSubtle,
            TextAlign = ContentAlignment.MiddleLeft
        };
        content.Controls.Add(resetButton);
        content.Controls.Add(hint);
        return row;
    }

    private void ExportProjectsToExcel()
    {
        if (_projects.Projects.Count == 0)
        {
            MessageBox.Show(this, "没有可导出的项目。", "DustDesk", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = "导出项目管理",
            Filter = "Excel 工作簿|*.xlsx",
            FileName = $"项目管理_{DateTime.Now:yyyyMMdd_HHmm}.xlsx",
            InitialDirectory = Directory.Exists(_store.DataDirectory) ? _store.DataDirectory : AppContext.BaseDirectory,
            OverwritePrompt = true,
            AddExtension = true,
            DefaultExt = "xlsx"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.FileName))
        {
            return;
        }

        try
        {
            ProjectExcelExporter.Export(_projects, dialog.FileName);
            MessageBox.Show(this, $"已导出：{dialog.FileName}", "DustDesk", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"导出失败：{ex.Message}", "DustDesk", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ResetAllData()
    {
        if (!ConfirmResetData())
        {
            return;
        }

        try
        {
            RestoreOrganizerItemsToDesktop();
            CloseDesktopWidgetsForReset();
            DeleteResetArtifacts();
            ResetInMemoryData();
            SaveAllData();
            ShowPage(8);
            MessageBox.Show(this, "已重置所有数据。", "DustDesk", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"重置失败：{ex.Message}", "DustDesk", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private bool ConfirmResetData()
    {
        var prompts = new[]
        {
            "这会删除所有数据，并将桌面收纳中的内容全部还原到桌面。\n\n是否继续？",
            "第二次确认：任务、便签、项目、快捷启动、桌面组件设置都会被清空。\n\n确定继续？",
            "最后确认：重置后无法从程序内撤销。\n\n立即重置？"
        };

        foreach (var prompt in prompts)
        {
            if (MessageBox.Show(this, prompt, "重置所有数据", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
            {
                return false;
            }
        }

        return true;
    }

    private void RestoreOrganizerItemsToDesktop()
    {
        var paths = _config.DesktopCategories
            .SelectMany(category => category.ItemPaths)
            .Where(path => File.Exists(path) || Directory.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var path in paths)
        {
            DesktopOrganizerStorage.MoveToDesktopAndRemove(_config, path);
        }
    }

    private void CloseDesktopWidgetsForReset()
    {
        if (_desktopOrganizerWidget is not null && !_desktopOrganizerWidget.IsDisposed)
        {
            _desktopOrganizerWidget.Close();
        }

        foreach (var widget in _desktopOrganizerSplitWidgets.ToArray())
        {
            if (!widget.IsDisposed)
            {
                widget.Close();
            }
        }

        if (_desktopTodoWidget is not null && !_desktopTodoWidget.IsDisposed)
        {
            _desktopTodoWidget.Close();
        }

        if (_desktopProjectWidget is not null && !_desktopProjectWidget.IsDisposed)
        {
            _desktopProjectWidget.Close();
        }

        foreach (var widget in _desktopProjectSplitWidgets.ToArray())
        {
            if (!widget.IsDisposed)
            {
                widget.Close();
            }
        }

        if (_desktopLauncherWidget is not null && !_desktopLauncherWidget.IsDisposed)
        {
            _desktopLauncherWidget.Close();
        }

        if (_desktopSearchWidget is not null && !_desktopSearchWidget.IsDisposed)
        {
            _desktopSearchWidget.Close();
        }

        foreach (var widget in _desktopNoteWidgets.ToArray())
        {
            if (!widget.IsDisposed)
            {
                widget.Close();
            }
        }
    }

    private void ResetInMemoryData()
    {
        _config.DesktopWidgetTransparent = false;
        _config.DesktopTodoWidgetTransparent = false;
        _config.DesktopProjectWidgetTransparent = false;
        _config.DesktopLauncherWidgetTransparent = false;
        _config.DesktopLauncherWidgetSnap = false;
        _config.StartHiddenToTray = false;
        _config.MainWindowHotKey = "Ctrl+Shift+K";
        _config.SearchAppData = true;
        _config.SearchDesktopFiles = true;
        _config.SearchStartMenuApps = true;
        _config.SearchProjectPaths = true;
        _config.DesktopSearchWidgetTransparent = false;
        _config.DesktopOrganizerWidget = null;
        _config.DesktopTodoWidget = null;
        _config.DesktopProjectWidget = null;
        _config.DesktopLauncherWidget = null;
        _config.DesktopSearchWidget = null;
        _config.DesktopNoteWidgets.Clear();
        _config.DesktopCategories.Clear();
        _config.DesktopCategories.AddRange(new[]
        {
            new DeskCategory { Name = "工作" },
            new DeskCategory { Name = "开发" },
            new DeskCategory { Name = "游戏" },
            new DeskCategory { Name = "工具" }
        });

        _splitDesktopCategories.Clear();
        _splitProjectIds.Clear();
        _todos.Items.Clear();
        _todos.TagPresets.Clear();
        EnsureTodoTagPresets();
        _notes.Items.Clear();
        _notes.Items.Add(new NoteItem { Title = "note.md" });
        _projects.Projects.Clear();
        _launchers.Items.Clear();
        _desktopNoteWidgets.Clear();
        _desktopOrganizerSplitWidgets.Clear();
        _desktopProjectSplitWidgets.Clear();
        _desktopOrganizerWidget = null;
        _desktopTodoWidget = null;
        _desktopProjectWidget = null;
        _desktopLauncherWidget = null;
        _desktopSearchWidget = null;
        FlushNoteResetState();
    }

    private void DeleteResetArtifacts()
    {
        DeleteDirectoryIfExists(Path.Combine(_store.DataDirectory, "DesktopOrganizer"));
        DeleteDirectoryIfExists(Path.Combine(_store.DataDirectory, "Launchers"));
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        Directory.Delete(path, recursive: true);
    }

    private void FlushNoteResetState()
    {
        _noteSaveTimer?.Stop();
        _activeNoteBox = null;
        _activeNoteItem = null;
        _pendingNoteSelection = null;
    }

    private void RestoreAllDesktopItems()
    {
        var paths = _config.DesktopCategories
            .SelectMany(category => category.ItemPaths)
            .Where(path => File.Exists(path) || Directory.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (paths.Length == 0)
        {
            MessageBox.Show(this, "没有可恢复的收纳项目。", "DustDesk", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var message =
            $"将 {paths.Length} 个项目恢复到桌面，桌面同名项目会被覆盖。\n\n" +
            "请确认相关文件已经保存，并且当前文件是最新版本。\n\n" +
            "是否继续？";
        if (MessageBox.Show(this, message, "恢复桌面布局", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
        {
            return;
        }

        foreach (var path in paths)
        {
            DesktopOrganizerStorage.MoveToDesktopAndRemove(_config, path);
        }

        foreach (var category in _config.DesktopCategories)
        {
            category.ItemPaths.RemoveAll(path => !File.Exists(path) && !Directory.Exists(path));
        }

        _store.SaveConfig(_config);
        _desktopOrganizerWidget?.RefreshWidget();
        ShowPage(_nav.SelectedIndex);
    }

    private Control CreateSettingShell(string title, out Panel content)
    {
        var row = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        var titleLabel = new Label
        {
            Text = title,
            Location = new Point(0, 0),
            Size = new Size(180, 52),
            ForeColor = TextColorMain,
            Font = new Font(Font.FontFamily, 10F, FontStyle.Regular),
            TextAlign = ContentAlignment.MiddleLeft
        };
        content = new Panel
        {
            Location = new Point(190, 0),
            Size = new Size(960, 56),
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right,
            BackColor = Color.Transparent
        };
        row.Controls.Add(titleLabel);
        row.Controls.Add(content);
        return row;
    }

    private void SaveAllData()
    {
        _store.SaveConfig(_config);
        _store.SaveTodos(_todos);
        _store.SaveNotes(_notes);
        _store.SaveProjects(_projects);
        _store.SaveLaunchers(_launchers);
    }

    private static bool IsAutoStartEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: false);
        return string.Equals(key?.GetValue("DustDesk") as string, Application.ExecutablePath, StringComparison.OrdinalIgnoreCase);
    }

    private static void SetAutoStart(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true)
            ?? Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled)
        {
            key?.SetValue("DustDesk", Application.ExecutablePath);
        }
        else
        {
            key?.DeleteValue("DustDesk", throwOnMissingValue: false);
        }
    }

    private TableLayoutPanel CreatePage(string title)
    {
        var page = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = BackColorMain
        };
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var label = new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            Font = new Font(Font.FontFamily, 18, FontStyle.Regular),
            ForeColor = TextColorMain,
            TextAlign = ContentAlignment.MiddleLeft
        };
        page.Controls.Add(label, 0, 0);
        return page;
    }

    private static FlowLayoutPanel CreateActionBar()
    {
        return new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 6, 0, 6),
            BackColor = Color.Transparent
        };
    }

    private Button CreateButton(string text)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            Height = 34,
            Margin = new Padding(0, 0, 8, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = AccentColor,
            ForeColor = Color.White,
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderSize = 0;
        return button;
    }

    private ListBox CreateListBox()
    {
        return new ListBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            Font = new Font(Font.FontFamily, 10F),
            IntegralHeight = false,
            BackColor = Color.FromArgb(42, 51, 67),
            ForeColor = TextColorMain
        };
    }

    private Panel CreateGroup(string title, Control content, params Control[] footerControls)
    {
        var panel = new GlassPanel
        {
            Dock = DockStyle.Fill,
            Radius = 10,
            BorderColor = CardBorderColor,
            BackColor = Color.FromArgb(118, 30, 40, 56),
            Padding = new Padding(12),
            Margin = new Padding(0, 0, 10, 0)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = footerControls.Length > 0 ? 3 : 2,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        if (footerControls.Length > 0)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        }

        var label = new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            Font = new Font(Font.FontFamily, 10F, FontStyle.Regular),
            ForeColor = TextColorMain,
            TextAlign = ContentAlignment.MiddleLeft
        };
        layout.Controls.Add(label, 0, 0);

        content.Dock = DockStyle.Fill;
        layout.Controls.Add(content, 0, 1);

        if (footerControls.Length > 0)
        {
            var footer = CreateActionBar();
            footer.Dock = DockStyle.Fill;
            footer.Controls.AddRange(footerControls);
            layout.Controls.Add(footer, 0, 2);
        }

        panel.Controls.Add(layout);
        return panel;
    }

    private Control CreateModuleRow(string title, string detail, int navIndex)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = Color.FromArgb(248, 250, 252),
            Margin = new Padding(0, 0, 0, 8),
            Padding = new Padding(14, 8, 14, 8)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));

        var titleLabel = new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            Font = new Font(Font.FontFamily, 11F, FontStyle.Regular),
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(31, 42, 55)
        };
        var detailLabel = new Label
        {
            Text = detail,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(92, 105, 122)
        };
        var openButton = CreateButton("打开");
        openButton.Dock = DockStyle.Fill;
        openButton.AutoSize = false;
        openButton.Margin = new Padding(0);
        openButton.Click += (_, _) => _nav.SelectedIndex = navIndex;

        row.Controls.Add(titleLabel, 0, 0);
        row.Controls.Add(detailLabel, 1, 0);
        row.Controls.Add(openButton, 2, 0);
        return row;
    }

    private IEnumerable<string> GetDesktopEntries()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
        };

        return roots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFileSystemEntries(root))
            .Where(path =>
            {
                try
                {
                    var attributes = File.GetAttributes(path);
                    return !attributes.HasFlag(FileAttributes.Hidden) && !attributes.HasFlag(FileAttributes.System);
                }
                catch
                {
                    return false;
                }
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => Path.GetFileName(path), StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private void RemoveMissingDesktopCategoryItems()
    {
        foreach (var category in _config.DesktopCategories)
        {
            category.ItemPaths.RemoveAll(path => !File.Exists(path) && !Directory.Exists(path));
        }
    }

    private static string GetUniquePath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path) ?? "";
        var fileName = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        for (var index = 2; ; index++)
        {
            var candidate = Path.Combine(directory, $"{fileName} ({index}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var cleaned = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "未命名" : cleaned;
    }

    private static ProjectStatus PreviousStatus(ProjectStatus status)
    {
        return status switch
        {
            ProjectStatus.Doing => ProjectStatus.Todo,
            ProjectStatus.Done => ProjectStatus.Doing,
            _ => ProjectStatus.Todo
        };
    }

    private static ProjectStatus NextStatus(ProjectStatus status)
    {
        return status switch
        {
            ProjectStatus.Todo => ProjectStatus.Doing,
            ProjectStatus.Doing => ProjectStatus.Done,
            _ => ProjectStatus.Done
        };
    }

    private static void SetDragEffect(DragEventArgs e)
    {
        e.Effect = e.Data?.GetDataPresent(DataFormats.Text) == true ? DragDropEffects.Move : DragDropEffects.None;
    }

    private static void OpenPath(string path)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                MessageBox.Show("路径不存在。", "DustDesk", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "打开失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void FlushNote()
    {
        if (_activeNoteBox is not null)
        {
            _noteSaveTimer?.Stop();
            SaveActiveNote();
        }

        _noteSaveTimer?.Dispose();
        _noteSaveTimer = null;
        _activeNoteBox = null;
        _activeNoteItem = null;
    }

    private NoteItem EnsureNoteItem()
    {
        if (_notes.Items.Count == 0)
        {
            _notes.Items.Add(new NoteItem { Title = "note.md" });
            _store.SaveNotes(_notes);
        }

        return _notes.Items[0];
    }

    private void SaveActiveNote()
    {
        if (_activeNoteBox is null || _activeNoteBox.IsDisposed || _activeNoteItem is null)
        {
            return;
        }

        _activeNoteItem.Text = _activeNoteBox.Text;
        _activeNoteItem.UpdatedAt = DateTime.Now;
        _store.SaveNotes(_notes);
    }

    private void SaveDesktopNoteEdit(NoteItem item)
    {
        item.UpdatedAt = DateTime.Now;
        if (ReferenceEquals(_activeNoteItem, item) && _activeNoteBox is not null && !_activeNoteBox.IsDisposed && !string.Equals(_activeNoteBox.Text, item.Text, StringComparison.Ordinal))
        {
            _activeNoteBox.Text = item.Text;
        }

        _store.SaveNotes(_notes);
        RefreshDesktopNoteWidgets(item);
    }

    private DesktopNoteWidgetForm CreateDesktopNoteWidget(NoteItem item)
    {
        var widget = new DesktopNoteWidgetForm(item, () => SaveDesktopNoteEdit(item), () => OpenNoteManager(item), SaveDesktopNotePlacement);
        widget.FormClosed += (_, _) =>
        {
            _desktopNoteWidgets.Remove(widget);
            if (!_closingApp)
            {
                var placement = FindDesktopNotePlacement(item);
                if (placement is not null)
                {
                    _config.DesktopNoteWidgets.Remove(placement);
                    _store.SaveConfig(_config);
                }
            }
        };
        return widget;
    }

    private DesktopNoteWidgetPlacement EnsureDesktopNotePlacement(NoteItem item)
    {
        var placement = FindDesktopNotePlacement(item);
        if (placement is null)
        {
            placement = new DesktopNoteWidgetPlacement { NoteId = item.Id, Visible = true };
            _config.DesktopNoteWidgets.Add(placement);
        }

        placement.Visible = true;
        _store.SaveConfig(_config);
        return placement;
    }

    private DesktopNoteWidgetPlacement? FindDesktopNotePlacement(NoteItem item)
    {
        return _config.DesktopNoteWidgets.FirstOrDefault(placement => string.Equals(placement.NoteId, item.Id, StringComparison.Ordinal));
    }

    private void SaveDesktopNotePlacement(NoteItem item, Rectangle bounds)
    {
        var placement = EnsureDesktopNotePlacement(item);
        SavePlacement(placement, bounds);
    }

    private void SaveDesktopOrganizerPlacement(Rectangle bounds)
    {
        _config.DesktopOrganizerWidget ??= new WidgetPlacement { Visible = true };
        SavePlacement(_config.DesktopOrganizerWidget, bounds);
    }

    private void SaveDesktopTodoPlacement(Rectangle bounds)
    {
        _config.DesktopTodoWidget ??= new WidgetPlacement { Visible = true };
        SavePlacement(_config.DesktopTodoWidget, bounds);
    }

    private void SaveDesktopProjectPlacement(Rectangle bounds)
    {
        _config.DesktopProjectWidget ??= new WidgetPlacement { Visible = true };
        SavePlacement(_config.DesktopProjectWidget, bounds);
    }

    private void SaveDesktopLauncherPlacement(Rectangle bounds)
    {
        _config.DesktopLauncherWidget ??= new WidgetPlacement { Visible = true };
        SavePlacement(_config.DesktopLauncherWidget, bounds);
    }

    private void SaveDesktopSearchPlacement(Rectangle bounds)
    {
        _config.DesktopSearchWidget ??= new WidgetPlacement { Visible = true };
        SavePlacement(_config.DesktopSearchWidget, bounds);
    }

    private void SavePlacement(WidgetPlacement placement, Rectangle bounds)
    {
        placement.Visible = true;
        placement.X = bounds.X;
        placement.Y = bounds.Y;
        placement.Width = Math.Max(1, bounds.Width);
        placement.Height = Math.Max(1, bounds.Height);
        _store.SaveConfig(_config);
    }

    private void RestoreDesktopWidgets()
    {
        if (_config.DesktopOrganizerWidget is { Visible: true } organizerPlacement)
        {
            ShowDesktopOrganizerWidget(false);
        }

        if (_config.DesktopTodoWidget is { Visible: true } todoPlacement)
        {
            ShowDesktopTodoWidget(false);
        }

        if (_config.DesktopProjectWidget is { Visible: true } projectPlacement)
        {
            ShowDesktopProjectWidget(false);
        }

        if (_config.DesktopLauncherWidget is { Visible: true } launcherPlacement)
        {
            ShowDesktopLauncherWidget(false);
        }

        if (_config.DesktopSearchWidget is { Visible: true } searchPlacement)
        {
            ShowDesktopSearchWidget();
        }

        foreach (var placement in _config.DesktopNoteWidgets.Where(item => item.Visible).ToArray())
        {
            var note = _notes.Items.FirstOrDefault(item => string.Equals(item.Id, placement.NoteId, StringComparison.Ordinal));
            if (note is null || _desktopNoteWidgets.Any(widget => !widget.IsDisposed && widget.Displays(note)))
            {
                continue;
            }

            var widget = CreateDesktopNoteWidget(note);
            _desktopNoteWidgets.Add(widget);
            widget.ShowAsDesktopWidget(placement);
        }
    }

    private void OpenNoteManager(NoteItem item)
    {
        _pendingNoteSelection = item;
        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }

        Show();
        BringToFront();
        Activate();
        if (_nav.SelectedIndex == 3)
        {
            ShowPage(3);
        }
        else
        {
            _nav.SelectedIndex = 3;
        }
    }

    private int NextNoteColorArgb()
    {
        var palette = new[]
        {
            Color.FromArgb(255, 246, 167),
            Color.FromArgb(197, 238, 255),
            Color.FromArgb(211, 248, 218),
            Color.FromArgb(255, 214, 224),
            Color.FromArgb(231, 220, 255),
            Color.FromArgb(255, 226, 183)
        };
        return palette[_notes.Items.Count % palette.Length].ToArgb();
    }

    private void RefreshDesktopNoteWidgets(NoteItem item)
    {
        foreach (var widget in _desktopNoteWidgets.ToArray())
        {
            if (widget.IsDisposed)
            {
                _desktopNoteWidgets.Remove(widget);
                continue;
            }

            widget.RefreshNote(item);
        }
    }

    private void RefreshDesktopTodoWidget()
    {
        if (_desktopTodoWidget is null || _desktopTodoWidget.IsDisposed)
        {
            return;
        }

        _desktopTodoWidget.RefreshTodos();
    }

    private void RefreshDesktopProjectWidget()
    {
        if (_desktopProjectWidget is null || _desktopProjectWidget.IsDisposed)
        {
            foreach (var widget in _desktopProjectSplitWidgets.ToArray())
            {
                if (widget.IsDisposed)
                {
                    _desktopProjectSplitWidgets.Remove(widget);
                    continue;
                }

                widget.RefreshProjects();
            }
            return;
        }

        _desktopProjectWidget.RefreshProjects();
        foreach (var widget in _desktopProjectSplitWidgets.ToArray())
        {
            if (widget.IsDisposed)
            {
                _desktopProjectSplitWidgets.Remove(widget);
                continue;
            }

            widget.RefreshProjects();
        }
    }

    private void RefreshDesktopLauncherWidget()
    {
        if (_desktopLauncherWidget is null || _desktopLauncherWidget.IsDisposed)
        {
            return;
        }

        _desktopLauncherWidget.RefreshLaunchers();
    }

    private static string? Prompt(string title, string label, string value = "")
    {
        using var form = new Form
        {
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.None,
            MinimizeBox = false,
            MaximizeBox = false,
            ClientSize = new Size(440, 210),
            Font = new Font("Microsoft YaHei UI", 9F),
            BackColor = Color.FromArgb(22, 30, 42),
            ShowInTaskbar = false,
            Padding = new Padding(1)
        };
        using var formPath = RoundedRectanglePath(new Rectangle(0, 0, form.Width, form.Height), 12);
        form.Region = new Region(formPath);

        var chrome = new Panel
        {
            Dock = DockStyle.Top,
            Height = 48,
            BackColor = Color.FromArgb(32, 43, 60)
        };
        chrome.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                NativeGlass.BeginMove(form.Handle);
            }
        };
        var titleLabel = new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(18, 0, 0, 0)
        };
        titleLabel.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                NativeGlass.BeginMove(form.Handle);
            }
        };
        chrome.Controls.Add(titleLabel);
        var closeButton = CreatePromptButton("×", Color.Transparent, Color.FromArgb(205, 216, 230));
        closeButton.Dock = DockStyle.Right;
        closeButton.Width = 48;
        closeButton.Click += (_, _) => form.DialogResult = DialogResult.Cancel;
        chrome.Controls.Add(closeButton);

        var labelControl = new Label
        {
            Text = label,
            Left = 24,
            Top = 68,
            Width = 392,
            Height = 24,
            ForeColor = Color.FromArgb(198, 215, 235),
            Font = new Font("Microsoft YaHei UI", 10F)
        };
        var inputHost = new Panel
        {
            Left = 24,
            Top = 96,
            Width = 392,
            Height = 38,
            BackColor = Color.FromArgb(42, 53, 70),
            Padding = new Padding(12, 8, 12, 0)
        };
        var input = new TextBox
        {
            BorderStyle = BorderStyle.None,
            Dock = DockStyle.Fill,
            Text = value,
            BackColor = Color.FromArgb(42, 53, 70),
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 10F)
        };
        inputHost.Controls.Add(input);

        var okButton = CreatePromptButton("确定", Color.FromArgb(35, 107, 238), Color.White);
        okButton.SetBounds(236, 154, 84, 34);
        okButton.DialogResult = DialogResult.OK;
        var cancelButton = CreatePromptButton("取消", Color.FromArgb(58, 70, 88), Color.FromArgb(218, 230, 245));
        cancelButton.SetBounds(332, 154, 84, 34);
        cancelButton.DialogResult = DialogResult.Cancel;

        form.Controls.AddRange(new Control[] { chrome, labelControl, inputHost, okButton, cancelButton });
        form.AcceptButton = okButton;
        form.CancelButton = cancelButton;
        form.Shown += (_, _) =>
        {
            input.Focus();
            input.SelectAll();
        };

        return form.ShowDialog() == DialogResult.OK ? input.Text : null;
    }

    private static Button CreatePromptButton(string text, Color backColor, Color foreColor)
    {
        var button = new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            BackColor = backColor,
            ForeColor = foreColor,
            Cursor = Cursors.Hand,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Microsoft YaHei UI", 9.5F)
        };
        button.FlatAppearance.BorderSize = 0;
        return button;
    }

    private sealed class NoteTextBox : TextBox
    {
        private const int WmPaint = 0x000F;
        private Image? _background;
        private bool _imageOnly;

        public void SetBackground(string? path, bool imageOnly)
        {
            _background?.Dispose();
            _background = null;
            _imageOnly = imageOnly;

            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                try
                {
                    using var stream = new MemoryStream(File.ReadAllBytes(path));
                    using var image = Image.FromStream(stream);
                    _background = new Bitmap(image);
                }
                catch
                {
                    _background?.Dispose();
                    _background = null;
                }
            }

            Invalidate();
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WmPaint && _background is not null)
            {
                DrawBackgroundWatermark();
            }
        }

        private void DrawBackgroundWatermark()
        {
            var bounds = ClientRectangle;
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            if (ScrollBars is ScrollBars.Vertical or ScrollBars.Both)
            {
                bounds.Width = Math.Max(1, bounds.Width - SystemInformation.VerticalScrollBarWidth);
            }
            var width = _background!.Width;
            var height = _background.Height;
            if (!_imageOnly)
            {
                var scale = Math.Min(bounds.Width / (float)_background.Width, bounds.Height / (float)_background.Height);
                width = Math.Max(1, (int)(_background.Width * scale));
                height = Math.Max(1, (int)(_background.Height * scale));
            }

            var target = new Rectangle(bounds.X + (bounds.Width - width) / 2, bounds.Y + (bounds.Height - height) / 2, width, height);

            using var g = Graphics.FromHwnd(Handle);
            using var attributes = new System.Drawing.Imaging.ImageAttributes();
            var matrix = new System.Drawing.Imaging.ColorMatrix { Matrix33 = 1F };
            attributes.SetColorMatrix(matrix, System.Drawing.Imaging.ColorMatrixFlag.Default, System.Drawing.Imaging.ColorAdjustType.Bitmap);
            g.DrawImage(_background, target, 0, 0, _background.Width, _background.Height, GraphicsUnit.Pixel, attributes);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _background?.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class NoteEditorPanel : Panel
    {
        private Image? _background;

        public void SetBackground(string? path)
        {
            _background?.Dispose();
            _background = null;
            BackgroundImage = null;

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            try
            {
                using var stream = new MemoryStream(File.ReadAllBytes(path));
                using var image = Image.FromStream(stream);
                _background = new Bitmap(image);
                BackgroundImage = _background;
                BackgroundImageLayout = ImageLayout.Zoom;
            }
            catch
            {
                _background?.Dispose();
                _background = null;
                BackgroundImage = null;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _background?.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class DesktopEntry
    {
        public DesktopEntry(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public override string ToString() => System.IO.Path.GetFileName(Path);
    }
}

internal sealed class BufferedPanel : Panel
{
    public BufferedPanel()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
}

internal static class ProjectExcelExporter
{
    private static readonly string[] Headers =
    {
        "项目",
        "项目路径",
        "事项",
        "状态",
        "开始日期",
        "截止日期",
        "进度",
        "事项路径",
        "子任务",
        "子任务完成",
        "子任务路径"
    };

    private static readonly double[] ColumnWidths = { 18, 42, 24, 12, 14, 14, 10, 42, 28, 12, 42 };

    public static void Export(ProjectData data, string path)
    {
        var rows = BuildRows(data);
        var hyperlinks = new List<HyperlinkInfo>();

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        AddText(archive, "[Content_Types].xml", ContentTypesXml());
        AddText(archive, "_rels/.rels", PackageRelationshipsXml());
        AddText(archive, "xl/workbook.xml", WorkbookXml());
        AddText(archive, "xl/_rels/workbook.xml.rels", WorkbookRelationshipsXml());
        AddText(archive, "xl/styles.xml", StylesXml());
        AddText(archive, "xl/worksheets/sheet1.xml", WorksheetXml(rows, hyperlinks));
        if (hyperlinks.Count > 0)
        {
            AddText(archive, "xl/worksheets/_rels/sheet1.xml.rels", RelationshipsXml(hyperlinks));
        }
    }

    private static List<List<ExportCell>> BuildRows(ProjectData data)
    {
        var rows = new List<List<ExportCell>>
        {
            Headers.Select(header => new ExportCell(header, null, 1)).ToList()
        };

        foreach (var project in data.Projects)
        {
            if (project.Items.Count == 0)
            {
                rows.Add(ProjectRow(project, null, null));
                continue;
            }

            foreach (var item in project.Items)
            {
                if (item.SubItems.Count == 0)
                {
                    rows.Add(ProjectRow(project, item, null));
                    continue;
                }

                foreach (var subItem in item.SubItems)
                {
                    rows.Add(ProjectRow(project, item, subItem));
                }
            }
        }

        return rows;
    }

    private static List<ExportCell> ProjectRow(ProjectBoard project, ProjectItem? item, ProjectSubItem? subItem)
    {
        return new List<ExportCell>
        {
            new(project.Name),
            PathCell(project.ProjectPath),
            new(item?.Title ?? ""),
            new(item is null ? "" : StatusText(item.Status)),
            new(item?.StartDate?.ToString("yyyy/MM/dd") ?? ""),
            new(item?.EndDate?.ToString("yyyy/MM/dd") ?? ""),
            new(item is null ? "" : $"{ProjectProgressPercent(item)}%"),
            PathCell(item?.ProjectPath ?? ""),
            new(subItem?.Title ?? ""),
            new(subItem is null ? "" : subItem.Done ? "是" : "否"),
            PathCell(subItem?.FilePath ?? "")
        };
    }

    private static ExportCell PathCell(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new ExportCell("");
        }

        var target = HyperlinkTarget(path);
        return new ExportCell(path, target, target is null ? 0 : 2);
    }

    private static string? HyperlinkTarget(string path)
    {
        var value = path.Trim();
        try
        {
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeFile))
            {
                return uri.AbsoluteUri;
            }

            if (Path.IsPathFullyQualified(value) || value.Contains(Path.DirectorySeparatorChar) || value.Contains(Path.AltDirectorySeparatorChar))
            {
                return new Uri(Path.GetFullPath(value)).AbsoluteUri;
            }
        }
        catch
        {
        }

        return null;
    }

    private static int ProjectProgressPercent(ProjectItem item)
    {
        if (item.SubItems.Count > 0)
        {
            var completed = item.SubItems.Count(subItem => subItem.Done);
            return (int)Math.Round(completed * 100D / item.SubItems.Count, MidpointRounding.AwayFromZero);
        }

        if (item.ProgressPercent >= 0)
        {
            return Math.Clamp(item.ProgressPercent, 0, 100);
        }

        return item.Status switch
        {
            ProjectStatus.Done => 100,
            ProjectStatus.Doing => 50,
            _ => 0
        };
    }

    private static string StatusText(ProjectStatus status)
    {
        return status switch
        {
            ProjectStatus.Doing => "进行中",
            ProjectStatus.Done => "已完成",
            _ => "待开始"
        };
    }

    private static string WorksheetXml(IReadOnlyList<List<ExportCell>> rows, List<HyperlinkInfo> hyperlinks)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");
        sb.Append("<dimension ref=\"A1:").Append(CellReference(rows.Count, Headers.Length)).Append("\"/>");
        sb.Append("<sheetViews><sheetView workbookViewId=\"0\"/></sheetViews>");
        sb.Append("<sheetFormatPr defaultRowHeight=\"18\"/>");
        sb.Append("<cols>");
        for (var i = 0; i < ColumnWidths.Length; i++)
        {
            sb.Append("<col min=\"").Append(i + 1).Append("\" max=\"").Append(i + 1).Append("\" width=\"")
                .Append(ColumnWidths[i].ToString(CultureInfo.InvariantCulture)).Append("\" customWidth=\"1\"/>");
        }
        sb.Append("</cols><sheetData>");

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var rowNumber = rowIndex + 1;
            sb.Append("<row r=\"").Append(rowNumber).Append("\">");
            var row = rows[rowIndex];
            for (var columnIndex = 0; columnIndex < row.Count; columnIndex++)
            {
                var cell = row[columnIndex];
                var reference = CellReference(rowNumber, columnIndex + 1);
                AppendCell(sb, reference, cell);
                if (!string.IsNullOrWhiteSpace(cell.Hyperlink))
                {
                    var relId = $"rId{hyperlinks.Count + 1}";
                    hyperlinks.Add(new HyperlinkInfo(reference, cell.Hyperlink, relId));
                }
            }
            sb.Append("</row>");
        }

        sb.Append("</sheetData>");
        if (hyperlinks.Count > 0)
        {
            sb.Append("<hyperlinks>");
            foreach (var hyperlink in hyperlinks)
            {
                sb.Append("<hyperlink ref=\"").Append(hyperlink.Reference).Append("\" r:id=\"").Append(hyperlink.RelationshipId).Append("\"/>");
            }
            sb.Append("</hyperlinks>");
        }

        sb.Append("</worksheet>");
        return sb.ToString();
    }

    private static void AppendCell(StringBuilder sb, string reference, ExportCell cell)
    {
        sb.Append("<c r=\"").Append(reference).Append("\"");
        if (cell.Style > 0)
        {
            sb.Append(" s=\"").Append(cell.Style).Append("\"");
        }

        sb.Append(" t=\"inlineStr\"><is><t");
        if (NeedsPreserveSpace(cell.Text))
        {
            sb.Append(" xml:space=\"preserve\"");
        }
        sb.Append(">").Append(XmlEscape(cell.Text)).Append("</t></is></c>");
    }

    private static bool NeedsPreserveSpace(string text)
    {
        return text.Length > 0 && (char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1]));
    }

    private static string CellReference(int row, int column)
    {
        var name = "";
        var current = column;
        while (current > 0)
        {
            current--;
            name = (char)('A' + current % 26) + name;
            current /= 26;
        }

        return $"{name}{row}";
    }

    private static string RelationshipsXml(IEnumerable<HyperlinkInfo> hyperlinks)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
        foreach (var hyperlink in hyperlinks)
        {
            sb.Append("<Relationship Id=\"").Append(hyperlink.RelationshipId)
                .Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink\" Target=\"")
                .Append(XmlEscape(hyperlink.Target))
                .Append("\" TargetMode=\"External\"/>");
        }
        sb.Append("</Relationships>");
        return sb.ToString();
    }

    private static string ContentTypesXml()
    {
        return """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
              <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
              <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
            </Types>
            """;
    }

    private static string PackageRelationshipsXml()
    {
        return """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
            </Relationships>
            """;
    }

    private static string WorkbookRelationshipsXml()
    {
        return """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
              <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
            </Relationships>
            """;
    }

    private static string WorkbookXml()
    {
        return """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets>
                <sheet name="项目管理" sheetId="1" r:id="rId1"/>
              </sheets>
            </workbook>
            """;
    }

    private static string StylesXml()
    {
        return """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <fonts count="3">
                <font><sz val="11"/><name val="Microsoft YaHei UI"/></font>
                <font><b/><sz val="11"/><name val="Microsoft YaHei UI"/></font>
                <font><u/><color rgb="FF0563C1"/><sz val="11"/><name val="Microsoft YaHei UI"/></font>
              </fonts>
              <fills count="2">
                <fill><patternFill patternType="none"/></fill>
                <fill><patternFill patternType="gray125"/></fill>
              </fills>
              <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
              <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
              <cellXfs count="3">
                <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
                <xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1"/>
                <xf numFmtId="0" fontId="2" fillId="0" borderId="0" xfId="0" applyFont="1"/>
              </cellXfs>
              <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
              <dxfs count="0"/>
              <tableStyles count="0" defaultTableStyle="TableStyleMedium2" defaultPivotStyle="PivotStyleLight16"/>
            </styleSheet>
            """;
    }

    private static void AddText(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string XmlEscape(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (c != '\t' && c != '\n' && c != '\r' && c < ' ')
            {
                continue;
            }

            sb.Append(c switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '"' => "&quot;",
                '\'' => "&apos;",
                _ => c
            });
        }

        return sb.ToString();
    }

    private sealed record ExportCell(string Text, string? Hyperlink = null, int Style = 0);

    private sealed record HyperlinkInfo(string Reference, string Target, string RelationshipId);
}

internal sealed class BufferedTableLayoutPanel : TableLayoutPanel
{
    public BufferedTableLayoutPanel()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
}

internal sealed class GlassPanel : Panel
{
    public int Radius { get; set; } = 10;
    public Color BorderColor { get; set; } = Color.FromArgb(80, 100, 124);

    public GlassPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = ClientRectangle;
        rect.Width -= 1;
        rect.Height -= 1;

        using var path = CreateRoundedPath(rect, Radius);
        using var brush = new SolidBrush(BackColor);
        using var pen = new Pen(BorderColor);
        e.Graphics.FillPath(brush, path);
        e.Graphics.DrawPath(pen, path);
    }

    private static GraphicsPath CreateRoundedPath(Rectangle rect, int radius)
    {
        if (radius <= 0)
        {
            var square = new GraphicsPath();
            square.AddRectangle(rect);
            return square;
        }

        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal static class NoteStyle
{
    public static int NormalizeTextColorArgb(int argb)
    {
        var color = Color.FromArgb(argb);
        return Color.FromArgb(255, color.R, color.G, color.B).ToArgb();
    }

    public static Color TextColor(NoteItem note)
    {
        return Color.FromArgb(NormalizeTextColorArgb(note.FontColorArgb));
    }
}

internal sealed class DesktopTodoWidgetForm : Form
{
    private const int WsExToolWindow = 0x00000080;
    private const int WsExAppWindow = 0x00040000;
    private const int WmContextMenu = 0x007B;

    private readonly DesktopTodoWidgetView _view;
    private readonly Action<Rectangle> _placementChanged;
    private readonly Action<bool> _transparentChanged;
    private bool _transparent;
    private bool _attachedToDesktop;
    private bool _restoringPlacement;

    public DesktopTodoWidgetForm(TodoData todos, Action<IWin32Window?> addRequested, Action todosChanged, Action manageRequested, Action<Rectangle> placementChanged, bool transparent, Action<bool> transparentChanged)
    {
        _placementChanged = placementChanged;
        _transparent = transparent;
        _transparentChanged = transparentChanged;
        _view = new DesktopTodoWidgetView(todos, () => addRequested(this), todosChanged, manageRequested, _transparent, SetTransparent)
        {
            Dock = DockStyle.Fill
        };

        Text = "今日工作记录";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        ShowIcon = false;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(420, 360);
        MinimumSize = new Size(300, 220);
        BackColor = Color.FromArgb(20, 28, 40);
        _view.BeginMoveRequested += () => NativeGlass.BeginMove(Handle);
        _view.BeginResizeRequested += () => NativeGlass.BeginResize(Handle, 17);
        _view.CloseRequested += Close;
        Controls.Add(_view);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WsExToolWindow;
            cp.ExStyle &= ~WsExAppWindow;
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmContextMenu)
        {
            m.Result = IntPtr.Zero;
            return;
        }

        base.WndProc(ref m);
    }

    public void ShowAsDesktopWidget(WidgetPlacement? placement = null)
    {
        ApplyPlacementOrDefault(placement);
        Show();
        BringToFront();
        AttachToDesktopHost();
        SavePlacement();
    }

    public void RefreshTodos()
    {
        _view.RefreshTodos();
    }

    protected override void OnMove(EventArgs e)
    {
        base.OnMove(e);
        SavePlacement();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateRoundedRegion();
        SavePlacement();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        AttachToDesktopHost();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyWidgetSkin();
    }

    private void SetTransparent(bool transparent)
    {
        _transparent = transparent;
        _transparentChanged(_transparent);
        ApplyWidgetSkin();
    }

    private void ApplyWidgetSkin()
    {
        BackColor = Color.FromArgb(20, 28, 40);
        _view.BackColor = BackColor;
        if (IsHandleCreated)
        {
            NativeGlass.EnableAcrylic(Handle, _transparent ? Color.FromArgb(34, 74, 138, 210) : Color.FromArgb(232, 18, 26, 38));
        }

        _view.Invalidate();
    }

    private void AttachToDesktopHost()
    {
        if (_attachedToDesktop || !IsHandleCreated)
        {
            return;
        }

        _attachedToDesktop = NativeGlass.AttachToDesktop(Handle);
    }

    private void ApplyPlacementOrDefault(WidgetPlacement? placement)
    {
        _restoringPlacement = true;
        try
        {
            if (placement is not null && placement.Width > 0 && placement.Height > 0)
            {
                Bounds = new Rectangle(placement.X, placement.Y, Math.Max(MinimumSize.Width, placement.Width), Math.Max(MinimumSize.Height, placement.Height));
                return;
            }

            var workArea = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
            Location = new Point(Math.Max(workArea.Left + 36, workArea.Right - Width - 56), workArea.Top + 470);
        }
        finally
        {
            _restoringPlacement = false;
        }
    }

    private void SavePlacement()
    {
        if (_restoringPlacement || !IsHandleCreated || WindowState == FormWindowState.Minimized)
        {
            return;
        }

        _placementChanged(Bounds);
    }

    private void UpdateRoundedRegion()
    {
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0)
        {
            return;
        }

        using var path = CreateRoundPath(new Rectangle(0, 0, ClientSize.Width, ClientSize.Height), 10);
        Region?.Dispose();
        Region = new Region(path);
    }

    private static GraphicsPath CreateRoundPath(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        rect.Width -= 1;
        rect.Height -= 1;
        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class DesktopLauncherWidgetForm : Form
{
    private const int WsExToolWindow = 0x00000080;
    private const int WsExAppWindow = 0x00040000;
    private const int WmContextMenu = 0x007B;
    private const int VisibleInset = 8;
    private const int LauncherChromeHeightDelta = 38;

    private readonly DesktopLauncherWidgetView _view;
    private readonly Action<Rectangle> _placementChanged;
    private readonly Func<string, string?, bool, bool> _pathDropped;
    private readonly Action<bool> _transparentChanged;
    private readonly Action<bool> _snapChanged;
    private bool _transparent;
    private bool _snap;
    private bool _attachedToDesktop;
    private bool _restoringPlacement;
    private bool _snapping;
    private bool _adjustingChromeHeight;
    private bool _launcherChromeExpanded;
    private bool _launcherChromeExpandedUp;
    private int _launcherChromeBaseHeight;

    public DesktopLauncherWidgetForm(LaunchData launchers, Action<Rectangle> placementChanged, Func<string, string?, bool, bool> pathDropped, bool transparent, Action<bool> transparentChanged, bool snap, Action<bool> snapChanged)
    {
        _placementChanged = placementChanged;
        _pathDropped = pathDropped;
        _transparent = transparent;
        _transparentChanged = transparentChanged;
        _snap = snap;
        _snapChanged = snapChanged;
        _view = new DesktopLauncherWidgetView(launchers, _pathDropped, _transparent, SetTransparent, _snap, SetSnap)
        {
            Dock = DockStyle.Fill
        };

        Text = "快捷启动";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        ShowIcon = false;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(420, 128);
        MinimumSize = new Size(96, 96);
        BackColor = Color.FromArgb(20, 28, 40);
        _view.BeginMoveRequested += () => NativeGlass.BeginMove(Handle);
        _view.BeginResizeRequested += () => NativeGlass.BeginResize(Handle, 17);
        _view.CloseRequested += Close;
        _view.ManageRequested += () => ManageRequested?.Invoke();
        _view.ChromeVisibilityChanged += AdjustChromeHeight;
        Controls.Add(_view);
    }

    public event Action? ManageRequested;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WsExToolWindow;
            cp.ExStyle &= ~WsExAppWindow;
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmContextMenu)
        {
            _view.ShowChrome();
            m.Result = IntPtr.Zero;
            return;
        }

        base.WndProc(ref m);
    }

    public void ShowAsDesktopWidget(WidgetPlacement? placement = null)
    {
        ApplyPlacementOrDefault(placement);
        AdjustChromeHeight(_view.ChromeVisible);
        EnsureVisibleOnScreen();
        Show();
        BringToFront();
        AttachToDesktopHost();
        EnsureVisibleOnScreen();
        SavePlacement();
    }

    public void RefreshLaunchers()
    {
        _view.RefreshLaunchers();
    }

    protected override void OnMove(EventArgs e)
    {
        base.OnMove(e);
        SnapToEdges();
        SavePlacement();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateRoundedRegion();
        SavePlacement();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        AttachToDesktopHost();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyWidgetSkin();
    }

    private void SetTransparent(bool transparent)
    {
        _transparent = transparent;
        _transparentChanged(_transparent);
        ApplyWidgetSkin();
    }

    private void SetSnap(bool snap)
    {
        _snap = snap;
        _snapChanged(_snap);
        SnapToEdges();
        SavePlacement();
    }

    private void ApplyWidgetSkin()
    {
        BackColor = Color.FromArgb(20, 28, 40);
        _view.BackColor = BackColor;
        if (IsHandleCreated)
        {
            NativeGlass.EnableAcrylic(Handle, _transparent ? Color.FromArgb(34, 74, 138, 210) : Color.FromArgb(232, 18, 26, 38));
        }

        _view.Invalidate();
    }

    private void AttachToDesktopHost()
    {
        if (_attachedToDesktop || !IsHandleCreated)
        {
            return;
        }

        _attachedToDesktop = NativeGlass.AttachToDesktop(Handle);
    }

    private void ApplyPlacementOrDefault(WidgetPlacement? placement)
    {
        _restoringPlacement = true;
        try
        {
            if (placement is not null && placement.Width > 0 && placement.Height > 0)
            {
                SetScreenBounds(NormalizeScreenBounds(new Rectangle(placement.X, placement.Y, Math.Max(MinimumSize.Width, placement.Width), Math.Max(MinimumSize.Height, placement.Height))));
                return;
            }

            var workArea = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
            SetScreenBounds(NormalizeScreenBounds(new Rectangle(Math.Max(workArea.Left + 40, workArea.Right - Width - 80), workArea.Top + 180, Width, Height)));
        }
        finally
        {
            _restoringPlacement = false;
        }
    }

    private void SavePlacement()
    {
        if (_restoringPlacement || _adjustingChromeHeight || !IsHandleCreated || WindowState == FormWindowState.Minimized)
        {
            return;
        }

        _placementChanged(GetPlacementBounds());
    }

    private void SnapToEdges(bool force = false)
    {
        if (!_snap || _restoringPlacement || _adjustingChromeHeight || _snapping || !IsHandleCreated || WindowState == FormWindowState.Minimized)
        {
            return;
        }

        var current = GetScreenBounds();
        var workArea = Screen.FromRectangle(current).WorkingArea;
        const int threshold = 48;
        var target = current;
        if (force || Math.Abs(target.Bottom - workArea.Bottom) <= threshold)
        {
            target.Y = workArea.Bottom - target.Height - VisibleInset;
        }
        else if (Math.Abs(target.Top - workArea.Top) <= threshold)
        {
            target.Y = workArea.Top + VisibleInset;
        }

        if (force || Math.Abs(target.Left - workArea.Left) <= threshold)
        {
            target.X = workArea.Left + VisibleInset;
        }
        else if (Math.Abs(target.Right - workArea.Right) <= threshold)
        {
            target.X = workArea.Right - target.Width - VisibleInset;
        }

        target = NormalizeScreenBounds(target);

        if (target == current)
        {
            return;
        }

        _snapping = true;
        try
        {
            SetScreenBounds(target);
        }
        finally
        {
            _snapping = false;
        }
    }

    private void AdjustChromeHeight(bool visible)
    {
        var current = GetScreenBounds();
        if (current.Width <= 0 || current.Height <= 0)
        {
            return;
        }

        if (visible)
        {
            if (_launcherChromeExpanded)
            {
                return;
            }

            _launcherChromeBaseHeight = current.Height;
            var workArea = Screen.FromRectangle(current).WorkingArea;
            var workBottom = workArea.Bottom - VisibleInset;
            _launcherChromeExpandedUp = current.Bottom + LauncherChromeHeightDelta > workBottom
                || (_snap && Math.Abs(current.Bottom - workBottom) <= 48);
            var target = new Rectangle(
                current.X,
                _launcherChromeExpandedUp ? current.Y - LauncherChromeHeightDelta : current.Y,
                current.Width,
                current.Height + LauncherChromeHeightDelta);
            SetAdjustedScreenBounds(target);
            _launcherChromeExpanded = true;
            return;
        }

        if (!_launcherChromeExpanded)
        {
            return;
        }

        var restoredHeight = Math.Max(MinimumSize.Height, _launcherChromeBaseHeight > 0 ? _launcherChromeBaseHeight : current.Height - LauncherChromeHeightDelta);
        var delta = current.Height - restoredHeight;
        var restored = new Rectangle(
            current.X,
            _launcherChromeExpandedUp ? current.Y + delta : current.Y,
            current.Width,
            restoredHeight);
        SetAdjustedScreenBounds(restored);
        _launcherChromeExpanded = false;
        _launcherChromeExpandedUp = false;
        _launcherChromeBaseHeight = 0;
    }

    private void SetAdjustedScreenBounds(Rectangle bounds)
    {
        _adjustingChromeHeight = true;
        try
        {
            SetScreenBounds(NormalizeScreenBounds(bounds));
        }
        finally
        {
            _adjustingChromeHeight = false;
        }
    }

    private Rectangle GetPlacementBounds()
    {
        var bounds = NormalizeScreenBounds(GetScreenBounds());
        if (!_launcherChromeExpanded)
        {
            return bounds;
        }

        var restoredHeight = Math.Max(MinimumSize.Height, _launcherChromeBaseHeight > 0 ? _launcherChromeBaseHeight : bounds.Height - LauncherChromeHeightDelta);
        var delta = bounds.Height - restoredHeight;
        return NormalizeScreenBounds(new Rectangle(
            bounds.X,
            _launcherChromeExpandedUp ? bounds.Y + delta : bounds.Y,
            bounds.Width,
            restoredHeight));
    }

    private Rectangle GetScreenBounds()
    {
        return IsHandleCreated ? RectangleToScreen(ClientRectangle) : Bounds;
    }

    private void SetScreenBounds(Rectangle bounds)
    {
        if (_attachedToDesktop && IsHandleCreated)
        {
            NativeGlass.SetDesktopChildScreenBounds(Handle, bounds);
            return;
        }

        Bounds = bounds;
    }

    private void EnsureVisibleOnScreen()
    {
        var current = GetScreenBounds();
        var target = NormalizeScreenBounds(current);
        if (target != current)
        {
            SetScreenBounds(target);
        }
    }

    private Rectangle NormalizeScreenBounds(Rectangle bounds)
    {
        var workArea = Screen.FromRectangle(bounds).WorkingArea;
        var maxWidth = Math.Max(MinimumSize.Width, workArea.Width - VisibleInset * 2);
        var maxHeight = Math.Max(MinimumSize.Height, workArea.Height - VisibleInset * 2);
        var width = Math.Clamp(bounds.Width, MinimumSize.Width, maxWidth);
        var height = Math.Clamp(bounds.Height, MinimumSize.Height, maxHeight);
        var minX = workArea.Left + VisibleInset;
        var minY = workArea.Top + VisibleInset;
        var maxX = Math.Max(minX, workArea.Right - width - VisibleInset);
        var maxY = Math.Max(minY, workArea.Bottom - height - VisibleInset);
        return new Rectangle(Math.Clamp(bounds.X, minX, maxX), Math.Clamp(bounds.Y, minY, maxY), width, height);
    }

    private void UpdateRoundedRegion()
    {
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0)
        {
            return;
        }

        using var path = CreateRoundPath(new Rectangle(0, 0, ClientSize.Width, ClientSize.Height), 10);
        Region?.Dispose();
        Region = new Region(path);
    }

    private static GraphicsPath CreateRoundPath(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        rect.Width -= 1;
        rect.Height -= 1;
        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class DesktopLauncherWidgetView : Control
{
    private const int WmContextMenu = 0x007B;
    private const int MaxLaunchers = 5;

    private readonly LaunchData _launchers;
    private readonly Func<string, string?, bool, bool> _pathDropped;
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _transparentMenuItem = new("透明");
    private readonly ToolStripMenuItem _snapMenuItem = new("吸附");
    private readonly System.Windows.Forms.Timer _chromeTimer = new();
    private readonly Action<bool> _transparentChanged;
    private readonly Action<bool> _snapChanged;
    private readonly Dictionary<string, Image> _iconCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(Rectangle Rect, LaunchItem Item)> _launcherAreas = new();
    private readonly Image? _settingsIcon;
    private Rectangle _settingsRect;
    private Rectangle _resizeRect;
    private bool _transparent;
    private bool _snap;
    private bool _chromeVisible = true;
    private bool _suppressNextContextMenu;

    private static readonly Color CardFill = Color.FromArgb(222, 24, 34, 48);
    private static readonly Color CardBorder = Color.FromArgb(98, 126, 154, 184);
    private Color CurrentCardFill => _transparent ? Color.FromArgb(70, 24, 34, 48) : CardFill;
    private Color CurrentCardBorder => _transparent ? Color.FromArgb(132, 150, 194, 238) : CardBorder;
    private static readonly Color TextMain = Color.FromArgb(242, 246, 252);
    private static readonly Color TextMuted = Color.FromArgb(170, 188, 210);
    private static readonly Font TitleFont = new("Microsoft YaHei UI", 12F, FontStyle.Regular);
    private static readonly Font SmallFont = new("Microsoft YaHei UI", 8.5F);

    public DesktopLauncherWidgetView(LaunchData launchers, Func<string, string?, bool, bool> pathDropped, bool transparent, Action<bool> transparentChanged, bool snap, Action<bool> snapChanged)
    {
        _launchers = launchers;
        _pathDropped = pathDropped;
        _transparent = transparent;
        _transparentChanged = transparentChanged;
        _snap = snap;
        _snapChanged = snapChanged;
        AllowDrop = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        using var settingsIcon = LoadLauncherWidgetImage("images", "zhuomianguinarongqi", "shezhi.png");
        _settingsIcon = settingsIcon is null ? null : TintImage(settingsIcon, Color.FromArgb(130, 180, 255));
        _transparentMenuItem.CheckOnClick = true;
        _transparentMenuItem.Checked = _transparent;
        _transparentMenuItem.Click += (_, _) =>
        {
            _transparent = _transparentMenuItem.Checked;
            _transparentChanged(_transparent);
            Invalidate();
        };
        _snapMenuItem.CheckOnClick = true;
        _snapMenuItem.Checked = _snap;
        _snapMenuItem.Click += (_, _) =>
        {
            _snap = _snapMenuItem.Checked;
            _snapChanged(_snap);
            Invalidate();
        };
        _menu.Items.Add("管理", null, (_, _) => ManageRequested?.Invoke());
        var skinMenu = new ToolStripMenuItem("皮肤设置");
        skinMenu.DropDownItems.Add(_transparentMenuItem);
        _menu.Items.Add(skinMenu);
        _menu.Items.Add(_snapMenuItem);
        _menu.Items.Add("关闭", null, (_, _) => CloseRequested?.Invoke());
        _menu.Opening += (_, _) =>
        {
            _transparentMenuItem.Checked = _transparent;
            _snapMenuItem.Checked = _snap;
        };
        _chromeTimer.Interval = 5000;
        _chromeTimer.Tick += (_, _) =>
        {
            _chromeTimer.Stop();
            if (!_chromeVisible)
            {
                return;
            }

            _chromeVisible = false;
            ChromeVisibilityChanged?.Invoke(false);
            Invalidate();
        };
        RestartChromeTimer();
    }

    public event Action? BeginMoveRequested;
    public event Action? BeginResizeRequested;
    public event Action? CloseRequested;
    public event Action? ManageRequested;
    public event Action<bool>? ChromeVisibilityChanged;

    public bool ChromeVisible => _chromeVisible;

    public void RefreshLaunchers()
    {
        foreach (var image in _iconCache.Values)
        {
            image.Dispose();
        }

        _iconCache.Clear();
        Invalidate();
    }

    public void ShowLauncherMenu(Point location)
    {
        ShowMenuAbove(location);
    }

    private void ShowMenuAbove(Point anchor)
    {
        var preferredSize = _menu.GetPreferredSize(Size.Empty);
        var x = Math.Clamp(anchor.X, 0, Math.Max(0, Width - preferredSize.Width));
        var y = Math.Max(0, anchor.Y - preferredSize.Height - 4);
        _menu.Show(this, new Point(x, y));
    }

    public void ShowChrome()
    {
        if (!_chromeVisible)
        {
            _chromeVisible = true;
        }

        ChromeVisibilityChanged?.Invoke(true);
        RestartChromeTimer();
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var image in _iconCache.Values)
            {
                image.Dispose();
            }

            _iconCache.Clear();
            _settingsIcon?.Dispose();
            _chromeTimer.Dispose();
            _menu.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            _suppressNextContextMenu = true;
            if (_chromeVisible && _settingsRect.Contains(e.Location))
            {
                ShowChrome();
                ShowMenuAbove(new Point(_settingsRect.Left, _settingsRect.Top));
            }
            else
            {
                ShowChrome();
            }

            return;
        }

        if (e.Button != MouseButtons.Left)
        {
            base.OnMouseDown(e);
            return;
        }

        if (_resizeRect.Contains(e.Location))
        {
            BeginResizeRequested?.Invoke();
            return;
        }

        if (_chromeVisible && _settingsRect.Contains(e.Location))
        {
            ShowChrome();
            ShowMenuAbove(new Point(_settingsRect.Left, _settingsRect.Top));
            return;
        }

        var hit = _launcherAreas.FirstOrDefault(item => item.Rect.Contains(e.Location));
        if (hit.Item is not null)
        {
            OpenLauncher(hit.Item.Path);
            return;
        }

        if (_chromeVisible && e.Y <= 52)
        {
            BeginMoveRequested?.Invoke();
            return;
        }

        base.OnMouseDown(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmContextMenu)
        {
            if (_suppressNextContextMenu)
            {
                _suppressNextContextMenu = false;
                m.Result = IntPtr.Zero;
                return;
            }

            ShowChrome();
            m.Result = IntPtr.Zero;
            return;
        }

        base.WndProc(ref m);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        Cursor = _resizeRect.Contains(e.Location) ? Cursors.SizeNWSE
            : (_chromeVisible && _settingsRect.Contains(e.Location)) || _launcherAreas.Any(item => item.Rect.Contains(e.Location)) ? Cursors.Hand
            : Cursors.Default;
        base.OnMouseMove(e);
    }

    protected override void OnDragEnter(DragEventArgs e)
    {
        e.Effect = GetDroppedPath(e) is null || _launchers.Items.Count >= MaxLaunchers
            ? DragDropEffects.None
            : DragDropEffects.Copy;
        base.OnDragEnter(e);
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        e.Effect = GetDroppedPath(e) is null || _launchers.Items.Count >= MaxLaunchers
            ? DragDropEffects.None
            : DragDropEffects.Copy;
        base.OnDragOver(e);
    }

    protected override void OnDragDrop(DragEventArgs e)
    {
        var path = GetDroppedPath(e);
        if (path is not null && _pathDropped(path, null, true))
        {
            RefreshLaunchers();
        }

        base.OnDragDrop(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        _launcherAreas.Clear();

        var card = new Rectangle(0, 0, Width, Height);
        FillRound(g, card, CurrentCardFill, 10);
        DrawRound(g, new Rectangle(0, 0, Width - 1, Height - 1), CurrentCardBorder, 10);

        _settingsRect = Rectangle.Empty;
        var topInset = _chromeVisible ? 50 : 12;
        if (_chromeVisible)
        {
            TextRenderer.DrawText(g, "□  快捷启动", TitleFont, new Rectangle(16, 12, Math.Max(80, Width - 90), 28), TextMain, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            _settingsRect = new Rectangle(Width - 48, 10, 32, 32);
            if (_settingsIcon is not null)
            {
                g.DrawImage(_settingsIcon, _settingsRect);
            }
            else
            {
                DrawGearIcon(g, _settingsRect, Color.FromArgb(130, 180, 255));
            }
        }

        var area = new Rectangle(14, topInset, Width - 28, Math.Max(1, Height - topInset - 24));
        if (_launchers.Items.Count == 0)
        {
            DrawCentered(g, "暂无快捷启动", SmallFont, TextMuted, area);
            DrawResizeGrip(g);
            return;
        }

        var tileW = 78;
        var tileH = 82;
        var horizontal = Width >= Height;
        for (var i = 0; i < _launchers.Items.Count; i++)
        {
            var tile = horizontal
                ? new Rectangle(area.X + i * tileW, area.Y, tileW - 8, Math.Min(tileH, area.Height))
                : new Rectangle(area.X, area.Y + i * tileH, Math.Min(tileW, area.Width), tileH - 6);
            if (tile.Right > area.Right || tile.Bottom > area.Bottom)
            {
                break;
            }

            DrawLauncher(g, tile, _launchers.Items[i]);
            _launcherAreas.Add((tile, _launchers.Items[i]));
        }

        DrawResizeGrip(g);
    }

    private void DrawLauncher(Graphics g, Rectangle rect, LaunchItem item)
    {
        var icon = new Rectangle(rect.X + rect.Width / 2 - 24, rect.Y + 2, 48, 48);
        var shellIcon = GetShellIcon(item.Path);
        if (shellIcon is not null)
        {
            g.DrawImage(shellIcon, icon);
        }
        else
        {
            FillRound(g, icon, Color.FromArgb(58, 126, 246), 8);
            DrawCentered(g, string.IsNullOrWhiteSpace(item.Name) ? "+" : item.Name[..1], TitleFont, Color.White, icon);
        }

        DrawCentered(g, item.Name, SmallFont, TextMain, new Rectangle(rect.X, icon.Bottom + 3, rect.Width, 24));
    }

    private void RestartChromeTimer()
    {
        _chromeTimer.Stop();
        _chromeTimer.Start();
    }

    private Image? GetShellIcon(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (_iconCache.TryGetValue(path, out var cached))
        {
            return cached;
        }

        var icon = ShellIconLoader.LoadLargeIcon(path);
        if (icon is not null)
        {
            _iconCache[path] = icon;
        }

        return icon;
    }

    private void DrawResizeGrip(Graphics g)
    {
        _resizeRect = new Rectangle(Width - 24, Height - 24, 18, 18);
        using var pen = new Pen(Color.FromArgb(120, 158, 178, 204), 1F);
        g.DrawLine(pen, _resizeRect.Right - 12, _resizeRect.Bottom - 4, _resizeRect.Right - 4, _resizeRect.Bottom - 12);
        g.DrawLine(pen, _resizeRect.Right - 8, _resizeRect.Bottom - 4, _resizeRect.Right - 4, _resizeRect.Bottom - 8);
    }

    private static void OpenLauncher(string path)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                return;
            }

            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch
        {
        }
    }

    private static void DrawGearIcon(Graphics g, Rectangle rect, Color color)
    {
        using var pen = new Pen(color, 2F);
        var center = new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        var outer = Math.Min(rect.Width, rect.Height) / 2 - 5;
        var inner = Math.Max(4, outer / 3);
        g.DrawEllipse(pen, center.X - inner, center.Y - inner, inner * 2, inner * 2);
        for (var i = 0; i < 8; i++)
        {
            var angle = Math.PI * 2 * i / 8;
            var x1 = center.X + (int)(Math.Cos(angle) * (outer - 4));
            var y1 = center.Y + (int)(Math.Sin(angle) * (outer - 4));
            var x2 = center.X + (int)(Math.Cos(angle) * outer);
            var y2 = center.Y + (int)(Math.Sin(angle) * outer);
            g.DrawLine(pen, x1, y1, x2, y2);
        }
    }

    private static Image? LoadLauncherWidgetImage(params string[] parts)
    {
        var roots = new[]
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory()
        };

        foreach (var root in roots)
        {
            var current = new DirectoryInfo(root);
            for (var depth = 0; current is not null && depth < 5; depth++, current = current.Parent)
            {
                var path = Path.Combine(new[] { current.FullName }.Concat(parts).ToArray());
                if (!File.Exists(path))
                {
                    continue;
                }

                using var stream = new MemoryStream(File.ReadAllBytes(path));
                using var image = Image.FromStream(stream);
                return new Bitmap(image);
            }
        }

        return null;
    }

    private static Image TintImage(Image source, Color color)
    {
        var bitmap = new Bitmap(source.Width, source.Height);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var attributes = new System.Drawing.Imaging.ImageAttributes();
        var matrix = new System.Drawing.Imaging.ColorMatrix(new[]
        {
            new[] { 0F, 0F, 0F, 0F, 0F },
            new[] { 0F, 0F, 0F, 0F, 0F },
            new[] { 0F, 0F, 0F, 0F, 0F },
            new[] { 0F, 0F, 0F, 1F, 0F },
            new[] { color.R / 255F, color.G / 255F, color.B / 255F, 0F, 1F }
        });
        attributes.SetColorMatrix(matrix);
        g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
        return bitmap;
    }

    private static string? GetDroppedPath(DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true
            && e.Data.GetData(DataFormats.FileDrop) is string[] files
            && files.Length > 0)
        {
            return files[0];
        }

        return e.Data?.GetDataPresent(DataFormats.Text) == true && e.Data.GetData(DataFormats.Text) is string path
            ? path
            : null;
    }

    private static void FillRound(Graphics g, Rectangle rect, Color color, int radius)
    {
        using var brush = new SolidBrush(color);
        using var path = RoundPath(rect, radius);
        g.FillPath(brush, path);
    }

    private static void DrawRound(Graphics g, Rectangle rect, Color color, int radius)
    {
        using var pen = new Pen(color);
        using var path = RoundPath(rect, radius);
        g.DrawPath(pen, path);
    }

    private static GraphicsPath RoundPath(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        rect.Width -= 1;
        rect.Height -= 1;
        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void DrawCentered(Graphics g, string text, Font font, Color color, Rectangle rect)
    {
        TextRenderer.DrawText(g, text, font, rect, color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

internal sealed class DesktopProjectWidgetForm : Form
{
    private const int WsExToolWindow = 0x00000080;
    private const int WsExAppWindow = 0x00040000;
    private const int WmContextMenu = 0x007B;

    private readonly DesktopProjectWidgetView _view;
    private readonly Action<Rectangle> _placementChanged;
    private readonly Action<bool> _transparentChanged;
    private bool _transparent;
    private bool _attachedToDesktop;
    private bool _restoringPlacement;

    public DesktopProjectWidgetForm(Func<IEnumerable<ProjectBoard>> projectProvider, Action<Rectangle> placementChanged, bool transparent, Action<bool> transparentChanged, Action projectsChanged)
    {
        _placementChanged = placementChanged;
        _transparent = transparent;
        _transparentChanged = transparentChanged;
        _view = new DesktopProjectWidgetView(projectProvider, _transparent, SetTransparent, projectsChanged)
        {
            Dock = DockStyle.Fill
        };

        Text = "项目管理";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        ShowIcon = false;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(560, 340);
        MinimumSize = new Size(380, 240);
        BackColor = Color.FromArgb(20, 28, 40);
        _view.BeginMoveRequested += () => NativeGlass.BeginMove(Handle);
        _view.BeginResizeRequested += () => NativeGlass.BeginResize(Handle, 17);
        _view.CloseRequested += Close;
        _view.ManageRequested += () => ManageRequested?.Invoke();
        _view.SplitRequested += project => SplitRequested?.Invoke(project);
        Controls.Add(_view);
    }

    public event Action? ManageRequested;
    public event Action<ProjectBoard>? SplitRequested;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WsExToolWindow;
            cp.ExStyle &= ~WsExAppWindow;
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmContextMenu)
        {
            m.Result = IntPtr.Zero;
            return;
        }

        base.WndProc(ref m);
    }

    public void ShowAsDesktopWidget(WidgetPlacement? placement = null)
    {
        ApplyPlacementOrDefault(placement);
        Show();
        BringToFront();
        AttachToDesktopHost();
        SavePlacement();
    }

    public void RefreshProjects()
    {
        _view.RefreshProjects();
    }

    protected override void OnMove(EventArgs e)
    {
        base.OnMove(e);
        SavePlacement();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateRoundedRegion();
        SavePlacement();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        AttachToDesktopHost();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyWidgetSkin();
    }

    private void SetTransparent(bool transparent)
    {
        _transparent = transparent;
        _transparentChanged(_transparent);
        ApplyWidgetSkin();
    }

    private void ApplyWidgetSkin()
    {
        BackColor = Color.FromArgb(20, 28, 40);
        _view.BackColor = BackColor;
        if (IsHandleCreated)
        {
            NativeGlass.EnableAcrylic(Handle, _transparent ? Color.FromArgb(34, 74, 138, 210) : Color.FromArgb(232, 18, 26, 38));
        }

        _view.Invalidate();
    }

    private void AttachToDesktopHost()
    {
        if (_attachedToDesktop || !IsHandleCreated)
        {
            return;
        }

        _attachedToDesktop = NativeGlass.AttachToDesktop(Handle);
    }

    private void ApplyPlacementOrDefault(WidgetPlacement? placement)
    {
        _restoringPlacement = true;
        try
        {
            if (placement is not null && placement.Width > 0 && placement.Height > 0)
            {
                Bounds = new Rectangle(placement.X, placement.Y, Math.Max(MinimumSize.Width, placement.Width), Math.Max(MinimumSize.Height, placement.Height));
                return;
            }

            var workArea = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
            Location = new Point(Math.Max(workArea.Left + 40, workArea.Right - Width - 80), workArea.Top + 520);
        }
        finally
        {
            _restoringPlacement = false;
        }
    }

    private void SavePlacement()
    {
        if (_restoringPlacement || !IsHandleCreated || WindowState == FormWindowState.Minimized)
        {
            return;
        }

        _placementChanged(Bounds);
    }

    private void UpdateRoundedRegion()
    {
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0)
        {
            return;
        }

        using var path = CreateRoundPath(new Rectangle(0, 0, ClientSize.Width, ClientSize.Height), 10);
        Region?.Dispose();
        Region = new Region(path);
    }

    private static GraphicsPath CreateRoundPath(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        rect.Width -= 1;
        rect.Height -= 1;
        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class DesktopProjectWidgetView : Control
{
    private const int WmContextMenu = 0x007B;

    private readonly Func<IEnumerable<ProjectBoard>> _projectProvider;
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _transparentMenuItem = new("透明");
    private readonly ToolStripMenuItem _splitMenuItem = new("拆分");
    private readonly ContextMenuStrip _projectMenu = new() { ShowImageMargin = false };
    private readonly ContextMenuStrip _phaseMenu = new() { ShowImageMargin = false };
    private readonly Action<bool> _transparentChanged;
    private readonly Action _projectsChanged;
    private readonly List<(Rectangle Rect, int Index)> _projectAreas = new();
    private readonly List<(Rectangle Rect, ProjectItem Item)> _phaseAreas = new();
    private Rectangle _settingsRect;
    private Rectangle _resizeRect;
    private ProjectBoard? _menuProject;
    private ProjectItem? _menuPhase;
    private Image? _settingsIcon;
    private Image? _projectIcon;
    private bool _transparent;
    private int _selectedIndex;

    private static readonly Color CardFill = Color.FromArgb(222, 24, 34, 48);
    private static readonly Color CardBorder = Color.FromArgb(98, 126, 154, 184);
    private static readonly Color PanelFill = Color.FromArgb(48, 58, 76);
    private Color CurrentPanelFill => _transparent ? Color.FromArgb(58, 48, 58, 76) : PanelFill;
    private Color CurrentTabFill(bool selected) => selected
        ? (_transparent ? Color.FromArgb(132, Blue.R, Blue.G, Blue.B) : Blue)
        : (_transparent ? Color.FromArgb(54, 52, 64, 84) : Color.FromArgb(52, 64, 84));
    private static readonly Color TextMain = Color.FromArgb(242, 246, 252);
    private static readonly Color TextMuted = Color.FromArgb(170, 188, 210);
    private static readonly Color Blue = Color.FromArgb(46, 126, 246);
    private static readonly Color Green = Color.FromArgb(58, 214, 122);
    private static readonly Font TitleFont = new("Microsoft YaHei UI", 12F, FontStyle.Regular);
    private static readonly Font NormalFont = new("Microsoft YaHei UI", 9.5F);
    private static readonly Font SmallFont = new("Microsoft YaHei UI", 8.5F);

    public DesktopProjectWidgetView(Func<IEnumerable<ProjectBoard>> projectProvider, bool transparent, Action<bool> transparentChanged, Action projectsChanged)
    {
        _projectProvider = projectProvider;
        _transparent = transparent;
        _transparentChanged = transparentChanged;
        _projectsChanged = projectsChanged;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        using var settingsIcon = LoadProjectWidgetImage("images", "zhuomianguinarongqi", "shezhi.png");
        _settingsIcon = settingsIcon is null ? null : TintImage(settingsIcon, Color.FromArgb(82, 168, 255));
        _projectIcon = LoadProjectWidgetImage("images", "Menu", "xiangmuguanli.png");
        _transparentMenuItem.Checked = _transparent;
        _transparentMenuItem.CheckOnClick = true;
        _transparentMenuItem.Click += (_, _) =>
        {
            _transparent = _transparentMenuItem.Checked;
            _transparentChanged(_transparent);
            Invalidate();
        };
        _splitMenuItem.Click += (_, _) =>
        {
            var project = CurrentProject();
            if (project is not null)
            {
                SplitRequested?.Invoke(project);
            }
        };
        _menu.Items.Add("管理", null, (_, _) => ManageRequested?.Invoke());
        _menu.Items.Add(_splitMenuItem);
        var skinMenu = new ToolStripMenuItem("皮肤设置");
        skinMenu.DropDownItems.Add(_transparentMenuItem);
        _menu.Items.Add(skinMenu);
        _menu.Items.Add("关闭", null, (_, _) => CloseRequested?.Invoke());
        _menu.Opening += (_, _) =>
        {
            _transparentMenuItem.Checked = _transparent;
            _splitMenuItem.Enabled = CurrentProject() is not null && Projects().Count > 1;
        };
        var projectPathItem = _projectMenu.Items.Add("设置路径");
        projectPathItem.Click += (_, _) =>
        {
            var project = _menuProject;
            if (project is null)
            {
                return;
            }

            if (Directory.Exists(project.ProjectPath))
            {
                OpenProjectPath(project.ProjectPath);
                return;
            }

            var path = ChooseProjectPath(project.ProjectPath);
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            project.ProjectPath = path;
            _projectsChanged();
            Invalidate();
        };
        _projectMenu.Opening += (_, e) =>
        {
            if (_menuProject is null)
            {
                e.Cancel = true;
                return;
            }

            projectPathItem.Text = Directory.Exists(_menuProject.ProjectPath) ? "打开项目路径" : "设置路径";
        };

        var phasePathItem = _phaseMenu.Items.Add("设置路径");
        phasePathItem.Click += (_, _) =>
        {
            var phase = _menuPhase;
            if (phase is null)
            {
                return;
            }

            if (Directory.Exists(phase.ProjectPath))
            {
                OpenProjectPath(phase.ProjectPath);
                return;
            }

            var path = ChooseProjectPath(phase.ProjectPath);
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            phase.ProjectPath = path;
            _projectsChanged();
            Invalidate();
        };
        _phaseMenu.Opening += (_, e) =>
        {
            if (_menuPhase is null)
            {
                e.Cancel = true;
                return;
            }

            phasePathItem.Text = Directory.Exists(_menuPhase.ProjectPath) ? "打开项目路径" : "设置路径";
        };
    }

    public event Action? BeginMoveRequested;
    public event Action? BeginResizeRequested;
    public event Action? CloseRequested;
    public event Action? ManageRequested;
    public event Action<ProjectBoard>? SplitRequested;

    public void ShowProjectMenu(Point location)
    {
        _menu.Show(this, location);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _settingsIcon?.Dispose();
            _projectIcon?.Dispose();
            _menu.Dispose();
            _projectMenu.Dispose();
            _phaseMenu.Dispose();
        }

        base.Dispose(disposing);
    }

    public void RefreshProjects()
    {
        var projects = Projects();
        if (_selectedIndex >= projects.Count)
        {
            _selectedIndex = Math.Max(0, projects.Count - 1);
        }

        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left && e.Button != MouseButtons.Right)
        {
            base.OnMouseDown(e);
            return;
        }

        if (_resizeRect.Contains(e.Location))
        {
            BeginResizeRequested?.Invoke();
            return;
        }

        if (_settingsRect.Contains(e.Location))
        {
            _menu.Show(this, _settingsRect.Left, _settingsRect.Bottom + 4);
            return;
        }

        var hit = _projectAreas.FirstOrDefault(item => item.Rect.Contains(e.Location));
        if (hit.Rect != Rectangle.Empty)
        {
            _selectedIndex = hit.Index;
            Invalidate();
            if (e.Button == MouseButtons.Right)
            {
                _menuProject = CurrentProject();
                _projectMenu.Show(this, e.Location);
            }
            return;
        }

        var phaseHit = _phaseAreas.FirstOrDefault(item => item.Rect.Contains(e.Location));
        if (phaseHit.Rect != Rectangle.Empty)
        {
            if (e.Button == MouseButtons.Right)
            {
                _menuPhase = phaseHit.Item;
                _phaseMenu.Show(this, e.Location);
            }
            return;
        }

        if (e.Button == MouseButtons.Right)
        {
            return;
        }

        if (e.Y <= 52)
        {
            BeginMoveRequested?.Invoke();
            return;
        }

        base.OnMouseDown(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmContextMenu)
        {
            m.Result = IntPtr.Zero;
            return;
        }

        base.WndProc(ref m);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        Cursor = _resizeRect.Contains(e.Location) ? Cursors.SizeNWSE
            : _settingsRect.Contains(e.Location) || _projectAreas.Any(item => item.Rect.Contains(e.Location)) || _phaseAreas.Any(item => item.Rect.Contains(e.Location)) ? Cursors.Hand
            : Cursors.Default;
        base.OnMouseMove(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        _projectAreas.Clear();
        _phaseAreas.Clear();
        var projects = Projects();

        var card = new Rectangle(0, 0, Width, Height);
        FillRound(g, card, _transparent ? Color.FromArgb(70, 24, 34, 48) : CardFill, 10);
        DrawRound(g, new Rectangle(0, 0, Width - 1, Height - 1), CardBorder, 10);

        var headerIcon = new Rectangle(18, 14, 20, 20);
        if (_projectIcon is not null)
        {
            g.DrawImage(_projectIcon, headerIcon);
        }
        else
        {
            TextRenderer.DrawText(g, "□", TitleFont, headerIcon, TextMain, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        TextRenderer.DrawText(g, "项目管理", TitleFont, new Rectangle(46, 12, 110, 28), TextMain, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        _settingsRect = new Rectangle(Width - 52, 8, 40, 40);
        if (_settingsIcon is not null)
        {
            g.DrawImage(_settingsIcon, new Rectangle(_settingsRect.X + 4, _settingsRect.Y + 4, 32, 32));
        }
        else
        {
            DrawGearIcon(g, new Rectangle(_settingsRect.X + 4, _settingsRect.Y + 4, 32, 32), Color.FromArgb(82, 168, 255));
        }

        if (projects.Count == 0)
        {
            DrawCentered(g, "暂无项目", NormalFont, TextMuted, new Rectangle(12, 56, Width - 24, Height - 74));
            DrawResizeGrip(g);
            return;
        }

        _selectedIndex = Math.Clamp(_selectedIndex, 0, projects.Count - 1);
        var project = projects[_selectedIndex];
        DrawProjectTabs(g, new Rectangle(164, 12, Math.Max(80, Width - 222), 28), projects, project);
        DrawProjectRows(g, new Rectangle(14, 54, Width - 28, Height - 72), project);
        DrawResizeGrip(g);
    }

    private void DrawProjectTabs(Graphics g, Rectangle rect, List<ProjectBoard> projects, ProjectBoard selectedProject)
    {
        var x = rect.X;
        for (var i = 0; i < projects.Count; i++)
        {
            var project = projects[i];
            var width = Math.Clamp(TextRenderer.MeasureText(project.Name, SmallFont).Width + 34, 116, 180);
            if (x >= rect.Right)
            {
                break;
            }

            var tab = new Rectangle(x, rect.Y, Math.Min(width, rect.Right - x), rect.Height);
            FillRound(g, tab, CurrentTabFill(project == selectedProject), 6);
            TextRenderer.DrawText(g, project.Name, SmallFont, new Rectangle(tab.X + 10, tab.Y, tab.Width - 20, tab.Height), project == selectedProject ? Color.White : TextMain, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            _projectAreas.Add((tab, i));
            x += width + 8;
        }
    }

    private void DrawProjectRows(Graphics g, Rectangle rect, ProjectBoard project)
    {
        FillRound(g, rect, CurrentPanelFill, 6);
        var items = project.Items.Take(Math.Max(1, rect.Height / 42)).ToArray();
        if (items.Length == 0)
        {
            DrawCentered(g, "暂无项目阶段", NormalFont, TextMuted, rect);
            return;
        }

        var y = rect.Y + 10;
        var dateWidth = Math.Max(
            TextRenderer.MeasureText("开始 2026/06/17  截止 2026/06/17", SmallFont).Width + 10,
            items.Max(item => TextRenderer.MeasureText(ProjectDateText(item), SmallFont).Width + 10));
        const int minTrackWidth = 160;
        const int percentWidth = 46;
        const int columnGap = 12;
        foreach (var item in items)
        {
            var percent = ProjectProgressPercent(item);
            var row = new Rectangle(rect.X + 12, y, rect.Width - 24, 36);
            var titleWidth = TextRenderer.MeasureText(item.Title, NormalFont).Width + 10;
            var maxTitleWidth = Math.Max(90, row.Width - dateWidth - minTrackWidth - percentWidth - columnGap * 4);
            var phaseNameRect = new Rectangle(row.X, row.Y, Math.Clamp(titleWidth, 90, maxTitleWidth), 22);
            TextRenderer.DrawText(g, item.Title, NormalFont, phaseNameRect, TextMain, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            _phaseAreas.Add((row, item));
            var dateRect = new Rectangle(phaseNameRect.Right + columnGap, row.Y, dateWidth, 22);
            TextRenderer.DrawText(g, ProjectDateText(item), SmallFont, dateRect, TextMuted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            var percentRect = new Rectangle(row.Right - percentWidth, row.Y, percentWidth, 22);
            var trackX = dateRect.Right + columnGap;
            var track = new Rectangle(trackX, row.Y + 8, Math.Max(60, percentRect.Left - trackX - columnGap), 8);
            FillRound(g, track, Color.FromArgb(62, 74, 96), 4);
            if (percent > 0)
            {
                FillRound(g, new Rectangle(track.X, track.Y, Math.Max(8, track.Width * percent / 100), track.Height), Green, 4);
            }

            DrawProgressTicks(g, track, item.SubItems.Count);

            TextRenderer.DrawText(g, $"{percent}%", SmallFont, percentRect, TextMuted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            y += 42;
        }
    }

    private void DrawResizeGrip(Graphics g)
    {
        _resizeRect = new Rectangle(Width - 24, Height - 24, 18, 18);
        using var pen = new Pen(Color.FromArgb(120, 158, 178, 204), 1F);
        g.DrawLine(pen, _resizeRect.Right - 12, _resizeRect.Bottom - 4, _resizeRect.Right - 4, _resizeRect.Bottom - 12);
        g.DrawLine(pen, _resizeRect.Right - 8, _resizeRect.Bottom - 4, _resizeRect.Right - 4, _resizeRect.Bottom - 8);
    }

    private List<ProjectBoard> Projects()
    {
        return _projectProvider()
            .Where(project => !string.IsNullOrWhiteSpace(project.Id))
            .ToList();
    }

    private ProjectBoard? CurrentProject()
    {
        var projects = Projects();
        return projects.Count == 0 ? null : projects[Math.Clamp(_selectedIndex, 0, projects.Count - 1)];
    }

    private static int ProjectProgressPercent(ProjectItem item)
    {
        if (item.SubItems.Count > 0)
        {
            var completed = item.SubItems.Count(ProjectSubItemCompleted);
            return (int)Math.Round(completed * 100D / item.SubItems.Count, MidpointRounding.AwayFromZero);
        }

        if (item.ProgressPercent >= 0)
        {
            return Math.Clamp(item.ProgressPercent, 0, 100);
        }

        return item.Status switch
        {
            ProjectStatus.Done => 100,
            ProjectStatus.Doing => 50,
            _ => 0
        };
    }

    private static string ProjectDateText(ProjectItem item)
    {
        return $"开始 {item.StartDate?.ToString("yyyy/MM/dd") ?? "----/--/--"}  截止 {item.EndDate?.ToString("yyyy/MM/dd") ?? "----/--/--"}";
    }

    private static void DrawProgressTicks(Graphics g, Rectangle track, int parts)
    {
        if (parts <= 1)
        {
            return;
        }

        using var pen = new Pen(Color.FromArgb(140, 176, 198, 220), 1F);
        for (var i = 1; i < parts; i++)
        {
            var x = track.X + track.Width * i / parts;
            g.DrawLine(pen, x, track.Y - 2, x, track.Bottom + 2);
        }
    }

    private static bool ProjectSubItemCompleted(ProjectSubItem item)
    {
        return item.Done;
    }

    private static void OpenProjectPath(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
        }
        catch
        {
        }
    }

    private static string? ChooseProjectPath(string currentPath)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择项目文件夹",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(currentPath) ? currentPath : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
        };

        return dialog.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath)
            ? dialog.SelectedPath
            : null;
    }

    private static void FillRound(Graphics g, Rectangle rect, Color color, int radius)
    {
        using var brush = new SolidBrush(color);
        using var path = RoundPath(rect, radius);
        g.FillPath(brush, path);
    }

    private static void DrawRound(Graphics g, Rectangle rect, Color color, int radius)
    {
        using var pen = new Pen(color);
        using var path = RoundPath(rect, radius);
        g.DrawPath(pen, path);
    }

    private static GraphicsPath RoundPath(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        rect.Width -= 1;
        rect.Height -= 1;
        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void DrawText(Graphics g, string text, Font font, Color color, int x, int y)
    {
        TextRenderer.DrawText(g, text, font, new Point(x, y), color, TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping);
    }

    private static void DrawCentered(Graphics g, string text, Font font, Color color, Rectangle rect)
    {
        TextRenderer.DrawText(g, text, font, rect, color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private static Image TintImage(Image source, Color color)
    {
        var bitmap = new Bitmap(source.Width, source.Height);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var attributes = new System.Drawing.Imaging.ImageAttributes();
        var matrix = new System.Drawing.Imaging.ColorMatrix(new[]
        {
            new[] { 0F, 0F, 0F, 0F, 0F },
            new[] { 0F, 0F, 0F, 0F, 0F },
            new[] { 0F, 0F, 0F, 0F, 0F },
            new[] { 0F, 0F, 0F, 1F, 0F },
            new[] { color.R / 255F, color.G / 255F, color.B / 255F, 0F, 1F }
        });
        attributes.SetColorMatrix(matrix);
        g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
        return bitmap;
    }

    private static void DrawGearIcon(Graphics g, Rectangle rect, Color color)
    {
        using var pen = new Pen(color, 1.8F);
        var center = new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        g.DrawEllipse(pen, center.X - 8, center.Y - 8, 16, 16);
        g.DrawEllipse(pen, center.X - 3, center.Y - 3, 6, 6);
        for (var i = 0; i < 8; i++)
        {
            var angle = Math.PI * 2 * i / 8;
            var x1 = center.X + (int)(Math.Cos(angle) * 10);
            var y1 = center.Y + (int)(Math.Sin(angle) * 10);
            var x2 = center.X + (int)(Math.Cos(angle) * 13);
            var y2 = center.Y + (int)(Math.Sin(angle) * 13);
            g.DrawLine(pen, x1, y1, x2, y2);
        }
    }

    private static Image? LoadProjectWidgetImage(params string[] parts)
    {
        var roots = new[]
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory()
        };

        foreach (var root in roots)
        {
            var current = new DirectoryInfo(root);
            for (var depth = 0; current is not null && depth < 5; depth++, current = current.Parent)
            {
                var path = Path.Combine(new[] { current.FullName }.Concat(parts).ToArray());
                if (!File.Exists(path))
                {
                    continue;
                }

                using var stream = new MemoryStream(File.ReadAllBytes(path));
                return new Bitmap(Image.FromStream(stream));
            }
        }

        return null;
    }
}

internal sealed class DesktopTodoWidgetView : Control
{
    private static readonly Color[] TagPalette =
    {
        Color.FromArgb(26, 135, 84),
        Color.FromArgb(13, 110, 253),
        Color.FromArgb(220, 53, 69),
        Color.FromArgb(255, 193, 7),
        Color.FromArgb(111, 66, 193),
        Color.FromArgb(32, 201, 151),
        Color.FromArgb(253, 126, 20),
        Color.FromArgb(108, 117, 125)
    };

    private readonly TodoData _todos;
    private readonly Action _addRequested;
    private readonly Action _todosChanged;
    private readonly Action _manageRequested;
    private readonly Action<bool> _transparentChanged;
    private readonly ToolTip _tip = new()
    {
        AutomaticDelay = 250,
        ReshowDelay = 100,
        AutoPopDelay = 8000
    };
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _transparentMenuItem = new("透明");
    private readonly System.Windows.Forms.Timer _clockTimer = new() { Interval = 30000 };
    private readonly List<(Rectangle Rect, TodoItem Item)> _checkAreas = new();
    private readonly List<(Rectangle Rect, TodoItem Item)> _rowAreas = new();
    private readonly Image? _settingsIcon;
    private bool _transparent;
    private bool _showCompleted;
    private Rectangle _headerRect;
    private Rectangle _settingsRect;
    private Rectangle _completedRect;
    private Rectangle _resizeRect;
    private string? _hoverKey;

    private static readonly Color CardFill = Color.FromArgb(232, 18, 26, 38);
    private static readonly Color CardBorder = Color.FromArgb(96, 104, 130, 156);
    private static readonly Color ContentFill = Color.FromArgb(190, 8, 14, 24);
    private static readonly Color TransparentContentFill = Color.FromArgb(92, 6, 12, 22);
    private static readonly Color TextMain = Color.FromArgb(242, 246, 252);
    private static readonly Color TextSubtle = Color.FromArgb(166, 184, 207);
    private static readonly Font TitleFont = new("Microsoft YaHei UI", 13F, FontStyle.Regular);
    private static readonly Font NormalFont = new("Microsoft YaHei UI", 9.5F);
    private static readonly Font SmallFont = new("Microsoft YaHei UI", 8.5F);

    public DesktopTodoWidgetView(TodoData todos, Action addRequested, Action todosChanged, Action manageRequested, bool transparent, Action<bool> transparentChanged)
    {
        _todos = todos;
        _addRequested = addRequested;
        _todosChanged = todosChanged;
        _manageRequested = manageRequested;
        _transparentChanged = transparentChanged;
        _transparent = transparent;
        _menu = new ContextMenuStrip();
        _transparentMenuItem.CheckOnClick = true;
        _transparentMenuItem.Checked = transparent;
        _transparentMenuItem.Click += (_, _) =>
        {
            _transparent = _transparentMenuItem.Checked;
            _transparentChanged(_transparent);
            Invalidate();
        };
        _menu.Items.Add("添加", null, (_, _) => _addRequested());
        _menu.Items.Add("管理", null, (_, _) => _manageRequested());
        var skinMenu = new ToolStripMenuItem("皮肤设置");
        skinMenu.DropDownItems.Add(_transparentMenuItem);
        _menu.Items.Add(skinMenu);
        _menu.Items.Add("关闭", null, (_, _) => CloseRequested?.Invoke());
        using var settingsIcon = LoadTodoWidgetImage("images", "zhuomianguinarongqi", "shezhi.png");
        _settingsIcon = settingsIcon is null ? null : TintImage(settingsIcon, Color.FromArgb(130, 180, 255));
        _clockTimer.Tick += (_, _) => Invalidate();
        _clockTimer.Start();
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.FromArgb(20, 28, 40);
    }

    public event Action? BeginMoveRequested;
    public event Action? BeginResizeRequested;
    public event Action? CloseRequested;

    public void RefreshTodos()
    {
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tip.Dispose();
            _menu.Dispose();
            _clockTimer.Dispose();
            _settingsIcon?.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            var rowHit = _rowAreas.FirstOrDefault(item => item.Rect.Contains(e.Location));
            if (rowHit.Item is not null)
            {
                ShowTodoDetails(rowHit.Item);
            }

            return;
        }

        if (_settingsRect.Contains(e.Location))
        {
            _menu.Show(this, _settingsRect.Left, _settingsRect.Bottom + 4);
            return;
        }

        if (_resizeRect.Contains(e.Location))
        {
            BeginResizeRequested?.Invoke();
            return;
        }

        if (_completedRect.Contains(e.Location))
        {
            _showCompleted = !_showCompleted;
            Invalidate();
            return;
        }

        if (e.Button == MouseButtons.Left)
        {
            var checkHit = _checkAreas.FirstOrDefault(item => item.Rect.Contains(e.Location));
            if (checkHit.Item is not null)
            {
                checkHit.Item.Done = !checkHit.Item.Done;
                _todosChanged();
                Invalidate();
                return;
            }
        }

        if (_headerRect.Contains(e.Location))
        {
            BeginMoveRequested?.Invoke();
            return;
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        UpdateToolTip(e.Location);
        Cursor = _resizeRect.Contains(e.Location)
            ? Cursors.SizeNWSE
            : _settingsRect.Contains(e.Location) || _completedRect.Contains(e.Location) || _checkAreas.Any(item => item.Rect.Contains(e.Location)) || _rowAreas.Any(item => item.Rect.Contains(e.Location))
                ? Cursors.Hand
                : Cursors.Default;
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hoverKey = null;
        _tip.SetToolTip(this, string.Empty);
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        _checkAreas.Clear();
        _rowAreas.Clear();

        var card = new Rectangle(0, 0, Width - 1, Height - 1);
        if (card.Width < 220 || card.Height < 160)
        {
            return;
        }

        FillRound(g, card, _transparent ? Color.FromArgb(34, CardFill.R, CardFill.G, CardFill.B) : CardFill, 10);
        DrawRound(g, _transparent ? Color.FromArgb(130, 92, 220, 210) : CardBorder, card, 10);
        DrawHeader(g, card);
        DrawTodos(g, card);
        DrawFooter(g, card);
        DrawResizeGrip(g, card);
    }

    private void DrawHeader(Graphics g, Rectangle card)
    {
        _settingsRect = new Rectangle(card.Right - 46, card.Y + 13, 28, 28);
        _headerRect = new Rectangle(card.X + 16, card.Y + 12, _settingsRect.Left - card.X - 24, 34);
        var title = "▣  今日工作记录";
        var titleWidth = Math.Min(TextRenderer.MeasureText(title, TitleFont).Width, Math.Max(120, _headerRect.Width / 2));
        var titleRect = new Rectangle(_headerRect.X, _headerRect.Y, titleWidth, _headerRect.Height);
        TextRenderer.DrawText(g, title, TitleFont, titleRect, TextMain, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping);

        var dateX = titleRect.Right + 12;
        var dateRect = new Rectangle(dateX, _headerRect.Y + 2, Math.Max(1, _headerRect.Right - dateX), _headerRect.Height - 2);
        var now = DateTime.Now;
        var dateText = $"{now:yyyy-MM-dd HH:mm}  {WeekText(now.DayOfWeek)}  {LunarText(now)}";
        TextRenderer.DrawText(g, dateText, SmallFont, dateRect, TextSubtle, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping);

        if (_settingsIcon is not null)
        {
            g.DrawImage(_settingsIcon, _settingsRect);
        }
        else
        {
            DrawGearIcon(g, _settingsRect, Color.FromArgb(130, 180, 255));
        }

        using var pen = new Pen(_transparent ? Color.FromArgb(145, 220, 245, 255) : Color.FromArgb(72, 104, 130, 156));
        g.DrawLine(pen, card.X + 14, card.Y + 54, card.Right - 14, card.Y + 54);
    }

    private void DrawTodos(Graphics g, Rectangle card)
    {
        var panel = new Rectangle(card.X + 14, card.Y + 66, card.Width - 28, Math.Max(1, card.Height - 112));
        FillRound(g, panel, _transparent ? TransparentContentFill : ContentFill, 7);
        var list = new Rectangle(panel.X + 10, panel.Y + 8, panel.Width - 20, Math.Max(1, panel.Height - 16));
        var rowCount = Math.Max(1, list.Height / 34);
        var items = (_showCompleted ? _todos.Items : _todos.Items.Where(item => !item.Done)).Take(rowCount).ToArray();
        if (items.Length == 0)
        {
            DrawCentered(g, "暂无待办任务", NormalFont, TextSubtle, list);
            return;
        }

        for (var i = 0; i < items.Length; i++)
        {
            var y = list.Y + i * 34;
            var item = items[i];
            var row = new Rectangle(list.X, y, list.Width, 30);
            _rowAreas.Add((row, item));
            _checkAreas.Add((new Rectangle(row.X - 4, row.Y, 28, row.Height), item));

            var box = new Rectangle(row.X, row.Y + 7, 15, 15);
            DrawRound(g, box, TextSubtle, 3);
            if (item.Done)
            {
                using var checkPen = new Pen(Color.FromArgb(126, 242, 210), 1.8F);
                g.DrawLine(checkPen, box.X + 3, box.Y + 8, box.X + 6, box.Y + 11);
                g.DrawLine(checkPen, box.X + 6, box.Y + 11, box.Right - 3, box.Y + 4);
            }
            var tagText = string.IsNullOrWhiteSpace(item.Tag) ? "未分类" : item.Tag.Trim();
            var badgeWidth = Math.Max(44, Math.Min(86, 18 + tagText.Length * 14));
            var timeWidth = 44;
            var textRight = row.Right - badgeWidth - timeWidth - 18;
            var textRect = new Rectangle(row.X + 26, row.Y + 4, Math.Max(40, textRight - row.X - 26), 22);
            TextRenderer.DrawText(g, item.Text, NormalFont, textRect, item.Done ? TextSubtle : TextMain, TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            DrawText(g, item.CreatedAt.ToString("HH:mm"), SmallFont, TextSubtle, row.Right - badgeWidth - timeWidth - 8, row.Y + 8);
            DrawBadge(g, tagText, new Rectangle(row.Right - badgeWidth, row.Y + 3, badgeWidth, 23), GetTagColor(item.Tag));
        }
    }

    private void DrawFooter(Graphics g, Rectangle card)
    {
        _completedRect = new Rectangle(card.X + 12, card.Bottom - 42, 148, 34);
        DrawText(g, $"已完成（{_todos.Items.Count(item => item.Done)}）", NormalFont, TextSubtle, card.X + 16, card.Bottom - 34);
    }

    private void DrawResizeGrip(Graphics g, Rectangle card)
    {
        _resizeRect = new Rectangle(card.Right - 22, card.Bottom - 22, 18, 18);
        using var pen = new Pen(Color.FromArgb(140, 166, 184, 206), 1.3F);
        for (var i = 0; i < 3; i++)
        {
            var offset = i * 5;
            g.DrawLine(pen, _resizeRect.Right - 2 - offset, _resizeRect.Bottom - 1, _resizeRect.Right - 1, _resizeRect.Bottom - 2 - offset);
        }
    }

    private Color GetTagColor(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return Color.FromArgb(70, 82, 100);
        }

        var preset = _todos.TagPresets.FirstOrDefault(item => string.Equals(item.Name, tag.Trim(), StringComparison.OrdinalIgnoreCase));
        if (preset is not null)
        {
            return Color.FromArgb(preset.ColorArgb);
        }

        return TagPalette[Math.Abs(tag.Trim().GetHashCode()) % TagPalette.Length];
    }

    private void UpdateToolTip(Point location)
    {
        var rowHit = _rowAreas.FirstOrDefault(item => item.Rect.Contains(location));
        var key = rowHit.Item is null ? null : $"{rowHit.Item.Text}|{rowHit.Item.CreatedAt.Ticks}";
        if (string.Equals(_hoverKey, key, StringComparison.Ordinal))
        {
            return;
        }

        _hoverKey = key;
        _tip.SetToolTip(this, string.IsNullOrWhiteSpace(rowHit.Item?.Note) ? string.Empty : rowHit.Item.Note);
    }

    private void ShowTodoDetails(TodoItem item)
    {
        var note = string.IsNullOrWhiteSpace(item.Note) ? "无" : item.Note.Trim();
        MessageBox.Show(FindForm(), $"任务名称：{item.Text}\n标签：{(string.IsNullOrWhiteSpace(item.Tag) ? "未分类" : item.Tag.Trim())}\n创建时间：{item.CreatedAt:yyyy-MM-dd HH:mm}\n\n备注：\n{note}", "任务详情", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static string WeekText(DayOfWeek day)
    {
        return day switch
        {
            DayOfWeek.Monday => "星期一",
            DayOfWeek.Tuesday => "星期二",
            DayOfWeek.Wednesday => "星期三",
            DayOfWeek.Thursday => "星期四",
            DayOfWeek.Friday => "星期五",
            DayOfWeek.Saturday => "星期六",
            _ => "星期日"
        };
    }

    private static string LunarText(DateTime date)
    {
        var calendar = new ChineseLunisolarCalendar();
        var year = calendar.GetYear(date);
        var month = calendar.GetMonth(date);
        var day = calendar.GetDayOfMonth(date);
        var leapMonth = calendar.GetLeapMonth(year);
        var isLeap = leapMonth > 0 && month == leapMonth;
        if (leapMonth > 0 && month >= leapMonth)
        {
            month--;
        }

        return $"农历{(isLeap ? "闰" : "")}{LunarMonthName(month)}{LunarDayName(day)}";
    }

    private static string LunarMonthName(int month)
    {
        var names = new[] { "正月", "二月", "三月", "四月", "五月", "六月", "七月", "八月", "九月", "十月", "冬月", "腊月" };
        return month >= 1 && month <= names.Length ? names[month - 1] : $"{month}月";
    }

    private static string LunarDayName(int day)
    {
        var prefixes = new[] { "初", "十", "廿", "三" };
        var digits = new[] { "", "一", "二", "三", "四", "五", "六", "七", "八", "九", "十" };
        return day switch
        {
            10 => "初十",
            20 => "二十",
            30 => "三十",
            _ => $"{prefixes[(day - 1) / 10]}{digits[day % 10]}"
        };
    }

    private static void DrawBadge(Graphics g, string text, Rectangle rect, Color color)
    {
        FillRound(g, rect, Color.FromArgb(78, color), 5);
        DrawRound(g, color, rect, 5);
        DrawCentered(g, text, SmallFont, Color.White, rect);
    }

    private static void DrawGearIcon(Graphics g, Rectangle rect, Color color)
    {
        using var pen = new Pen(color, 2F);
        var center = new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        g.DrawEllipse(pen, center.X - 8, center.Y - 8, 16, 16);
        g.DrawEllipse(pen, center.X - 3, center.Y - 3, 6, 6);
        for (var i = 0; i < 8; i++)
        {
            var angle = Math.PI * 2 * i / 8;
            var x1 = center.X + (int)(Math.Cos(angle) * 10);
            var y1 = center.Y + (int)(Math.Sin(angle) * 10);
            var x2 = center.X + (int)(Math.Cos(angle) * 13);
            var y2 = center.Y + (int)(Math.Sin(angle) * 13);
            g.DrawLine(pen, x1, y1, x2, y2);
        }
    }

    private static Image? LoadTodoWidgetImage(params string[] parts)
    {
        var relativePath = Path.Combine(parts);
        var roots = new[]
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory()
        };

        foreach (var root in roots)
        {
            var current = new DirectoryInfo(root);
            for (var depth = 0; current is not null && depth < 5; depth++, current = current.Parent)
            {
                var path = Path.Combine(current.FullName, relativePath);
                if (!File.Exists(path))
                {
                    continue;
                }

                using var stream = new MemoryStream(File.ReadAllBytes(path));
                using var image = Image.FromStream(stream);
                return new Bitmap(image);
            }
        }

        return null;
    }

    private static Image TintImage(Image source, Color color)
    {
        using var sourceBitmap = new Bitmap(source);
        var tinted = new Bitmap(sourceBitmap.Width, sourceBitmap.Height);
        for (var y = 0; y < sourceBitmap.Height; y++)
        {
            for (var x = 0; x < sourceBitmap.Width; x++)
            {
                var pixel = sourceBitmap.GetPixel(x, y);
                tinted.SetPixel(x, y, Color.FromArgb(pixel.A * color.A / 255, color.R, color.G, color.B));
            }
        }

        return tinted;
    }

    private static void FillRound(Graphics g, Rectangle rect, Color color, int radius)
    {
        using var brush = new SolidBrush(color);
        using var path = RoundPath(rect, radius);
        g.FillPath(brush, path);
    }

    private static void DrawRound(Graphics g, Rectangle rect, Color color, int radius)
    {
        using var pen = new Pen(color);
        using var path = RoundPath(rect, radius);
        g.DrawPath(pen, path);
    }

    private static void DrawRound(Graphics g, Color color, Rectangle rect, int radius)
    {
        DrawRound(g, rect, color, radius);
    }

    private static GraphicsPath RoundPath(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        rect.Width -= 1;
        rect.Height -= 1;
        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void DrawText(Graphics g, string text, Font font, Color color, int x, int y)
    {
        TextRenderer.DrawText(g, text, font, new Point(x, y), color, TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping);
    }

    private static void DrawCentered(Graphics g, string text, Font font, Color color, Rectangle rect)
    {
        TextRenderer.DrawText(g, text, font, rect, color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping);
    }
}

internal sealed class DesktopNoteWidgetForm : Form
{
    private readonly NoteItem _note;
    private readonly DesktopNoteWidgetView _view;
    private readonly Action<NoteItem, Rectangle> _placementChanged;
    private bool _attachedToDesktop;
    private bool _restoringPlacement;

    public DesktopNoteWidgetForm(NoteItem note, Action noteChanged, Action manageRequested, Action<NoteItem, Rectangle> placementChanged)
    {
        _note = note;
        _placementChanged = placementChanged;
        _view = new DesktopNoteWidgetView(_note, noteChanged, manageRequested)
        {
            Dock = DockStyle.Fill
        };
        Text = _note.Title;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(420, 300);
        BackColor = Color.FromArgb(20, 28, 40);
        _view.BeginMoveRequested += () => NativeGlass.BeginMove(Handle);
        _view.BeginResizeRequested += () => NativeGlass.BeginResize(Handle, 17);
        _view.CloseRequested += Close;
        Controls.Add(_view);
        MinimumSize = new Size(140, 100);
        if (_note.ImageOnly && !_view.NaturalImageSize.IsEmpty)
        {
            Size = _view.NaturalImageSize;
        }
    }

    public bool Displays(NoteItem item) => ReferenceEquals(item, _note);

    public void FocusWidget()
    {
        BringToFront();
        Invalidate(true);
    }

    public void ShowAsDesktopWidget(WidgetPlacement? placement = null)
    {
        ApplyPlacementOrDefault(placement);
        Show();
        BringToFront();
        AttachToDesktopHost();
        BringToFront();
        Invalidate(true);
        SavePlacement();
    }

    public void RefreshNote(NoteItem item)
    {
        if (!ReferenceEquals(item, _note))
        {
            return;
        }

        Text = _note.Title;
        _view.RefreshNote();
        if (_note.ImageOnly && !_view.NaturalImageSize.IsEmpty)
        {
            Size = _view.NaturalImageSize;
        }

        Invalidate(true);
    }

    protected override void OnMove(EventArgs e)
    {
        base.OnMove(e);
        SavePlacement();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        SavePlacement();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        AttachToDesktopHost();
    }

    private void AttachToDesktopHost()
    {
        if (_attachedToDesktop || !IsHandleCreated)
        {
            return;
        }

        _attachedToDesktop = NativeGlass.AttachToDesktop(Handle);
    }

    private void ApplyPlacementOrDefault(WidgetPlacement? placement)
    {
        _restoringPlacement = true;
        try
        {
            if (placement is not null && placement.Width > 0 && placement.Height > 0)
            {
                Bounds = new Rectangle(placement.X, placement.Y, Math.Max(MinimumSize.Width, placement.Width), Math.Max(MinimumSize.Height, placement.Height));
                return;
            }

            var workArea = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
            Location = new Point(Math.Max(workArea.Left + 40, workArea.Right - Width - 80), workArea.Top + 140);
        }
        finally
        {
            _restoringPlacement = false;
        }
    }

    private void SavePlacement()
    {
        if (_restoringPlacement || !IsHandleCreated || WindowState == FormWindowState.Minimized)
        {
            return;
        }

        _placementChanged(_note, RectangleToScreen(ClientRectangle));
    }
}

internal sealed class DesktopNoteWidgetView : Control
{
    private readonly NoteItem _note;
    private readonly Action _noteChanged;
    private readonly Action _manageRequested;
    private readonly ContextMenuStrip _menu;
    private readonly Color _menuButtonColor;
    private DesktopNoteEditorForm? _editor;
    private Image? _background;
    private Rectangle _closeRect;

    public DesktopNoteWidgetView(NoteItem note, Action noteChanged, Action manageRequested)
    {
        _note = note;
        _noteChanged = noteChanged;
        _manageRequested = manageRequested;
        _menuButtonColor = RandomWidgetButtonColor();
        _menu = new ContextMenuStrip
        {
            ShowImageMargin = false,
            BackColor = Color.FromArgb(32, 42, 58),
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold)
        };
        _menu.Items.Add("管理", null, (_, _) => _manageRequested());
        _menu.Items.Add("关闭", null, (_, _) => CloseRequested?.Invoke());
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.FromArgb(20, 28, 40);
        LoadBackground();
    }

    public Size NaturalImageSize => _background?.Size ?? Size.Empty;

    public void RefreshNote()
    {
        LoadBackground();
        Invalidate();
    }

    public event Action? BeginMoveRequested;
    public event Action? BeginResizeRequested;
    public event Action? CloseRequested;

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && _closeRect.Contains(e.Location))
        {
            _menu.Show(this, new Point(_closeRect.Left - 26, _closeRect.Bottom + 4));
            return;
        }

        if (e.Button == MouseButtons.Left && GetResizeRect().Contains(e.Location))
        {
            BeginResizeRequested?.Invoke();
            return;
        }

        if (e.Button == MouseButtons.Left && !_note.ImageOnly && GetTextRect().Contains(e.Location))
        {
            return;
        }

        if (e.Button == MouseButtons.Left)
        {
            BeginMoveRequested?.Invoke();
        }
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button == MouseButtons.Left && !_note.ImageOnly && GetTextRect().Contains(e.Location))
        {
            BeginEdit(e.Location);
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        MoveEditor();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        var rect = new Rectangle(0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        var noteColor = Color.FromArgb(_note.ColorArgb);
        if (noteColor.A > 0)
        {
            using var fill = new SolidBrush(noteColor);
            g.FillRectangle(fill, rect);
        }
        else
        {
            using var fill = new SolidBrush(Color.FromArgb(42, 20, 28, 40));
            g.FillRectangle(fill, rect);
            using var border = new Pen(Color.FromArgb(128, 150, 180, 220), 1.2F);
            g.DrawRectangle(border, rect);
        }

        if (_background is not null)
        {
            if (_note.ImageOnly)
            {
                DrawImageActualSize(g, _background, rect);
            }
            else
            {
                DrawImageFit(g, _background, rect);
            }
        }

        if (!_note.ImageOnly)
        {
            var textRect = GetTextRect();
            using var font = new Font("Microsoft YaHei UI", Math.Clamp(_note.FontSize, 8F, 42F), _note.FontBold ? FontStyle.Bold : FontStyle.Regular);
            DrawWrappedText(g, _note.Text, font, textRect, NoteStyle.TextColor(_note));
        }

        if (!_note.ImageOnly)
        {
            using var header = new SolidBrush(Color.FromArgb(112, 20, 28, 40));
            g.FillRectangle(header, 0, 0, Width, 36);
            using var titleFont = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            DrawSingleLineText(g, _note.Title, titleFont, new Rectangle(12, 7, Width - 52, 22), NoteStyle.TextColor(_note));
        }

        _closeRect = new Rectangle(Width - 34, 4, 26, 26);
        DrawMenuButton(g, _closeRect);

        DrawResizeGrip(g);
    }

    private void DrawMenuButton(Graphics g, Rectangle rect)
    {
        using var shadow = new SolidBrush(Color.FromArgb(80, 0, 0, 0));
        g.FillEllipse(shadow, rect.X + 3, rect.Y + 4, rect.Width, rect.Height);
        using var fill = new SolidBrush(_menuButtonColor);
        g.FillEllipse(fill, rect);
        using var highlight = new Pen(Color.FromArgb(150, 255, 255, 255), 1.2F);
        g.DrawArc(highlight, rect.X + 4, rect.Y + 4, rect.Width - 8, rect.Height - 8, 210, 110);
    }

    private static Color RandomWidgetButtonColor()
    {
        var colors = new[]
        {
            Color.FromArgb(232, 92, 92),
            Color.FromArgb(35, 107, 238),
            Color.FromArgb(26, 135, 84),
            Color.FromArgb(190, 94, 35),
            Color.FromArgb(126, 87, 194),
            Color.FromArgb(24, 145, 160)
        };
        return colors[Random.Shared.Next(colors.Length)];
    }

    private Rectangle GetTextRect()
    {
        return new Rectangle(18, 48, Math.Max(1, Width - 36), Math.Max(1, Height - 62));
    }

    private void BeginEdit(Point clickPoint)
    {
        var textRect = GetTextRect();
        var editorPoint = new Point(clickPoint.X - textRect.X, clickPoint.Y - textRect.Y);
        if (_editor is not null && !_editor.IsDisposed)
        {
            _editor.ActivateEditor(editorPoint);
            return;
        }

        _editor = new DesktopNoteEditorForm(_note, GetEditorBackColor(), NoteStyle.TextColor(_note), CommitEditorText)
        {
            Bounds = RectangleToScreen(textRect)
        };
        _editor.FormClosed += (_, _) => _editor = null;
        _editor.Show();
        _editor.ActivateEditor(editorPoint);
        Invalidate();
    }

    private void MoveEditor()
    {
        if (_editor is null || _editor.IsDisposed)
        {
            return;
        }

        _editor.Bounds = RectangleToScreen(GetTextRect());
    }

    private Color GetEditorBackColor()
    {
        var noteColor = Color.FromArgb(_note.ColorArgb);
        return noteColor.A == 0 ? Color.FromArgb(28, 38, 54) : Color.FromArgb(255, noteColor.R, noteColor.G, noteColor.B);
    }

    private void CommitEditorText(string text)
    {
        _note.Text = text;
        _note.UpdatedAt = DateTime.Now;
        _noteChanged();
        Invalidate();
    }

    private static void DrawWrappedText(Graphics g, string text, Font font, Rectangle rect, Color color)
    {
        using var brush = new SolidBrush(color);
        using var format = new StringFormat(StringFormatFlags.LineLimit)
        {
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.LineLimit
        };
        g.DrawString(text, font, brush, rect, format);
    }

    private static void DrawSingleLineText(Graphics g, string text, Font font, Rectangle rect, Color color)
    {
        using var brush = new SolidBrush(color);
        using var format = new StringFormat(StringFormatFlags.NoWrap)
        {
            Trimming = StringTrimming.EllipsisCharacter,
            LineAlignment = StringAlignment.Center
        };
        g.DrawString(text, font, brush, rect, format);
    }

    private void LoadBackground()
    {
        _background?.Dispose();
        _background = null;

        if (string.IsNullOrWhiteSpace(_note.BackgroundImagePath) || !File.Exists(_note.BackgroundImagePath))
        {
            return;
        }

        try
        {
            using var stream = new MemoryStream(File.ReadAllBytes(_note.BackgroundImagePath));
            using var image = Image.FromStream(stream);
            _background = new Bitmap(image);
        }
        catch
        {
            _background?.Dispose();
            _background = null;
        }
    }

    private static void DrawImageFit(Graphics g, Image image, Rectangle rect)
    {
        var scale = Math.Max(rect.Width / (float)image.Width, rect.Height / (float)image.Height);
        var width = Math.Max(1, (int)(image.Width * scale));
        var height = Math.Max(1, (int)(image.Height * scale));
        var target = new Rectangle(rect.X + (rect.Width - width) / 2, rect.Y + (rect.Height - height) / 2, width, height);
        g.DrawImage(image, target);
    }

    private static void DrawImageActualSize(Graphics g, Image image, Rectangle rect)
    {
        var target = new Rectangle(rect.X + (rect.Width - image.Width) / 2, rect.Y + (rect.Height - image.Height) / 2, image.Width, image.Height);
        g.DrawImage(image, target);
    }

    private Rectangle GetResizeRect()
    {
        return new Rectangle(Math.Max(0, Width - 24), Math.Max(0, Height - 24), 24, 24);
    }

    private void DrawResizeGrip(Graphics g)
    {
        var rect = GetResizeRect();
        using var pen = new Pen(Color.FromArgb(210, 255, 255, 255), 1.5F);
        for (var i = 0; i < 3; i++)
        {
            var offset = i * 6;
            g.DrawLine(pen, rect.Right - 5 - offset, rect.Bottom - 4, rect.Right - 4, rect.Bottom - 5 - offset);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _editor?.Close();
            _menu.Dispose();
            _background?.Dispose();
        }

        base.Dispose(disposing);
    }
}

internal sealed class DesktopNoteEditorForm : Form
{
    private const int NiCompositionStr = 0x0015;
    private const int CpsComplete = 0x0001;

    private readonly TextBox _textBox;
    private readonly Action<string> _commit;
    private string _lastCommittedText;
    private bool _closing;
    private bool _cancelled;

    public DesktopNoteEditorForm(NoteItem note, Color backColor, Color foreColor, Action<string> commit)
    {
        _commit = commit;
        _lastCommittedText = note.Text;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        BackColor = backColor;
        _textBox = new TextBox
        {
            Multiline = true,
            AcceptsTab = true,
            BorderStyle = BorderStyle.None,
            ScrollBars = ScrollBars.None,
            Dock = DockStyle.Fill,
            Text = note.Text,
            BackColor = backColor,
            ForeColor = foreColor,
            Font = new Font("Microsoft YaHei UI", Math.Clamp(note.FontSize, 8F, 42F), note.FontBold ? FontStyle.Bold : FontStyle.Regular)
        };
        _textBox.TextChanged += (_, _) => CommitCurrentText();
        _textBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                _cancelled = true;
                Close();
                e.SuppressKeyPress = true;
            }
        };
        Controls.Add(_textBox);
        _textBox.Leave += (_, _) => CompleteImeComposition();
        Deactivate += (_, _) =>
        {
            CompleteImeComposition();
            BeginInvoke(new Action(() =>
            {
                CommitCurrentText();
                Close();
            }));
        };
    }

    public void ActivateEditor(Point editorPoint)
    {
        Activate();
        _textBox.Focus();
        var index = _textBox.GetCharIndexFromPosition(editorPoint);
        _textBox.SelectionStart = Math.Clamp(index, 0, _textBox.TextLength);
        _textBox.SelectionLength = 0;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_closing && !_cancelled)
        {
            CompleteImeComposition();
            CommitCurrentText();
        }

        _closing = true;
        base.OnFormClosing(e);
    }

    private void CommitCurrentText()
    {
        if (_cancelled || string.Equals(_lastCommittedText, _textBox.Text, StringComparison.Ordinal))
        {
            return;
        }

        _lastCommittedText = _textBox.Text;
        _commit(_lastCommittedText);
    }

    private void CompleteImeComposition()
    {
        if (!_textBox.IsHandleCreated)
        {
            return;
        }

        var context = ImmGetContext(_textBox.Handle);
        if (context == IntPtr.Zero)
        {
            return;
        }

        try
        {
            _ = ImmNotifyIME(context, NiCompositionStr, CpsComplete, 0);
        }
        finally
        {
            _ = ImmReleaseContext(_textBox.Handle, context);
        }
    }

    [DllImport("imm32.dll")]
    private static extern IntPtr ImmGetContext(IntPtr windowHandle);

    [DllImport("imm32.dll")]
    private static extern bool ImmReleaseContext(IntPtr windowHandle, IntPtr inputContext);

    [DllImport("imm32.dll")]
    private static extern bool ImmNotifyIME(IntPtr inputContext, int action, int index, int value);
}

internal sealed class ResizeGripControl : Control
{
    public ResizeGripControl()
    {
        Cursor = Cursors.SizeNWSE;
        BackColor = Color.FromArgb(24, 34, 48);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(Color.FromArgb(150, 166, 184, 206), 1.5F);
        for (var i = 0; i < 3; i++)
        {
            var offset = i * 6;
            e.Graphics.DrawLine(
                pen,
                Width - 5 - offset,
                Height - 4,
                Width - 4,
                Height - 5 - offset);
        }
    }
}

internal static class DesktopOrganizerStorage
{
    public static string? MoveToDesktopAndRemove(AppConfig config, string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        var target = GetDesktopTargetPath(source);
        if (File.Exists(source) || Directory.Exists(source))
        {
            target = MoveToDesktop(source);
        }

        RemoveOrganizerReferences(config, source, target);
        return target;
    }

    public static bool RemoveDesktopDuplicateOrganizerReferences(AppConfig config)
    {
        var desktopNames = GetDesktopEntryNames();
        if (desktopNames.Count == 0)
        {
            return false;
        }

        var removed = false;
        foreach (var category in config.DesktopCategories)
        {
            var count = category.ItemPaths.RemoveAll(path =>
            {
                var name = Path.GetFileName(path);
                return !string.IsNullOrWhiteSpace(name) && desktopNames.Contains(name);
            });
            removed |= count > 0;
        }

        return removed;
    }

    public static void RemoveOrganizerReferences(AppConfig config, params string?[] paths)
    {
        var referencePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var referenceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            referencePaths.Add(NormalizePath(path));
            var name = Path.GetFileName(path);
            if (!string.IsNullOrWhiteSpace(name))
            {
                referenceNames.Add(name);
            }
        }

        if (referencePaths.Count == 0 && referenceNames.Count == 0)
        {
            return;
        }

        foreach (var category in config.DesktopCategories)
        {
            category.ItemPaths.RemoveAll(item => IsSameDesktopEntry(item, referencePaths, referenceNames));
        }
    }

    public static string? MoveIntoCategory(AppStore store, DeskCategory category, string source)
    {
        if (string.IsNullOrWhiteSpace(source) || (!File.Exists(source) && !Directory.Exists(source)))
        {
            return null;
        }

        var folder = Path.Combine(store.DataDirectory, "DesktopOrganizer", SanitizeFileName(category.Name));
        var target = Path.Combine(folder, Path.GetFileName(source));
        return MoveOverwrite(source, target);
    }

    public static string? MoveToDesktop(string source)
    {
        if (string.IsNullOrWhiteSpace(source) || (!File.Exists(source) && !Directory.Exists(source)))
        {
            return null;
        }

        var target = GetDesktopTargetPath(source);
        if (target is null)
        {
            return null;
        }

        return MoveOverwrite(source, target);
    }

    private static string MoveOverwrite(string source, string target)
    {
        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            return source;
        }

        var directory = Path.GetDirectoryName(target);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(target))
        {
            File.Delete(target);
        }
        else if (Directory.Exists(target))
        {
            Directory.Delete(target, recursive: true);
        }

        if (File.Exists(source))
        {
            File.Move(source, target);
        }
        else
        {
            Directory.Move(source, target);
        }

        return target;
    }

    private static bool IsSameDesktopEntry(string candidate, HashSet<string> referencePaths, HashSet<string> referenceNames)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        if (referencePaths.Contains(NormalizePath(candidate)))
        {
            return true;
        }

        var name = Path.GetFileName(candidate);
        return !string.IsNullOrWhiteSpace(name) && referenceNames.Contains(name);
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    private static string? GetDesktopTargetPath(string source)
    {
        var name = Path.GetFileName(source);
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), name);
    }

    private static HashSet<string> GetDesktopEntryNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var desktop in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
        })
        {
            if (string.IsNullOrWhiteSpace(desktop) || !Directory.Exists(desktop))
            {
                continue;
            }

            try
            {
                foreach (var path in Directory.EnumerateFileSystemEntries(desktop))
                {
                    var name = Path.GetFileName(path);
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        names.Add(name);
                    }
                }
            }
            catch
            {
            }
        }

        return names;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var cleaned = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "未命名" : cleaned;
    }
}

internal sealed class DragPreviewForm : Form
{
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExTransparent = 0x00000020;
    private const int WsExLayered = 0x00080000;
    private const int WmNchittest = 0x0084;
    private const int HtTransparent = -1;
    private const int UlwAlpha = 0x00000002;
    private const byte AcSrcOver = 0x00;
    private const byte AcSrcAlpha = 0x01;
    private readonly Image? _icon;
    private readonly string _name;

    public DragPreviewForm(string path)
    {
        _name = GetDisplayName(path);
        _icon = File.Exists(path) || Directory.Exists(path) ? ShellIconLoader.LoadLargeIcon(path) : null;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(48, 48);
        TopMost = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WsExToolWindow | WsExNoActivate | WsExTransparent | WsExLayered;
            return cp;
        }
    }

    public void MoveToCursor(Point cursor)
    {
        Location = new Point(cursor.X + 10, cursor.Y + 10);
        UpdateLayeredPreview();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        UpdateLayeredPreview();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmNchittest)
        {
            m.Result = (IntPtr)HtTransparent;
            return;
        }

        base.WndProc(ref m);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        DrawPreviewIcon(e.Graphics, new Rectangle(0, 0, Width, Height));
    }

    private void UpdateLayeredPreview()
    {
        if (!IsHandleCreated || Width <= 0 || Height <= 0)
        {
            return;
        }

        using var bitmap = new Bitmap(Width, Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            DrawPreviewIcon(graphics, new Rectangle(0, 0, Width, Height));
        }

        var screenDc = GetDC(IntPtr.Zero);
        var memoryDc = CreateCompatibleDC(screenDc);
        var bitmapHandle = bitmap.GetHbitmap(Color.FromArgb(0));
        var oldBitmap = SelectObject(memoryDc, bitmapHandle);
        try
        {
            var size = new LayeredSize { Cx = Width, Cy = Height };
            var source = new LayeredPoint();
            var position = new LayeredPoint { X = Left, Y = Top };
            var blend = new BlendFunction
            {
                BlendOp = AcSrcOver,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = AcSrcAlpha
            };
            _ = UpdateLayeredWindow(Handle, screenDc, ref position, ref size, memoryDc, ref source, 0, ref blend, UlwAlpha);
        }
        finally
        {
            _ = SelectObject(memoryDc, oldBitmap);
            _ = DeleteObject(bitmapHandle);
            _ = DeleteDC(memoryDc);
            _ = ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private void DrawPreviewIcon(Graphics graphics, Rectangle iconRect)
    {
        if (_icon is not null)
        {
            graphics.DrawImage(_icon, iconRect);
        }
        else
        {
            using var brush = new SolidBrush(IconColor(_name));
            using var path = RoundPath(iconRect, 8);
            graphics.FillPath(brush, path);
            TextRenderer.DrawText(graphics, IconText(_name), new Font("Microsoft YaHei UI", 12F, FontStyle.Bold), iconRect, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _icon?.Dispose();
        }

        base.Dispose(disposing);
    }

    private static string GetDisplayName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return string.IsNullOrWhiteSpace(name) ? Path.GetFileName(path) : name;
    }

    private static string IconText(string name)
    {
        return string.IsNullOrWhiteSpace(name) ? "?" : name.Trim()[0].ToString().ToUpperInvariant();
    }

    private static Color IconColor(string name)
    {
        var hash = Math.Abs(name.GetHashCode());
        var colors = new[]
        {
            Color.FromArgb(54, 128, 245),
            Color.FromArgb(42, 176, 111),
            Color.FromArgb(236, 78, 78),
            Color.FromArgb(244, 169, 45),
            Color.FromArgb(143, 91, 234)
        };
        return colors[hash % colors.Length];
    }

    private static GraphicsPath RoundPath(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        rect.Width -= 1;
        rect.Height -= 1;
        path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetDC(IntPtr windowHandle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int ReleaseDC(IntPtr windowHandle, IntPtr deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteDC(IntPtr deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr SelectObject(IntPtr deviceContext, IntPtr handle);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(IntPtr windowHandle, IntPtr destinationDc, ref LayeredPoint position, ref LayeredSize size, IntPtr sourceDc, ref LayeredPoint source, int colorKey, ref BlendFunction blend, int flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct LayeredPoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LayeredSize
    {
        public int Cx;
        public int Cy;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }
}

internal sealed class DesktopOrganizerWidgetForm : Form
{
    private const int ResizeBorder = 12;
    private const int WmNchittest = 0x0084;
    private const int HtClient = 1;
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;

    private readonly AppConfig _config;
    private readonly AppStore _store;
    private readonly Action<Rectangle> _placementChanged;
    private readonly FlowLayoutPanel _list = new();
    private readonly DesktopOrganizerWidgetView _view;
    private WidgetResizeMessageFilter? _resizeFilter;
    private bool _placedOnce;
    private bool _attachedToDesktop;
    private bool _restoringPlacement;

    public DesktopOrganizerWidgetForm(AppConfig config, AppStore store, Action<Rectangle> placementChanged, Func<IEnumerable<DeskCategory>>? categoryProvider = null, bool isSplit = false)
    {
        _config = config;
        _store = store;
        _placementChanged = placementChanged;
        _view = new DesktopOrganizerWidgetView(_config, categoryProvider, isSplit);

        Text = "桌面收纳";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(620, 520);
        MinimumSize = new Size(300, 240);
        ShowInTaskbar = false;
        TopMost = false;
        BackColor = Color.FromArgb(20, 28, 40);
        Font = new Font("Microsoft YaHei UI", 9F);
        DoubleBuffered = true;

        BuildUi();
        ApplyWidgetSkin();
        RefreshList();
        _resizeFilter = new WidgetResizeMessageFilter(this);
        Application.AddMessageFilter(_resizeFilter);
    }

    public event Action? ManageRequested;
    public event Action<DeskCategory>? SplitRequested;
    public event Action? MergeRequested;

    public void RefreshWidget()
    {
        RefreshList();
    }

    public void ShowAsDesktopWidget(WidgetPlacement? placement = null)
    {
        RefreshList();
        ApplyPlacementOrDefault(placement);

        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }

        Show();
        AttachToDesktopHost();
        SavePlacement();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyWidgetSkin();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        AttachToDesktopHost();
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);

        if (m.Msg != WmNchittest || m.Result != (IntPtr)HtClient)
        {
            return;
        }

        var point = PointToClient(GetScreenPoint(m.LParam));
        var hit = GetResizeHit(ClientSize, point);
        if (hit != HtClient)
        {
            m.Result = (IntPtr)hit;
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateRoundedRegion();
        _view.Invalidate();
        SavePlacement();
    }

    protected override void OnMove(EventArgs e)
    {
        base.OnMove(e);
        SavePlacement();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_resizeFilter is not null)
        {
            Application.RemoveMessageFilter(_resizeFilter);
            _resizeFilter = null;
        }
        base.OnFormClosing(e);
    }

    private void ApplyPlacementOrDefault(WidgetPlacement? placement)
    {
        _restoringPlacement = true;
        try
        {
            if (placement is not null && placement.Width > 0 && placement.Height > 0)
            {
                Bounds = new Rectangle(placement.X, placement.Y, Math.Max(MinimumSize.Width, placement.Width), Math.Max(MinimumSize.Height, placement.Height));
                _placedOnce = true;
                return;
            }

        var screen = Screen.PrimaryScreen;
        var workArea = screen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        var current = new Rectangle(Location, Size);
        var visible = Screen.AllScreens.Any(item => Rectangle.Intersect(item.WorkingArea, current).Width > 80);
        if (_placedOnce && visible)
        {
            return;
        }

        Location = new Point(
            Math.Max(workArea.Left + 24, workArea.Right - Width - 36),
            workArea.Top + 88);
        _placedOnce = true;
        }
        finally
        {
            _restoringPlacement = false;
        }
    }

    private void AttachToDesktopHost()
    {
        if (_attachedToDesktop || !IsHandleCreated)
        {
            return;
        }

        _attachedToDesktop = NativeGlass.AttachToDesktop(Handle);
    }

    private void SavePlacement()
    {
        if (_restoringPlacement || !IsHandleCreated || WindowState == FormWindowState.Minimized)
        {
            return;
        }

        _placementChanged(Bounds);
    }

    private void BuildUi()
    {
        _view.Dock = DockStyle.Fill;
        _view.BeginMoveRequested += () => NativeGlass.BeginMove(Handle);
        _view.BeginResizeRequested += () => NativeGlass.BeginResize(Handle, HtBottomRight);
        _view.RefreshRequested += RefreshList;
        _view.AddCategoryRequested += AddCategoryFromWidget;
        _view.ManageRequested += () => ManageRequested?.Invoke();
        _view.OrganizeRequested += () => ManageRequested?.Invoke();
        _view.SplitRequested += category => SplitRequested?.Invoke(category);
        _view.MergeRequested += () => MergeRequested?.Invoke();
        _view.PathDroppedRequested += AddPathToCategoryFromWidget;
        _view.PathRemovedRequested += MovePathOutFromWidget;
        _view.ReorderRequested += () => _store.SaveConfig(_config);
        _view.SkinChangedRequested += () =>
        {
            _store.SaveConfig(_config);
            ApplyWidgetSkin();
        };
        _view.CloseRequested += Close;
        Controls.Add(_view);
    }

    private void ApplyWidgetSkin()
    {
        Opacity = 1.0;
        BackColor = Color.FromArgb(20, 28, 40);
        _view.BackColor = BackColor;
        if (IsHandleCreated)
        {
            var tint = _config.DesktopWidgetTransparent
                ? Color.FromArgb(34, 74, 138, 210)
                : Color.FromArgb(232, 18, 26, 38);
            NativeGlass.EnableAcrylic(Handle, tint);
        }

        _view.RefreshData();
    }

    private void RefreshList()
    {
        if (DesktopOrganizerStorage.RemoveDesktopDuplicateOrganizerReferences(_config))
        {
            _store.SaveConfig(_config);
        }

        _view.RefreshData();
    }

    private void AddCategoryFromWidget()
    {
        var name = Prompt("添加分类", "分类名称");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        _config.DesktopCategories.Add(new DeskCategory { Name = name.Trim() });
        _store.SaveConfig(_config);
        RefreshList();
    }

    private void AddPathToCategoryFromWidget(DeskCategory category, string path, int? insertIndex)
    {
        if (!_config.DesktopCategories.Contains(category) || string.IsNullOrWhiteSpace(path) || (!File.Exists(path) && !Directory.Exists(path)))
        {
            return;
        }

        var target = DesktopOrganizerStorage.MoveIntoCategory(_store, category, path);
        if (target is null)
        {
            return;
        }

        DesktopOrganizerStorage.RemoveOrganizerReferences(_config, path, target);

        var index = insertIndex.HasValue
            ? Math.Clamp(insertIndex.Value, 0, category.ItemPaths.Count)
            : category.ItemPaths.Count;
        category.ItemPaths.Insert(index, target);
        _store.SaveConfig(_config);
        RefreshList();
    }

    private void MovePathOutFromWidget(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || (!File.Exists(path) && !Directory.Exists(path)))
        {
            return;
        }

        DesktopOrganizerStorage.MoveToDesktopAndRemove(_config, path);
        _store.SaveConfig(_config);
        RefreshList();
    }

    private static string GetUniquePath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var fileName = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        for (var index = 2; ; index++)
        {
            var candidate = Path.Combine(directory, $"{fileName} ({index}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var cleaned = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "未命名" : cleaned;
    }

    private void UpdateRoundedRegion()
    {
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0)
        {
            return;
        }

        using var path = CreateRoundPath(new Rectangle(0, 0, ClientSize.Width, ClientSize.Height), 10);
        Region?.Dispose();
        Region = new Region(path);
    }

    private static GraphicsPath CreateRoundPath(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        rect.Width -= 1;
        rect.Height -= 1;
        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private Control CreateCategoryPanel(DeskCategory category)
    {
        var panel = new Panel
        {
            Width = Math.Max(260, _list.ClientSize.Width - 24),
            Height = 78,
            BackColor = Color.FromArgb(43, 55, 73),
            Margin = new Padding(8, 8, 8, 0),
            Padding = new Padding(12)
        };

        var title = new Label
        {
            Text = $"{category.Name}  {category.ItemPaths.Count}个",
            Dock = DockStyle.Top,
            Height = 24,
            ForeColor = Color.White,
            Font = new Font(Font.FontFamily, 10F, FontStyle.Bold)
        };
        var preview = new Label
        {
            Text = BuildPreviewText(category),
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(185, 198, 214),
            TextAlign = ContentAlignment.MiddleLeft
        };

        panel.Controls.Add(preview);
        panel.Controls.Add(title);
        return panel;
    }

    private static string BuildPreviewText(DeskCategory category)
    {
        var names = category.ItemPaths
            .Where(path => File.Exists(path) || Directory.Exists(path))
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Take(4)
            .ToArray();

        return names.Length == 0 ? "暂无项目" : string.Join("    ", names);
    }

    private Button CreateWidgetButton(string text)
    {
        var button = new Button
        {
            Text = text,
            Height = 30,
            AutoSize = false,
            Margin = new Padding(0, 6, 8, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(35, 107, 238),
            ForeColor = Color.White,
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderSize = 0;
        return button;
    }

    private static Point GetScreenPoint(IntPtr lParamPtr)
    {
        var lParam = lParamPtr.ToInt64();
        return new Point((short)(lParam & 0xffff), (short)((lParam >> 16) & 0xffff));
    }

    private static int GetResizeHit(Size clientSize, Point point)
    {
        var left = point.X <= ResizeBorder;
        var right = point.X >= clientSize.Width - ResizeBorder;
        var top = point.Y <= ResizeBorder;
        var bottom = point.Y >= clientSize.Height - ResizeBorder;

        return (left, right, top, bottom) switch
        {
            (true, false, true, false) => HtTopLeft,
            (false, true, true, false) => HtTopRight,
            (true, false, false, true) => HtBottomLeft,
            (false, true, false, true) => HtBottomRight,
            (true, false, false, false) => HtLeft,
            (false, true, false, false) => HtRight,
            (false, false, true, false) => HtTop,
            (false, false, false, true) => HtBottom,
            _ => HtClient
        };
    }

    private sealed class WidgetResizeMessageFilter : IMessageFilter
    {
        private readonly DesktopOrganizerWidgetForm _form;

        public WidgetResizeMessageFilter(DesktopOrganizerWidgetForm form)
        {
            _form = form;
        }

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != WmNchittest || _form.IsDisposed)
            {
                return false;
            }

            var control = Control.FromHandle(m.HWnd);
            if (control is null || !BelongsToForm(control))
            {
                return false;
            }

            var hit = GetResizeHit(_form.ClientSize, _form.PointToClient(GetScreenPoint(m.LParam)));
            if (hit == HtClient)
            {
                return false;
            }

            m.Result = (IntPtr)hit;
            return true;
        }

        private bool BelongsToForm(Control control)
        {
            Control? current = control;
            while (current is not null)
            {
                if (ReferenceEquals(current, _form))
                {
                    return true;
                }

                current = current.Parent;
            }

            return false;
        }
    }

    private static string? Prompt(string title, string label)
    {
        using var form = new Form
        {
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ClientSize = new Size(360, 132),
            Font = new Font("Microsoft YaHei UI", 9F)
        };

        var labelControl = new Label
        {
            Text = label,
            Left = 16,
            Top = 18,
            Width = 328
        };
        var input = new TextBox
        {
            Left = 16,
            Top = 44,
            Width = 328
        };
        var okButton = new Button
        {
            Text = "确定",
            Left = 178,
            Top = 86,
            Width = 78,
            DialogResult = DialogResult.OK
        };
        var cancelButton = new Button
        {
            Text = "取消",
            Left = 266,
            Top = 86,
            Width = 78,
            DialogResult = DialogResult.Cancel
        };

        form.Controls.AddRange(new Control[] { labelControl, input, okButton, cancelButton });
        form.AcceptButton = okButton;
        form.CancelButton = cancelButton;
        return form.ShowDialog() == DialogResult.OK ? input.Text : null;
    }
}

internal sealed class DesktopOrganizerWidgetView : Control
{
    private const int WmContextMenu = 0x007B;
    private readonly AppConfig _config;
    private readonly Func<IReadOnlyList<DeskCategory>> _categoryProvider;
    private readonly List<(Rectangle Rect, string Key)> _hotspots = new();
    private readonly List<(Rectangle Rect, DeskCategory? Category)> _previewAreas = new();
    private readonly List<(Rectangle Rect, int Index)> _categoryTabAreas = new();
    private readonly List<(Rectangle Rect, Rectangle Tile, DeskCategory Category, string Path, int Index)> _itemAreas = new();
    private readonly Dictionary<DeskCategory, int> _previewScrollOffsets = new();
    private readonly Dictionary<string, Image> _shellIconCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ContextMenuStrip _menu = new();
    private readonly ContextMenuStrip _settingsMenu = new();
    private readonly bool _isSplit;
    private readonly ToolStripMenuItem _transparentMenuItem = new("透明");
    private readonly ToolStripMenuItem _splitMenuItem = new("拆分");
    private readonly ToolStripMenuItem _mergeMenuItem = new("合并");
    private readonly ToolTip _itemToolTip = new()
    {
        AutomaticDelay = 250,
        ReshowDelay = 100,
        AutoPopDelay = 8000
    };
    private readonly Image? _settingsIcon;
    private DeskCategory? _dragPreviewCategory;
    private Rectangle _dragPreviewArea;
    private int _dragPreviewStartX;
    private int _dragPreviewStartOffset;
    private string? _pendingItemDragPath;
    private Point _pendingItemDragStart;
    private string? _activeDraggedItemPath;
    private bool _handledActiveDraggedItemDrop;
    private DragPreviewForm? _externalDragPreview;
    private string? _externalDragPreviewPath;
    private DeskCategory? _externalDragCategory;
    private int? _externalDragInsertIndex;
    private DeskCategory? _dragItemCategory;
    private string? _dragItemPath;
    private List<string>? _dragItemOriginalPaths;
    private int? _dragItemInsertIndex;
    private Point _dragItemLocation;
    private Point _dragItemOffset;
    private Rectangle _dragItemTile;
    private bool _draggingItem;
    private Rectangle _categoryTabArea;
    private bool _draggingCategoryTabs;
    private int _categoryTabDragStartX;
    private int _categoryTabDragStartOffset;
    private int _categoryTabScrollOffset;
    private int _dragTargetCategoryIndex = -1;
    private int _selectedCategoryIndex;
    private string? _selectedItemPath;
    private Rectangle _headerRect;
    private Rectangle _resizeRect;
    private string? _hoverToolTipPath;
    private bool _suppressNextClick;
    private const int DragThreshold = 6;
    private const int PreviewTileWidth = 120;
    private const int PreviewTileHeight = 82;
    private const int HeaderTop = 13;
    private const int HeaderHeight = 64;
    private const int ListBottomInset = 18;

    private static readonly Color CardFill = Color.FromArgb(238, 25, 35, 50);
    private static readonly Color CardBorder = Color.FromArgb(92, 100, 123, 152);
    private static readonly Color PanelFill = Color.FromArgb(126, 17, 25, 37);
    private static readonly Color RowLine = Color.FromArgb(52, 92, 110, 136);
    private static readonly Color TextMain = Color.FromArgb(238, 244, 252);
    private static readonly Color TextMuted = Color.FromArgb(158, 174, 194);
    private static readonly Color Accent = Color.FromArgb(58, 109, 248);
    private static readonly Font HeaderFont = new("Microsoft YaHei UI", 11.5F, FontStyle.Regular);
    private static readonly Font TitleFont = new("Microsoft YaHei UI", 10.5F, FontStyle.Regular);
    private static readonly Font NormalFont = new("Microsoft YaHei UI", 8.8F);
    private static readonly Font SmallFont = new("Microsoft YaHei UI", 8F);
    private static readonly Font IconFont = new("Microsoft YaHei UI", 11F, FontStyle.Bold);

    private Color CurrentCardFill => _config.DesktopWidgetTransparent ? Color.FromArgb(74, 18, 30, 46) : CardFill;
    private Color CurrentCardBorder => _config.DesktopWidgetTransparent ? Color.FromArgb(132, 150, 194, 238) : CardBorder;
    private Color CurrentPanelFill => _config.DesktopWidgetTransparent ? Color.FromArgb(44, 10, 22, 36) : PanelFill;

    public DesktopOrganizerWidgetView(AppConfig config, Func<IEnumerable<DeskCategory>>? categoryProvider = null, bool isSplit = false)
    {
        _config = config;
        _isSplit = isSplit;
        _categoryProvider = () => (categoryProvider?.Invoke() ?? _config.DesktopCategories).ToArray();
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.FromArgb(20, 28, 40);

        _menu.Items.Add("刷新", null, (_, _) => RefreshRequested?.Invoke());
        _menu.Items.Add("添加分类", null, (_, _) => AddCategoryRequested?.Invoke());
        _settingsMenu.Items.Add("整理桌面", null, (_, _) => OrganizeRequested?.Invoke());
        _settingsMenu.Items.Add("添加分类", null, (_, _) => AddCategoryRequested?.Invoke());
        _splitMenuItem.Click += (_, _) =>
        {
            var category = CurrentCategory();
            if (category is not null)
            {
                SplitRequested?.Invoke(category);
            }
        };
        _settingsMenu.Items.Add(_splitMenuItem);
        _mergeMenuItem.Click += (_, _) => MergeRequested?.Invoke();
        _settingsMenu.Items.Add(_mergeMenuItem);
        var skinMenu = new ToolStripMenuItem("皮肤设置");
        _transparentMenuItem.Checked = _config.DesktopWidgetTransparent;
        _transparentMenuItem.Click += (_, _) =>
        {
            _config.DesktopWidgetTransparent = !_config.DesktopWidgetTransparent;
            _transparentMenuItem.Checked = _config.DesktopWidgetTransparent;
            SkinChangedRequested?.Invoke();
        };
        skinMenu.DropDownItems.Add(_transparentMenuItem);
        _settingsMenu.Items.Add(skinMenu);
        _settingsMenu.Items.Add("关闭组件", null, (_, _) => CloseRequested?.Invoke());
        _settingsMenu.Opening += (_, _) =>
        {
            _transparentMenuItem.Checked = _config.DesktopWidgetTransparent;
            _splitMenuItem.Visible = !_isSplit;
            _splitMenuItem.Enabled = CurrentCategory() is not null && Categories().Count > 1;
            _mergeMenuItem.Visible = _isSplit;
        };

        using var settingsIcon = LoadWidgetImage("images", "zhuomianguinarongqi", "shezhi.png");
        _settingsIcon = settingsIcon is null ? null : TintImage(settingsIcon, Color.FromArgb(130, 180, 255));
        AllowDrop = true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var icon in _shellIconCache.Values)
            {
                icon.Dispose();
            }

            _shellIconCache.Clear();
            _itemToolTip.Dispose();
            _menu.Dispose();
            _settingsMenu.Dispose();
            _settingsIcon?.Dispose();
            HideExternalDragPreview();
        }

        base.Dispose(disposing);
    }

    public event Action? BeginMoveRequested;
    public event Action? BeginResizeRequested;
    public event Action? RefreshRequested;
    public event Action? AddCategoryRequested;
    public event Action? ManageRequested;
    public event Action? OrganizeRequested;
    public event Action<DeskCategory>? SplitRequested;
    public event Action? MergeRequested;
    public event Action? SkinChangedRequested;
    public event Action<DeskCategory, string, int?>? PathDroppedRequested;
    public event Action<string>? PathRemovedRequested;
    public event Action? ReorderRequested;
    public event Action? CloseRequested;

    public void RefreshData()
    {
        var categories = Categories();
        if (_selectedCategoryIndex >= categories.Count)
        {
            _selectedCategoryIndex = Math.Max(0, categories.Count - 1);
        }

        Invalidate();
    }

    private IReadOnlyList<DeskCategory> Categories() => _categoryProvider();

    private DeskCategory? CurrentCategory()
    {
        var categories = Categories();
        return categories.Count == 0 ? null : categories[Math.Clamp(_selectedCategoryIndex, 0, categories.Count - 1)];
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmContextMenu)
        {
            m.Result = IntPtr.Zero;
            return;
        }

        base.WndProc(ref m);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            var rightItemHit = FindItemHit(e.Location);
            if (rightItemHit is not null)
            {
                _selectedItemPath = rightItemHit.Value.Path;
                Invalidate();
                if (ShellContextMenu.ShowForPath(Handle, rightItemHit.Value.Path, PointToScreen(e.Location)))
                {
                    RefreshRequested?.Invoke();
                }

                return;
            }

            var rightPreviewHit = _previewAreas.FirstOrDefault(item => item.Rect.Contains(e.Location));
            if (!rightPreviewHit.Rect.IsEmpty)
            {
                _settingsMenu.Show(this, e.Location);
            }
            else
            {
                _menu.Show(this, e.Location);
            }

            return;
        }

        if (_resizeRect.Contains(e.Location))
        {
            BeginResizeRequested?.Invoke();
            return;
        }

        var hit = _hotspots.FirstOrDefault(item => item.Rect.Contains(e.Location));
        switch (hit.Key)
        {
            case "manage":
                ManageRequested?.Invoke();
                return;
            case "organize":
                OrganizeRequested?.Invoke();
                return;
            case "addCategory":
                AddCategoryRequested?.Invoke();
                return;
            case "refresh":
                RefreshRequested?.Invoke();
                return;
            case "close":
                CloseRequested?.Invoke();
                return;
            case "settings":
                _settingsMenu.Show(this, hit.Rect.Left, hit.Rect.Bottom + 4);
                return;
        }

        if (e.Button == MouseButtons.Left && _categoryTabArea.Contains(e.Location))
        {
            var tabHit = _categoryTabAreas.FirstOrDefault(item => item.Rect.Contains(e.Location));
            if (!tabHit.Rect.IsEmpty)
            {
                _selectedCategoryIndex = tabHit.Index;
                _previewScrollOffsets.Clear();
                Invalidate();
            }

            if (CanScrollCategoryTabs(_categoryTabArea.Width))
            {
                _draggingCategoryTabs = true;
                _categoryTabDragStartX = e.X;
                _categoryTabDragStartOffset = _categoryTabScrollOffset;
                Capture = true;
                return;
            }

            if (!tabHit.Rect.IsEmpty)
            {
                return;
            }
        }

        var itemHit = FindItemHit(e.Location);
        if (e.Button == MouseButtons.Left && itemHit is not null)
        {
            _pendingItemDragPath = itemHit.Value.Path;
            _pendingItemDragStart = e.Location;
            Capture = true;
            return;
        }

        var previewHit = _previewAreas.FirstOrDefault(item => item.Rect.Contains(e.Location));
        if (e.Button == MouseButtons.Left && previewHit.Category is not null && CanScrollPreview(previewHit.Category, previewHit.Rect.Width, previewHit.Rect.Height))
        {
            _dragPreviewCategory = previewHit.Category;
            _dragPreviewArea = previewHit.Rect;
            _dragPreviewStartX = e.X;
            _dragPreviewStartOffset = _previewScrollOffsets.GetValueOrDefault(previewHit.Category);
            Capture = true;
            return;
        }

        if (_headerRect.Contains(e.Location))
        {
            BeginMoveRequested?.Invoke();
            return;
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_draggingCategoryTabs)
        {
            SetCategoryTabOffset(_categoryTabArea.Width, _categoryTabDragStartOffset + _categoryTabDragStartX - e.X);
            Cursor = Cursors.Default;
            return;
        }

        if (_pendingItemDragPath is not null)
        {
            var dx = Math.Abs(e.X - _pendingItemDragStart.X);
            var dy = Math.Abs(e.Y - _pendingItemDragStart.Y);
            if (dx >= DragThreshold || dy >= DragThreshold)
            {
                BeginItemDrag(e.Location);
                return;
            }
        }

        if (_draggingItem)
        {
            _dragItemLocation = e.Location;
            UpdateDragTargetCategory(e.Location);
            UpdateDraggedItemOrder(e.Location);
            Cursor = Cursors.Hand;
            Invalidate();
            return;
        }

        if (_dragPreviewCategory is not null)
        {
            SetPreviewOffset(_dragPreviewCategory, _dragPreviewArea.Width, _dragPreviewArea.Height, _dragPreviewStartOffset + _dragPreviewStartX - e.X);
            Cursor = Cursors.Default;
            return;
        }

        UpdateItemToolTip(e.Location);
        var hoveringItem = FindItemHit(e.Location) is not null;
        Cursor = _resizeRect.Contains(e.Location)
            ? Cursors.SizeNWSE
            : hoveringItem || _hotspots.Any(item => item.Rect.Contains(e.Location)) ? Cursors.Hand : Cursors.Default;
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        ClearItemToolTip();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_draggingCategoryTabs)
        {
            _draggingCategoryTabs = false;
            Capture = false;
            return;
        }

        if (_pendingItemDragPath is not null)
        {
            _pendingItemDragPath = null;
            Capture = false;
            return;
        }

        if (_draggingItem)
        {
            EndItemDrag(e.Location);
            return;
        }

        if (_dragPreviewCategory is not null)
        {
            _dragPreviewCategory = null;
            Capture = false;
            return;
        }

        base.OnMouseUp(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (_categoryTabArea.Contains(e.Location))
        {
            SetCategoryTabOffset(_categoryTabArea.Width, _categoryTabScrollOffset + (e.Delta < 0 ? 72 : -72));
            return;
        }

        var hit = _previewAreas.FirstOrDefault(item => item.Rect.Contains(e.Location));
        if (hit.Category is not null)
        {
            SetPreviewOffset(hit.Category, hit.Rect.Width, hit.Rect.Height, _previewScrollOffsets.GetValueOrDefault(hit.Category) + (e.Delta < 0 ? PreviewTileHeight : -PreviewTileHeight));
            return;
        }

        base.OnMouseWheel(e);
    }

    protected override void OnDragEnter(DragEventArgs e)
    {
        UpdateExternalDragPreview(e);
        e.Effect = GetDroppedPath(e) is null ? DragDropEffects.None : DragDropEffects.Move;
        base.OnDragEnter(e);
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        var location = PointToClient(new Point(e.X, e.Y));
        UpdateExternalDragPreview(e);
        UpdateDragTargetCategory(location);
        if (_draggingItem)
        {
            _dragItemLocation = location;
            UpdateDraggedItemOrder(location);
            Invalidate();
        }
        else
        {
            UpdateExternalDragInsert(location, GetDroppedPath(e));
        }

        e.Effect = GetDroppedPath(e) is null ? DragDropEffects.None : DragDropEffects.Move;
        base.OnDragOver(e);
    }

    protected override void OnDragDrop(DragEventArgs e)
    {
        var path = GetDroppedPath(e);
        HideExternalDragPreview();
        var categories = Categories();
        if (path is not null && categories.Count > 0)
        {
            if (!string.IsNullOrWhiteSpace(_activeDraggedItemPath)
                && string.Equals(path, _activeDraggedItemPath, StringComparison.OrdinalIgnoreCase))
            {
                _handledActiveDraggedItemDrop = true;
            }

            var targetIndex = FindCategoryTabIndex(PointToClient(new Point(e.X, e.Y)));
            _selectedCategoryIndex = targetIndex >= 0
                ? targetIndex
                : Math.Clamp(_selectedCategoryIndex, 0, categories.Count - 1);
            var insertIndex = _externalDragCategory is not null
                && _selectedCategoryIndex >= 0
                && _selectedCategoryIndex < categories.Count
                && ReferenceEquals(categories[_selectedCategoryIndex], _externalDragCategory)
                ? _externalDragInsertIndex
                : null;

            if (_draggingItem
                && _dragItemCategory is not null
                && _dragItemInsertIndex is not null
                && _selectedCategoryIndex >= 0
                && _selectedCategoryIndex < categories.Count
                && ReferenceEquals(categories[_selectedCategoryIndex], _dragItemCategory)
                && string.Equals(path, _dragItemPath, StringComparison.OrdinalIgnoreCase))
            {
                ApplyDraggedItemOrder();
                ReorderRequested?.Invoke();
                _handledActiveDraggedItemDrop = true;
                _dragTargetCategoryIndex = -1;
                Invalidate();
                return;
            }

            PathDroppedRequested?.Invoke(categories[_selectedCategoryIndex], path, insertIndex);
        }

        _dragTargetCategoryIndex = -1;
        ClearExternalDragInsert();
        Invalidate();
        base.OnDragDrop(e);
    }

    protected override void OnDragLeave(EventArgs e)
    {
        HideExternalDragPreview();
        UpdateDragTargetCategory(Point.Empty);
        if (_draggingItem)
        {
            Invalidate();
        }
        else
        {
            ClearExternalDragInsert();
        }

        base.OnDragLeave(e);
    }

    private void UpdateExternalDragPreview(DragEventArgs e)
    {
        if (_activeDraggedItemPath is not null)
        {
            return;
        }

        var path = GetDroppedPath(e);
        if (path is null)
        {
            HideExternalDragPreview();
            return;
        }

        if (!string.Equals(_externalDragPreviewPath, path, StringComparison.OrdinalIgnoreCase))
        {
            HideExternalDragPreview();
            _externalDragPreview = new DragPreviewForm(path);
            _externalDragPreviewPath = path;
            _externalDragPreview.Show();
        }

        _externalDragPreview?.MoveToCursor(new Point(e.X, e.Y));
    }

    private void UpdateExternalDragInsert(Point location, string? path)
    {
        var categories = Categories();
        if (path is null || categories.Count == 0)
        {
            ClearExternalDragInsert();
            return;
        }

        var targetIndex = FindCategoryTabIndex(location);
        var category = targetIndex >= 0
            ? categories[Math.Clamp(targetIndex, 0, categories.Count - 1)]
            : _previewAreas.FirstOrDefault(item => item.Rect.Contains(location)).Category;
        if (category is null)
        {
            ClearExternalDragInsert();
            return;
        }

        var preview = _previewAreas.FirstOrDefault(item => ReferenceEquals(item.Category, category));
        if (preview.Rect.IsEmpty || !preview.Rect.Contains(location))
        {
            _externalDragCategory = category;
            _externalDragInsertIndex = null;
            Invalidate();
            return;
        }

        var visibleCount = PreviewItemPaths(category)
            .Where(item => !string.Equals(item, path, StringComparison.OrdinalIgnoreCase))
            .Count();
        var insertIndex = GetItemInsertIndex(preview.Rect, category, location, visibleCount);
        if (ReferenceEquals(_externalDragCategory, category) && _externalDragInsertIndex == insertIndex && string.Equals(_externalDragPreviewPath, path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _externalDragCategory = category;
        _externalDragInsertIndex = insertIndex;
        Invalidate();
    }

    private void HideExternalDragPreview()
    {
        if (_externalDragPreview is null)
        {
            _externalDragPreviewPath = null;
            return;
        }

        _externalDragPreview.Close();
        _externalDragPreview.Dispose();
        _externalDragPreview = null;
        _externalDragPreviewPath = null;
    }

    private void ClearExternalDragInsert()
    {
        if (_externalDragCategory is null && _externalDragInsertIndex is null)
        {
            return;
        }

        _externalDragCategory = null;
        _externalDragInsertIndex = null;
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        if (_suppressNextClick)
        {
            _suppressNextClick = false;
            base.OnMouseClick(e);
            return;
        }

        if (e.Button == MouseButtons.Left)
        {
            var itemHit = FindItemHit(e.Location);
            if (itemHit is not null)
            {
                _selectedItemPath = itemHit.Value.Path;
                Invalidate();
                return;
            }
        }

        base.OnMouseClick(e);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            var itemHit = FindItemHit(e.Location);
            if (itemHit is not null)
            {
                _selectedItemPath = itemHit.Value.Path;
                Invalidate();
                OpenPath(itemHit.Value.Path);
                return;
            }
        }

        base.OnMouseDoubleClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        _hotspots.Clear();
        _previewAreas.Clear();
        _categoryTabAreas.Clear();
        _itemAreas.Clear();

        var card = new Rectangle(0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        if (card.Width < 260 || card.Height < 220)
        {
            return;
        }

        FillRound(g, card, CurrentCardFill, 10);
        DrawRound(g, card, CurrentCardBorder, 10);
        DrawHeader(g, card);
        DrawCategoryList(g, card);
        DrawResizeGrip(g, card);
    }

    private void DrawHeader(Graphics g, Rectangle card)
    {
        _headerRect = new Rectangle(card.X + 16, card.Y + HeaderTop, card.Width - 32, HeaderHeight);
        _categoryTabArea = new Rectangle(_headerRect.X, _headerRect.Y + 1, Math.Max(1, _headerRect.Width - 62), 34);
        DrawCategoryTabs(g, _categoryTabArea);

        var settings = new Rectangle(card.Right - 49, _headerRect.Y + 2, 30, 30);
        if (_settingsIcon is not null)
        {
            g.DrawImage(_settingsIcon, settings);
        }
        else
        {
            DrawGearIcon(g, settings, Color.FromArgb(106, 154, 255));
        }

        _hotspots.Add((settings, "settings"));
    }

    private void DrawCategoryTabs(Graphics g, Rectangle area)
    {
        var categories = Categories();
        if (categories.Count == 0)
        {
            DrawText(g, "暂无分类", HeaderFont, TextMuted, area.X, area.Y + 5);
            return;
        }

        _selectedCategoryIndex = Math.Clamp(_selectedCategoryIndex, 0, categories.Count - 1);
        var maxOffset = Math.Max(0, MeasureCategoryTabsWidth(categories) - area.Width);
        _categoryTabScrollOffset = Math.Clamp(_categoryTabScrollOffset, 0, maxOffset);

        var state = g.Save();
        try
        {
            g.SetClip(area);
            var x = area.X - _categoryTabScrollOffset;
            for (var i = 0; i < categories.Count; i++)
            {
                var name = categories[i].Name;
                var textSize = TextRenderer.MeasureText(name, HeaderFont);
                var tab = new Rectangle(x, area.Y + 1, Math.Max(84, textSize.Width + 43), 31);
                if (tab.Right >= area.Left && tab.Left <= area.Right)
                {
                    var selected = i == _selectedCategoryIndex;
                    var dragTarget = i == _dragTargetCategoryIndex;
                    if (selected || dragTarget)
                    {
                        FillRound(g, tab, dragTarget ? Color.FromArgb(132, 58, 144, 255) : Color.FromArgb(88, 58, 109, 248), 6);
                    }

                    if (dragTarget)
                    {
                        DrawRound(g, tab, Color.FromArgb(210, 178, 210, 255), 6);
                    }

                    var textY = tab.Y + Math.Max(0, (tab.Height - textSize.Height) / 2);
                    var folder = new Rectangle(tab.X + 8, tab.Y + (tab.Height - 19) / 2, 22, 19);
                    DrawFolder(g, folder, CategoryColor(name, i));
                    DrawText(g, name, HeaderFont, selected ? Color.White : TextMain, tab.X + 36, textY);
                    _categoryTabAreas.Add((tab, i));
                }

                x += tab.Width + 14;
            }
        }
        finally
        {
            g.Restore(state);
        }
    }

    private void DrawCategoryList(Graphics g, Rectangle card)
    {
        var listTop = card.Y + HeaderTop + HeaderHeight + 1;
        var list = new Rectangle(card.X + 12, listTop, card.Width - 24, Math.Max(1, card.Bottom - ListBottomInset - listTop));
        FillRound(g, list, CurrentPanelFill, 6);

        IReadOnlyList<DeskCategory> categories = Categories();
        if (categories.Count == 0)
        {
            DrawCentered(g, "暂无分类", NormalFont, TextMuted, list);
            return;
        }

        _selectedCategoryIndex = Math.Clamp(_selectedCategoryIndex, 0, categories.Count - 1);
        DrawCategoryRow(g, list, categories[_selectedCategoryIndex]);
    }

    private void DrawCategoryRow(Graphics g, Rectangle row, DeskCategory category)
    {
        var itemPaths = VisiblePreviewItemPaths(category).ToArray();
        var previewArea = new Rectangle(row.X + 14, row.Y + 12, Math.Max(1, row.Width - 28), Math.Max(1, row.Height - 24));
        _previewAreas.Add((previewArea, category));
        var columns = GetPreviewColumns(previewArea.Width);
        var contentHeight = GetPreviewContentHeight(itemPaths.Length, columns);
        var maxOffset = Math.Max(0, contentHeight - previewArea.Height);
        var offset = Math.Clamp(_previewScrollOffsets.GetValueOrDefault(category), 0, maxOffset);
        _previewScrollOffsets[category] = offset;

        if (itemPaths.Length == 0)
        {
            DrawCentered(g, "暂无项目", NormalFont, TextMuted, previewArea);
            return;
        }

        var state = g.Save();
        try
        {
            g.SetClip(previewArea);
            for (var i = 0; i < itemPaths.Length; i++)
            {
                var tile = new Rectangle(
                    previewArea.X + i % columns * PreviewTileWidth,
                    previewArea.Y + i / columns * PreviewTileHeight - offset,
                    PreviewTileWidth - 2,
                    PreviewTileHeight - 4);
                if (tile.Bottom < previewArea.Top || tile.Top > previewArea.Bottom)
                {
                    continue;
                }

                if (_draggingItem
                    && ReferenceEquals(category, _dragItemCategory)
                    && string.Equals(itemPaths[i], _dragItemPath, StringComparison.OrdinalIgnoreCase))
                {
                    DrawDragPlaceholder(g, tile);
                    continue;
                }

                if (!_draggingItem
                    && ReferenceEquals(category, _externalDragCategory)
                    && string.Equals(itemPaths[i], _externalDragPreviewPath, StringComparison.OrdinalIgnoreCase))
                {
                    DrawDragPlaceholder(g, tile);
                    continue;
                }

                if (string.Equals(itemPaths[i], _selectedItemPath, StringComparison.OrdinalIgnoreCase))
                {
                    DrawSelectedTile(g, tile);
                }

                DrawAppTile(g, tile, itemPaths[i]);
                _itemAreas.Add((GetAppTileHitRect(tile), tile, category, itemPaths[i], i));
            }
        }
        finally
        {
            g.Restore(state);
        }
    }

    private static Rectangle GetAppTileHitRect(Rectangle tile)
    {
        return tile;
    }

    private (Rectangle Rect, Rectangle Tile, DeskCategory Category, string Path, int Index)? FindItemHit(Point location)
    {
        foreach (var item in _itemAreas)
        {
            if (item.Rect.Contains(location))
            {
                return item;
            }
        }

        return null;
    }

    private void BeginItemDrag(Point location)
    {
        var item = FindItemHit(_pendingItemDragStart);
        if (item is null || _pendingItemDragPath is null)
        {
            _pendingItemDragPath = null;
            Capture = false;
            return;
        }

        ClearItemToolTip();
        var path = _pendingItemDragPath;
        var category = item.Value.Category;
        _dragItemTile = item.Value.Tile;
        _dragItemOffset = new Point(_pendingItemDragStart.X - item.Value.Tile.X, _pendingItemDragStart.Y - item.Value.Tile.Y);
        _dragItemCategory = category;
        _dragItemPath = path;
        _dragItemOriginalPaths = category.ItemPaths.ToList();
        _dragItemInsertIndex = item.Value.Index;
        _dragItemLocation = location;
        _draggingItem = true;
        _pendingItemDragPath = null;
        _suppressNextClick = true;
        Capture = false;
        var data = new DataObject();
        data.SetData(DataFormats.Text, path);
        data.SetData(DataFormats.FileDrop, new[] { path });
        _activeDraggedItemPath = path;
        _handledActiveDraggedItemDrop = false;
        using var preview = new DragPreviewForm(path);
        void MovePreview() => preview.MoveToCursor(Cursor.Position);
        GiveFeedbackEventHandler giveFeedback = (_, e) =>
        {
            e.UseDefaultCursors = true;
            MovePreview();
        };
        QueryContinueDragEventHandler queryContinue = (_, _) => MovePreview();

        try
        {
            preview.Show();
            MovePreview();
            GiveFeedback += giveFeedback;
            QueryContinueDrag += queryContinue;
            var effect = DoDragDrop(data, DragDropEffects.Move | DragDropEffects.Copy);
            if (effect != DragDropEffects.None && !_handledActiveDraggedItemDrop)
            {
                PathRemovedRequested?.Invoke(path);
            }
        }
        finally
        {
            GiveFeedback -= giveFeedback;
            QueryContinueDrag -= queryContinue;
            preview.Close();
            _activeDraggedItemPath = null;
            _handledActiveDraggedItemDrop = false;
            ClearDraggedItemState();
        }

        Invalidate();
    }

    private void ClearDraggedItemState()
    {
        _draggingItem = false;
        _dragItemCategory = null;
        _dragItemPath = null;
        _dragItemOriginalPaths = null;
        _dragItemInsertIndex = null;
        _dragTargetCategoryIndex = -1;
        Capture = false;
    }

    private void UpdateDraggedItemOrder(Point location)
    {
        if (_dragItemCategory is null || _dragItemPath is null)
        {
            return;
        }

        var preview = _previewAreas.FirstOrDefault(item => ReferenceEquals(item.Category, _dragItemCategory));
        if (preview.Rect.IsEmpty || !preview.Rect.Contains(location))
        {
            return;
        }

        var visiblePaths = (_dragItemOriginalPaths ?? _dragItemCategory.ItemPaths)
            .Where(path => File.Exists(path) || Directory.Exists(path))
            .Where(path => !string.Equals(path, _dragItemPath, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var insertIndex = GetItemInsertIndex(preview.Rect, _dragItemCategory, location, visiblePaths.Count);
        if (_dragItemInsertIndex == insertIndex)
        {
            return;
        }

        _dragItemInsertIndex = insertIndex;
    }

    private void EndItemDrag(Point location)
    {
        var category = _dragItemCategory;
        var path = _dragItemPath;
        var categories = Categories();
        var targetCategoryIndex = FindCategoryTabIndex(location);
        var movedToCategory = targetCategoryIndex >= 0
            && targetCategoryIndex < categories.Count
            && !ReferenceEquals(categories[targetCategoryIndex], category);
        var inside = _previewAreas.Any(item => ReferenceEquals(item.Category, category) && item.Rect.Contains(location));

        if (inside)
        {
            ApplyDraggedItemOrder();
        }

        ClearDraggedItemState();

        if (category is not null && path is not null)
        {
            if (targetCategoryIndex >= 0)
            {
                _selectedCategoryIndex = Math.Clamp(targetCategoryIndex, 0, categories.Count - 1);
                if (movedToCategory)
                {
                    PathDroppedRequested?.Invoke(categories[_selectedCategoryIndex], path, null);
                }
            }
            else if (inside)
            {
                ReorderRequested?.Invoke();
            }
            else
            {
                PathRemovedRequested?.Invoke(path);
            }
        }

        Invalidate();
    }

    private void UpdateDragTargetCategory(Point location)
    {
        var targetIndex = FindCategoryTabIndex(location);
        if (_dragTargetCategoryIndex == targetIndex)
        {
            return;
        }

        _dragTargetCategoryIndex = targetIndex;
        Invalidate();
    }

    private int FindCategoryTabIndex(Point location)
    {
        foreach (var item in _categoryTabAreas)
        {
            if (item.Rect.Contains(location))
            {
                return item.Index;
            }
        }

        return -1;
    }

    private int GetItemInsertIndex(Rectangle previewArea, DeskCategory category, Point location, int visibleItemCount)
    {
        var columns = GetPreviewColumns(previewArea.Width);
        var offset = _previewScrollOffsets.GetValueOrDefault(category);
        var localX = Math.Clamp(location.X - previewArea.X, 0, Math.Max(0, previewArea.Width - 1));
        var localY = Math.Max(0, location.Y - previewArea.Y + offset);
        var row = localY / PreviewTileHeight;
        var column = Math.Clamp(localX / PreviewTileWidth, 0, columns - 1);
        var afterHalf = localX % PreviewTileWidth >= PreviewTileWidth / 2;
        var index = row * columns + column + (afterHalf ? 1 : 0);
        return Math.Clamp(index, 0, visibleItemCount);
    }

    private void ApplyDraggedItemOrder()
    {
        if (_dragItemCategory is null || _dragItemPath is null || _dragItemInsertIndex is null)
        {
            return;
        }

        var currentIndex = _dragItemCategory.ItemPaths.FindIndex(path => string.Equals(path, _dragItemPath, StringComparison.OrdinalIgnoreCase));
        if (currentIndex < 0)
        {
            return;
        }

        var visiblePaths = _dragItemCategory.ItemPaths
            .Where(path => File.Exists(path) || Directory.Exists(path))
            .Where(path => !string.Equals(path, _dragItemPath, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var targetVisibleIndex = Math.Clamp(_dragItemInsertIndex.Value, 0, visiblePaths.Count);
        var targetPath = targetVisibleIndex < visiblePaths.Count ? visiblePaths[targetVisibleIndex] : null;

        _dragItemCategory.ItemPaths.RemoveAt(currentIndex);
        var insertIndex = targetPath is null
            ? _dragItemCategory.ItemPaths.Count
            : _dragItemCategory.ItemPaths.FindIndex(path => string.Equals(path, targetPath, StringComparison.OrdinalIgnoreCase));
        _dragItemCategory.ItemPaths.Insert(Math.Clamp(insertIndex, 0, _dragItemCategory.ItemPaths.Count), _dragItemPath);
    }

    private void DrawDraggedItem(Graphics g)
    {
        if (!_draggingItem || _dragItemPath is null)
        {
            return;
        }

        var tile = new Rectangle(
            _dragItemLocation.X - _dragItemOffset.X,
            _dragItemLocation.Y - _dragItemOffset.Y,
            _dragItemTile.Width,
            _dragItemTile.Height);
        var state = g.Save();
        g.CompositingQuality = CompositingQuality.HighQuality;
        DrawAppTile(g, tile, _dragItemPath);
        g.Restore(state);
    }

    private static void DrawDragPlaceholder(Graphics g, Rectangle tile)
    {
    }

    private static void DrawSelectedTile(Graphics g, Rectangle tile)
    {
        var rect = new Rectangle(tile.X + 6, tile.Y + 2, tile.Width - 14, Math.Min(tile.Height - 8, 62));
        FillRound(g, rect, Color.FromArgb(72, 58, 144, 255), 8);
        DrawRound(g, rect, Color.FromArgb(160, 128, 184, 255), 8);
    }

    private void UpdateItemToolTip(Point location)
    {
        var itemHit = FindItemHit(location);
        if (itemHit is null)
        {
            ClearItemToolTip();
            return;
        }

        var path = itemHit.Value.Path;
        if (string.Equals(_hoverToolTipPath, path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _hoverToolTipPath = path;
        _itemToolTip.SetToolTip(this, GetDisplayName(path));
    }

    private void ClearItemToolTip()
    {
        if (_hoverToolTipPath is null)
        {
            return;
        }

        _hoverToolTipPath = null;
        _itemToolTip.SetToolTip(this, string.Empty);
    }

    private bool CanScrollPreview(DeskCategory category, int areaWidth, int areaHeight)
    {
        var columns = GetPreviewColumns(areaWidth);
        return GetPreviewContentHeight(VisiblePreviewItemPaths(category).Count(), columns) > areaHeight;
    }

    private void SetPreviewOffset(DeskCategory category, int areaWidth, int areaHeight, int offset)
    {
        var columns = GetPreviewColumns(areaWidth);
        var maxOffset = Math.Max(0, GetPreviewContentHeight(VisiblePreviewItemPaths(category).Count(), columns) - areaHeight);
        _previewScrollOffsets[category] = Math.Clamp(offset, 0, maxOffset);
        Invalidate();
    }

    private static int GetPreviewColumns(int areaWidth)
    {
        return Math.Max(1, areaWidth / PreviewTileWidth);
    }

    private static int GetPreviewContentHeight(int itemCount, int columns)
    {
        if (itemCount <= 0)
        {
            return 0;
        }

        return (int)Math.Ceiling(itemCount / (double)Math.Max(1, columns)) * PreviewTileHeight;
    }

    private bool CanScrollCategoryTabs(int areaWidth)
    {
        return MeasureCategoryTabsWidth(Categories()) > areaWidth;
    }

    private void SetCategoryTabOffset(int areaWidth, int offset)
    {
        var maxOffset = Math.Max(0, MeasureCategoryTabsWidth(Categories()) - areaWidth);
        _categoryTabScrollOffset = Math.Clamp(offset, 0, maxOffset);
        Invalidate();
    }

    private static int MeasureCategoryTabsWidth(IReadOnlyList<DeskCategory> categories)
    {
        if (categories.Count == 0)
        {
            return 0;
        }

        var width = 0;
        foreach (var category in categories)
        {
            var textWidth = TextRenderer.MeasureText(category.Name, HeaderFont).Width;
            width += Math.Max(76, textWidth + 34) + 14;
        }

        return Math.Max(0, width - 14);
    }

    private void DrawFooter(Graphics g, Rectangle card)
    {
        var y = card.Bottom - 53;
        var left = new Rectangle(card.X + 16, y, (card.Width - 48) / 2, 38);
        var right = new Rectangle(left.Right + 16, y, Math.Max(76, card.Right - left.Right - 58), 38);

        DrawButton(g, left, "整理桌面", Accent, Color.White, true);
        DrawButton(g, right, "添加分类", Color.FromArgb(76, 52, 63, 80), TextMain, false);
        _hotspots.Add((left, "organize"));
        _hotspots.Add((right, "addCategory"));
    }

    private void DrawResizeGrip(Graphics g, Rectangle card)
    {
        _resizeRect = new Rectangle(card.Right - 28, card.Bottom - 28, 24, 24);
        using var pen = new Pen(Color.FromArgb(128, 164, 180, 204), 1.4F);
        for (var i = 0; i < 3; i++)
        {
            var offset = i * 6;
            g.DrawLine(
                pen,
                _resizeRect.Right - 5 - offset,
                _resizeRect.Bottom - 4,
                _resizeRect.Right - 4,
                _resizeRect.Bottom - 5 - offset);
        }

        _hotspots.Add((_resizeRect, "resize"));
    }

    private static void DrawButton(Graphics g, Rectangle rect, string text, Color fill, Color color, bool check)
    {
        FillRound(g, rect, fill, 5);
        var icon = new Rectangle(rect.X + Math.Max(18, rect.Width / 2 - 56), rect.Y + 11, 15, 15);
        using (var pen = new Pen(Color.FromArgb(210, color), 1.5F))
        {
            if (check)
            {
                g.DrawRectangle(pen, icon);
                g.DrawLine(pen, icon.X + 4, icon.Y + 8, icon.X + 7, icon.Y + 11);
                g.DrawLine(pen, icon.X + 7, icon.Y + 11, icon.X + 12, icon.Y + 4);
            }
            else
            {
                g.DrawEllipse(pen, icon);
                g.DrawLine(pen, icon.X + 4, icon.Y + 7, icon.Right - 4, icon.Y + 7);
                g.DrawLine(pen, icon.X + 7, icon.Y + 4, icon.X + 7, icon.Bottom - 4);
            }
        }

        DrawCentered(g, text, NormalFont, color, new Rectangle(rect.X + 22, rect.Y, rect.Width - 22, rect.Height));
    }

    private static void DrawGearIcon(Graphics g, Rectangle rect, Color color)
    {
        using var pen = new Pen(color, 2F);
        var center = new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        g.DrawEllipse(pen, center.X - 8, center.Y - 8, 16, 16);
        g.DrawEllipse(pen, center.X - 3, center.Y - 3, 6, 6);
        for (var i = 0; i < 8; i++)
        {
            var angle = Math.PI * 2 * i / 8;
            var x1 = center.X + (int)(Math.Cos(angle) * 10);
            var y1 = center.Y + (int)(Math.Sin(angle) * 10);
            var x2 = center.X + (int)(Math.Cos(angle) * 13);
            var y2 = center.Y + (int)(Math.Sin(angle) * 13);
            g.DrawLine(pen, x1, y1, x2, y2);
        }
    }

    private static void DrawCloseIcon(Graphics g, Rectangle rect, Color color)
    {
        using var pen = new Pen(color, 2F)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        var inset = 9;
        g.DrawLine(pen, rect.X + inset, rect.Y + inset, rect.Right - inset, rect.Bottom - inset);
        g.DrawLine(pen, rect.Right - inset, rect.Y + inset, rect.X + inset, rect.Bottom - inset);
    }

    private void DrawAppTile(Graphics g, Rectangle tile, string path)
    {
        var name = GetDisplayName(path);
        var iconSize = Math.Clamp(Math.Min(tile.Width - 12, tile.Height - 10), 34, 52);
        var icon = new Rectangle(tile.X + tile.Width / 2 - iconSize / 2, tile.Y + tile.Height / 2 - iconSize / 2, iconSize, iconSize);
        var shellIcon = GetShellIcon(path);
        if (shellIcon is not null)
        {
            g.DrawImage(shellIcon, icon);
        }
        else
        {
            FillRound(g, icon, IconColor(name), 7);
            DrawCentered(g, IconText(name), IconFont, Color.White, icon);
        }
    }

    private static string GetDisplayName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return string.IsNullOrWhiteSpace(name) ? Path.GetFileName(path) : name;
    }

    private static Image? LoadWidgetImage(params string[] parts)
    {
        var relativePath = Path.Combine(parts);
        var roots = new[]
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory()
        };

        foreach (var root in roots)
        {
            var current = new DirectoryInfo(root);
            for (var depth = 0; current is not null && depth < 5; depth++, current = current.Parent)
            {
                var path = Path.Combine(current.FullName, relativePath);
                if (!File.Exists(path))
                {
                    continue;
                }

                using var stream = new MemoryStream(File.ReadAllBytes(path));
                using var image = Image.FromStream(stream);
                return new Bitmap(image);
            }
        }

        return null;
    }

    private static Image TintImage(Image source, Color color)
    {
        using var sourceBitmap = new Bitmap(source);
        var tinted = new Bitmap(sourceBitmap.Width, sourceBitmap.Height);
        for (var y = 0; y < sourceBitmap.Height; y++)
        {
            for (var x = 0; x < sourceBitmap.Width; x++)
            {
                var pixel = sourceBitmap.GetPixel(x, y);
                tinted.SetPixel(x, y, Color.FromArgb(pixel.A * color.A / 255, color.R, color.G, color.B));
            }
        }

        return tinted;
    }

    private Image? GetShellIcon(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return null;
        }

        if (_shellIconCache.TryGetValue(path, out var cached))
        {
            return cached;
        }

        var icon = ShellIconLoader.LoadLargeIcon(path);
        if (icon is not null)
        {
            _shellIconCache[path] = icon;
        }

        return icon;
    }

    private static void DrawAddTile(Graphics g, Rectangle rect)
    {
        var box = new Rectangle(rect.X + rect.Width / 2 - 16, rect.Y + 1, 32, 32);
        using (var pen = new Pen(Color.FromArgb(116, 136, 158), 1F) { DashStyle = DashStyle.Dash })
        {
            using var path = RoundPath(box, 5);
            g.DrawPath(pen, path);
        }

        using (var pen = new Pen(Color.FromArgb(176, 188, 204), 1.4F))
        {
            g.DrawLine(pen, box.X + 10, box.Y + 16, box.Right - 10, box.Y + 16);
            g.DrawLine(pen, box.X + 16, box.Y + 10, box.X + 16, box.Bottom - 10);
        }

        DrawCentered(g, "添加", SmallFont, TextMuted, new Rectangle(rect.X, box.Bottom + 3, rect.Width, 20));
    }

    private static void DrawFolder(Graphics g, Rectangle rect, Color color)
    {
        using var brush = new SolidBrush(color);
        using var path = CreateFolderSvgPath(rect);
        g.FillPath(brush, path);
    }

    private static GraphicsPath CreateFolderSvgPath(Rectangle rect)
    {
        const float minX = 64.512F;
        const float minY = 194.56F;
        const float width = 886.784F;
        const float height = 702.464F;

        var path = new GraphicsPath();
        var current = Map(855.04F, 385.024F);
        var first = current;
        PointF lastControl = default;
        var hasLastControl = false;

        void LineRel(float dx, float dy)
        {
            var target = MapRel(current, dx, dy);
            path.AddLine(current, target);
            current = target;
            hasLastControl = false;
        }

        void QuadRel(float cdx, float cdy, float dx, float dy)
        {
            var control = MapRel(current, cdx, cdy);
            var target = MapRel(current, dx, dy);
            AddQuadratic(path, current, control, target);
            current = target;
            lastControl = control;
            hasLastControl = true;
        }

        void SmoothQuadRel(float dx, float dy)
        {
            var control = hasLastControl
                ? new PointF(2F * current.X - lastControl.X, 2F * current.Y - lastControl.Y)
                : current;
            var target = MapRel(current, dx, dy);
            AddQuadratic(path, current, control, target);
            current = target;
            lastControl = control;
            hasLastControl = true;
        }

        QuadRel(19.456F, 2.048F, 38.912F, 10.24F);
        SmoothQuadRel(33.792F, 23.04F);
        SmoothQuadRel(21.504F, 37.376F);
        SmoothQuadRel(2.048F, 54.272F);
        QuadRel(-2.048F, 8.192F, -8.192F, 40.448F);
        SmoothQuadRel(-14.336F, 74.24F);
        SmoothQuadRel(-18.432F, 86.528F);
        SmoothQuadRel(-19.456F, 76.288F);
        QuadRel(-5.12F, 18.432F, -14.848F, 37.888F);
        SmoothQuadRel(-25.088F, 35.328F);
        SmoothQuadRel(-36.864F, 26.112F);
        SmoothQuadRel(-51.2F, 10.24F);
        LineRel(-567.296F, 0F);
        QuadRel(-21.504F, 0F, -44.544F, -9.216F);
        SmoothQuadRel(-42.496F, -26.112F);
        SmoothQuadRel(-31.744F, -40.96F);
        SmoothQuadRel(-12.288F, -53.76F);
        LineRel(0F, -439.296F);
        QuadRel(0F, -62.464F, 33.792F, -97.792F);
        SmoothQuadRel(95.232F, -35.328F);
        LineRel(503.808F, 0F);
        QuadRel(22.528F, 0F, 46.592F, 8.704F);
        SmoothQuadRel(43.52F, 24.064F);
        SmoothQuadRel(31.744F, 35.84F);
        SmoothQuadRel(12.288F, 44.032F);
        LineRel(0F, 11.264F);
        LineRel(-53.248F, 0F);
        QuadRel(-40.96F, 0F, -95.744F, -0.512F);
        SmoothQuadRel(-116.736F, -0.512F);
        SmoothQuadRel(-115.712F, -0.512F);
        SmoothQuadRel(-92.672F, -0.512F);
        LineRel(-47.104F, 0F);
        QuadRel(-26.624F, 0F, -41.472F, 16.896F);
        SmoothQuadRel(-23.04F, 44.544F);
        QuadRel(-8.192F, 29.696F, -18.432F, 62.976F);
        SmoothQuadRel(-18.432F, 61.952F);
        QuadRel(-10.24F, 33.792F, -20.48F, 65.536F);
        QuadRel(-2.048F, 8.192F, -2.048F, 13.312F);
        QuadRel(0F, 17.408F, 11.776F, 29.184F);
        SmoothQuadRel(29.184F, 11.776F);
        QuadRel(31.744F, 0F, 43.008F, -39.936F);
        LineRel(54.272F, -198.656F);
        QuadRel(133.12F, 1.024F, 243.712F, 1.024F);
        LineRel(286.72F, 0F);
        path.AddLine(current, first);
        path.CloseFigure();
        return path;

        PointF Map(float svgX, float svgY)
        {
            return new PointF(
                rect.X + (svgX - minX) / width * rect.Width,
                rect.Y + (svgY - minY) / height * rect.Height);
        }

        PointF MapRel(PointF from, float dx, float dy)
        {
            return new PointF(
                from.X + dx / width * rect.Width,
                from.Y + dy / height * rect.Height);
        }
    }

    private static void AddQuadratic(GraphicsPath path, PointF start, PointF control, PointF end)
    {
        var c1 = new PointF(start.X + (control.X - start.X) * 2F / 3F, start.Y + (control.Y - start.Y) * 2F / 3F);
        var c2 = new PointF(end.X + (control.X - end.X) * 2F / 3F, end.Y + (control.Y - end.Y) * 2F / 3F);
        path.AddBezier(start, c1, c2, end);
    }

    private static DeskCategory[] DefaultCategories()
    {
        return new[]
        {
            new DeskCategory { Name = "工作" },
            new DeskCategory { Name = "设计" },
            new DeskCategory { Name = "工具" },
            new DeskCategory { Name = "娱乐" }
        };
    }

    private static IEnumerable<string> PreviewItems(DeskCategory category)
    {
        return PreviewItemPaths(category)
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>();
    }

    private IEnumerable<string> VisiblePreviewItemPaths(DeskCategory category)
    {
        if (!_draggingItem
            && ReferenceEquals(category, _externalDragCategory)
            && _externalDragInsertIndex is not null
            && !string.IsNullOrWhiteSpace(_externalDragPreviewPath))
        {
            var externalPaths = PreviewItemPaths(category)
                .Where(path => !string.Equals(path, _externalDragPreviewPath, StringComparison.OrdinalIgnoreCase))
                .ToList();
            externalPaths.Insert(Math.Clamp(_externalDragInsertIndex.Value, 0, externalPaths.Count), _externalDragPreviewPath);
            return externalPaths;
        }

        if (!_draggingItem || !ReferenceEquals(category, _dragItemCategory) || _dragItemOriginalPaths is null || _dragItemPath is null)
        {
            return PreviewItemPaths(category);
        }

        var paths = _dragItemOriginalPaths
            .Where(path => File.Exists(path) || Directory.Exists(path))
            .ToList();
        var originalIndex = paths.FindIndex(path => string.Equals(path, _dragItemPath, StringComparison.OrdinalIgnoreCase));
        paths.RemoveAll(path => string.Equals(path, _dragItemPath, StringComparison.OrdinalIgnoreCase));
        var insertIndex = _dragItemInsertIndex ?? Math.Max(0, originalIndex);
        paths.Insert(Math.Clamp(insertIndex, 0, paths.Count), _dragItemPath);
        return paths;
    }

    private static IEnumerable<string> PreviewItemPaths(DeskCategory category)
    {
        return category.ItemPaths
            .Where(path => File.Exists(path) || Directory.Exists(path))
            .Cast<string>();
    }

    private static string? GetDroppedPath(DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true && e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            return files[0];
        }

        return e.Data?.GetDataPresent(DataFormats.Text) == true && e.Data.GetData(DataFormats.Text) is string path
            ? path
            : null;
    }

    private static string GetUniquePath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var fileName = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        for (var index = 2; ; index++)
        {
            var candidate = Path.Combine(directory, $"{fileName} ({index}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var cleaned = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "未命名" : cleaned;
    }

    private static void OpenPath(string path)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                MessageBox.Show("路径不存在。", "DustDesk", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static Color CategoryColor(string name, int index)
    {
        var palette = new[]
        {
            Color.FromArgb(255, 217, 71),
            Color.FromArgb(88, 145, 255),
            Color.FromArgb(255, 93, 92),
            Color.FromArgb(255, 179, 59),
            Color.FromArgb(97, 172, 255),
            Color.FromArgb(78, 202, 142),
            Color.FromArgb(225, 73, 194)
        };

        return index >= 0
            ? palette[index % palette.Length]
            : name switch
        {
            "工作" => Color.FromArgb(255, 217, 71),
            "设计" => Color.FromArgb(225, 73, 194),
            "工具" => Color.FromArgb(255, 179, 59),
            "娱乐" or "游戏" => Color.FromArgb(255, 93, 92),
            "开发" => Color.FromArgb(88, 145, 255),
            _ => Color.FromArgb(97, 172, 255)
        };
    }

    private static Color IconColor(string name)
    {
        if (name.Contains("Excel", StringComparison.OrdinalIgnoreCase)) return Color.FromArgb(48, 151, 89);
        if (name.Contains("Word", StringComparison.OrdinalIgnoreCase)) return Color.FromArgb(75, 112, 220);
        if (name.Contains("WPS", StringComparison.OrdinalIgnoreCase)) return Color.FromArgb(232, 76, 61);
        if (name.Contains("Photoshop", StringComparison.OrdinalIgnoreCase)) return Color.FromArgb(27, 84, 145);
        if (name.Contains("Figma", StringComparison.OrdinalIgnoreCase)) return Color.FromArgb(238, 91, 73);
        if (name.Contains("Sketch", StringComparison.OrdinalIgnoreCase)) return Color.FromArgb(255, 197, 62);
        if (name.Contains("Canva", StringComparison.OrdinalIgnoreCase)) return Color.FromArgb(71, 190, 222);
        if (name.Contains("Everything", StringComparison.OrdinalIgnoreCase)) return Color.FromArgb(243, 150, 58);
        if (name.Contains("Bandizip", StringComparison.OrdinalIgnoreCase)) return Color.FromArgb(77, 116, 230);
        if (name.Contains("Steam", StringComparison.OrdinalIgnoreCase)) return Color.FromArgb(43, 75, 139);
        if (name.Contains("QQ", StringComparison.OrdinalIgnoreCase)) return Color.FromArgb(209, 217, 52);
        if (name.Contains("网易", StringComparison.OrdinalIgnoreCase)) return Color.FromArgb(231, 67, 58);

        var hash = Math.Abs(name.GetHashCode());
        var colors = new[]
        {
            Color.FromArgb(58, 121, 245),
            Color.FromArgb(52, 181, 120),
            Color.FromArgb(238, 91, 78),
            Color.FromArgb(244, 174, 61),
            Color.FromArgb(138, 102, 238)
        };
        return colors[hash % colors.Length];
    }

    private static string IconText(string name)
    {
        if (name.Contains("VS", StringComparison.OrdinalIgnoreCase)) return "V";
        if (name.Contains("Excel", StringComparison.OrdinalIgnoreCase)) return "X";
        if (name.Contains("Word", StringComparison.OrdinalIgnoreCase)) return "W";
        if (name.Contains("Photoshop", StringComparison.OrdinalIgnoreCase)) return "Ps";
        if (name.Contains("Figma", StringComparison.OrdinalIgnoreCase)) return "F";
        if (name.Contains("Sketch", StringComparison.OrdinalIgnoreCase)) return "S";
        if (name.Contains("Everything", StringComparison.OrdinalIgnoreCase)) return "Q";
        if (name.Contains("Steam", StringComparison.OrdinalIgnoreCase)) return "S";
        return string.IsNullOrWhiteSpace(name) ? "+" : name.Trim()[0].ToString().ToUpperInvariant();
    }

    private static string TrimText(string text, int length)
    {
        return text.Length <= length ? text : text[..length];
    }

    private static void FillRound(Graphics g, Rectangle rect, Color color, int radius)
    {
        using var brush = new SolidBrush(color);
        using var path = RoundPath(rect, radius);
        g.FillPath(brush, path);
    }

    private static void DrawRound(Graphics g, Rectangle rect, Color color, int radius)
    {
        using var pen = new Pen(color);
        using var path = RoundPath(rect, radius);
        g.DrawPath(pen, path);
    }

    private static GraphicsPath RoundPath(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        rect.Width -= 1;
        rect.Height -= 1;
        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void DrawText(Graphics g, string text, Font font, Color color, int x, int y)
    {
        TextRenderer.DrawText(g, text, font, new Point(x, y), color, TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping);
    }

    private static void DrawCentered(Graphics g, string text, Font font, Color color, Rectangle rect)
    {
        TextRenderer.DrawText(g, text, font, rect, color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping);
    }
}

internal sealed class SidebarMenu : Control
{
    private readonly List<string> _items = new();
    private int _selectedIndex = -1;
    private int _hoverIndex = -1;

    public SidebarMenu()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
    }

    public IReadOnlyDictionary<int, Image> Icons { get; set; } = new Dictionary<int, Image>();
    public Color SelectedColor { get; set; } = Color.FromArgb(35, 107, 238);
    public int ItemHeight { get; set; } = 48;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (_selectedIndex == value)
            {
                return;
            }

            _selectedIndex = value;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? SelectedIndexChanged;

    public void SetItems(IEnumerable<string> items)
    {
        _items.Clear();
        _items.AddRange(items);
        if (_items.Count > 0 && SelectedIndex < 0)
        {
            _selectedIndex = 0;
        }
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var background = new SolidBrush(BackColor);
        e.Graphics.FillRectangle(background, ClientRectangle);

        for (var i = 0; i < _items.Count; i++)
        {
            var row = new Rectangle(0, i * ItemHeight, Width, ItemHeight);
            var selected = i == SelectedIndex;
            var hot = i == _hoverIndex;

            if (selected || hot)
            {
                using var brush = new SolidBrush(selected ? SelectedColor : Color.FromArgb(28, 44, 60));
                e.Graphics.FillRoundedRectangle(brush, new Rectangle(row.X + 1, row.Y + 8, row.Width - 2, row.Height - 16), 7);
            }

            if (Icons.TryGetValue(i, out var icon))
            {
                e.Graphics.DrawImage(icon, new Rectangle(row.X + 16, row.Y + 18, 20, 20));
            }

            TextRenderer.DrawText(
                e.Graphics,
                _items[i],
                Font,
                new Rectangle(row.X + 48, row.Y + 1, row.Width - 52, row.Height),
                selected ? Color.White : ForeColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var index = HitTest(e.Location);
        if (_hoverIndex != index)
        {
            _hoverIndex = index;
            Invalidate();
        }
        Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hoverIndex = -1;
        Cursor = Cursors.Default;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            var index = HitTest(e.Location);
            if (index >= 0)
            {
                SelectedIndex = index;
            }
        }

        base.OnMouseDown(e);
    }

    private int HitTest(Point point)
    {
        var index = point.Y / ItemHeight;
        return point.X >= 0 && point.X < Width && index >= 0 && index < _items.Count ? index : -1;
    }
}

internal sealed record QuickSearchEntry(string Title, string Type, string Subtitle, Action Open);

internal sealed class DoubleBufferedListBox : ListBox
{
    public DoubleBufferedListBox()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        UpdateStyles();
    }
}

internal sealed class QuickSearchForm : Form
{
    private readonly List<QuickSearchEntry> _entries;
    private readonly TextBox _input = new();
    private readonly ListView _list = new();

    private static readonly Color Back = Color.FromArgb(24, 34, 48);
    private static readonly Color Panel = Color.FromArgb(36, 48, 64);
    private static readonly Color Border = Color.FromArgb(78, 98, 122);
    private static readonly Color TextMain = Color.FromArgb(240, 245, 252);
    private static readonly Color TextSubtle = Color.FromArgb(154, 169, 188);

    public QuickSearchForm(IEnumerable<QuickSearchEntry> entries)
    {
        _entries = entries.ToList();
        Text = "快速检索";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Size = new Size(760, 520);
        BackColor = Back;
        ForeColor = TextMain;
        Font = new Font("Microsoft YaHei UI", 9.5F);
        Padding = new Padding(16);
        KeyPreview = true;

        _input.Dock = DockStyle.Top;
        _input.Height = 38;
        _input.BorderStyle = BorderStyle.FixedSingle;
        _input.BackColor = Panel;
        _input.ForeColor = TextMain;
        _input.Font = new Font(Font.FontFamily, 13F);
        _input.Margin = new Padding(0, 0, 0, 12);
        _input.TextChanged += (_, _) => RefreshResults();
        _input.KeyDown += InputKeyDown;

        _list.Dock = DockStyle.Fill;
        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.HideSelection = false;
        _list.MultiSelect = false;
        _list.BorderStyle = BorderStyle.FixedSingle;
        _list.BackColor = Panel;
        _list.ForeColor = TextMain;
        _list.HeaderStyle = ColumnHeaderStyle.None;
        _list.Columns.Add("名称", 300);
        _list.Columns.Add("类型", 92);
        _list.Columns.Add("位置", 330);
        _list.DoubleClick += (_, _) => OpenSelected();
        _list.KeyDown += ListKeyDown;

        var hint = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 28,
            ForeColor = TextSubtle,
            Text = "输入关键词过滤，Enter 打开，Esc 关闭",
            TextAlign = ContentAlignment.BottomLeft
        };

        var host = new Panel { Dock = DockStyle.Fill, BackColor = Back };
        host.Controls.Add(_list);
        host.Controls.Add(_input);
        host.Controls.Add(hint);
        Controls.Add(host);

        Paint += (_, e) =>
        {
            using var pen = new Pen(Border);
            e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        };
        Shown += (_, _) =>
        {
            _input.Focus();
            RefreshResults();
        };
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            Close();
            e.SuppressKeyPress = true;
            return;
        }

        base.OnKeyDown(e);
    }

    private void InputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            OpenFirst();
            e.SuppressKeyPress = true;
            return;
        }

        if (e.KeyCode == Keys.Down && _list.Items.Count > 0)
        {
            _list.Focus();
            _list.Items[0].Selected = true;
            e.SuppressKeyPress = true;
        }
    }

    private void ListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            OpenSelected();
            e.SuppressKeyPress = true;
            return;
        }

        if (e.KeyCode == Keys.Back)
        {
            _input.Focus();
            if (_input.Text.Length > 0)
            {
                _input.Text = _input.Text[..^1];
                _input.SelectionStart = _input.Text.Length;
            }
            e.SuppressKeyPress = true;
        }
    }

    private void RefreshResults()
    {
        var query = _input.Text.Trim();
        var items = string.IsNullOrWhiteSpace(query)
            ? _entries.Take(80)
            : _entries
                .Select(entry => (Entry: entry, Score: MatchScore(entry, query)))
                .Where(item => item.Score >= 0)
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.Entry.Title, StringComparer.CurrentCultureIgnoreCase)
                .Take(80)
                .Select(item => item.Entry);

        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            foreach (var entry in items)
            {
                var item = new ListViewItem(entry.Title) { Tag = entry };
                item.SubItems.Add(entry.Type);
                item.SubItems.Add(entry.Subtitle);
                _list.Items.Add(item);
            }

            if (_list.Items.Count > 0)
            {
                _list.Items[0].Selected = true;
            }
        }
        finally
        {
            _list.EndUpdate();
        }
    }

    public static int MatchScore(QuickSearchEntry entry, string query)
    {
        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var searchable = $"{entry.Title} {entry.Type} {entry.Subtitle}";
        if (tokens.Any(token => searchable.IndexOf(token, StringComparison.CurrentCultureIgnoreCase) < 0))
        {
            return -1;
        }

        var score = 0;
        foreach (var token in tokens)
        {
            if (entry.Title.StartsWith(token, StringComparison.CurrentCultureIgnoreCase))
            {
                score += 100;
            }
            else if (entry.Title.IndexOf(token, StringComparison.CurrentCultureIgnoreCase) >= 0)
            {
                score += 60;
            }
            else if (entry.Type.IndexOf(token, StringComparison.CurrentCultureIgnoreCase) >= 0)
            {
                score += 30;
            }
            else
            {
                score += 10;
            }
        }

        return score;
    }

    private void OpenFirst()
    {
        if (_list.Items.Count > 0)
        {
            _list.Items[0].Selected = true;
            OpenSelected();
        }
    }

    private void OpenSelected()
    {
        var entry = _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as QuickSearchEntry : null;
        if (entry is null)
        {
            return;
        }

        Close();
        entry.Open();
    }
}

internal sealed class DesktopSearchWidgetForm : Form
{
    private const int WsExToolWindow = 0x00000080;
    private const int WsExAppWindow = 0x00040000;
    private const int VisibleInset = 24;
    private const int CapsuleHeight = 44;
    private const int ShadowInset = 8;
    private const int SearchIconAreaWidth = 52;
    private const int ClearButtonSize = 28;

    private readonly Func<List<QuickSearchEntry>> _entryProvider;
    private readonly Action<Rectangle> _placementChanged;
    private readonly Func<bool> _transparentProvider;
    private readonly TextBox _input = new();
    private readonly ListBox _results = new DoubleBufferedListBox();
    private readonly ContextMenuStrip _menu = new() { ShowImageMargin = false };
    private readonly ContextMenuStrip _resultMenu = new() { ShowImageMargin = false };
    private readonly System.Windows.Forms.Timer _resultScrollTimer = new() { Interval = 45 };
    private List<QuickSearchEntry> _entries = new();
    private bool _attachedToDesktop;
    private bool _restoringPlacement;
    private int _hoverResultIndex = -1;
    private int _hoverResultOffset;
    private Rectangle _capsuleRect;
    private Rectangle _clearRect;
    private bool TransparentStyle => _transparentProvider();
    private Color SearchBackColor => TransparentStyle ? Color.FromArgb(180, 192, 205) : Color.FromArgb(245, 250, 255);
    private Color SearchFillColor => TransparentStyle ? Color.FromArgb(180, 192, 205) : Color.FromArgb(245, 250, 255);
    private Color SearchPanelFillColor => TransparentStyle ? Color.FromArgb(190, 202, 214) : SearchFillColor;
    private Color SearchTextColor => Color.FromArgb(18, 25, 38);
    private Color SearchSubtleColor => TransparentStyle ? Color.FromArgb(72, 86, 104) : Color.FromArgb(68, 84, 104);
    private Color SearchSelectedColor => TransparentStyle ? Color.FromArgb(205, 216, 226) : Color.FromArgb(225, 235, 247, 255);
    private Color SearchBorderColor => TransparentStyle ? Color.FromArgb(132, 150, 194, 238) : Color.FromArgb(170, 202, 232);
    private Color SearchFocusedBorderColor => TransparentStyle ? Color.FromArgb(132, 150, 194, 238) : Color.FromArgb(112, 170, 255);
    private Color SearchIconBackColor => TransparentStyle ? Color.FromArgb(130, 180, 255) : Color.FromArgb(94, 116, 142);
    private Color SearchChipBackColor => TransparentStyle ? Color.FromArgb(44, 10, 22, 36) : Color.FromArgb(218, 230, 242);

    public DesktopSearchWidgetForm(Func<List<QuickSearchEntry>> entryProvider, Action<Rectangle> placementChanged, Func<bool> transparentProvider)
    {
        _entryProvider = entryProvider;
        _placementChanged = placementChanged;
        _transparentProvider = transparentProvider;
        Text = "快速搜索";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        ShowIcon = false;
        StartPosition = FormStartPosition.Manual;
        MinimumSize = new Size(280, 64);
        MaximumSize = new Size(900, 620);
        Size = new Size(460, 64);
        BackColor = SearchBackColor;
        Font = new Font("Microsoft YaHei UI", 9F);
        Padding = Padding.Empty;

        _input.BorderStyle = BorderStyle.None;
        _input.BackColor = SearchBackColor;
        _input.ForeColor = SearchTextColor;
        _input.Font = new Font(Font.FontFamily, 12F);
        _input.AutoSize = false;
        _input.TextChanged += (_, _) =>
        {
            LayoutInput();
            RefreshResults();
        };
        _input.KeyDown += InputKeyDown;
        _input.GotFocus += (_, _) => Invalidate();
        _input.LostFocus += (_, _) => Invalidate();
        _input.MouseDown += (_, _) => FocusSearchInput();
        _input.Click += (_, _) => FocusSearchInput();

        _results.Visible = false;
        _results.IntegralHeight = false;
        _results.BorderStyle = BorderStyle.None;
        _results.BackColor = SearchBackColor;
        _results.ForeColor = SearchTextColor;
        _results.DrawMode = DrawMode.OwnerDrawFixed;
        _results.ItemHeight = 42;
        _results.Font = new Font(Font.FontFamily, 9F);
        _results.MouseDoubleClick += (_, _) => OpenSelectedResult();
        _results.MouseDown += ResultsMouseDown;
        _results.MouseMove += ResultsMouseMove;
        _results.MouseLeave += (_, _) => SetHoverResult(-1);
        _results.KeyDown += ResultKeyDown;
        _results.DrawItem += DrawResultItem;
        _results.ContextMenuStrip = _resultMenu;

        _menu.Items.Add("刷新", null, (_, _) => ReloadEntries());
        _menu.Items.Add("关闭", null, (_, _) => Close());
        ContextMenuStrip = _menu;

        _resultMenu.Items.Add("打开路径", null, (_, _) => OpenSelectedResultLocation());
        _resultMenu.Opening += (_, e) =>
        {
            var entry = _results.SelectedItem as QuickSearchEntry;
            var canOpenLocation = TryGetEntryPath(entry, out var _);
            _resultMenu.Items[0].Enabled = canOpenLocation;
            e.Cancel = entry is null;
        };
        _resultScrollTimer.Tick += (_, _) =>
        {
            if (_hoverResultIndex < 0 || _hoverResultIndex >= _results.Items.Count)
            {
                _resultScrollTimer.Stop();
                return;
            }

            if (!ResultTitleOverflows(_hoverResultIndex))
            {
                _resultScrollTimer.Stop();
                return;
            }

            _hoverResultOffset += 2;
            _results.Invalidate(_results.GetItemRectangle(_hoverResultIndex));
        };

        Controls.Add(_input);
        Controls.Add(_results);
        LocationChanged += (_, _) => SavePlacement();
        SizeChanged += (_, _) =>
        {
            UpdateRoundedRegion();
            LayoutInput();
            SavePlacement();
        };
        Shown += (_, _) =>
        {
            ReloadEntries();
            RefreshResults();
            BeginInvoke(new Action(FocusSearchInput));
        };
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        ApplySearchStyle();
        LayoutInput();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WsExToolWindow;
            cp.ExStyle &= ~WsExAppWindow;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UpdateRoundedRegion();
    }

    public void ShowAsDesktopWidget(WidgetPlacement? placement = null)
    {
        var work = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(80, 80, 1280, 720);
        var width = placement?.Width > 0 ? placement.Width : 460;
        var height = 64;
        var x = placement?.X != 0 ? placement!.X : work.Right - width - 80;
        var y = placement?.Y != 0 ? placement!.Y : work.Top + 90;
        var target = NormalizeScreenBounds(new Rectangle(x, y, width, height));
        _restoringPlacement = true;
        try
        {
            Bounds = target;
        }
        finally
        {
            _restoringPlacement = false;
        }

        ApplySearchStyle();
        Show();
        SetScreenBounds(target);
        BringToFront();
        Activate();
        FocusSearchInput();
        UpdateRoundedRegion();
        ReloadEntries();
        SavePlacement();
    }

    public void RefreshSearch()
    {
        ApplySearchStyle();
        ReloadEntries();
        RefreshResults();
        Invalidate(true);
    }

    private void ReloadEntries()
    {
        _entries = _entryProvider();
    }

    private void ApplySearchStyle()
    {
        Opacity = 1.0;
        BackColor = SearchBackColor;
        _input.BackColor = SearchBackColor;
        _input.ForeColor = SearchTextColor;
        _results.BackColor = SearchBackColor;
        _results.ForeColor = SearchTextColor;
        if (IsHandleCreated)
        {
            NativeGlass.DisableAcrylic(Handle);
        }

        UpdateRoundedRegion();
    }

    private void InputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            OpenBestMatch();
            e.SuppressKeyPress = true;
            return;
        }

        if (e.KeyCode == Keys.Down && _results.Visible && _results.Items.Count > 0)
        {
            _results.Focus();
            _results.SelectedIndex = Math.Max(0, _results.SelectedIndex);
            e.SuppressKeyPress = true;
            return;
        }

        if (e.KeyCode == Keys.Escape)
        {
            _input.Clear();
            SetResultsVisible(false);
            e.SuppressKeyPress = true;
        }
    }

    private void ResultKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            OpenSelectedResult();
            e.SuppressKeyPress = true;
            return;
        }

        if (e.KeyCode == Keys.Escape)
        {
            _input.Focus();
            _input.Clear();
            SetResultsVisible(false);
            e.SuppressKeyPress = true;
        }
    }

    private void ResultsMouseDown(object? sender, MouseEventArgs e)
    {
        var index = _results.IndexFromPoint(e.Location);
        if (index >= 0)
        {
            _results.SelectedIndex = index;
        }

        if (e.Button == MouseButtons.Right)
        {
            _results.Focus();
        }
    }

    private void ResultsMouseMove(object? sender, MouseEventArgs e)
    {
        SetHoverResult(_results.IndexFromPoint(e.Location));
    }

    private void SetHoverResult(int index)
    {
        if (_hoverResultIndex == index)
        {
            return;
        }

        var oldIndex = _hoverResultIndex;
        _hoverResultIndex = index;
        _hoverResultOffset = 0;
        _resultScrollTimer.Enabled = index >= 0 && ResultTitleOverflows(index);
        if (oldIndex >= 0 && oldIndex < _results.Items.Count)
        {
            _results.Invalidate(_results.GetItemRectangle(oldIndex));
        }

        if (index >= 0 && index < _results.Items.Count)
        {
            _results.Invalidate(_results.GetItemRectangle(index));
        }
    }

    private void RefreshResults()
    {
        var query = _input.Text.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            _results.Items.Clear();
            SetResultsVisible(false);
            return;
        }

        if (_entries.Count == 0)
        {
            ReloadEntries();
        }

        var items = _entries
            .Select(item => (Entry: item, Score: QuickSearchForm.MatchScore(item, query)))
            .Where(item => item.Score >= 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Entry.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(8)
            .Select(item => item.Entry)
            .ToList();

        _results.BeginUpdate();
        try
        {
        _results.Items.Clear();
            foreach (var entry in items)
            {
                _results.Items.Add(entry);
            }

            if (_results.Items.Count > 0)
            {
                _results.SelectedIndex = 0;
            }
        }
        finally
        {
            _results.EndUpdate();
        }

        SetResultsVisible(_results.Items.Count > 0);
        SetHoverResult(-1);
    }

    private void OpenBestMatch()
    {
        var query = _input.Text.Trim();
        var entry = _results.Visible && _results.SelectedItem is QuickSearchEntry selected
            ? selected
            : string.IsNullOrWhiteSpace(query)
                ? null
                : _entries
                    .Select(item => (Entry: item, Score: QuickSearchForm.MatchScore(item, query)))
                    .Where(item => item.Score >= 0)
                    .OrderByDescending(item => item.Score)
                    .ThenBy(item => item.Entry.Title, StringComparer.CurrentCultureIgnoreCase)
                    .Select(item => item.Entry)
                    .FirstOrDefault();

        if (entry is not null)
        {
            entry.Open();
        }
    }

    private void OpenSelectedResult()
    {
        if (_results.SelectedItem is QuickSearchEntry entry)
        {
            entry.Open();
        }
    }

    private void OpenSelectedResultLocation()
    {
        if (!TryGetEntryPath(_results.SelectedItem as QuickSearchEntry, out var path))
        {
            return;
        }

        try
        {
            if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                return;
            }

            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "打开失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static bool TryGetEntryPath(QuickSearchEntry? entry, out string path)
    {
        path = entry?.Subtitle ?? "";
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            path = Path.GetFullPath(path);
            return File.Exists(path) || Directory.Exists(path);
        }
        catch
        {
            path = "";
            return false;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        _capsuleRect = new Rectangle(ShadowInset, 8, Math.Max(1, Width - ShadowInset * 2), CapsuleHeight);

        if (!TransparentStyle)
        {
            using var shadowBrush = new SolidBrush(Color.FromArgb(70, 16, 24, 36));
            using var shadowPath = RoundPath(new Rectangle(_capsuleRect.X + 2, _capsuleRect.Y + 4, _capsuleRect.Width - 4, _capsuleRect.Height), _capsuleRect.Height / 2);
            g.FillPath(shadowBrush, shadowPath);
        }

        using (var fillBrush = new SolidBrush(SearchFillColor))
        using (var fillPath = RoundPath(_capsuleRect, _capsuleRect.Height / 2))
        {
            g.FillPath(fillBrush, fillPath);
        }

        var iconArea = new Rectangle(_capsuleRect.X, _capsuleRect.Y, 52, _capsuleRect.Height);
        using (var iconBrush = new SolidBrush(SearchIconBackColor))
        using (var iconPath = RoundPath(iconArea, iconArea.Height / 2))
        {
            g.FillPath(iconBrush, iconPath);
        }

        if (!TransparentStyle)
        {
            using var pen = new Pen(_input.Focused ? SearchFocusedBorderColor : SearchBorderColor, 1.4F);
            using var borderPath = RoundPath(_capsuleRect, _capsuleRect.Height / 2);
            g.DrawPath(pen, borderPath);
        }

        DrawSearchIcon(g, new Rectangle(_capsuleRect.X + 17, _capsuleRect.Y + 13, 18, 18), Color.FromArgb(238, 246, 255));

        if (!string.IsNullOrEmpty(_input.Text))
        {
            using var clearFill = new SolidBrush(Color.FromArgb(224, 64, 74));
            using var clearPath = RoundPath(_clearRect, 8);
            g.FillPath(clearFill, clearPath);
            using var clearPen = new Pen(Color.White, 1.8F)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            g.DrawLine(clearPen, _clearRect.X + 9, _clearRect.Y + 9, _clearRect.Right - 9, _clearRect.Bottom - 9);
            g.DrawLine(clearPen, _clearRect.Right - 9, _clearRect.Y + 9, _clearRect.X + 9, _clearRect.Bottom - 9);
        }

        if (_results.Visible)
        {
            var dropdown = GetResultsPanelRect();
            using var shadow = new SolidBrush(Color.FromArgb(52, 16, 24, 36));
            using var shadowPath = RoundPath(new Rectangle(dropdown.X + 2, dropdown.Y + 4, dropdown.Width - 4, dropdown.Height), 12);
            g.FillPath(shadow, shadowPath);

            using var fill = new SolidBrush(SearchPanelFillColor);
            using var fillPath = RoundPath(dropdown, 12);
            g.FillPath(fill, fillPath);

            using var border = new Pen(SearchBorderColor, 1.2F);
            g.DrawPath(border, fillPath);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && !_clearRect.IsEmpty && _clearRect.Contains(e.Location) && !string.IsNullOrEmpty(_input.Text))
        {
            _input.Clear();
            FocusSearchInput();
            return;
        }

        var iconArea = new Rectangle(_capsuleRect.X, _capsuleRect.Y, SearchIconAreaWidth, _capsuleRect.Height);
        if (e.Button == MouseButtons.Left && iconArea.Contains(e.Location))
        {
            NativeGlass.BeginMove(Handle);
            return;
        }

        if (e.Button == MouseButtons.Left && _capsuleRect.Contains(e.Location))
        {
            FocusSearchInput();
            return;
        }

        base.OnMouseDown(e);
    }

    private void FocusSearchInput()
    {
        if (!IsHandleCreated || !_input.IsHandleCreated)
        {
            return;
        }

        Activate();
        _input.Focus();
        NativeGlass.FocusInput(Handle, _input.Handle);
    }

    private void SavePlacement()
    {
        if (!_restoringPlacement && Visible && WindowState == FormWindowState.Normal)
        {
            var bounds = GetScreenBounds();
            bounds.Height = 64;
            _placementChanged(NormalizeScreenBounds(bounds));
        }
    }

    private void AttachToDesktopHost()
    {
        if (_attachedToDesktop || !IsHandleCreated)
        {
            return;
        }

        _attachedToDesktop = NativeGlass.AttachToDesktop(Handle);
    }

    private Rectangle GetScreenBounds()
    {
        return IsHandleCreated ? RectangleToScreen(ClientRectangle) : Bounds;
    }

    private void SetScreenBounds(Rectangle bounds)
    {
        Bounds = bounds;
    }

    private Rectangle NormalizeScreenBounds(Rectangle bounds)
    {
        var workArea = Screen.FromRectangle(bounds).WorkingArea;
        var maxWidth = Math.Max(MinimumSize.Width, workArea.Width - VisibleInset * 2);
        var maxHeight = Math.Max(MinimumSize.Height, workArea.Height - VisibleInset * 2);
        var width = Math.Clamp(bounds.Width, MinimumSize.Width, maxWidth);
        var height = Math.Clamp(64, MinimumSize.Height, maxHeight);
        var minX = workArea.Left + VisibleInset;
        var minY = workArea.Top + VisibleInset;
        var maxX = Math.Max(minX, workArea.Right - width - VisibleInset);
        var maxY = Math.Max(minY, workArea.Bottom - height - VisibleInset);
        return new Rectangle(Math.Clamp(bounds.X, minX, maxX), Math.Clamp(bounds.Y, minY, maxY), width, height);
    }

    private void LayoutInput()
    {
        _capsuleRect = new Rectangle(ShadowInset, 8, Math.Max(1, Width - ShadowInset * 2), CapsuleHeight);
        var inputX = _capsuleRect.X + SearchIconAreaWidth + 12;
        const int inputHeight = 28;
        var showClear = !string.IsNullOrEmpty(_input.Text);
        _clearRect = showClear
            ? new Rectangle(_capsuleRect.Right - ClearButtonSize - 10, _capsuleRect.Y + (_capsuleRect.Height - ClearButtonSize) / 2, ClearButtonSize, ClearButtonSize)
            : Rectangle.Empty;
        var inputRight = showClear ? _clearRect.Left - 10 : _capsuleRect.Right - 16;
        _input.SetBounds(inputX, _capsuleRect.Y + (_capsuleRect.Height - inputHeight) / 2 + 1, Math.Max(1, inputRight - inputX), inputHeight);
        var resultsRect = GetResultsListRect();
        _results.SetBounds(resultsRect.X, resultsRect.Y, resultsRect.Width, resultsRect.Height);
        Invalidate();
    }

    private void UpdateRoundedRegion()
    {
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0)
        {
            return;
        }

        var topRegion = TransparentStyle
            ? new Rectangle(ShadowInset, 8, Math.Max(1, ClientSize.Width - ShadowInset * 2), CapsuleHeight)
            : new Rectangle(0, 0, ClientSize.Width, Math.Min(64, ClientSize.Height));
        using var path = RoundPath(topRegion, topRegion.Height / 2);
        if (_results.Visible && ClientSize.Height > 72)
        {
            using var dropdownPath = RoundPath(GetResultsPanelRect(), 12);
            path.AddPath(dropdownPath, false);
        }

        Region?.Dispose();
        Region = new Region(path);
    }

    private Rectangle GetResultsPanelRect()
    {
        return new Rectangle(ShadowInset + 6, 58, Math.Max(1, Width - ShadowInset * 2 - 12), Math.Max(1, Height - 62));
    }

    private Rectangle GetResultsListRect()
    {
        var panel = GetResultsPanelRect();
        return new Rectangle(panel.X + 8, panel.Y + 8, Math.Max(1, panel.Width - 16), Math.Max(1, panel.Height - 14));
    }

    private void SetResultsVisible(bool visible)
    {
        var rowCount = visible ? Math.Min(8, _results.Items.Count) : 0;
        var targetHeight = visible ? 72 + rowCount * _results.ItemHeight : 64;
        var workArea = Screen.FromRectangle(Bounds).WorkingArea;
        targetHeight = Math.Clamp(targetHeight, 64, Math.Max(64, workArea.Bottom - Top - VisibleInset));

        _results.Visible = visible && targetHeight > 96;
        if (Height != targetHeight)
        {
            _restoringPlacement = true;
            try
            {
                Height = targetHeight;
            }
            finally
            {
                _restoringPlacement = false;
            }
        }

        LayoutInput();
        UpdateRoundedRegion();
        Invalidate();
    }

    private void DrawResultItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _results.Items.Count || _results.Items[e.Index] is not QuickSearchEntry entry)
        {
            return;
        }

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        using (var fill = new SolidBrush(selected ? SearchSelectedColor : _results.BackColor))
        {
            e.Graphics.FillRectangle(fill, e.Bounds);
        }

        var typeWidth = Math.Min(86, Math.Max(54, TextRenderer.MeasureText(entry.Type, _results.Font).Width + 18));
        var typeRect = new Rectangle(e.Bounds.Right - typeWidth - 8, e.Bounds.Y + 10, typeWidth, 22);
        var titleRect = new Rectangle(e.Bounds.X + 8, e.Bounds.Y + 8, Math.Max(1, typeRect.X - e.Bounds.X - 18), 26);
        var textColor = SearchTextColor;
        var titleSize = TextRenderer.MeasureText(entry.Title, _results.Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        var shouldScroll = e.Index == _hoverResultIndex && titleSize.Width > titleRect.Width;

        if (shouldScroll)
        {
            var maxOffset = Math.Max(0, titleSize.Width - titleRect.Width + 36);
            var offset = maxOffset == 0 ? 0 : _hoverResultOffset % maxOffset;
            var state = e.Graphics.Save();
            e.Graphics.SetClip(titleRect);
            using var textBrush = new SolidBrush(textColor);
            e.Graphics.DrawString(entry.Title, _results.Font, textBrush, titleRect.X - offset, titleRect.Y + 4);
            e.Graphics.Restore(state);
        }
        else
        {
            TextRenderer.DrawText(e.Graphics, entry.Title, _results.Font, titleRect, textColor, TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        using var chipFill = new SolidBrush(SearchChipBackColor);
        using var chipPath = RoundPath(typeRect, 9);
        e.Graphics.FillPath(chipFill, chipPath);
        TextRenderer.DrawText(e.Graphics, entry.Type, _results.Font, typeRect, SearchSubtleColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        if (e.Index < _results.Items.Count - 1)
        {
            using var line = new Pen(TransparentStyle ? Color.FromArgb(72, 95, 122) : Color.FromArgb(226, 234, 242));
            e.Graphics.DrawLine(line, e.Bounds.X + 4, e.Bounds.Bottom - 1, e.Bounds.Right - 4, e.Bounds.Bottom - 1);
        }
    }

    private bool ResultTitleOverflows(int index)
    {
        if (index < 0 || index >= _results.Items.Count || _results.Items[index] is not QuickSearchEntry entry)
        {
            return false;
        }

        var bounds = _results.GetItemRectangle(index);
        if (bounds.Width <= 0)
        {
            return false;
        }

        var typeWidth = Math.Min(86, Math.Max(54, TextRenderer.MeasureText(entry.Type, _results.Font).Width + 18));
        var titleWidth = Math.Max(1, bounds.Width - typeWidth - 26);
        var measured = TextRenderer.MeasureText(entry.Title, _results.Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        return measured.Width > titleWidth;
    }

    private static void DrawSearchIcon(Graphics g, Rectangle rect, Color color)
    {
        using var pen = new Pen(color, 1.8F)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        g.DrawEllipse(pen, rect.X + 1, rect.Y + 1, 10, 10);
        g.DrawLine(pen, rect.X + 11, rect.Y + 11, rect.Right - 2, rect.Bottom - 2);
    }

    private static GraphicsPath RoundPath(Rectangle rect, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        rect.Width -= 1;
        rect.Height -= 1;
        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _menu.Dispose();
            _resultMenu.Dispose();
            _resultScrollTimer.Dispose();
        }

        base.Dispose(disposing);
    }
}

internal sealed class DashboardCanvas : Control
{
    private readonly ToolTip _todoToolTip = new()
    {
        AutomaticDelay = 250,
        ReshowDelay = 100,
        AutoPopDelay = 8000
    };
    private readonly ContextMenuStrip _todoMenu = new() { ShowImageMargin = false };
    private readonly List<(Rectangle Rect, TodoItem Item)> _todoAreas = new();
    private readonly List<(Rectangle Rect, TodoItem Item)> _todoCheckAreas = new();
    private Rectangle _todoCompletedArea;
    private string? _hoverTodoKey;

    private readonly AppConfig _config;
    private readonly TodoData _todos;
    private readonly ProjectData _projects;
    private readonly LaunchData _launchers;
    private readonly NoteData _notes;
    private readonly List<(Rectangle Rect, string Key)> _hotspots = new();
    private readonly List<(Rectangle Rect, string Key)> _noteHotspots = new();
    private readonly List<(Rectangle Rect, DeskCategory? Category)> _desktopPreviewAreas = new();
    private readonly Dictionary<DeskCategory, int> _desktopPreviewScrollOffsets = new();
    private readonly Dictionary<string, Image> _launcherIconCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Image> _desktopIconCache = new(StringComparer.OrdinalIgnoreCase);
    private DeskCategory? _dragDesktopCategory;
    private Rectangle _dragDesktopArea;
    private int _dragDesktopStartX;
    private int _dragDesktopStartOffset;
    private bool _suppressNextClick;
    private bool _showCompletedTodos;
    private int _noteIndex;
    private int _selectedProjectIndex;
    private Rectangle _projectTabStripArea;
    private bool _pressProjectTabs;
    private bool _dragProjectTabs;
    private int _dragProjectTabsStartX;
    private int _dragProjectTabsStartOffset;
    private int _projectTabScrollOffset;
    private const int DesktopPreviewTileWidth = 54;
    private const int ProjectTabWidth = 126;
    private const int ProjectTabGap = 8;

    private static readonly Color CardFill = Color.FromArgb(218, 27, 38, 54);
    private static readonly Color CardBorder = Color.FromArgb(88, 104, 130, 156);
    private static readonly Color TextMain = Color.FromArgb(242, 246, 252);
    private static readonly Color TextSubtle = Color.FromArgb(154, 169, 188);
    private static readonly Color Blue = Color.FromArgb(46, 126, 246);
    private static readonly Font HeaderFont = new("Microsoft YaHei UI", 22F, FontStyle.Regular);
    private static readonly Font TitleFont = new("Microsoft YaHei UI", 12.5F, FontStyle.Regular);
    private static readonly Font NormalFont = new("Microsoft YaHei UI", 9.5F);
    private static readonly Font SmallFont = new("Microsoft YaHei UI", 8.5F);

    public DashboardCanvas(AppConfig config, TodoData todos, ProjectData projects, LaunchData launchers, NoteData notes)
    {
        _config = config;
        _todos = todos;
        _projects = projects;
        _launchers = launchers;
        _notes = notes;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
    }

    public event Action<int>? Navigate;
    public event Action? AddTodo;
    public event Action? TodoChanged;
    public event Action? PinTodo;
    public event Action? AddLauncher;
    public event Action? OrganizeDesktop;
    public event Action? SearchRequested;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragDesktopCategory is not null)
        {
            SetDesktopPreviewOffset(_dragDesktopCategory, _dragDesktopArea.Width, _dragDesktopStartOffset + _dragDesktopStartX - e.X);
            Cursor = Cursors.Default;
            return;
        }

        if (_pressProjectTabs)
        {
            var distance = _dragProjectTabsStartX - e.X;
            if (_dragProjectTabs || Math.Abs(distance) > 4)
            {
                _dragProjectTabs = true;
                SetProjectTabOffset(_dragProjectTabsStartOffset + distance);
                Cursor = Cursors.Hand;
                return;
            }
        }

        UpdateTodoToolTip(e.Location);
        Cursor = _projectTabStripArea.Contains(e.Location) || _hotspots.Any(h => h.Rect.Contains(e.Location)) || _noteHotspots.Any(h => h.Rect.Contains(e.Location)) || _todoCompletedArea.Contains(e.Location) || _todoAreas.Any(item => item.Rect.Contains(e.Location)) || _todoCheckAreas.Any(item => item.Rect.Contains(e.Location))
            ? Cursors.Hand
            : Cursors.Default;
        base.OnMouseMove(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        var previewHit = _desktopPreviewAreas.FirstOrDefault(item => item.Rect.Contains(e.Location));
        if (e.Button == MouseButtons.Left && previewHit.Category is not null && CanScrollDesktopPreview(previewHit.Category, previewHit.Rect.Width))
        {
            _dragDesktopCategory = previewHit.Category;
            _dragDesktopArea = previewHit.Rect;
            _dragDesktopStartX = e.X;
            _dragDesktopStartOffset = _desktopPreviewScrollOffsets.GetValueOrDefault(previewHit.Category);
            Capture = true;
            return;
        }

        if (e.Button == MouseButtons.Left && _projectTabStripArea.Contains(e.Location) && CanScrollProjectTabs())
        {
            _pressProjectTabs = true;
            _dragProjectTabs = false;
            _dragProjectTabsStartX = e.X;
            _dragProjectTabsStartOffset = _projectTabScrollOffset;
            Capture = true;
            return;
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_dragDesktopCategory is not null)
        {
            _dragDesktopCategory = null;
            _suppressNextClick = true;
            Capture = false;
            return;
        }

        if (_pressProjectTabs)
        {
            _pressProjectTabs = false;
            if (_dragProjectTabs)
            {
                _dragProjectTabs = false;
                _suppressNextClick = true;
                Capture = false;
                return;
            }

            Capture = false;
        }

        base.OnMouseUp(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        var hit = _desktopPreviewAreas.FirstOrDefault(item => item.Rect.Contains(e.Location));
        if (hit.Category is not null)
        {
            SetDesktopPreviewOffset(hit.Category, hit.Rect.Width, _desktopPreviewScrollOffsets.GetValueOrDefault(hit.Category) + (e.Delta < 0 ? DesktopPreviewTileWidth : -DesktopPreviewTileWidth));
            return;
        }

        if (_projectTabStripArea.Contains(e.Location) && CanScrollProjectTabs())
        {
            SetProjectTabOffset(_projectTabScrollOffset + (e.Delta < 0 ? ProjectTabWidth : -ProjectTabWidth));
            return;
        }

        base.OnMouseWheel(e);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            var todoHit = _todoAreas.FirstOrDefault(item => item.Rect.Contains(e.Location));
            if (todoHit.Item is not null)
            {
                ShowTodoMenu(todoHit.Item, e.Location);
                base.OnMouseClick(e);
                return;
            }
        }

        if (_suppressNextClick)
        {
            _suppressNextClick = false;
            base.OnMouseClick(e);
            return;
        }

        if (e.Button == MouseButtons.Left)
        {
            var noteHit = _noteHotspots.FirstOrDefault(item => item.Rect.Contains(e.Location));
            switch (noteHit.Key)
            {
                case "notePrev":
                    ChangeNote(-1);
                    base.OnMouseClick(e);
                    return;
                case "noteNext":
                    ChangeNote(1);
                    base.OnMouseClick(e);
                    return;
            }

            if (_todoCompletedArea.Contains(e.Location))
            {
                _showCompletedTodos = !_showCompletedTodos;
                Invalidate();
                base.OnMouseClick(e);
                return;
            }

            var checkHit = _todoCheckAreas.FirstOrDefault(item => item.Rect.Contains(e.Location));
            if (checkHit.Item is not null)
            {
                checkHit.Item.Done = !checkHit.Item.Done;
                TodoChanged?.Invoke();
                Invalidate();
                base.OnMouseClick(e);
                return;
            }
        }

        var hit = _hotspots.FirstOrDefault(h => h.Rect.Contains(e.Location));
        switch (hit.Key)
        {
            case "desktop":
                Navigate?.Invoke(1);
                break;
            case "todo":
                Navigate?.Invoke(2);
                break;
            case "note":
                Navigate?.Invoke(3);
                break;
            case "project":
                Navigate?.Invoke(4);
                break;
            case var key when key is not null && key.StartsWith("projectTab:", StringComparison.Ordinal):
                if (int.TryParse(key["projectTab:".Length..], out var projectIndex))
                {
                    _selectedProjectIndex = projectIndex;
                    Invalidate();
                }
                break;
            case "launcher":
                Navigate?.Invoke(5);
                break;
            case "stats":
                Navigate?.Invoke(6);
                break;
            case "search":
                SearchRequested?.Invoke();
                break;
            case "addTodo":
                AddTodo?.Invoke();
                break;
            case "pinTodo":
                PinTodo?.Invoke();
                break;
            case "addLauncher":
                AddLauncher?.Invoke();
                break;
            case "organize":
                OrganizeDesktop?.Invoke();
                break;
        }
        base.OnMouseClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        _hotspots.Clear();
        _noteHotspots.Clear();
        _desktopPreviewAreas.Clear();
        _todoAreas.Clear();
        _todoCheckAreas.Clear();
        _todoCompletedArea = Rectangle.Empty;
        _projectTabStripArea = Rectangle.Empty;

        var width = ClientSize.Width;
        var height = ClientSize.Height;
        if (width < 700 || height < 500)
        {
            return;
        }

        const int headerHeight = 130;
        DrawHeader(g, new Rectangle(0, 0, width, headerHeight));

        const int gap = 16;
        var area = new Rectangle(0, headerHeight + 18, width, height - headerHeight - 18);
        var rightWidth = Math.Max(280, (int)(area.Width * 0.25));
        var leftWidth = area.Width - rightWidth - gap;
        var topHeight = Math.Max(255, (int)(area.Height * 0.45));
        var bottomHeight = area.Height - topHeight - gap;
        var desktopWidth = (int)(leftWidth * 0.49);
        var todoWidth = leftWidth - desktopWidth - gap;

        var desktop = new Rectangle(area.X, area.Y, desktopWidth, topHeight);
        var todo = new Rectangle(desktop.Right + gap, area.Y, todoWidth, topHeight);
        var project = new Rectangle(area.X, desktop.Bottom + gap, leftWidth, bottomHeight);

        var noteHeight = (int)(area.Height * 0.36);
        var statsHeight = (int)(area.Height * 0.27);
        var launcherHeight = area.Height - noteHeight - statsHeight - gap * 2;
        var rightX = area.X + leftWidth + gap;
        var note = new Rectangle(rightX, area.Y, rightWidth, noteHeight);
        var stats = new Rectangle(rightX, note.Bottom + gap, rightWidth, statsHeight);
        var launcher = new Rectangle(rightX, stats.Bottom + gap, rightWidth, launcherHeight);

        DrawDesktopCard(g, desktop);
        DrawTodoCard(g, todo);
        DrawProjectCard(g, project);
        DrawNoteCard(g, note);
        DrawStatsCard(g, stats);
        DrawLauncherCard(g, launcher);
    }

    private void DrawHeader(Graphics g, Rectangle rect)
    {
        DrawText(g, $"{DateTime.Now:yyyy年M月d日}    {WeekText(DateTime.Now.DayOfWeek)}", NormalFont, TextSubtle, rect.X + 4, rect.Y + 16);
        DrawText(g, $"{Greeting()}，开发者！", HeaderFont, TextMain, rect.X + 4, rect.Y + 48);
        DrawText(g, "专注当下，持续记录，成就更好的自己！", NormalFont, TextSubtle, rect.X + 4, rect.Y + 110);

        var search = new Rectangle(rect.Right - 405, rect.Y + 50, 385, 42);
        FillRound(g, search, Color.FromArgb(190, 47, 58, 76), 9);
        DrawRound(g, search, Color.FromArgb(82, 96, 116, 140), 9);
        var key = new Rectangle(search.Right - 74, search.Y + 8, 62, 26);
        DrawText(g, "⌕", NormalFont, TextSubtle, search.X + 16, search.Y + 12);
        TextRenderer.DrawText(
            g,
            "搜索文件、应用、项目、记录...",
            NormalFont,
            new Rectangle(search.X + 40, search.Y + 1, Math.Max(1, key.Left - search.X - 50), search.Height),
            TextSubtle,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping);
        FillRound(g, key, Color.FromArgb(70, 82, 98), 5);
        DrawCentered(g, "Ctrl + K", SmallFont, TextSubtle, key);
        _hotspots.Add((search, "search"));
    }

    private void DrawDesktopCard(Graphics g, Rectangle rect)
    {
        DrawCard(g, rect, "▣  桌面收纳", "管理", "desktop");
        var inner = Inflate(rect, 14, 54, 14, 62);
        var visibleRows = Math.Clamp(inner.Height / 54, 1, 4);
        var rowHeight = Math.Max(34, inner.Height / visibleRows);
        var categories = _config.DesktopCategories.Take(visibleRows).ToArray();
        for (var i = 0; i < categories.Length; i++)
        {
            var items = PreviewItemPaths(categories[i]).ToArray();
            var row = new Rectangle(inner.X, inner.Y + i * rowHeight, inner.Width, rowHeight - 7);
            FillRound(g, row, Color.FromArgb(95, 35, 45, 60), 6);
            DrawText(g, $"■ {categories[i].Name}", NormalFont, TextMain, row.X + 12, row.Y + 8);
            if (row.Height >= 42)
            {
                DrawText(g, $"{items.Length}个", SmallFont, TextSubtle, row.X + 18, row.Y + 27);
            }

            var add = new Rectangle(row.Right - 40, row.Y + Math.Max(5, row.Height / 2 - 14), 28, 28);
            var previewArea = new Rectangle(row.X + 92, row.Y + 5, Math.Max(1, add.Left - row.X - 104), row.Height - 8);
            _desktopPreviewAreas.Add((previewArea, categories[i]));
            var maxOffset = Math.Max(0, items.Length * DesktopPreviewTileWidth - previewArea.Width);
            var offset = Math.Clamp(_desktopPreviewScrollOffsets.GetValueOrDefault(categories[i]), 0, maxOffset);
            _desktopPreviewScrollOffsets[categories[i]] = offset;

            var state = g.Save();
            try
            {
                g.SetClip(previewArea);
                for (var itemIndex = 0; itemIndex < items.Length; itemIndex++)
                {
                    var tile = new Rectangle(previewArea.X + itemIndex * DesktopPreviewTileWidth - offset, row.Y + 6, 46, Math.Max(32, row.Height - 10));
                    if (tile.Left < previewArea.Left || tile.Right > previewArea.Right)
                    {
                        continue;
                    }

                    DrawDesktopPreviewIcon(g, tile, items[itemIndex]);
                }
            }
            finally
            {
                g.Restore(state);
            }

            DrawRound(g, add, Color.FromArgb(120, 138, 160), 5);
            DrawPlusIcon(g, add, TextSubtle);
            _hotspots.Add((add, "desktop"));
        }

        var primary = new Rectangle(rect.X + 14, rect.Bottom - 48, 150, 34);
        var secondary = new Rectangle(primary.Right + 14, primary.Y, 150, 34);
        FillRound(g, primary, Blue, 6);
        FillRound(g, secondary, Color.FromArgb(70, 82, 100), 6);
        DrawCentered(g, "添加到桌面", NormalFont, Color.White, primary);
        DrawCentered(g, "添加分类", NormalFont, TextMain, secondary);
        _hotspots.Add((primary, "organize"));
        _hotspots.Add((secondary, "desktop"));
    }

    private bool CanScrollDesktopPreview(DeskCategory category, int areaWidth)
    {
        return PreviewItemPaths(category).Count() * DesktopPreviewTileWidth > areaWidth;
    }

    private void SetDesktopPreviewOffset(DeskCategory category, int areaWidth, int offset)
    {
        var itemCount = PreviewItemPaths(category).Count();
        var maxOffset = Math.Max(0, itemCount * DesktopPreviewTileWidth - areaWidth);
        _desktopPreviewScrollOffsets[category] = Math.Clamp(offset, 0, maxOffset);
        Invalidate();
    }

    private void DrawTodoCard(Graphics g, Rectangle rect)
    {
        DrawCard(g, rect, "▣  今日工作记录", "+", "addTodo", Color.FromArgb(82, 160, 255));
        var inner = Inflate(rect, 14, 56, 14, 48);
        var items = (_showCompletedTodos ? _todos.Items : _todos.Items.Where(item => !item.Done)).Take(5).ToArray();
        if (items.Length == 0)
        {
            DrawCentered(g, "暂无待办任务", NormalFont, TextSubtle, inner);
        }
        else
        {
            for (var i = 0; i < items.Length; i++)
            {
                var y = inner.Y + i * 38;
                var box = new Rectangle(inner.X, y + 8, 16, 16);
                _todoCheckAreas.Add((new Rectangle(inner.X - 4, y, 26, 30), items[i]));
                DrawRound(g, box, TextSubtle, 3);
                if (items[i].Done)
                {
                    using var checkPen = new Pen(Color.FromArgb(126, 242, 210), 1.8F);
                    g.DrawLine(checkPen, box.X + 3, box.Y + 9, box.X + 7, box.Y + 13);
                    g.DrawLine(checkPen, box.X + 7, box.Y + 13, box.Right - 3, box.Y + 4);
                }

                DrawText(g, items[i].Text, NormalFont, items[i].Done ? TextSubtle : TextMain, inner.X + 28, y + 5);
                DrawText(g, items[i].CreatedAt.ToString("HH:mm"), SmallFont, TextSubtle, inner.Right - 112, y + 8);
                var tagText = string.IsNullOrWhiteSpace(items[i].Tag) ? "未分类" : items[i].Tag.Trim();
                var badgeWidth = Math.Max(44, Math.Min(84, 18 + tagText.Length * 14));
                var todoRect = new Rectangle(inner.X, y, inner.Width, 30);
                _todoAreas.Add((todoRect, items[i]));
                DrawBadge(g, tagText, new Rectangle(inner.Right - badgeWidth, y + 4, badgeWidth, 24), GetDashboardTodoTagColor(items[i].Tag));
            }
        }

        _todoCompletedArea = new Rectangle(rect.X + 12, rect.Bottom - 42, 132, 34);
        DrawText(g, $"已完成（{_todos.Items.Count(item => item.Done)}）", NormalFont, TextSubtle, rect.X + 16, rect.Bottom - 32);
        var pin = new Rectangle(rect.Right - 122, rect.Bottom - 40, 106, 28);
        FillRound(g, pin, Color.FromArgb(70, 82, 100), 6);
        DrawCentered(g, "添加到桌面", SmallFont, TextMain, pin);
        _hotspots.Add((pin, "pinTodo"));
    }

    private Color GetDashboardTodoTagColor(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return Color.FromArgb(70, 82, 100);
        }

        var preset = _todos.TagPresets.FirstOrDefault(item => string.Equals(item.Name, tag.Trim(), StringComparison.OrdinalIgnoreCase));
        return Color.FromArgb((preset?.ColorArgb).GetValueOrDefault(Color.FromArgb(26, 135, 84).ToArgb()));
    }

    private void UpdateTodoToolTip(Point location)
    {
        var todoHit = _todoAreas.FirstOrDefault(item => item.Rect.Contains(location));
        var key = todoHit.Item is null ? null : $"{todoHit.Item.Text}|{todoHit.Item.CreatedAt.Ticks}";
        if (string.Equals(_hoverTodoKey, key, StringComparison.Ordinal))
        {
            return;
        }

        _hoverTodoKey = key;
        _todoToolTip.SetToolTip(this, string.IsNullOrWhiteSpace(todoHit.Item?.Note) ? string.Empty : todoHit.Item.Note);
    }

    private void ShowTodoMenu(TodoItem item, Point location)
    {
        _todoMenu.Items.Clear();
        _todoMenu.Items.Add("查看详情", null, (_, _) =>
        {
            var note = string.IsNullOrWhiteSpace(item.Note) ? "无" : item.Note.Trim();
            MessageBox.Show(FindForm(), $"任务名称：{item.Text}\n标签：{(string.IsNullOrWhiteSpace(item.Tag) ? "未分类" : item.Tag.Trim())}\n创建时间：{item.CreatedAt:yyyy-MM-dd HH:mm}\n\n备注：\n{note}", "任务详情", MessageBoxButtons.OK, MessageBoxIcon.Information);
        });
        _todoMenu.Show(this, location);
    }

    private void DrawNoteCard(Graphics g, Rectangle rect)
    {
        DrawCard(g, rect, "▣  快捷便签", "＋", "note", Color.FromArgb(255, 190, 70));
        var note = Inflate(rect, 12, 58, 12, 12);
        var noteItems = _notes.Items.Count == 0 ? Array.Empty<NoteItem>() : _notes.Items.ToArray();
        _noteIndex = noteItems.Length == 0 ? 0 : Math.Clamp(_noteIndex, 0, noteItems.Length - 1);
        var current = noteItems.Length == 0 ? null : noteItems[_noteIndex];
        var noteColor = current is null ? Color.FromArgb(unchecked((int)0xFFFFEC8E)) : Color.FromArgb(current.ColorArgb);
        var textColor = current is null ? Color.FromArgb(58, 49, 20) : NoteStyle.TextColor(current);
        FillRound(g, note, noteColor, 3);
        DrawRound(g, note, ControlPaint.Dark(noteColor), 3);
        var text = current?.Text ?? "";
        if (!string.IsNullOrWhiteSpace(text))
        {
            DrawMultiline(g, text, NormalFont, textColor, new Rectangle(note.X + 16, note.Y + 18, note.Width - 28, note.Height - 52));
        }

        var footer = new Rectangle(note.X + 12, note.Bottom - 31, note.Width - 24, 24);
        var canSwitch = noteItems.Length > 1;
        if (canSwitch)
        {
            var prev = new Rectangle(footer.X, footer.Y, 24, 22);
            var next = new Rectangle(prev.Right + 4, footer.Y, 24, 22);
            DrawCentered(g, "‹", TitleFont, textColor, prev);
            DrawCentered(g, "›", TitleFont, textColor, next);
            _noteHotspots.Add((prev, "notePrev"));
            _noteHotspots.Add((next, "noteNext"));
            DrawText(g, $"{_noteIndex + 1}/{noteItems.Length}", SmallFont, textColor, next.Right + 6, footer.Y + 5);
        }

        var time = (current?.UpdatedAt ?? DateTime.Now).ToString("yyyy-MM-dd HH:mm");
        var timeRect = new Rectangle(footer.Right - 132, footer.Y + 3, 132, 18);
        TextRenderer.DrawText(g, time, SmallFont, timeRect, textColor, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping);
    }

    private void ChangeNote(int delta)
    {
        if (_notes.Items.Count <= 1)
        {
            return;
        }

        _noteIndex = (_noteIndex + delta + _notes.Items.Count) % _notes.Items.Count;
        Invalidate();
    }

    private void DrawStatsCard(Graphics g, Rectangle rect)
    {
        DrawCard(g, rect, "▣  本周统计", "＋", "stats");
        var inner = Inflate(rect, 18, 56, 18, 38);
        var projectCount = _projects.Projects.SelectMany(p => p.Items).Count();
        var total = Math.Max(1, _todos.Items.Count + projectCount + _launchers.Items.Count);
        DrawProgress(g, "工作记录", _todos.Items.Count, total, inner.X, inner.Y, inner.Width);
        DrawProgress(g, "项目事项", projectCount, total, inner.X, inner.Y + 30, inner.Width);
        DrawProgress(g, "快捷启动", _launchers.Items.Count, total, inner.X, inner.Y + 60, inner.Width);
        DrawText(g, $"总计    {total} 项 ›", NormalFont, TextMain, rect.Right - 116, rect.Bottom - 32);
    }

    private void DrawLauncherCard(Graphics g, Rectangle rect)
    {
        DrawCard(g, rect, "▣  快捷启动", "⚙", "launcher");
        var inner = Inflate(rect, 18, 58, 18, 18);
        var col = 4;
        var tileW = inner.Width / col;
        var tileH = Math.Max(54, inner.Height / 2);
        var items = _launchers.Items.Take(5).ToArray();

        for (var i = 0; i < items.Length; i++)
        {
            var tile = new Rectangle(inner.X + i % col * tileW, inner.Y + i / col * tileH, tileW - 4, tileH - 6);
            DrawLauncherTile(g, tile, items[i], "launcher");
        }

        if (_launchers.Items.Count < 5)
        {
            var addIndex = Math.Min(items.Length, 5);
            var add = new Rectangle(inner.X + addIndex % col * tileW, inner.Y + addIndex / col * tileH, tileW - 4, tileH - 6);
            DrawTinajiaIcon(g, new Rectangle(add.X + add.Width / 2 - 15, add.Y + 8, 30, 30), Color.FromArgb(85, 214, 130));
            DrawText(g, "添加", SmallFont, TextSubtle, add.X + add.Width / 2 - 14, add.Y + 42);
            _hotspots.Add((add, "addLauncher"));
        }
    }

    private void DrawProjectCard(Graphics g, Rectangle rect)
    {
        DrawCard(g, rect, "▣  项目管理", "项目管理", "project");
        var inner = Inflate(rect, 14, 58, 14, 14);
        _projectTabStripArea = new Rectangle(rect.X + 176, rect.Y + 12, Math.Max(80, rect.Width - 272), 34);
        if (_selectedProjectIndex < 0 || _selectedProjectIndex >= _projects.Projects.Count)
        {
            _selectedProjectIndex = 0;
        }

        var project = _projects.Projects.ElementAtOrDefault(_selectedProjectIndex);
        if (project is null)
        {
            DrawCentered(g, "暂无项目", NormalFont, TextSubtle, inner);
            return;
        }

        DrawProjectTabs(g, project);
        DrawProjectPhaseProgress(g, project, inner);
    }

    private void DrawProjectTabs(Graphics g, ProjectBoard selectedProject)
    {
        var maxOffset = ProjectTabMaxOffset();
        _projectTabScrollOffset = Math.Clamp(_projectTabScrollOffset, 0, maxOffset);
        var oldClip = g.Clip;
        g.SetClip(_projectTabStripArea);

        var tabX = _projectTabStripArea.X - _projectTabScrollOffset;
        for (var i = 0; i < _projects.Projects.Count; i++)
        {
            var project = _projects.Projects[i];
            var tab = new Rectangle(tabX, _projectTabStripArea.Y + 1, ProjectTabWidth, _projectTabStripArea.Height - 2);
            FillRound(g, tab, project == selectedProject ? Blue : Color.FromArgb(48, 58, 74), 6);
            TextRenderer.DrawText(g, project.Name, SmallFont, new Rectangle(tab.X + 12, tab.Y, tab.Width - 24, tab.Height), project == selectedProject ? Color.White : TextMain, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping);

            var visibleTab = Rectangle.Intersect(tab, _projectTabStripArea);
            if (!visibleTab.IsEmpty)
            {
                _hotspots.Add((visibleTab, $"projectTab:{i}"));
            }

            tabX += ProjectTabWidth + ProjectTabGap;
        }

        g.Clip = oldClip;
    }

    private int ProjectTabMaxOffset()
    {
        var totalWidth = Math.Max(0, _projects.Projects.Count * (ProjectTabWidth + ProjectTabGap) - ProjectTabGap);
        return Math.Max(0, totalWidth - _projectTabStripArea.Width);
    }

    private bool CanScrollProjectTabs()
    {
        return ProjectTabMaxOffset() > 0;
    }

    private void SetProjectTabOffset(int offset)
    {
        _projectTabScrollOffset = Math.Clamp(offset, 0, ProjectTabMaxOffset());
        Invalidate(_projectTabStripArea);
    }

    private void DrawProjectPhaseProgress(Graphics g, ProjectBoard project, Rectangle rect)
    {
        const int rowHeight = 44;
        var items = project.Items.Take(Math.Max(1, (rect.Height - 42) / rowHeight)).ToArray();
        if (items.Length == 0)
        {
            DrawCentered(g, "暂无项目阶段", NormalFont, TextSubtle, rect);
            return;
        }

        DrawText(g, "阶段进度", NormalFont, TextSubtle, rect.X + 6, rect.Y + 8);
        for (var i = 0; i < items.Length; i++)
        {
            var item = items[i];
            var y = rect.Y + 42 + i * rowHeight;
            var percent = ProjectProgressPercent(item);
            DrawProjectPhaseRow(g, item, percent, Color.FromArgb(58, 214, 122), new Rectangle(rect.X + 6, y, rect.Width - 18, rowHeight - 8));
        }
    }

    private void DrawProjectPhaseRow(Graphics g, ProjectItem item, int percent, Color color, Rectangle rect)
    {
        var infoWidth = Math.Min(430, Math.Max(360, rect.Width / 2));
        var nameWidth = Math.Min(88, Math.Max(64, infoWidth / 4));
        TextRenderer.DrawText(g, item.Title, SmallFont, new Rectangle(rect.X, rect.Y + 8, nameWidth, 22), TextMain, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping);
        TextRenderer.DrawText(g, ProjectDateRangeText(item), SmallFont, new Rectangle(rect.X + nameWidth + 10, rect.Y + 8, infoWidth - nameWidth - 12, 22), TextSubtle, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping);

        var track = new Rectangle(rect.X + infoWidth + 16, rect.Y + 15, Math.Max(80, rect.Width - infoWidth - 70), 9);
        FillRound(g, track, Color.FromArgb(58, 68, 88), 4);
        if (percent > 0)
        {
            FillRound(g, new Rectangle(track.X, track.Y, Math.Max(8, track.Width * percent / 100), track.Height), color, 4);
        }

        DrawProjectProgressTicks(g, track, item.SubItems.Count);

        var thumbX = track.X + track.Width * percent / 100;
        using var thumbBrush = new SolidBrush(Color.White);
        using var thumbBorder = new Pen(Color.FromArgb(126, 151, 186), 1F);
        var thumb = new Rectangle(thumbX - 6, track.Y - 5, 13, 19);
        g.FillEllipse(thumbBrush, thumb);
        g.DrawEllipse(thumbBorder, thumb);
        DrawText(g, $"{percent}%", SmallFont, TextSubtle, track.Right + 10, rect.Y + 9);
    }

    private void DrawCard(Graphics g, Rectangle rect, string title, string action, string key, Color? actionIconColor = null)
    {
        FillRound(g, rect, CardFill, 8);
        DrawRound(g, rect, CardBorder, 8);
        DrawText(g, title, TitleFont, TextMain, rect.X + 16, rect.Y + 15);
        var actionRect = new Rectangle(rect.Right - 78, rect.Y + 8, 64, 38);
        if (actionIconColor.HasValue)
        {
            DrawTinajiaIcon(g, new Rectangle(actionRect.X + 18, actionRect.Y + 5, 28, 28), actionIconColor.Value);
        }
        else if (action == "管理")
        {
            DrawManageIcon(g, new Rectangle(actionRect.X + 21, actionRect.Y + 8, 22, 22), TextSubtle);
        }
        else if (action == "项目管理")
        {
            DrawProjectManageIcon(g, new Rectangle(actionRect.X + 20, actionRect.Y + 7, 24, 24), TextSubtle);
        }
        else
        {
            DrawText(g, action, NormalFont, TextSubtle, actionRect.X + 13, actionRect.Y + 11);
        }
        _hotspots.Add((actionRect, key));
    }

    private static void DrawManageIcon(Graphics g, Rectangle rect, Color color)
    {
        using var pen = new Pen(color, 1.6F);
        var gap = Math.Max(3, rect.Width / 7);
        var size = (rect.Width - gap) / 2;
        DrawRound(g, new Rectangle(rect.X, rect.Y, size, size), color, 4);
        DrawRound(g, new Rectangle(rect.X + size + gap, rect.Y, size, size), color, 4);
        DrawRound(g, new Rectangle(rect.X, rect.Y + size + gap, size, size), color, 4);
        DrawRound(g, new Rectangle(rect.X + size + gap, rect.Y + size + gap, size, size), color, 4);
    }

    private static void DrawProjectManageIcon(Graphics g, Rectangle rect, Color color)
    {
        DrawRound(g, rect, color, 4);
        using var pen = new Pen(color, 1.8F);
        var circle = new Rectangle(rect.X + 4, rect.Y + 13, 7, 7);
        g.DrawEllipse(pen, circle);
        g.DrawLine(pen, rect.X + 14, rect.Y + 7, rect.Right - 4, rect.Y + 7);
        g.DrawLine(pen, rect.X + 14, rect.Y + 16, rect.Right - 4, rect.Y + 16);
        using var checkPen = new Pen(Color.FromArgb(255, 156, 0), 1.8F);
        g.DrawLine(checkPen, rect.X + 4, rect.Y + 8, rect.X + 7, rect.Y + 11);
        g.DrawLine(checkPen, rect.X + 7, rect.Y + 11, rect.X + 12, rect.Y + 5);
    }

    private void DrawLauncherTile(Graphics g, Rectangle rect, LaunchItem item, string key)
    {
        var icon = new Rectangle(rect.X + rect.Width / 2 - 17, rect.Y + 8, 34, 34);
        var shellIcon = GetLauncherIcon(item.Path, _launcherIconCache);
        if (shellIcon is not null)
        {
            g.DrawImage(shellIcon, icon);
        }
        else
        {
            FillRound(g, icon, IconColor(item.Name), 8);
            DrawText(g, Initial(item.Name), TitleFont, Color.White, icon.X + 8, icon.Y + 5);
        }

        DrawCentered(g, item.Name, SmallFont, TextMain, new Rectangle(rect.X, rect.Y + 42, rect.Width, 24));
        _hotspots.Add((rect, key));
    }

    private static Image? GetLauncherIcon(string path, Dictionary<string, Image> cache)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (cache.TryGetValue(path, out var cached))
        {
            return cached;
        }

        var icon = ShellIconLoader.LoadLargeIcon(path);
        if (icon is not null)
        {
            cache[path] = icon;
        }

        return icon;
    }

    private void DrawAppIcon(Graphics g, Rectangle rect, string name)
    {
        var icon = new Rectangle(rect.X + rect.Width / 2 - 14, rect.Y + rect.Height / 2 - 14, 28, 28);
        FillRound(g, icon, IconColor(name), 7);
        DrawCentered(g, Initial(name), SmallFont, Color.White, icon);
    }

    private void DrawDesktopPreviewIcon(Graphics g, Rectangle rect, string path)
    {
        var icon = new Rectangle(rect.X + rect.Width / 2 - 16, rect.Y + rect.Height / 2 - 16, 32, 32);
        var shellIcon = GetLauncherIcon(path, _desktopIconCache);
        if (shellIcon is not null)
        {
            g.DrawImage(shellIcon, icon);
            return;
        }

        var name = Path.GetFileNameWithoutExtension(path);
        FillRound(g, icon, IconColor(name), 7);
        DrawCentered(g, Initial(name), SmallFont, Color.White, icon);
    }

    private static void DrawPlusIcon(Graphics g, Rectangle rect, Color color)
    {
        using var pen = new Pen(color, 1.8F);
        var centerX = rect.X + rect.Width / 2;
        var centerY = rect.Y + rect.Height / 2;
        g.DrawLine(pen, centerX - 6, centerY, centerX + 6, centerY);
        g.DrawLine(pen, centerX, centerY - 6, centerX, centerY + 6);
    }

    private static void DrawTinajiaIcon(Graphics g, Rectangle rect, Color color)
    {
        FillRound(g, rect, color, Math.Max(4, rect.Width / 5));
        var bar = Math.Max(2, rect.Width / 5);
        var span = Math.Max(8, rect.Width * 3 / 5);
        var centerX = rect.X + rect.Width / 2;
        var centerY = rect.Y + rect.Height / 2;
        FillRound(g, new Rectangle(centerX - span / 2, centerY - bar / 2, span, bar), Color.White, Math.Max(1, bar / 2));
        FillRound(g, new Rectangle(centerX - bar / 2, centerY - span / 2, bar, span), Color.White, Math.Max(1, bar / 2));
    }

    private void DrawProgress(Graphics g, string title, int value, int total, int x, int y, int width)
    {
        DrawText(g, title, SmallFont, TextMain, x, y + 5);
        var track = new Rectangle(x + 82, y + 10, Math.Max(50, width - 142), 6);
        FillRound(g, track, Color.FromArgb(58, 68, 88), 3);
        var fill = new Rectangle(track.X, track.Y, Math.Max(8, track.Width * value / Math.Max(1, total)), track.Height);
        FillRound(g, fill, Blue, 3);
        DrawText(g, $"{value} 项", SmallFont, TextSubtle, track.Right + 10, y + 3);
    }

    private static IEnumerable<string> PreviewItems(DeskCategory category)
    {
        return PreviewItemPaths(category)
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>();
    }

    private static IEnumerable<string> PreviewItemPaths(DeskCategory category)
    {
        return category.ItemPaths
            .Where(path => File.Exists(path) || Directory.Exists(path))
            .Cast<string>();
    }

    private static Rectangle Inflate(Rectangle rect, int left, int top, int right, int bottom)
    {
        return new Rectangle(rect.X + left, rect.Y + top, rect.Width - left - right, rect.Height - top - bottom);
    }

    private static void FillRound(Graphics g, Rectangle rect, Color color, int radius)
    {
        using var brush = new SolidBrush(color);
        using var path = RoundPath(rect, radius);
        g.FillPath(brush, path);
    }

    private static void DrawRound(Graphics g, Rectangle rect, Color color, int radius)
    {
        using var pen = new Pen(color);
        using var path = RoundPath(rect, radius);
        g.DrawPath(pen, path);
    }

    private static GraphicsPath RoundPath(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        rect.Width -= 1;
        rect.Height -= 1;
        path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void DrawText(Graphics g, string text, Font font, Color color, int x, int y)
    {
        TextRenderer.DrawText(g, text, font, new Point(x, y), color, TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping);
    }

    private static void DrawCentered(Graphics g, string text, Font font, Color color, Rectangle rect)
    {
        TextRenderer.DrawText(g, text, font, rect, color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping);
    }

    private static void DrawMultiline(Graphics g, string text, Font font, Color color, Rectangle rect)
    {
        TextRenderer.DrawText(g, text, font, rect, color, TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
    }

    private static void DrawBadge(Graphics g, string text, Rectangle rect, Color color)
    {
        FillRound(g, rect, Color.FromArgb(color.R / 2, color.G / 2, color.B / 2), 5);
        DrawRound(g, rect, color, 5);
        DrawCentered(g, text, SmallFont, Color.FromArgb(176, 236, 255), rect);
    }

    private static Color IconColor(string name)
    {
        var hash = Math.Abs(name.GetHashCode());
        var colors = new[]
        {
            Color.FromArgb(54, 128, 245),
            Color.FromArgb(42, 176, 111),
            Color.FromArgb(236, 78, 78),
            Color.FromArgb(244, 169, 45),
            Color.FromArgb(143, 91, 234)
        };
        return colors[hash % colors.Length];
    }

    private static string Initial(string name)
    {
        return string.IsNullOrWhiteSpace(name) ? "+" : name.Trim()[0].ToString().ToUpperInvariant();
    }

    private static string TrimName(string name, int length)
    {
        return name.Length <= length ? name : name[..length];
    }

    private static string Greeting()
    {
        var hour = DateTime.Now.Hour;
        return hour < 12 ? "上午好" : hour < 18 ? "下午好" : "晚上好";
    }

    private static string WeekText(DayOfWeek day)
    {
        return day switch
        {
            DayOfWeek.Monday => "星期一",
            DayOfWeek.Tuesday => "星期二",
            DayOfWeek.Wednesday => "星期三",
            DayOfWeek.Thursday => "星期四",
            DayOfWeek.Friday => "星期五",
            DayOfWeek.Saturday => "星期六",
            _ => "星期日"
        };
    }

    private static int ProjectProgressPercent(ProjectItem item)
    {
        if (item.SubItems.Count > 0)
        {
            var completed = item.SubItems.Count(ProjectSubItemCompleted);
            return (int)Math.Round(completed * 100D / item.SubItems.Count, MidpointRounding.AwayFromZero);
        }

        if (item.ProgressPercent >= 0)
        {
            return Math.Clamp(item.ProgressPercent, 0, 100);
        }

        return item.Status switch
        {
            ProjectStatus.Done => 100,
            ProjectStatus.Doing => 50,
            _ => 0
        };
    }

    private static string ProjectDateRangeText(ProjectItem item)
    {
        return item.StartDate.HasValue || item.EndDate.HasValue
            ? $"开始 {item.StartDate?.ToString("yyyy/MM/dd") ?? "----/--/--"}  截止 {item.EndDate?.ToString("yyyy/MM/dd") ?? "----/--/--"}"
            : "开始 ----/--/--  截止 ----/--/--";
    }

    private static string ProjectStartDateText(ProjectItem item)
    {
        return $"开始 {item.StartDate?.ToString("yyyy/MM/dd") ?? "----/--/--"}";
    }

    private static string ProjectEndDateText(ProjectItem item)
    {
        return $"截止 {item.EndDate?.ToString("yyyy/MM/dd") ?? "----/--/--"}";
    }

    private static void DrawProjectProgressTicks(Graphics g, Rectangle track, int parts)
    {
        if (parts <= 1)
        {
            return;
        }

        using var pen = new Pen(Color.FromArgb(140, 176, 198, 220), 1F);
        for (var i = 1; i < parts; i++)
        {
            var x = track.X + track.Width * i / parts;
            g.DrawLine(pen, x, track.Y - 2, x, track.Bottom + 2);
        }
    }

    private static bool ProjectSubItemCompleted(ProjectSubItem item)
    {
        return item.Done;
    }

    private static string StatusText(ProjectStatus status)
    {
        return status switch
        {
            ProjectStatus.Doing => "进行中",
            ProjectStatus.Done => "已完成",
            _ => "待开始"
        };
    }

    private static Color StatusColor(ProjectStatus status)
    {
        return status switch
        {
            ProjectStatus.Doing => Color.FromArgb(197, 135, 22),
            ProjectStatus.Done => Color.FromArgb(26, 135, 84),
            _ => Color.FromArgb(35, 107, 238)
        };
    }
}

internal static class ShellIconLoader
{
    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiLargeIcon = 0x000000000;
    private const uint ShgfiAddOverlays = 0x000000020;

    public static Image? LoadLargeIcon(string path)
    {
        try
        {
            var result = SHGetFileInfo(
                path,
                0,
                out var fileInfo,
                (uint)Marshal.SizeOf<SHFILEINFO>(),
                ShgfiIcon | ShgfiLargeIcon | ShgfiAddOverlays);

            if (result == IntPtr.Zero || fileInfo.hIcon == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                using var icon = (Icon)Icon.FromHandle(fileInfo.hIcon).Clone();
                return icon.ToBitmap();
            }
            finally
            {
                DestroyIcon(fileInfo.hIcon);
            }
        }
        catch
        {
            return null;
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        out SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }
}

internal static class ShellContextMenu
{
    private const uint CmfNormal = 0x00000000;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCmd = 0x0100;
    private const uint FirstCommandId = 1;
    private const uint LastCommandId = 0x7fff;
    private const int SwShowNormal = 1;

    public static bool ShowForPath(IntPtr ownerHandle, string path, Point screenPoint)
    {
        if (string.IsNullOrWhiteSpace(path) || (!File.Exists(path) && !Directory.Exists(path)))
        {
            return false;
        }

        IShellFolder? parentFolder = null;
        IContextMenu? contextMenu = null;
        var absolutePidl = IntPtr.Zero;
        var itemPidlArray = IntPtr.Zero;
        var menu = IntPtr.Zero;

        try
        {
            if (SHParseDisplayName(path, IntPtr.Zero, out absolutePidl, 0, out _) < 0 || absolutePidl == IntPtr.Zero)
            {
                return false;
            }

            var shellFolderId = typeof(IShellFolder).GUID;
            if (SHBindToParent(absolutePidl, ref shellFolderId, out parentFolder, out var itemPidl) < 0 || parentFolder is null || itemPidl == IntPtr.Zero)
            {
                return false;
            }

            itemPidlArray = Marshal.AllocCoTaskMem(IntPtr.Size);
            Marshal.WriteIntPtr(itemPidlArray, itemPidl);

            var contextMenuId = typeof(IContextMenu).GUID;
            if (parentFolder.GetUIObjectOf(ownerHandle, 1, itemPidlArray, ref contextMenuId, IntPtr.Zero, out var contextMenuPtr) < 0 || contextMenuPtr == IntPtr.Zero)
            {
                return false;
            }

            contextMenu = (IContextMenu)Marshal.GetObjectForIUnknown(contextMenuPtr);
            Marshal.Release(contextMenuPtr);

            menu = CreatePopupMenu();
            if (menu == IntPtr.Zero)
            {
                return false;
            }

            if (contextMenu.QueryContextMenu(menu, 0, FirstCommandId, LastCommandId, CmfNormal) < 0)
            {
                return false;
            }

            var command = TrackPopupMenuEx(menu, TpmRightButton | TpmReturnCmd, screenPoint.X, screenPoint.Y, ownerHandle, IntPtr.Zero);
            if (command >= FirstCommandId)
            {
                var invoke = new CMINVOKECOMMANDINFO
                {
                    cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFO>(),
                    hwnd = ownerHandle,
                    lpVerb = (IntPtr)(command - FirstCommandId),
                    nShow = SwShowNormal
                };
                contextMenu.InvokeCommand(ref invoke);
            }

            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (menu != IntPtr.Zero)
            {
                DestroyMenu(menu);
            }

            if (contextMenu is not null)
            {
                Marshal.ReleaseComObject(contextMenu);
            }

            if (parentFolder is not null)
            {
                Marshal.ReleaseComObject(parentFolder);
            }

            if (itemPidlArray != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(itemPidlArray);
            }

            if (absolutePidl != IntPtr.Zero)
            {
                CoTaskMemFree(absolutePidl);
            }
        }
    }

    public static bool ShowDesktopBackground(IntPtr ownerHandle, Point screenPoint)
    {
        IShellFolder? desktopFolder = null;
        IContextMenu? contextMenu = null;
        var menu = IntPtr.Zero;

        try
        {
            if (SHGetDesktopFolder(out desktopFolder) < 0 || desktopFolder is null)
            {
                return false;
            }

            var contextMenuId = typeof(IContextMenu).GUID;
            if (desktopFolder.CreateViewObject(ownerHandle, ref contextMenuId, out var contextMenuPtr) < 0 || contextMenuPtr == IntPtr.Zero)
            {
                return false;
            }

            contextMenu = (IContextMenu)Marshal.GetObjectForIUnknown(contextMenuPtr);
            Marshal.Release(contextMenuPtr);

            menu = CreatePopupMenu();
            if (menu == IntPtr.Zero)
            {
                return false;
            }

            if (contextMenu.QueryContextMenu(menu, 0, FirstCommandId, LastCommandId, CmfNormal) < 0)
            {
                return false;
            }

            var command = TrackPopupMenuEx(menu, TpmRightButton | TpmReturnCmd, screenPoint.X, screenPoint.Y, ownerHandle, IntPtr.Zero);
            if (command >= FirstCommandId)
            {
                var invoke = new CMINVOKECOMMANDINFO
                {
                    cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFO>(),
                    hwnd = ownerHandle,
                    lpVerb = (IntPtr)(command - FirstCommandId),
                    nShow = SwShowNormal
                };
                contextMenu.InvokeCommand(ref invoke);
            }

            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (menu != IntPtr.Zero)
            {
                DestroyMenu(menu);
            }

            if (contextMenu is not null)
            {
                Marshal.ReleaseComObject(contextMenu);
            }

            if (desktopFolder is not null)
            {
                Marshal.ReleaseComObject(desktopFolder);
            }
        }
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetDesktopFolder(out IShellFolder ppshf);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string pszName, IntPtr pbc, out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);

    [DllImport("shell32.dll")]
    private static extern int SHBindToParent(IntPtr pidl, ref Guid riid, out IShellFolder ppv, out IntPtr ppidlLast);

    [DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(IntPtr pv);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

    [ComImport]
    [Guid("000214E6-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        [PreserveSig]
        int ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName, out uint pchEaten, out IntPtr ppidl, ref uint pdwAttributes);

        [PreserveSig]
        int EnumObjects(IntPtr hwnd, int grfFlags, out IntPtr ppenumIDList);

        [PreserveSig]
        int BindToObject(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);

        [PreserveSig]
        int BindToStorage(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);

        [PreserveSig]
        int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);

        [PreserveSig]
        int CreateViewObject(IntPtr hwndOwner, ref Guid riid, out IntPtr ppv);

        [PreserveSig]
        int GetAttributesOf(uint cidl, IntPtr apidl, ref uint rgfInOut);

        [PreserveSig]
        int GetUIObjectOf(IntPtr hwndOwner, uint cidl, IntPtr apidl, ref Guid riid, IntPtr rgfReserved, out IntPtr ppv);

        [PreserveSig]
        int GetDisplayNameOf(IntPtr pidl, uint uFlags, IntPtr pName);

        [PreserveSig]
        int SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string pszName, uint uFlags, out IntPtr ppidlOut);
    }

    [ComImport]
    [Guid("000214e4-0000-0000-c000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig]
        int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);

        [PreserveSig]
        int InvokeCommand(ref CMINVOKECOMMANDINFO pici);

        [PreserveSig]
        int GetCommandString(UIntPtr idCmd, uint uFlags, IntPtr pReserved, IntPtr pszName, uint cchMax);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct CMINVOKECOMMANDINFO
    {
        public int cbSize;
        public int fMask;
        public IntPtr hwnd;
        public IntPtr lpVerb;

        [MarshalAs(UnmanagedType.LPStr)]
        public string? lpParameters;

        [MarshalAs(UnmanagedType.LPStr)]
        public string? lpDirectory;

        public int nShow;
        public int dwHotKey;
        public IntPtr hIcon;
    }
}

internal static class GraphicsExtensions
{
    public static void FillRoundedRectangle(this Graphics graphics, Brush brush, Rectangle rect, int radius)
    {
        using var path = CreateRoundedPath(rect, radius);
        graphics.FillPath(brush, path);
    }

    private static GraphicsPath CreateRoundedPath(Rectangle rect, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}




