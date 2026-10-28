using McServerManager.Models.Transactions;

namespace McServerManager.Services.Transactions;

/// <summary>Trusted injected synthetic fixtures only. No production download/path adapter is included.</summary>
public interface ISyntheticArtifactSource
{
    bool IsSynthetic { get; }
    Task<Stream> OpenReadAsync(TransactionArtifact artifact, CancellationToken cancellationToken);
}
