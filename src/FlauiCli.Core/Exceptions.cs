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

/// <summary>Recognises UI Automation errors that surface as plain COM exceptions.</summary>
public static class UiaErrors
{
    /// <summary>
    /// UIA_E_TIMEOUT (0x80131505, the same value as COR_E_TIMEOUT): the provider did not answer within the UI Automation
    /// transaction timeout, typically while a search walked thousands of elements (the rows of a large table).
    /// </summary>
    public const int TimeoutHResult = unchecked((int)0x80131505);

    public static bool IsTimeout(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e.HResult == TimeoutHResult) return true;
        }
        return false;
    }

    public const string TimeoutMessage =
        "UI Automation timed out (0x80131505): the window may have too many elements, for example a table with many rows. " +
        "Scope the selector to the part of the window you need (for example id=panel >> id=textBox), try snapshot --depth N, " +
        "or use click --offset / --at when the element cannot be listed";
}
