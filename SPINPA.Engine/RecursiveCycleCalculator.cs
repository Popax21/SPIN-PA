using System.Numerics;

namespace SPINPA.Engine;

public static class RecursiveCycleCalculator {
    public static long CalcCycleLength(BigRational interv, BigRational delta) {
        BigRational f = interv / BigRational.Abs(delta);
        if(1 < f && f < 2) return 2;
        return (long) BigRational.Round(f);
    }

    public static BigRational CalcResidualDrift(BigRational interv, BigRational delta) => (interv - CalcCycleLength(interv, delta) * BigRational.Abs(delta));
    public static BigRational CalcOffsetDrift(BigRational interv, BigRational delta, long numFrames) => -((delta * numFrames) % interv);

    public static long CalcCycleGroup(BigRational interv, BigRational delta, BigRational off) => CalcUtils.RMod((long) BigRational.Ceiling(off / delta), CalcCycleLength(interv, delta));

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