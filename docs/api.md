# API 文档

基础路径：`/api`

除报告文件流外，错误统一使用 `application/problem+json` 风格响应：

```json
{
  "type": "about:blank",
  "title": "资源不存在",
  "status": 404,
  "detail": "找不到模板：00000000-0000-0000-0000-000000000000。",
  "instance": "/api/templates/...",
  "errorCode": "template_not_found",
  "traceId": "..."
}
```

## 1. 上传模板

```http
POST /api/templates/upload
Content-Type: multipart/form-data
```

表单字段：

| 字段 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `file` | DOCX 文件 | 是 | 最大值由 `MaxUploadSizeMb` 配置 |

成功响应 `200`：

```json
{
  "templateId": "f583f393-57ae-4a19-9845-5a93404fc27c",
  "fileName": "template.docx",
  "contentHash": "sha256-hex",
  "mockItemCount": 1,
  "chartCount": 1,
  "bindingCount": 0,
  "importSummary": {
    "textBindingsRestored": 0,
    "chartBindingsRestored": 0,
    "unresolvedPlaceholders": [],
    "warnings": []
  },
  "mockItems": [
    {
      "locatorId": "base64url",
      "mockValue": "88.5",
      "dataType": "Decimal",
      "locator": {
        "partKind": "MainDocument",
        "partKey": "/word/document.xml",
        "paragraphIndex": 0,
        "startOffset": 11,
        "length": 4,
        "occurrenceIndex": 0,
        "originalValue": "88.5",
        "contextHash": "sha256-hex"
      },
      "paragraphText": "本年度学生平均成绩为 88.5 分。",
      "previewParagraphIndex": 0,
      "isBound": false,
      "boundDataPath": null,
      "boundDataType": null
    }
  ],
  "charts": [
    {
      "locatorId": "chart-base64url",
      "locator": {
        "partKey": "/word/charts/chart1.xml",
        "relationshipId": "rId7",
        "documentOrder": 0
      },
      "chartType": "bar",
      "title": "学生成绩",
      "categories": ["四年级", "八年级"],
      "series": [
        { "seriesIndex": 0, "name": "你县", "values": [543, 505] },
        { "seriesIndex": 1, "name": "全省", "values": [506, 493] }
      ],
      "isBindable": true,
      "isBound": false,
      "boundDataPath": null,
      "boundDataType": null
    }
  ],
  "preview": {
    "paragraphs": [
      {
        "paragraphIndex": 0,
        "text": "本年度学生平均成绩为 88.5 分。",
        "highlights": [
          {
            "locatorId": "base64url",
            "startOffset": 11,
            "length": 4,
            "mockValue": "88.5"
          }
        ]
      }
    ]
  }
}
```

`mockItems[].dataType` 可能为 `Decimal`、`Integer` 或 `String`。文字使用 `{{text:示例文字}}` 显式标记；此时 `mockValue` 为内部文字，`locator.originalValue` 为包含标记语法的完整原文，生成报告时会替换整个标记。

`charts[]` 表示主文档中的 Word 原生 ChartPart。图表只能绑定 `Array` 集合字段。

`importSummary` 始终存在。上传普通 DOCX 时各计数和集合为空；上传复用模板时，后端会在扫描完成后根据 `{{完整数据路径}}` 与内嵌图表 Manifest 创建新 Locator 的绑定。路径匹配区分大小写且必须完全一致。未知字段或损坏 Manifest 不会拒绝整个模板，而是分别进入 `unresolvedPlaceholders` 或 `warnings`。

错误：

- `400 invalid_template_file`
- `400 no_mock_data_found`
- `413 template_too_large`

## 2. 获取模板扫描结果

```http
GET /api/templates/{templateId}
```

返回模板信息、模拟数据、预览和当前绑定状态，响应结构与上传成功响应相同。

错误：

- `404 template_not_found`

## 3. 重新扫描模板

```http
POST /api/templates/{templateId}/rescan
```

从内存中的不可变原始 DOCX 字节重新扫描。成功后删除不再存在的 Locator 绑定。

扫描失败或没有识别结果时不会覆盖旧扫描状态。

错误：

- `404 template_not_found`
- `400 invalid_template_file`
- `400 no_mock_data_found`

## 4. 获取或搜索 Schema

完整树：

```http
GET /api/data-schema
```

搜索：

```http
GET /api/data-schema?query=AverageScore
```

响应：

```json
{
  "query": "AverageScore",
  "totalLeafCount": 3008,
  "matchCount": 1,
  "isTruncated": false,
  "nodes": [
    {
      "name": "平均成绩",
      "path": "StudentStatistics.AverageScore",
      "type": "Decimal",
      "isCollection": false,
      "isLeaf": true,
      "isBindable": true,
      "children": []
    }
  ]
}
```

查询匹配名称或路径，忽略大小写，最多返回 200 项。

## 5. 创建或更新绑定

```http
POST /api/bindings
Content-Type: application/json
```

请求：

```json
{
  "templateId": "f583f393-57ae-4a19-9845-5a93404fc27c",
  "locatorId": "base64url",
  "dataPath": "StudentStatistics.AverageScore"
}
```

成功响应：

```json
{
  "success": true,
  "binding": {
    "templateId": "f583f393-57ae-4a19-9845-5a93404fc27c",
    "targetKind": "Text",
    "locatorId": "base64url",
    "dataPath": "StudentStatistics.AverageScore",
    "dataType": "Decimal",
    "createdAt": "2026-07-17T00:00:00+00:00",
    "updatedAt": "2026-07-17T00:00:00+00:00"
  }
}
```

同一模板和 Locator 再次提交会覆盖当前字段，但保留最初创建时间。

图表绑定请求与文本请求结构相同，`locatorId` 使用 `charts[].locatorId`，`dataPath` 必须指向 `type: "Array"` 且 `isBindable: true` 的集合节点；成功响应中的 `targetKind` 为 `Chart`。

错误：

- `404 template_not_found`
- `404 locator_not_found`
- `404 data_field_not_found`
- `409 binding_validation_failed`

## 6. 获取绑定列表

```http
GET /api/templates/{templateId}/bindings
```

成功返回绑定数组。

错误：

- `404 template_not_found`

## 7. 删除绑定

```http
DELETE /api/templates/{templateId}/bindings/{locatorId}
```

成功响应：

```json
{
  "success": true,
  "deleted": true
}
```

重复删除时 `deleted` 为 `false`。

错误：

- `404 template_not_found`

## 8. 生成报告

```http
POST /api/reports/generate
Content-Type: application/json
```

使用演示值：

```json
{
  "templateId": "f583f393-57ae-4a19-9845-5a93404fc27c"
}
```

覆盖部分演示值：

```json
{
  "templateId": "f583f393-57ae-4a19-9845-5a93404fc27c",
  "values": {
    "StudentStatistics.AverageScore": 92.3
  }
}
```

覆盖图表集合值：

```json
{
  "templateId": "f583f393-57ae-4a19-9845-5a93404fc27c",
  "values": {
    "ChartData.ScienceScores": [
      { "Category": "四年级", "你县": 552, "全省": 506 },
      { "Category": "八年级", "你县": 518, "全省": 493 }
    ]
  }
}
```

集合每行第一列（或 `Category`、`Name`、`Label` 等命名列）作为分类，其余数值列按图表系列名优先匹配，随后按列顺序匹配。集合数值列不能少于模板现有系列数量。

成功响应：

```http
HTTP/1.1 200 OK
Content-Type: application/vnd.openxmlformats-officedocument.wordprocessingml.document
Content-Disposition: attachment; filename=template_generated.docx
```

请求值按字段路径覆盖演示值。合并后某个绑定字段仍缺值时返回错误，不会静默写入空字符串。

错误：

- `404 template_not_found`
- `409 empty_bindings`
- `400 missing_data_value`
- `400 data_value_conversion_failed`
- `404 locator_not_found`
- `500 report_rendering_failed`

## 9. 导出可复用模板

```http
POST /api/templates/{templateId}/export-reusable
```

请求体为空，也不接收任何真实数据值。后端从不可变原始 DOCX 字节创建副本：

- 文本绑定写为 `{{binding.DataPath}}`；
- 未绑定模拟值保持原样；
- 图表本体、类型、样式、分类和系列缓存保持不变；
- 图表绑定写入命名空间为 `urn:word-template-binding:bindings:v1`、版本为 `1` 的 CustomXmlPart；
- 所有 Locator、上下文、重叠范围、字段路径和图表定位在返回文件前统一校验。

成功响应：

```http
HTTP/1.1 200 OK
Content-Type: application/vnd.openxmlformats-officedocument.wordprocessingml.document
Content-Disposition: attachment; filename*=UTF-8''template-template.docx
```

文件名规则为 `{stem}-template.docx`。原文件已以 `-template` 结尾时不重复追加；路径、非法字符、控制字符和过长名称会被清理。

错误：

- `404 template_not_found`
- `409 empty_reusable_template_bindings`
- `409 reusable_template_rendering_failed`

## 状态码汇总

| 状态码 | 使用场景 |
| --- | --- |
| `200` | 请求成功或返回 DOCX |
| `400` | 无效模板、无模拟数据、值缺失或转换失败 |
| `404` | 模板、Locator 或字段不存在 |
| `409` | 绑定类型不兼容、模板没有绑定或复用模板导出前校验失败 |
| `413` | 上传文件超过配置限制 |
| `500` | 报告生成或服务器内部错误 |
