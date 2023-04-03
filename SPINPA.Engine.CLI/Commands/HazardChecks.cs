using System;
using System.CommandLine;

namespace SPINPA.Engine.CLI;

public sealed class HazardChecksCommand : CLICommand {
    private readonly Option<long> startFrameOpt, numFramesOpt;
    private readonly Option<float> checkIntvOpt;
    private readonly Argument<float> hazardOffArg;

    public HazardChecksCommand() : base("hazard_checks", "Prints the sequence of load checks performed by a certain hazard") {
        Command.AddOption(startFrameOpt = new Option<long>("--start-frame", description: "The starting frame of the sequence to print", getDefaultValue: () => 0));
        Command.AddOption(numFramesOpt = new Option<long>("--num-frames", description: "The number frame of the sequence to print", getDefaultValue: () => long.MaxValue));
        Command.AddOption(checkIntvOpt = new Option<float>("--check-interval", description: "The OnInterval check interval", getDefaultValue: () => Constants.HazardLoadInterval));
        Command.AddArgument(hazardOffArg = new Argument<float>("hazard-offset", description: "The hazard offset whose sequence to print"));
        Command.SetHandler(PrintCheckSequence, startFrameOpt, numFramesOpt, hazardOffArg, checkIntvOpt);
    }

    private void PrintCheckSequence(long startFrame, long numFrames, float hazardOff, float checkIntv) {
        IntervalCheckCyclePredictor pred = new IntervalCheckCyclePredictor(hazardOff, checkIntv);
        foreach(bool res in pred.PredictCheckResults(startFrame, (numFrames < long.MaxValue) ? (startFrame + numFrames) : long.MaxValue)) {
            Console.Write(res ? 'C' : 'X');
        }
        Console.WriteLine();
    }
}