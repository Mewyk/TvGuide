using System.Text.Json;
using NetCord.Services.ApplicationCommands;

namespace TvGuide.Attributes;

/// <summary>
/// Provides access to command attribute variables loaded from <c>AttributeVariables.json</c>.
/// </summary>
/// <remarks>
/// The configuration is loaded lazily on first access. Missing files, unreadable content, and
/// deserialization failures all fall back to an empty variable map.
/// </remarks>
public static class AttributeConfiguration
{
    private static readonly Lazy<Dictionary<string, CommandAttributeVariable>> _variables = new(() =>
    {
        var jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AttributeVariables.json");
        if (!File.Exists(jsonPath))
            return [];

        try
        {
            var jsonContent = File.ReadAllText(jsonPath);
            return JsonSerializer.Deserialize<Dictionary<string, CommandAttributeVariable>>(jsonContent) ?? [];
        }
        catch
        {
            return [];
        }
    });

    /// <summary>
    /// Gets the configured attribute variable identified by <paramref name="key"/>.
    /// </summary>
    /// <param name="key">The configuration key to look up.</param>
    /// <returns>
    /// The matching <see cref="CommandAttributeVariable"/> when the key exists; otherwise,
    /// <see langword="null"/>.
    /// </returns>
    public static CommandAttributeVariable? GetVariable(string key) =>
        _variables.Value.TryGetValue(key, out var variable) ? variable : null;
}

/// <summary>
/// Applies a sub slash command name and description resolved from <see cref="AttributeConfiguration"/>.
/// </summary>
/// <remarks>
/// When the configured variable cannot be found, the command name falls back to the supplied key
/// and the description falls back to an empty string.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class DynamicSubSlashCommandAttribute(
    string variableKey) 
    : SubSlashCommandAttribute(
        GetName(variableKey), 
        GetDescription(variableKey))
{
    private static string GetName(string key)
        => AttributeConfiguration.GetVariable(key)?.Name
        ?? key;
    
    private static string GetDescription(string key)
        => AttributeConfiguration.GetVariable(key)?.Description
        ?? string.Empty;
}

/// <summary>
/// Applies a slash command name and description resolved from <see cref="AttributeConfiguration"/>.
/// </summary>
/// <remarks>
/// When the configured variable cannot be found, the command name falls back to the supplied key
/// and the description falls back to an empty string.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class DynamicSlashCommandAttribute(
    string variableKey) 
    : SlashCommandAttribute(
        GetName(variableKey),
        GetDescription(variableKey))
{
    private static string GetName(string key)
        => AttributeConfiguration.GetVariable(key)?.Name
        ?? key;

    private static string GetDescription(string key)
        => AttributeConfiguration.GetVariable(key)?.Description
        ?? string.Empty;
}

/// <summary>
/// Applies slash command parameter metadata resolved from <see cref="AttributeConfiguration"/>.
/// </summary>
/// <remarks>
/// When a configured variable is found, this attribute copies the configured name, description,
/// numeric bounds, and length bounds onto the underlying <see cref="SlashCommandParameterAttribute"/>.
/// If no variable is found, the inherited default values remain unchanged.
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class DynamicSlashCommandParameterAttribute : SlashCommandParameterAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DynamicSlashCommandParameterAttribute"/> class.
    /// </summary>
    /// <param name="variableKey">The configuration key for the parameter metadata to load.</param>
    public DynamicSlashCommandParameterAttribute(string variableKey)
    {
        var variable = AttributeConfiguration.GetVariable(variableKey);
        if (variable is not null)
        {
            Name = variable.Name;
            Description = variable.Description;
            if (variable.MaxValue.HasValue)
                MaxValue = variable.MaxValue.Value;
            if (variable.MinValue.HasValue)
                MinValue = variable.MinValue.Value;
            if (variable.MaxLength.HasValue)
                MaxLength = variable.MaxLength.Value;
            if (variable.MinLength.HasValue)
                MinLength = variable.MinLength.Value;
        }
    }
}

/// <summary>
/// Represents the configurable values used to populate dynamic command attributes.
/// </summary>
public sealed record CommandAttributeVariable
{
    /// <summary>
    /// Gets the command or parameter name to apply.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the description text to apply.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Gets the optional maximum numeric value allowed for the parameter.
    /// </summary>
    public double? MaxValue { get; init; }

    /// <summary>
    /// Gets the optional minimum numeric value allowed for the parameter.
    /// </summary>
    public double? MinValue { get; init; }

    /// <summary>
    /// Gets the optional maximum string length allowed for the parameter.
    /// </summary>
    public int? MaxLength { get; init; }

    /// <summary>
    /// Gets the optional minimum string length allowed for the parameter.
    /// </summary>
    public int? MinLength { get; init; }
}
