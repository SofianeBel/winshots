using Winshots.App.Windows;

namespace Winshots.App.UI;

public sealed class RecordingOverlayForm : Form
{
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    private readonly Label _textLabel;

    public RecordingOverlayForm()
    {
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.FromArgb(18, 20, 24);
        ClientSize = new Size(210, 42);
        FormBorderStyle = FormBorderStyle.None;
        Opacity = 0.92;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;

        _textLabel = new Label
        {
            AutoEllipsis = true,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = Color.White,
            Padding = new Padding(38, 0, 12, 1),
            Text = "Recording",
            TextAlign = ContentAlignment.MiddleLeft
        };
        Controls.Add(_textLabel);
    }

    protected override bool ShowWithoutActivation => true;

    public string StatusText => _textLabel.Text;

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= WsExTransparent | WsExToolWindow | WsExNoActivate;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeMethods.SetWindowDisplayAffinity(Handle, NativeMethods.WdaExcludeFromCapture);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        using var borderPen = new Pen(Color.FromArgb(70, 255, 255, 255));
        using var dotBrush = new SolidBrush(Color.FromArgb(235, 56, 72));
        using var dotGlowBrush = new SolidBrush(Color.FromArgb(60, 235, 56, 72));

        Rectangle bounds = ClientRectangle;
        bounds.Width -= 1;
        bounds.Height -= 1;
        e.Graphics.DrawRectangle(borderPen, bounds);
        e.Graphics.FillEllipse(dotGlowBrush, 13, 12, 18, 18);
        e.Graphics.FillEllipse(dotBrush, 18, 17, 8, 8);
    }

    public void ShowStatus(string text)
    {
        _textLabel.Text = text;
        Reposition();

        if (!Visible)
        {
            Show();
        }
        else
        {
            Invalidate();
        }
    }

    public void Reposition()
    {
        Rectangle area = Screen.PrimaryScreen?.WorkingArea ?? SystemInformation.WorkingArea;
        Location = new Point(area.Right - Width - 18, area.Top + 18);
    }
}
