namespace OppOcr.Web.Extensions;

/// <summary>
/// Development convenience: loads the repository's <c>.env</c> (the same file docker compose uses)
/// so <c>dotnet run</c> and IDEs pick up OCR_API_* without exporting them first. Real environment
/// variables still win, and nothing is loaded outside Development.
/// </summary>
internal static class DotEnvConfiguration
{
    public const string FileName = ".env";

    public static WebApplicationBuilder AddDotEnvInDevelopment(this WebApplicationBuilder builder)
    {
        if (!builder.Environment.IsDevelopment())
            return builder;

        var path = FindUpwards(builder.Environment.ContentRootPath);
        if (path is null)
            return builder;

        var values = Parse(File.ReadLines(path))
            .Where(kv => Environment.GetEnvironmentVariable(kv.Key) is null)
            .ToDictionary(kv => kv.Key, kv => (string?)kv.Value);
        builder.Configuration.AddInMemoryCollection(values);
        return builder;
    }

    internal static IEnumerable<KeyValuePair<string, string>> Parse(IEnumerable<string> lines)
    {
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;
            if (line.StartsWith("export ", StringComparison.Ordinal))
                line = line["export ".Length..].TrimStart();

            var separator = line.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
                continue;

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0])
                value = value[1..^1];

            // Empty values mean "not set", so they don't shadow appsettings.
            if (value.Length > 0)
                yield return new(key, value);
        }
    }

    private static string? FindUpwards(string start)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, FileName);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
