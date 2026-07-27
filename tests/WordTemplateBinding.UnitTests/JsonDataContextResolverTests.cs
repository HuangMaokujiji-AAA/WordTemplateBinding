using System.Text.Json;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;
using WordTemplateBinding.Infrastructure.OpenXml.Data;

namespace WordTemplateBinding.UnitTests;

/// <summary>
/// 验证 JSON 数据上下文解析器的路径解析、数组解析和实例键解析。
/// </summary>
public sealed class JsonDataContextResolverTests
{
    private readonly IDataContextResolver _resolver = new JsonDataContextResolver();

    /// <summary>
    /// 创建包含学校和专业数组的根作用域。
    /// </summary>
    private static RenderScope CreateRootScope()
    {
        string json = """
        {
            "school": {
                "schoolId": "1001",
                "schoolName": "示例大学",
                "year": 2026
            },
            "majors": [
                {
                    "majorId": "080901",
                    "majorName": "计算机科学与技术",
                    "employmentRate": 95.2
                },
                {
                    "majorId": "080902",
                    "majorName": "软件工程",
                    "employmentRate": 93.1
                }
            ],
            "year": 2026
        }
        """;

        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement.Clone();

        Dictionary<string, object?> variables = new(StringComparer.Ordinal)
        {
            ["school"] = root.GetProperty("school").Clone(),
            ["majors"] = root.GetProperty("majors").Clone(),
            ["year"] = 2026,
        };

        return RenderScope.CreateRoot(variables);
    }

    /// <summary>根路径值可以正确解析。</summary>
    [Fact]
    public void ResolveValue_RootPath_ReturnsValue()
    {
        RenderScope scope = CreateRootScope();
        object? result = _resolver.ResolveValue(scope, "year");
        Assert.NotNull(result);
        Assert.Equal(2026, Convert.ToInt32(result));
    }

    /// <summary>嵌套路径可以正确解析。</summary>
    [Fact]
    public void ResolveValue_NestedPath_ReturnsNestedValue()
    {
        RenderScope scope = CreateRootScope();
        object? result = _resolver.ResolveValue(scope, "school.schoolName");
        Assert.NotNull(result);
        Assert.Equal("示例大学", result!.ToString());
    }

    /// <summary>不存在的路径返回 null。</summary>
    [Fact]
    public void ResolveValue_NonExistentPath_ReturnsNull()
    {
        RenderScope scope = CreateRootScope();
        object? result = _resolver.ResolveValue(scope, "school.nonExistent");
        Assert.Null(result);
    }

    /// <summary>空路径返回 null。</summary>
    [Fact]
    public void ResolveValue_EmptyPath_ReturnsNull()
    {
        RenderScope scope = CreateRootScope();
        object? result = _resolver.ResolveValue(scope, "");
        Assert.Null(result);
    }

    /// <summary>有效数组路径返回列表。</summary>
    [Fact]
    public void ResolveArray_ValidArrayPath_ReturnsList()
    {
        RenderScope scope = CreateRootScope();
        IReadOnlyList<object?>? result = _resolver.ResolveArray(scope, "majors");
        Assert.NotNull(result);
        Assert.Equal(2, result!.Count);
    }

    /// <summary>非数组路径返回 null。</summary>
    [Fact]
    public void ResolveArray_NonArrayPath_ReturnsNull()
    {
        RenderScope scope = CreateRootScope();
        IReadOnlyList<object?>? result = _resolver.ResolveArray(scope, "school");
        Assert.Null(result);
    }

    /// <summary>不存在的数组路径返回 null。</summary>
    [Fact]
    public void ResolveArray_NonExistentPath_ReturnsNull()
    {
        RenderScope scope = CreateRootScope();
        IReadOnlyList<object?>? result = _resolver.ResolveArray(scope, "nonExistent");
        Assert.Null(result);
    }

    /// <summary>通过 itemKeyPath 解析实例键。</summary>
    [Fact]
    public void ResolveItemKey_ValidPath_ReturnsKey()
    {
        RenderScope scope = CreateRootScope();
        IReadOnlyList<object?>? majors = _resolver.ResolveArray(scope, "majors");
        object? firstMajor = majors![0];
        string? key = _resolver.ResolveItemKey(firstMajor, "majorId");
        Assert.Equal("080901", key);
    }

    /// <summary>不存在的键路径返回 null。</summary>
    [Fact]
    public void ResolveItemKey_NonExistentPath_ReturnsNull()
    {
        RenderScope scope = CreateRootScope();
        IReadOnlyList<object?>? majors = _resolver.ResolveArray(scope, "majors");
        object? firstMajor = majors![0];
        string? key = _resolver.ResolveItemKey(firstMajor, "nonExistent");
        Assert.Null(key);
    }

    /// <summary>通过 $parent 引用父作用域的变量。</summary>
    [Fact]
    public void ResolveValue_ParentScope_ResolvesViaParent()
    {
        RenderScope root = CreateRootScope();
        IReadOnlyList<object?>? majors = _resolver.ResolveArray(root, "majors");
        object? firstMajor = majors![0];
        RenderScope child = root.CreateChild("major", firstMajor, 0, "major-loop/080901");
        object? result = _resolver.ResolveValue(child, "$parent.school.schoolName");
        Assert.NotNull(result);
        Assert.Equal("示例大学", result!.ToString());
    }

    /// <summary>$index 变量返回当前索引。</summary>
    [Fact]
    public void ResolveValue_IndexVariable_ReturnsIndex()
    {
        RenderScope root = CreateRootScope();
        IReadOnlyList<object?>? majors = _resolver.ResolveArray(root, "majors");
        RenderScope child = root.CreateChild("major", majors![0], 5, "major-loop/080901");
        object? result = _resolver.ResolveValue(child, "$index");
        Assert.Equal(5, result);
    }

    /// <summary>$key 变量返回实例键。</summary>
    [Fact]
    public void ResolveValue_KeyVariable_ReturnsInstanceKey()
    {
        RenderScope root = CreateRootScope();
        IReadOnlyList<object?>? majors = _resolver.ResolveArray(root, "majors");
        RenderScope child = root.CreateChild("major", majors![0], 0, "major-loop/080901");
        object? result = _resolver.ResolveValue(child, "$key");
        Assert.Equal("major-loop/080901", result);
    }

    /// <summary>循环变量可以访问其属性。</summary>
    [Fact]
    public void ResolveValue_ItemVariable_ReturnsItemProperty()
    {
        RenderScope root = CreateRootScope();
        IReadOnlyList<object?>? majors = _resolver.ResolveArray(root, "majors");
        RenderScope child = root.CreateChild("major", majors![0], 0, "major-loop/080901");
        object? result = _resolver.ResolveValue(child, "major.majorName");
        Assert.NotNull(result);
        Assert.Equal("计算机科学与技术", result!.ToString());
    }

    /// <summary>小数属性可以正确解析。</summary>
    [Fact]
    public void ResolveValue_DecimalProperty_ReturnsDecimal()
    {
        RenderScope root = CreateRootScope();
        IReadOnlyList<object?>? majors = _resolver.ResolveArray(root, "majors");
        RenderScope child = root.CreateChild("major", majors![0], 0, "major-loop/080901");
        object? result = _resolver.ResolveValue(child, "major.employmentRate");
        Assert.NotNull(result);
        Assert.Equal(95.2m, Convert.ToDecimal(result));
    }

    /// <summary>不同项的键解析结果不同。</summary>
    [Fact]
    public void ResolveItemKey_DuplicateKeys_ResolvesCorrectly()
    {
        RenderScope scope = CreateRootScope();
        IReadOnlyList<object?>? majors = _resolver.ResolveArray(scope, "majors");
        string? key1 = _resolver.ResolveItemKey(majors![0], "majorId");
        string? key2 = _resolver.ResolveItemKey(majors[1], "majorId");
        Assert.Equal("080901", key1);
        Assert.Equal("080902", key2);
        Assert.NotEqual(key1, key2);
    }

    /// <summary>DTO 公开属性可以正确解析。</summary>
    [Fact]
    public void ResolveValue_DtoProperty_ResolvesCorrectly()
    {
        TestDto dto = new() { Name = "test", Value = 42 };
        Dictionary<string, object?> variables = new(StringComparer.Ordinal)
        {
            ["dto"] = dto,
        };
        RenderScope scope = RenderScope.CreateRoot(variables);
        object? name = _resolver.ResolveValue(scope, "dto.Name");
        object? value = _resolver.ResolveValue(scope, "dto.Value");
        Assert.Equal("test", name);
        Assert.Equal(42, value);
    }

    /// <summary>测试用的 DTO 类。</summary>
    private sealed class TestDto
    {
        /// <summary>获取或设置名称。</summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>获取或设置数值。</summary>
        public int Value { get; set; }
    }
}
