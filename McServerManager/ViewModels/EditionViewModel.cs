using System.Windows.Input;
using McServerManager.Models.Editions;
using McServerManager.Services.Editions;
using McServerManager.Utilities;

namespace McServerManager.ViewModels;

/// <summary>Edition navigation is separate from shared preferences and never grants entitlement.</summary>
public sealed class EditionViewModel : ObservableObject
{
    private readonly IEditionPolicy _policy;
    private readonly ProDistributionLinks _links;
    private readonly IProStoreNavigation _navigation;
    private string _status = string.Empty;
    public EditionViewModel(IEditionPolicy policy, ProDistributionLinks links, IProStoreNavigation navigation)
    {
        _policy = policy;
        _links = links;
        _navigation = navigation;
        PurchaseCommand = new EditionCommand(OpenPurchase, () => CanPurchase);
        UpdatesCommand = new EditionCommand(OpenUpdates, () => CanOpenUpdates);
    }
    public string DisplayName => _policy.DisplayName;
    public bool IsPro => _policy.Edition == AppEdition.Pro;
    public bool UsesPublicUpdater => !IsPro;
    public bool CanPurchase => !IsPro && _links.CanPurchase;
    public bool CanOpenUpdates => IsPro && _links.CanOpenUpdates;
    public string ProUpdateHint => CanOpenUpdates ? "購入済みのPro更新をBOOTHで確認します" : "Pro更新先の確認が済むまで、アプリ内の更新は利用できません";
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public ICommand PurchaseCommand { get; }
    public ICommand UpdatesCommand { get; }
    private void OpenPurchase()
    {
        if (!CanPurchase) return;
        Status = _navigation.OpenPurchase() ? "購入手続きはBOOTHで確認してください。設定は同じWindowsユーザーで引き継ぎます" : "BOOTHを開けませんでした。設定やアプリは変更していません";
    }
    private void OpenUpdates()
    {
        if (!CanOpenUpdates) return;
        Status = _navigation.OpenUpdates() ? "購入済みの配布ファイルと更新案内をBOOTHで確認してください" : "BOOTHを開けませんでした。現在のPro版は変更していません";
    }
    private sealed class EditionCommand(Action execute, Func<bool> canExecute) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => canExecute();
        public void Execute(object? parameter) { if (canExecute()) execute(); }
    }
}
