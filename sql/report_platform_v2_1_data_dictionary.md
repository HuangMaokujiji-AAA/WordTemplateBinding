# 评估报告批量生成平台 V2.1 数据库设计与数据字典

> 数据库：MySQL 8.0+  
> 默认字符集：`utf8mb4`  
> 默认排序规则：`utf8mb4_0900_ai_ci`  
> 文件存储方式：**MySQL 分片 BLOB 存储**  
> 适用场景：300 页级大文档拆分、多人协同、模板解析、拖拽数据绑定、并行章节渲染、最终组装与正式发布。

## 1. V2.1 变更结论

由于部署环境不允许使用本地持久化目录、MinIO、OSS 或 S3，V2.1 将所有 DOCX、PDF、Excel、图片和大型 JSON 的文件字节保存到 MySQL。

本方案**不把完整大文件直接放入 `rp_file_object` 的单个 `LONGBLOB` 字段**，而采用以下结构：

- `rp_file_object`：只保存文件元数据、完整哈希、分片参数和状态；
- `rp_file_chunk`：保存实际二进制分片，主键为 `(file_object_id, chunk_no)`；
- `rp_file_upload_session`：保存断点上传会话和进度。

这样可以避免元数据查询加载大 BLOB，降低单次事务和 `max_allowed_packet` 压力，并支持断点续传、失败重试、流式下载和单片校验。

## 2. 关键设计约定

1. 文件原始字节直接写入 `MEDIUMBLOB`，禁止先 Base64 编码后放入 JSON。
2. 默认分片大小为 `4 MiB`（`4194304` 字节），可按压测调整，但不得超过 `MEDIUMBLOB` 上限。
3. 每个分片单独提交事务；禁止把整个数百 MB 文件放在一个数据库事务中。
4. 只有完成分片数量、字节总和、分片哈希和完整文件 SHA-256 校验后，文件状态才能变为 `READY`。
5. 下载必须执行 `ORDER BY chunk_no ASC` 并流式写入响应，不允许一次性把完整文件读入 JVM 内存。
6. 模板版本、数据快照和生成产物仍通过 `file_object_id` 关联文件；业务表无需新增大 BLOB 字段。
7. 文件逻辑删除只更新 `deleted_at/object_status`；物理清理必须先确认没有模板、快照或产物引用。
8. 用户、角色和组织机构继续复用现有认证系统，相关用户 ID 不建立跨系统物理外键。

## 3. 表清单

| 领域 | 表名 | 作用 |
|---|---|---|
| 文件存储域 | `rp_file_object` | 保存数据库文件的元数据、完整哈希、分片参数和生命周期状态。文件二进制不放在本表，避免普通元数据查询加载大 BLOB。 |
| 文件存储域 | `rp_file_chunk` | 按文件和分片序号保存实际二进制内容。默认每片 4MiB，可流式上传、下载、校验和断点续传。 |
| 文件存储域 | `rp_file_upload_session` | 保存大文件断点上传会话、进度、期望哈希、过期时间和失败原因。 |
| 模板域 | `rp_template` | 表示可长期复用的逻辑模板。模板本身不直接对应某个 DOCX 文件，具体文件由模板版本表管理。 |
| 模板域 | `rp_template_version` | 保存逻辑模板的不可变版本及其 DOCX 文件、解析状态、解析器版本和样式指纹。 |
| 模板域 | `rp_template_element` | 保存从某个模板版本中解析出的可绑定元素，是前端模板元素树和拖拽绑定的目标清单。 |
| 项目与章节域 | `rp_project` | 表示一份报告项目，保存当前项目状态、主模板版本、全局上下文版本和最终组装策略。 |
| 项目与章节域 | `rp_project_member` | 保存用户在具体项目中的角色与成员状态，实现项目级权限控制。 |
| 项目与章节域 | `rp_project_context_version` | 保存报告年份、单位名称、报告期等项目全局变量的不可变版本。 |
| 项目与章节域 | `rp_chapter` | 保存项目章节树、章节当前状态、当前版本和排序信息。该表表示章节的当前工作态，不直接承担历史版本职责。 |
| 项目与章节域 | `rp_chapter_revision` | 保存章节的不可变版本，固定章节标题、模板版本、绑定版本、全局上下文版本和章节设置。 |
| 项目与章节域 | `rp_chapter_lock` | 保存章节编辑租约锁。通过令牌、心跳和过期时间避免永久锁，并配合章节乐观锁防止覆盖。 |
| 数据与绑定域 | `rp_data_connection` | 保存数据库、API、SFTP 等数据连接的非敏感配置以及密钥引用。 |
| 数据与绑定域 | `rp_data_source` | 定义一个业务数据源及其刷新方式。数据源描述“从哪里、如何取数”，不直接代表某次实际数据。 |
| 数据与绑定域 | `rp_data_snapshot` | 保存数据源在某次抓取时得到的不可变数据快照，保证生成报告时能够复现。 |
| 数据与绑定域 | `rp_data_field` | 保存数据快照解析出的字段目录，供前端展示字段树并进行拖拽绑定。 |
| 数据与绑定域 | `rp_binding_set` | 保存一个章节完整绑定配置的版本头，固定模板版本并记录校验结果。 |
| 数据与绑定域 | `rp_binding_item` | 保存一条具体绑定规则，将模板元素的某个属性绑定到项目上下文、数据字段、常量或受控表达式。 |
| 生成与发布域 | `rp_generation_job` | 保存一次章节预览、项目预览或正式生成任务的总状态和固定输入清单。 |
| 生成与发布域 | `rp_generation_job_snapshot` | 固定某次生成任务实际使用的各数据源快照，避免生成过程中数据发生变化。 |
| 生成与发布域 | `rp_generation_job_chapter` | 保存生成任务中每个章节的子任务状态、重试次数和输出产物。 |
| 生成与发布域 | `rp_artifact` | 统一记录渲染和组装产生的 DOCX、PDF、HTML、图片及日志等文件产物。 |
| 生成与发布域 | `rp_release` | 保存正式报告发布记录，关联成功生成任务、最终产物和完整版本清单。 |
| 生成与发布域 | `rp_release_chapter` | 保存正式发布中包含的章节顺序、章节版本、章节产物和数据快照清单。 |
| 审计域 | `rp_audit_log` | 保存不可变的业务审计日志，记录操作人、对象、修改前后内容和请求链路。 |

## 4. 文件上传、校验与下载流程

### 4.1 上传流程

1. 创建 `rp_file_object`，状态设为 `UPLOADING`，写入文件名、预计大小、分片大小和逻辑键。
2. 创建 `rp_file_upload_session`，生成唯一 `upload_token`。
3. 客户端按 `chunk_no` 上传二进制分片；服务端计算分片 SHA-256 后写入 `rp_file_chunk`。
4. 每片成功后原子更新上传会话进度；重复上传同一分片时必须校验哈希，不能静默覆盖不同内容。
5. 完成请求触发服务端校验：分片必须从 0 连续到 `total_chunks - 1`，`SUM(chunk_length) = file_size`。
6. 服务端按序读取分片重新计算完整 SHA-256。校验成功后将文件设为 `READY`，会话设为 `COMPLETED`。

### 4.2 下载流程

1. 校验文件状态为 `READY` 且未软删除。
2. 根据 `file_object_id` 查询 `rp_file_chunk`，按 `chunk_no ASC` 流式读取。
3. 直接把 `chunk_data` 写入 HTTP 输出流，并设置 `Content-Length`、`Content-Type` 和下载文件名。

### 4.3 删除流程

业务删除只软删除 `rp_file_object`。定时物理清理时，只有在不存在模板版本、数据快照、产物等引用后才能删除文件对象；物理删除后分片和上传会话通过外键级联清理。

## 5. 物理外键总表

| 外键名 | 子表.字段 | 父表.字段 | 策略 |
|---|---|---|---|
| `fk_rp_file_chunk_object` | `rp_file_chunk.file_object_id` | `rp_file_object.id` | `ON DELETE CASCADE` |
| `fk_rp_upload_file_object` | `rp_file_upload_session.file_object_id` | `rp_file_object.id` | `ON DELETE CASCADE` |
| `fk_rp_tv_template` | `rp_template_version.template_id` | `rp_template.id` | `ON DELETE RESTRICT` |
| `fk_rp_tv_file` | `rp_template_version.file_object_id` | `rp_file_object.id` | `ON DELETE RESTRICT` |
| `fk_rp_te_version` | `rp_template_element.template_version_id` | `rp_template_version.id` | `ON DELETE CASCADE` |
| `fk_rp_project_master_tv` | `rp_project.master_template_version_id` | `rp_template_version.id` | `ON DELETE SET NULL` |
| `fk_rp_member_project` | `rp_project_member.project_id` | `rp_project.id` | `ON DELETE CASCADE` |
| `fk_rp_context_project` | `rp_project_context_version.project_id` | `rp_project.id` | `ON DELETE CASCADE` |
| `fk_rp_chapter_project` | `rp_chapter.project_id` | `rp_project.id` | `ON DELETE CASCADE` |
| `fk_rp_chapter_parent` | `rp_chapter.parent_id` | `rp_chapter.id` | `ON DELETE RESTRICT` |
| `fk_rp_chapter_current_revision` | `rp_chapter.current_revision_id` | `rp_chapter_revision.id` | `ON DELETE SET NULL` |
| `fk_rp_connection_project` | `rp_data_connection.project_id` | `rp_project.id` | `ON DELETE CASCADE` |
| `fk_rp_ds_project` | `rp_data_source.project_id` | `rp_project.id` | `ON DELETE CASCADE` |
| `fk_rp_ds_connection` | `rp_data_source.connection_id` | `rp_data_connection.id` | `ON DELETE SET NULL` |
| `fk_rp_snapshot_source` | `rp_data_snapshot.data_source_id` | `rp_data_source.id` | `ON DELETE RESTRICT` |
| `fk_rp_snapshot_file` | `rp_data_snapshot.file_object_id` | `rp_file_object.id` | `ON DELETE RESTRICT` |
| `fk_rp_field_snapshot` | `rp_data_field.snapshot_id` | `rp_data_snapshot.id` | `ON DELETE CASCADE` |
| `fk_rp_bs_chapter` | `rp_binding_set.chapter_id` | `rp_chapter.id` | `ON DELETE CASCADE` |
| `fk_rp_bs_template_version` | `rp_binding_set.template_version_id` | `rp_template_version.id` | `ON DELETE RESTRICT` |
| `fk_rp_bi_set` | `rp_binding_item.binding_set_id` | `rp_binding_set.id` | `ON DELETE CASCADE` |
| `fk_rp_bi_element` | `rp_binding_item.template_element_id` | `rp_template_element.id` | `ON DELETE RESTRICT` |
| `fk_rp_bi_source` | `rp_binding_item.data_source_id` | `rp_data_source.id` | `ON DELETE RESTRICT` |
| `fk_rp_cr_chapter` | `rp_chapter_revision.chapter_id` | `rp_chapter.id` | `ON DELETE CASCADE` |
| `fk_rp_cr_template_version` | `rp_chapter_revision.template_version_id` | `rp_template_version.id` | `ON DELETE RESTRICT` |
| `fk_rp_cr_binding_set` | `rp_chapter_revision.binding_set_id` | `rp_binding_set.id` | `ON DELETE RESTRICT` |
| `fk_rp_lock_chapter` | `rp_chapter_lock.chapter_id` | `rp_chapter.id` | `ON DELETE CASCADE` |
| `fk_rp_job_project` | `rp_generation_job.project_id` | `rp_project.id` | `ON DELETE RESTRICT` |
| `fk_rp_job_retry` | `rp_generation_job.retry_of_job_id` | `rp_generation_job.id` | `ON DELETE SET NULL` |
| `fk_rp_gjs_job` | `rp_generation_job_snapshot.job_id` | `rp_generation_job.id` | `ON DELETE CASCADE` |
| `fk_rp_gjs_source` | `rp_generation_job_snapshot.data_source_id` | `rp_data_source.id` | `ON DELETE RESTRICT` |
| `fk_rp_gjs_snapshot` | `rp_generation_job_snapshot.data_snapshot_id` | `rp_data_snapshot.id` | `ON DELETE RESTRICT` |
| `fk_rp_artifact_project` | `rp_artifact.project_id` | `rp_project.id` | `ON DELETE RESTRICT` |
| `fk_rp_artifact_job` | `rp_artifact.generation_job_id` | `rp_generation_job.id` | `ON DELETE SET NULL` |
| `fk_rp_artifact_chapter` | `rp_artifact.chapter_id` | `rp_chapter.id` | `ON DELETE SET NULL` |
| `fk_rp_artifact_file` | `rp_artifact.file_object_id` | `rp_file_object.id` | `ON DELETE RESTRICT` |
| `fk_rp_gjc_job` | `rp_generation_job_chapter.job_id` | `rp_generation_job.id` | `ON DELETE CASCADE` |
| `fk_rp_gjc_chapter` | `rp_generation_job_chapter.chapter_id` | `rp_chapter.id` | `ON DELETE RESTRICT` |
| `fk_rp_gjc_revision` | `rp_generation_job_chapter.chapter_revision_id` | `rp_chapter_revision.id` | `ON DELETE RESTRICT` |
| `fk_rp_gjc_artifact` | `rp_generation_job_chapter.output_artifact_id` | `rp_artifact.id` | `ON DELETE SET NULL` |
| `fk_rp_release_project` | `rp_release.project_id` | `rp_project.id` | `ON DELETE RESTRICT` |
| `fk_rp_release_job` | `rp_release.generation_job_id` | `rp_generation_job.id` | `ON DELETE RESTRICT` |
| `fk_rp_release_artifact` | `rp_release.final_artifact_id` | `rp_artifact.id` | `ON DELETE RESTRICT` |
| `fk_rp_rc_release` | `rp_release_chapter.release_id` | `rp_release.id` | `ON DELETE RESTRICT` |
| `fk_rp_rc_chapter` | `rp_release_chapter.chapter_id` | `rp_chapter.id` | `ON DELETE RESTRICT` |
| `fk_rp_rc_revision` | `rp_release_chapter.chapter_revision_id` | `rp_chapter_revision.id` | `ON DELETE RESTRICT` |
| `fk_rp_rc_artifact` | `rp_release_chapter.artifact_id` | `rp_artifact.id` | `ON DELETE SET NULL` |

## 6. 文件存储域

### `rp_file_object`

**作用：** 保存数据库文件的元数据、完整哈希、分片参数和生命周期状态。文件二进制不放在本表，避免普通元数据查询加载大 BLOB。

| 字段 | 类型 | 允许空 | 默认值 | 键/关联 | 详细作用 |
|---|---|---:|---|---|---|
| `id` | `BIGINT UNSIGNED` | 否 | — | PK | 表记录主键。 由数据库自增生成。 |
| `storage_provider` | `VARCHAR(32)` | 否 | `'DATABASE'` | UK:uk_rp_file_object_key | 存储提供方。V2.1 固定使用 `DATABASE`，实际字节位于 `rp_file_chunk`。保留该字段用于兼容原有代码和未来迁移。 |
| `bucket_name` | `VARCHAR(128)` | 否 | `'default'` | UK:uk_rp_file_object_key | 数据库文件逻辑命名空间，默认 `default`。它不再表示外部对象存储桶，并参与文件逻辑键唯一约束。 |
| `object_key` | `VARCHAR(512)` | 否 | — | UK:uk_rp_file_object_key | 数据库内稳定逻辑文件键。用于定位业务文件，不能保存临时下载 URL。 |
| `original_name` | `VARCHAR(255)` | 否 | — | — | 用户上传时的原始文件名。 |
| `file_ext` | `VARCHAR(32)` | 是 | `NULL` | — | 文件扩展名，不含点号。 |
| `mime_type` | `VARCHAR(128)` | 是 | `NULL` | — | 文件 MIME 类型。 |
| `file_size` | `BIGINT UNSIGNED` | 否 | `0` | — | 文件字节数。 |
| `sha256` | `CHAR(64)` | 是 | `NULL` | — | 文件内容 SHA-256，用于完整性校验、去重和版本追踪。 |
| `chunk_size` | `INT UNSIGNED` | 否 | `4194304` | — | 该文件标准分片大小，单位字节。默认 4MiB；除最后一片外，各分片通常应等于该值。 |
| `total_chunks` | `INT UNSIGNED` | 否 | `0` | — | 文件完成上传后应具有的分片总数。必须与 `rp_file_chunk` 实际行数一致。 |
| `object_status` | `VARCHAR(32)` | 否 | `'UPLOADING'` | — | 文件生命周期状态。只有 `READY` 文件允许被模板、数据快照、生成产物等业务正式使用。 |
| `upload_completed_at` | `DATETIME(3)` | 是 | `NULL` | — | 所有分片写入并完成完整文件大小、分片数量和 SHA-256 校验的时间。 |
| `metadata_json` | `JSON` | 是 | `NULL` | — | 文件扩展元数据，例如图片尺寸、页数、编码、文档属性。 |
| `created_by` | `BIGINT UNSIGNED` | 是 | `NULL` | 逻辑用户关联 | 创建人或上传人用户 ID，逻辑关联现有用户系统（如 RuoYi `sys_user.user_id`），不建立物理外键。 |
| `created_at` | `DATETIME(3)` | 否 | `CURRENT_TIMESTAMP(3)` | — | 记录创建时间，使用毫秒精度。 |
| `row_version` | `INT UNSIGNED` | 否 | `0` | — | 文件元数据乐观锁版本，更新上传状态、计数和完成时间时用于防止并发覆盖。 |
| `deleted_at` | `DATETIME(3)` | 是 | `NULL` | — | 软删除时间；为空表示未删除。 |

**主键：** `id`

**唯一约束：**

- `uk_rp_file_object_key`：(`storage_provider`, `bucket_name`, `object_key`)

**普通索引：**

- `idx_rp_file_sha256`：(`sha256`)
- `idx_rp_file_status_time`：(`object_status`, `created_at`)
- `idx_rp_file_created_at`：(`created_at`)

**外键关联：**
 无物理外键。

### `rp_file_chunk`

**作用：** 按文件和分片序号保存实际二进制内容。默认每片 4MiB，可流式上传、下载、校验和断点续传。

| 字段 | 类型 | 允许空 | 默认值 | 键/关联 | 详细作用 |
|---|---|---:|---|---|---|
| `file_object_id` | `BIGINT UNSIGNED` | 否 | — | PK<br>FK:fk_rp_file_chunk_object | 所属文件对象 ID，与 `chunk_no` 组成复合主键；物理删除文件对象时分片级联删除。 |
| `chunk_no` | `INT UNSIGNED` | 否 | — | PK | 从 0 开始连续递增的分片序号。下载时必须按该字段升序流式输出。 |
| `chunk_length` | `INT UNSIGNED` | 否 | — | — | 当前分片实际字节数。最后一片可以小于文件标准 `chunk_size`。 |
| `chunk_sha256` | `CHAR(64)` | 否 | — | — | 当前分片 SHA-256，用于断点续传时校验单片内容和防止错误覆盖。 |
| `chunk_data` | `MEDIUMBLOB` | 否 | — | — | 实际文件二进制分片。使用 `MEDIUMBLOB`，应用层默认控制在 4MiB，禁止 Base64 编码后再写入。 |
| `created_at` | `DATETIME(3)` | 否 | `CURRENT_TIMESTAMP(3)` | — | 记录创建时间，使用毫秒精度。 |

**主键：** `file_object_id`, `chunk_no`

**唯一约束：**
 无。

**普通索引：**
 无。

**外键关联：**

- `fk_rp_file_chunk_object`：(`file_object_id`) → `rp_file_object`(`id`)；策略：`ON DELETE CASCADE`。

### `rp_file_upload_session`

**作用：** 保存大文件断点上传会话、进度、期望哈希、过期时间和失败原因。

| 字段 | 类型 | 允许空 | 默认值 | 键/关联 | 详细作用 |
|---|---|---:|---|---|---|
| `id` | `BIGINT UNSIGNED` | 否 | — | PK | 上传会话ID 由数据库自增生成。 |
| `upload_token` | `CHAR(36)` | 否 | — | UK:uk_rp_file_upload_token | 上传会话 UUID。客户端上传分片、查询缺失分片、完成校验和取消上传时均应携带。 |
| `file_object_id` | `BIGINT UNSIGNED` | 否 | — | FK:fk_rp_upload_file_object | 本次上传对应的文件元数据记录。创建上传会话前应先创建状态为 `UPLOADING` 的文件对象。 |
| `expected_file_size` | `BIGINT UNSIGNED` | 否 | — | — | 客户端声明的完整文件字节数，完成上传时必须与分片字节总和一致。 |
| `expected_sha256` | `CHAR(64)` | 是 | `NULL` | — | 客户端可选声明的完整文件 SHA-256；服务端完成上传后必须流式重算并比对。 |
| `chunk_size` | `INT UNSIGNED` | 否 | `4194304` | — | 该上传会话约定的分片大小，应与对应文件对象的 `chunk_size` 一致。 |
| `expected_chunks` | `INT UNSIGNED` | 否 | — | — | 根据完整文件大小和分片大小计算出的预计分片数。 |
| `uploaded_chunks` | `INT UNSIGNED` | 否 | `0` | — | 已确认上传分片数量的进度缓存。最终完整性以查询 `rp_file_chunk` 为准。 |
| `uploaded_bytes` | `BIGINT UNSIGNED` | 否 | `0` | — | 已确认上传字节数的进度缓存。最终完整性以分片实际字节总和为准。 |
| `upload_status` | `VARCHAR(32)` | 否 | `'CREATED'` | — | 上传会话状态。完成前不得把文件对象状态设置为 `READY`。 |
| `last_activity_at` | `DATETIME(3)` | 否 | `CURRENT_TIMESTAMP(3)` | — | 最近一次上传、续传、查询或校验时间，用于识别僵尸会话。 |
| `expires_at` | `DATETIME(3)` | 否 | — | — | 会话过期时间。过期会话可由定时任务标记为 `EXPIRED` 并清理无引用分片。 |
| `error_message` | `VARCHAR(2000)` | 是 | `NULL` | — | 上传或完整性校验失败的可读错误摘要。 |
| `created_by` | `BIGINT UNSIGNED` | 是 | `NULL` | 逻辑用户关联 | 上传发起人 |
| `created_at` | `DATETIME(3)` | 否 | `CURRENT_TIMESTAMP(3)` | — | 记录创建时间，使用毫秒精度。 |
| `updated_at` | `DATETIME(3)` | 否 | `CURRENT_TIMESTAMP(3)` | — | 记录最后更新时间。 |
| `row_version` | `INT UNSIGNED` | 否 | `0` | — | 乐观锁版本 |

**主键：** `id`

**唯一约束：**

- `uk_rp_file_upload_token`：(`upload_token`)

**普通索引：**

- `idx_rp_upload_file_status`：(`file_object_id`, `upload_status`)
- `idx_rp_upload_status_expire`：(`upload_status`, `expires_at`)

**外键关联：**

- `fk_rp_upload_file_object`：(`file_object_id`) → `rp_file_object`(`id`)；策略：`ON DELETE CASCADE`。


## 7. 模板域

### `rp_template`

**作用：** 表示可长期复用的逻辑模板。模板本身不直接对应某个 DOCX 文件，具体文件由模板版本表管理。

| 字段 | 类型 | 允许空 | 默认值 | 键/关联 | 详细作用 |
|---|---|---:|---|---|---|
| `id` | `BIGINT UNSIGNED` | 否 | — | PK | 表记录主键。 由数据库自增生成。 |
| `template_code` | `VARCHAR(64)` | 否 | — | UK:uk_rp_template_code | 逻辑模板稳定业务编码，创建后不应随名称修改。 |
| `template_name` | `VARCHAR(255)` | 否 | — | — | 模板显示名称。 |
| `template_type` | `VARCHAR(32)` | 否 | `'SECTION'` | — | 模板类型：主模板、章节模板或可复用组件。 |
| `category_code` | `VARCHAR(64)` | 是 | `NULL` | — | 模板分类编码，便于目录筛选和权限控制。 |
| `template_status` | `VARCHAR(32)` | 否 | `'ACTIVE'` | — | 逻辑模板状态。 |
| `description` | `TEXT` | 是 | `NULL` | — | 业务描述或备注。 |
| `current_version_no` | `INT UNSIGNED` | 否 | `0` | — | 当前推荐或生效的模板版本号，只用于快速定位；正式生成仍应固定模板版本 ID。 |
| `created_by` | `BIGINT UNSIGNED` | 是 | `NULL` | 逻辑用户关联 | 创建人或上传人用户 ID，逻辑关联现有用户系统（如 RuoYi `sys_user.user_id`），不建立物理外键。 |
| `created_at` | `DATETIME(3)` | 否 | `CURRENT_TIMESTAMP(3)` | — | 记录创建时间，使用毫秒精度。 |
| `updated_by` | `BIGINT UNSIGNED` | 是 | `NULL` | 逻辑用户关联 | 最后修改人用户 ID，逻辑关联现有用户系统。 |
| `updated_at` | `DATETIME(3)` | 否 | `CURRENT_TIMESTAMP(3)` | — | 记录最后更新时间，由 MySQL 自动刷新。 |
| `row_version` | `INT UNSIGNED` | 否 | `0` | — | 乐观锁版本号。更新时必须携带旧版本并自增，防止并发覆盖。 |
| `deleted_at` | `DATETIME(3)` | 是 | `NULL` | — | 软删除时间；为空表示未删除。 |

**主键：** `id`

**唯一约束：**

- `uk_rp_template_code`：(`template_code`)

**普通索引：**

- `idx_rp_template_type_status`：(`template_type`, `template_status`)

**外键关联：**
 无物理外键。

### `rp_template_version`

**作用：** 保存逻辑模板的不可变版本及其 DOCX 文件、解析状态、解析器版本和样式指纹。

| 字段 | 类型 | 允许空 | 默认值 | 键/关联 | 详细作用 |
|---|---|---:|---|---|---|
| `id` | `BIGINT UNSIGNED` | 否 | — | PK | 模板版本主键。 由数据库自增生成。 |
| `template_id` | `BIGINT UNSIGNED` | 否 | — | UK:uk_rp_template_version<br>UK:uk_rp_template_file<br>FK:fk_rp_tv_template | 所属逻辑模板 ID。 |
| `version_no` | `INT UNSIGNED` | 否 | — | UK:uk_rp_template_version | 该逻辑模板内部递增的版本号。 |
| `file_object_id` | `BIGINT UNSIGNED` | 否 | — | UK:uk_rp_template_file<br>FK:fk_rp_tv_file | 关联统一文件对象表中的文件记录。 |
| `version_status` | `VARCHAR(32)` | 否 | `'UPLOADED'` | — | 模板文件版本处理状态。 |
| `parser_name` | `VARCHAR(64)` | 是 | `NULL` | — | 执行模板解析的解析器名称。 |
| `parser_version` | `VARCHAR(32)` | 是 | `NULL` | — | 解析器程序版本，用于问题复现。 |
| `parse_result_json` | `JSON` | 是 | `NULL` | — | 模板解析摘要、兼容性警告和错误信息。 |
| `page_count` | `INT UNSIGNED` | 是 | `NULL` | — | 文档或产物页数。 |
| `element_count` | `INT UNSIGNED` | 否 | `0` | — | 模板版本解析出的可绑定元素数量。 |
| `style_fingerprint` | `CHAR(64)` | 是 | `NULL` | — | 模板样式集合指纹，用于判断样式变化和组装冲突。 |
| `published_by` | `BIGINT UNSIGNED` | 是 | `NULL` | 逻辑用户关联 | 发布操作人用户 ID，逻辑关联现有用户系统。 |
| `published_at` | `DATETIME(3)` | 是 | `NULL` | — | 版本或发布记录正式生效时间。 |
| `created_by` | `BIGINT UNSIGNED` | 是 | `NULL` | 逻辑用户关联 | 创建人或上传人用户 ID，逻辑关联现有用户系统（如 RuoYi `sys_user.user_id`），不建立物理外键。 |
| `created_at` | `DATETIME(3)` | 否 | `CURRENT_TIMESTAMP(3)` | — | 记录创建时间，使用毫秒精度。 |

**主键：** `id`

**唯一约束：**

- `uk_rp_template_version`：(`template_id`, `version_no`)
- `uk_rp_template_file`：(`template_id`, `file_object_id`)

**普通索引：**

- `idx_rp_template_version_status`：(`template_id`, `version_status`)

**外键关联：**

- `fk_rp_tv_template`：(`template_id`) → `rp_template`(`id`)；策略：`ON DELETE RESTRICT`。
- `fk_rp_tv_file`：(`file_object_id`) → `rp_file_object`(`id`)；策略：`ON DELETE RESTRICT`。

### `rp_template_segment`（V2.2）

**作用：** 保存不可变模板版本中的逻辑绑定范围、稳定锚点、树形关系、结构指纹和片段预览缓存。片段预览仅供工作区使用，最终报告仍读取完整原始模板。

| 字段组 | 关键字段 | 说明 |
|---|---|---|
| 身份与树 | `id`, `template_version_id`, `parent_segment_id`, `segment_key` | `segment_key` 在模板版本内唯一；父片段删除受限。 |
| 显示与范围 | `segment_name`, `segment_type`, `anchor_type`, `start_anchor_json`, `end_anchor_json`, `document_order_start`, `document_order_end` | 第一版支持 `CONTENT_CONTROL` 和旧模板的 `VIRTUAL` 根片段。 |
| 状态与缓存 | `segment_status`, `segment_fingerprint`, `preview_file_object_id`, `preview_status`, `preview_error_message` | 指纹变化会使预览缓存失效；预览文件删除时引用自动置空。 |
| 并发与审计 | `sort_no`, `row_version`, `created_by`, `created_at`, `updated_by`, `updated_at` | `row_version` 用于乐观锁。 |

**约束与索引：**

- `uk_rp_template_segment`：(`template_version_id`, `segment_key`)
- `idx_rp_segment_order`：(`template_version_id`, `document_order_start`)
- `idx_rp_segment_parent`：(`parent_segment_id`, `sort_no`)
- `fk_rp_segment_version` → `rp_template_version(id)`，`ON DELETE CASCADE`
- `fk_rp_segment_parent` → `rp_template_segment(id)`，`ON DELETE RESTRICT`
- `fk_rp_segment_preview_file` → `rp_file_object(id)`，`ON DELETE SET NULL`
- `chk_rp_segment_document_order`：`document_order_start <= document_order_end`

### `rp_template_element`

**作用：** 保存从某个模板版本中解析出的可绑定元素，是前端模板元素树和拖拽绑定的目标清单。

| 字段 | 类型 | 允许空 | 默认值 | 键/关联 | 详细作用 |
|---|---|---:|---|---|---|
| `id` | `BIGINT UNSIGNED` | 否 | — | PK | 模板元素主键。 由数据库自增生成。 |
| `template_version_id` | `BIGINT UNSIGNED` | 否 | — | UK:uk_rp_template_element<br>FK:fk_rp_te_version | 关联具体、不可变的模板版本。 |
| `segment_id` | `BIGINT UNSIGNED` | 是 | `NULL` | FK:fk_rp_element_segment | 所属最内层片段；全局元素或尚未重扫的历史元素为空。 |
| `element_key` | `VARCHAR(255)` | 否 | — | UK:uk_rp_template_element | 模板版本内稳定定位键，例如内容控件 Tag、书签、占位符或 Chart 关系 ID。 |
| `element_type` | `VARCHAR(32)` | 否 | — | — | 模板元素类型，例如文本、表格、图表、图片、重复块或条件块。 |
| `locator_type` | `VARCHAR(32)` | 否 | — | — | 元素在 DOCX 中的定位方式。 |
| `display_name` | `VARCHAR(255)` | 是 | `NULL` | — | 元素在前端配置界面的显示名称。 |
| `locator_json` | `JSON` | 否 | — | — | 元素在 Word/OpenXML 文档中的完整定位信息。 |
| `binding_schema_json` | `JSON` | 是 | `NULL` | — | 元素允许绑定的数据形状和属性定义。 |
| `default_value_json` | `JSON` | 是 | `NULL` | — | 元素未绑定或数据为空时的模板默认值。 |
| `is_required` | `TINYINT(1)` | 否 | `0` | — | 是否为必须绑定或必须有值的元素。 |
| `sort_no` | `INT` | 否 | `0` | — | 展示或执行顺序。 |
| `segment_local_order` | `INT UNSIGNED` | 否 | `0` | idx_rp_element_segment | 元素在所属片段内的文档顺序。 |
| `parse_status` | `VARCHAR(32)` | 否 | `'VALID'` | — | 元素解析结果状态。 |
| `parse_message` | `VARCHAR(1000)` | 是 | `NULL` | — | 元素解析警告、暂不支持原因或错误说明。 |
| `created_at` | `DATETIME(3)` | 否 | `CURRENT_TIMESTAMP(3)` | — | 记录创建时间，使用毫秒精度。 |

**主键：** `id`

**唯一约束：**

- `uk_rp_template_element`：(`template_version_id`, `element_key`)

**普通索引：**

- `idx_rp_element_type`：(`template_version_id`, `element_type`)
- `idx_rp_element_segment`：(`segment_id`, `segment_local_order`)

**外键关联：**

- `fk_rp_te_version`：(`template_version_id`) → `rp_template_version`(`id`)；策略：`ON DELETE CASCADE`。
- `fk_rp_element_segment`：(`segment_id`) → `rp_template_segment`(`id`)；策略：`ON DELETE SET NULL`。


## 8. 项目与章节域

### `rp_project`

**作用：** 表示一份报告项目，保存当前项目状态、主模板版本、全局上下文版本和最终组装策略。

| 字段 | 类型 | 允许空 | 默认值 | 键/关联 | 详细作用 |
|---|---|---:|---|---|---|
| `id` | `BIGINT UNSIGNED` | 否 | — | PK | 表记录主键。 由数据库自增生成。 |
| `project_code` | `VARCHAR(64)` | 否 | — | UK:uk_rp_project_code | 项目稳定业务编码，供接口、目录和外部系统引用。 |
| `project_name` | `VARCHAR(255)` | 否 | — | — | 报告项目名称。 |
| `description` | `TEXT` | 是 | `NULL` | — | 业务描述或备注。 |
| `project_status` | `VARCHAR(32)` | 否 | `'DRAFT'` | — | 项目当前生命周期状态。 |
| `master_template_version_id` | `BIGINT UNSIGNED` | 是 | `NULL` | FK:fk_rp_project_master_tv | 项目选用的主控模板具体版本 ID。 |
| `current_context_version_no` | `INT UNSIGNED` | 否 | `0` | — | 项目当前使用的全局上下文版本号。 |
| `assembly_config_json` | `JSON` | 是 | `NULL` | — | 最终组装配置，例如目录、分页、页眉页脚、样式与编号冲突策略。 |
| `created_by` | `BIGINT UNSIGNED` | 否 | — | 逻辑用户关联 | 创建人或上传人用户 ID，逻辑关联现有用户系统（如 RuoYi `sys_user.user_id`），不建立物理外键。 |
| `created_at` | `DATETIME(3)` | 否 | `CURRENT_TIMESTAMP(3)` | — | 记录创建时间，使用毫秒精度。 |
| `updated_by` | `BIGINT UNSIGNED` | 是 | `NULL` | 逻辑用户关联 | 最后修改人用户 ID，逻辑关联现有用户系统。 |
| `updated_at` | `DATETIME(3)` | 否 | `CURRENT_TIMESTAMP(3)` | — | 记录最后更新时间，由 MySQL 自动刷新。 |
| `row_version` | `INT UNSIGNED` | 否 | `0` | — | 乐观锁版本号。更新时必须携带旧版本并自增，防止并发覆盖。 |
| `deleted_at` | `DATETIME(3)` | 是 | `NULL` | — | 软删除时间；为空表示未删除。 |

**主键：** `id`

**唯一约束：**

- `uk_rp_project_code`：(`project_code`)

**普通索引：**

- `idx_rp_project_status`：(`project_status`, `updated_at`)

**外键关联：**

- `fk_rp_project_master_tv`：(`master_template_version_id`) → `rp_template_version`(`id`)；策略：`ON DELETE SET NULL`。

### `rp_project_member`

**作用：** 保存用户在具体项目中的角色与成员状态，实现项目级权限控制。

| 字段 | 类型 | 允许空 | 默认值 | 键/关联 | 详细作用 |
|---|---|---:|---|---|---|
| `project_id` | `BIGINT UNSIGNED` | 否 | — | PK<br>FK:fk_rp_member_project | 项目成员所属项目 ID。 |
| `user_id` | `BIGINT UNSIGNED` | 否 | — | PK<br>逻辑用户关联 | 成员用户 ID，逻辑关联现有用户系统，不建立物理外键。 |
| `project_role` | `VARCHAR(32)` | 否 | — | — | 用户在该项目中的角色。 |
| `member_status` | `VARCHAR(32)` | 否 | `'ACTIVE'` | — | 项目成员状态。 |
| `joined_at` | `DATETIME(3)` | 否 | `CURRENT_TIMESTAMP(3)` | — | 用户加入项目时间。 |
| `updated_at` | `DATETIME(3)` | 否 | `CURRENT_TIMESTAMP(3)` | — | 记录最后更新时间，由 MySQL 自动刷新。 |

**主键：** `project_id`, `user_id`

**唯一约束：**
 无。

**普通索引：**

- `idx_rp_member_user`：(`user_id`, `member_status`)

**外键关联：**

- `fk_rp_member_project`：(`project_id`) → `rp_project`(`id`)；策略：`ON DELETE CASCADE`。

### `rp_project_context_version`

**作用：** 保存报告年份、单位名称、报告期等项目全局变量的不可变版本。

| 字段 | 类型 | 允许空 | 默认值 | 键/关联 | 详细作用 |
|---|---|---:|---|---|---|
| `id` | `BIGINT UNSIGNED` | 否 | — | PK | 项目全局上下文版本主键。 由数据库自增生成。 |
| `project_id` | `BIGINT UNSIGNED` | 否 | — | UK:uk_rp_project_context<br>FK:fk_rp_context_project | 所属报告项目 ID。 |
| `version_no` | `INT UNSIGNED` | 否 | — | UK:uk_rp_project_context | 该项目全局上下文内部递增的版本号。 |
| `context_status` | `VARCHAR(32)` | 否 | `'DRAFT'` | — | 全局上下文版本状态。 |
| `content_json` | `JSON` | 否 | — | — | 小型结构化数据正文；大型数据应保存为文件对象。 |
| `content_hash` | `CHAR(64)` | 是 | `NULL` | — | 结构化数据内容哈希，用于变更检测和复现。 |
| `change_summary` | `VARCHAR(1000)` | 是 | `NULL` | — | 本版本相对上一版本的变更说明。 |
| `created_by` | `BIGINT UNSIGNED` | 是 | `NULL` | 逻辑用户关联 | 创建人或上传人用户 ID，逻辑关联现有用户系统（如 RuoYi `sys_user.user_id`），不建立物理外键。 |
| `created_at` | `DATETIME(3)` | 否 | `CURRENT_TIMESTAMP(3)` | — | 记录创建时间，使用毫秒精度。 |
| `published_at` | `DATETIME(3)` | 是 | `NULL` | — | 版本或发布记录正式生效时间。 |

**主键：** `id`

**唯一约束：**

- `uk_rp_project_context`：(`project_id`, `version_no`)

**普通索引：**

- `idx_rp_context_status`：(`project_id`, `context_status`)

**外键关联：**

- `fk_rp_context_project`：(`project_id`) → `rp_project`(`id`)；策略：`ON DELETE CASCADE`。

### `rp_chapter`

**作用：** 保存项目章节树、章节当前状态、当前版本和排序信息。该表表示章节的当前工作态，不直接承担历史版本职责。

| 字段 | 类型 | 允许空 | 默认值 | 键/关联 | 详细作用 |
|---|---|---:|---|---|---|
| `id` | `BIGINT UNSIGNED` | 否 | — | PK | 表记录主键。 由数据库自增生成。 |
| `project_id` | `BIGINT UNSIGNED` | 否 | — | UK:uk_rp_chapter_code<br>FK:fk_rp_chapter_project | 所属报告项目 ID。 |
| `parent_id` | `BIGINT UNSIGNED` | 是 | `NULL` | FK:fk_rp_chapter_parent | 父章节 ID；为空表示项目根级章节。 |
| `chapter_code` | `VARCHAR(64)` | 否 | — | UK:uk_rp_chapter_code | 项目内稳定章节编码，不应直接使用可变标题作为标识。 |
| `current_title` | `VARCHAR(255)` | 否 | — | — | 章节当前显示标题。正式发布时以章节版本中的标题快照为准。 |
| `level_no` | `SMALLINT UNSIGNED` | 否 | `1` | — | 章节层级缓存，便于查询和前端展示。 |
| `sort_key` | `DECIMAL(20,10)` | 否 | `1000.0000000000` | — | 同级章节排序键。拖拽时可插入前后值之间，减少大范围重排。 |
| `workflow_status` | `VARCHAR(32)` | 否 | `'PENDING'` | — | 章节当前工作流状态。 |
| `current_revision_id` | `BIGINT UNSIGNED` | 是 | `NULL` | FK:fk_rp_chapter_current_revision | 章节当前生效或推荐的章节版本 ID。 |
| `owner_user_id` | `BIGINT UNSIGNED` | 是 | `NULL` | 逻辑用户关联 | 章节负责人用户 ID，逻辑关联现有用户系统。 |
| `is_enabled` | `TINYINT(1)` | 否 | `1` | — | 章节是否参与当前项目配置和默认生成。 |
| `created_by` | `BIGINT UNSIGNED` | 是 | `NULL` | 逻辑用户关联 | 创建人或上传人用户 ID，逻辑关联现有用户系统（如 RuoYi `sys_user.user_id`），不建立物理外键。 |
| `created_at` | `DATETIME(3)` | 否 | `CURRENT_TIMESTAMP(3)` | — | 记录创建时间，使用毫秒精度。 |
| `updated_by` | `BIGINT UNSIGNED` | 是 | `NULL` | 逻辑用户关联 | 最后修改人用户 ID，逻辑关联现有用户系统。 |
| `updated_at` | `DATETIME(3)` | 否 | `CURRENT_TIMESTAMP(3)` | — | 记录最后更新时间，由 MySQL 自动刷新。 |
| `row_version` | `INT UNSIGNED` | 否 | `0` | — | 乐观锁版本号。更新时必须携带旧版本并自增，防止并发覆盖。 |
| `deleted_at` | `DATETIME(3)` | 是 | `NULL` | — | 软删除时间；为空表示未删除。 |

**主键：** `id`

**唯一约束：**

- `uk_rp_chapter_code`：(`project_id`, `chapter_code`)

**普通索引：**

- `idx_rp_chapter_tree`：(`project_id`, `parent_id`, `sort_key`)
- `idx_rp_chapter_status`：(`project_id`, `workflow_status`)

**外键关联：**

- `fk_rp_chapter_project`：(`project_id`) → `rp_project`(`id`)；策略：`ON DELETE CASCADE`。
- `fk_rp_chapter_parent`：(`parent_id`) → `rp_chapter`(`id`)；策略：`ON DELETE RESTRICT`。
- `fk_rp_chapter_current_revision`：(`current_revision_id`) → `rp_chapter_revision`(`id`)；策略：`ON DELETE SET NULL`。

### `rp_chapter_revision`

**作用：** 保存章节的不可变版本，固定章节标题、模板版本、绑定版本、全局上下文版本和章节设置。

| 字段 | 类型 | 允许空 | 默认值 | 键/关联 | 详细作用 |
|---|---|---:|---|---|---|
| `id` | `BIGINT UNSIGNED` | 否 | — | PK | 章节版本主键。 由数据库自增生成。 |
| `chapter_id` | `BIGINT UNSIGNED` | 否 | — | UK:uk_rp_chapter_revision<br>FK:fk_rp_cr_chapter | 关联报告章节。 |
| `revision_no` | `INT UNSIGNED` | 否 | — | UK:uk_rp_chapter_revision | 章节内部递增版本号。 |
| `revision_status` | `VARCHAR(32)` | 否 | `'DRAFT'` | — | 章节版本状态。 |
| `title_snapshot` | `VARCHAR(255)` | 否 | — | — | 发布该章节版本时冻结的章节标题。 |
| `template_version_id` | `BIGINT UNSIGNED` | 否 | — | FK:fk_rp_cr_template_version | 关联具体、不可变的模板版本。 |
| `binding_set_id` | `BIGINT UNSIGNED` | 是 | `NULL` | FK:fk_rp_cr_binding_set | 关联该章节版本使用的绑定配置版本。 |
| `context_version_no` | `INT UNSIGNED` | 否 | `0` | — | 该章节版本基于的项目全局上下文版本号。 |
| `settings_json` | `JSON` | 是 | `NULL` | — | 章节级分页、纸张方向、页眉页脚、目录和组装设置。 |
| `revision_hash` | `CHAR(64)` | 是 | `NULL` | — | 章节版本完整输入指纹，用于缓存、复现和增量生成。 |
| `change_summary` | `VARCHAR(1000)` | 是 | `NULL` | — | 本版本相对上一版本的变更说明。 |
| `created_by` | `BIGINT UNSIGNED` | 是 | `NULL` | 逻辑用户关联 | 创建人或上传人用户 ID，逻辑关联现有用户系统（如 RuoYi `sys_user.user_id`），不建立物理外键。 |
| `created_at` | `DATETIME(3)` | 否 | `CURRENT_TIMESTAMP(3)` | — | 记录创建时间，使用毫秒精度。 |
| `published_by` | `BIGINT UNSIGNED` | 是 | `NULL` | 逻辑用户关联 | 发布操作人用户 ID，逻辑关联现有用户系统。 |
| `published_at` | `DATETIME(3)` | 是 | `NULL` | — | 版本或发布记录正式生效时间。 |

**主键：** `id`

**唯一约束：**

- `uk_rp_chapter_revision`：(`chapter_id`, `revision_no`)

**普通索引：**

- `idx_rp_chapter_revision_status`：(`chapter_id`, `revision_status`)

**外键关联：**

- `fk_rp_cr_chapter`：(`chapter_id`) → `rp_chapter`(`id`)；策略：`ON DELETE CASCADE`。
- `fk_rp_cr_template_version`：(`template_version_id`) → `rp_template_version`(`id`)；策略：`ON DELETE RESTRICT`。
- `fk_rp_cr_binding_set`：(`binding_set_id`) → `rp_binding_set`(`id`)；策略：`ON DELETE RESTRICT`。

### `rp_chapter_lock`

**作用：** 保存章节编辑租约锁。通过令牌、心跳和过期时间避免永久锁，并配合章节乐观锁防止覆盖。

| 字段 | 类型 | 允许空 | 默认值 | 键/关联 | 详细作用 |
|---|---|---:|---|---|---|
| `chapter_id` | `BIGINT UNSIGNED` | 否 | — | PK<br>FK:fk_rp_lock_chapter | 被锁定章节 ID，也是该表主键，因此一个章节同时只能存在一条有效租约记录。 |
| `lock_token` | `CHAR(36)` | 否 | — | UK:uk_rp_chapter_lock_token | 客户端持有的唯一租约令牌，续租和解锁时必须匹配。 |
| `owner_user_id` | `BIGINT UNSIGNED` | 否 | — | 逻辑用户关联 | 当前锁持有人用户 ID，逻辑关联现有用户系统。 |
| `lock_type` | `VARCHAR(32)` | 否 | `'EDIT'` | — | 锁定业务类型，例如编辑、绑定或审核。 |
| `acquired_at` | `DATETIME(3)` | 否 | `CURRENT_TIMESTAMP(3)` | — | 首次成功获取锁的时间。 |
| `heartbeat_at` | `DATETIME(3)` | 否 | `CURRENT_TIMESTAMP(3)` | — | 锁持有客户端最近一次心跳时间。 |
| `expires_at` | `DATETIME(3)` | 否 | — | — | 租约、快照或临时产物失效时间。 |
| `lock_version` | `INT UNSIGNED` | 否 | `0` | — | 锁记录版本号，用于锁状态并发控制。 |

**主键：** `chapter_id`

**唯一约束：**

- `uk_rp_chapter_lock_token`：(`lock_token`)

**普通索引：**

- `idx_rp_chapter_lock_expire`：(`expires_at`)
- `idx_rp_chapter_lock_owner`：(`owner_user_id`, `expires_at`)

**外键关联：**

- `fk_rp_lock_chapter`：(`chapter_id`) → `rp_chapter`(`id`)；策略：`ON DELETE CASCADE`。


## 9. 数据与绑定域

### `rp_data_connection`

**作用：** 保存数据库、API、SFTP 等数据连接的非敏感配置以及密钥引用。

| 字段 | 类型 | 允许空 | 默认值 | 键/关联 | 详细作用 |
|---|---|---:|---|---|---|
| `id` | `BIGINT UNSIGNED` | 否 | — | PK | 表记录主键。 由数据库自增生成。 |
| `project_id` | `BIGINT UNSIGNED` | 是 | `NULL` | FK:fk_rp_connection_project | 所属报告项目 ID。 |
| `connection_name` | `VARCHAR(255)` | 否 | — | — | 数据连接显示名称。 |
| `connection_type` | `VARCHAR(32)` | 否 | — | — | 连接类型，例如 MySQL、PostgreSQL、API、SFTP 或本地。 |
| `config_json` | `JSON` | 否 | — | — | 连接地址、端口、数据库名、API 基础地址等非敏感配置。 |
| `credential_ref` | `VARCHAR(255)` | 是 | `NULL` | — | 密钥中心、Kubernetes Secret 或服务端加密配置中的凭据引用。 |
| `connection_status` | `VARCHAR(32)` | 否 | `'ACTIVE'` | — | 数据连接状态。 |
| `last_tested_at` | `DATETIME(3)` | 是 | `NULL` | — | 最近一次连接测试时间。 |
| `last_test_result` | `JSON` | 是 | `NULL` | — | 最近一次连接测试结果、耗时和错误摘要。 |
| `created_by` | `BIGINT UNSIGNED` | 是 | `NULL` | 逻辑用户关联 | 创建人或上传人用户 ID，逻辑关联现有用户系统（如 RuoYi `sys_user.user_id`），不建立物理外键。 |
| `created_at` | `DATETIME(3)` | 否 | `CURRENT_TIMESTAMP(3)` | — | 记录创建时间，使用毫秒精度。 |
| `updated_at` | `DATETIME(3)` | 否 | `CURRENT_TIMESTAMP(3)` | — | 记录最后更新时间，由 MySQL 自动刷新。 |
| `row_version` | `INT UNSIGNED` | 否 | `0` | — | 乐观锁版本号。更新时必须携带旧版本并自增，防止并发覆盖。 |
| `deleted_at` | `DATETIME(3)` | 是 | `NULL` | — | 软删除时间；为空表示未删除。 |

**主键：** `id`

**唯一约束：**
 无。

**普通索引：**

- `idx_rp_connection_project`：(`project_id`, `connection_status`)

**外键关联：**

- `fk_rp_connection_project`：(`project_id`) → `rp_project`(`id`)；策略：`ON DELETE CASCADE`。

### `rp_data_source`

**作用：** 定义一个业务数据源及其刷新方式。数据源描述“从哪里、如何取数”，不直接代表某次实际数据。

| 字段 | 类型 | 允许空 | 默认值 | 键/关联 | 详细作用 |
|---|---|---:|---|---|---|
| `id` | `BIGINT UNSIGNED` | 否 | — | PK | 表记录主键。 由数据库自增生成。 |
| `project_id` | `BIGINT UNSIGNED` | 否 | — | UK:uk_rp_data_source_code<br>FK:fk_rp_ds_project | 所属报告项目 ID。 |
| `connection_id` | `BIGINT UNSIGNED` | 是 | `NULL` | FK:fk_rp_ds_connection | 关联数据连接；文件或手工数据源可为空。 |
| `source_code` | `VARCHAR(64)` | 否 | — | UK:uk_rp_data_source_code | 项目内稳定数据源编码。 |
| `source_name` | `VARCHAR(255)` | 否 | — | — | 数据源显示名称。 |
| `source_type` | `VARCHAR(32)` | 否 | — | — | 数据源类型，例如 JSON、Excel、CSV、API、数据库或手工录入。 |
| `source_status` | `VARCHAR(32)` | 否 | `'ACTIVE'` | — | 数据源状态。 |
| `config_json` | `JSON` | 是 | `NULL` | — | 数据源配置，例如 Excel Sheet、API 路径、请求参数映射或受控数据库查询模板。 |
| `refresh_mode` | `VARCHAR(32)` | 否 | `'MANUAL'` | — | 数据刷新方式：手动、生成前刷新或定时刷新。 |
| `schema_json` | `JSON` | 是 | `NULL` | — | 数据源预期结构或本次快照的实际结构。 |
| `created_by` | `BIGINT UNSIGNED` | 是 | `NULL` | 逻辑用户关联 | 创建人或上传人用户 ID，逻辑关联现有用户系统（如 RuoYi `sys_user.user_id`），不建立物理外键。 |
| `created_at` | `DATETIME(3)` | 否 | `CURRENT_TIMESTAMP(3)` | — | 记录创建时间，使用毫秒精度。 |
| `updated_by` | `BIGINT UNSIGNED` | 是 | `NULL` | 逻辑用户关联 | 最后修改人用户 ID，逻辑关联现有用户系统。 |
| `updated_at` | `DATETIME(3)` | 否 | `CURRENT_TIMESTAMP(3)` | — | 记录最后更新时间，由 MySQL 自动刷新。 |
| `row_version` | `INT UNSIGNED` | 否 | `0` | — | 乐观锁版本号。更新时必须携带旧版本并自增，防止并发覆盖。 |
| `deleted_at` | `DATETIME(3)` | 是 | `NULL` | — | 软删除时间；为空表示未删除。 |

**主键：** `id`

**唯一约束：**

- `uk_rp_data_source_code`：(`project_id`, `source_code`)

**普通索引：**

- `idx_rp_data_source_status`：(`project_id`, `source_status`)
- `idx_rp_data_source_connection`：(`connection_id`)

**外键关联：**

- `fk_rp_ds_project`：(`project_id`) → `rp_project`(`id`)；策略：`ON DELETE CASCADE`。
- `fk_rp_ds_connection`：(`connection_id`) → `rp_data_connection`(`id`)；策略：`ON DELETE SET NULL`。

### `rp_data_snapshot`

**作用：** 保存数据源在某次抓取时得到的不可变数据快照，保证生成报告时能够复现。

| 字段 | 类型 | 允许空 | 默认值 | 键/关联 | 详细作用 |
|---|---|---:|---|---|---|
| `id` | `BIGINT UNSIGNED` | 否 | — | PK | 数据快照主键。 由数据库自增生成。 |
| `data_source_id` | `BIGINT UNSIGNED` | 否 | — | UK:uk_rp_data_snapshot<br>FK:fk_rp_snapshot_source | 关联逻辑数据源。 |
| `snapshot_no` | `BIGINT UNSIGNED` | 否 | — | UK:uk_rp_data_snapshot | 数据源内部递增快照号。 |
| `snapshot_status` | `VARCHAR(32)` | 否 | `'CAPTURING'` | — | 数据快照抓取和可用状态。 |
| `content_json` | `JSON` | 是 | `NULL` | — | 小型快照数据正文；大型数据改由 `file_object_id` 指向对象存储文件。 |
| `file_object_id` | `BIGINT UNSIGNED` | 是 | `NULL` | FK:fk_rp_snapshot_file | 大型快照文件对象 ID；与 `content_json` 至少有一种可承载实际数据。 |
| `schema_json` | `JSON` | 是 | `NULL` | — | 数据源预期结构或本次快照的实际结构。 |
| `content_hash` | `CHAR(64)` | 是 | `NULL` | — | 结构化数据内容哈希，用于变更检测和复现。 |
| `row_count` | `BIGINT UNSIGNED` | 是 | `NULL` | — | 快照包含的业务记录数。 |
| `source_watermark` | `VARCHAR(255)` | 是 | `NULL` | — | 源系统水位，例如业务时间、数据库 SCN、文件 ETag 或接口版本。 |
| `captured_at` | `DATETIME(3)` | 否 | `CURRENT_TIMESTAMP(3)` | — | 数据快照实际抓取时间。 |
| `expires_at` | `DATETIME(3)` | 是 | `NULL` | — | 租约、快照或临时产物失效时间。 |
| `error_message` | `VARCHAR(2000)` | 是 | `NULL` | — | 失败原因或错误摘要。 |
| `created_by` | `BIGINT UNSIGNED` | 是 | `NULL` | 逻辑用户关联 | 创建人或上传人用户 ID，逻辑关联现有用户系统（如 RuoYi `sys_user.user_id`），不建立物理外键。 |
| `created_at` | `DATETIME(3)` | 否 | `CURRENT_TIMESTAMP(3)` | — | 记录创建时间，使用毫秒精度。 |

**主键：** `id`

**唯一约束：**

- `uk_rp_data_snapshot`：(`data_source_id`, `snapshot_no`)

**普通索引：**

- `idx_rp_snapshot_status_time`：(`data_source_id`, `snapshot_status`, `captured_at`)
- `idx_rp_snapshot_hash`：(`content_hash`)

**外键关联：**

- `fk_rp_snapshot_source`：(`data_source_id`) → `rp_data_source`(`id`)；策略：`ON DELETE RESTRICT`。
- `fk_rp_snapshot_file`：(`file_object_id`) → `rp_file_object`(`id`)；策略：`ON DELETE RESTRICT`。

### `rp_data_field`

**作用：** 保存数据快照解析出的字段目录，供前端展示字段树并进行拖拽绑定。

| 字段 | 类型 | 允许空 | 默认值 | 键/关联 | 详细作用 |
|---|---|---:|---|---|---|
| `id` | `BIGINT UNSIGNED` | 否 | — | PK | 数据字段目录主键。 由数据库自增生成。 |
| `snapshot_id` | `BIGINT UNSIGNED` | 否 | — | UK:uk_rp_data_field<br>FK:fk_rp_field_snapshot | 关联具体数据快照。 |
| `field_path` | `VARCHAR(1024)` | 否 | — | UK:uk_rp_data_field | 字段完整路径，例如 JSONPath、Excel 列路径或对象属性路径。 |
| `field_name` | `VARCHAR(255)` | 否 | — | — | 字段显示名称。 |
| `data_type` | `VARCHAR(32)` | 否 | — | — | 字段规范化数据类型。 |
| `is_array` | `TINYINT(1)` | 否 | `0` | — | 字段是否表示集合或数组。 |
| `is_nullable` | `TINYINT(1)` | 否 | `1` | — | 字段值是否允许为空。 |
| `sample_value_json` | `JSON` | 是 | `NULL` | — | 字段样例值，供前端预览和绑定判断。 |
| `display_order` | `INT` | 否 | `0` | — | 字段树中的显示顺序。 |
| `created_at` | `DATETIME(3)` | 否 | `CURRENT_TIMESTAMP(3)` | — | 记录创建时间，使用毫秒精度。 |

**主键：** `id`

**唯一约束：**

- `uk_rp_data_field`：(`snapshot_id`, `field_path(512)`)

**普通索引：**

- `idx_rp_data_field_type`：(`snapshot_id`, `data_type`)

**外键关联：**

- `fk_rp_field_snapshot`：(`snapshot_id`) → `rp_data_snapshot`(`id`)；策略：`ON DELETE CASCADE`。

### `rp_binding_set`

**作用：** 保存一个章节完整绑定配置的版本头，固定模板版本并记录校验结果。

| 字段 | 类型 | 允许空 | 默认值 | 键/关联 | 详细作用 |
|---|---|---:|---|---|---|
| `id` | `BIGINT UNSIGNED` | 否 | — | PK | 绑定配置版本主键。 由数据库自增生成。 |
| `chapter_id` | `BIGINT UNSIGNED` | 否 | — | UK:uk_rp_binding_set<br>FK:fk_rp_bs_chapter | 关联报告章节。 |
| `version_no` | `INT UNSIGNED` | 否 | — | UK:uk_rp_binding_set | 该章节绑定配置内部递增的版本号。 |
| `template_version_id` | `BIGINT UNSIGNED` | 否 | — | FK:fk_rp_bs_template_version | 关联具体、不可变的模板版本。 |
| `binding_status` | `VARCHAR(32)` | 否 | `'DRAFT'` | — | 绑定配置版本状态。 |
| `validation_status` | `VARCHAR(32)` | 否 | `'NOT_VALIDATED'` | — | 绑定配置整体校验状态。 |
| `validation_result_json` | `JSON` | 是 | `NULL` | — | 绑定校验的错误、警告和统计结果。 |
| `change_summary` | `VARCHAR(1000)` | 是 | `NULL` | — | 本版本相对上一版本的变更说明。 |
| `created_by` | `BIGINT UNSIGNED` | 是 | `NULL` | 逻辑用户关联 | 创建人或上传人用户 ID，逻辑关联现有用户系统（如 RuoYi `sys_user.user_id`），不建立物理外键。 |
| `created_at` | `DATETIME(3)` | 否 | `CURRENT_TIMESTAMP(3)` | — | 记录创建时间，使用毫秒精度。 |
| `published_by` | `BIGINT UNSIGNED` | 是 | `NULL` | 逻辑用户关联 | 发布操作人用户 ID，逻辑关联现有用户系统。 |
| `published_at` | `DATETIME(3)` | 是 | `NULL` | — | 版本或发布记录正式生效时间。 |

**主键：** `id`

**唯一约束：**

- `uk_rp_binding_set`：(`chapter_id`, `version_no`)

**普通索引：**

- `idx_rp_binding_status`：(`chapter_id`, `binding_status`)

**外键关联：**

- `fk_rp_bs_chapter`：(`chapter_id`) → `rp_chapter`(`id`)；策略：`ON DELETE CASCADE`。
- `fk_rp_bs_template_version`：(`template_version_id`) → `rp_template_version`(`id`)；策略：`ON DELETE RESTRICT`。

### `rp_binding_item`

**作用：** 保存一条具体绑定规则，将模板元素的某个属性绑定到项目上下文、数据字段、常量或受控表达式。

| 字段 | 类型 | 允许空 | 默认值 | 键/关联 | 详细作用 |
|---|---|---:|---|---|---|
| `id` | `BIGINT UNSIGNED` | 否 | — | PK | 绑定明细主键。 由数据库自增生成。 |
| `binding_set_id` | `BIGINT UNSIGNED` | 否 | — | UK:uk_rp_binding_target<br>FK:fk_rp_bi_set | 关联该章节版本使用的绑定配置版本。 |
| `template_element_id` | `BIGINT UNSIGNED` | 否 | — | UK:uk_rp_binding_target<br>FK:fk_rp_bi_element | 关联模板版本中的具体可绑定元素。 |
| `target_property` | `VARCHAR(255)` | 否 | `'$'` | UK:uk_rp_binding_target | 模板元素内部目标属性，例如文本、分类轴、系列值或图片地址。 |
| `source_kind` | `VARCHAR(32)` | 否 | — | — | 绑定来源类型：项目上下文、数据源、常量或受控表达式。 |
| `data_source_id` | `BIGINT UNSIGNED` | 是 | `NULL` | FK:fk_rp_bi_source | 关联逻辑数据源。 |
| `source_path` | `VARCHAR(1024)` | 是 | `NULL` | — | 来源字段路径或项目上下文路径。 |
| `constant_value_json` | `JSON` | 是 | `NULL` | — | 来源为常量时保存的值。 |
| `transform_config_json` | `JSON` | 是 | `NULL` | — | 受控数据转换函数链配置，不允许存放任意可执行脚本。 |
| `format_config_json` | `JSON` | 是 | `NULL` | — | 日期、数字、表格、图表和图片等格式配置。 |
| `fallback_value_json` | `JSON` | 是 | `NULL` | — | 数据缺失、为空或转换失败时的回退值。 |
| `is_required` | `TINYINT(1)` | 否 | `0` | — | 是否为必须绑定或必须有值的元素。 |
| `sort_no` | `INT` | 否 | `0` | — | 展示或执行顺序。 |
| `created_at` | `DATETIME(3)` | 否 | `CURRENT_TIMESTAMP(3)` | — | 记录创建时间，使用毫秒精度。 |
| `updated_at` | `DATETIME(3)` | 否 | `CURRENT_TIMESTAMP(3)` | — | 记录最后更新时间，由 MySQL 自动刷新。 |

**主键：** `id`

**唯一约束：**

- `uk_rp_binding_target`：(`binding_set_id`, `template_element_id`, `target_property`)

**普通索引：**

- `idx_rp_binding_source`：(`data_source_id`)

**外键关联：**

- `fk_rp_bi_set`：(`binding_set_id`) → `rp_binding_set`(`id`)；策略：`ON DELETE CASCADE`。
- `fk_rp_bi_element`：(`template_element_id`) → `rp_template_element`(`id`)；策略：`ON DELETE RESTRICT`。
- `fk_rp_bi_source`：(`data_source_id`) → `rp_data_source`(`id`)；策略：`ON DELETE RESTRICT`。


## 10. 生成与发布域

### `rp_generation_job`

**作用：** 保存一次章节预览、项目预览或正式生成任务的总状态和固定输入清单。

| 字段 | 类型 | 允许空 | 默认值 | 键/关联 | 详细作用 |
|---|---|---:|---|---|---|
| `id` | `BIGINT UNSIGNED` | 否 | — | PK | 生成任务主键。 由数据库自增生成。 |
| `project_id` | `BIGINT UNSIGNED` | 否 | — | FK:fk_rp_job_project | 所属报告项目 ID。 |
| `job_type` | `VARCHAR(32)` | 否 | — | — | 生成任务类型：章节预览、项目预览或正式生成。 |
| `job_status` | `VARCHAR(32)` | 否 | `'QUEUED'` | — | 生成任务当前状态。 |
| `requested_by` | `BIGINT UNSIGNED` | 是 | `NULL` | 逻辑用户关联 | 发起生成任务的用户 ID，逻辑关联现有用户系统。 |
| `context_version_no` | `INT UNSIGNED` | 否 | `0` | — | 该章节版本基于的项目全局上下文版本号。 |
| `manifest_json` | `JSON` | 否 | — | — | 任务创建时冻结的章节版本、模板版本、绑定版本、顺序和运行参数清单。 |
| `progress_percent` | `DECIMAL(5,2)` | 否 | `0.00` | — | 任务进度百分比。 |
| `total_chapters` | `INT UNSIGNED` | 否 | `0` | — | 任务章节总数。 |
| `completed_chapters` | `INT UNSIGNED` | 否 | `0` | — | 已成功完成章节数。 |
| `failed_chapters` | `INT UNSIGNED` | 否 | `0` | — | 失败章节数。 |
| `retry_of_job_id` | `BIGINT UNSIGNED` | 是 | `NULL` | FK:fk_rp_job_retry | 当前任务所重试的原任务 ID。 |
| `error_code` | `VARCHAR(64)` | 是 | `NULL` | — | 机器可识别错误码。 |
| `error_message` | `VARCHAR(2000)` | 是 | `NULL` | — | 失败原因或错误摘要。 |
| `queued_at` | `DATETIME(3)` | 否 | `CURRENT_TIMESTAMP(3)` | — | 任务进入队列时间。 |
| `started_at` | `DATETIME(3)` | 是 | `NULL` | — | 任务或子任务开始执行时间。 |
| `finished_at` | `DATETIME(3)` | 是 | `NULL` | — | 任务或子任务结束时间。 |
| `created_at` | `DATETIME(3)` | 否 | `CURRENT_TIMESTAMP(3)` | — | 记录创建时间，使用毫秒精度。 |
| `row_version` | `INT UNSIGNED` | 否 | `0` | — | 乐观锁版本号。更新时必须携带旧版本并自增，防止并发覆盖。 |

**主键：** `id`

**唯一约束：**
 无。

**普通索引：**

- `idx_rp_job_project_status`：(`project_id`, `job_status`, `created_at`)
- `idx_rp_job_retry`：(`retry_of_job_id`)

**外键关联：**

- `fk_rp_job_project`：(`project_id`) → `rp_project`(`id`)；策略：`ON DELETE RESTRICT`。
- `fk_rp_job_retry`：(`retry_of_job_id`) → `rp_generation_job`(`id`)；策略：`ON DELETE SET NULL`。

### `rp_generation_job_snapshot`

**作用：** 固定某次生成任务实际使用的各数据源快照，避免生成过程中数据发生变化。

| 字段 | 类型 | 允许空 | 默认值 | 键/关联 | 详细作用 |
|---|---|---:|---|---|---|
| `job_id` | `BIGINT UNSIGNED` | 否 | — | PK<br>FK:fk_rp_gjs_job | 使用该数据快照的生成任务 ID。 |
| `data_source_id` | `BIGINT UNSIGNED` | 否 | — | PK<br>FK:fk_rp_gjs_source | 快照所属的数据源 ID，同时用于保证一个任务对一个数据源只选一个快照。 |
| `data_snapshot_id` | `BIGINT UNSIGNED` | 否 | — | FK:fk_rp_gjs_snapshot | 生成任务固定使用的数据快照 ID。 |
| `selected_at` | `DATETIME(3)` | 否 | `CURRENT_TIMESTAMP(3)` | — | 生成任务选择该数据快照的时间。 |

**主键：** `job_id`, `data_source_id`

**唯一约束：**
 无。

**普通索引：**

- `idx_rp_job_snapshot_id`：(`data_snapshot_id`)

**外键关联：**

- `fk_rp_gjs_job`：(`job_id`) → `rp_generation_job`(`id`)；策略：`ON DELETE CASCADE`。
- `fk_rp_gjs_source`：(`data_source_id`) → `rp_data_source`(`id`)；策略：`ON DELETE RESTRICT`。
- `fk_rp_gjs_snapshot`：(`data_snapshot_id`) → `rp_data_snapshot`(`id`)；策略：`ON DELETE RESTRICT`。

### `rp_generation_job_chapter`

**作用：** 保存生成任务中每个章节的子任务状态、重试次数和输出产物。

| 字段 | 类型 | 允许空 | 默认值 | 键/关联 | 详细作用 |
|---|---|---:|---|---|---|
| `id` | `BIGINT UNSIGNED` | 否 | — | PK | 生成章节子任务主键。 由数据库自增生成。 |
| `job_id` | `BIGINT UNSIGNED` | 否 | — | UK:uk_rp_job_chapter<br>FK:fk_rp_gjc_job | 关联生成任务。 |
| `chapter_id` | `BIGINT UNSIGNED` | 否 | — | UK:uk_rp_job_chapter<br>FK:fk_rp_gjc_chapter | 该子任务处理的逻辑章节。 |
| `chapter_revision_id` | `BIGINT UNSIGNED` | 否 | — | FK:fk_rp_gjc_revision | 关联不可变章节版本。 |
| `sequence_no` | `INT UNSIGNED` | 否 | — | — | 章节在任务或发布版本中的最终顺序。 |
| `task_status` | `VARCHAR(32)` | 否 | `'QUEUED'` | — | 章节子任务执行状态。 |
| `attempt_no` | `INT UNSIGNED` | 否 | `0` | — | 章节子任务已执行次数。 |
| `output_artifact_id` | `BIGINT UNSIGNED` | 是 | `NULL` | FK:fk_rp_gjc_artifact | 章节子任务成功输出的产物 ID。 |
| `error_code` | `VARCHAR(64)` | 是 | `NULL` | — | 机器可识别错误码。 |
| `error_message` | `VARCHAR(2000)` | 是 | `NULL` | — | 失败原因或错误摘要。 |
| `started_at` | `DATETIME(3)` | 是 | `NULL` | — | 任务或子任务开始执行时间。 |
| `finished_at` | `DATETIME(3)` | 是 | `NULL` | — | 任务或子任务结束时间。 |

**主键：** `id`

**唯一约束：**

- `uk_rp_job_chapter`：(`job_id`, `chapter_id`)

**普通索引：**

- `idx_rp_job_chapter_status`：(`job_id`, `task_status`, `sequence_no`)

**外键关联：**

- `fk_rp_gjc_job`：(`job_id`) → `rp_generation_job`(`id`)；策略：`ON DELETE CASCADE`。
- `fk_rp_gjc_chapter`：(`chapter_id`) → `rp_chapter`(`id`)；策略：`ON DELETE RESTRICT`。
- `fk_rp_gjc_revision`：(`chapter_revision_id`) → `rp_chapter_revision`(`id`)；策略：`ON DELETE RESTRICT`。
- `fk_rp_gjc_artifact`：(`output_artifact_id`) → `rp_artifact`(`id`)；策略：`ON DELETE SET NULL`。

### `rp_artifact`

**作用：** 统一记录渲染和组装产生的 DOCX、PDF、HTML、图片及日志等文件产物。

| 字段 | 类型 | 允许空 | 默认值 | 键/关联 | 详细作用 |
|---|---|---:|---|---|---|
| `id` | `BIGINT UNSIGNED` | 否 | — | PK | 生成产物主键。 由数据库自增生成。 |
| `project_id` | `BIGINT UNSIGNED` | 否 | — | FK:fk_rp_artifact_project | 所属报告项目 ID。 |
| `generation_job_id` | `BIGINT UNSIGNED` | 是 | `NULL` | FK:fk_rp_artifact_job | 产生该文件的生成任务 ID。 |
| `chapter_id` | `BIGINT UNSIGNED` | 是 | `NULL` | FK:fk_rp_artifact_chapter | 章节级产物所属章节；项目级最终产物可为空。 |
| `file_object_id` | `BIGINT UNSIGNED` | 否 | — | FK:fk_rp_artifact_file | 产物对应的统一文件对象 ID。 |
| `artifact_type` | `VARCHAR(32)` | 否 | — | — | 产物类型，例如章节 DOCX、最终 DOCX、PDF、预览 HTML、图片或日志。 |
| `artifact_status` | `VARCHAR(32)` | 否 | `'READY'` | — | 产物创建与可用状态。 |
| `file_format` | `VARCHAR(32)` | 否 | — | — | 产物文件格式。 |
| `page_count` | `INT UNSIGNED` | 是 | `NULL` | — | 文档或产物页数。 |
| `metadata_json` | `JSON` | 是 | `NULL` | — | 文件扩展元数据，例如图片尺寸、页数、编码、文档属性。 |
| `created_by` | `BIGINT UNSIGNED` | 是 | `NULL` | 逻辑用户关联 | 创建人或上传人用户 ID，逻辑关联现有用户系统（如 RuoYi `sys_user.user_id`），不建立物理外键。 |
| `created_at` | `DATETIME(3)` | 否 | `CURRENT_TIMESTAMP(3)` | — | 记录创建时间，使用毫秒精度。 |
| `expires_at` | `DATETIME(3)` | 是 | `NULL` | — | 租约、快照或临时产物失效时间。 |
| `deleted_at` | `DATETIME(3)` | 是 | `NULL` | — | 软删除时间；为空表示未删除。 |

**主键：** `id`

**唯一约束：**
 无。

**普通索引：**

- `idx_rp_artifact_job`：(`generation_job_id`, `artifact_type`)
- `idx_rp_artifact_chapter`：(`chapter_id`, `created_at`)
- `idx_rp_artifact_project`：(`project_id`, `created_at`)

**外键关联：**

- `fk_rp_artifact_project`：(`project_id`) → `rp_project`(`id`)；策略：`ON DELETE RESTRICT`。
- `fk_rp_artifact_job`：(`generation_job_id`) → `rp_generation_job`(`id`)；策略：`ON DELETE SET NULL`。
- `fk_rp_artifact_chapter`：(`chapter_id`) → `rp_chapter`(`id`)；策略：`ON DELETE SET NULL`。
- `fk_rp_artifact_file`：(`file_object_id`) → `rp_file_object`(`id`)；策略：`ON DELETE RESTRICT`。

### `rp_release`

**作用：** 保存正式报告发布记录，关联成功生成任务、最终产物和完整版本清单。

| 字段 | 类型 | 允许空 | 默认值 | 键/关联 | 详细作用 |
|---|---|---:|---|---|---|
| `id` | `BIGINT UNSIGNED` | 否 | — | PK | 正式发布主键。 由数据库自增生成。 |
| `project_id` | `BIGINT UNSIGNED` | 否 | — | UK:uk_rp_release_no<br>FK:fk_rp_release_project | 所属报告项目 ID。 |
| `release_no` | `INT UNSIGNED` | 否 | — | UK:uk_rp_release_no | 项目内递增的正式发布号。 |
| `version_label` | `VARCHAR(64)` | 是 | `NULL` | — | 面向用户的版本标签，例如 v1.0 或 2026-Q3。 |
| `release_status` | `VARCHAR(32)` | 否 | `'DRAFT'` | — | 正式发布状态。 |
| `generation_job_id` | `BIGINT UNSIGNED` | 否 | — | FK:fk_rp_release_job | 产生该文件的生成任务 ID。 |
| `final_artifact_id` | `BIGINT UNSIGNED` | 否 | — | FK:fk_rp_release_artifact | 正式发布对应的最终报告产物 ID。 |
| `manifest_json` | `JSON` | 否 | — | — | 正式版本完整物料清单，固定模板、章节、绑定、上下文、数据快照和程序版本。 |
| `release_notes` | `TEXT` | 是 | `NULL` | — | 正式版本说明。 |
| `created_by` | `BIGINT UNSIGNED` | 是 | `NULL` | 逻辑用户关联 | 创建人或上传人用户 ID，逻辑关联现有用户系统（如 RuoYi `sys_user.user_id`），不建立物理外键。 |
| `created_at` | `DATETIME(3)` | 否 | `CURRENT_TIMESTAMP(3)` | — | 记录创建时间，使用毫秒精度。 |
| `published_by` | `BIGINT UNSIGNED` | 是 | `NULL` | 逻辑用户关联 | 发布操作人用户 ID，逻辑关联现有用户系统。 |
| `published_at` | `DATETIME(3)` | 是 | `NULL` | — | 版本或发布记录正式生效时间。 |

**主键：** `id`

**唯一约束：**

- `uk_rp_release_no`：(`project_id`, `release_no`)

**普通索引：**

- `idx_rp_release_status`：(`project_id`, `release_status`, `created_at`)

**外键关联：**

- `fk_rp_release_project`：(`project_id`) → `rp_project`(`id`)；策略：`ON DELETE RESTRICT`。
- `fk_rp_release_job`：(`generation_job_id`) → `rp_generation_job`(`id`)；策略：`ON DELETE RESTRICT`。
- `fk_rp_release_artifact`：(`final_artifact_id`) → `rp_artifact`(`id`)；策略：`ON DELETE RESTRICT`。

### `rp_release_chapter`

**作用：** 保存正式发布中包含的章节顺序、章节版本、章节产物和数据快照清单。

| 字段 | 类型 | 允许空 | 默认值 | 键/关联 | 详细作用 |
|---|---|---:|---|---|---|
| `id` | `BIGINT UNSIGNED` | 否 | — | PK | 发布章节清单主键。 由数据库自增生成。 |
| `release_id` | `BIGINT UNSIGNED` | 否 | — | UK:uk_rp_release_chapter<br>FK:fk_rp_rc_release | 关联正式发布记录。 |
| `chapter_id` | `BIGINT UNSIGNED` | 否 | — | UK:uk_rp_release_chapter<br>FK:fk_rp_rc_chapter | 关联项目中的逻辑章节。 |
| `chapter_revision_id` | `BIGINT UNSIGNED` | 否 | — | FK:fk_rp_rc_revision | 关联不可变章节版本。 |
| `sequence_no` | `INT UNSIGNED` | 否 | — | — | 章节在任务或发布版本中的最终顺序。 |
| `artifact_id` | `BIGINT UNSIGNED` | 是 | `NULL` | FK:fk_rp_rc_artifact | 关联该发布章节对应的章节产物。 |
| `data_snapshot_manifest_json` | `JSON` | 是 | `NULL` | — | 该发布章节实际使用的数据源与快照清单。 |

**主键：** `id`

**唯一约束：**

- `uk_rp_release_chapter`：(`release_id`, `chapter_id`)

**普通索引：**

- `idx_rp_release_chapter_order`：(`release_id`, `sequence_no`)

**外键关联：**

- `fk_rp_rc_release`：(`release_id`) → `rp_release`(`id`)；策略：`ON DELETE RESTRICT`。
- `fk_rp_rc_chapter`：(`chapter_id`) → `rp_chapter`(`id`)；策略：`ON DELETE RESTRICT`。
- `fk_rp_rc_revision`：(`chapter_revision_id`) → `rp_chapter_revision`(`id`)；策略：`ON DELETE RESTRICT`。
- `fk_rp_rc_artifact`：(`artifact_id`) → `rp_artifact`(`id`)；策略：`ON DELETE SET NULL`。


## 11. 审计域

### `rp_audit_log`

**作用：** 保存不可变的业务审计日志，记录操作人、对象、修改前后内容和请求链路。

| 字段 | 类型 | 允许空 | 默认值 | 键/关联 | 详细作用 |
|---|---|---:|---|---|---|
| `id` | `BIGINT UNSIGNED` | 否 | — | PK | 审计日志主键。 由数据库自增生成。 |
| `project_id` | `BIGINT UNSIGNED` | 是 | `NULL` | — | 相关项目 ID，仅用于审计筛选；故意不建立物理外键。 |
| `actor_user_id` | `BIGINT UNSIGNED` | 是 | `NULL` | 逻辑用户关联 | 审计操作人用户 ID，逻辑关联现有用户系统。 |
| `action_code` | `VARCHAR(64)` | 否 | — | — | 操作动作编码，例如创建、更新、删除、锁定、绑定、校验、生成或发布。 |
| `entity_type` | `VARCHAR(64)` | 否 | — | — | 被操作业务对象类型。 |
| `entity_id` | `BIGINT UNSIGNED` | 是 | `NULL` | — | 被操作业务对象 ID；审计表不建立物理外键。 |
| `request_id` | `VARCHAR(64)` | 是 | `NULL` | — | 一次接口请求或分布式调用链的追踪 ID。 |
| `before_json` | `JSON` | 是 | `NULL` | — | 修改前业务快照。 |
| `after_json` | `JSON` | 是 | `NULL` | — | 修改后业务快照。 |
| `detail_json` | `JSON` | 是 | `NULL` | — | 额外上下文、参数摘要和操作结果。 |
| `ip_address` | `VARCHAR(45)` | 是 | `NULL` | — | 操作来源 IP 地址，兼容 IPv4 和 IPv6。 |
| `user_agent` | `VARCHAR(512)` | 是 | `NULL` | — | 客户端 User-Agent。 |
| `created_at` | `DATETIME(3)` | 否 | `CURRENT_TIMESTAMP(3)` | — | 记录创建时间，使用毫秒精度。 |

**主键：** `id`

**唯一约束：**
 无。

**普通索引：**

- `idx_rp_audit_project_time`：(`project_id`, `created_at`)
- `idx_rp_audit_actor_time`：(`actor_user_id`, `created_at`)
- `idx_rp_audit_entity`：(`entity_type`, `entity_id`, `created_at`)
- `idx_rp_audit_request`：(`request_id`)

**外键关联：**
 无物理外键。

## 12. 文件存储应用层强制约束

1. `rp_file_object.storage_provider` 新数据必须写入 `DATABASE`。
2. `rp_file_object.total_chunks` 必须等于该文件在 `rp_file_chunk` 中的实际分片行数。
3. `rp_file_object.file_size` 必须等于所有分片 `chunk_length` 之和。
4. 同一文件的 `chunk_no` 必须从 0 开始连续，不能缺片或重复；复合主键阻止重复序号，但连续性仍由服务层校验。
5. 除最后一片外，`chunk_length` 原则上必须等于 `rp_file_object.chunk_size`。
6. `rp_file_chunk.chunk_sha256` 必须由服务端基于原始二进制计算，不能信任客户端结果。
7. 上传会话中的计数字段仅用于进度显示，最终完成判断必须重新聚合分片表。
8. 文件未达到 `READY` 状态时，不允许创建正式模板版本、正式数据快照或正式发布引用。
9. 文件内容不可通过普通 ORM 实体的 `SELECT *` 加载；分片查询必须使用专门 Mapper/Repository。
10. 清理过期上传时，只能删除未被正式业务引用且状态不为 `READY` 的文件对象。

## 13. MySQL 与连接配置建议

- `max_allowed_packet` 必须大于单片大小及协议开销。默认 4MiB 分片时，建议至少配置为 16MiB 或 32MiB。
- 根据并发上传量调整 InnoDB redo、磁盘空间、备份窗口和复制带宽；不要仅按业务表大小估算容量。
- 确保数据库、备库和备份系统都能承载 BLOB 增长，且恢复演练包含文件分片表。
- 上传接口和 JDBC 层必须使用流式二进制，不要调用会把整个文件读入 `byte[]` 的实现。
- 分片写入建议一片一事务；完成校验和状态切换使用短事务。
- 对 `rp_file_chunk` 不执行无条件全表扫描、`SELECT *` 或通用分页查询。
