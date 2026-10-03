using McServerManager.Models.Transactions;

namespace McServerManager.Services.Transactions;

public interface ITransactionPlanService
{
    TransactionPlan Prepare(Guid installId, TransactionTarget target, TransactionReview review,
        IEnumerable<TransactionArtifact> artifacts);
    string GetFingerprint(TransactionPlan plan);
}
