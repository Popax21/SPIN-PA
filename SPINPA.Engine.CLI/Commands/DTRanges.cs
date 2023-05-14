using System.CommandLine;
using System.CommandLine.Invocation;
using System.Numerics;
using System.Text;

namespace SPINPA.Engine.CLI;

public sealed class DTRangesCommand : CLICommand {
    private readonly Option<float> startTimeActiveOpt;
    private readonly Option<bool> frameRangeOpt, startTAOpt, effDTOpt;
    private readonly Option<int> cycleDepthOpt;
    private readonly Option<bool> cycleDeltasOpt, cycleIntervalsOpt, cycleDriftsOpt, cycleLensOpt, cycleBaseLensOpt;

    public DTRangesCommand() : base("dt_ranges", "Prints information about the effective delta time ranges") {
        Command.AddOption(startTimeActiveOpt = new Option<float>("--start-timeactive", description: "The TimeActive value of the first frame", getDefaultValue: () => 0));
        Command.AddOption(frameRangeOpt = new Option<bool>("--frame-range", description: "Print the frame index range of the DT range", getDefaultValue: () => true));
        Command.AddOption(startTAOpt = new Option<bool>("--start-ta", description: "Print the starting TimeActive value of the DT range", getDefaultValue: () => true));
        Command.AddOption(effDTOpt = new Option<bool>("--effective-dts", description: "Print the effective delta time values of the DT range", getDefaultValue: () => true));
        Command.AddOption(cycleDepthOpt = new Option<int>("--cycle-depth", description: "The number of recursive cycles to print", getDefaultValue: () => 8));
        Command.AddOption(cycleDeltasOpt = new Option<bool>("--cycle-deltas", description: "Print the deltas of recursive cycles", getDefaultValue: () => true));
        Command.AddOption(cycleIntervalsOpt = new Option<bool>("--cycle-intervals", description: "Print the intervals of recursive cycles", getDefaultValue: () => true));
        Command.AddOption(cycleDriftsOpt = new Option<bool>("--cycle-drifts", description: "Print the residual drifts of recursive cycles", getDefaultValue: () => true));
        Command.AddOption(cycleLensOpt = new Option<bool>("--cycle-lengths", description: "Print the cycle lengths of recursive cycles", getDefaultValue: () => true));
        Command.AddOption(cycleBaseLensOpt = new Option<bool>("--cycle-base-lengths", description: "Print the range base lengths of recursive cycles", getDefaultValue: () => true));
        Command.SetHandler(PrintDTRanges);
    }

    private void PrintDTRanges(InvocationContext ctx) {
        bool frameRange = ctx.ParseResult.GetValueForOption(frameRangeOpt);
        bool startTA = ctx.ParseResult.GetValueForOption(startTAOpt);
        bool effectiveDTs = ctx.ParseResult.GetValueForOption(effDTOpt);

        int cycleDepth = ctx.ParseResult.GetValueForOption(cycleDepthOpt);
        bool cycleDeltas = ctx.ParseResult.GetValueForOption(cycleDeltasOpt);
        bool cycleIntervals = ctx.ParseResult.GetValueForOption(cycleIntervalsOpt);
        bool cycleDrifts = ctx.ParseResult.GetValueForOption(cycleDriftsOpt);
        bool cycleLens = ctx.ParseResult.GetValueForOption(cycleLensOpt);
        bool cycleBaseLens = ctx.ParseResult.GetValueForOption(cycleBaseLensOpt);

        int idx = 0;
        foreach(EffectiveDTRange range in EffectiveDTRange.EnumerateDTRanges(ctx.ParseResult.GetValueForOption(startTimeActiveOpt))) {
            if(idx > 0) ctx.Console.WriteLine(string.Empty);
            ctx.Console.WriteLine($">>>> {idx++}: EXPONENT {range.TimeActiveExponent} <<<<");

            if(frameRange) ctx.Console.WriteLine($"-> frames: {range.StartFrame}-{(range.EndFrame == long.MaxValue ? "..." : range.EndFrame)}");
            if(startTA) ctx.Console.WriteLine($"-> startTA={FormatFloat(range.StartTimeActive, false)}");
            if(effectiveDTs) ctx.Console.WriteLine($"-> effDT={FormatFloat(range.EffectiveDT, false)} transEffDT={FormatFloat(range.TransitionDT, false)}");

            if(cycleDepth > 0 && (cycleDeltas || cycleIntervals || cycleDrifts || cycleLens) && range.EffectiveDT != 0 && float.IsFinite(range.EffectiveDT)) {
                ctx.Console.WriteLine("-> recursive cycles:");

                StringBuilder sb = new StringBuilder();
                BigRational cycleIntv = new BigRational(Constants.HazardLoadInterval), cycleDelta = new BigRational(range.EffectiveDT), cycleThresh = new BigRational(Constants.DeltaTime);
                for(int i = 0; i < cycleDepth; i++) {
                    if(cycleDelta == 0) break;

                    sb.Clear();
                    sb.Append($"    {i}:");
                    if(cycleDeltas) sb.Append($" dt={FormatFloat(cycleDelta, true)}");
                    if(cycleIntervals) sb.Append($" intv={FormatFloat(cycleIntv, false)}");
                    if(cycleDrifts) sb.Append($" drift={FormatFloat(RecursiveCycleCalculator.CalcResidualDrift(cycleIntv, cycleDelta), true)}");
                    if(cycleLens) sb.Append($" len={RecursiveCycleCalculator.CalcCycleLength(cycleIntv, cycleDelta).ToString().PadLeft(8)}");
                    if(cycleBaseLens) sb.Append($" baselen={RecursiveCycleCalculator.CalcBaseLength(cycleThresh, cycleDelta).ToString().PadLeft(8)}");
                    ctx.Console.WriteLine(sb.ToString());

                    RecursiveCycleCalculator.DescendRecursionLevels(ref cycleIntv, ref cycleDelta, ref cycleThresh, 1);
                }
            }
        }
    }

    private string FormatFloat(float v, bool sign) => (sign && (v >= 0) ? "+" : string.Empty) + v.ToString("F30");
    private string FormatFloat(BigRational v, bool sign) => (sign && (v >= 0) ? "+" : string.Empty) + v.ToString("F30");
}