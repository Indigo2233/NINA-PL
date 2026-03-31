using System.Collections.ObjectModel;
using System.ComponentModel.Composition.Hosting;
using System.Reflection;

namespace NINA.PL.Plugin;

public sealed class PluginLoader : IDisposable
{
    private readonly List<IPlugin> _loadedPlugins = [];
    private CompositionContainer? _container;
    private bool _disposed;

    public IReadOnlyList<IPlugin> LoadedPlugins { get; private set; } =
        new ReadOnlyCollection<IPlugin>(Array.Empty<IPlugin>());

    public void LoadPlugins(string pluginDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginDirectory);

        TeardownAll();
        _container?.Dispose();
        _container = null;
        _loadedPlugins.Clear();

        var fullPath = Path.GetFullPath(pluginDirectory);
        if (!Directory.Exists(fullPath))
        {
            Directory.CreateDirectory(fullPath);
        }

        var catalog = new AggregateCatalog();

        foreach (var dllPath in Directory.EnumerateFiles(fullPath, "*.dll", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var asmCat = new AssemblyCatalog(Assembly.LoadFrom(dllPath));
                catalog.Catalogs.Add(asmCat);
            }
            catch (BadImageFormatException)
            {
                // Skip native or non-.NET assemblies
            }
            catch (FileLoadException)
            {
                // Skip assemblies that fail to load
            }
        }

        _container = new CompositionContainer(catalog, isThreadSafe: true);

        foreach (var plugin in _container.GetExportedValues<IPlugin>())
        {
            _loadedPlugins.Add(plugin);
        }

        LoadedPlugins = new ReadOnlyCollection<IPlugin>(_loadedPlugins);
    }

    public void InitializeAll()
    {
        foreach (var plugin in _loadedPlugins)
        {
            plugin.Initialize();
        }
    }

    public void TeardownAll()
    {
        foreach (var plugin in _loadedPlugins)
        {
            try
            {
                plugin.Teardown();
            }
            catch
            {
                // Best-effort teardown
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        TeardownAll();
        _container?.Dispose();
        _disposed = true;
    }
}
