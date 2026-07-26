-- ============================================================
-- Report Platform V2 -> V2.1 数据库文件存储迁移脚本
-- MySQL 8.0+
--
-- 目标：
-- 1. 不再依赖本地磁盘、MinIO、OSS 或 S3 保存业务文件；
-- 2. rp_file_object 继续保存文件元数据；
-- 3. 新增 rp_file_chunk，以 4MiB 为默认大小保存文件二进制分片；
-- 4. 新增 rp_file_upload_session，支持断点上传、进度统计和完整性校验。
--
-- 执行前：
-- 1. 完整备份 report_platform 数据库；
-- 2. 确认当前没有正在上传或生成报告的任务；
-- 3. 若 rp_file_object 已有外部文件记录，先将外部文件内容导入 rp_file_chunk，
--    完成 SHA-256 校验后再把对应 storage_provider 改为 DATABASE；
-- 4. 本脚本为一次性迁移脚本，不要重复执行。
-- ============================================================

USE report_platform;

-- bucket_name 在数据库存储模式下作为逻辑命名空间使用，不能为 NULL，
-- 否则原复合唯一键会允许多个 NULL 命名空间下出现相同 object_key。
UPDATE rp_file_object
SET bucket_name = 'default'
WHERE bucket_name IS NULL OR bucket_name = '';

ALTER TABLE rp_file_object
    MODIFY COLUMN storage_provider VARCHAR(32) NOT NULL DEFAULT 'DATABASE'
        COMMENT '固定为DATABASE，文件内容保存在rp_file_chunk',
    MODIFY COLUMN bucket_name VARCHAR(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'default'
        COMMENT '逻辑命名空间，不对应外部对象存储桶',
    MODIFY COLUMN object_key VARCHAR(512) CHARACTER SET ascii COLLATE ascii_bin NOT NULL
        COMMENT '数据库内稳定逻辑文件键',
    MODIFY COLUMN file_size BIGINT UNSIGNED NOT NULL DEFAULT 0
        COMMENT '完整文件大小（字节）',
    MODIFY COLUMN sha256 CHAR(64) CHARACTER SET ascii COLLATE ascii_bin DEFAULT NULL
        COMMENT '完整文件内容SHA-256',
    MODIFY COLUMN object_status VARCHAR(32) NOT NULL DEFAULT 'UPLOADING'
        COMMENT 'UPLOADING/VERIFYING/READY/QUARANTINED/FAILED/DELETED',
    ADD COLUMN chunk_size INT UNSIGNED NOT NULL DEFAULT 4194304
        COMMENT '标准分片大小（字节），默认4MiB，最后一片可小于该值'
        AFTER sha256,
    ADD COLUMN total_chunks INT UNSIGNED NOT NULL DEFAULT 0
        COMMENT '文件完整分片数量'
        AFTER chunk_size,
    ADD COLUMN upload_completed_at DATETIME(3) DEFAULT NULL
        COMMENT '文件完整校验并转为READY的时间'
        AFTER object_status,
    ADD COLUMN row_version INT UNSIGNED NOT NULL DEFAULT 0
        COMMENT '乐观锁版本'
        AFTER created_at,
    ADD KEY idx_rp_file_status_time (object_status, created_at);

ALTER TABLE rp_file_object
    COMMENT = '数据库文件对象元数据表';

CREATE TABLE rp_file_chunk (
    file_object_id      BIGINT UNSIGNED NOT NULL COMMENT '所属文件对象ID',
    chunk_no            INT UNSIGNED NOT NULL COMMENT '分片序号，从0开始连续递增',
    chunk_length        INT UNSIGNED NOT NULL COMMENT '当前分片实际字节数',
    chunk_sha256        CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '当前分片SHA-256',
    chunk_data          MEDIUMBLOB NOT NULL COMMENT '文件分片二进制内容，单片不得超过MEDIUMBLOB上限',
    created_at          DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    PRIMARY KEY (file_object_id, chunk_no),
    CONSTRAINT fk_rp_file_chunk_object
        FOREIGN KEY (file_object_id) REFERENCES rp_file_object(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci ROW_FORMAT=DYNAMIC COMMENT='数据库文件二进制分片表';

CREATE TABLE rp_file_upload_session (
    id                  BIGINT UNSIGNED NOT NULL AUTO_INCREMENT COMMENT '上传会话ID',
    upload_token        CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '断点上传会话令牌（UUID）',
    file_object_id      BIGINT UNSIGNED NOT NULL COMMENT '待上传文件对象ID',
    expected_file_size  BIGINT UNSIGNED NOT NULL COMMENT '客户端声明的完整文件大小',
    expected_sha256     CHAR(64) CHARACTER SET ascii COLLATE ascii_bin DEFAULT NULL COMMENT '客户端声明的完整文件SHA-256',
    chunk_size          INT UNSIGNED NOT NULL DEFAULT 4194304 COMMENT '本次上传约定分片大小',
    expected_chunks     INT UNSIGNED NOT NULL COMMENT '预计分片总数',
    uploaded_chunks     INT UNSIGNED NOT NULL DEFAULT 0 COMMENT '已确认写入的分片数，仅作进度缓存',
    uploaded_bytes      BIGINT UNSIGNED NOT NULL DEFAULT 0 COMMENT '已确认写入字节数，仅作进度缓存',
    upload_status       VARCHAR(32) NOT NULL DEFAULT 'CREATED' COMMENT 'CREATED/UPLOADING/VERIFYING/COMPLETED/FAILED/CANCELLED/EXPIRED',
    last_activity_at    DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) COMMENT '最近一次上传或校验活动时间',
    expires_at          DATETIME(3) NOT NULL COMMENT '会话过期时间',
    error_message       VARCHAR(2000) DEFAULT NULL COMMENT '上传或校验失败原因',
    created_by          BIGINT UNSIGNED DEFAULT NULL COMMENT '上传发起人',
    created_at          DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    updated_at          DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
    row_version         INT UNSIGNED NOT NULL DEFAULT 0 COMMENT '乐观锁版本',
    PRIMARY KEY (id),
    UNIQUE KEY uk_rp_file_upload_token (upload_token),
    KEY idx_rp_upload_file_status (file_object_id, upload_status),
    KEY idx_rp_upload_status_expire (upload_status, expires_at),
    CONSTRAINT fk_rp_upload_file_object
        FOREIGN KEY (file_object_id) REFERENCES rp_file_object(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='数据库文件断点上传会话表';

-- 新建数据库中没有历史文件时，可直接统一存储提供方：
-- UPDATE rp_file_object SET storage_provider = 'DATABASE';
--
-- 已有外部文件时，必须由迁移程序执行：
-- 1. 读取外部文件；
-- 2. 按 chunk_size 切片写入 rp_file_chunk；
-- 3. 验证 COUNT(*)、SUM(chunk_length)、每片 SHA-256 和完整文件 SHA-256；
-- 4. 更新 total_chunks、file_size、sha256、upload_completed_at；
-- 5. 最后把 storage_provider 和 object_status 更新为 DATABASE、READY。

-- 迁移后结构检查
SHOW CREATE TABLE rp_file_object;
SHOW CREATE TABLE rp_file_chunk;
SHOW CREATE TABLE rp_file_upload_session;
