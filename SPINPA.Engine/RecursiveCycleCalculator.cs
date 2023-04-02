using System.Numerics;

namespace SPINPA.Engine;

public static class RecursiveCycleCalculator {
    public static long CalcCycleLength(BigRational interv, BigRational delta) {
        Assert.AssertTrue(interv >= delta);
        return (long) BigRational.Round(interv / BigRational.Abs(delta));
    }

    public static BigRational CalcBoundedOffset(BigRational interv, BigRational delta, BigRational off) {
        off = CalcUtils.RMod(off, interv);

        //Magical off_B edge cases
        if(delta > 0 && off > interv - BigRational.Abs(delta)) off -= interv;
        if(delta < 0 && off > 0) off -= interv;

        return off;
    }

    public static long CalcCycleGroup(BigRational interv, BigRational delta, BigRational off) {
        off = CalcBoundedOffset(interv, delta, off);

        long group = long.Abs((long) BigRational.Ceiling(off / BigRational.Abs(delta))), len = CalcCycleLength(interv, delta);
        Assert.AssertTrue(0 <= group && group <= len);
        Assert.AssertTrue(0 <= group*delta - off && group*delta - off < BigRational.Abs(delta));
        return group % len;
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

            off = CalcBoundedOffset(interv, delta, off);
            if(BigRational.Sign(delta) == BigRational.Sign(residualDrift)) off -= BigRational.Sign(delta) * residualDrift;

            interv = BigRational.Abs(delta);
            delta = -BigRational.Sign(delta) * residualDrift;
        }
    }
}