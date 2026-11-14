using System.Text.Json.Serialization;

namespace StatsMonitor.Diagnostics;

/// <summary>Classification of a finding or of a component health.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Severity { Normal, Info, Warning, Critical, Indeterminate }

public static class SeverityInfo
{
    /// <summary>Worst-first ordering used to roll findings up into a component state.</summary>
    public static int Rank(Severity s) => s switch { Severity.Critical => 4, Severity.Warning => 3, Severity.Indeterminate => 2, Severity.Info => 1, _ => 0 };

    public static Severity Worst(IEnumerable<Severity> items)
    {
        var worst = Severity.Normal;
        foreach (var s in items) if (Rank(s) > Rank(worst)) worst = s;
        return worst;
    }

    public static string Label(Severity s, bool en) => s switch
    {
        Severity.Normal => "NORMAL",
        Severity.Info => en ? "INFORMATIONAL" : "INFORMATIVO",
        Severity.Warning => en ? "WARNING" : "ATENÇÃO",
        Severity.Critical => en ? "CRITICAL" : "CRÍTICO",
        _ => en ? "UNDETERMINED" : "INDETERMINADO",
    };

    public static string Icon(Severity s) => s switch { Severity.Normal => "✅", Severity.Info => "ℹ️", Severity.Warning => "⚠️", Severity.Critical => "⛔", _ => "❔" };
}

/// <summary>Component keys shared by the inventory, the tests and the findings.</summary>
public static class Comp
{
    public const string Hardware = "hardware", Cpu = "cpu", Memory = "memory", Gpu = "gpu", Storage = "storage", Battery = "battery",
        Os = "os", Drivers = "drivers", Network = "network", Security = "security";

    public static readonly string[] All = [Hardware, Cpu, Memory, Gpu, Storage, Battery, Os, Drivers, Network, Security];

    public static string Title(string key, bool en) => key switch
    {
        Hardware => "Hardware",
        Cpu => en ? "Processor" : "Processador",
        Memory => en ? "RAM" : "Memória RAM",
        Gpu => en ? "Graphics card" : "Placa de vídeo",
        Storage => en ? "Storage" : "Armazenamento",
        Battery => en ? "Battery and power" : "Bateria e energia",
        Os => en ? "Operating system" : "Sistema operacional",
        Drivers => en ? "Drivers and devices" : "Drivers e dispositivos",
        Network => en ? "Network and connectivity" : "Rede e conectividade",
        Security => en ? "Security" : "Segurança",
        _ => key,
    };
}

public sealed class Fact
{
    public string Label { get; set; } = "";
    public string Value { get; set; } = "";
    public Fact() { }
    public Fact(string label, string value) { Label = label; Value = value; }
}

/// <summary>Everything collected from the machine. Numbers and strings are keyed by "component.name"; a missing key means the data was not available.</summary>
public sealed class Inventory
{
    public Dictionary<string, List<Fact>> Sections { get; set; } = new();
    public Dictionary<string, double> Num { get; set; } = new();
    public Dictionary<string, string> Str { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    /// <summary>Components whose collection finished (even with errors).</summary>
    public List<string> Collected { get; set; } = new();

    public double? N(string key) => Num.TryGetValue(key, out var v) ? v : null;
    public string? S(string key) => Str.TryGetValue(key, out var v) ? v : null;
}

public sealed class TestResult
{
    public string Name { get; set; } = "";
    public string Component { get; set; } = "";
    public Severity Result { get; set; }
    public string Summary { get; set; } = "";
    public double? Value { get; set; }
    public string? Unit { get; set; }
    public double DurationMs { get; set; }
    public DateTime Time { get; set; } = DateTime.Now;
}

public sealed class Finding
{
    public string Component { get; set; } = "";
    public string Problem { get; set; } = "";
    public DateTime Time { get; set; } = DateTime.Now;
    public string Evidence { get; set; } = "";
    public Severity Level { get; set; }
    public string Cause { get; set; } = "";
    public string Recommendation { get; set; } = "";
    public string TestResult { get; set; } = "";
}

public sealed class DiagnosticSession
{
    public string Id { get; set; } = DateTime.Now.ToString("yyyyMMdd-HHmmss");
    public DateTime Started { get; set; } = DateTime.Now;
    public DateTime? Finished { get; set; }
    public bool Full { get; set; }
    public bool Elevated { get; set; }
    public string Computer { get; set; } = "";
    public string Model { get; set; } = "";
    public string AppVersion { get; set; } = "";
    public Inventory Inventory { get; set; } = new();
    public List<TestResult> Tests { get; set; } = new();
    public List<Finding> Findings { get; set; } = new();
    public Dictionary<string, Severity> Health { get; set; } = new();
    public int? Score { get; set; }
}
