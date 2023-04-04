using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics;

namespace SPINPA.Engine.CLI;

public sealed class ValidateCommand : CLICommand {
    public sealed class ConsoleValidator : SimulationValidator {
        public const int ProgressBarWidth = 60;

        public readonly IConsole? Console;
        public readonly bool Quiet, ShowProgress;

        private string? progStatus;
        private float progProgress;

        public ConsoleValidator(IConsole? console, bool quiet, bool progress) => (Console, Quiet, ShowProgress) = (console, quiet, progress);

        protected override void LogMessage(string msg) {
            EraseProgressBar();
            Console?.WriteLine(msg);
            WriteProgressBar();
        }

        protected override void LogProgress(string? status, float progress) {
            if(!ShowProgress || Console == null) return;
            EraseProgressBar();
            progStatus = status;
            progProgress = progress;
            WriteProgressBar();
        }

        public void EraseProgressBar() {
            if(progStatus != null) Console?.Write("\x1b[3F\x1b[J");
        }

        public void WriteLogSeperator() {
            if(ShowProgress && !Quiet) Console?.WriteLine(new string('-', ProgressBarWidth+2));
        }

        public void WriteProgressBar() {
            if(progStatus == null || Console == null) return;

            WriteLogSeperator();
            Console.WriteLine(progStatus);

            int nBars = int.Clamp((int) (progProgress * ProgressBarWidth), 0, ProgressBarWidth);
            if(nBars <= 0) Console.WriteLine($"[{new string(' ', ProgressBarWidth)}]");
            else if(nBars >= ProgressBarWidth) Console.WriteLine($"[{new string('#', ProgressBarWidth)}]");
            else Console.WriteLine($"[{new string('#', nBars-1)}>{new string(' ', ProgressBarWidth-nBars)}]");
        }
    }

    private readonly Option<bool> quietOpt, progressOpt;

    //hazard_checks
    private readonly Option<bool> deepValidationOpt, skipValidationOpt;
    private readonly Option<float> checkIntvOpt;
    private readonly Argument<float> hazardOffArg;

    public ValidateCommand() : base("validate", "Validates the results of the engine by comparing them against simulation data") {
        Command.AddGlobalOption(quietOpt = new Option<bool>("--quiet", "Surpress validator log output"));
        Command.AddGlobalOption(progressOpt = new Option<bool>("--progress", description: "Show a progress bar of the validation's progress", getDefaultValue: () => !Console.IsOutputRedirected));

        Command dtRangesCmd = new Command("dt_ranges", "Validates the effective delta time ranges");
        dtRangesCmd.SetHandler(ValidateDTRanges);
        Command.AddCommand(dtRangesCmd);

        Command hazardChecksCmd = new Command("hazard_checks", "Validates the hazard interval check result sequence");
        hazardChecksCmd.AddOption(deepValidationOpt = new Option<bool>("--deep-validation", description: "Whether to enable deep interval check cycle validation", getDefaultValue: () => true));
        hazardChecksCmd.AddOption(skipValidationOpt = new Option<bool>("--skip-validation", description: "Whether to enable skip interval check cycle validation", getDefaultValue: () => true));
        hazardChecksCmd.AddOption(checkIntvOpt = new Option<float>("--check-interval", description: "The OnInterval check interval", getDefaultValue: () => Constants.HazardLoadInterval));
        hazardChecksCmd.AddArgument(hazardOffArg = new Argument<float>("hazard-offset", description: "The hazard offset whose sequence to check"));
        hazardChecksCmd.SetHandler(ValidateHazardChecks);
        Command.AddCommand(hazardChecksCmd);
    }

    private void ValidateDTRanges(InvocationContext ctx) => RunValidator(ctx, v => v.ValidateDTRanges());
    private void ValidateHazardChecks(InvocationContext ctx) => RunValidator(ctx, v => v.ValidateIntervalCheckCycles(ctx.ParseResult.GetValueForArgument(hazardOffArg), ctx.ParseResult.GetValueForOption(checkIntvOpt), ctx.ParseResult.GetValueForOption(deepValidationOpt), ctx.ParseResult.GetValueForOption(skipValidationOpt)));

    private void RunValidator(InvocationContext ctx, Action<Validator> cb) {
        ConsoleValidator validator = new ConsoleValidator(ctx.Console, ctx.ParseResult.GetValueForOption(quietOpt), ctx.ParseResult.GetValueForOption(progressOpt));
        try {
            Stopwatch sw = new Stopwatch();

            sw.Start();
            cb(validator);
            sw.Stop();

            validator.EraseProgressBar();
            validator.WriteLogSeperator();
            ctx.Console.WriteLine($"Validation OK - took {sw.Elapsed.TotalSeconds:F4}s [{sw.ElapsedMilliseconds}ms]");
        } catch(Exception e) {
            validator.EraseProgressBar();
            validator.WriteLogSeperator();
            ctx.Console.WriteLine($"Error during validation: {e}");
            ctx.ExitCode = -1;
        }
    }
}