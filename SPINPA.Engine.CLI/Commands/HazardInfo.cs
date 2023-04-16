using System.CommandLine;
using System.CommandLine.Invocation;

namespace SPINPA.Engine.CLI;

public sealed class HazardInfoCommand : CLICommand {
    private readonly Option<float> checkIntvOpt;

    private readonly Option<bool> recCycleInfoOpt;
    private readonly Option<bool> rawCheckInfoOpt;
    private readonly Option<bool> rangeCheckInfoOpt;

    private readonly Argument<float> hazardOffArg;
    private readonly Argument<long?> frameArg;

    public HazardInfoCommand() : base("hazard_info", "Prints information about the load check cycles of a hazard with a given offset") {
        Command.AddOption(checkIntvOpt = new Option<float>("--check-interval", description: "The OnInterval check interval", getDefaultValue: () => Constants.HazardLoadInterval));

        Command.AddOption(recCycleInfoOpt = new Option<bool>("--cycle-info", description: "Whether to print information about the states of recursive cycles", getDefaultValue: () => true));
        Command.AddOption(rawCheckInfoOpt = new Option<bool>("--raw-check-info", description: "Whether to print information about raw check results", getDefaultValue: () => true));
        Command.AddOption(rangeCheckInfoOpt = new Option<bool>("--range-check-info", description: "Whether to print information about range check results", getDefaultValue: () => true));

        Command.AddArgument(hazardOffArg = new Argument<float>("hazard-offset", description: "The offset of the hazard"));
        Command.AddArgument(frameArg = new Argument<long?>("frame", description: "If specified, the frame whose cycle info to print", getDefaultValue: () => null));
        Command.SetHandler(PrintCycleInfo);
    }

    private void PrintCycleInfo(InvocationContext ctx) {
        bool recCycleInfo = ctx.ParseResult.GetValueForOption(recCycleInfoOpt);
        bool rawCheckInfo = ctx.ParseResult.GetValueForOption(rawCheckInfoOpt);
        bool rangeCheckInfo = ctx.ParseResult.GetValueForOption(rangeCheckInfoOpt);

        IntervalCheckPredictor pred = new IntervalCheckPredictor(ctx.ParseResult.GetValueForArgument(hazardOffArg), ctx.ParseResult.GetValueForOption(checkIntvOpt));

        void PrintCycleStates(IntervalCheckDTRangePredictor dtPred) {
            for(int i = 0; i < dtPred.RecursiveCycles.Length; i++) {
                IntervalCheckDTRangePredictor.RecursiveCycle cycle = dtPred.RecursiveCycles[i];
                ctx.Console.WriteLine($"    << CYCLE {i} >>");
                ctx.Console.WriteLine($"        -> delta:  {(cycle.Offset >= 0 ? "+" : "")}{cycle.Delta:F30}");
                ctx.Console.WriteLine($"        -> interv:  {cycle.Interval:F30}");
                ctx.Console.WriteLine($"        -> thresh:  {cycle.Threshold:F30}");
                ctx.Console.WriteLine($"        -> offset: {(cycle.Offset >= 0 ? "+" : "")}{cycle.Offset:F30}");
                ctx.Console.WriteLine($"        -> cycle state: {cycle.CurCycleOffset}/{cycle.CycleLength} target={cycle.CurCycleTarget}");

                if(rawCheckInfo) {
                    ctx.Console.WriteLine($"        -> raw check info:");
                    if(cycle.HasGroupDrifts) {
                        ctx.Console.WriteLine($"            - prev/cur/next raw check: {cycle.PrevRawCheckResult} / {cycle.CurRawCheckResult} / {cycle.NextRawCheckResult}");
                        ctx.Console.WriteLine($"            - last raw check: before {cycle.TicksSinceLastRawCheck} tick(s), group drift: {cycle.LastRawCheckDidGroupDrift}");
                        ctx.Console.WriteLine($"            - next raw check: in {cycle.TicksTillNextRawCheck} tick(s), group drift: {cycle.NextRawCheckDidGroupDrift}");
                        ctx.Console.WriteLine($"            - next group drift: in {cycle.TicksTillNextGroupDrift} tick(s)");
                    } else {
                        ctx.Console.WriteLine($"            - prev/cur/next raw check: {cycle.PrevRawCheckResult} / {cycle.CurRawCheckResult} / {cycle.NextRawCheckResult}");
                        ctx.Console.WriteLine($"            - last raw check: before {cycle.TicksSinceLastRawCheck} tick(s)");
                        ctx.Console.WriteLine($"            - next raw check: in {cycle.TicksTillNextRawCheck} tick(s)");
                        ctx.Console.WriteLine($"            - next group drift: in {cycle.TicksTillNextGroupDrift} tick(s)");
                    }
                }

                if(rangeCheckInfo && cycle.HasRangeChecks) {
                    ctx.Console.WriteLine($"        -> range check info:");
                    if(cycle.HasLengthDrifts) {
                        ctx.Console.WriteLine($"            - range length: base: {cycle.BaseLength}, for drifts: {cycle.BaseLength+1}");
                        ctx.Console.WriteLine($"            - prev/cur/next range check: {cycle.PrevRangeCheckResult} / {cycle.CurRangeCheckResult} / {cycle.NextRangeCheckResult}");
                        ctx.Console.WriteLine($"            - last range check: before {cycle.TicksSinceLastRangeCheck} tick(s), length drift: {cycle.LastCheckRangeDidLengthDrift}");
                        ctx.Console.WriteLine($"            - next range check: in {cycle.TicksTillNextRangeCheck} tick(s), length drift: {cycle.NextCheckRangeDidLengthDrift}");
                        ctx.Console.WriteLine($"            - next length drift: in {cycle.TicksTillNextLengthDrift} tick(s)");
                    } else {
                        ctx.Console.WriteLine($"            - range length: {cycle.BaseLength}");
                        ctx.Console.WriteLine($"            - prev/cur/next range check: {cycle.PrevRangeCheckResult} / {cycle.CurRangeCheckResult} / {cycle.NextRangeCheckResult}");
                        ctx.Console.WriteLine($"            - last range check: before {cycle.TicksSinceLastRangeCheck} tick(s)");
                        ctx.Console.WriteLine($"            - next range check: in {cycle.TicksTillNextRangeCheck} tick(s)");
                    }
                }
            }
        }
    
        if(ctx.ParseResult.GetValueForArgument(frameArg) is long frame) {
            pred.CurrentFrame = frame;

            ref IntervalCheckPredictor.DTRange range = ref pred.CurrentRange;
            IntervalCheckDTRangePredictor dtPred = range.DTRangePredictor;

            ctx.Console.WriteLine($"-> range index: {pred.CurrentRangeIndex}");
            ctx.Console.WriteLine($"-> frames: {range.StartFrame}-{(range.EndFrame == long.MaxValue ? "..." : range.EndFrame)}");
            ctx.Console.WriteLine($"-> DT: {dtPred.EffectiveDT:F30}");
            ctx.Console.WriteLine($"-> offset: {dtPred.Offset:F30}");
            ctx.Console.WriteLine($"-> current check result: {dtPred.CheckResult}");

            if(recCycleInfo && dtPred.RecursiveCycles.Length > 0) {
                ctx.Console.WriteLine($"-> current cycle states:");
                PrintCycleStates(dtPred);
            }
        } else {
            for(int rangeIdx = 0; rangeIdx < pred.Ranges.Length; rangeIdx++) {
                if(rangeIdx > 0) ctx.Console.WriteLine("");

                ref IntervalCheckPredictor.DTRange range = ref pred.Ranges[rangeIdx];
                IntervalCheckDTRangePredictor dtPred = range.DTRangePredictor;

                ctx.Console.WriteLine($">>>> RANGE {rangeIdx} <<<<");
                ctx.Console.WriteLine($"-> frames: {range.StartFrame}-{(range.EndFrame == long.MaxValue ? "..." : range.EndFrame)}");
                ctx.Console.WriteLine($"-> DT: {dtPred.EffectiveDT:F30}");
                ctx.Console.WriteLine($"-> offset: {dtPred.Offset:F30}");

                if(recCycleInfo && dtPred.RecursiveCycles.Length > 0) {
                    ctx.Console.WriteLine($"-> initial cycle states:");
                    PrintCycleStates(dtPred);
                }
            }
        }
    }
}