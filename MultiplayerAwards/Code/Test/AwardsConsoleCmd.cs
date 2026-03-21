using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using MultiplayerAwards.UI;

namespace MultiplayerAwards.Test;

/// <summary>
/// Console command: type "awards" in the dev console (~) to toggle the test awards screen.
/// </summary>
public class AwardsConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "awards";
    public override string Args => "";
    public override string Description => "Toggle test multiplayer awards screen with simulated stats";
    public override bool IsNetworked => false;
    public override bool DebugOnly => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        if (AwardsScreen.IsVisible)
        {
            AwardsScreen.CloseIfOpen();
            return new CmdResult(true, "Awards screen closed.");
        }

        TestAwardsTrigger.ShowTestAwards();
        return new CmdResult(true, "Awards screen opened!");
    }
}
