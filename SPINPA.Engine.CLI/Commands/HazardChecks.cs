using System;
using System.CommandLine;
using System.CommandLine.Invocation;

namespace SPINPA.Engine.CLI;

public sealed class HazardChecksCommand : CLICommand {
    private readonly Option<float> startTimeActiveOpt;
    private readonly Option<long> startFrameOpt, numFramesOpt;
    private readonly Option<float> checkIntvOpt;

    private readonly Option<int> recDepthOpt;
    private readonly Option<bool> rawCheckSequenceOpt, seperateRangesOpt;

    private readonly Option<bool> highlightDriftsOpt, highlightGroupDriftsOpt, highlightLengthDriftsOpt;

    private readonly Argument<float> hazardOffArg;

    public HazardChecksCommand() : base("hazard_checks", "Prints the sequence of load checks performed by a certain hazard") {
        Command.AddOption(startTimeActiveOpt = new Option<float>("--start-timeactive", description: "The TimeActive value of the first frame", getDefaultValue: () => 0));
        Command.AddOption(startFrameOpt = new Option<long>("--start-frame", description: "The starting frame of the sequence to print", getDefaultValue: () => 0));
        Command.AddOption(numFramesOpt = new Option<long>("--num-frames", description: "The number frame of the sequence to print", getDefaultValue: () => long.MaxValue));
        Command.AddOption(checkIntvOpt = new Option<float>("--check-interval", description: "The OnInterval check interval", getDefaultValue: () => Constants.HazardLoadInterval));

        Command.AddOption(recDepthOpt = new Option<int>("--rec-depth", description: "The depth/index of the recursive cycle whose results to print", getDefaultValue: () => 0));
        Command.AddOption(rawCheckSequenceOpt = new Option<bool>("--raw-checks", description: "Print raw check results instead of range check results", getDefaultValue: () => false));
        Command.AddOption(seperateRangesOpt = new Option<bool>("--seperate-ranges", description: "Seperate different effective deltatime ranges using spaces", getDefaultValue: () => false));

        Command.AddOption(highlightDriftsOpt = new Option<bool>("--drifts", description: "Highlights group and length drifts *in the same effective deltatime range* in the sequence", getDefaultValue: () => false));
        Command.AddOption(highlightGroupDriftsOpt = new Option<bool>("--group-drifts", description: "Highlights group drifts *in the same effective deltatime range* in the sequence", getDefaultValue: () => false));
        Command.AddOption(highlightLengthDriftsOpt = new Option<bool>("--length-drifts", description: "Highlights length drifts *in the same effective deltatime range* in the sequence", getDefaultValue: () => false));

        Command.AddArgument(hazardOffArg = new Argument<float>("hazard-offset", description: "The hazard offset whose sequence to print"));
        Command.SetHandler(PrintCheckSequence);
    }

    private void PrintCheckSequence(InvocationContext ctx) {
        //Get arguments and options
        long startFrame = ctx.ParseResult.GetValueForOption(startFrameOpt), numFrames = ctx.ParseResult.GetValueForOption(numFramesOpt);
        float checkIntv = ctx.ParseResult.GetValueForOption(checkIntvOpt), hazardOff = ctx.ParseResult.GetValueForArgument(hazardOffArg);

        int recDepth = ctx.ParseResult.GetValueForOption(recDepthOpt);
        bool rawCheckSequence = ctx.ParseResult.GetValueForOption(rawCheckSequenceOpt), seperateRanges = ctx.ParseResult.GetValueForOption(seperateRangesOpt);
        bool highlightDrifts = ctx.ParseResult.GetValueForOption(highlightDriftsOpt), highlightGroupDrifts = ctx.ParseResult.GetValueForOption(highlightGroupDriftsOpt), highlightLengthDrifts = ctx.ParseResult.GetValueForOption(highlightLengthDriftsOpt);

        if(startFrame < 0) throw new ArgumentOutOfRangeException(nameof(startFrame));
        if(numFrames < 0) throw new ArgumentOutOfRangeException(nameof(numFrames));
        if(checkIntv <= 0) throw new ArgumentOutOfRangeException(nameof(checkIntv));
        if(recDepth < 0) throw new ArgumentOutOfRangeException(nameof(recDepth));
    
        highlightGroupDrifts |= highlightDrifts;
        highlightLengthDrifts |= highlightDrifts;

        IntervalCheckPredictor pred = new IntervalCheckPredictor(hazardOff, checkIntv, ctx.ParseResult.GetValueForOption(startTimeActiveOpt));

        bool inGroupDriftHighlight = false, inLengthDriftHighlight = false;
        long lastTick = -1;
        for(pred.CurrentFrame = startFrame; pred.CurrentFrame < startFrame + numFrames; pred.CurrentFrame++) {
            IntervalCheckDTRangePredictor.RecursiveCycle? cycle = (pred.CurrentDTRangePredictor.RecursiveCycles.Length > recDepth) ? pred.CurrentDTRangePredictor.RecursiveCycles[recDepth] : null;

            //Handle range transitions
            if(pred.CurrentDTRangePredictor.CurrentFrame == 0) {
                lastTick = -1;

                //Reset highlights when transitioning ranges
                if(inGroupDriftHighlight) ctx.Console.Write(">");
                if(inLengthDriftHighlight) ctx.Console.Write("}");
                inGroupDriftHighlight = inLengthDriftHighlight = false;

                if(pred.CurrentRangeIndex > 0 && seperateRanges) ctx.Console.Out.Write(" ");
            }

            //Don't print the same tick twice
            if(cycle != null) {
                if(lastTick == cycle.CurTickIndex) continue;
                lastTick = cycle.CurTickIndex;
            } else if(recDepth > 0) {
                if(lastTick == -1) ctx.Console.Write("-");
                lastTick = 0;
                continue;
            }

            //Highlight drifts if enabled - opening brackets
            if(cycle != null) {
                if(highlightGroupDrifts && !inGroupDriftHighlight) {
                    if(
                        (cycle.ResidualDrift < 0 && cycle.CurRawCheckResult && cycle.LastRawCheckDidGroupDrift) ||
                        (cycle.ResidualDrift > 0 && cycle.NextRawCheckResult && cycle.NextRawCheckDidGroupDrift)
                    ) {
                        ctx.Console.Write("<");
                        inGroupDriftHighlight = true;
                    }
                }
                if(!rawCheckSequence && highlightLengthDrifts && !inLengthDriftHighlight) {
                    if(
                        (cycle.Delta < 0 && ((cycle.BaseLength == 0) ? cycle.LastCheckRangeDidLengthDrift : cycle.NextCheckRangeDidLengthDrift) && cycle.TicksTillAnyRawCheck < cycle.BaseLength+1) ||
                        (cycle.Delta > 0 && cycle.CurRawCheckResult && cycle.LastCheckRangeDidLengthDrift)
                    ) {
                        ctx.Console.Write("{");
                        inLengthDriftHighlight = true;
                    }
                }
            }

            //Write the check result
            if(cycle != null) ctx.Console.Write((rawCheckSequence ? cycle.CurRawCheckResult : cycle.CurRangeCheckResult) ? "C" : "X");
            else ctx.Console.Write(pred.CheckResult ? "C" : "X");

            //Highlight drifts if enabled - closing brackets
            if(cycle != null) {
                if(highlightGroupDrifts && inGroupDriftHighlight) {
                    if(
                        (cycle.ResidualDrift < 0 && cycle.LastRawCheckDidGroupDrift && (cycle.PrevRawCheckResult || cycle.NextRawCheckResult)) ||
                        (cycle.ResidualDrift > 0 && cycle.LastRawCheckDidGroupDrift && cycle.CurRawCheckResult)
                    ) {
                        ctx.Console.Write(">");
                        inGroupDriftHighlight = false;
                    }
                }
                if(!rawCheckSequence && highlightLengthDrifts && inLengthDriftHighlight) {
                    if(
                        (cycle.Delta < 0 && cycle.LastRawCheckDidGroupDrift && cycle.CurRawCheckResult) ||
                        (cycle.Delta > 0 && cycle.LastCheckRangeDidLengthDrift && (cycle.TicksSinceLastRawCheck >= cycle.BaseLength || cycle.NextRawCheckResult))
                    ) {
                        ctx.Console.Write("}");
                        inLengthDriftHighlight = false;
                    }
                }
            }
        }
        ctx.Console.WriteLine(string.Empty);
    }
}