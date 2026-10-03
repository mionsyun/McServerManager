using System.Collections.Immutable;
using System.Text;
using McServerManager.Models.Transactions;
using McServerManager.Services.Transactions;

namespace McServerManager.TemplateTests.Transactions;

internal sealed class SyntheticSource : ISyntheticArtifactSource
{
    public bool IsSynthetic { get; set; } = true;
    public int Reads { get; private set; }
    public Dictionary<string, byte[]> Data { get; } = [];
    public Func<TransactionArtifact, Stream>? Open { get; set; }
    public Task<Stream> OpenReadAsync(TransactionArtifact artifact, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Reads++;
        return Task.FromResult(Open?.Invoke(artifact) ?? new MemoryStream(Data[artifact.ArtifactId], writable: false));
    }
}

internal static class TransactionFixtures
{
    public static readonly TransactionPlanService Plans = new();
    public static TransactionReview Review => new(new string('a', 64), new string('b', 64), "synthetic", "v1", 1024, 25565,
        [new("motd", "synthetic test only"), new("difficulty", "normal")]);
    public static TransactionTarget Target => new("store", "anchor", "new-server", "server-1", "absent-1", "lease-1");
    public static TransactionArtifact Artifact(string id, byte[] bytes, string? destination = null) =>
        new("synthetic", "project-" + id, "version-1", id, destination ?? "payloads/" + id + ".bin", bytes.Length,
            IsolatedDirectoryTransactionStore.Hash(bytes));
    public static TransactionPlan Plan(TransactionTarget? target = null, params TransactionArtifact[] artifacts) =>
        Plans.Prepare(Guid.NewGuid(), target ?? Target, Review,
            artifacts.Length == 0 ? [Artifact("one", Encoding.UTF8.GetBytes("synthetic payload one"))] : artifacts);
    public static TransactionApproval Approval(TransactionPlan plan) => new(Plans.GetFingerprint(plan));
    public static (TransactionPlan Plan, SyntheticSource Source, TransactionExecutor Executor) Setup(IsolatedDirectoryTransactionStore store, int count = 2)
    {
        var source = new SyntheticSource();
        var artifacts = Enumerable.Range(1, count).Select(i =>
        {
            var id = "item-" + i;
            var bytes = Encoding.UTF8.GetBytes("synthetic fixture payload " + i);
            source.Data[id] = bytes;
            return Artifact(id, bytes);
        }).ToArray();
        var plan = Plan(store.Target(), artifacts);
        return (plan, source, new(Plans, store, source));
    }
}
