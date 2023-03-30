using System.Collections.Generic;
using System.Numerics;

namespace SPINPA.Engine;

public readonly record struct EffectiveDTRange(long StartFrame, long EndFrame, float StartTimeActive, int TimeActiveExponent, float EffectiveDT, BigRational TransitionDT) {
    public static IEnumerable<EffectiveDTRange> EnumerateDTRanges() {
        int dtExp = FloatUtils.GetExponent(Constants.DeltaTime), dtMant = FloatUtils.GetMantissa(Constants.DeltaTime);

        long startFrame = 0;
        float timeActive = Constants.DeltaTime;
        while(true) {
            Assert.AssertTrue(FloatUtils.IsNormalized(timeActive));
            int taExp = FloatUtils.GetExponent(timeActive), taMant = FloatUtils.GetMantissa(timeActive);
            Assert.AssertTrue(taExp >= dtExp);

            float effDt = float.NaN, lastRangeTA = timeActive;
            int numFrames = 1;
            if(FloatUtils.GetExponent(timeActive + Constants.DeltaTime) == taExp) {
                //Determine effective deltatime
                int effDtMant = (FloatUtils.GetMantissa(timeActive + Constants.DeltaTime) - taMant) << (taExp - dtExp);
                effDt = FloatUtils.BuildFloatyNormFloat(0, dtExp, effDtMant);

                //Determine size of range
                int taMantDelta = effDtMant >> (taExp - dtExp);
                if(taMantDelta == 0) {
                    //We've reached the freeze range
                    yield return new EffectiveDTRange(startFrame, long.MaxValue, timeActive, taExp, 0, float.NaN);
                    yield break;
                }

                numFrames = ((1 << FloatUtils.MantissaBits) - taMant + taMantDelta-1) / taMantDelta;
                Assert.AssertTrue(numFrames > 0);

                lastRangeTA = FloatUtils.BuildFloat(0, taExp, taMant + (numFrames-1) * taMantDelta);
            }

            //Manually step through the transition, because its float jank is extremly stinky
            float nextTA = lastRangeTA + Constants.DeltaTime;
            BigRational transEffDt = new BigRational(nextTA) - new BigRational(lastRangeTA);

            //Yield this range
            yield return new EffectiveDTRange(startFrame, startFrame + numFrames, timeActive, taExp, effDt, transEffDt);
            timeActive = nextTA;
            startFrame += numFrames;
        }
    }
}