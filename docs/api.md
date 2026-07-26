# HTTP API

基础路径：`/api`。除 DOCX 文件响应外，错误使用 `application/problem+json`：

```json
{
  "title": "请求无法处理",
  "status": 400,
  "detail": "templateId 必须是大于 0 的无符号整数。",
  "errorCode": "invalid_database_id",
  "traceId": "..."
}
```

所有来自 `BIGINT UNSIGNED` 的 ID 都是 JSON 字符串，例如 `"18446744073709551615"`，不得由 JavaScript 转为 `number`。

## 1. 模板

### 查询模板

```http
GET /api/templates?name=&code=&type=&status=&page=1&pageSize=20
```

响应：

```json
{
  "items": [
    {
      "id": "12",
      "templateCode": "ANNUAL_REPORT",
      "templateName": "年度报告",
      "templateType": "SECTION",
      "templateStatus": "ACTIVE",
      "currentVersionNo": 3
    }
  ],
  "total": 1,
  "page": 1,
  "pageSize": 20
}
```

### 创建模板并上传首版本

```http
POST /api/templates
Content-Type: multipart/form-data
```

字段：

| 字段 | 必填 | 说明 |
| --- | --- | --- |
| `file` | 是 | 普通 `.docx` |
| `templateCode` | 是 | 字母、数字、`_`、`-`，最多 64 字符 |
| `templateName` | 是 | 最多 255 字符 |
| `templateType` | 否 | 默认 `SECTION` |
| `categoryCode` | 否 | 分类编码 |
| `description` | 否 | 描述 |

成功 `201`，返回模板版本视图：

```json
{
  "template": {
    "id": "12",
    "templateCode": "ANNUAL_REPORT",
    "currentVersionNo": 1
  },
  "version": {
    "id": "31",
    "templateId": "12",
    "versionNo": 1,
    "fileObjectId": "45",
    "versionStatus": "READY",
    "elementCount": 2
  },
  "file": {
    "id": "45",
    "originalName": "annual.docx",
    "fileSize": 12345,
    "sha256": "..."
  },
  "elements": [
    {
      "id": "101",
      "templateVersionId": "31",
      "elementType": "TEXT",
      "locator": {
        "locatorId": "base64url",
        "partKind": "MainDocument",
        "partKey": "/word/document.xml"
      },
      "bindingSchema": {
        "targetProperty": "$",
        "allowedTypes": [ "String", "Integer", "Decimal", "Date", "Boolean" ]
      }
    }
  ],
  "parseResult": {
    "warnings": [],
    "scanResult": {
      "mockItems": [],
      "charts": []
    }
  }
}
```

### 上传新版本

```http
POST /api/templates/{templateId}/versions
Content-Type: multipart/form-data
```

字段：`file`。返回 `201` 模板版本视图。

### 获取模板和版本

```http
GET /api/templates/{templateId}
GET /api/templates/{templateId}/versions
GET /api/templates/{templateId}/current
GET /api/template-versions/{versionId}
GET /api/template-versions/{versionId}/elements
```

### 下载版本文件

```http
GET /api/template-versions/{versionId}/file
```

响应为原始 DOCX 流。服务端按数据库分片顺序输出并校验大小与完整 SHA-256。

### 重扫

```http
POST /api/template-versions/{versionId}/rescan
```

重用原 `file_object_id`，替换该版本的元素目录并更新解析结果。

## 2. 项目与章节

```http
GET  /api/projects
POST /api/projects
GET  /api/projects/{projectId}/chapters
POST /api/projects/{projectId}/chapters
```

创建项目：

```json
{
  "projectCode": "REPORT_2026",
  "projectName": "2026 年度报告",
  "description": "..."
}
```

创建章节：

```json
{
  "chapterCode": "CH01",
  "title": "第一章",
  "parentId": null,
  "sortKey": 1
}
```

## 3. 数据连接

```http
GET  /api/data-connections?projectId={projectId}
POST /api/data-connections
POST /api/data-connections/{connectionId}/test
GET  /api/data-connections/{connectionId}/schemas
GET  /api/data-connections/{connectionId}/objects?schema=reporting
GET  /api/data-connections/{connectionId}/columns?schema=reporting&objectName=student_score
```

创建请求：

```json
{
  "projectId": "1",
  "connectionName": "只读业务库",
  "connectionType": "MYSQL",
  "config": {
    "host": "db.internal",
    "port": 3306,
    "database": "school",
    "sslMode": "Required"
  },
  "credentialRef": "config:DataSourceCredentials:schoolDb"
}
```

响应不会包含账号、密码或连接串。

## 4. 数据源、快照与字段

```http
GET  /api/data-sources?projectId={projectId}
POST /api/data-sources
POST /api/data-sources/{dataSourceId}/refresh
GET  /api/data-sources/{dataSourceId}/snapshot
GET  /api/data-sources/{dataSourceId}/fields?query=&limit=200
GET  /api/data-sources/{dataSourceId}/schema?query=
```

创建数据库对象数据源：

```json
{
  "projectId": "1",
  "connectionId": "3",
  "sourceCode": "STUDENT_SCORE",
  "sourceName": "学生成绩",
  "sourceType": "DATABASE",
  "schemaName": "school",
  "objectType": "BASE TABLE",
  "objectName": "student_score"
}
```

刷新成功后创建不可变快照与字段目录。样例最多 20 行；Binary/BLOB 列不返回。

## 5. 绑定集

### 获取或创建草稿

```http
POST /api/binding-sets
```

```json
{
  "chapterId": "7",
  "templateVersionId": "31"
}
```

相同章节和模板版本已有 DRAFT 时返回原草稿，否则创建递增版本。

### 查询和保存绑定

```http
GET    /api/binding-sets/{bindingSetId}/items
PUT    /api/binding-sets/{bindingSetId}/items/{templateElementId}
DELETE /api/binding-sets/{bindingSetId}/items/{templateElementId}
```

保存请求：

```json
{
  "dataSourceId": "9",
  "sourcePath": "rows.AverageScore",
  "targetProperty": "$",
  "sourceKind": "DATA_SOURCE",
  "transformConfigJson": null,
  "formatConfigJson": null,
  "fallbackValueJson": null,
  "isRequired": false
}
```

图表格式映射可放在 `formatConfigJson`：

```json
{
  "chartMapping": {
    "mode": "rows",
    "categoryField": "Grade",
    "seriesMappings": [
      {
        "seriesIndex": 0,
        "seriesKey": "county",
        "valueField": "CountyScore"
      }
    ]
  }
}
```

### 建议、预览和校验

```http
GET  /api/template-elements/{templateElementId}/suggestions?dataSourceId={dataSourceId}
POST /api/binding-sets/{bindingSetId}/resolve-candidates?dataSourceId={dataSourceId}
GET  /api/binding-sets/{bindingSetId}/preview/{templateElementId}
POST /api/binding-sets/{bindingSetId}/validate
```

建议响应包含 `fieldPath`、0–100 `score` 和 `reasons`。候选恢复只保存高置信度、无并列结果。

校验响应：

```json
{
  "status": "VALID",
  "summary": {
    "elementCount": 4,
    "boundCount": 3,
    "requiredUnboundCount": 0,
    "invalidBindingCount": 0,
    "warningCount": 1
  },
  "items": []
}
```

### 生成输出

```http
POST /api/binding-sets/{bindingSetId}/reports
POST /api/binding-sets/{bindingSetId}/export-reusable
```

报告生成会先执行全量校验，使用绑定项数据源的最新 READY 快照。响应均为独立 DOCX。

## 6. 常见错误

| HTTP | errorCode | 含义 |
| --- | --- | --- |
| 400 | `invalid_database_id` | ID 不是大于 0 的无符号整数 |
| 400 | `invalid_template_file` | 空文件、非 DOCX 或包损坏 |
| 400 | `data_connection_unavailable` | 凭据引用缺失或连接不可用 |
| 404 | `template_not_found` | 模板不存在 |
| 409 | `binding_validation_failed` | 类型、归属或目标属性不兼容 |
| 409 | `empty_bindings` | 绑定集为空 |
| 413 | `template_too_large` | 文件超过配置上限 |
| 500 | `report_rendering_failed` | OpenXML 报告渲染失败 |

## 7. 开发兼容 API

`Persistence:Mode=InMemory` 时额外映射原 GUID 演示接口（`/api/templates/upload`、`/api/bindings`、`/api/reports/generate` 等）用于既有回归测试。生产 MySQL 模式不映射这些端点；新前端只调用本文件描述的正式 API。
