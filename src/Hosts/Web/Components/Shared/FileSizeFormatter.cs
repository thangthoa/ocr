using System.Globalization;

namespace OppOcr.Web.Components.Shared;

public static class FileSizeFormatter
{
    public static string Format(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => (bytes / 1024d).ToString("0.#", CultureInfo.CurrentCulture) + " KB",
        _ => (bytes / 1024d / 1024d).ToString("0.#", CultureInfo.CurrentCulture) + " MB",
    };
}
