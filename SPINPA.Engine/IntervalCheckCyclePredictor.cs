using System;
using System.Collections.Generic;
using System.Numerics;

namespace SPINPA.Engine;

public sealed class IntervalCheckCyclePredictor {
    public readonly record struct DTRange(long StartFrame, long EndFrame, IntervalCheckCycleDTRangePredictor DTRangePredictor) {
        public readonly long NumFrames = (EndFrame < long.MaxValue) ? (EndFrame - StartFrame) : long.MaxValue;
    }

    public readonly BigRational Offset, Interval;
    public readonly DTRange[] Ranges;
    private int curRangeIdx = 0;

    public IntervalCheckCyclePredictor(float off, float intv) : this(new BigRational(off), new BigRational(intv)) {}
    public IntervalCheckCyclePredictor(BigRational off, BigRational intv) {
        Offset = off;
        Interval = intv;

        //Build deltatime ranges
        List<DTRange> ranges = new List<DTRange>();

        BigRational rOff = off, rDt = float.NaN;
        long rStart = -1, rEnd = -1;
        void AddRange(long startFrame, long endFrame, BigRational effDt) {
            if(rDt != effDt) {
                if(!BigRational.IsNaN(rDt)) {
                    //Emit the previous range
                    ranges.Add(new DTRange(rStart, rEnd, new IntervalCheckCycleDTRangePredictor(rOff, intv, rDt)));
                    rOff += RecursiveCycleCalculator.CalcOffsetDrift(intv, rDt, rEnd - rStart);
                }

                //Start a new range
                rDt = effDt;
                rStart = startFrame;
                rEnd = endFrame;
            } else {
                //Extend the last range
                Assert.AssertTrue(startFrame == rEnd);
                rEnd = endFrame;
            }
        }

        //Add effective DT ranges
        rOff += RecursiveCycleCalculator.CalcOffsetDrift(intv, new BigRational(Constants.DeltaTime), 1); //TimeActive is one frame offset from T in the SPIN doc
        foreach(EffectiveDTRange effDtRange in EffectiveDTRange.EnumerateDTRanges()) {
            if(!float.IsNaN(effDtRange.EffectiveDT)) AddRange(effDtRange.StartFrame, (effDtRange.EndFrame < long.MaxValue) ? (effDtRange.EndFrame-1) : long.MaxValue, new BigRational(effDtRange.EffectiveDT));
            if(!BigRational.IsNaN(effDtRange.TransitionDT)) AddRange(effDtRange.EndFrame-1, effDtRange.EndFrame, effDtRange.TransitionDT);
        }
        
        //Emit the last range
        if(!BigRational.IsNaN(rDt)) ranges.Add(new DTRange(rStart, rEnd, new IntervalCheckCycleDTRangePredictor(rOff, intv, rDt)));

        Ranges = ranges.ToArray();
    }

    public int GetRangeIndex(long frame) {
        //Use binary search for slightly more performance
        int l = 0, r = Ranges.Length;
        while(l < r-1) {
            int m = l + (r-l)/2;
            if(Ranges[m].StartFrame <= frame) l = m;
            else r = m;
        }

        Assert.AssertTrue(Ranges[l].StartFrame <= frame && frame < Ranges[l].EndFrame);
        return l;
    }

    public IEnumerable<bool> PredictCheckResults(long startFrame = 0, long endFrame = long.MaxValue) {
        CurrentFrame = startFrame;
        while(CurrentFrame < endFrame) {
            yield return CheckResult;
            CurrentFrame++;
        }
    }

    public long CurrentFrame {
        get => CurrentRange.StartFrame + CurrentRange.DTRangePredictor.CurrentFrame;
        set {
            if(value < CurrentRange.StartFrame || value >= CurrentRange.EndFrame) curRangeIdx = GetRangeIndex(value);
            CurrentRange.DTRangePredictor.CurrentFrame = value - CurrentRange.StartFrame;
        }
    }

    public ref DTRange CurrentRange => ref Ranges[curRangeIdx];
    public int CurrentRangeIndex => curRangeIdx;

    public bool CheckResult => CurrentRange.DTRangePredictor.CheckResult;
}

public sealed class IntervalCheckCycleDTRangePredictor {
    public class RecursiveCycle {
        public readonly IntervalCheckCycleDTRangePredictor Predictor;
        public readonly int CycleIndex;

        public readonly BigRational Offset, Interval, Delta, ResidualDrift;
        public readonly long InitialCycleTarget, CycleLength;

        public readonly BigRational Threshold, LengthOffset;
        public readonly int BaseLength;

        internal RecursiveCycle(IntervalCheckCycleDTRangePredictor pred, int idx, BigRational off, BigRational intv, BigRational delta, BigRational thresh) {
            Predictor = pred;
            CycleIndex = idx;

            Offset = RecursiveCycleCalculator.CalcBoundedOffset(intv, delta, off);
            Interval = intv;
            Delta = delta;
            ResidualDrift = RecursiveCycleCalculator.CalcResidualDrift(intv, delta);

            InitialCycleTarget = RecursiveCycleCalculator.CalcCycleGroup(intv, delta, off);
            CycleLength = RecursiveCycleCalculator.CalcCycleLength(intv, delta);

            Threshold = thresh;
            LengthOffset = RecursiveCycleCalculator.CalcLengthOffset(thresh, delta);
            BaseLength = (int) RecursiveCycleCalculator.CalcBaseLength(thresh, delta);
        }

        internal void Reset() {
            //Reset next recursive cycle
            NextCycle?.Reset();

            //Reset counters to tick -1
            CurTickCount = -1;
            CurCycleOffset = CycleLength-1;
            CurCycleTarget = CycleLength + InitialCycleTarget;

            TicksSinceLastRawCheck = CurCycleOffset - (InitialCycleTarget - ((NextCycle?.CurRawCheckResult ?? false) ? BigRational.Sign(ResidualDrift) : 0));
            Assert.AssertTrue(0 <= TicksSinceLastRawCheck);

            UpdateCheckResults();

            //Advance to tick 0
            AdvanceTicks(1);
        }

        internal long AdvanceTicks(long ticks, bool willUpdatePrev = false) {
            if(ticks < 0) throw new ArgumentOutOfRangeException(nameof(ticks));
            if(ticks == 0) return 0;

            //Split of the last tick advance from 2+ tick to ensure PrevRangeCheckResult is updated correctly
            if(ticks >= 2 && willUpdatePrev) return AdvanceTicks(ticks-1, willUpdatePrev: true) + AdvanceTicks(1, willUpdatePrev: true);

            //Update ticks since last raw check
            TicksSinceLastRawCheck += ticks;

            //Determine number of target hits
            Assert.AssertTrue(CurCycleOffset < CurCycleTarget && CurCycleTarget - CurCycleOffset <= CycleLength+1);
            long hitCount = 0, nextHitCount = 0, hRem = CurCycleTarget - CurCycleOffset;
            for(long tOff = 0; tOff < ticks;) {
                //Check if we can hit the target
                long rTicks = ticks - tOff;
                if(rTicks < hRem) break;

                //Register hit
                hitCount++;
                TicksSinceLastRawCheck = rTicks - hRem;

                //Advance counters
                tOff += hRem;
                hRem = CycleLength;

                //Tick next cycle
                if(NextCycle?.AdvanceTicks(1) > 0) {
                    nextHitCount++;
                    hRem += BigRational.Sign(ResidualDrift);
                }
            }

            //Update offset and target value
            CurCycleOffset += ticks;
            long numWraps = CurCycleOffset / CycleLength;
            CurCycleOffset -= numWraps*CycleLength;
            CurCycleTarget -= numWraps*CycleLength;

            //Apply drift to target
            CurCycleTarget += hitCount*CycleLength + nextHitCount*BigRational.Sign(ResidualDrift);
            Assert.AssertTrue(CurCycleOffset < CurCycleTarget && CurCycleTarget - CurCycleOffset <= CycleLength+1);

            //Update check results
            UpdateCheckResults();

            CurTickCount += ticks;
            return hitCount;
        }

        private void UpdateCheckResults() {
            //Update raw check result
            PrevRawCheckResult = TicksSinceLastRawCheck == 1;
            CurRawCheckResult = TicksSinceLastRawCheck == 0;
            NextRawCheckResult = TicksTillNextRawCheck == 1;

            //Update range check results
            long lenA = BaseLength, lenB = BaseLength;
            if(NextCycle != null) {
                if(BigRational.Sign(ResidualDrift) == BigRational.Sign(Delta))  {
                    if(NextCycle.CurRangeCheckResult) lenA++;
                    if(NextCycle.NextRangeCheckResult) lenB++;
                } else {
                    if(NextCycle.PrevRangeCheckResult) lenA++;
                    if(NextCycle.CurRangeCheckResult) lenB++;
                }
            }

            //These edge cases are there to deal with how and when we are ticking the next cycle
            //Proving that they are correct for all possible inputs is left as an exercise to the reader
            PrevRangeCheckResult = CurRangeCheckResult; //AdvanceTicks takes care of ensuring that the last tick is split off from the main block
            CurRangeCheckResult = ((Delta > 0 || CurRawCheckResult) ? (TicksSinceLastRawCheck < lenA) : (TicksTillNextRawCheck < lenB));
            NextRangeCheckResult = ((Delta > 0 && !NextRawCheckResult) ? (TicksSinceLastRawCheck+1 < lenA) : (TicksTillNextRawCheck-1 < lenB));
        }

        public RecursiveCycle? PrevCycle => (CycleIndex > 0) ? Predictor.RecursiveCycles[CycleIndex-1] : null;
        public RecursiveCycle? NextCycle => (CycleIndex < Predictor.RecursiveCycles.Length-1) ? Predictor.RecursiveCycles[CycleIndex+1] : null;

        public long CurTickCount { get; private set; }
        public long CurCycleOffset { get; private set; }
        public long CurCycleTarget { get; private set; }

        public bool PrevRawCheckResult { get; private set; }
        public bool CurRawCheckResult { get; private set; }
        public bool NextRawCheckResult { get; private set; }

        public long TicksSinceLastRawCheck { get; private set; }
        public long TicksTillNextRawCheck => CurCycleTarget - CurCycleOffset;

        public bool PrevRangeCheckResult { get; private set; }
        public bool CurRangeCheckResult { get; private set; }
        public bool NextRangeCheckResult { get; private set; }
    }

    public readonly BigRational Offset, Interval, EffectiveDT;
    public readonly RecursiveCycle[] RecursiveCycles;
    private long curFrame;
    private long[] tickCnts;

    public IntervalCheckCycleDTRangePredictor(float off, float intv, float effDt) : this(new BigRational(off), new BigRational(intv), new BigRational(effDt)) {}
    public IntervalCheckCycleDTRangePredictor(BigRational off, BigRational intv, BigRational effDt) {
        if(intv <= 0) throw new ArgumentOutOfRangeException(nameof(intv));
        if(effDt < 0) throw new ArgumentOutOfRangeException(nameof(effDt));

        Offset = off;
        Interval = intv;
        EffectiveDT = effDt;

        //Initialize recursive cycles
        List<RecursiveCycle> cycles = new List<RecursiveCycle>();

        BigRational cOff = off, cIntv = intv, cDelta = effDt, cThresh = new BigRational(Constants.DeltaTime);
        while(cDelta != 0) {
            cycles.Add(new RecursiveCycle(this, cycles.Count, cOff, cIntv, cDelta, cThresh));
            cThresh = RecursiveCycleCalculator.CalcLengthOffset(cThresh, cDelta);
            RecursiveCycleCalculator.DescendRecursionLevels(ref cIntv, ref cDelta, ref cOff, 1);
        }
        
        RecursiveCycles = cycles.ToArray();
        tickCnts = new long[RecursiveCycles.Length];

        //Initial reset
        Reset();
    }

    public void Reset() {
        if(RecursiveCycles.Length > 0) RecursiveCycles[0].Reset();
        curFrame = 0;
    }

    public void AdvanceFrames(long frames) {
        if(frames < 0) throw new ArgumentOutOfRangeException(nameof(frames));
        if(frames == 0 || RecursiveCycles.Length <= 0) return;
        RecursiveCycles[0].AdvanceTicks(frames);
        curFrame += frames;
    }

    public long CurrentFrame {
        get => curFrame;
        set {
            if(curFrame > value) Reset();
            if(value != curFrame) AdvanceFrames(value - curFrame);
        }
    }

    public bool CheckResult => RecursiveCycles.Length > 0 && RecursiveCycles[0].CurRangeCheckResult;
}