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
