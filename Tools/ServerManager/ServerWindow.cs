using System.ComponentModel;
using System.Globalization;

namespace Workerborn.ServerManager;

internal sealed class ServerWindow : Form
{
    private readonly ServerWorlds catalog = new();
    private readonly ComboBox worlds = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 224, AccessibleName = "월드 선택" };
    private readonly Button newWorld = Button("새 월드");
    private readonly Button openWorld = Button("기존 월드 열기");
    private readonly Button settings = Button("설정");
    private readonly Button startStop = Button("서버 시작");
    private readonly Label summary = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 0, 0, 0) };
    private readonly RichTextBox log = new()
    {
        ReadOnly = true, Dock = DockStyle.Fill, DetectUrls = false, WordWrap = false,
        BackColor = Color.FromArgb(22, 31, 41), ForeColor = Color.FromArgb(222, 234, 242), BorderStyle = BorderStyle.None,
        AccessibleName = "서버 로그"
    };
    private readonly TextBox command = new()
    {
        Dock = DockStyle.Fill, PlaceholderText = "서버 명령어", AccessibleName = "서버 명령어", MaxLength = DedicatedAdminProtocol.MaxCommandLength
    };
    private readonly Button send = Button("전송");
    private readonly DataGridView roster = new()
    {
        ReadOnly = true, Dock = DockStyle.Fill, MultiSelect = false, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        BackgroundColor = SystemColors.Window, BorderStyle = BorderStyle.FixedSingle, AccessibleName = "월드 캐릭터 목록"
    };
    private readonly Button ban = Button("차단");
    private readonly Button delete = Button("캐릭터 삭제");
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 350 };
    private readonly Dictionary<string, DataGridViewRow> rows = new();
    private ServerProcess? server;
    private DedicatedAdminStatus? lastStatus;
    private bool polling, stopping, stopSent, closeRequested, pipeErrorShown;
    private long nextStatus;

    private ServerWorld? SelectedWorld => worlds.SelectedItem as ServerWorld;
    private DedicatedAdminCharacter? SelectedCharacter => roster.SelectedRows.Count == 0
        ? null : roster.SelectedRows[0].Tag as DedicatedAdminCharacter;

    public ServerWindow()
    {
        Text = "Workerborn 서버";
        Font = new Font("Segoe UI", 10);
        AutoScaleMode = AutoScaleMode.Font;
        MinimumSize = new Size(940, 535);
        ClientSize = new Size(1120, 675);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(238, 243, 246);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(15) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        var caption = new Label { Text = "월드", AutoSize = true, Margin = new Padding(2, 10, 8, 0) };
        worlds.Margin = new Padding(0, 6, 10, 0);
        toolbar.Controls.AddRange(new Control[] { caption, worlds, newWorld, openWorld, settings, startStop });
        startStop.BackColor = Color.FromArgb(223, 237, 248);
        layout.Controls.Add(toolbar, 0, 0);
        layout.Controls.Add(summary, 0, 1);
        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(LogPage());
        tabs.TabPages.Add(RosterPage());
        layout.Controls.Add(tabs, 0, 2);
        Controls.Add(layout);

        worlds.SelectedIndexChanged += (_, _) => RefreshControls();
        newWorld.Click += (_, _) => NewWorld();
        openWorld.Click += (_, _) => ImportWorld();
        settings.Click += async (_, _) => await EditSettingsAsync();
        startStop.Click += (_, _) => { if (server == null) StartServer(); else RequestStop(); };
        send.Click += async (_, _) => await SendCommandAsync();
        command.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                await SendCommandAsync();
            }
        };
        roster.SelectionChanged += (_, _) => RefreshCharacterActions();
        ban.Click += async (_, _) => await ToggleBanAsync();
        delete.Click += async (_, _) => await DeleteCharacterAsync();
        timer.Tick += async (_, _) => await PollAsync();
        FormClosing += HandleClosing;
        FormClosed += (_, _) => { timer.Dispose(); server?.Dispose(); };

        try { catalog.Load(); }
        catch (IOException error) { AppendLog(error.Message + Environment.NewLine); }
        ReloadWorlds();
        timer.Start();
    }

    private static Button Button(string text) => new()
    {
        Text = text, AutoSize = true, Height = 30, Padding = new Padding(7, 0, 7, 0), UseVisualStyleBackColor = true
    };

    private TabPage LogPage()
    {
        var page = new TabPage("서버 로그") { Padding = new Padding(8) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        command.Margin = new Padding(3, 8, 8, 3);
        send.Margin = new Padding(3, 6, 3, 3);
        layout.Controls.Add(log, 0, 0);
        layout.SetColumnSpan(log, 2);
        layout.Controls.Add(command, 0, 1);
        layout.Controls.Add(send, 1, 1);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage RosterPage()
    {
        var page = new TabPage("캐릭터") { Padding = new Padding(8) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        foreach ((string title, int width) in new[]
        {
            ("캐릭터", 105), ("플레이어 ID", 115), ("상태", 65), ("생성 시각", 125), ("최근 접속", 125),
            ("마지막 접속 종료", 125), ("접속 시간", 82), ("지연시간", 71)
        })
            roster.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = title, FillWeight = width, SortMode = DataGridViewColumnSortMode.NotSortable });
        roster.RowTemplate.Height = 29;
        roster.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(243, 247, 250);
        layout.Controls.Add(roster, 0, 0);
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        actions.Controls.AddRange(new Control[] { delete, ban });
        layout.Controls.Add(actions, 0, 1);
        page.Controls.Add(layout);
        return page;
    }

    private void ReloadWorlds(ServerWorld? selection = null)
    {
        worlds.BeginUpdate();
        worlds.Items.Clear();
        foreach (var world in catalog.Worlds)
        {
            worlds.Items.Add(world);
            if (world.Path == selection?.Path)
                worlds.SelectedItem = world;
        }
        if (worlds.SelectedIndex < 0 && worlds.Items.Count > 0)
            worlds.SelectedIndex = 0;
        worlds.EndUpdate();
        RefreshControls();
    }

    private void NewWorld()
    {
        var world = new ServerWorld("솔바람 마을", ServerWorlds.NewPath(), DedicatedAdminProtocol.DefaultBind,
            DedicatedAdminProtocol.DefaultPort, WorldCapacity.DefaultNewWorld);
        using var dialog = new WorldSettingsDialog(world, "새 월드 만들기", "만들고 시작", true, true);
        if (dialog.ShowDialog(this) == DialogResult.OK && StoreWorld(dialog.World))
            StartServer();
    }

    private void ImportWorld()
    {
        using var picker = new OpenFileDialog { Title = "Workerborn 월드 저장 선택", Filter = "Workerborn 월드 저장 (*.json;*.json.bak)|*.json;*.json.bak", CheckFileExists = true };
        if (picker.ShowDialog(this) != DialogResult.OK)
            return;
        string path = picker.FileName.EndsWith(".json.bak", StringComparison.OrdinalIgnoreCase) ? picker.FileName[..^4] : picker.FileName;
        var existing = catalog.Find(path);
        if (existing != null)
        {
            ReloadWorlds(existing);
            return;
        }
        var world = new ServerWorld(Path.GetFileNameWithoutExtension(path), path, DedicatedAdminProtocol.DefaultBind,
            DedicatedAdminProtocol.DefaultPort, WorldCapacity.DefaultNewWorld);
        using var dialog = new WorldSettingsDialog(world, "기존 월드 등록", "등록", true, false);
        if (dialog.ShowDialog(this) == DialogResult.OK)
            StoreWorld(dialog.World);
    }

    private async Task EditSettingsAsync()
    {
        if (SelectedWorld is not { } world)
            return;
        var current = server;
        var status = current?.Status;
        using var dialog = new WorldSettingsDialog(world, "월드 설정", "적용", current == null, status != null || !world.HasSave, status);
        if (dialog.ShowDialog(this) != DialogResult.OK || !StoreWorld(dialog.World))
            return;
        if (current != null && current == server && status != null && status.characterLimit != dialog.World.InitialCharacterLimit)
            await SendAsync($"character-limit {dialog.World.InitialCharacterLimit}");
    }

    private bool StoreWorld(ServerWorld world)
    {
        try
        {
            catalog.Store(world);
            ReloadWorlds(world);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"월드 설정을 저장할 수 없어요.\n{error.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private void StartServer()
    {
        if (server != null || SelectedWorld is not { } world)
            return;
        try
        {
            server = new ServerProcess(world);
            lastStatus = null;
            pipeErrorShown = false;
            nextStatus = 0;
            log.Clear();
            AppendLog($"{world.Name} 월드를 여는 중이에요.{Environment.NewLine}");
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or Win32Exception or UnauthorizedAccessException)
        {
            AppendLog(error.Message + Environment.NewLine);
            MessageBox.Show(this, error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        RefreshControls();
    }

    private void RequestStop()
    {
        if (server == null || stopping)
            return;
        if (!server.Connected && server.Status == null)
            server.CancelStartup();
        server.TakeStopFailure();
        stopping = true;
        stopSent = false;
        RefreshControls();
    }

    private void HandleClosing(object? sender, FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.WindowsShutDown || server == null || server.Exited)
            return;
        e.Cancel = true;
        closeRequested = true;
        RequestStop();
    }

    private async Task PollAsync()
    {
        if (polling || server is not { } current)
            return;
        polling = true;
        try
        {
            ReadLog(current);
            if (current.Exited)
            {
                ReadLog(current, drain: true);
                AppendLog(current.ExitCode == 0 ? "서버가 종료됐어요.\r\n" : $"서버가 종료됐어요. 종료 코드 {current.ExitCode}\r\n");
                current.Dispose();
                server = null;
                stopping = stopSent = false;
                lastStatus = null;
                rows.Clear();
                roster.Rows.Clear();
                RefreshControls();
                if (closeRequested)
                    Close();
                return;
            }
            if (current.TransportError is { } error && !pipeErrorShown)
            {
                pipeErrorShown = true;
                AppendLog($"서버와 관리 창의 연결이 끊겼어요. {error}{Environment.NewLine}");
            }
            if (current.TakeStopFailure())
            {
                stopping = stopSent = closeRequested = false;
                MessageBox.Show(this, "월드를 저장할 수 없어 서버를 계속 실행 중이에요. 로그를 확인해 주세요.", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            if (stopping && !stopSent && current.Connected)
                stopSent = await current.SendAsync("stop");
            if (current.Status is { } state && !ReferenceEquals(lastStatus, state))
            {
                lastStatus = state;
                UpdateRoster(state);
            }
            RefreshControls();
            if (!stopSent && current.Connected && Environment.TickCount64 >= nextStatus)
            {
                nextStatus = Environment.TickCount64 + 1000;
                await current.SendAsync("status");
            }
        }
        finally { polling = false; }
    }

    private void ReadLog(ServerProcess current, bool drain = false)
    {
        try
        {
            string text;
            do
            {
                text = current.ReadLog();
                if (text.Length != 0)
                    AppendLog(text);
            } while (drain && text.Length != 0);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void AppendLog(string text)
    {
        bool atEnd = log.SelectionStart + log.SelectionLength >= log.TextLength;
        int selectionStart = log.SelectionStart;
        int selectionLength = log.SelectionLength;
        log.AppendText(text);
        if (log.TextLength > 220_000)
        {
            int excess = log.TextLength - 180_000;
            int trim = Math.Max(excess, log.Text.IndexOf('\n', excess) + 1);
            log.Select(0, trim);
            log.SelectedText = "";
            selectionStart = Math.Max(0, selectionStart - trim);
        }
        if (atEnd)
        {
            log.Select(log.TextLength, 0);
            log.ScrollToCaret();
        }
        else
            log.Select(selectionStart, selectionLength);
    }

    private async Task<bool> SendAsync(string text)
    {
        if (server is { } current && await current.SendAsync(text))
        {
            AppendLog($"> {text}{Environment.NewLine}");
            return true;
        }
        AppendLog("명령을 보내지 못했어요. 서버의 연결 상태를 확인해 주세요.\r\n");
        return false;
    }

    private async Task SendCommandAsync()
    {
        string text = command.Text.Trim();
        if (text.Length > 0 && await SendAsync(text) && command.Text.Trim() == text)
            command.Clear();
    }

    private async Task ToggleBanAsync()
    {
        if (SelectedCharacter is { } profile)
            await SendAsync($"{(profile.banned ? "unban" : "ban")} {profile.id}");
    }

    private async Task DeleteCharacterAsync()
    {
        if (SelectedCharacter is not { } profile)
            return;
        string question = $"‘{profile.name}’의 이 월드 소지품과 성장 기록을 영구 삭제할까요?";
        if (profile.banned)
            question += "\n이 캐릭터의 차단 기록도 삭제됩니다.";
        if (MessageBox.Show(this, question, "캐릭터 삭제", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
            await SendAsync($"delete-character {profile.id}");
    }

    private void UpdateRoster(DedicatedAdminStatus status)
    {
        var identities = status.characters.Select(character => character.id).ToHashSet();
        foreach (string identity in rows.Keys.Where(id => !identities.Contains(id)).ToArray())
        {
            roster.Rows.Remove(rows[identity]);
            rows.Remove(identity);
        }
        foreach (var character in status.characters)
        {
            if (!rows.TryGetValue(character.id, out var row))
            {
                row = roster.Rows[roster.Rows.Add()];
                rows.Add(character.id, row);
            }
            row.Tag = character;
            row.SetValues(character.name, character.id, character.banned ? "차단됨" : character.online ? "접속 중" : "오프라인",
                FormatDate(character.createdUtc), FormatDate(character.lastConnectedUtc), FormatDate(character.lastDisconnectedUtc),
                FormatDuration(character), character.pingMilliseconds < 0 ? "—" : $"{character.pingMilliseconds} ms");
            row.Cells[1].ToolTipText = character.id;
        }
        RefreshCharacterActions();
    }

    private static string FormatDate(long value) => value is > 0 and <= 253402300799L
        ? DateTimeOffset.FromUnixTimeSeconds(value).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
        : "—";

    private static string FormatDuration(DedicatedAdminCharacter character)
    {
        long start = character.lastConnectedUtc;
        long end = character.online ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() : character.lastDisconnectedUtc;
        if (start <= 0 || end < start || end > 253402300799L)
            return "—";
        var duration = TimeSpan.FromSeconds(end - start);
        return $"{(long)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}";
    }

    private void RefreshControls()
    {
        var world = SelectedWorld;
        bool running = server != null;
        bool ready = server?.Connected == true && lastStatus != null && !stopping;
        worlds.Enabled = newWorld.Enabled = openWorld.Enabled = !running;
        startStop.Enabled = !stopping && world != null;
        startStop.Text = running ? "서버 정지" : "서버 시작";
        settings.Enabled = world != null && (!running || ready);
        command.Enabled = send.Enabled = ready;
        string state = stopping ? "저장 및 종료 중" : ready ? "실행 중" : running ? "시작 중" : "대기 중";
        int? port = lastStatus?.port ?? world?.Port;
        string endpoint = port.HasValue ? $"TCP {port.Value}" : "TCP —";
        string players = $"동시 접속 {lastStatus?.connected ?? 0}/{WorldCapacity.MaxConnections}";
        string capacity = lastStatus == null ? "" : $"   ·   캐릭터 {lastStatus.characters.Length}/{lastStatus.characterLimit}";
        string latency = "평균 지연 —";
        if (lastStatus != null)
        {
            var samples = lastStatus.characters.Where(character => character.online && character.pingMilliseconds >= 0).ToArray();
            if (samples.Length > 0)
                latency = $"평균 지연 {samples.Average(character => character.pingMilliseconds):0} ms";
        }
        summary.Text = $"{state}   ·   {endpoint}   ·   {players}{capacity}   ·   {latency}";
        RefreshCharacterActions();
    }

    private void RefreshCharacterActions()
    {
        var character = SelectedCharacter;
        ban.Enabled = delete.Enabled = character != null && server?.Connected == true && !stopping;
        ban.Text = character?.banned == true ? "차단 해제" : "차단";
    }
}
