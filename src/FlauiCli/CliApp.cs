using System.CommandLine;
using FlauiCli.Core.Commands;
using FlauiCli.Core.Protocol;

namespace FlauiCli;

/// <summary>依 <see cref="CommandCatalog"/> 動態建立 System.CommandLine 指令樹。</summary>
internal static class CliApp
{
    public const string SessionEnvVar = "FLAUI_CLI_SESSION";

    private static readonly Option<string?> SessionOption = new("--session", "-s")
    {
        Description = $"session 名稱（預設 default，或環境變數 {SessionEnvVar}）",
        Recursive = true,
    };

    private static readonly Option<bool> JsonOption = new("--json")
    {
        Description = "以 JSON 輸出結果",
        Recursive = true,
    };

    public static int Run(string[] args)
    {
        var root = new RootCommand("flaui-cli：以 FlaUI 驅動的 Windows 桌面 UI 自動化命令列工具（仿 playwright-cli）");
        root.Options.Add(SessionOption);
        root.Options.Add(JsonOption);
        foreach (var spec in CommandCatalog.All) root.Subcommands.Add(Build(spec));
        return root.Parse(args).Invoke();
    }

    private static Command Build(CommandSpec spec)
    {
        var cmd = new Command(spec.Name, spec.Description) { Hidden = spec.Hidden };
        var argBindings = new List<(ArgSpec Spec, Argument Arg)>();
        var optBindings = new List<(OptSpec Spec, Option Opt)>();

        foreach (var a in spec.Args)
        {
            Argument arg = a.Variadic
                ? new Argument<string[]>(a.Name)
                {
                    Description = a.Description,
                    Arity = a.Required ? ArgumentArity.OneOrMore : ArgumentArity.ZeroOrMore,
                }
                : new Argument<string?>(a.Name)
                {
                    Description = a.Description,
                    Arity = a.Required ? ArgumentArity.ExactlyOne : ArgumentArity.ZeroOrOne,
                };
            cmd.Arguments.Add(arg);
            argBindings.Add((a, arg));
        }

        foreach (var o in spec.Options)
        {
            var aliases = o.Alias is null ? Array.Empty<string>() : ["-" + o.Alias];
            Option opt = o.Flag
                ? new Option<bool>("--" + o.Name, aliases) { Description = o.Description }
                : o.Multiple
                    ? new Option<string[]>("--" + o.Name, aliases) { Description = o.Description, AllowMultipleArgumentsPerToken = false }
                    : new Option<string?>("--" + o.Name, aliases) { Description = o.Description };
            cmd.Options.Add(opt);
            optBindings.Add((o, opt));
        }

        cmd.SetAction(pr =>
        {
            var call = new CommandCall(spec.Name) { Cwd = Environment.CurrentDirectory };
            foreach (var (a, arg) in argBindings)
            {
                if (arg is Argument<string[]> multi)
                {
                    if (pr.GetValue(multi) is { Length: > 0 } values) call.Set(a.Name, string.Join(CommandCall.MultiValueSeparator, values));
                }
                else
                {
                    call.Set(a.Name, pr.GetValue((Argument<string?>)arg));
                }
            }

            foreach (var (o, opt) in optBindings)
            {
                switch (opt)
                {
                    case Option<bool> flag:
                        if (pr.GetValue(flag)) call.Set(o.Name, "true");
                        break;
                    case Option<string[]> multi:
                        if (pr.GetValue(multi) is { Length: > 0 } values) call.Set(o.Name, string.Join(CommandCall.MultiValueSeparator, values));
                        break;
                    case Option<string?> single:
                        call.Set(o.Name, pr.GetValue(single));
                        break;
                }
            }

            var session = pr.GetValue(SessionOption) ?? Environment.GetEnvironmentVariable(SessionEnvVar) ?? "default";
            var json = pr.GetValue(JsonOption);
            try
            {
                return spec.Location == CommandLocation.Local
                    ? LocalCommands.Execute(call, session, json)
                    : RemoteCommands.Execute(call, session, json);
            }
            catch (Core.CliException ex)
            {
                Output.Print(CommandResult.Failure(ex.Message), json);
                return ExitCodes.Error;
            }
        });

        return cmd;
    }
}
