using DotBot.Configuration.Contracts;

namespace DotBot.Configuration.Core;

/// <summary>
/// Default implementation of module configuration provider.
/// Manages configuration binders and provides access to module configurations.
/// </summary>
public sealed class ModuleConfigProvider : IModuleConfigProvider
{
    private readonly AppConfig _appConfig;
    private readonly Dictionary<Type, object> _configs = new();
    private readonly Dictionary<Type, object> _binders = new();
    private readonly Dictionary<string, Type> _sectionTypes = new();

    /// <summary>
    /// Creates a new module configuration provider.
    /// </summary>
    /// <param name="appConfig">The application configuration.</param>
    public ModuleConfigProvider(AppConfig appConfig)
    {
        _appConfig = appConfig;
    }

    /// <summary>
    /// Registers a configuration binder.
    /// </summary>
    /// <typeparam name="TConfig">The configuration type.</typeparam>
    /// <typeparam name="TBinder">The binder type.</typeparam>
    public void RegisterBinder<TConfig, TBinder>()
        where TConfig : class, new()
        where TBinder : IModuleConfigBinder<TConfig>, new()
    {
        var binder = new TBinder();
        _binders[typeof(TConfig)] = binder;
        _sectionTypes[binder.SectionName] = typeof(TConfig);

        // Bind immediately
        var config = binder.Bind(_appConfig);
        _configs[typeof(TConfig)] = config;
    }

    /// <summary>
    /// Registers a configuration binder instance.
    /// </summary>
    /// <typeparam name="TConfig">The configuration type.</typeparam>
    /// <param name="binder">The binder instance.</param>
    public void RegisterBinder<TConfig>(IModuleConfigBinder<TConfig> binder) where TConfig : class, new()
    {
        _binders[typeof(TConfig)] = binder;
        _sectionTypes[binder.SectionName] = typeof(TConfig);

        // Bind immediately
        var config = binder.Bind(_appConfig);
        _configs[typeof(TConfig)] = config;
    }

    /// <inheritdoc />
    public TConfig? GetConfig<TConfig>() where TConfig : class
    {
        if (_configs.TryGetValue(typeof(TConfig), out var config))
        {
            return config as TConfig;
        }
        return null;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetRegisteredSections()
    {
        return _sectionTypes.Keys.ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, IReadOnlyList<string>> ValidateAll()
    {
        var result = new Dictionary<string, IReadOnlyList<string>>();

        foreach (var (sectionName, configType) in _sectionTypes)
        {
            if (_binders.TryGetValue(configType, out var binderObj) && 
                _configs.TryGetValue(configType, out var configObj))
            {
                // Use reflection to call Validate method
                var validateMethod = binderObj.GetType().GetMethod("Validate");
                if (validateMethod != null)
                {
                    if (validateMethod.Invoke(binderObj, [configObj]) is IReadOnlyList<string> errors && errors.Count > 0)
                    {
                        result[sectionName] = errors;
                    }
                }
            }
        }

        return result;
    }
}
