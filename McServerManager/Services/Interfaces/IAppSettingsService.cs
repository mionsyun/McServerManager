using McServerManager.Models;

namespace McServerManager.Services;

public interface IAppSettingsService
{
    AppSettings Load();
    void Save(AppSettings settings);

    /// <summary>
    /// 最新の設定を読み、apply で一部だけ書き換えて保存する。
    /// 画面ごとに読み込んだ古い AppSettings で他の項目を上書きしないために使う。
    /// </summary>
    AppSettings Update(Action<AppSettings> apply)
    {
        var settings = Load();
        apply(settings);
        Save(settings);
        return settings;
    }
}
