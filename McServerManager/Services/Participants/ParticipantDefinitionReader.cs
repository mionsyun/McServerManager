using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using McServerManager.Models.Participants;

namespace McServerManager.Services.Participants;

internal sealed class ParticipantDefinitionException(string code, string path, string message) : Exception(message)
{
    public string Code { get; } = code;
    public string Path { get; } = path;
}

internal static class ParticipantDefinitionReader
{
    public static ParticipantClientDefinition Read(JsonElement root, string digest, CancellationToken cancellationToken)
    {
        Object(root, "$", ["schemaVersion", "definitionKind", "name", "runtime", "mods"]);
        Choice(root.GetProperty("schemaVersion"), "$.schemaVersion", "1.0");
        Choice(root.GetProperty("definitionKind"), "$.definitionKind", "client");
        var name = DisplayText(root.GetProperty("name"), "$.name", ParticipantDefinitionPolicy.MaxNameLength);

        var runtime = root.GetProperty("runtime");
        Object(runtime, "$.runtime", ["minecraftVersion", "loader", "loaderVersion", "javaVersion"]);
        var minecraft = Token(runtime.GetProperty("minecraftVersion"), "$.runtime.minecraftVersion",
            @"\A1\.[1-9][0-9]?(?:\.(?:0|[1-9][0-9]?))?\z");
        var loader = Choice(runtime.GetProperty("loader"), "$.runtime.loader", "fabric", "forge");
        var loaderVersion = Token(runtime.GetProperty("loaderVersion"), "$.runtime.loaderVersion",
            @"\A[0-9]+(?:\.[0-9]+){1,3}\z");
        var javaVersion = Token(runtime.GetProperty("javaVersion"), "$.runtime.javaVersion",
            @"\A[1-9][0-9]?(?:\.(?:0|[1-9][0-9]*)){2,3}(?:\+[1-9][0-9]*)?\z");

        var mods = root.GetProperty("mods");
        if (mods.ValueKind != JsonValueKind.Array)
            Fail("InvalidType", "$.mods", "MOD 一覧は配列で指定してください。");
        if (mods.GetArrayLength() > ParticipantDefinitionPolicy.MaxMods)
            Fail("TooManyMods", "$.mods", "MOD の件数上限を超えています。");
        var result = new List<ParticipantClientMod>();
        var projects = new HashSet<string>(StringComparer.Ordinal);
        var versions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var mod in mods.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = $"$.mods[{result.Count}]";
            Object(mod, path, ["provider", "projectId", "versionId", "name", "version", "clientSide"], "note");
            Choice(mod.GetProperty("provider"), path + ".provider", "modrinth");
            var project = Token(mod.GetProperty("projectId"), path + ".projectId", @"\A[A-Za-z0-9]{8}\z");
            var versionId = Token(mod.GetProperty("versionId"), path + ".versionId", @"\A[A-Za-z0-9]{8}\z");
            if (!projects.Add(project) || !versions.Add(versionId))
                Fail("DuplicateMod", path, "同じプロジェクトまたはバージョンを重複して指定できません。");
            var modName = DisplayText(mod.GetProperty("name"), path + ".name", ParticipantDefinitionPolicy.MaxNameLength);
            var version = Token(mod.GetProperty("version"), path + ".version", @"\A[A-Za-z0-9][A-Za-z0-9._+\-]*\z");
            if (new[] { "latest", "recommended", "stable", "release", "snapshot" }.Contains(version, StringComparer.OrdinalIgnoreCase)
                || Regex.IsMatch(version, @"(?:\A|[._+\-])[xX](?:\z|[._+\-])", RegexOptions.NonBacktracking))
                Fail("UnpinnedVersion", path + ".version", "正確な MOD バージョンを指定してください。");
            var clientSide = Choice(mod.GetProperty("clientSide"), path + ".clientSide", "required", "optional");
            var note = mod.TryGetProperty("note", out var noteValue)
                ? DisplayText(noteValue, path + ".note", ParticipantDefinitionPolicy.MaxNoteLength) : null;
            result.Add(new ParticipantClientMod(project, versionId, modName, version, clientSide, note));
        }

        return new ParticipantClientDefinition(name,
            new ParticipantClientRuntime(minecraft, loader, loaderVersion, javaVersion),
            Array.AsReadOnly(result.ToArray()), digest);
    }

    private static void Object(JsonElement value, string path, string[] required, string? optional = null)
    {
        if (value.ValueKind != JsonValueKind.Object)
            Fail("InvalidType", path, "JSON オブジェクトが必要です。");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!seen.Add(property.Name)) Fail("DuplicateProperty", path, "重複した項目は使用できません。");
            if (!required.Contains(property.Name, StringComparer.Ordinal) && property.Name != optional)
                Fail("UnknownProperty", path, "許可されていない項目が含まれています。");
        }
        foreach (var property in required)
            if (!seen.Contains(property)) Fail("MissingProperty", path + "." + property, "必須項目がありません。");
    }

    private static string Text(JsonElement value, string path, int maximum)
    {
        if (value.ValueKind != JsonValueKind.String) Fail("InvalidType", path, "文字列が必要です。");
        var text = value.GetString()!;
        if (text.Length == 0 || text.Length > maximum)
            Fail("InvalidLength", path, "文字列の長さが許容範囲外です。");
        // Strict encoding rejects unpaired escaped surrogates as well as raw invalid UTF-8.
        try { new UTF8Encoding(false, true).GetByteCount(text); }
        catch (EncoderFallbackException) { Fail("InvalidUnicode", path, "不正な Unicode 文字が含まれています。"); }
        return text;
    }

    private static string DisplayText(JsonElement value, string path, int maximum)
    {
        var text = Text(value, path, maximum);
        if (text != text.Trim() || text is "." or ".." || text.Contains("..", StringComparison.Ordinal))
            Fail("UnsafeText", path, "前後の空白やパス表現は使用できません。");
        foreach (var rune in text.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            var letterOrNumber = category is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter
                or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter
                or UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark
                or UnicodeCategory.DecimalDigitNumber or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber;
            if (!letterOrNumber && !" -_+.(),'[]&!、。・（）「」".EnumerateRunes().Contains(rune))
                Fail("UnsafeText", path, "制御文字、URL、パス、コマンドやマークアップ用の記号は使用できません。");
        }
        return text;
    }

    private static string Choice(JsonElement value, string path, params string[] choices)
    {
        var text = Text(value, path, ParticipantDefinitionPolicy.MaxVersionLength);
        if (!choices.Contains(text, StringComparer.Ordinal)) Fail("UnsupportedValue", path, "未対応の値です。");
        return text;
    }

    private static string Token(JsonElement value, string path, string pattern)
    {
        var text = Text(value, path, ParticipantDefinitionPolicy.MaxVersionLength);
        if (!Regex.IsMatch(text, pattern, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking))
            Fail("InvalidPin", path, "正確な識別子または固定バージョンを指定してください。");
        return text;
    }

    private static void Fail(string code, string path, string message) => throw new ParticipantDefinitionException(code, path, message);
}
