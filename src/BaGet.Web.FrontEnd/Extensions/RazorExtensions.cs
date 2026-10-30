using Humanizer;

namespace BaGet.Web.V3.Extensions;

public static class RazorExtensions
{
    public static string ToMetric(this long value)
    {
        return ((double) value).ToMetric();
    }
}