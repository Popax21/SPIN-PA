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

    protected virtual void Log(string ms) {}
    protected abstract IEnumerable<float> EnumerateTimeActiveValues();
}