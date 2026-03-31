namespace NINA.PL.Equipment.Camera;

/// <summary>
/// Registers and constructs native camera backends (SDK DLLs). Add <see cref="Register"/> calls from composition root or module init.
/// </summary>
public static class NativeBackendFactory
{
    private static readonly List<Func<INativeCameraBackend>> _registrations = new();

    public static void Register(Func<INativeCameraBackend> factory) => _registrations.Add(factory);

    /// <summary>
    /// Instantiates one backend per registered factory; failures are skipped.
    /// </summary>
    public static List<INativeCameraBackend> CreateAllBackends()
    {
        var list = new List<INativeCameraBackend>();
        foreach (var factory in _registrations)
        {
            try
            {
                var backend = factory();
                if (backend is not null)
                    list.Add(backend);
            }
            catch
            {
                // Ignore backends that fail construction (missing DLL, wrong platform, etc.).
            }
        }

        return list;
    }
}
