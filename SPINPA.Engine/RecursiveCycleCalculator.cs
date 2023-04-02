using System.Numerics;

namespace SPINPA.Engine;

public static class RecursiveCycleCalculator {
    public static long CalcCycleGroup(BigRational interv, BigRational delta, BigRational off) {
        off = CalcUtils.RMod(off, interv);

        //Magical edge cases
        if(delta > 0 && off - interv > -BigRational.Abs(delta)) off -= interv;
        if(delta < 0 && off > 0) off -= interv;

        long group = long.Abs((long) BigRational.Ceiling(off / BigRational.Abs(delta)));
        Assert.AssertTrue(0 <= group && group <= CalcCycleLength(interv, delta));
        return group;
    }

    public static long CalcCycleLength(BigRational interv, BigRational delta) {
        BigRational f = interv / BigRational.Abs(delta);
        if(1 < f && f < 2) return 2;
        return (long) BigRational.Round(f);
    }

    public static BigRational CalcResidualDrift(BigRational interv, BigRational delta) => (interv - CalcCycleLength(interv, delta) * BigRational.Abs(delta));
    public static BigRational CalcOffsetDrift(BigRational interv, BigRational delta, long numFrames) => -((delta * numFrames) % interv);

    public static long CalcBaseLength(BigRational thresh, BigRational delta) => (long) BigRational.Floor(thresh / BigRational.Abs(delta));
    public static BigRational CalcLengthOffset(BigRational thresh, BigRational delta) => thresh % BigRational.Abs(delta);

    public static void DescendRecursionLevels(ref BigRational interv, ref BigRational delta, int recLevel) {
        BigRational off = default;
        DescendRecursionLevels(ref interv, ref delta, ref off, recLevel);
    }

    public static void DescendRecursionLevels(ref BigRational interv, ref BigRational delta, ref BigRational off, int recLevel) {
        for(; recLevel > 0; recLevel--) {
            BigRational residualDrift = CalcResidualDrift(interv, delta);
            if(BigRational.Sign(delta) == BigRational.Sign(residualDrift)) off += BigRational.Sign(delta) * residualDrift;
            interv = BigRational.Abs(delta);
            delta = -BigRational.Sign(delta) * residualDrift;
        }
    }
}