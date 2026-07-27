using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;

namespace WordTemplateBinding.Infrastructure.OpenXml.Data;

/// <summary>
/// 基于 System.Text.Json 的数据上下文解析器。
/// 支持 JsonElement、Dictionary 和 DTO 公开属性。
/// </summary>
public sealed class JsonDataContextResolver : IDataContextResolver
{
    /// <inheritdoc />
    public object? ResolveValue(RenderScope scope, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string normalized = path.Trim();
        string[] segments = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return null;
        }

        // Handle $parent — walk up the scope chain
        RenderScope currentScope = scope;
        int segmentIndex = 0;

        while (segmentIndex < segments.Length &&
               segments[segmentIndex].StartsWith("$parent", StringComparison.Ordinal))
        {
            currentScope = currentScope.Parent
                ?? throw new InvalidOperationException(
                    $"路径 \"{path}\" 引用了 $parent，但当前作用域没有父级。");
            segmentIndex++;
        }

        if (segmentIndex >= segments.Length)
        {
            // Path was only $parent references — return the scope itself
            return currentScope;
        }

        // Handle special variables
        string firstSegment = segments[segmentIndex];
        if (firstSegment == "$index")
        {
            return currentScope.Index;
        }

        if (firstSegment == "$key")
        {
            return currentScope.InstanceKey;
        }

        // Look up the first segment in the scope variables
        if (!currentScope.Variables.TryGetValue(firstSegment, out object? current))
        {
            return null;
        }

        segmentIndex++;

        // Walk remaining segments
        for (; segmentIndex < segments.Length; segmentIndex++)
        {
            current = ResolveProperty(current, segments[segmentIndex]);
            if (current is null)
            {
                return null;
            }
        }

        return current;
    }

    /// <inheritdoc />
    public IReadOnlyList<object?>? ResolveArray(RenderScope scope, string sourcePath)
    {
        object? resolved = ResolveValue(scope, sourcePath);
        if (resolved is null)
        {
            return null;
        }

        return ConvertToArray(resolved);
    }

    /// <inheritdoc />
    public string? ResolveItemKey(object? item, string itemKeyPath)
    {
        if (item is null)
        {
            return null;
        }

        string[] segments = itemKeyPath.Split('.', StringSplitOptions.RemoveEmptyEntries);
        object? current = item;
        foreach (string segment in segments)
        {
            current = ResolveProperty(current, segment);
            if (current is null)
            {
                return null;
            }
        }

        return ConvertToStableString(current);
    }

    /// <summary>
    /// 在给定对象上解析属性。
    /// </summary>
    private static object? ResolveProperty(object? target, string propertyName)
    {
        if (target is null)
        {
            return null;
        }

        return target switch
        {
            JsonElement jsonElement => ResolveJsonElementProperty(jsonElement, propertyName),
            IReadOnlyDictionary<string, object?> dict => ResolveDictionaryProperty(dict, propertyName),
            IDictionary<string, object?> mutableDict => ResolveDictionaryProperty(
                new Dictionary<string, object?>(mutableDict), propertyName),
            RenderScope scope => ResolveRenderScopeProperty(scope, propertyName),
            _ => ResolveDtoProperty(target, propertyName),
        };
    }

    /// <summary>
    /// 在 JsonElement 上解析属性或数组索引。
    /// </summary>
    private static object? ResolveJsonElementProperty(JsonElement element, string propertyName)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object =>
                element.TryGetProperty(propertyName, out JsonElement property)
                    ? CloneJsonElement(property)
                    : null,

            JsonValueKind.Array =>
                int.TryParse(propertyName, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)
                    && index >= 0 && index < element.GetArrayLength()
                        ? CloneJsonElement(element[index])
                        : null,

            _ => null,
        };
    }

    /// <summary>
    /// 在字典上解析属性。
    /// </summary>
    private static object? ResolveDictionaryProperty(
        IReadOnlyDictionary<string, object?> dictionary,
        string propertyName) =>
        dictionary.TryGetValue(propertyName, out object? value) ? value : null;

    /// <summary>
    /// 在 RenderScope 上解析属性（用于 $parent.school 这种情况）。
    /// </summary>
    private static object? ResolveRenderScopeProperty(RenderScope scope, string propertyName) =>
        scope.Variables.TryGetValue(propertyName, out object? value) ? value : null;

    /// <summary>
    /// 在 DTO 公开实例属性上解析。
    /// </summary>
    private static object? ResolveDtoProperty(object target, string propertyName)
    {
        Type type = target.GetType();
        PropertyInfo? property = type.GetProperty(
            propertyName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (property is null)
        {
            return null;
        }

        try
        {
            return property.GetValue(target);
        }
        catch (TargetInvocationException)
        {
            return null;
        }
    }

    /// <summary>
    /// 将任意对象安全转换为数组。
    /// </summary>
    private static IReadOnlyList<object?>? ConvertToArray(object? value)
    {
        return value switch
        {
            null => null,
            JsonElement jsonElement => ConvertJsonElementToArray(jsonElement),
            IReadOnlyList<object?> list => list,
            IList<object?> mutableList => mutableList.ToList().AsReadOnly(),
            IEnumerable enumerable => enumerable.Cast<object?>().ToList().AsReadOnly(),
            _ => null,
        };
    }

    /// <summary>
    /// 将 JsonElement 安全转换为数组。
    /// </summary>
    private static IReadOnlyList<object?>? ConvertJsonElementToArray(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        List<object?> items = new(element.GetArrayLength());
        foreach (JsonElement item in element.EnumerateArray())
        {
            items.Add(CloneJsonElement(item));
        }

        return items.AsReadOnly();
    }

    /// <summary>
    /// 将 JsonElement 值类型克隆为对应的 .NET 对象。
    /// </summary>
    private static object? CloneJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out long l)
                ? l
                : element.GetDecimal(),
            JsonValueKind.Object => element.Clone(),
            JsonValueKind.Array => element.Clone(),
            _ => element.Clone(),
        };
    }

    /// <summary>
    /// 将解析值转换为稳定字符串表示。
    /// </summary>
    private static string? ConvertToStableString(object? value)
    {
        return value switch
        {
            null => null,
            string s => s,
            JsonElement jsonElement => jsonElement.ValueKind switch
            {
                JsonValueKind.String => jsonElement.GetString(),
                JsonValueKind.Number => jsonElement.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null,
            },
            _ => Convert.ToString(value, CultureInfo.InvariantCulture),
        };
    }
}
