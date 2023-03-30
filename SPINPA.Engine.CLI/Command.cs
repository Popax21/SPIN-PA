using System.CommandLine;

namespace SPINPA.Engine.CLI;

public abstract class CLICommand {
    public readonly Command Command;

    public CLICommand(string name, string descr) {
        Command = new Command(name, descr);
    }
}