using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using McServerManager.Models.Templates;

namespace McServerManager.Services.Templates;

/// <summary>
/// Offline schema and local consistency checks only. Never downloads, installs, executes,
/// trusts an author, or confirms provider availability or runtime compatibility.
/// </summary>
public sealed class TemplateManifestService : ITemplateManifestService
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public TemplateValidationResult Parse(ReadOnlyMemory<byte> utf8Json)
    {
        if (utf8Json.Length > TemplatePolicy.MaxManifestBytes)
            return Failure("ManifestTooLarge", "$", "The manifest exceeds the 1 MiB limit.");

        try
        {
            // Snapshot caller-owned memory so validation and the digest describe the same bytes.
            utf8Json = utf8Json.ToArray();
            // JsonDocument alone can retain invalid UTF-8 in unaccessed string tokens.
            StrictUtf8.GetCharCount(utf8Json.Span);
            using var document = JsonDocument.Parse(utf8Json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = TemplatePolicy.MaxJsonDepth
            });
            var manifest = TemplateManifestReader.Read(document.RootElement);
            TemplateManifestConsistency.Validate(manifest);
            return new TemplateValidationResult
            {
                IsValid = true,
                Manifest = manifest,
                ManifestSha256 = Convert.ToHexString(SHA256.HashData(utf8Json.Span)).ToLowerInvariant(),
                Issues = Array.Empty<TemplateValidationIssue>()
            };
        }
        catch (DecoderFallbackException)
        {
            return Failure("InvalidUtf8", "$", "The manifest must contain valid UTF-8.");
        }
        catch (JsonException)
        {
            return Failure("InvalidJson", "$", "Invalid JSON or nesting beyond the maximum depth of 16.");
        }
        catch (InvalidOperationException)
        {
            // Includes invalid escaped Unicode in object property names.
            return Failure("InvalidUnicode", "$", "The manifest contains an invalid Unicode string.");
        }
        catch (TemplateManifestException exception)
        {
            return Failure(exception.Code, exception.Path, exception.Message);
        }
    }

    public async Task<TemplateValidationResult> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        cancellationToken.ThrowIfCancellationRequested();
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = TemplatePolicy.MaxManifestBytes + 1 - (int)output.Length;
            var read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
                break;
            output.Write(buffer, 0, read);
            if (output.Length > TemplatePolicy.MaxManifestBytes)
                return Failure("ManifestTooLarge", "$", "The manifest exceeds the 1 MiB limit.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        return Parse(output.GetBuffer().AsMemory(0, (int)output.Length));
    }

    private static TemplateValidationResult Failure(string code, string path, string message) => new()
    {
        IsValid = false,
        Issues = Array.AsReadOnly(new[] { new TemplateValidationIssue(code, path, message) })
    };
}
