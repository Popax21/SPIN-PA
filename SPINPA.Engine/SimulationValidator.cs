using System;
using System.Collections.Generic;

namespace SPINPA.Engine;

public class SimulationValidator : Validator {
    private readonly Action<string>? logCB;

    public SimulationValidator(Action<string>? logCB = null) => this.logCB = logCB;

    protected override IEnumerable<float> EnumerateTimeActiveValues() {
        for(float timeActive = Constants.DeltaTime;; timeActive += Constants.DeltaTime) yield return timeActive;
    }

    protected override void Log(string msg) => logCB?.Invoke(msg);
}