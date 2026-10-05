using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using McServerManager.Models.Participants;

namespace McServerManager.Services.Participants;

/// <summary>
/// Parses explicit client JSON only. Never reads server artifacts, resolves dependencies,
/// contacts a provider, downloads a MOD or verifies provider declarations.
/// </summary>
public sealed class ParticipantClientDefinitionService : IParticipantClientDefinitionService
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public ParticipantDefinitionValidationResult Parse(ReadOnlyMemory<byte> utf8Json,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (utf8Json.Length > ParticipantDefinitionPolicy.MaxDefinitionBytes)
            return Failure("DefinitionTooLarge", "$", "クライアント定義のサイズ上限を超えています。");

        try
        {
            // Never retain caller-owned mutable bytes across validation and hashing.
            var snapshot = utf8Json.ToArray();
            StrictUtf8.GetCharCount(snapshot);
            using var document = JsonDocument.Parse(snapshot, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = ParticipantDefinitionPolicy.MaxJsonDepth
            });
            var digest = Convert.ToHexString(SHA256.HashData(snapshot)).ToLowerInvariant();
            var definition = ParticipantDefinitionReader.Read(document.RootElement, digest, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return new(definition, Array.Empty<ParticipantDefinitionIssue>());
        }
        catch (DecoderFallbackException)
        {
            return Failure("InvalidUtf8", "$", "定義は正しい UTF-8 で記述してください。");
        }
        catch (JsonException)
        {
            return Failure("InvalidJson", "$", "JSON の形式または階層の深さが不正です。");
        }
        catch (InvalidOperationException)
        {
            return Failure("InvalidUnicode", "$", "不正な Unicode 文字が含まれています。");
        }
        catch (ParticipantDefinitionException exception)
        {
            return Failure(exception.Code, exception.Path, exception.Message);
        }
    }

    public async Task<ParticipantDefinitionValidationResult> ReadAsync(Stream input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        using var snapshot = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = ParticipantDefinitionPolicy.MaxDefinitionBytes + 1 - (int)snapshot.Length;
            var read = await input.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0) break;
            snapshot.Write(buffer, 0, read);
            if (snapshot.Length > ParticipantDefinitionPolicy.MaxDefinitionBytes)
                return Failure("DefinitionTooLarge", "$", "クライアント定義のサイズ上限を超えています。");
        }
        return Parse(snapshot.GetBuffer().AsMemory(0, (int)snapshot.Length), cancellationToken);
    }

    private static ParticipantDefinitionValidationResult Failure(string code, string path, string message) =>
        new(null, Array.AsReadOnly(new[] { new ParticipantDefinitionIssue(code, path, message) }));
}
