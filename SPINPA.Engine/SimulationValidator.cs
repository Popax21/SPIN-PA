using System;
using System.Collections.Generic;

namespace SPINPA.Engine;

public class SimulationValidator : Validator {
    protected override IEnumerable<float> EnumerateTimeActiveValues() {
        for(float timeActive = Constants.DeltaTime;; timeActive += Constants.DeltaTime) yield return timeActive;
    }

    protected override bool DoOnIntervalCheck(float timeActive, float interval, float offset) {
        return Math.Floor(((double) timeActive - offset - Constants.DeltaTime) / interval) < Math.Floor(((double) timeActive - offset) / interval);
    }
}