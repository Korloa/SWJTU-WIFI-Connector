namespace SwjtuKeeper;

internal sealed class SettingsForm : Form
{
    private readonly KeeperContext keeper;
    private readonly Label statusLabel;
    private readonly CheckBox enabledBox;
    private readonly TextBox wifiNameBox;
    private readonly NumericUpDown intervalBox;
    private readonly CheckBox startupBox;

    public SettingsForm(KeeperContext keeper)
    {
        this.keeper = keeper;
        Text = "SWJTU 保活";
        ClientSize = new Size(410, 285);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        enabledBox = new CheckBox { Text = "自动连接与保活", Left = 22, Top = 18, Width = 180 };
        enabledBox.CheckedChanged += (_, _) => keeper.SetEnabled(enabledBox.Checked);
        Controls.Add(enabledBox);

        statusLabel = new Label { Left = 22, Top = 49, Width = 365, Height = 38 };
        Controls.Add(statusLabel);

        Controls.Add(new Label { Text = "Wi-Fi 名称 / 已保存配置文件名", Left = 22, Top = 96, Width = 260 });
        wifiNameBox = new TextBox { Left = 22, Top = 116, Width = 365, Text = keeper.Settings.WifiName };
        Controls.Add(wifiNameBox);

        Controls.Add(new Label { Text = "保活间隔（秒）", Left = 22, Top = 151, Width = 130 });
        intervalBox = new NumericUpDown
        {
            Left = 148,
            Top = 148,
            Width = 75,
            Minimum = 30,
            Maximum = 300,
            Value = keeper.Settings.IntervalSeconds
        };
        Controls.Add(intervalBox);

        startupBox = new CheckBox { Text = "登录 Windows 后自动启动", Left = 22, Top = 185, Width = 230 };
        startupBox.Checked = Startup.IsEnabled();
        Controls.Add(startupBox);

        var saveButton = new Button { Text = "保存设置", Left = 22, Top = 231, Width = 105 };
        saveButton.Click += (_, _) => Save();
        Controls.Add(saveButton);

        var closeButton = new Button { Text = "关闭", Left = 282, Top = 231, Width = 105 };
        closeButton.Click += (_, _) => Close();
        Controls.Add(closeButton);

        RefreshState();
    }

    public void RefreshState()
    {
        if (enabledBox.Checked != keeper.Settings.Enabled)
            enabledBox.Checked = keeper.Settings.Enabled;
        statusLabel.Text = $"状态：{keeper.Status}";
    }

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(wifiNameBox.Text))
        {
            MessageBox.Show("请输入 Wi-Fi 名称。", "SWJTU 保活");
            return;
        }

        try
        {
            keeper.UpdateSettings(wifiNameBox.Text, (int)intervalBox.Value, startupBox.Checked);
            MessageBox.Show("设置已保存。", "SWJTU 保活");
        }
        catch (Exception error)
        {
            MessageBox.Show($"保存失败：{error.Message}", "SWJTU 保活", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
