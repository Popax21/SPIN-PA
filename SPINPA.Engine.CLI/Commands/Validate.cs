using System;
using System.CommandLine;
using System.CommandLine.Invocation;

namespace SPINPA.Engine.CLI;

public sealed class ValidateCommand : CLICommand {
    private readonly Option<bool> quietOpt;

    //hazard_checks
    private readonly Option<float> checkIntvOpt;
    private readonly Argument<float> hazardOffArg;

    public ValidateCommand() : base("validate", "Validates the results of the engine by comparing them against simulation data") {
        Command.AddGlobalOption(quietOpt = new Option<bool>("--quiet", "Surpress validator log output"));

        Command dtRangesCmd = new Command("dt_ranges", "Validates the effective delta time ranges");
        dtRangesCmd.SetHandler(ValidateDTRanges);
        Command.AddCommand(dtRangesCmd);

        Command hazardChecksCmd = new Command("hazard_checks", "Validates the hazard interval check result sequence");
        hazardChecksCmd.AddOption(checkIntvOpt = new Option<float>("--check-interval", description: "The OnInterval check interval", getDefaultValue: () => Constants.HazardLoadInterval));
        hazardChecksCmd.AddArgument(hazardOffArg = new Argument<float>("hazard-offset", description: "The hazard offset whose sequence to check"));
        hazardChecksCmd.SetHandler(ValidateHazardChecks);
        Command.AddCommand(hazardChecksCmd);
    }

    private void ValidateDTRanges(InvocationContext ctx) => RunValidator(ctx, v => v.ValidateDTRanges());
    private void ValidateHazardChecks(InvocationContext ctx) => RunValidator(ctx, v => v.ValidateIntervalCheckCycles(ctx.ParseResult.GetValueForArgument(hazardOffArg), ctx.ParseResult.GetValueForOption(checkIntvOpt)));

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