using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace SPINPA.Engine;

public sealed class IntervalCheckPredictor {
    public readonly record struct DTRange(long StartFrame, long EndFrame, IntervalCheckDTRangePredictor DTRangePredictor) {
        public readonly long NumFrames = (EndFrame < long.MaxValue) ? (EndFrame - StartFrame) : long.MaxValue;
    }

    public readonly BigRational Offset, Interval;
    public readonly DTRange[] Ranges;
    private int curRangeIdx = 0;

    public IntervalCheckPredictor(float off, float intv) : this(new BigRational(off), new BigRational(intv)) {}
    public IntervalCheckPredictor(BigRational off, BigRational intv) {
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
                    ranges.Add(new DTRange(rStart, rEnd, new IntervalCheckDTRangePredictor(rOff, intv, rDt)));
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
        if(!BigRational.IsNaN(rDt)) ranges.Add(new DTRange(rStart, rEnd, new IntervalCheckDTRangePredictor(rOff, intv, rDt)));

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
    public IntervalCheckDTRangePredictor CurrentDTRangePredictor => CurrentRange.DTRangePredictor;

    public bool CheckResult => CurrentRange.DTRangePredictor.CheckResult;
}

public sealed class IntervalCheckDTRangePredictor {
    public sealed class RecursiveCycle {
        public readonly IntervalCheckDTRangePredictor Predictor;
        public readonly int CycleIndex;

        public readonly BigRational Offset, Interval, Delta, ResidualDrift;
        public readonly long InitialCycleTarget, CycleLength;

        public readonly BigRational Threshold, LengthOffset;
        public readonly int BaseLength;

        internal RecursiveCycle(IntervalCheckDTRangePredictor pred, int idx, BigRational off, BigRational intv, BigRational delta, BigRational thresh) {
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
 
#region Public Helpers
        [ThreadStatic] private static long[]? _CalcRemainingTicks;
        public long CalcNumTargetHits(long ticks) {
            if(ticks == 0) return 0;

            [MethodImpl(MethodImplOptions.AggressiveOptimization)]
            static long SimulateAdvance(long[] cycleRemTicks, int idx, RecursiveCycle cycle, long ticks) {
                long remTicks = cycleRemTicks[idx];

                //Initialize remaining ticks
                if(remTicks == long.MaxValue) {
                    if(ticks > 0) {
                        remTicks = cycle.CycleLength - cycle._TicksTillNextRawCheck;
                    } else {
                        remTicks = cycle.CycleLength - (cycle._TicksSinceLastRawCheck+1);

                        //The next cycle's current result corresponds to the *next* range check
                        //As we are advancing through the cycle backwards, our actual next range check is actually the last one, so we need to rewind the next cycle by one tick
                        if(cycle.NextCycle != null) SimulateAdvance(cycleRemTicks, idx+1, cycle.NextCycle, -1);
                    }
                }

                //Count number of target hits within the given number of ticks
                remTicks += long.Abs(ticks);

                long hitCount = 0;
                while(true) {
                    //Calculate a conservative estimate of how often we can hit the target
                    long hc = remTicks / cycle.CycleLength;
                    if(hc <= 0) break;
                    if(cycle.ResidualDrift > 0) hc = (remTicks - (hc-1)) / cycle.CycleLength;

                    //Register these 100% confirmed hits
                    hitCount += hc;
                    remTicks -= hc*cycle.CycleLength;

                    //Apply drift to the number of remaining ticks
                    if(cycle.NextCycle != null) {
                        remTicks -= BigRational.Sign(cycle.ResidualDrift) * SimulateAdvance(cycleRemTicks, idx+1, cycle.NextCycle, hc);
                    }
                }

                cycleRemTicks[idx] = remTicks;
                return hitCount;
            }

            _CalcRemainingTicks ??= new long[64];
            Array.Fill(_CalcRemainingTicks, long.MaxValue);
            return SimulateAdvance(_CalcRemainingTicks, 0, this, ticks);
        }

        public long CalcDrift(long targetHits) {
            if(NextCycle == null || targetHits == 0) return 0;
            if(targetHits > 0) return BigRational.Sign(ResidualDrift) * NextCycle.CalcNumTargetHits(targetHits);

            //We need to cancel out that the current raw check result of the next cycle corresponds to if the *next* raw check of this cycle did group drift
            //So we need one additional retreat (whose eventual target hit we have to cancel out) to arrive at the correct result
            long numHits = NextCycle.CalcNumTargetHits(-(-targetHits + 1));
            if(NextCycle._TicksSinceLastRawCheck == 0) numHits--;
            return BigRational.Sign(ResidualDrift) * numHits;
        }

        public long CalcTicksFromPropagatedTicks(long propTicks) {
            if(propTicks > 0) return _TicksTillNextRawCheck + CycleLength * (propTicks - 1) + CalcDrift(propTicks - 1);
            if(propTicks < 0) return -(_TicksSinceLastRawCheck + CycleLength * (-propTicks - 1) + CalcDrift(-(-propTicks - 1)) + 1);
            return 0;
        }
#endregion

#region State Update
        internal void Reset() {
            //Reset next recursive cycle
            NextCycle?.Reset();

            //Reset variables to tick -1
            _CurTickCount = -1;
            _CurCycleOffset = CycleLength-1;
            _CurCycleTarget = CycleLength + InitialCycleTarget;

            _TicksSinceLastRawCheck = _TicksTillNextRawCheck = -1;
            _PrevRawCheckResult = false;
            _PrevRangeCheckResult = _CurRangeCheckResult = _NextRangeCheckResult = false;

            _LastCheckRangeDidLengthDrift = _NextCheckRangeDidLengthDrift = false;

            UpdateCheckResults();

            //Advance to tick 0
            AdvanceTicks(1);

            Assert.AssertTrue(_TicksSinceLastRawCheck >= 0);
            Assert.AssertTrue(_TicksTillNextRawCheck > 0);
        }

        internal long AdvanceTicks(long ticks, bool willUpdatePrev = false) {
            if(ticks < 0) throw new ArgumentOutOfRangeException(nameof(ticks));
            if(ticks == 0) return 0;

            //Split off the last tick from 2+ tick advances to ensure PrevXYZCheckResult is updated correctly
            if(ticks >= 2 && !willUpdatePrev) return AdvanceTicks(ticks-1, willUpdatePrev: true) + AdvanceTicks(1, willUpdatePrev: true);

            //Determine number of target hits
            long hitCount = 0, nextHitCount = 0;
            for(long remTicks = (CycleLength - _TicksTillNextRawCheck) + ticks;;) {
                //Calculate a conservative estimate of how often we can hit the target
                long hc = remTicks / CycleLength;
                if(hc <= 0) break;
                if(ResidualDrift > 0) hc = (remTicks - (hc-1)) / CycleLength;

                //Register these 100% confirmed hits
                hitCount += hc;
                remTicks -= hc*CycleLength;

                //Advance the next recursive cycle, and apply drift to the number of remaining ticks
                long nhc = NextCycle?.AdvanceTicks(hc) ?? 0;
                nextHitCount += nhc;
                remTicks -= BigRational.Sign(ResidualDrift) * nhc;
            }

            //Update offset and handle wrap around
            _CurCycleOffset += ticks;
            long numWraps = _CurCycleOffset / CycleLength;
            _CurCycleOffset -= numWraps*CycleLength;
            _CurCycleTarget -= numWraps*CycleLength;

            //Update target value, take drift into account
            _CurCycleTarget += hitCount*CycleLength + nextHitCount*BigRational.Sign(ResidualDrift);
            Assert.AssertTrue(_CurCycleOffset < _CurCycleTarget && _CurCycleTarget - _CurCycleOffset <= CycleLength+1);

            //Update check results
            UpdateCheckResults();

            _CurTickCount += ticks;
            return hitCount;
        }
#endregion

#region Results Calculation
        private void UpdateCheckResults() {
            //Update previous results
            _PrevRawCheckResult = CurRawCheckResult;
            _PrevRangeCheckResult = _CurRangeCheckResult;
            _PrevTicksSinceLastRangeCheck = _TicksSinceLastRangeCheck; //Needed for TicksSinceLastRangeCheck when BaseLength == 0

            //Update raw check results
            Assert.AssertTrue(_CurCycleTarget > _CurCycleOffset);
            _TicksSinceLastRawCheck = _CurCycleOffset - (_CurCycleTarget - ((NextCycle?.CurRawCheckResult ?? false) ? BigRational.Sign(ResidualDrift) : 0) - CycleLength);
            _TicksTillNextRawCheck = _CurCycleTarget - _CurCycleOffset;

            //Determine check range lengths
            long lenA = BaseLength, lenB = BaseLength;
            bool lenADidDrift = false, lenBDidDrift = false;
            if(NextCycle != null) {
                if(BigRational.Sign(ResidualDrift) == BigRational.Sign(Delta)) {
                    lenADidDrift = NextCycle.CurRangeCheckResult;
                    lenBDidDrift = NextCycle.NextRangeCheckResult;
                } else {
                    lenADidDrift = NextCycle.PrevRangeCheckResult;
                    lenBDidDrift = NextCycle.CurRangeCheckResult;
                }

                if(lenADidDrift) lenA++;
                if(lenBDidDrift) lenB++;
            }

            //Update group drift predictions
            if(NextCycle != null) {
                _TicksTillNextGroupDrift = TicksTillNextRawCheck + CycleLength*NextCycle.TicksTillAnyRawCheck;
                if(!NextRawCheckDidGroupDrift) _TicksTillNextGroupDrift += BigRational.Sign(ResidualDrift);
            } else {
                _TicksTillNextGroupDrift = -1;
            }

            //Update range check results
            //These edge cases are here to deal with how and when we are ticking the next cycle
            //Proving that they are correct for all possible inputs is left as an exercise to the reader
            _CurRangeCheckResult = ((Delta > 0 || CurRawCheckResult) ? (_TicksSinceLastRawCheck < lenA) : (_TicksTillNextRawCheck < lenB));
            _NextRangeCheckResult = ((Delta > 0 && !NextRawCheckResult) ? (_TicksSinceLastRawCheck+1 < lenA) : (_TicksTillNextRawCheck-1 < lenB));

            _LastCheckRangeDidLengthDrift = lenADidDrift;
            _NextCheckRangeDidLengthDrift = lenBDidDrift;

            //Update range check / length drift predictions
            //Calculate some predictions lazy as they can be expensive
            _TicksTillNextLengthDrift = null; //Calculate lazily

            if(BaseLength == 0) {
                _TicksSinceLastRangeCheck = CalcTicksSinceLastLengthDrift();
                _TicksTillNextLengthDrift = _TicksTillNextRangeCheck = CalcTicksTillNextLengthDrift();
            } else if(Delta > 0) {
                _TicksSinceLastRangeCheck = _CurRangeCheckResult ? 0 : (_TicksSinceLastRawCheck - (lenA-1));
                _TicksTillNextRangeCheck = (_TicksSinceLastRawCheck < lenA-1) ? 1 : _TicksTillNextRawCheck;
            } else {
                _TicksSinceLastRangeCheck = _CurRangeCheckResult ? 0 : _TicksSinceLastRawCheck;
                _TicksTillNextRangeCheck = long.Max(1, _TicksTillNextRawCheck - (lenB-1));
            }
        }

        private long CalcTicksSinceLastLengthDrift() {
            if(!HasLengthDrifts) return -1;

            if(BigRational.Sign(ResidualDrift) == BigRational.Sign(Delta)) {
                //-1 as we want to advance to the raw check before the one where the range check result turned false
                return -(CalcTicksFromPropagatedTicks(-NextCycle._TicksSinceLastRangeCheck - 1) + 1);
            } else {
                //-1 because [see above]
                //_PrevTicksSinceLastRangeCheck lags one tick behind, but we also want to advance one more tick so that the *previous* range check result is true, which cancels out
                return -(CalcTicksFromPropagatedTicks(-NextCycle._PrevTicksSinceLastRangeCheck - 1) + 1);
            }
        }

        private long CalcTicksTillNextLengthDrift() {
            if(!HasLengthDrifts) return -1;

            if(BigRational.Sign(ResidualDrift) == BigRational.Sign(Delta)) {
                return CalcTicksFromPropagatedTicks(NextCycle.TicksTillNextRangeCheck);
            } else {
                //+1 as we want the *previous* range check result to be true
                return CalcTicksFromPropagatedTicks(NextCycle.TicksTillAnyRangeCheck + 1);
            }
        }
#endregion

        public RecursiveCycle? PrevCycle => (CycleIndex > 0) ? Predictor.RecursiveCycles[CycleIndex-1] : null;
        public RecursiveCycle? NextCycle => (CycleIndex < Predictor.RecursiveCycles.Length-1) ? Predictor.RecursiveCycles[CycleIndex+1] : null;

#region Cycle state
        private long _CurTickCount, _CurCycleOffset, _CurCycleTarget;
        public long CurTickIndex => _CurTickCount;
        public long CurCycleOffset => _CurCycleOffset;
        public long CurCycleTarget => _CurCycleTarget;
#endregion

#region Raw check results
        private long _TicksSinceLastRawCheck, _TicksTillNextRawCheck;
        public long TicksSinceLastRawCheck => _TicksSinceLastRawCheck;
        public long TicksTillNextRawCheck => _TicksTillNextRawCheck;
        public long TicksTillAnyRawCheck => (_TicksSinceLastRawCheck == 0) ? 0 : _TicksTillNextRawCheck;

        private bool _PrevRawCheckResult; 
        public bool PrevRawCheckResult => _PrevRawCheckResult;
        public bool CurRawCheckResult => _TicksSinceLastRawCheck == 0;
        public bool NextRawCheckResult => TicksTillNextRawCheck == 1;
#endregion

#region Range check results
        public bool HasRangeChecks => BaseLength > 0 || HasLengthDrifts;

        private long _TicksSinceLastRangeCheck, _TicksTillNextRangeCheck, _PrevTicksSinceLastRangeCheck;
        public long TicksSinceLastRangeCheck => _TicksSinceLastRangeCheck;
        public long TicksTillNextRangeCheck => _TicksTillNextRangeCheck;
        public long TicksTillAnyRangeCheck => _CurRangeCheckResult ? 0 : _TicksTillNextRangeCheck;

        private bool _PrevRangeCheckResult, _CurRangeCheckResult, _NextRangeCheckResult;
        public bool PrevRangeCheckResult => _PrevRangeCheckResult;
        public bool CurRangeCheckResult => _CurRangeCheckResult;
        public bool NextRangeCheckResult => _NextRangeCheckResult;
#endregion

#region Group drift indicators
        [MemberNotNullWhen(true, nameof(NextCycle))]
        public bool HasGroupDrifts => NextCycle != null;

        public bool LastRawCheckDidGroupDrift => NextCycle?.PrevRawCheckResult ?? false;
        public bool NextRawCheckDidGroupDrift => NextCycle?.CurRawCheckResult ?? false;

        private long _TicksTillNextGroupDrift;
        public long TicksTillNextGroupDrift => _TicksTillNextGroupDrift;
        public long TicksTillAnyGroupDrift => (_TicksSinceLastRawCheck == 0 && LastRawCheckDidGroupDrift) ? 0 : _TicksTillNextGroupDrift;
#endregion

#region Length drift indicators
        [MemberNotNullWhen(true, nameof(NextCycle))]
        public bool HasLengthDrifts => NextCycle?.HasRangeChecks ?? false;

        private bool _LastCheckRangeDidLengthDrift, _NextCheckRangeDidLengthDrift;
        public bool LastCheckRangeDidLengthDrift => _LastCheckRangeDidLengthDrift;
        public bool NextCheckRangeDidLengthDrift => _NextCheckRangeDidLengthDrift;

        private long? _TicksTillNextLengthDrift;
        public long TicksTillNextLengthDrift => _TicksTillNextLengthDrift ??= CalcTicksTillNextLengthDrift(); //Ticks till the next raw check whose check range's length drifts
        public long TicksTillAnyLengthDrift => (_TicksSinceLastRawCheck == 0 && LastCheckRangeDidLengthDrift) ? 0 : TicksTillNextLengthDrift;
#endregion
    }

    public readonly BigRational Offset, Interval, EffectiveDT;
    public readonly RecursiveCycle[] RecursiveCycles;
    private long curFrame;
    private long[] tickCnts;

    public IntervalCheckDTRangePredictor(float off, float intv, float effDt) : this(new BigRational(off), new BigRational(intv), new BigRational(effDt)) {}
    public IntervalCheckDTRangePredictor(BigRational off, BigRational intv, BigRational effDt) {
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
        if(frames == 0) return;
        if(RecursiveCycles.Length > 0) RecursiveCycles[0].AdvanceTicks(frames);
        curFrame += frames;
    }

    public long CurrentFrame {
        get => curFrame;
        set {
            if(curFrame > value) Reset();
            if(value != curFrame) AdvanceFrames(value - curFrame);
        }
    }

    public bool CheckResult => (RecursiveCycles.Length > 0) ? RecursiveCycles[0].CurRangeCheckResult : (CalcUtils.RMod(-Offset, Interval) < new BigRational(Constants.DeltaTime));
}