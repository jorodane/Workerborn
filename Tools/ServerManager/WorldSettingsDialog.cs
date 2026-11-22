using System.Net;
using System.Net.Sockets;

namespace Workerborn.ServerManager;

internal sealed class WorldSettingsDialog : Form
{
    private readonly TextBox name = new() { MaxLength = 48, Dock = DockStyle.Fill };
    private readonly TextBox bind = new() { MaxLength = 15, Dock = DockStyle.Fill };
    private readonly NumericUpDown port = new() { Minimum = 1, Maximum = 65535, Dock = DockStyle.Fill };
    private readonly NumericUpDown limit = new() { Minimum = 1, Maximum = WorldCapacity.MaxCharacters, Dock = DockStyle.Fill };
    private readonly Label message = new() { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 10) };
    public ServerWorld World { get; private set; }

    public WorldSettingsDialog(ServerWorld world, string title, string confirm, bool endpointEditable, bool limitEditable,
        DedicatedAdminStatus? status = null)
    {
        World = world;
        Text = title;
        Font = new Font("Segoe UI", 10);
        AutoScaleMode = AutoScaleMode.Font;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = MaximizeBox = false;
        ClientSize = new Size(430, 250);

        name.Text = world.Name;
        bind.Text = status?.bind ?? world.Bind;
        bind.Enabled = port.Enabled = endpointEditable;
        port.Value = Math.Clamp(status?.port ?? world.Port, 1, 65535);
        limit.Value = Math.Clamp(status?.characterLimit ?? world.InitialCharacterLimit, 1, WorldCapacity.MaxCharacters);
        limit.Enabled = limitEditable;
        limit.Minimum = Math.Max(1, status?.characters.Length ?? 0);
        message.Text = status != null
            ? $"생성된 캐릭터 {status.characters.Length}명 · 현재 시대 기준 {status.requirementCharacters}명\n주소와 포트는 서버 정지 후 변경"
            : limitEditable ? $"동시 접속 {WorldCapacity.MaxConnections}명" : "";

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(16), RowCount = 6 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddField(layout, 0, "월드 이름", name);
        AddField(layout, 1, "수신 주소 (IPv4)", bind);
        AddField(layout, 2, "TCP 포트", port);
        Control capacityField = limitEditable ? limit : new Label
        {
            Text = "서버 실행 후 확인 및 변경", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill
        };
        AddField(layout, 3, "캐릭터 정원", capacityField);
        layout.Controls.Add(message, 0, 4);
        layout.SetColumnSpan(message, 2);
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var apply = new Button { Text = confirm, AutoSize = true, Padding = new Padding(8, 0, 8, 0) };
        var cancel = new Button { Text = "취소", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.AddRange(new Control[] { apply, cancel });
        layout.Controls.Add(buttons, 0, 5);
        layout.SetColumnSpan(buttons, 2);
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(layout);
        AcceptButton = apply;
        CancelButton = cancel;
        apply.Click += (_, _) => SaveSettings();
    }

    private static void AddField(TableLayoutPanel layout, int index, string caption, Control control)
    {
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.Controls.Add(new Label { Text = caption, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, index);
        layout.Controls.Add(control, 1, index);
    }

    private void SaveSettings()
    {
        string text = name.Text.Trim();
        if (text.Length == 0)
        {
            message.Text = "월드 이름을 입력해 주세요.";
            name.Focus();
            return;
        }
        if (!IPAddress.TryParse(bind.Text.Trim(), out var address) || address.AddressFamily != AddressFamily.InterNetwork)
        {
            message.Text = "수신 주소를 IPv4 형식으로 입력해 주세요.";
            bind.Focus();
            return;
        }

        World = World with { Name = text, Bind = address.ToString(), Port = (int)port.Value,
            InitialCharacterLimit = limit.Enabled ? (int)limit.Value : World.InitialCharacterLimit };
        DialogResult = DialogResult.OK;
    }
}
