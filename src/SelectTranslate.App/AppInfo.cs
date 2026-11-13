namespace SelectTranslate.App;

public static class AppInfo
{
    public const string Name = "SelectTranslate";

    public const string DisplayName = "SelectTranslate（划词译）";

    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";

    public static string Framework { get; } = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
}