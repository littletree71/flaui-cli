using FlauiCli.Core.Protocol;

namespace FlauiCli;

internal static class Output
{
    public static void Print(CommandResult result, bool json)
    {
        if (json)
        {
            Console.WriteLine(ProtocolJson.Serialize(result, indented: true));
            return;
        }

        if (!string.IsNullOrEmpty(result.Text)) Console.WriteLine(result.Text);
        if (!result.Ok && !string.IsNullOrEmpty(result.Error))
            Console.Error.WriteLine(result.IsAssertionFailure ? result.Error : "Error: " + result.Error);
    }
}
