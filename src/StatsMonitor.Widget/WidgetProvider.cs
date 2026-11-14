using System.Runtime.InteropServices;
using Microsoft.Windows.Widgets.Providers;

namespace StatsMonitor.Widget;

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
[Guid("7F3C2B6E-5D14-4A9B-9E21-3B8A1C0D4F52")]
public sealed class WidgetProvider : IWidgetProvider
{
    private static readonly string Template = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Templates", "summary.json"));
    private readonly Dictionary<string, string> _active = new();
    private readonly object _lock = new();
    private readonly Timer _timer;

    public WidgetProvider()
    {
        _timer = new Timer(_ => RefreshAll(), null, Timeout.Infinite, Timeout.Infinite);
        foreach (var info in WidgetManager.GetDefault().GetWidgetInfos())
            _active[info.WidgetContext.Id] = info.WidgetContext.DefinitionId;
        if (_active.Count > 0) StartTimer();
    }

    public void CreateWidget(WidgetContext context)
    {
        lock (_lock) _active[context.Id] = context.DefinitionId;
        Update(context.Id);
        StartTimer();
    }

    public void DeleteWidget(string widgetId, string customState)
    {
        lock (_lock)
        {
            _active.Remove(widgetId);
            if (_active.Count == 0) _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    public void OnActionInvoked(WidgetActionInvokedArgs actionInvokedArgs) { }

    public void OnWidgetContextChanged(WidgetContextChangedArgs contextChangedArgs) => Update(contextChangedArgs.WidgetContext.Id);

    public void Activate(WidgetContext widgetContext)
    {
        lock (_lock) _active[widgetContext.Id] = widgetContext.DefinitionId;
        Update(widgetContext.Id);
        StartTimer();
    }

    // Only visible widgets are refreshed, so nothing runs while the panel is closed.
    public void Deactivate(string widgetId)
    {
        lock (_lock)
        {
            _active.Remove(widgetId);
            if (_active.Count == 0) _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    private void StartTimer() => _timer.Change(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

    private void RefreshAll()
    {
        string[] ids;
        lock (_lock) ids = _active.Keys.ToArray();
        foreach (var id in ids) Update(id);
    }

    private static void Update(string widgetId)
    {
        try
        {
            WidgetManager.GetDefault().UpdateWidget(new WidgetUpdateRequestOptions(widgetId)
            {
                Template = Template,
                Data = WidgetFeed.BuildData(),
            });
        }
        catch (Exception) { }
    }
}
