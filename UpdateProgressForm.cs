namespace DustDesk;

internal sealed class UpdateProgressForm : Form
{
    private readonly Label _titleLabel;
    private readonly Label _statusLabel;
    private readonly ProgressBar _progressBar;

    public UpdateProgressForm(UpdateInfo update)
    {
        Text = "DustDesk 更新";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ControlBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(420, 150);
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        _titleLabel = new Label
        {
            AutoSize = false,
            Text = $"正在安装新版本 v{update.VersionText}",
            Font = new Font(Font, FontStyle.Bold),
            Location = new Point(22, 18),
            Size = new Size(376, 24)
        };

        _statusLabel = new Label
        {
            AutoSize = false,
            Text = "正在准备更新...",
            Location = new Point(22, 52),
            Size = new Size(376, 24)
        };

        _progressBar = new ProgressBar
        {
            Location = new Point(22, 88),
            Size = new Size(376, 22),
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 24
        };

        Controls.AddRange(new Control[] { _titleLabel, _statusLabel, _progressBar });
    }

    public void SetProgress(UpdateInstallProgress progress)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => SetProgress(progress)));
            return;
        }

        _statusLabel.Text = progress.Message;
        if (progress.Percent is { } percent)
        {
            _progressBar.Style = ProgressBarStyle.Blocks;
            _progressBar.MarqueeAnimationSpeed = 0;
            _progressBar.Value = Math.Clamp(percent, _progressBar.Minimum, _progressBar.Maximum);
        }
        else
        {
            _progressBar.Style = ProgressBarStyle.Marquee;
            _progressBar.MarqueeAnimationSpeed = 24;
        }
    }
}
