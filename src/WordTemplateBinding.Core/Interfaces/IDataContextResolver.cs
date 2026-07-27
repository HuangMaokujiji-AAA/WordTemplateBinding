using System.Text.Json;
using WordTemplateBinding.Core.Models;

namespace WordTemplateBinding.Core.Interfaces;

/// <summary>
/// 定义从 JSON 数据中解析路径并构建渲染作用域的能力。
/// </summary>
public interface IDataContextResolver
{
    /// <summary>
    /// 根据数据路径在当前作用域中解析值。
    /// </summary>
    /// <param name="scope">当前渲染作用域。</param>
    /// <param name="path">数据路径，例如 "major.majorName" 或 "$parent.school.schoolName"。</param>
    /// <returns>返回解析到的值；路径不存在时返回 <see langword="null"/>。</returns>
    object? ResolveValue(RenderScope scope, string path);

    /// <summary>
    /// 根据数据路径在当前作用域中解析数组。
    /// </summary>
    /// <param name="scope">当前渲染作用域。</param>
    /// <param name="sourcePath">数据源路径，例如 "majors"。</param>
    /// <returns>返回解析到的 JSON 数组；路径不存在或不是数组时返回 <see langword="null"/>。</returns>
    IReadOnlyList<object?>? ResolveArray(RenderScope scope, string sourcePath);

    /// <summary>
    /// 在数组项中解析稳定业务键的值。
    /// </summary>
    /// <param name="item">数组中的单个元素。</param>
    /// <param name="itemKeyPath">键路径，例如 "majorId"。</param>
    /// <returns>返回键的字符串表示；无法解析时返回 <see langword="null"/>。</returns>
    string? ResolveItemKey(object? item, string itemKeyPath);
}
