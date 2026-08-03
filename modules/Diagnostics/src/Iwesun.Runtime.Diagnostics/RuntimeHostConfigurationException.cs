namespace Iwesun.Runtime.Diagnostics;

/// <summary>Represents an invalid or conflicting Runtime host registration.</summary>
public sealed class RuntimeHostConfigurationException : InvalidOperationException
{
    public RuntimeHostConfigurationException(string message)
        : base(message)
    {
    }

    public RuntimeHostConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
