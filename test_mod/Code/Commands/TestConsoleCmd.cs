using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;

namespace MCPTest.Commands;

public class TestConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "mcptest";
    public override string Args => "[message:string]";
    public override string Description => "Prints a test message to verify custom commands work.";
    public override bool IsNetworked => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        string message = args.Length > 0
            ? string.Join(" ", args)
            : "MCPTest console command works!";

        MegaCrit.Sts2.Core.Logging.Log.Warn($"[MCPTest] {message}");
        return new CmdResult(true, message);
    }
}
