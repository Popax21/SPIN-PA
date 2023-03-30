using System;
using System.CommandLine;
using System.CommandLine.Invocation;

namespace SPINPA.Engine.CLI;

public sealed class ValidateCommand : CLICommand {
    private readonly Option<bool> quietOpt;

    public ValidateCommand() : base("validate", "Validates the results of the engine by comparing them against simulation data") {
        Command.AddGlobalOption(quietOpt = new Option<bool>("--quiet", "Surpress validator log output"));

        Command dtRangesCmd = new Command("dt_ranges", "Validates the effective delta time ranges");
        dtRangesCmd.SetHandler(ValidateDTRanges);
        Command.AddCommand(dtRangesCmd);
    }

    private void ValidateDTRanges(InvocationContext ctx) => RunValidator(ctx, v => v.ValidateDTRanges());

    private void RunValidator(InvocationContext ctx, Action<Validator> cb) {
        Validator validator = new SimulationValidator(ctx.ParseResult.GetValueForOption(quietOpt) ? null : Console.WriteLine);
        try {
            cb(validator);
            ctx.Console.WriteLine("Validation OK");
        } catch(Exception e) {
            ctx.Console.Error.Write($"Error during validation: {e}\n");
        }
    }
}