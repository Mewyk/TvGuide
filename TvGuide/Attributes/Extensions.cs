using System.Text.Json;

using NetCord.Services.ApplicationCommands;

namespace TvGuide.Attributes;

public static class AttributeConfiguration
{
    private static readonly Dictionary<string, CommandAttributeVariable> _variables;

    static AttributeConfiguration()
    {
        var jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AttributeVariables.json");
        _variables = File.Exists(jsonPath)
            ? JsonSerializer
                .Deserialize<Dictionary<string, CommandAttributeVariable>>(
                    File.ReadAllText(jsonPath))
                ?? []
            : [];
    }

    public static CommandAttributeVariable? GetVariable(string key) =>
        _variables.TryGetValue(key, out var variable) ? variable : null;
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class DynamicSubSlashCommandAttribute(
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

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class DynamicSlashCommandAttribute(
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

[AttributeUsage(AttributeTargets.Parameter)]
public class DynamicSlashCommandParameterAttribute : SlashCommandParameterAttribute
{
    public DynamicSlashCommandParameterAttribute(string variableKey)
    {
        var variable = AttributeConfiguration.GetVariable(variableKey);
        if (variable != null)
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

public class CommandAttributeVariable
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public double? MaxValue { get; set; }
    public double? MinValue { get; set; }
    public int? MaxLength { get; set; }
    public int? MinLength { get; set; }
}
