namespace MicMute;

internal static class InputDialog
{
    /// <summary>Returns the entered text, or null if cancelled.</summary>
    public static string Show(IWin32Window owner, string prompt, string value)
    {
        using var form = new Form
        {
            Text = "MicMute",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.CenterScreen,
            AutoScaleMode = AutoScaleMode.Font,
            Font = new Font("Segoe UI", 9f),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12),
            TopMost = true
        };
        var layout = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Dock = DockStyle.Fill };
        var label = new Label { Text = prompt, AutoSize = true, Margin = new Padding(0, 0, 0, 6) };
        var box = new TextBox { Text = value ?? "", Width = 320 };
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 0) };
        var cancel = new Button { Text = "Avbryt", DialogResult = DialogResult.Cancel, AutoSize = true };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        layout.Controls.Add(label);
        layout.Controls.Add(box);
        layout.Controls.Add(buttons);
        form.Controls.Add(layout);
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        return form.ShowDialog(owner) == DialogResult.OK ? box.Text : null;
    }
}
