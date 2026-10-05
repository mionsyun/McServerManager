// Compile-only stand-in for the runtime manager's optional WPF notification boundary.
// Tests inject their notification dispatcher. This does not emulate or validate WPF.
namespace System.Windows;

internal sealed class Application
{
    public static Application? Current => null;
    public HeadlessDispatcher Dispatcher => throw new NotSupportedException("Native WPF is not available in this suite.");
}

internal sealed class HeadlessDispatcher
{
    public bool HasShutdownStarted => true;
    public bool HasShutdownFinished => true;
    public Thread Thread => Thread.CurrentThread;
    public bool CheckAccess() => false;
    public object BeginInvoke(Action action) => throw new NotSupportedException("Inject a test notification dispatcher.");
    public void Invoke(Action action) => throw new NotSupportedException("Inject a test notification dispatcher.");
}
