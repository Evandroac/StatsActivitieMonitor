using System.Collections.ObjectModel;
using StatsMonitor.Helpers;
using StatsMonitor.Models;

namespace StatsMonitor.ViewModels;

/// <summary>Presentation state of the storage card (volumes and physical drives).</summary>
public sealed class DiskViewModel : ObservableObject
{
    private const string NA = ComponentViewModel.NotAvailable;
    private string _loadText = NA;

    public string Title => Loc.T("Discos");
    public ObservableCollection<VolumeItem> Volumes { get; } = [];
    public ObservableCollection<DriveItem> Drives { get; } = [];

    /// <summary>Highest activity among the drives, used by the gadget.</summary>
    public string LoadText { get => _loadText; private set => Set(ref _loadText, value); }

    public void Update(HardwareSnapshot s)
    {
        Sync(Volumes, s.Volumes.Count, () => new VolumeItem());
        for (int i = 0; i < s.Volumes.Count; i++)
        {
            var v = s.Volumes[i];
            double used = v.TotalGb - v.FreeGb;
            double pct = v.TotalGb > 0 ? used / v.TotalGb * 100 : 0;
            Volumes[i].Set(v.Name, pct, Loc.T("{0} de {1} usados ({2:0}%)", Size(used), Size(v.TotalGb), pct), Loc.T("{0} livres", Size(v.FreeGb)));
        }

        Sync(Drives, s.Disks.Count, () => new DriveItem());
        for (int i = 0; i < s.Disks.Count; i++)
        {
            var d = s.Disks[i];
            Drives[i].Set(d.Name,
                d.Temp is { } t ? Units.Temp(t) : NA,
                Speed(d.ReadBps), Speed(d.WriteBps),
                d.Activity is { } a ? $"{a:0}%" : NA,
                d.Health is { } hl ? Loc.T(hl) : NA,
                d.LifePercent is { } l ? $"{l:0}%" : NA);
        }
        LoadText = s.Disks.Select(d => d.Activity).Where(a => a.HasValue).DefaultIfEmpty(null).Max() is { } m ? $"{m:0}%" : NA;
    }

    private static void Sync<T>(ObservableCollection<T> list, int count, Func<T> make)
    {
        while (list.Count < count) list.Add(make());
        while (list.Count > count) list.RemoveAt(list.Count - 1);
    }

    private static string Size(double gb) => gb >= 1000 ? $"{gb / 1024:0.00} TB" : $"{gb:0.0} GB";

    private static string Speed(double? bps)
    {
        if (!bps.HasValue) return NA;
        double mb = bps.Value / 1048576;
        return mb >= 1 ? $"{mb:0.0} MB/s" : $"{bps.Value / 1024:0} KB/s";
    }
}

public sealed class VolumeItem : ObservableObject
{
    private string _name = "", _usage = "", _free = "";
    private double _percent;
    public string Name { get => _name; private set => Set(ref _name, value); }
    public double Percent { get => _percent; private set => Set(ref _percent, value); }
    public string Usage { get => _usage; private set => Set(ref _usage, value); }
    public string Free { get => _free; private set => Set(ref _free, value); }
    public void Set(string name, double percent, string usage, string free) { Name = name; Percent = percent; Usage = usage; Free = free; }
}

public sealed class DriveItem : ObservableObject
{
    private string _name = "", _temp = "", _read = "", _write = "", _activity = "", _health = "", _life = "";
    public string Name { get => _name; private set => Set(ref _name, value); }
    public string Temp { get => _temp; private set => Set(ref _temp, value); }
    public string Read { get => _read; private set => Set(ref _read, value); }
    public string Write { get => _write; private set => Set(ref _write, value); }
    public string Activity { get => _activity; private set => Set(ref _activity, value); }
    public string Health { get => _health; private set => Set(ref _health, value); }
    public string Life { get => _life; private set => Set(ref _life, value); }
    public void Set(string name, string temp, string read, string write, string activity, string health, string life)
    { Name = name; Temp = temp; Read = read; Write = write; Activity = activity; Health = health; Life = life; }
}
