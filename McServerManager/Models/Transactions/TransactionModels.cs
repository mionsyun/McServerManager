using System.Collections.Immutable;

namespace McServerManager.Models.Transactions;

/// <summary>Logical storage identities, never caller-supplied filesystem paths. Only absent, NEW targets are supported.</summary>
public sealed record TransactionTarget(string StorageId, string AnchorIdentity, string TargetName,
    string ServerIdentity, string AbsentBaseline, string StoppedLease);

/// <summary>Exact synthetic input identity; no URL, local source path, or implicit latest version.</summary>
public sealed record TransactionArtifact(string Provider, string ProjectId, string VersionId,
    string ArtifactId, string Destination, long SizeBytes, string Sha256);

public sealed record TransactionSetting(string Key, string Value);

/// <summary>Review context is bound to approval, not verified as genuine provider evidence by this synthetic core.</summary>
public sealed record TransactionReview(string SourceManifestSha256, string InspectionEvidenceSha256,
    string RuntimeIdentity, string RuntimeVersion, int MemoryMiB, int Port,
    ImmutableArray<TransactionSetting> Settings);

/// <summary>Deeply immutable, bounded synthetic-only plan. All operations are additions to an absent target.</summary>
public sealed record TransactionPlan(Guid InstallId, TransactionTarget Target, TransactionReview Review,
    ImmutableArray<TransactionArtifact> Artifacts);

/// <summary>
/// Host attestation that this exact fingerprint was explicitly confirmed. This is not a cryptographic
/// user-authentication token. Production UI must never manufacture it from a review-only result.
/// </summary>
public sealed record TransactionApproval(string PlanFingerprint);

/// <summary>Opaque ownership capability for one attempt; contains no path and can never authorize deleting a target.</summary>
public sealed record TransactionReceipt(Guid InstallId, Guid OwnerToken, string StorageId, string PlanFingerprint);

public enum TransactionStorageMode { Unsupported, IsolatedSyntheticTests }

public sealed record TransactionStorageCapabilities(TransactionStorageMode Mode, bool AtomicNoReplacePublish,
    bool ExclusiveStoppedLease, bool VerifiedOwnedStage, bool StageOutsideDiscovery);

public enum TransactionObservation { NotStarted, OwnedStage, OtherAttempt, Published, ConflictingPlan, Unknown }
public enum TransactionPublishDecision { Published, Rejected }
public enum TransactionCleanupDecision { Removed, AlreadyAbsent, RefusedForeignOrChanged, Failed }
public enum TransactionStatus { Committed, Rejected, Cancelled, Failed, Recovered, RecoveryRequired, PublicationUnknown }

public sealed record TransactionResult(TransactionStatus Status, string Code, string Detail = "",
    TransactionReceipt? RecoveryReceipt = null);
