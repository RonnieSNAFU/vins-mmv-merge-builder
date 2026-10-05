namespace NRMerge;

/// <summary>A problem the player can act on (wrong folder, wrong version, no network...): reported as a plain message, no stack trace.</summary>
public sealed class BuildException : Exception
{
    public BuildException(string message) : base(message) { }
    public BuildException(string message, Exception inner) : base(message, inner) { }
}
