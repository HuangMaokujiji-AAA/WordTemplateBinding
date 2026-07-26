# Word 模板多人协作锁设计

> 适用仓库：`HuangMaokujiji-AAA/WordTemplateBinding`  
> 适用技术栈：ASP.NET Core Minimal API、MySqlConnector 2.6.1、MySQL 8.0、Vue 3  
> 文档目标：实现多个用户同时处理同一超长模板的不同章节，同时避免同一章节绑定被覆盖、浏览器异常退出造成永久锁、应用多实例部署后锁失效。

---

## 1. 当前问题

数据库设计已经预留 `rp_chapter_lock`，包含：

- `chapter_id`
- `lock_token`
- `owner_user_id`
- `lock_type`
- `heartbeat_at`
- `expires_at`
- `lock_version`

但是当前 API 的绑定写入仍类似：

```http
PUT /api/binding-sets/{bindingSetId}/items/{templateElementId}
```

请求中没有：

- 锁令牌；
- 绑定集编辑版本；
- 客户端预期版本；
- 并发冲突响应；
- 心跳；
- 只读降级。

因此当前状态属于：

> 数据表设计已考虑多人协作，但业务服务和 API 尚未真正执行锁约束。

---

## 2. 总体原则

采用：

```text
章节/Segment 租约锁
        +
BindingSet 乐观锁
        +
已发布版本不可变
```

三个层次共同保证一致性。

### 2.1 租约锁解决什么

租约锁负责：

- 同一章节同一时刻只允许一个用户编辑；
- 其他用户进入只读模式；
- 浏览器异常退出后自动释放；
- 多个应用实例共享锁状态；
- 显示当前编辑者和锁过期时间。

### 2.2 乐观锁解决什么

即使出现以下情况：

- 两个标签页；
- 网络延迟；
- 旧请求晚到；
- 锁过期后旧页面继续提交；
- 管理员强制接管；
- 服务重试；

也必须通过 `edit_version` 阻止旧数据覆盖新数据。

### 2.3 不可变版本解决什么

正式生成不读取正在编辑的草稿，而读取：

```text
TemplateVersion
ChapterRevision
BindingSetVersion
ContextVersion
DataSnapshot
```

生成期间无需持有编辑锁。

---

## 3. 使用的方法和库

## 3.1 MySqlConnector 2.6.1

仓库已有：

```xml
<PackageVersion Include="MySqlConnector" Version="2.6.1" />
```

锁必须存储在 MySQL，不能使用：

```csharp
lock (...)
SemaphoreSlim
ConcurrentDictionary
MemoryCache
```

因为这些方法只在单个进程内有效，应用部署两个实例后会失效。

MySQL 实现优点：

- 不需要额外部署 Redis；
- 与现有数据事务一致；
- 多实例共享；
- 支持 `SELECT ... FOR UPDATE`；
- 支持唯一主键保证一个章节只有一条锁记录；
- 可在绑定写事务中同时校验锁和版本。

## 3.2 ASP.NET Core

使用：

- Minimal API Endpoint Filter 或服务层守卫；
- `HttpContext.Request.Headers` 读取 `X-Lock-Token`；
- `ProblemDetails` 返回统一冲突；
- 认证系统提供当前 `userId`；
- `CancellationToken` 取消数据库操作。

不建议把锁校验只写在前端。

## 3.3 Vue 3

前端新增组合式函数：

```text
useChapterLease.ts
```

负责：

- 获取锁；
- 定时心跳；
- 页面隐藏时调整状态；
- 页面退出时尽力释放；
- 心跳失败后切换只读；
- 显示锁持有者；
- 写请求附带 Lock Token 和 Edit Version。

---

## 4. 锁粒度

## 4.1 推荐：Chapter/Segment 级锁

```text
一个 Segment 对应一个 Chapter
一个 Chapter 同时一个编辑者
其他用户只读
```

适合当前项目，因为模板已按绑定范围拆分。

## 4.2 不锁完整模板

不能以 `template_version_id` 为锁键，否则：

- 用户 A 编辑封面；
- 用户 B 无法编辑附录；
- 超长模板拆分失去意义。

## 4.3 暂不做元素级锁

元素级锁会导致：

- 每个黄色标记都需要锁状态；
- 心跳数量膨胀；
- 图表包含多个属性，不易定义锁边界；
- 用户理解困难；
- 冲突合并复杂。

第一阶段采用章节独占编辑最稳妥。

---

## 5. 数据库设计

## 5.1 `rp_chapter_lock`

建议使用已有设计，并补充 `client_instance_id` 和 `last_request_id`，便于排查多标签页。

```sql
CREATE TABLE rp_chapter_lock (
    chapter_id BIGINT UNSIGNED NOT NULL,
    lock_token CHAR(36) NOT NULL,
    owner_user_id BIGINT UNSIGNED NOT NULL,
    lock_type VARCHAR(32) NOT NULL DEFAULT 'BINDING_EDIT',

    client_instance_id CHAR(36) NULL,
    last_request_id VARCHAR(128) NULL,

    acquired_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    heartbeat_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    expires_at DATETIME(3) NOT NULL,

    lock_version INT UNSIGNED NOT NULL DEFAULT 0,

    PRIMARY KEY (chapter_id),
    UNIQUE KEY uk_rp_chapter_lock_token (lock_token),
    KEY idx_rp_chapter_lock_expire (expires_at),
    KEY idx_rp_chapter_lock_owner (owner_user_id, expires_at),

    CONSTRAINT fk_rp_lock_chapter
        FOREIGN KEY (chapter_id)
        REFERENCES rp_chapter(id)
        ON DELETE CASCADE
);
```

## 5.2 给 `rp_binding_set` 增加编辑版本

```sql
ALTER TABLE rp_binding_set
    ADD COLUMN edit_version INT UNSIGNED NOT NULL DEFAULT 0,
    ADD COLUMN updated_by BIGINT UNSIGNED NULL,
    ADD COLUMN updated_at DATETIME(3) NOT NULL
        DEFAULT CURRENT_TIMESTAMP(3)
        ON UPDATE CURRENT_TIMESTAMP(3);
```

`edit_version` 每次绑定明细增加、修改或删除时加 1。

## 5.3 可选：给绑定明细增加版本

第一阶段只需要 BindingSet 级版本。

后续需要局部冲突比较时再增加：

```sql
ALTER TABLE rp_binding_item
    ADD COLUMN row_version INT UNSIGNED NOT NULL DEFAULT 0;
```

---

## 6. 租约参数

推荐默认值：

```text
租约时长：120 秒
心跳间隔：30 秒
前端请求超时：10 秒
连续心跳失败阈值：2 次
锁过期宽限：不额外增加
```

原因：

- 30 秒心跳不会产生过大数据库压力；
- 120 秒允许短暂断网；
- 关闭浏览器后最多约两分钟释放；
- 锁状态足够及时。

配置：

```json
{
  "CollaborationLock": {
    "LeaseSeconds": 120,
    "HeartbeatSeconds": 30,
    "MaxHeartbeatFailures": 2
  }
}
```

对应：

```csharp
public sealed class CollaborationLockOptions
{
    [Range(30, 600)]
    public int LeaseSeconds { get; init; } = 120;

    [Range(10, 300)]
    public int HeartbeatSeconds { get; init; } = 30;

    [Range(1, 5)]
    public int MaxHeartbeatFailures { get; init; } = 2;
}
```

---

## 7. 获取锁的事务算法

## 7.1 为什么不用单条简单 UPSERT

虽然可以编写复杂的 `INSERT ... ON DUPLICATE KEY UPDATE IF(...)`，但：

- 错误信息不清楚；
- 很难区分续租、接管和冲突；
- 逻辑扩展困难；
- 审计不方便。

推荐使用事务和 `SELECT ... FOR UPDATE`。

## 7.2 获取锁流程

```text
BEGIN
→ SELECT chapter_lock FOR UPDATE
→ 无记录：创建
→ 已过期：接管
→ 同一 token：续租
→ 其他有效锁：409
→ COMMIT
```

伪代码：

```csharp
public async Task<ChapterLease> AcquireAsync(
    ulong chapterId,
    ulong userId,
    Guid clientInstanceId,
    string lockType,
    CancellationToken cancellationToken)
{
    await using MySqlConnection connection =
        await _factory.OpenConnectionAsync(cancellationToken);

    await using MySqlTransaction transaction =
        await connection.BeginTransactionAsync(cancellationToken);

    ChapterLockRecord? current =
        await SelectForUpdateAsync(
            connection,
            transaction,
            chapterId,
            cancellationToken);

    DateTime now = _clock.UtcNow;
    DateTime expiresAt = now.AddSeconds(_options.LeaseSeconds);

    if (current is null)
    {
        ChapterLockRecord created = await InsertAsync(
            chapterId,
            Guid.NewGuid(),
            userId,
            clientInstanceId,
            lockType,
            now,
            expiresAt,
            connection,
            transaction,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return Map(created);
    }

    if (current.ExpiresAt <= now)
    {
        ChapterLockRecord takenOver = await ReplaceExpiredAsync(
            current,
            Guid.NewGuid(),
            userId,
            clientInstanceId,
            lockType,
            now,
            expiresAt,
            connection,
            transaction,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return Map(takenOver);
    }

    throw new ChapterLockedException(
        chapterId,
        current.OwnerUserId,
        current.ExpiresAt);
}
```

## 7.3 SQL

```sql
SELECT
    chapter_id,
    lock_token,
    owner_user_id,
    lock_type,
    client_instance_id,
    acquired_at,
    heartbeat_at,
    expires_at,
    lock_version
FROM rp_chapter_lock
WHERE chapter_id = @chapterId
FOR UPDATE;
```

插入：

```sql
INSERT INTO rp_chapter_lock (
    chapter_id,
    lock_token,
    owner_user_id,
    lock_type,
    client_instance_id,
    acquired_at,
    heartbeat_at,
    expires_at,
    lock_version
)
VALUES (
    @chapterId,
    @lockToken,
    @ownerUserId,
    @lockType,
    @clientInstanceId,
    @now,
    @now,
    @expiresAt,
    0
);
```

接管过期锁：

```sql
UPDATE rp_chapter_lock
SET lock_token = @newToken,
    owner_user_id = @ownerUserId,
    lock_type = @lockType,
    client_instance_id = @clientInstanceId,
    acquired_at = @now,
    heartbeat_at = @now,
    expires_at = @expiresAt,
    lock_version = lock_version + 1
WHERE chapter_id = @chapterId
  AND lock_version = @expectedLockVersion
  AND expires_at <= @now;
```

必须检查 `RowsAffected == 1`。

---

## 8. 心跳算法

请求：

```http
POST /api/chapters/{chapterId}/lock/heartbeat
X-Lock-Token: 5f2...
```

SQL：

```sql
UPDATE rp_chapter_lock
SET heartbeat_at = @now,
    expires_at = @expiresAt,
    lock_version = lock_version + 1
WHERE chapter_id = @chapterId
  AND lock_token = @lockToken
  AND owner_user_id = @userId
  AND expires_at > @now;
```

结果：

- `RowsAffected == 1`：续租成功；
- `RowsAffected == 0`：锁已过期、被接管或 Token 错误。

心跳不得自动创建新锁。否则旧页面可能在锁丢失后悄悄重新取得编辑权。

---

## 9. 释放锁

请求：

```http
DELETE /api/chapters/{chapterId}/lock
X-Lock-Token: 5f2...
```

SQL：

```sql
DELETE FROM rp_chapter_lock
WHERE chapter_id = @chapterId
  AND lock_token = @lockToken
  AND owner_user_id = @userId;
```

释放失败不应删除别人的锁。

浏览器 `beforeunload` 只能作为“尽力释放”，不能作为锁正确性的前提。

---

## 10. 绑定写入守卫

## 10.1 请求头

所有绑定写接口必须携带：

```http
X-Lock-Token: {uuid}
If-Match: "17"
```

其中：

- `X-Lock-Token` 对应章节租约；
- `If-Match` 对应 `rp_binding_set.edit_version`。

也可把版本放在 JSON，但 `If-Match` 更符合 HTTP 并发控制语义。

## 10.2 事务顺序

```text
BEGIN
→ 锁定 rp_binding_set
→ 查询并校验章节租约
→ 校验 edit_version
→ 校验模板元素属于该 BindingSet 的 TemplateVersion
→ Upsert/Delete rp_binding_item
→ edit_version + 1
→ 重置 validation_status
→ 写审计日志
→ COMMIT
```

核心 SQL：

```sql
SELECT
    id,
    chapter_id,
    template_version_id,
    binding_status,
    edit_version
FROM rp_binding_set
WHERE id = @bindingSetId
FOR UPDATE;
```

锁校验：

```sql
SELECT 1
FROM rp_chapter_lock
WHERE chapter_id = @chapterId
  AND lock_token = @lockToken
  AND owner_user_id = @userId
  AND lock_type = 'BINDING_EDIT'
  AND expires_at > UTC_TIMESTAMP(3);
```

更新版本：

```sql
UPDATE rp_binding_set
SET edit_version = edit_version + 1,
    validation_status = 'NOT_VALIDATED',
    validation_result_json = NULL,
    updated_by = @userId
WHERE id = @bindingSetId
  AND edit_version = @expectedEditVersion;
```

必须检查 `RowsAffected == 1`。

## 10.3 服务层方法

```csharp
public async Task<BindingMutationResult> UpsertWithLeaseAsync(
    ulong bindingSetId,
    ulong templateElementId,
    BindingItemUpsert upsert,
    string lockToken,
    uint expectedEditVersion,
    ulong actorUserId,
    CancellationToken cancellationToken);
```

不要让 Endpoint 先校验锁，再调用仓储写入。这样会出现检查与写入之间的竞态。

锁校验和写入必须处于同一数据库事务。

---

## 11. API 设计

## 11.1 获取锁

```http
POST /api/chapters/{chapterId}/lock
```

请求：

```json
{
  "lockType": "BINDING_EDIT",
  "clientInstanceId": "7bbc..."
}
```

成功：

```json
{
  "chapterId": "31",
  "lockToken": "5f2...",
  "ownerUserId": "12",
  "lockType": "BINDING_EDIT",
  "acquiredAt": "2026-07-27T06:00:00Z",
  "expiresAt": "2026-07-27T06:02:00Z",
  "heartbeatSeconds": 30
}
```

冲突：

```http
409 Conflict
```

```json
{
  "title": "章节正在被其他用户编辑",
  "status": 409,
  "errorCode": "chapter_locked",
  "ownerUserId": "27",
  "expiresAt": "2026-07-27T06:01:42Z"
}
```

不要返回其他用户的敏感资料，只返回允许展示的姓名或 ID。

## 11.2 查询锁状态

```http
GET /api/chapters/{chapterId}/lock
```

用于只读用户查看当前编辑者。

## 11.3 心跳

```http
POST /api/chapters/{chapterId}/lock/heartbeat
X-Lock-Token: ...
```

## 11.4 释放

```http
DELETE /api/chapters/{chapterId}/lock
X-Lock-Token: ...
```

## 11.5 管理员强制接管

```http
POST /api/chapters/{chapterId}/lock/takeover
```

要求：

- 管理员权限；
- 请求填写原因；
- 写审计日志；
- 原锁立即失效；
- 不自动合并旧页面未提交内容。

---

## 12. 错误码

| 错误码 | HTTP | 含义 |
|---|---:|---|
| `chapter_locked` | 409 | 其他用户持有有效锁 |
| `chapter_lock_expired` | 409 | 当前 Token 已过期 |
| `chapter_lock_token_invalid` | 409 | Token 不匹配 |
| `chapter_lock_owner_mismatch` | 403 | 当前用户不是锁持有者 |
| `chapter_lock_type_mismatch` | 409 | 锁类型不允许当前写操作 |
| `binding_edit_version_conflict` | 409 | BindingSet 已被其他请求修改 |
| `binding_set_published` | 409 | 已发布版本不可修改 |
| `chapter_lock_required` | 428 | 写操作缺少锁令牌 |
| `if_match_required` | 428 | 写操作缺少预期版本 |

---

## 13. 后端代码结构

建议增加：

```text
src/WordTemplateBinding.Core/
├─ Models/ChapterLockModels.cs
├─ Interfaces/IChapterLockRepository.cs
├─ Services/ChapterLockService.cs
├─ Services/BindingMutationGuard.cs
└─ Exceptions/CollaborationExceptions.cs

src/WordTemplateBinding.Infrastructure/
└─ Persistence/MySql/
   ├─ MySqlChapterLockRepository.cs
   └─ MySqlBindingMutationRepository.cs

src/WordTemplateBinding.Api/
├─ Endpoints/ChapterLockEndpoints.cs
└─ Infrastructure/LockTokenParser.cs
```

核心接口：

```csharp
public interface IChapterLockRepository
{
    Task<ChapterLease> AcquireAsync(
        ulong chapterId,
        ulong ownerUserId,
        string lockType,
        Guid clientInstanceId,
        CancellationToken cancellationToken);

    Task<ChapterLease> HeartbeatAsync(
        ulong chapterId,
        Guid lockToken,
        ulong ownerUserId,
        CancellationToken cancellationToken);

    Task<bool> ReleaseAsync(
        ulong chapterId,
        Guid lockToken,
        ulong ownerUserId,
        CancellationToken cancellationToken);

    Task<ChapterLease?> GetAsync(
        ulong chapterId,
        CancellationToken cancellationToken);
}
```

---

## 14. Vue 前端实现

## 14.1 `useChapterLease.ts`

```ts
export interface ChapterLeaseState {
  mode: "acquiring" | "editable" | "readonly" | "lost";
  lockToken: string | null;
  expiresAt: string | null;
  ownerUserId: string | null;
  heartbeatFailures: number;
}
```

功能：

```text
acquire()
heartbeat()
release()
markLost()
attachHeaders()
```

示意：

```ts
const heartbeatTimer = window.setInterval(async () => {
  if (state.value.mode !== "editable") return;

  try {
    const lease = await api.heartbeatChapterLock(
      chapterId,
      state.value.lockToken!
    );
    state.value.expiresAt = lease.expiresAt;
    state.value.heartbeatFailures = 0;
  } catch {
    state.value.heartbeatFailures++;

    if (state.value.heartbeatFailures >= 2) {
      state.value.mode = "lost";
    }
  }
}, 30_000);
```

## 14.2 页面行为

### 获取成功

- 绑定控件可编辑；
- 显示“我正在编辑”；
- 启动心跳；
- 保存请求附加 Token 和版本。

### 获取失败

- 文档可预览；
- 所有拖拽和删除按钮禁用；
- 显示当前编辑者；
- 提供“刷新锁状态”；
- 管理员显示“强制接管”。

### 锁丢失

立即：

1. 禁止新的绑定操作；
2. 停止自动保存；
3. 保留本地尚未提交操作；
4. 提示用户刷新；
5. 不自动重新获取锁；
6. 不自动覆盖服务器数据。

## 14.3 多标签页识别

浏览器启动时创建：

```ts
crypto.randomUUID()
```

保存为 `clientInstanceId`，每个标签页独立。

可用 `BroadcastChannel` 提示：

> 当前浏览器的另一个标签页已在编辑此章节。

这只是用户体验优化，最终一致性仍依赖数据库锁。

---

## 15. 保存策略

## 15.1 不建议每次拖拽立即单独请求

可以采用 300～800 ms 防抖，但每次批量保存仍必须：

- 使用当前 Token；
- 使用当前 `editVersion`；
- 返回新的 `editVersion`。

请求：

```json
{
  "expectedEditVersion": 17,
  "operations": [
    {
      "operation": "UPSERT",
      "templateElementId": "301",
      "sourcePath": "rows.majorName"
    },
    {
      "operation": "DELETE",
      "templateElementId": "302"
    }
  ]
}
```

响应：

```json
{
  "editVersion": 18,
  "applied": 2
}
```

批量事务能避免一组关联图表绑定只保存一半。

---

## 16. 发布与锁

发布操作要求：

1. 用户持有 `BINDING_EDIT` 或 `REVIEW` 锁；
2. `BindingSet` 校验为 VALID；
3. 客户端 `editVersion` 最新；
4. 在事务内创建不可变版本；
5. 发布后当前 BindingSet 不允许继续修改；
6. 如需修改，从已发布版本克隆新草稿。

最终生成任务不需要编辑锁，只读取已发布版本。

---

## 17. 清理策略

不需要定时删除所有过期锁才能保证正确性，因为获取锁时会判断 `expires_at`。

但可增加低频清理：

```sql
DELETE FROM rp_chapter_lock
WHERE expires_at < UTC_TIMESTAMP(3) - INTERVAL 7 DAY;
```

作用只是减少历史过期记录，不参与正确性。

---

## 18. 审计日志

以下操作必须写 `rp_audit_log`：

- 获取锁；
- 释放锁；
- 锁过期接管；
- 管理员强制接管；
- 心跳不必每次写审计；
- 绑定批量修改；
- 发布 BindingSet；
- 因版本冲突拒绝写入。

审计内容不要保存完整数据值，可保存：

```json
{
  "bindingSetId": "88",
  "beforeEditVersion": 17,
  "afterEditVersion": 18,
  "affectedElementIds": ["301", "302"],
  "operationCount": 2
}
```

---

## 19. 测试

## 19.1 单元测试

```text
ChapterLockServiceTests
LockTokenParserTests
BindingMutationGuardTests
LeaseExpirationTests
```

覆盖：

1. 无锁时获取；
2. 有效锁冲突；
3. 过期锁接管；
4. Token 不匹配；
5. User 不匹配；
6. 心跳续租；
7. 心跳过期；
8. 释放自己的锁；
9. 不能释放别人的锁；
10. 版本冲突。

## 19.2 MySQL 并发集成测试

建议可选使用：

```text
Testcontainers.MySql
```

测试必须真实并行开启两个连接：

```text
Task A：获取锁
Task B：同时获取同一章节
期望：只有一个成功
```

以及：

```text
Task A/B 同时携带 editVersion=5 保存
期望：一个成功变为 6，另一个 409
```

不能只依赖 InMemory 仓储测试锁正确性。

## 19.3 前端测试

Vitest 覆盖：

- 获取锁后进入编辑；
- 冲突后只读；
- 心跳成功；
- 连续两次心跳失败；
- 锁丢失后按钮禁用；
- 请求头包含 Token；
- 409 后不覆盖本地状态；
- 页面卸载释放资源。

---

## 20. 分阶段实施

### 阶段 A：租约锁闭环

- 实现 `rp_chapter_lock` 仓储；
- 获取、查询、心跳、释放 API；
- 前端只读/编辑模式；
- 暂不修改绑定接口。

### 阶段 B：绑定写入强制校验

- `rp_binding_set.edit_version`；
- 写接口要求 Token 和 `If-Match`；
- 服务层同事务校验；
- 返回新版本；
- 统一错误码。

### 阶段 C：批量保存与审计

- 批量 Mutation API；
- 审计日志；
- 冲突提示；
- 管理员接管。

### 阶段 D：发布版本

- BindingSet 不可变发布；
- ChapterRevision；
- 生成任务只读取发布版本；
- 锁与生成彻底解耦。

---

## 21. 验收标准

1. 用户 A 编辑章节 1 时，用户 B 可以编辑章节 2；
2. 用户 B 进入章节 1 时只能只读；
3. 关闭用户 A 浏览器后，锁最多 120 秒自动失效；
4. 多个 API 实例下仍只能一个用户获得同一章节锁；
5. 旧 Token 不能写入；
6. 旧 `editVersion` 不能覆盖新绑定；
7. 管理员接管后旧页面立即失去保存能力；
8. 心跳失败时前端转只读；
9. 生成报告不依赖编辑锁；
10. 所有并发冲突有明确 409/428 错误，而不是静默覆盖。
