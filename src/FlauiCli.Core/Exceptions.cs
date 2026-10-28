namespace FlauiCli.Core;

/// <summary>An expected usage error (bad argument, element not found...); the message is shown to the user as is.</summary>
public class CliException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>The target element could not be found.</summary>
public sealed class ElementNotFoundException(string target, string? detail = null)
    : CliException($"Element not found: {target}" + (detail is null ? "" : $" ({detail})"));

/// <summary>Input or a screenshot was needed while the desktop could not receive it (exit code 4).</summary>
public sealed class DesktopUnavailableException(string reason, string? detail = null)
    : CliException($"Mouse and keyboard input cannot be simulated: {reason}. Unlock the workstation and run the command again" +
                   (detail is null ? "" : $" ({detail})"));

/// <summary>An assertion failed (exit code 1).</summary>
public sealed class AssertionFailedException(string message) : CliException(message);
