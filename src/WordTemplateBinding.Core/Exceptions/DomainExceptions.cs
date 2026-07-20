namespace WordTemplateBinding.Core.Exceptions;

/// <summary>
/// 表示 Word 模板绑定业务中的可预期异常。
/// </summary>
public abstract class WordTemplateBindingException : Exception
{
    /// <summary>
    /// 初始化业务异常。
    /// </summary>
    /// <param name="errorCode">稳定错误代码。</param>
    /// <param name="message">面向调用方的错误说明。</param>
    /// <param name="innerException">导致当前异常的内部异常。</param>
    protected WordTemplateBindingException(
        string errorCode,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    /// <summary>
    /// 获取稳定错误代码。
    /// </summary>
    public string ErrorCode { get; }
}

/// <summary>
/// 表示找不到指定模板。
/// </summary>
public sealed class TemplateNotFoundException : WordTemplateBindingException
{
    /// <summary>
    /// 初始化模板不存在异常。
    /// </summary>
    /// <param name="templateId">模板唯一标识。</param>
    public TemplateNotFoundException(Guid templateId)
        : base("template_not_found", $"找不到模板：{templateId}。")
    {
    }
}

/// <summary>
/// 表示上传内容不是可处理的 DOCX 模板。
/// </summary>
public sealed class InvalidTemplateFileException : WordTemplateBindingException
{
    /// <summary>
    /// 初始化无效模板异常。
    /// </summary>
    /// <param name="message">无效原因。</param>
    /// <param name="innerException">底层 OpenXML 或包异常。</param>
    public InvalidTemplateFileException(string message, Exception? innerException = null)
        : base("invalid_template_file", message, innerException)
    {
    }
}

/// <summary>
/// 表示上传模板超过配置大小限制。
/// </summary>
public sealed class TemplateTooLargeException : WordTemplateBindingException
{
    /// <summary>
    /// 初始化文件过大异常。
    /// </summary>
    /// <param name="maxSizeMb">允许的最大文件大小。</param>
    public TemplateTooLargeException(int maxSizeMb)
        : base("template_too_large", $"模板文件超过 {maxSizeMb} MB 限制。")
    {
    }
}

/// <summary>
/// 表示模板中没有识别到当前阶段支持的模拟数据。
/// </summary>
public sealed class NoMockDataFoundException : WordTemplateBindingException
{
    /// <summary>
    /// 初始化未识别到模拟数据异常。
    /// </summary>
    public NoMockDataFoundException()
        : base("no_mock_data_found", "模板中没有识别到小数、整数或显式文字模拟数据。")
    {
    }
}

/// <summary>
/// 表示找不到指定模拟数据定位标识。
/// </summary>
public sealed class LocatorNotFoundException : WordTemplateBindingException
{
    /// <summary>
    /// 初始化定位标识不存在异常。
    /// </summary>
    /// <param name="locatorId">模拟数据定位标识。</param>
    public LocatorNotFoundException(string locatorId)
        : base("locator_not_found", $"找不到模拟数据定位标识：{locatorId}。")
    {
    }
}

/// <summary>
/// 表示找不到指定数据字段。
/// </summary>
public sealed class DataFieldNotFoundException : WordTemplateBindingException
{
    /// <summary>
    /// 初始化字段不存在异常。
    /// </summary>
    /// <param name="dataPath">数据字段路径。</param>
    public DataFieldNotFoundException(string dataPath)
        : base("data_field_not_found", $"找不到数据字段：{dataPath}。")
    {
    }
}

/// <summary>
/// 表示绑定关系不满足当前阶段约束。
/// </summary>
public sealed class BindingValidationException : WordTemplateBindingException
{
    /// <summary>
    /// 初始化绑定校验异常。
    /// </summary>
    /// <param name="message">校验失败原因。</param>
    public BindingValidationException(string message)
        : base("binding_validation_failed", message)
    {
    }
}

/// <summary>
/// 表示模板当前没有任何可用于生成报告的绑定。
/// </summary>
public sealed class EmptyBindingsException : WordTemplateBindingException
{
    /// <summary>
    /// 初始化空绑定异常。
    /// </summary>
    public EmptyBindingsException()
        : base("empty_bindings", "当前模板尚未保存任何绑定关系。")
    {
    }
}

/// <summary>
/// 表示绑定字段缺少可用的数据值。
/// </summary>
public sealed class MissingDataValueException : WordTemplateBindingException
{
    /// <summary>
    /// 初始化缺少数据值异常。
    /// </summary>
    /// <param name="dataPath">缺少值的字段路径。</param>
    public MissingDataValueException(string dataPath)
        : base("missing_data_value", $"字段 {dataPath} 缺少可用数据值。")
    {
    }
}

/// <summary>
/// 表示输入数据无法转换为字段声明类型。
/// </summary>
public sealed class DataValueConversionException : WordTemplateBindingException
{
    /// <summary>
    /// 初始化数据转换异常。
    /// </summary>
    /// <param name="dataPath">字段路径。</param>
    /// <param name="innerException">底层格式化异常。</param>
    public DataValueConversionException(string dataPath, Exception? innerException = null)
        : base("data_value_conversion_failed", $"字段 {dataPath} 的数据值无法解析。", innerException)
    {
    }
}

/// <summary>
/// 表示报告生成过程中发生不可恢复的 OpenXML 错误。
/// </summary>
public sealed class ReportRenderingException : WordTemplateBindingException
{
    /// <summary>
    /// 初始化报告生成异常。
    /// </summary>
    /// <param name="message">错误说明。</param>
    /// <param name="innerException">底层异常。</param>
    public ReportRenderingException(string message, Exception? innerException = null)
        : base("report_rendering_failed", message, innerException)
    {
    }
}
