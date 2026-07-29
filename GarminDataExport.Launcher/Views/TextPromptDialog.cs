namespace GarminDataExport.Launcher.Views;

internal sealed class TextPromptDialog : Form
{
    private readonly TextBox _textBox = new();

    public TextPromptDialog(string title, string prompt, string initialValue = "")
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(460, 145);
        Font = new Font("Segoe UI", 10F);

        var label = new Label
        {
            Text = prompt,
            AutoSize = true,
            Location = new Point(18, 18),
        };
        _textBox.Location = new Point(18, 49);
        _textBox.Width = 420;
        _textBox.Text = initialValue;
        _textBox.SelectAll();

        var ok = new Button
        {
            Text = "Aceptar",
            DialogResult = DialogResult.OK,
            AutoSize = true,
            Location = new Point(260, 96),
        };
        var cancel = new Button
        {
            Text = "Cancelar",
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
            Location = new Point(350, 96),
        };
        Controls.AddRange([label, _textBox, ok, cancel]);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    public string Value => _textBox.Text.Trim();
}
