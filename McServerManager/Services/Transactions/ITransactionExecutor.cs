using McServerManager.Models.Transactions;

namespace McServerManager.Services.Transactions;

public interface ITransactionExecutor
{
    Task<TransactionResult> ApplyAsync(TransactionPlan plan, TransactionApproval approval, CancellationToken cancellationToken = default);
    Task<TransactionResult> RecoverAsync(TransactionReceipt receipt, CancellationToken cancellationToken = default);
}
