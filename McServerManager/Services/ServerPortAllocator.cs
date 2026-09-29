namespace McServerManager.Services;

/// <summary>複数のサーバーを同時に動かせるよう、既存サーバーと重ならないポートを選ぶ。</summary>
public static class ServerPortAllocator
{
    /// <summary>preferred から順に、used に含まれない最初のポートを返す。上限まで埋まっていれば 1024 から探し直す。</summary>
    public static int FindFreePort(int preferred, ICollection<int> used)
    {
        var start = Math.Clamp(preferred, 1024, 65535);
        for (var port = start; port <= 65535; port++)
        {
            if (!used.Contains(port))
                return port;
        }

        for (var port = 1024; port < start; port++)
        {
            if (!used.Contains(port))
                return port;
        }

        throw new InvalidOperationException("空いているポート番号が見つかりません。");
    }
}
