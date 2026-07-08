using Winshots.App.Settings;

namespace Winshots.App.UI;

public sealed class ShortcutSettingsDialog : Form
{
    private readonly TextBox _captureInput;
    private readonly TextBox _codexInput;

    public ShortcutSettingsDialog(ShortcutSettings settings)
    {
        Text = "Shortcuts";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(420, 150);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(12)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        Controls.Add(root);

        root.Controls.Add(new Label
        {
            Text = "Capture",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        _captureInput = new TextBox
        {
            Dock = DockStyle.Fill,
            Text = settings.CaptureHotkey
        };
        root.Controls.Add(_captureInput, 1, 0);

        root.Controls.Add(new Label
        {
            Text = "Capture to Codex",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 1);

        _codexInput = new TextBox
        {
            Dock = DockStyle.Fill,
            Text = settings.CaptureToCodexHotkey
        };
        root.Controls.Add(_codexInput, 1, 1);

        var helpLabel = new Label
        {
            Text = "Example: Ctrl+Shift+Enter. Empty disables a shortcut.",
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft
        };
        root.Controls.Add(helpLabel, 0, 2);
        root.SetColumnSpan(helpLabel, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        root.Controls.Add(buttons, 0, 3);
        root.SetColumnSpan(buttons, 2);

        var cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Width = 86
        };
        buttons.Controls.Add(cancelButton);

        var okButton = new Button
        {
            Text = "Save",
            DialogResult = DialogResult.OK,
            Width = 86
        };
        buttons.Controls.Add(okButton);

        AcceptButton = okButton;
        CancelButton = cancelButton;
    }

    public ShortcutSettings Settings => new()
    {
        CaptureHotkey = _captureInput.Text.Trim(),
        CaptureToCodexHotkey = _codexInput.Text.Trim()
    };
}
