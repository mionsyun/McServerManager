using McServerManager.Models.Templates;

namespace McServerManager.Services.Templates;

public interface ITemplateManifestService
{
    TemplateValidationResult Parse(ReadOnlyMemory<byte> utf8Json);

    /// <summary>Reads at most the manifest limit plus one byte, and leaves the caller's stream open.</summary>
    Task<TemplateValidationResult> ReadAsync(Stream stream, CancellationToken cancellationToken = default);
}
