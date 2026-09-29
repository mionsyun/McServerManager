namespace McServerManager.Services;

/// <summary>Java 版サーバーの起動に使う java.exe が見つからない・起動できないときの例外。</summary>
public sealed class JavaNotFoundException : Exception
{
    public JavaNotFoundException(string javaPath, Exception? innerException = null)
        : base($"Java が見つかりません: {javaPath}", innerException)
    {
        JavaPath = javaPath;
    }

    public string JavaPath { get; }
}
