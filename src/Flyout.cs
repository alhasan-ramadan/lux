using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Lux;

/// <summary>
/// Kleines Fenster über der Taskleiste: ein Regler pro Monitor, darunter feste Stufen für alle.
/// Schließt sich, sobald woanders hingeklickt wird; danach läuft LUX nicht mehr.
/// </summary>
sealed class Flyout : Form
{
    const int ContentWidth = 300;

    readonly Settings settings = Settings.Load();
    readonly List<Monitor> monitors = Monitor.All();
    readonly List<Row> rows = [];
    readonly CheckBox? together;
    bool syncing;

    sealed record Row(Monitor Monitor, TrackBar Bar, Label Value);

    public Flyout()
    {
        bool light = LightTheme();
        var back = light ? Color.FromArgb(0xF9, 0xF9, 0xF9) : Color.FromArgb(0x2B, 0x2B, 0x2B);
        var fore = light ? Color.FromArgb(0x1A, 0x1A, 0x1A) : Color.FromArgb(0xF2, 0xF2, 0xF2);
        var muted = light ? Color.FromArgb(0x5F, 0x5F, 0x5F) : Color.FromArgb(0xB0, 0xB0, 0xB0);
        var buttonBack = light ? Color.White : Color.FromArgb(0x3A, 0x3A, 0x3A);
        var buttonLine = light ? Color.FromArgb(0xC8, 0xC8, 0xC8) : Color.FromArgb(0x55, 0x55, 0x55);

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "LUX";
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = true;
        TopMost = true;
        KeyPreview = true;
        BackColor = back;
        ForeColor = fore;
        Font = new Font("Segoe UI", 9.75f);

        int x = 16, y = 12;
        Controls.Add(new Label { Text = "Helligkeit", AutoSize = true, Location = new Point(x, y), Font = new Font(Font, FontStyle.Bold) });
        y += 30;

        foreach (var monitor in monitors)
        {
            string name = monitor.IsPrimary ? $"{monitor.Name} (Hauptmonitor)" : monitor.Name;
            Controls.Add(new Label { Text = name, AutoEllipsis = true, Location = new Point(x, y), Size = new Size(ContentWidth - 70, 20) });
            var value = new Label
            {
                Text = "…", ForeColor = muted, TextAlign = ContentAlignment.TopRight,
                Location = new Point(x + ContentWidth - 70, y), Size = new Size(70, 20),
            };
            Controls.Add(value);
            y += 22;
            var bar = new TrackBar
            {
                Minimum = 0, Maximum = monitor.Max, TickStyle = TickStyle.None, AutoSize = false, Enabled = false,
                SmallChange = 1, LargeChange = 10, BackColor = back,
                Location = new Point(x - 6, y), Size = new Size(ContentWidth + 12, 30),
            };
            var row = new Row(monitor, bar, value);
            bar.ValueChanged += (_, _) =>
            {
                if (!syncing)
                    Apply(row, bar.Value);
            };
            Controls.Add(bar);
            rows.Add(row);
            y += 38;
        }

        if (monitors.Count == 0)
        {
            Controls.Add(new Label { Text = "Kein Monitor gefunden, der DDC/CI kann.", AutoSize = true, ForeColor = muted, Location = new Point(x, y) });
            y += 30;
        }
        else
        {
            if (monitors.Count > 1)
            {
                together = new CheckBox { Text = "Alle gemeinsam bewegen", AutoSize = true, Checked = settings.Together, Location = new Point(x, y) };
                Controls.Add(together);
                y += 32;
            }

            int[] steps = [25, 50, 75, 100];
            const int gap = 8;
            int width = (ContentWidth - gap * (steps.Length - 1)) / steps.Length;
            for (int i = 0; i < steps.Length; i++)
            {
                int percent = steps[i];
                var step = new Button
                {
                    Text = $"{percent} %", FlatStyle = FlatStyle.Flat, BackColor = buttonBack, ForeColor = fore, Cursor = Cursors.Hand,
                    Location = new Point(x + i * (width + gap), y), Size = new Size(width, 30),
                };
                step.FlatAppearance.BorderColor = buttonLine;
                step.Click += (_, _) => SetAll(percent);
                Controls.Add(step);
            }
            y += 30;
        }

        ClientSize = new Size(ContentWidth + 2 * x, y + 14);
        ResumeLayout(false);

        // Wie ein Menü: Klick daneben schließt. Startet LUX ohne Vordergrundrecht (etwa aus einem Skript),
        // nimmt Windows ihm den Fokus sofort wieder; dann offen bleiben, bis jemand hineinklickt.
        var activatedAt = DateTime.MinValue;
        Activated += (_, _) => activatedAt = DateTime.Now;
        Deactivate += (_, _) =>
        {
            if (DateTime.Now - activatedAt > TimeSpan.FromMilliseconds(300))
                Close();
        };
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
                Close();
        };
        Shown += async (_, _) => await LoadValuesAsync();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // Über dem Mauszeiger, also über dem angehefteten Symbol, innerhalb des Bildschirms.
        var cursor = Cursor.Position;
        var area = Screen.FromPoint(cursor).WorkingArea;
        int left = Math.Clamp(cursor.X - Width / 2, area.Left + 12, Math.Max(area.Left + 12, area.Right - Width - 12));
        int top = cursor.Y > area.Top + area.Height / 2 ? area.Bottom - Height - 12 : area.Top + 12;
        Location = new Point(left, top);
        // Windows 11: runde Ecken wie bei den Fenstern der Taskleiste.
        int round = DWMWCP_ROUND;
        DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
        Activate();
    }

    /// <summary>
    /// Liest alle Monitore parallel; jeder Regler wird frei, sobald sein Monitor fertig ist. Manche Monitore
    /// (der Samsung C24F390 nach einem Neustart) verweigern das Lesen, nehmen Befehle aber an. Dann bleibt der
    /// Regler bedienbar und startet beim zuletzt bekannten Wert, mit „≈“ davor.
    /// </summary>
    Task LoadValuesAsync() => Task.WhenAll(rows.Select(async row =>
    {
        bool answered = await Task.Run(row.Monitor.Read);
        int value = row.Monitor.Brightness ?? settings.LastBrightness.GetValueOrDefault(row.Monitor.Name, row.Monitor.Max);
        syncing = true;
        row.Bar.Maximum = row.Monitor.Max;
        row.Bar.Value = Math.Clamp(value, 0, row.Monitor.Max);
        row.Bar.Enabled = true;
        row.Value.Text = answered ? Percent(row.Monitor, value) : $"≈ {Percent(row.Monitor, value)}";
        syncing = false;
    }));

    void Apply(Row source, int value)
    {
        source.Monitor.Set(value);
        source.Value.Text = Percent(source.Monitor, value);
        if (together?.Checked != true)
            return;
        syncing = true;
        foreach (var row in rows.Where(r => r != source && r.Bar.Enabled))
            SetRow(row, value * row.Monitor.Max / source.Monitor.Max);
        syncing = false;
    }

    void SetAll(int percent)
    {
        syncing = true;
        foreach (var row in rows.Where(r => r.Bar.Enabled))
            SetRow(row, percent * row.Monitor.Max / 100);
        syncing = false;
    }

    static void SetRow(Row row, int value)
    {
        row.Bar.Value = Math.Clamp(value, 0, row.Monitor.Max);
        row.Monitor.Set(value);
        row.Value.Text = Percent(row.Monitor, value);
    }

    static string Percent(Monitor monitor, int value) => $"{value * 100 / monitor.Max} %";

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        bool changed = together is not null && together.Checked != settings.Together;
        settings.Together = together?.Checked ?? settings.Together;
        foreach (var monitor in monitors.Where(m => m.Brightness is not null))
        {
            changed |= settings.LastBrightness.GetValueOrDefault(monitor.Name, -1) != monitor.Brightness;
            settings.LastBrightness[monitor.Name] = monitor.Brightness!.Value;
        }
        if (changed)
            settings.Save();
        foreach (var monitor in monitors)
            monitor.Dispose();
        base.OnFormClosed(e);
    }

    static bool LightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value && value == 1;
    }

    const int DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_ROUND = 2;

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
