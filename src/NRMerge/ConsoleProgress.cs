namespace NRMerge;

/// <summary>Synchronous progress sink (System.Progress posts to the thread pool and reorders lines).</summary>
public sealed class ConsoleProgress(Action<string> write = null) : IProgress<string>
{
    public void Report(string value) => (write ?? Console.WriteLine)(value);
}
