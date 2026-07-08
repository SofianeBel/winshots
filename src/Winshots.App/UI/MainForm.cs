using System.Diagnostics;
using System.Runtime.InteropServices;
using Winshots.App.Codex;
using Winshots.App.Capture;
using Winshots.App.Settings;
using Winshots.App.Windows;

namespace Winshots.App.UI;

public sealed class MainForm : Form
{
    private const int CaptureHotkeyId = 100;
    private const int CaptureToCodexHotkeyId = 101;

    private readonly CaptureWorkflow _workflow;
    private readonly ShortcutSettingsStore _settingsStore = new();
    private readonly RecordingOverlayForm _overlay = new();
    private readonly System.Windows.Forms.Timer _targetPollTimer = new();
    private readonly System.Windows.Forms.Timer _timelineTimer = new();
    private readonly int _ownProcessId = Environment.ProcessId;

    private ShortcutSettings _settings = null!;
    private IntPtr _lastExternalWindow;
    private bool _isCapturing;

    private Button _captureButton = null!;
    private Button _captureToCodexButton = null!;
    private Button _timelineButton = null!;
    private Button _sessionButton = null!;
    private Button _openRootButton = null!;
    private Button _copyTextButton = null!;
    private Button _shortcutsButton = null!;
    private NumericUpDown _intervalInput = null!;
    private Label _statusLabel = null!;
    private Label _targetLabel = null!;
    private ListView _captureList = null!;
    private PictureBox _preview = null!;
    private TextBox _contextBox = null!;
    private NotifyIcon _notifyIcon = null!;
    private VisualSessionRecorder? _sessionRecorder;

    public MainForm()
    {
        _workflow = new CaptureWorkflow(CapturePaths.DefaultRoot);
        _settings = _settingsStore.Load();

        Text = "Winshots";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(980, 640);
        Size = new Size(1160, 760);

        BuildUi();
        LoadRecentCaptures();

        _targetPollTimer.Interval = 500;
        _targetPollTimer.Tick += (_, _) => UpdateLastExternalWindow();
        _targetPollTimer.Start();

        _timelineTimer.Tick += async (_, _) => await CaptureFromCurrentContextAsync("periodic");
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        RegisterConfiguredHotkeys();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        UnregisterConfiguredHotkeys();
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WmHotKey)
        {
            int hotkeyId = m.WParam.ToInt32();
            if (hotkeyId == CaptureHotkeyId)
            {
                _ = CaptureFromCurrentContextAsync("hotkey");
                return;
            }

            if (hotkeyId == CaptureToCodexHotkeyId)
            {
                _ = CaptureFromCurrentContextAsync("codex-hotkey", pasteToCodex: true);
                return;
            }
        }

        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _preview.Image?.Dispose();
            _overlay.Dispose();
            _targetPollTimer.Dispose();
            _timelineTimer.Dispose();
            _notifyIcon.Dispose();
            if (_sessionRecorder?.IsRunning == true)
            {
                _sessionRecorder.StopAsync().GetAwaiter().GetResult();
            }
        }

        base.Dispose(disposing);
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        root.Controls.Add(header, 0, 0);

        var commands = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        header.Controls.Add(commands, 0, 0);

        _captureButton = new Button { Text = "Capture now", Width = 112, Height = 32 };
        _captureButton.Click += async (_, _) => await CaptureFromCurrentContextAsync("manual");
        commands.Controls.Add(_captureButton);

        _captureToCodexButton = new Button { Text = "Capture to Codex", Width = 128, Height = 32 };
        _captureToCodexButton.Click += async (_, _) => await CaptureFromCurrentContextAsync("codex", pasteToCodex: true);
        commands.Controls.Add(_captureToCodexButton);

        _timelineButton = new Button { Text = "Start timeline", Width = 112, Height = 32 };
        _timelineButton.Click += (_, _) => ToggleTimeline();
        commands.Controls.Add(_timelineButton);

        _sessionButton = new Button { Text = "Start session", Width = 112, Height = 32 };
        _sessionButton.Click += async (_, _) => await ToggleVisualSessionAsync();
        commands.Controls.Add(_sessionButton);

        commands.Controls.Add(new Label
        {
            Text = "Every",
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 8, 0, 0)
        });

        _intervalInput = new NumericUpDown
        {
            Minimum = 5,
            Maximum = 3600,
            Value = 60,
            Width = 72
        };
        commands.Controls.Add(_intervalInput);

        commands.Controls.Add(new Label
        {
            Text = "seconds",
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(0, 8, 12, 0)
        });

        _openRootButton = new Button { Text = "Open captures", Width = 116, Height = 32 };
        _openRootButton.Click += (_, _) => OpenPath(_workflow.Storage.RootPath);
        commands.Controls.Add(_openRootButton);

        _copyTextButton = new Button { Text = "Copy context", Width = 112, Height = 32, Enabled = false };
        _copyTextButton.Click += (_, _) => Clipboard.SetText(_contextBox.Text);
        commands.Controls.Add(_copyTextButton);

        _shortcutsButton = new Button { Text = "Shortcuts", Width = 92, Height = 32 };
        _shortcutsButton.Click += (_, _) => OpenShortcutSettings();
        commands.Controls.Add(_shortcutsButton);

        var statusPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2
        };
        statusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        statusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 430));
        header.Controls.Add(statusPanel, 0, 1);

        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = ShortcutStatusText(),
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft
        };
        statusPanel.Controls.Add(_statusLabel, 0, 0);

        _targetLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            AutoEllipsis = true
        };
        statusPanel.Controls.Add(_targetLabel, 1, 0);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterDistance = 360
        };
        root.Controls.Add(split, 0, 1);

        _captureList = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = false
        };
        _captureList.Columns.Add("Time", 138);
        _captureList.Columns.Add("Window", 190);
        _captureList.Columns.Add("Reason", 72);
        _captureList.SelectedIndexChanged += (_, _) => ShowSelectedCapture();
        _captureList.DoubleClick += (_, _) =>
        {
            if (SelectedCapture() is { } capture)
            {
                OpenPath(capture.DirectoryPath);
            }
        };
        split.Panel1.Controls.Add(_captureList);

        var previewSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 390
        };
        split.Panel2.Controls.Add(previewSplit);

        _preview = new PictureBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(28, 30, 34),
            SizeMode = PictureBoxSizeMode.Zoom
        };
        previewSplit.Panel1.Controls.Add(_preview);

        _contextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Both,
            Font = new Font(FontFamily.GenericMonospace, 9),
            ReadOnly = true,
            WordWrap = false
        };
        previewSplit.Panel2.Controls.Add(_contextBox);

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Winshots",
            Visible = true,
            ContextMenuStrip = BuildTrayMenu()
        };
        _notifyIcon.DoubleClick += (_, _) => ShowFromTray();
    }

    private ContextMenuStrip BuildTrayMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Capture now", null, async (_, _) => await CaptureFromCurrentContextAsync("tray"));
        menu.Items.Add("Capture to Codex", null, async (_, _) => await CaptureFromCurrentContextAsync("codex-tray", pasteToCodex: true));
        menu.Items.Add("Show Winshots", null, (_, _) => ShowFromTray());
        menu.Items.Add("Open captures", null, (_, _) => OpenPath(_workflow.Storage.RootPath));
        menu.Items.Add("Exit", null, (_, _) => Close());
        return menu;
    }

    private async Task CaptureFromCurrentContextAsync(string reason, bool pasteToCodex = false)
    {
        if (_isCapturing)
        {
            return;
        }

        IntPtr hwnd = SelectCaptureTarget();
        if (!NativeMethods.IsUsableCaptureTarget(hwnd))
        {
            SetStatus("No capture target. Focus another window, then use the hotkey or Capture now.");
            return;
        }

        try
        {
            _isCapturing = true;
            UpdateOverlayState();
            SetCaptureControls(false);
            SetStatus(pasteToCodex ? "Capturing active window for Codex..." : "Capturing active window...");

            CaptureResult result = await Task.Run(() => _workflow.CaptureWindow(hwnd, reason));
            AddCapture(result, select: true);
            if (pasteToCodex)
            {
                PasteCaptureToCodex(result);
            }
            else
            {
                SetStatus($"Captured {result.Metadata.WindowTitle}{FormatMetrics(result.Metadata.Metrics)}");
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Capture failed: {ex.Message}");
            _contextBox.Text = ex.ToString();
        }
        finally
        {
            _isCapturing = false;
            UpdateOverlayState();
            SetCaptureControls(true);
        }
    }

    private IntPtr SelectCaptureTarget()
    {
        IntPtr foreground = NativeMethods.GetForegroundWindow();
        if (NativeMethods.GetProcessId(foreground) != _ownProcessId)
        {
            return foreground;
        }

        return _lastExternalWindow;
    }

    private void UpdateLastExternalWindow()
    {
        IntPtr hwnd = NativeMethods.GetForegroundWindow();
        if (NativeMethods.GetProcessId(hwnd) == _ownProcessId || !NativeMethods.IsUsableCaptureTarget(hwnd))
        {
            return;
        }

        _lastExternalWindow = hwnd;
        string title = NativeMethods.GetWindowTitle(hwnd);
        _targetLabel.Text = string.IsNullOrWhiteSpace(title) ? "Target: untitled window" : $"Target: {title}";
    }

    private void ToggleTimeline()
    {
        if (_timelineTimer.Enabled)
        {
            _timelineTimer.Stop();
            _timelineButton.Text = "Start timeline";
            SetStatus("Periodic capture stopped.");
            UpdateOverlayState();
            return;
        }

        _timelineTimer.Interval = Decimal.ToInt32(_intervalInput.Value) * 1000;
        _timelineTimer.Start();
        _timelineButton.Text = "Stop timeline";
        SetStatus($"Periodic capture every {_intervalInput.Value} seconds.");
        UpdateOverlayState();
    }

    private void UpdateOverlayState()
    {
        if (_isCapturing)
        {
            _overlay.ShowStatus(_timelineTimer.Enabled || _sessionRecorder?.IsRunning == true ? "Recording: capturing" : "Capturing");
            return;
        }

        if (_sessionRecorder?.IsRunning == true)
        {
            _overlay.ShowStatus("Recording session");
            return;
        }

        if (_timelineTimer.Enabled)
        {
            _overlay.ShowStatus($"Recording every {_intervalInput.Value}s");
            return;
        }

        _overlay.Hide();
    }

    private async Task ToggleVisualSessionAsync()
    {
        if (_sessionRecorder?.IsRunning == true)
        {
            await StopVisualSessionAsync();
            return;
        }

        var recorder = new VisualSessionRecorder(new VisualSessionOptions
        {
            IntervalMs = Decimal.ToInt32(_intervalInput.Value) * 1000,
            MaxDurationSeconds = 300,
            IncludeVideo = true
        });

        _sessionRecorder = recorder;
        recorder.Start(SelectCaptureTarget);
        _sessionButton.Text = "Stop session";
        SetStatus($"Visual session recording: {recorder.DirectoryPath}");
        UpdateOverlayState();
        _ = WatchVisualSessionAsync(recorder);
    }

    private async Task StopVisualSessionAsync()
    {
        VisualSessionRecorder? recorder = _sessionRecorder;
        if (recorder is null)
        {
            return;
        }

        _sessionButton.Enabled = false;
        SetStatus("Finalizing visual session...");

        VisualSessionManifest manifest = await recorder.StopAsync();
        FinishVisualSessionUi(recorder, manifest);
    }

    private async Task WatchVisualSessionAsync(VisualSessionRecorder recorder)
    {
        VisualSessionManifest manifest = await recorder.WaitForCompletionAsync();
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        BeginInvoke(() => FinishVisualSessionUi(recorder, manifest));
    }

    private void FinishVisualSessionUi(VisualSessionRecorder recorder, VisualSessionManifest manifest)
    {
        if (_sessionRecorder != recorder)
        {
            return;
        }

        _sessionButton.Text = "Start session";
        _sessionButton.Enabled = true;
        string video = string.IsNullOrWhiteSpace(manifest.VideoPath) ? "no video" : "video ready";
        SetStatus($"Visual session saved: {manifest.CapturedFrameCount} frames, {video}. {manifest.DirectoryPath}");
        UpdateOverlayState();
    }

    private void LoadRecentCaptures()
    {
        foreach (CaptureResult capture in _workflow.Storage.ListRecent(50).Reverse())
        {
            AddCapture(capture, select: false);
        }
    }

    private void AddCapture(CaptureResult capture, bool select)
    {
        var item = new ListViewItem(capture.Metadata.TimestampLocal)
        {
            Tag = capture
        };
        item.SubItems.Add(capture.Metadata.WindowTitle);
        item.SubItems.Add(capture.Metadata.Reason);
        _captureList.Items.Insert(0, item);

        if (select)
        {
            item.Selected = true;
            item.EnsureVisible();
            ShowCapture(capture);
        }
    }

    private void ShowSelectedCapture()
    {
        if (SelectedCapture() is { } capture)
        {
            ShowCapture(capture);
        }
    }

    private CaptureResult? SelectedCapture()
    {
        return _captureList.SelectedItems.Count == 0
            ? null
            : _captureList.SelectedItems[0].Tag as CaptureResult;
    }

    private void ShowCapture(CaptureResult capture)
    {
        _preview.Image?.Dispose();
        _preview.Image = null;

        if (File.Exists(capture.ScreenshotPath))
        {
            using Image image = Image.FromFile(capture.ScreenshotPath);
            _preview.Image = new Bitmap(image);
        }

        _contextBox.Text = File.Exists(capture.TextPath)
            ? File.ReadAllText(capture.TextPath)
            : "Context file not found.";

        _copyTextButton.Enabled = !string.IsNullOrWhiteSpace(_contextBox.Text);
    }

    private void SetCaptureControls(bool enabled)
    {
        _captureButton.Enabled = enabled;
        _captureToCodexButton.Enabled = enabled;
        _timelineButton.Enabled = enabled;
        _sessionButton.Enabled = enabled || _sessionRecorder?.IsRunning == true;
        _openRootButton.Enabled = enabled;
        _shortcutsButton.Enabled = enabled;
    }

    private void SetStatus(string message)
    {
        _statusLabel.Text = message;
    }

    private static string FormatMetrics(CaptureMetrics? metrics)
    {
        return metrics is null
            ? string.Empty
            : $" ({metrics.TotalMs}ms, text {metrics.TextExtractionMs}ms)";
    }

    private void RegisterConfiguredHotkeys()
    {
        var errors = new List<string>();
        RegisterConfiguredHotkey(CaptureHotkeyId, _settings.CaptureHotkey, "Capture", errors);
        RegisterConfiguredHotkey(CaptureToCodexHotkeyId, _settings.CaptureToCodexHotkey, "Capture to Codex", errors);

        SetStatus(errors.Count == 0 ? ShortcutStatusText() : string.Join(" ", errors));
    }

    private void RegisterConfiguredHotkey(int id, string value, string label, List<string> errors)
    {
        if (!HotkeyBinding.TryParseOptional(value, out HotkeyBinding? hotkey, out string error))
        {
            errors.Add($"{label} shortcut disabled: {error}");
            return;
        }

        if (hotkey is null)
        {
            return;
        }

        if (!NativeMethods.RegisterHotKey(Handle, id, hotkey.Modifiers, (uint)hotkey.Key))
        {
            errors.Add($"{label} shortcut {hotkey} failed: Windows error {Marshal.GetLastWin32Error()}.");
        }
    }

    private void UnregisterConfiguredHotkeys()
    {
        NativeMethods.UnregisterHotKey(Handle, CaptureHotkeyId);
        NativeMethods.UnregisterHotKey(Handle, CaptureToCodexHotkeyId);
    }

    private void OpenShortcutSettings()
    {
        using var dialog = new ShortcutSettingsDialog(_settings);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        ShortcutSettings settings = dialog.Settings;
        if (!TryValidateShortcutSettings(settings, out string error))
        {
            MessageBox.Show(this, error, "Invalid shortcut", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _settings = settings;
        try
        {
            _settingsStore.Save(_settings);
        }
        catch (Exception ex)
        {
            SetStatus($"Could not save shortcuts: {ex.Message}");
            return;
        }

        if (IsHandleCreated)
        {
            UnregisterConfiguredHotkeys();
            RegisterConfiguredHotkeys();
        }
        else
        {
            SetStatus(ShortcutStatusText());
        }
    }

    private static bool TryValidateShortcutSettings(ShortcutSettings settings, out string error)
    {
        error = string.Empty;

        if (!HotkeyBinding.TryParseOptional(settings.CaptureHotkey, out HotkeyBinding? capture, out string captureError))
        {
            error = $"Capture shortcut: {captureError}";
            return false;
        }

        if (!HotkeyBinding.TryParseOptional(settings.CaptureToCodexHotkey, out HotkeyBinding? codex, out string codexError))
        {
            error = $"Capture to Codex shortcut: {codexError}";
            return false;
        }

        if (capture is not null && capture == codex)
        {
            error = "Capture and Capture to Codex cannot use the same shortcut.";
            return false;
        }

        return true;
    }

    private void PasteCaptureToCodex(CaptureResult result)
    {
        try
        {
            CodexPasteResult paste = CodexChatPaster.TryPasteCapture(result);
            string prefix = $"Captured {result.Metadata.WindowTitle}{FormatMetrics(result.Metadata.Metrics)}.";
            SetStatus(paste.Success ? $"{prefix} {paste.Message}" : $"{prefix} {paste.Message}");
            if (!paste.Success)
            {
                MessageBox.Show(this, paste.Message, "Capture saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Captured {result.Metadata.WindowTitle}, but Codex paste failed: {ex.Message}");
        }
    }

    private string ShortcutStatusText()
    {
        string capture = ShortcutDisplayText(_settings.CaptureHotkey);
        string codex = ShortcutDisplayText(_settings.CaptureToCodexHotkey);
        return $"Hotkeys: capture {capture}; Codex {codex}. Captures stay local unless pasted to Codex.";
    }

    private static string ShortcutDisplayText(string value)
    {
        return string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), "None", StringComparison.OrdinalIgnoreCase)
            ? "disabled"
            : value;
    }

    private static void OpenPath(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }
}
