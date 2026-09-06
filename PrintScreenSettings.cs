namespace PrintFreeTool;

public enum PrintScreenCaptureMode
{
    AllScreens,
    SelectedScreens,
    EachScreenSeparately
}

public sealed record PrintScreenSettings(PrintScreenCaptureMode Mode, List<string> SelectedMonitorIds)
{
    public static PrintScreenSettings Default { get; } = new(PrintScreenCaptureMode.AllScreens, []);
}
