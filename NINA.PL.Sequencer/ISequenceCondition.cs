namespace NINA.PL.Sequencer;

public interface ISequenceCondition
{
    string Name { get; }
    string Category { get; }
    bool Check(SequenceContext context);
}
