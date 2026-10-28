using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using McServerManager.Models.Transactions;

namespace McServerManager.Services.Transactions;

public sealed class TransactionPlanService : ITransactionPlanService
{
    public TransactionPlan Prepare(Guid installId, TransactionTarget target, TransactionReview review,
        IEnumerable<TransactionArtifact> artifacts)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        ArgumentNullException.ThrowIfNull(review);
        // Bound enumeration before materialization; callers cannot supply a mutable backing collection.
        var snapshot = artifacts.Take(TransactionPolicy.MaxArtifacts + 1).ToImmutableArray();
        var plan = new TransactionPlan(installId, target, review, snapshot);
        TransactionPolicy.Validate(plan);
        return plan with
        {
            Artifacts = snapshot.OrderBy(x => x.Destination, StringComparer.Ordinal).ToImmutableArray(),
            Review = review with { Settings = review.Settings.OrderBy(x => x.Key, StringComparer.Ordinal).ToImmutableArray() }
        };
    }

    public string GetFingerprint(TransactionPlan plan)
    {
        TransactionPolicy.Validate(plan);
        using var bytes = new MemoryStream();
        using (var writer = new BinaryWriter(bytes, Encoding.UTF8, leaveOpen: true))
        {
            // Versioned, length-prefixed encoding. No ambiguous concatenation, locale, JSON, or object hash codes.
            writer.Write("MaiPilot.synthetic.new-target.v1");
            writer.Write(plan.InstallId.ToString("D"));
            writer.Write(plan.Target.StorageId);
            writer.Write(plan.Target.AnchorIdentity);
            writer.Write(plan.Target.TargetName);
            writer.Write(plan.Target.ServerIdentity);
            writer.Write(plan.Target.AbsentBaseline);
            writer.Write(plan.Target.StoppedLease);
            writer.Write(plan.Review.SourceManifestSha256);
            writer.Write(plan.Review.InspectionEvidenceSha256);
            writer.Write(plan.Review.RuntimeIdentity);
            writer.Write(plan.Review.RuntimeVersion);
            writer.Write(plan.Review.MemoryMiB);
            writer.Write(plan.Review.Port);
            writer.Write(plan.Review.Settings.Length);
            foreach (var setting in plan.Review.Settings.OrderBy(x => x.Key, StringComparer.Ordinal))
            { writer.Write(setting.Key); writer.Write(setting.Value); }
            writer.Write(plan.Artifacts.Length);
            foreach (var artifact in plan.Artifacts.OrderBy(x => x.Destination, StringComparer.Ordinal))
            {
                writer.Write(artifact.Provider);
                writer.Write(artifact.ProjectId);
                writer.Write(artifact.VersionId);
                writer.Write(artifact.ArtifactId);
                writer.Write(artifact.Destination);
                writer.Write(artifact.SizeBytes);
                writer.Write(artifact.Sha256);
            }
        }
        return Convert.ToHexString(SHA256.HashData(bytes.GetBuffer().AsSpan(0, checked((int)bytes.Length)))).ToLowerInvariant();
    }
}
