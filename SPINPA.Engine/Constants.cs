using System;

namespace SPINPA.Engine;

public static class Constants {
    public const float SpinnerInterval = 0.05f;
    public static readonly float DeltaTime = (float) TimeSpan.FromTicks(166667).TotalSeconds;
}