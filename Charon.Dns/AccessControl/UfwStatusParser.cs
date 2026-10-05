using System.Net;

namespace Charon.Dns.AccessControl;

public static class UfwStatusParser
{
    public static async Task<bool> IsActive(IAsyncEnumerable<string> ufwStatusLines)
    {
        var isActive = false;
        await foreach (var line in ufwStatusLines)
        {
            if (line.Contains("Status: active", StringComparison.OrdinalIgnoreCase))
            {
                isActive = true;
            }
        }

        return isActive;
    }

    public static async IAsyncEnumerable<IPAddress> ExtractBlockedIps(
        IAsyncEnumerable<string> ufwStatusLines,
        string tag)
    {
        await foreach (var line in ufwStatusLines)
        {
            if (!line.Contains(tag, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var token in line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (IPAddress.TryParse(token, out var ip))
                {
                    yield return ip;
                }
            }
        }
    }
}
