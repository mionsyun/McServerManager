using McServerManager.Services;

namespace McServerManager.Tests.TestInfrastructure;

internal sealed class TemporaryAppDataScope : IDisposable
{
    private readonly string? _originalAppDataOverride;
    private readonly string? _originalAppData;
    private readonly string? _originalLocalAppData;
    private readonly string? _originalTemp;
    private readonly string? _originalTmp;
    private readonly TemporaryDirectoryScope _directoryScope;

    public TemporaryAppDataScope()
    {
        _originalAppDataOverride = Environment.GetEnvironmentVariable(AppPathsService.AppDataRootOverrideEnvVar);
        _originalAppData = Environment.GetEnvironmentVariable("APPDATA");
        _originalLocalAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        _originalTemp = Environment.GetEnvironmentVariable("TEMP");
        _originalTmp = Environment.GetEnvironmentVariable("TMP");
        _directoryScope = new TemporaryDirectoryScope();

        AppDataPath = Path.Combine(_directoryScope.Path, "Roaming");
        LocalAppDataPath = Path.Combine(_directoryScope.Path, "Local");
        TempPath = Path.Combine(_directoryScope.Path, "Temp");
        Directory.CreateDirectory(AppDataPath);
        Directory.CreateDirectory(LocalAppDataPath);
        Directory.CreateDirectory(TempPath);

        Environment.SetEnvironmentVariable(AppPathsService.AppDataRootOverrideEnvVar, AppDataPath);
        Environment.SetEnvironmentVariable("APPDATA", AppDataPath);
        Environment.SetEnvironmentVariable("LOCALAPPDATA", LocalAppDataPath);
        Environment.SetEnvironmentVariable("TEMP", TempPath);
        Environment.SetEnvironmentVariable("TMP", TempPath);
    }

    public string AppDataPath { get; }
    public string LocalAppDataPath { get; }
    public string TempPath { get; }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(AppPathsService.AppDataRootOverrideEnvVar, _originalAppDataOverride);
        Environment.SetEnvironmentVariable("APPDATA", _originalAppData);
        Environment.SetEnvironmentVariable("LOCALAPPDATA", _originalLocalAppData);
        Environment.SetEnvironmentVariable("TEMP", _originalTemp);
        Environment.SetEnvironmentVariable("TMP", _originalTmp);
        _directoryScope.Dispose();
    }
}
