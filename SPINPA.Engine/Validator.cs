using System;
using System.Collections.Generic;
using System.Numerics;

namespace SPINPA.Engine;

public abstract partial class Validator {
    public const int ProgressPrecision = 10000, MinReportingGap = 50;

    public void ValidateDTRanges() {
        IEnumerator<EffectiveDTRange> dtRangeIt = EffectiveDTRange.EnumerateDTRanges().GetEnumerator();
        string FormatCurrentDTRange() => $"exp={dtRangeIt.Current.TimeActiveExponent} frames={dtRangeIt.Current.StartFrame}-{dtRangeIt.Current.EndFrame} startTA={dtRangeIt.Current.StartTimeActive:F30} effDT={dtRangeIt.Current.EffectiveDT:F30}";

        long dtRangeIdx = 0;
        Assert.AssertTrue(dtRangeIt.MoveNext());
        LogMessage($"Starting effective DT range validation, initial range: {FormatCurrentDTRange()}");

        float timeActive = float.NaN;
        long frame = 0, progressUpdateTimer = 0;
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
                    LogMessage($"Validating range transition: exp={dtRange.TimeActiveExponent}->{dtRange.TimeActiveExponent+1} frame={frame} transEffDT={dtRange.TransitionDT:F30}");
                    Assert.AssertTrue(dtRange.TransitionDT == effDt);
                    Assert.AssertTrue(dtRangeIt.MoveNext());
                    LogMessage($"Entering new effective DT range: {FormatCurrentDTRange()}");

                    progressUpdateTimer = 0;
                    dtRangeIdx++;
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

            if(progressUpdateTimer-- < 0) {
                progressUpdateTimer = long.Max(dtRangeIt.Current.NumFrames / ProgressPrecision, MinReportingGap);
                LogProgress($"Validating DT range {dtRangeIdx} [frames {dtRangeIt.Current.StartFrame}-{dtRangeIt.Current.EndFrame}]", (float) (frame - dtRangeIt.Current.StartFrame) / dtRangeIt.Current.NumFrames);
            }
        }

        Assert.AssertTrue(!dtRangeIt.MoveNext());
    }

    public void ValidateIntervalCheckCycles(float off, float intv, bool runDeepValidation = true, bool runSkipValidation = true) {
        IntervalCheckPredictor pred = new IntervalCheckPredictor(off, intv);

        //>>>>>>>>>>>>>>> DEEP VALIDATION <<<<<<<<<<<<<<<
        //Go over each frame and check the predicted state against the expected
        if(runDeepValidation) {
            LogMessage($"Starting deep interval check cycle validation for offset {off:F30} interval {intv:F30}");
            pred.CurrentFrame = 0;
            ValidateIntervalCheckCyclesDeep(pred, off, intv);
        }

        //>>>>>>>>>>>>>>> SKIP VALIDATION <<<<<<<<<<<<<<<
        //Skip over large ranges of frames at once
        if(runSkipValidation) {
            LogMessage($"Starting skip interval check cycle validation for offset {off:F30} interval {intv:F30}");
            pred.CurrentFrame = 0;
            ValidateIntervalCheckCyclesSkip(pred, off, intv);
        }
    }

    private void ValidateIntervalCheckCyclesDeep(IntervalCheckPredictor pred, float off, float intv) {
        long[] nextValidateOnTicks = null!;
        long[] lastRawCheckTicks = null!, nextRawCheckTicks = null!;
        long[] lastRangeCheckTicks = null!, nextRangeCheckTicks = null!;
        bool[] nextGroupDriftIndicators = null!, nextLengthDriftIndicators = null!;
        long[] nextGroupDriftTicks = null!, nextLengthDriftTicks = null!;

        int lastRangeIdx = -1;
        float lastTimeActive = float.NaN;
        long progressUpdateTimer = 0;
        foreach(float timeActive in EnumerateTimeActiveValues()) {
            long frame = pred.CurrentFrame;
            IntervalCheckDTRangePredictor dtPred = pred.CurrentDTRangePredictor;

            //Handle range advances
            if(lastRangeIdx != pred.CurrentRangeIndex) {
                LogMessage($"Entering DT range {pred.CurrentRangeIndex} on frame {frame}: frames {pred.CurrentRange.StartFrame}-{pred.CurrentRange.EndFrame} effDt={dtPred.EffectiveDT:F30}");
                lastRangeIdx = pred.CurrentRangeIndex;
                progressUpdateTimer = 0;

                //Initialize cycle validation arrays
                InitCycleNextValidationTicks(dtPred, ref nextValidateOnTicks);
                InitCycleCheckResultTicks(dtPred, true, ref lastRawCheckTicks, ref nextRawCheckTicks);
                InitCycleCheckResultTicks(dtPred, false, ref lastRangeCheckTicks, ref nextRangeCheckTicks);
                InitCycleDriftIndicators(dtPred, true, ref nextGroupDriftIndicators, ref nextGroupDriftTicks);
                InitCycleDriftIndicators(dtPred, false, ref nextLengthDriftIndicators, ref nextLengthDriftTicks);
                dtPred.Reset();
            }

            try {
                //Check the predicted check result against the expected
                Assert.AssertTrue(pred.CheckResult == DoOnIntervalCheck(timeActive, intv, off));

                //Check recursive cycle states
                foreach(IntervalCheckDTRangePredictor.RecursiveCycle cycle in dtPred.RecursiveCycles) {
                    ValidateRecursiveCycleDeep(
                        cycle, nextValidateOnTicks,
                        lastRawCheckTicks, nextRawCheckTicks,
                        lastRangeCheckTicks, nextRangeCheckTicks,
                        nextGroupDriftIndicators, nextLengthDriftIndicators,
                        nextGroupDriftTicks, nextLengthDriftTicks
                    );
                }
            } catch(Exception e) {
                throw new ApplicationException($"Error during validation of frame {frame} [TimeActive={timeActive:F30} OnInterval={DoOnIntervalCheck(timeActive, intv, off)} CheckResult={pred.CheckResult}]", e);
            }

            //Check if TimeActive froze
            if(timeActive == lastTimeActive) break;
            lastTimeActive = timeActive;

            //Advance to the next frame
            pred.CurrentFrame++;

            if(progressUpdateTimer-- < 0) {
                progressUpdateTimer = long.Max(pred.CurrentRange.NumFrames / ProgressPrecision, MinReportingGap);
                LogProgress($"Deep-Validating DT range {1+pred.CurrentRangeIndex}/{pred.Ranges.Length} [frames {pred.CurrentRange.StartFrame}-{pred.CurrentRange.EndFrame}]", (float) dtPred.CurrentFrame / pred.CurrentRange.NumFrames);
            }
        }
    }

    private void ValidateIntervalCheckCyclesSkip(IntervalCheckPredictor pred, float off, float intv) {
        Random rng = new Random(421234);
        
        long[] nextValidateOnTicks = null!;

        int lastRangeIdx = -1;
        float lastTimeActive = float.NaN;
        long frame = 0, progressUpdateTimer = 0;
        foreach(float timeActive in EnumerateTimeActiveValues()) {
            //Advance to this frame for some random frames, and check if the expected state matches
            if(rng.NextInt64(pred.CurrentRange.NumFrames / 40) < 1) {
                long prevFrame = pred.CurrentFrame;
                pred.CurrentFrame = frame;
                IntervalCheckDTRangePredictor dtPred = pred.CurrentDTRangePredictor;

                //Handle range advances
                if(lastRangeIdx != pred.CurrentRangeIndex) {
                    LogMessage($"Entering DT range {pred.CurrentRangeIndex} on frame {frame}: frames {pred.CurrentRange.StartFrame}-{pred.CurrentRange.EndFrame} effDt={dtPred.EffectiveDT:F30}");
                    lastRangeIdx = pred.CurrentRangeIndex;
                    progressUpdateTimer = 0;

                    //Initialize cycle validation arrays
                    InitCycleNextValidationTicks(dtPred, ref nextValidateOnTicks);
                }

                try {
                    //Check recursive cycle states
                    foreach(IntervalCheckDTRangePredictor.RecursiveCycle cycle in pred.CurrentDTRangePredictor.RecursiveCycles) {
                        int cycleIdx = cycle.CycleIndex;

                        if(nextValidateOnTicks[cycleIdx] > cycle.CurTickIndex) break;
                        nextValidateOnTicks[cycleIdx]++;

                        //Raw + range checks
                        ValidateCycleCheckResults(cycle);
                    }

                    //Check the predicted result against the expected
                    Assert.AssertTrue(pred.CheckResult == DoOnIntervalCheck(timeActive, intv, off));
                } catch(Exception e) {
                    throw new ApplicationException($"Error during validation of frame skip {prevFrame}->{frame} [TimeActive={timeActive:F30} OnInterval={DoOnIntervalCheck(timeActive, intv, off)} CheckResult={pred.CheckResult}]", e);
                }
            }
            //Check if TimeActive froze
            if(timeActive == lastTimeActive) break;
            lastTimeActive = timeActive;

            if(progressUpdateTimer-- < 0) {
                progressUpdateTimer = long.Max(pred.CurrentRange.NumFrames / ProgressPrecision, MinReportingGap);
                LogProgress($"Skip-Validating DT range {1+pred.CurrentRangeIndex}/{pred.Ranges.Length} [frames {pred.CurrentRange.StartFrame}-{pred.CurrentRange.EndFrame}]", (float) pred.CurrentDTRangePredictor.CurrentFrame / pred.CurrentRange.NumFrames);
            }

            frame++;
        }
    }

    protected virtual void LogMessage(string ms) {}
    protected virtual void LogProgress(string? status, float progress) {}

    protected abstract IEnumerable<float> EnumerateTimeActiveValues();
    protected abstract bool DoOnIntervalCheck(float timeActive, float interval, float offset);
}