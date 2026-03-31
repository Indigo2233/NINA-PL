namespace NINA.PL.Plugin;

public interface IPlugin : IPluginManifest
{
    void Initialize();

    void Teardown();
}
