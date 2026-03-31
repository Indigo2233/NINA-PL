using NINA.PL.Core;

namespace NINA.PL.Sequencer.Conditions;

public sealed class SafetyCondition : ISequenceCondition
{
    public string Name { get; set; } = nameof(SafetyCondition);

    public string Category { get; set; } = "Loop";

    /// <summary>If true, condition passes when mount is tracking and camera is connected.</summary>
    public bool RequireTracking { get; set; } = true;

    public bool RequireCameraConnected { get; set; } = true;

    public bool Check(SequenceContext context)
    {
        if (RequireTracking)
        {
            IMountProvider? mount = context.Mount.GetConnectedProvider();
            if (mount is null || !mount.IsTracking)
                return false;
        }
        if (RequireCameraConnected)
        {
            if (!context.Camera.IsConnected)
                return false;
        }
        return true;
    }
}
