using MaiPort.Models;

namespace MaiPort.Utilities;

/// <summary>
/// "19132" や "49152-49200" といった入力と <see cref="PortRange"/> を相互変換する。
/// </summary>
public static class PortRangeParser
{
    public const int MinPort = 1;
    public const int MaxPort = 65535;

    public static bool TryParse(string? text, out PortRange range)
    {
        range = new PortRange(0, 0);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        var separatorIndex = trimmed.IndexOf('-');
        if (separatorIndex < 0)
        {
            if (!TryParsePort(trimmed, out var single))
            {
                return false;
            }

            range = new PortRange(single, single);
            return true;
        }

        if (!TryParsePort(trimmed[..separatorIndex], out var start) ||
            !TryParsePort(trimmed[(separatorIndex + 1)..], out var end) ||
            end < start)
        {
            return false;
        }

        range = new PortRange(start, end);
        return true;
    }

    public static string Format(PortRange range)
    {
        return range.Start == range.End ? range.Start.ToString() : $"{range.Start}-{range.End}";
    }

    public static int Count(PortRange range) => range.End - range.Start + 1;

    public static bool IsValid(PortRange range)
    {
        return range.Start >= MinPort && range.End <= MaxPort && range.Start <= range.End;
    }

    public static IEnumerable<int> EnumeratePorts(PortRange range)
    {
        for (var port = range.Start; port <= range.End; port++)
        {
            yield return port;
        }
    }

    private static bool TryParsePort(string text, out int port)
    {
        return int.TryParse(text.Trim(), out port) && port is >= MinPort and <= MaxPort;
    }
}
