namespace McServerManager.ViewModels;

public sealed class StartupPresetOption
{
    public StartupPresetOption(string id, string label, int memoryXmsMb, int memoryXmxMb, string javaExtraArguments)
    {
        Id = id;
        Label = label;
        MemoryXmsMb = memoryXmsMb;
        MemoryXmxMb = memoryXmxMb;
        JavaExtraArguments = javaExtraArguments;
    }

    public string Id { get; }
    public string Label { get; }
    public int MemoryXmsMb { get; }
    public int MemoryXmxMb { get; }
    public string JavaExtraArguments { get; }
}
