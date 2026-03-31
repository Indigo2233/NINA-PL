using System.Diagnostics;
using NINA.PL.Core;

namespace NINA.PL.Sequencer.Instructions;

public sealed class ExternalScriptInstruction : ISequenceItem
{
    public string Name { get; set; } = nameof(ExternalScriptInstruction);

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = "Utility";

    public InstructionErrorBehavior ErrorBehavior { get; set; } = InstructionErrorBehavior.ContinueOnError;

    public int Attempts { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    public string FilePath { get; set; } = string.Empty;

    public string Arguments { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 60;

    public async Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(FilePath))
            throw new InvalidOperationException($"{nameof(FilePath)} is required.");

        var psi = new ProcessStartInfo
        {
            FileName = FilePath,
            Arguments = Arguments,
            UseShellExecute = false,
        };

        using var process = new Process { StartInfo = psi };
        if (!process.Start())
            throw new InvalidOperationException($"Failed to start process: {FilePath}");

        Logger.Info("Started external script: {0} {1}", FilePath, Arguments);

        using var reg = ct.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Failed to kill external script process.");
            }
        });

        await Task.Run(() =>
        {
            if (!process.WaitForExit(TimeSpan.FromSeconds(TimeoutSeconds)))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "Killing timed-out external script failed.");
                }
                throw new TimeoutException($"External script exceeded {TimeoutSeconds}s: {FilePath}");
            }
        }, ct).ConfigureAwait(false);

        Logger.Info("External script exited with code {0}.", process.ExitCode);
    }

    public TimeSpan GetEstimatedDuration() => TimeSpan.FromSeconds(Math.Max(1, TimeoutSeconds));
}
