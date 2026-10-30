using UmamusumeResponseAnalyzer.Plugin;
using UmamusumeResponseAnalyzer.TerminalGui;

static class PluginLifecycleSmoke
{
    public static void Run(WorkspaceSmokeSession ui)
    {
        using var directory = new TempCurrentDirectory("WinSaddleAnalyzer-unused-lifecycle");
        using var context = new RuntimePluginContext(ui.Application);
        ui.Bootstrap.SwitchTo();
        IPlugin plugin = new WinSaddleAnalyzer.WinSaddleAnalyzer();
        try
        {
            plugin.Initialize(context);
            ui.Flush();
            if (!ReferenceEquals(Workspace.Current, ui.Bootstrap))
                throw new InvalidOperationException("Initialize changed the active workspace.");
        }
        finally
        {
            plugin.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        ui.Flush();
        if (!ReferenceEquals(Workspace.Current, ui.Bootstrap))
            throw new InvalidOperationException("Unused Dispose changed the active workspace.");
        Console.WriteLine("PASS WinSaddleAnalyzer unused workspace lifecycle");
    }
}
