using System;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace SPINPA.Engine;

public abstract partial class Validator {
    private static void InitCycleValidationArray<T>(IntervalCheckDTRangePredictor pred, [NotNull] ref T[]? arr) {
        if(arr == null || arr.Length < pred.RecursiveCycles.Length) Array.Resize<T>(ref arr, pred.RecursiveCycles.Length);
    }

    private static void InitCycleNextValidationTicks(IntervalCheckDTRangePredictor pred, ref long[] nextValidateOnTicks) {
        InitCycleValidationArray(pred, ref nextValidateOnTicks);
        foreach(IntervalCheckDTRangePredictor.RecursiveCycle cycle in pred.RecursiveCycles) nextValidateOnTicks[cycle.CycleIndex] = cycle.CurTickIndex;
    }

    //Optimized validation method for better performance
    private static void ValidateCycleCheckResult(IntervalCheckDTRangePredictor.RecursiveCycle cycle, long tickIdx, bool rawCheck, bool expectedRes) {
        BigRational.SafeCPU cpu = BigRational.task_cpu;

        //Calculate left-side term
        cpu.push(tickIdx);
        cpu.mul(cycle.Delta);
        cpu.push(cycle.Offset);
        cpu.sub();
        cpu.norm();

        cpu.dup();
        cpu.push(cycle.Interval);
        cpu.div();
        cpu.rnd(0, (cpu.sign(1) < 0) ? 4 : 0);
        cpu.mul(cycle.Interval);
        cpu.neg();
        cpu.add();

        //Push threshold on stack
        cpu.push(rawCheck ? cycle.Delta : cycle.Threshold);
        if(rawCheck) cpu.abs();

        //Do the comparison
        Assert.AssertTrue((cpu.cmp(1, 0) < 0) == expectedRes);
        cpu.pop(2);
    }

    private static void ValidateCycleCheckResults(IntervalCheckDTRangePredictor.RecursiveCycle cycle) {
        ValidateCycleCheckResult(cycle, cycle.CurTickIndex-1, true, cycle.PrevRawCheckResult);
        ValidateCycleCheckResult(cycle, cycle.CurTickIndex, true, cycle.CurRawCheckResult);
        ValidateCycleCheckResult(cycle, cycle.CurTickIndex+1, true, cycle.NextRawCheckResult);

        ValidateCycleCheckResult(cycle, cycle.CurTickIndex-1, false, cycle.PrevRangeCheckResult);
        ValidateCycleCheckResult(cycle, cycle.CurTickIndex, false, cycle.CurRangeCheckResult);
        ValidateCycleCheckResult(cycle, cycle.CurTickIndex+1, false, cycle.NextRangeCheckResult);
    }

    private static void InitCycleCheckResultTicks(IntervalCheckDTRangePredictor pred, bool rawCheck, ref long[] lastCheckTicks, ref long[] nextCheckTicks) {
        InitCycleValidationArray(pred, ref lastCheckTicks);
        InitCycleValidationArray(pred, ref nextCheckTicks);

        foreach(IntervalCheckDTRangePredictor.RecursiveCycle cycle in pred.RecursiveCycles) {
            if(rawCheck) {
                ValidateCycleCheckResult(cycle, cycle.CurTickIndex-1, true, cycle.PrevRawCheckResult);
                lastCheckTicks[cycle.CycleIndex] = cycle.CurTickIndex - (cycle.PrevRawCheckResult ? 1 : cycle.TicksSinceLastRawCheck);
                nextCheckTicks[cycle.CycleIndex] = cycle.CurTickIndex + cycle.TicksTillAnyRawCheck;
            } else {
                ValidateCycleCheckResult(cycle, cycle.CurTickIndex-1, false, cycle.PrevRangeCheckResult);
                lastCheckTicks[cycle.CycleIndex] = cycle.CurTickIndex - (cycle.PrevRangeCheckResult ? 1 : cycle.TicksSinceLastRangeCheck);
                nextCheckTicks[cycle.CycleIndex] = cycle.CurTickIndex + cycle.TicksTillAnyRangeCheck;
            }
        }
    }

    private static void ValidateCycleCheckResults(IntervalCheckDTRangePredictor.RecursiveCycle cycle, long lastRawCheckTick, long lastRangeCheckTick) {
        Assert.AssertTrue(cycle.PrevRawCheckResult == (lastRawCheckTick == cycle.CurTickIndex-1));
        ValidateCycleCheckResult(cycle, cycle.CurTickIndex, true, cycle.CurRawCheckResult);
        ValidateCycleCheckResult(cycle, cycle.CurTickIndex+1, true, cycle.NextRawCheckResult);

        Assert.AssertTrue(cycle.PrevRangeCheckResult == (lastRangeCheckTick == cycle.CurTickIndex-1));
        ValidateCycleCheckResult(cycle, cycle.CurTickIndex, false, cycle.CurRangeCheckResult);
        ValidateCycleCheckResult(cycle, cycle.CurTickIndex+1, false, cycle.NextRangeCheckResult);
    }

    private static void ValidateCycleLastTickPrediction(IntervalCheckDTRangePredictor.RecursiveCycle cycle, long predTicks, bool curRes, ref long lastTick) {
        if(curRes) lastTick = cycle.CurTickIndex;
        Assert.AssertTrue(predTicks == cycle.CurTickIndex - lastTick);
    }

    private static void ValidateCycleNextTickPrediction(IntervalCheckDTRangePredictor.RecursiveCycle cycle, long predTicks, bool curRes, ref long nextTick) {
        if(curRes) {
            Assert.AssertTrue(nextTick == cycle.CurTickIndex);
            nextTick = cycle.CurTickIndex + predTicks;
        } else {
            Assert.AssertTrue(nextTick > cycle.CurTickIndex);
            Assert.AssertTrue(nextTick == cycle.CurTickIndex + predTicks);
        }
    }

    private static void InitCycleDriftIndicators(IntervalCheckDTRangePredictor pred, bool groupDrift, ref bool[] nextIndicators, ref long[] nextDriftTicks) {
        InitCycleValidationArray(pred, ref nextIndicators);
        InitCycleValidationArray(pred, ref nextDriftTicks);

        foreach(IntervalCheckDTRangePredictor.RecursiveCycle cycle in pred.RecursiveCycles) {
            if(groupDrift) {
                nextIndicators[cycle.CycleIndex] = cycle.LastRawCheckDidGroupDrift;
                nextDriftTicks[cycle.CycleIndex] = cycle.CurTickIndex + cycle.TicksTillAnyGroupDrift;
            } else {
                nextIndicators[cycle.CycleIndex] = cycle.LastCheckRangeDidLengthDrift;
                nextDriftTicks[cycle.CycleIndex] = cycle.CurTickIndex + cycle.TicksTillAnyLengthDrift;
            }
        }
    }

    private static void ValidateCycleDriftIndicator(IntervalCheckDTRangePredictor.RecursiveCycle cycle, bool lastDidDrift, bool nextDidDrift, ref bool nextIndicator) {
        Assert.AssertTrue(lastDidDrift == nextIndicator);
        if(cycle.NextRawCheckResult) nextIndicator = nextDidDrift;
    }

    private static void ValidateRecursiveCycleDeep(
        IntervalCheckDTRangePredictor.RecursiveCycle cycle, long[] nextValidateOnTicks,
        long[] lastRawCheckTicks, long[] nextRawCheckTicks,
        long[] lastRangeCheckTicks, long[] nextRangeCheckTicks,
        bool[] nextGroupDriftIndicators, bool[] nextLengthDriftIndicators,
        long[] nextGroupDriftTicks, long[] nextLengthDriftTicks
    ) {
        int cycleIdx = cycle.CycleIndex;

        try {
            Assert.AssertTrue(nextValidateOnTicks[cycleIdx] >= cycle.CurTickIndex);
            if(nextValidateOnTicks[cycleIdx] > cycle.CurTickIndex) return;
            nextValidateOnTicks[cycleIdx]++;

            //Raw + range checks
            ValidateCycleCheckResults(cycle, lastRawCheckTicks[cycleIdx], lastRangeCheckTicks[cycleIdx]);

            //Raw + range check prediction
            ValidateCycleLastTickPrediction(cycle, cycle.TicksSinceLastRawCheck, cycle.CurRawCheckResult, ref lastRawCheckTicks[cycleIdx]);
            ValidateCycleNextTickPrediction(cycle, cycle.TicksTillNextRawCheck, cycle.CurRawCheckResult, ref nextRawCheckTicks[cycleIdx]);

            if(cycle.HasRangeChecks) {
                ValidateCycleLastTickPrediction(cycle, cycle.TicksSinceLastRangeCheck, cycle.CurRangeCheckResult, ref lastRangeCheckTicks[cycleIdx]);
                ValidateCycleNextTickPrediction(cycle, cycle.TicksTillNextRangeCheck, cycle.CurRangeCheckResult, ref nextRangeCheckTicks[cycleIdx]);
            } else {
                Assert.AssertTrue(!cycle.PrevRangeCheckResult);
                Assert.AssertTrue(!cycle.CurRangeCheckResult);
                Assert.AssertTrue(!cycle.NextRangeCheckResult);
                Assert.AssertTrue(cycle.TicksSinceLastRangeCheck == -1);
                Assert.AssertTrue(cycle.TicksTillNextRangeCheck == -1);
            }

            //Group drift validation
            if(cycle.HasGroupDrifts) {
                Assert.AssertTrue(cycle.TicksTillNextRawCheck + cycle.TicksSinceLastRawCheck == cycle.CycleLength + (cycle.NextRawCheckDidGroupDrift ? BigRational.Sign(cycle.ResidualDrift) : 0));
                ValidateCycleDriftIndicator(cycle, cycle.LastRawCheckDidGroupDrift, cycle.NextRawCheckDidGroupDrift, ref nextGroupDriftIndicators[cycleIdx]);
                ValidateCycleNextTickPrediction(cycle, cycle.TicksTillNextGroupDrift, cycle.CurRawCheckResult && cycle.LastRawCheckDidGroupDrift, ref nextGroupDriftTicks[cycleIdx]);
            } else {
                Assert.AssertTrue(!cycle.LastRawCheckDidGroupDrift);
                Assert.AssertTrue(!cycle.NextRawCheckDidGroupDrift);
                Assert.AssertTrue(cycle.TicksTillNextGroupDrift == -1);
                Assert.AssertTrue(cycle.TicksTillAnyGroupDrift == -1);
            }

            //Length drift validation
            if(cycle.HasLengthDrifts) {
                if(cycle.Delta > 0 || cycle.CurRawCheckResult) Assert.AssertTrue(cycle.CurRangeCheckResult == (cycle.TicksSinceLastRawCheck < cycle.BaseLength + (cycle.LastCheckRangeDidLengthDrift ? 1 : 0)));
                else Assert.AssertTrue(cycle.CurRangeCheckResult == (cycle.TicksTillAnyRawCheck < cycle.BaseLength + (cycle.NextCheckRangeDidLengthDrift ? 1 : 0)));
                ValidateCycleDriftIndicator(cycle, cycle.LastCheckRangeDidLengthDrift, cycle.NextCheckRangeDidLengthDrift, ref nextLengthDriftIndicators[cycleIdx]);
                ValidateCycleNextTickPrediction(cycle, cycle.TicksTillNextLengthDrift, cycle.CurRawCheckResult && cycle.LastCheckRangeDidLengthDrift, ref nextLengthDriftTicks[cycleIdx]);
            } else {
                Assert.AssertTrue(!cycle.LastCheckRangeDidLengthDrift);
                Assert.AssertTrue(!cycle.NextCheckRangeDidLengthDrift);
                Assert.AssertTrue(cycle.TicksTillNextLengthDrift == -1);
                Assert.AssertTrue(cycle.TicksTillAnyLengthDrift == -1);
            }
        } catch(Exception e) {
            throw new ApplicationException($"Error during validation of recursive cycle {cycleIdx} [CurCycleOffset={cycle.CurCycleOffset} CurCycleTarget={cycle.CurCycleTarget} CycleLength={cycle.CycleLength} BaseLength={cycle.BaseLength}]", e);
        }
    }
}