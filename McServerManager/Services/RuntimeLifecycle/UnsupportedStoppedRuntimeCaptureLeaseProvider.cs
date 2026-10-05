using System.Threading;

namespace McServerManager.Services.RuntimeLifecycle;

/// <summary>Fail closed until the Windows native lifetime/identity/lease mechanism has been validated.</summary>
public sealed class UnsupportedStoppedRuntimeCaptureLeaseProvider : IStoppedRuntimeCaptureLeaseProvider
{
    public ValueTask<IDisposable> AcquireAsync(
        string serverId, string serverDirectory, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotSupportedException(
            "稼働サーバーからのキャプチャは利用できません。Windows のプロセスツリー終了、" +
            "再解析ポイントを含むディレクトリの実体、およびアプリ間の排他リースの検証が未完了です。" +
            "起動プロセスの終了やアプリ内の排他だけではキャプチャを許可しません。");
    }
}
