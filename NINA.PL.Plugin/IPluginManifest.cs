namespace NINA.PL.Plugin;

public interface IPluginManifest
{
    string Id { get; }

    string Name { get; }

    string Version { get; }

    string Author { get; }

    string Description { get; }
}
