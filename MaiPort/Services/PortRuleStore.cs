using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MaiPort.Models;

namespace MaiPort.Services;

/// <summary>
/// 開放済みポートの記録を %LOCALAPPDATA%\MaiPort\rules.json に保存する。
/// </summary>
public sealed class PortRuleStore : IPortRuleStore
{
    private const int MaxRetryCount = 3;
    private const int RetryDelayMs = 200;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _directory;
    private readonly string _filePath;

    public PortRuleStore()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MaiPort"))
    {
    }

    public PortRuleStore(string directory)
    {
        _directory = directory;
        _filePath = Path.Combine(directory, "rules.json");
    }

    public async Task<IReadOnlyList<PortRule>> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        try
        {
            await using var stream = File.OpenRead(_filePath);
            var rules = await JsonSerializer.DeserializeAsync<List<PortRule>>(stream, SerializerOptions, ct).ConfigureAwait(false);
            return rules ?? [];
        }
        catch (JsonException)
        {
            // 壊れた記録ファイルで起動できなくならないよう、空として扱う。
            return [];
        }
    }

    public async Task SaveAsync(IReadOnlyList<PortRule> rules, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_directory);
        var json = JsonSerializer.Serialize(rules, SerializerOptions);
        var tempPath = _filePath + ".tmp";

        for (var attempt = 1; attempt <= MaxRetryCount; attempt++)
        {
            try
            {
                await File.WriteAllTextAsync(tempPath, json, ct).ConfigureAwait(false);
                File.Move(tempPath, _filePath, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < MaxRetryCount)
            {
                // ウイルス対策ソフトなどによる一時的なロックを想定してリトライする。
                await Task.Delay(RetryDelayMs * attempt, ct).ConfigureAwait(false);
            }
        }
    }
}
