using System.Text;

namespace Lux;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length > 0)
            return RunCommand(args);

        // Schon offen: das vorhandene Fenster reicht.
        using var mutex = new Mutex(true, @"Local\Lux", out bool firstInstance);
        if (!firstInstance)
            return 0;

        ApplicationConfiguration.Initialize();
        Application.Run(new Flyout());
        return 0;
    }

    /// <summary>Für Verknüpfungen und Tastenkürzel: --set 50, --change +10 / -10, --dump; --restore für die Anmeldung.</summary>
    static int RunCommand(string[] args)
    {
        using var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
        var settings = Settings.Load();
        var monitors = Monitor.All();
        try
        {
            // Bei der Anmeldung: zuletzt gewählte Helligkeit erneut setzen, ohne erst zu lesen (der Samsung
            // verweigert das Lesen nach einem Neustart, nimmt Befehle aber an).
            if (args[0] == "--restore")
            {
                foreach (var monitor in monitors)
                    if (settings.LastBrightness.TryGetValue(monitor.Name, out int last))
                    {
                        monitor.Set(last);
                        output.WriteLine($"{monitor.Name}: {last * 100 / monitor.Max} %");
                    }
                return 0;
            }
            foreach (var monitor in monitors)
                monitor.Read();
            switch (args[0])
            {
                // Setzen geht auch bei Monitoren, die das Lesen verweigern; sie nehmen Befehle trotzdem an.
                case "--set" when args.Length > 1 && int.TryParse(args[1].TrimEnd('%'), out int percent):
                    foreach (var monitor in monitors)
                        monitor.Set(percent * monitor.Max / 100);
                    break;
                case "--change" when args.Length > 1 && int.TryParse(args[1].TrimEnd('%'), out int delta):
                    foreach (var monitor in monitors)
                        if ((monitor.Brightness ?? settings.LastBrightness.GetValueOrDefault(monitor.Name, -1)) is int current and >= 0)
                            monitor.Set(current + delta * monitor.Max / 100);
                    break;
                case "--dump":
                    break;
                default:
                    output.WriteLine("Aufruf: Lux.exe [--set <Prozent> | --change <+/-Prozent> | --dump | --restore]");
                    return 2;
            }
            foreach (var monitor in monitors)
            {
                output.WriteLine($"{monitor.Name}{(monitor.IsPrimary ? " (Hauptmonitor)" : "")}: "
                    + (monitor.Brightness is int value ? $"{value * 100 / monitor.Max} %" : "antwortet nicht"));
                if (monitor.Brightness is int known)
                    settings.LastBrightness[monitor.Name] = known;
            }
            settings.Save();
            return 0;
        }
        finally
        {
            foreach (var monitor in monitors)
                monitor.Dispose();
        }
    }
}
