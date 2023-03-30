using System.Numerics;

namespace SPINPA.Engine;

public static class CycleCalculator {
    public static long CalcCycleLength(BigRational interv, BigRational delta) {
        BigRational f = interv / BigRational.Abs(delta);
        if(1 < f && f < 2) return 2;
        return (long) BigRational.Round(f);
    }

    public static BigRational CalcResidualDrift(BigRational interv, BigRational delta) => (interv - CalcCycleLength(interv, delta) * BigRational.Abs(delta));

    public static void DescendRecursionLevels(ref BigRational interv, ref BigRational delta, int recLevel) {
        BigRational off = default;
        DescendRecursionLevels(ref interv, ref delta, ref off, recLevel);
    }

    public static void DescendRecursionLevels(ref BigRational interv, ref BigRational delta, ref BigRational off, int recLevel) {
        for(; recLevel > 0; recLevel--) {
            BigRational residualDrift = CalcResidualDrift(interv, delta);
            interv = BigRational.Abs(delta);
            delta = -BigRational.Sign(delta) * residualDrift;
            if(BigRational.Sign(delta) != BigRational.Sign(residualDrift)) off += delta;
        }
    }
}