using System;
using System.Numerics;

namespace SPINPA.Engine;

public static class Assert {
    private sealed class AssertionFailureException : ApplicationException {
        public AssertionFailureException() : base("ASSERTION FAILURE") {}
    }

    public static void AssertTrue(bool cond) {
        if(!cond) throw new AssertionFailureException();
    }
}

public static class FloatUtils {
    public const int ExponentBits = 8, MantissaBits = 23;
    public const int NormalizedMantissaBit = 1 << MantissaBits;
    public const int ExponentBias = 127;

    public static int GetMantissa(float v) => (int) (BitConverter.SingleToUInt32Bits(v) & ((1 << MantissaBits) - 1));
    public static int GetExponent(float v) => (int) ((BitConverter.SingleToUInt32Bits(v) >> MantissaBits) & ((1 << ExponentBits) - 1));
    public static int GetSignBit(float v) => (int) (BitConverter.SingleToUInt32Bits(v) >> (MantissaBits + ExponentBits));

    public static float BuildFloat(int sign, int exp, int mant) {
        Assert.AssertTrue(0 <= sign && sign <= 1);
        Assert.AssertTrue(0 <= exp && exp < (1 << ExponentBits));
        Assert.AssertTrue(0 <= mant && mant < (1 << MantissaBits));
        return BitConverter.UInt32BitsToSingle(((uint) sign << (MantissaBits + ExponentBits)) | ((uint) exp << MantissaBits) | (uint) mant);
    }

    public static bool IsNormalized(float v) => GetExponent(v) is (> 0) and (< 0xff);

    public static float BuildFloatyNormFloat(int sign, int exp, int mant) {
        int mantOff = (31 - BitOperations.LeadingZeroCount((uint) mant)) - MantissaBits;
        if(mantOff <= 0) return BuildFloat(sign, exp + mantOff, (mant << -mantOff) & (NormalizedMantissaBit - 1));
        
        Assert.AssertTrue((mant & (1 << (mantOff - 1))) == 0);
        return BuildFloat(sign, exp + mantOff, (mant >> mantOff) & (NormalizedMantissaBit - 1));
    }

}