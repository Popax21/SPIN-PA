using System;
using System.Collections.Generic;
using System.Numerics;

namespace SPINPA.Engine;

public abstract class Validator {
    public void ValidateDTRanges() {
        IEnumerator<EffectiveDTRange> dtRangeIt = EffectiveDTRange.EnumerateDTRanges().GetEnumerator();
        string FormatCurrentDTRange() => $"exp={dtRangeIt.Current.TimeActiveExponent} frames={dtRangeIt.Current.StartFrame}-{dtRangeIt.Current.EndFrame} startTA={dtRangeIt.Current.StartTimeActive:F30} effDT={dtRangeIt.Current.EffectiveDT:F30}";

        Assert.AssertTrue(dtRangeIt.MoveNext());
        Log($"Starting effective DT range validation, initial range: {FormatCurrentDTRange()}");

        float timeActive = float.NaN;
        long frame = 0;
        foreach(float nextTimeActive in EnumerateTimeActiveValues()) {
            if(float.IsNaN(timeActive)) {
                timeActive = nextTimeActive;
                continue;
            }

            BigRational effDt = float.NaN;
            try {
                //Check that this frame is within the expected range
                EffectiveDTRange dtRange = dtRangeIt.Current;
                Assert.AssertTrue(dtRange.StartFrame <= frame && frame < dtRange.EndFrame);
                if(frame == dtRange.StartFrame) Assert.AssertTrue(dtRange.StartTimeActive == timeActive);

                //Determine and check effective deltatime
                effDt = new BigRational(nextTimeActive) - new BigRational(timeActive);
                if(dtRange.EndFrame <= frame+1) {
                    Log($"Validating range transition: exp={dtRange.TimeActiveExponent}->{dtRange.TimeActiveExponent+1} frame={frame} transEffDT={dtRange.TransitionDT:F30}");
                    Assert.AssertTrue(dtRange.TransitionDT == effDt);
                    Assert.AssertTrue(dtRangeIt.MoveNext());
                    Log($"Entering new effective DT range: {FormatCurrentDTRange()}");
                } else {
                    Assert.AssertTrue(new BigRational(dtRange.EffectiveDT) == effDt);
                }

                //Check if we have reached the freeze range
                if(effDt <= 0) {
                    Assert.AssertTrue(dtRange.EndFrame == long.MaxValue);
                    break;
                }
            } catch(Exception e) {
                throw new ApplicationException($"Error during validation of frame {frame} [TimeActive={timeActive:F30} effDt={effDt:F30}]", e);
            }

            //Move onto next frame
            timeActive = nextTimeActive;
            frame++;
        }

        Assert.AssertTrue(!dtRangeIt.MoveNext());
    }

    public void ValidateIntervalCheckCycles(float off, float intv) {
        IntervalCheckCyclePredictor pred = new IntervalCheckCyclePredictor(off, intv);

        float lastTimeActive = float.NaN;
        foreach(float timeActive in EnumerateTimeActiveValues()) {
            long frame = pred.CurrentFrame;

            try {
                //Check recursive cycle states
                // foreach(IntervalCheckCycleDTRangePredictor.RecursiveCycle cycle in pred.CurrentRange.DTRangePredictor.RecursiveCycles) {
                //     Console.WriteLine($"{cycle.CycleIndex} t {cycle.CurTickCount}: {cycle.CurRangeCheckResult} == {CalcUtils.RMod(cycle.CurTickCount*cycle.Delta - cycle.Offset, cycle.Interval) < cycle.Threshold} | {CalcUtils.RMod(cycle.CurTickCount*cycle.Delta - cycle.Offset, cycle.Interval):F30} | {cycle.Threshold:F30} {cycle.Delta:F30} {cycle.Offset:F30} {cycle.ResidualDrift:F30}");
                // }

                foreach(IntervalCheckCycleDTRangePredictor.RecursiveCycle cycle in pred.CurrentRange.DTRangePredictor.RecursiveCycles) {
                    Assert.AssertTrue((CalcUtils.RMod(cycle.CurTickCount*cycle.Delta - cycle.Offset, cycle.Interval) < BigRational.Abs(cycle.Delta)) == cycle.CurRawCheckResult);
                    Assert.AssertTrue((CalcUtils.RMod(cycle.CurTickCount*cycle.Delta - cycle.Offset, cycle.Interval) < cycle.Threshold) == cycle.CurRangeCheckResult);
                }

                //Check the predicted results against the expected
                bool expectedRes = DoOnIntervalCheck(timeActive, intv, off);

                //-> Single advance
                Assert.AssertTrue(pred.CheckResult == expectedRes);

                // //-> Rewind from 0
                // pred.CurrentRange.DTRangePredictor.Reset();
                // pred.CurrentFrame = frame;
                // Assert.AssertTrue(pred.CheckResult == expectedRes);

                // //-> Rewind from random frame
                // pred.CurrentRange.DTRangePredictor.Reset();
                // pred.CurrentRange.DTRangePredictor.AdvanceFrames(new Random(unchecked((int) frame)).NextInt64(0, frame));
                // pred.CurrentFrame = frame;
                // Assert.AssertTrue(pred.CheckResult == expectedRes);

            } catch(Exception e) {
                throw new ApplicationException($"Error during validation of frame {frame} [TimeActive={timeActive:F30} OnInterval={DoOnIntervalCheck(timeActive, intv, off)} CheckResult={pred.CheckResult}]", e);
            }

            //Check if TimeActive froze
            if(timeActive == lastTimeActive) break;
            lastTimeActive = timeActive;

            //Advance to the next frame
            int lastRangeIdx = pred.CurrentRangeIndex;
            pred.CurrentFrame++;
            if(lastRangeIdx != pred.CurrentRangeIndex) {
                Log($"Advancing DT ranges {lastRangeIdx}->{pred.CurrentRangeIndex} on frame {frame} [frames {pred.CurrentRange.StartFrame}-{pred.CurrentRange.EndFrame} effDt={pred.CurrentRange.DTRangePredictor.EffectiveDT:F30}]");
                pred.CurrentRange.DTRangePredictor.Reset();
            }
        }
    }


    protected virtual void Log(string ms) {}
    protected abstract IEnumerable<float> EnumerateTimeActiveValues();
    protected abstract bool DoOnIntervalCheck(float timeActive, float interval, float offset);
}