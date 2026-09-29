using McServerManager.Models;
using McServerManager.Services;
using McServerManager.ViewModels;

namespace McServerManager.Tests;

public sealed class TutorialViewModelTests
{
    [Fact]
    public void StartNextFinish_CompletesTutorialAndSavesSettings()
    {
        var settings = new AppSettings();
        var settingsService = new RecordingSettingsService(settings);
        var vm = new TutorialViewModel(settings, settingsService);

        vm.Start(GuideType.InitialSetup);
        Assert.True(vm.IsActive);
        Assert.Equal(0, vm.StepIndex);
        Assert.True(vm.TotalSteps > 1);

        while (vm.IsActive)
        {
            vm.NextCommand.Execute(null);
        }

        Assert.True(settings.HasCompletedTutorial);
        Assert.Equal(1, settingsService.SaveCallCount);
    }

    [Fact]
    public void Finish_DoesNotRevertFirstRunFlagSavedAfterStartup()
    {
        using var appData = new TestInfrastructure.TemporaryAppDataScope();
        IAppSettingsService service = new AppSettingsService(new AppPathsService());
        // MainViewModel 生成時（初回案内の前）に読んだ設定をチュートリアルが持つ
        var staleSettings = service.Load();
        var vm = new TutorialViewModel(staleSettings, service);
        // 初回案内を閉じたときに App が保存する
        service.Update(s => s.HasShownFirstRun = true);

        vm.Start(GuideType.InitialSetup);
        vm.Finish();

        var saved = service.Load();
        Assert.True(saved.HasShownFirstRun);
        Assert.True(saved.HasCompletedTutorial);
    }

    [Fact]
    public void BackCommand_MovesToPreviousStep()
    {
        var settings = new AppSettings();
        var settingsService = new RecordingSettingsService(settings);
        var vm = new TutorialViewModel(settings, settingsService);

        vm.Start(GuideType.Mod);
        vm.NextCommand.Execute(null);
        Assert.Equal(1, vm.StepIndex);

        vm.BackCommand.Execute(null);

        Assert.Equal(0, vm.StepIndex);
    }

    private sealed class RecordingSettingsService : IAppSettingsService
    {
        private readonly AppSettings _settings;

        public RecordingSettingsService(AppSettings settings)
        {
            _settings = settings;
        }

        public int SaveCallCount { get; private set; }

        public AppSettings Load() => _settings;

        public void Save(AppSettings settings)
        {
            SaveCallCount++;
        }
    }
}
