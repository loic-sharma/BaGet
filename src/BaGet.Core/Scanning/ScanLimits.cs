namespace BaGet.Core;

/// <summary>Column lengths of the scanning tables (shared by the model and the code that truncates to them).</summary>
public static class ScanLimits
{
    public const int NameLength = 400;
    public const int VersionLength = 200;
    public const int IdLength = 200;
    public const int SummaryLength = 1000;
    public const int UrlLength = 1000;
    public const int ErrorLength = 1000;
    public const int ScannerLength = 32;
}
