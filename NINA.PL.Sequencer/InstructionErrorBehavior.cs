namespace NINA.PL.Sequencer;

/// <summary>How the sequencer handles errors when executing an instruction.</summary>
public enum InstructionErrorBehavior
{
    SkipInstruction,
    AbortOnError,
    ContinueOnError,
}
