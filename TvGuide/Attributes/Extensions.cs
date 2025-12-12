using System.Text.Json;
using NetCord.Services.ApplicationCommands;

namespace TvGuide.Attributes;

/// <summary>
/// Command attribute variables loaded from configuration.
/// </summary>
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
    /// Gets attribute variable by key, or null if not found.
    /// </summary>
    public static CommandAttributeVariable? GetVariable(string key) =>
        _variables.Value.TryGetValue(key, out var variable) ? variable : null;
}

/// <summary>
/// Dynamic sub slash command with name and description from configuration.
/// </summary>
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
/// Dynamic slash command with name and description from configuration.
/// </summary>
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
/// Dynamic slash command parameter with properties from configuration.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class DynamicSlashCommandParameterAttribute : SlashCommandParameterAttribute
{
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
/// Configuration values for dynamic command attributes.
/// </summary>
public sealed record CommandAttributeVariable
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public double? MaxValue { get; init; }
    public double? MinValue { get; init; }
    public int? MaxLength { get; init; }
    public int? MinLength { get; init; }
}
