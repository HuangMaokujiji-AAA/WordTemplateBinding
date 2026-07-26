-- ============================================================
-- Report Platform V2 - MySQL 8.0+
-- 适用场景：大文档拆分、多人协同、模板解析、数据绑定、并行渲染、最终组装与归档
--
-- 设计约定：
-- 1. 用户、角色、组织机构优先复用现有认证系统（如 RuoYi sys_user/sys_role）。
--    本脚本中的 created_by / user_id 等字段不建立跨系统外键。
-- 2. 数据库只存元数据、小型 JSON 和索引；DOCX、PDF、图片、大型 JSON/Excel 存对象存储。
-- 3. 模板版本、数据快照、章节版本、发布版本一经发布即不可修改。
-- 4. 所有时间建议按 UTC 写入，接口层按用户时区展示。
-- ============================================================

CREATE DATABASE IF NOT EXISTS report_platform
  CHARACTER SET utf8mb4
  COLLATE utf8mb4_0900_ai_ci;

USE report_platform;

SET NAMES utf8mb4;
SET FOREIGN_KEY_CHECKS = 0;

-- ============================================================
-- 1. 统一文件对象
-- 不直接存可过期的访问 URL，而存稳定的 object_key。
-- ============================================================
CREATE TABLE rp_file_object (
    id                  BIGINT UNSIGNED NOT NULL AUTO_INCREMENT COMMENT '文件对象ID',
    storage_provider    VARCHAR(32) NOT NULL DEFAULT 'LOCAL' COMMENT 'LOCAL/MINIO/OSS/S3',
    bucket_name         VARCHAR(128) CHARACTER SET ascii COLLATE ascii_bin DEFAULT NULL COMMENT '存储桶',
    object_key          VARCHAR(512) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '稳定对象键',
    original_name       VARCHAR(255) NOT NULL COMMENT '原始文件名',
    file_ext            VARCHAR(32) DEFAULT NULL COMMENT '扩展名',
    mime_type           VARCHAR(128) DEFAULT NULL COMMENT 'MIME类型',
    file_size           BIGINT UNSIGNED NOT NULL DEFAULT 0 COMMENT '文件大小（字节）',
    sha256              CHAR(64) CHARACTER SET ascii COLLATE ascii_bin DEFAULT NULL COMMENT '内容SHA-256',
    object_status       VARCHAR(32) NOT NULL DEFAULT 'READY' COMMENT 'UPLOADING/READY/QUARANTINED/DELETED',
    metadata_json       JSON DEFAULT NULL COMMENT '宽高、页数、编码等扩展信息',
    created_by          BIGINT UNSIGNED DEFAULT NULL COMMENT '上传/创建人',
    created_at          DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    deleted_at          DATETIME(3) DEFAULT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uk_rp_file_object_key (storage_provider, bucket_name, object_key),
    KEY idx_rp_file_sha256 (sha256),
    KEY idx_rp_file_created_at (created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='统一文件对象表';

-- ============================================================
-- 2. 模板：逻辑模板、不可变版本、解析出的可绑定元素
-- ============================================================
CREATE TABLE rp_template (
    id                  BIGINT UNSIGNED NOT NULL AUTO_INCREMENT COMMENT '逻辑模板ID',
    template_code       VARCHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '稳定业务编码',
    template_name       VARCHAR(255) NOT NULL COMMENT '模板名称',
    template_type       VARCHAR(32) NOT NULL DEFAULT 'SECTION' COMMENT 'MASTER/SECTION/COMPONENT',
    category_code       VARCHAR(64) DEFAULT NULL COMMENT '模板分类',
    template_status     VARCHAR(32) NOT NULL DEFAULT 'ACTIVE' COMMENT 'ACTIVE/DISABLED/ARCHIVED',
    description         TEXT DEFAULT NULL,
    current_version_no  INT UNSIGNED NOT NULL DEFAULT 0 COMMENT '当前生效版本号，仅作快速定位',
    created_by          BIGINT UNSIGNED DEFAULT NULL,
    created_at          DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    updated_by          BIGINT UNSIGNED DEFAULT NULL,
    updated_at          DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
    row_version         INT UNSIGNED NOT NULL DEFAULT 0 COMMENT '乐观锁版本',
    deleted_at          DATETIME(3) DEFAULT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uk_rp_template_code (template_code),
    KEY idx_rp_template_type_status (template_type, template_status)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='逻辑模板表';

CREATE TABLE rp_template_version (
    id                  BIGINT UNSIGNED NOT NULL AUTO_INCREMENT COMMENT '模板版本ID',
    template_id         BIGINT UNSIGNED NOT NULL COMMENT '逻辑模板ID',
    version_no          INT UNSIGNED NOT NULL COMMENT '版本号，从1递增',
    file_object_id      BIGINT UNSIGNED NOT NULL COMMENT 'DOCX文件对象',
    version_status      VARCHAR(32) NOT NULL DEFAULT 'UPLOADED' COMMENT 'UPLOADED/PARSING/READY/FAILED/RETIRED',
    parser_name         VARCHAR(64) DEFAULT NULL COMMENT '解析器名称',
    parser_version      VARCHAR(32) DEFAULT NULL COMMENT '解析器版本',
    parse_result_json   JSON DEFAULT NULL COMMENT '解析摘要、警告、兼容性信息',
    page_count          INT UNSIGNED DEFAULT NULL COMMENT '解析或转换得到的页数',
    element_count       INT UNSIGNED NOT NULL DEFAULT 0 COMMENT '可绑定元素数量',
    style_fingerprint   CHAR(64) CHARACTER SET ascii COLLATE ascii_bin DEFAULT NULL COMMENT '样式指纹',
    published_by        BIGINT UNSIGNED DEFAULT NULL,
    published_at        DATETIME(3) DEFAULT NULL,
    created_by          BIGINT UNSIGNED DEFAULT NULL,
    created_at          DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    PRIMARY KEY (id),
    UNIQUE KEY uk_rp_template_version (template_id, version_no),
    UNIQUE KEY uk_rp_template_file (template_id, file_object_id),
    KEY idx_rp_template_version_status (template_id, version_status),
    CONSTRAINT fk_rp_tv_template
        FOREIGN KEY (template_id) REFERENCES rp_template(id) ON DELETE RESTRICT,
    CONSTRAINT fk_rp_tv_file
        FOREIGN KEY (file_object_id) REFERENCES rp_file_object(id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='不可变模板版本表';

CREATE TABLE rp_template_element (
    id                  BIGINT UNSIGNED NOT NULL AUTO_INCREMENT COMMENT '模板元素ID',
    template_version_id BIGINT UNSIGNED NOT NULL COMMENT '模板版本ID',
    element_key         VARCHAR(255) COLLATE utf8mb4_bin NOT NULL COMMENT '稳定定位键：内容控件Tag/书签/占位符/Chart关系ID',
    element_type        VARCHAR(32) NOT NULL COMMENT 'TEXT/TABLE/CHART/IMAGE/REPEAT_BLOCK/CONDITION',
    locator_type        VARCHAR(32) NOT NULL COMMENT 'CONTENT_CONTROL/BOOKMARK/PLACEHOLDER/RELATIONSHIP',
    display_name        VARCHAR(255) DEFAULT NULL COMMENT '前端显示名称',
    locator_json        JSON NOT NULL COMMENT '文档内部定位信息',
    binding_schema_json JSON DEFAULT NULL COMMENT '该元素允许的绑定结构',
    default_value_json  JSON DEFAULT NULL COMMENT '模板默认值',
    is_required         TINYINT(1) NOT NULL DEFAULT 0,
    sort_no             INT NOT NULL DEFAULT 0,
    parse_status        VARCHAR(32) NOT NULL DEFAULT 'VALID' COMMENT 'VALID/WARNING/UNSUPPORTED',
    parse_message       VARCHAR(1000) DEFAULT NULL,
    created_at          DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    PRIMARY KEY (id),
    UNIQUE KEY uk_rp_template_element (template_version_id, element_key),
    KEY idx_rp_element_type (template_version_id, element_type),
    CONSTRAINT fk_rp_te_version
        FOREIGN KEY (template_version_id) REFERENCES rp_template_version(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='模板可绑定元素清单';

-- ============================================================
-- 3. 项目、项目成员、项目全局上下文版本
-- ============================================================
CREATE TABLE rp_project (
    id                          BIGINT UNSIGNED NOT NULL AUTO_INCREMENT COMMENT '项目ID',
    project_code                VARCHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '项目稳定编码',
    project_name                VARCHAR(255) NOT NULL COMMENT '报告项目名称',
    description                 TEXT DEFAULT NULL,
    project_status              VARCHAR(32) NOT NULL DEFAULT 'DRAFT' COMMENT 'DRAFT/CONFIGURING/READY/GENERATING/COMPLETED/ARCHIVED',
    master_template_version_id  BIGINT UNSIGNED DEFAULT NULL COMMENT '固定到主模板具体版本',
    current_context_version_no  INT UNSIGNED NOT NULL DEFAULT 0 COMMENT '当前全局上下文版本',
    assembly_config_json        JSON DEFAULT NULL COMMENT '目录、分页、页眉页脚、样式冲突等组装策略',
    created_by                  BIGINT UNSIGNED NOT NULL COMMENT '创建人',
    created_at                  DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    updated_by                  BIGINT UNSIGNED DEFAULT NULL,
    updated_at                  DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
    row_version                 INT UNSIGNED NOT NULL DEFAULT 0 COMMENT '乐观锁版本',
    deleted_at                  DATETIME(3) DEFAULT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uk_rp_project_code (project_code),
    KEY idx_rp_project_status (project_status, updated_at),
    CONSTRAINT fk_rp_project_master_tv
        FOREIGN KEY (master_template_version_id) REFERENCES rp_template_version(id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='报告项目表';

CREATE TABLE rp_project_member (
    project_id          BIGINT UNSIGNED NOT NULL,
    user_id             BIGINT UNSIGNED NOT NULL COMMENT '外部用户系统ID',
    project_role        VARCHAR(32) NOT NULL COMMENT 'OWNER/MANAGER/EDITOR/REVIEWER/VIEWER',
    member_status       VARCHAR(32) NOT NULL DEFAULT 'ACTIVE' COMMENT 'ACTIVE/DISABLED',
    joined_at           DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    updated_at          DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
    PRIMARY KEY (project_id, user_id),
    KEY idx_rp_member_user (user_id, member_status),
    CONSTRAINT fk_rp_member_project
        FOREIGN KEY (project_id) REFERENCES rp_project(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='项目级成员与权限';

CREATE TABLE rp_project_context_version (
    id                  BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    project_id          BIGINT UNSIGNED NOT NULL,
    version_no          INT UNSIGNED NOT NULL,
    context_status      VARCHAR(32) NOT NULL DEFAULT 'DRAFT' COMMENT 'DRAFT/PUBLISHED/SUPERSEDED',
    content_json        JSON NOT NULL COMMENT '公司、年份、报告期等全局数据',
    content_hash        CHAR(64) CHARACTER SET ascii COLLATE ascii_bin DEFAULT NULL,
    change_summary      VARCHAR(1000) DEFAULT NULL,
    created_by          BIGINT UNSIGNED DEFAULT NULL,
    created_at          DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    published_at        DATETIME(3) DEFAULT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uk_rp_project_context (project_id, version_no),
    KEY idx_rp_context_status (project_id, context_status),
    CONSTRAINT fk_rp_context_project
        FOREIGN KEY (project_id) REFERENCES rp_project(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='项目全局上下文不可变版本';

-- ============================================================
-- 4. 章节树
-- current_revision_id 的外键在章节版本表创建后补充。
-- sort_key 使用小数排序，拖拽时可取前后章节中间值，减少批量更新。
-- ============================================================
CREATE TABLE rp_chapter (
    id                  BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    project_id          BIGINT UNSIGNED NOT NULL,
    parent_id           BIGINT UNSIGNED DEFAULT NULL COMMENT '父章节，支持多级目录',
    chapter_code        VARCHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '项目内稳定编码',
    current_title       VARCHAR(255) NOT NULL COMMENT '当前显示标题；发布时以章节版本快照为准',
    level_no            SMALLINT UNSIGNED NOT NULL DEFAULT 1 COMMENT '层级缓存',
    sort_key            DECIMAL(20,10) NOT NULL DEFAULT 1000.0000000000 COMMENT '同级排序键',
    workflow_status     VARCHAR(32) NOT NULL DEFAULT 'PENDING' COMMENT 'PENDING/EDITING/BOUND/VALIDATED/READY/APPROVED',
    current_revision_id BIGINT UNSIGNED DEFAULT NULL COMMENT '当前章节版本ID',
    owner_user_id       BIGINT UNSIGNED DEFAULT NULL COMMENT '章节负责人',
    is_enabled          TINYINT(1) NOT NULL DEFAULT 1,
    created_by          BIGINT UNSIGNED DEFAULT NULL,
    created_at          DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    updated_by          BIGINT UNSIGNED DEFAULT NULL,
    updated_at          DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
    row_version         INT UNSIGNED NOT NULL DEFAULT 0 COMMENT '乐观锁版本',
    deleted_at          DATETIME(3) DEFAULT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uk_rp_chapter_code (project_id, chapter_code),
    KEY idx_rp_chapter_tree (project_id, parent_id, sort_key),
    KEY idx_rp_chapter_status (project_id, workflow_status),
    CONSTRAINT fk_rp_chapter_project
        FOREIGN KEY (project_id) REFERENCES rp_project(id) ON DELETE CASCADE,
    CONSTRAINT fk_rp_chapter_parent
        FOREIGN KEY (parent_id) REFERENCES rp_chapter(id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='章节树与当前状态';

-- ============================================================
-- 5. 数据连接、数据源定义、不可变数据快照、可拖拽字段目录
-- ============================================================
CREATE TABLE rp_data_connection (
    id                  BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    project_id          BIGINT UNSIGNED DEFAULT NULL COMMENT '为空表示平台级共享连接',
    connection_name     VARCHAR(255) NOT NULL,
    connection_type     VARCHAR(32) NOT NULL COMMENT 'MYSQL/POSTGRES/API/SFTP/LOCAL',
    config_json         JSON NOT NULL COMMENT '不含密码的连接配置',
    credential_ref      VARCHAR(255) DEFAULT NULL COMMENT '密钥中心引用，禁止存明文密码',
    connection_status   VARCHAR(32) NOT NULL DEFAULT 'ACTIVE' COMMENT 'ACTIVE/DISABLED/ERROR',
    last_tested_at      DATETIME(3) DEFAULT NULL,
    last_test_result    JSON DEFAULT NULL,
    created_by          BIGINT UNSIGNED DEFAULT NULL,
    created_at          DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    updated_at          DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
    row_version         INT UNSIGNED NOT NULL DEFAULT 0,
    deleted_at          DATETIME(3) DEFAULT NULL,
    PRIMARY KEY (id),
    KEY idx_rp_connection_project (project_id, connection_status),
    CONSTRAINT fk_rp_connection_project
        FOREIGN KEY (project_id) REFERENCES rp_project(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='数据连接配置';

CREATE TABLE rp_data_source (
    id                  BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    project_id          BIGINT UNSIGNED NOT NULL,
    connection_id       BIGINT UNSIGNED DEFAULT NULL,
    source_code         VARCHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '项目内稳定编码',
    source_name         VARCHAR(255) NOT NULL,
    source_type         VARCHAR(32) NOT NULL COMMENT 'JSON/EXCEL/CSV/API/DATABASE/MANUAL',
    source_status       VARCHAR(32) NOT NULL DEFAULT 'ACTIVE' COMMENT 'ACTIVE/DISABLED/ERROR',
    config_json         JSON DEFAULT NULL COMMENT '文件Sheet、API路径、受控查询模板等',
    refresh_mode        VARCHAR(32) NOT NULL DEFAULT 'MANUAL' COMMENT 'MANUAL/ON_GENERATE/SCHEDULED',
    schema_json         JSON DEFAULT NULL COMMENT '当前预期结构',
    created_by          BIGINT UNSIGNED DEFAULT NULL,
    created_at          DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    updated_by          BIGINT UNSIGNED DEFAULT NULL,
    updated_at          DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
    row_version         INT UNSIGNED NOT NULL DEFAULT 0,
    deleted_at          DATETIME(3) DEFAULT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uk_rp_data_source_code (project_id, source_code),
    KEY idx_rp_data_source_status (project_id, source_status),
    KEY idx_rp_data_source_connection (connection_id),
    CONSTRAINT fk_rp_ds_project
        FOREIGN KEY (project_id) REFERENCES rp_project(id) ON DELETE CASCADE,
    CONSTRAINT fk_rp_ds_connection
        FOREIGN KEY (connection_id) REFERENCES rp_data_connection(id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='数据源逻辑定义';

CREATE TABLE rp_data_snapshot (
    id                  BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    data_source_id      BIGINT UNSIGNED NOT NULL,
    snapshot_no         BIGINT UNSIGNED NOT NULL COMMENT '数据源内递增快照号',
    snapshot_status     VARCHAR(32) NOT NULL DEFAULT 'CAPTURING' COMMENT 'CAPTURING/READY/FAILED/EXPIRED',
    content_json        JSON DEFAULT NULL COMMENT '小型数据直接保存',
    file_object_id      BIGINT UNSIGNED DEFAULT NULL COMMENT '大型JSON/Excel/CSV等对象文件',
    schema_json         JSON DEFAULT NULL COMMENT '本次快照实际结构',
    content_hash        CHAR(64) CHARACTER SET ascii COLLATE ascii_bin DEFAULT NULL,
    row_count           BIGINT UNSIGNED DEFAULT NULL,
    source_watermark    VARCHAR(255) DEFAULT NULL COMMENT '业务时间戳、数据库SCN、ETag等',
    captured_at         DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    expires_at          DATETIME(3) DEFAULT NULL,
    error_message       VARCHAR(2000) DEFAULT NULL,
    created_by          BIGINT UNSIGNED DEFAULT NULL,
    created_at          DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    PRIMARY KEY (id),
    UNIQUE KEY uk_rp_data_snapshot (data_source_id, snapshot_no),
    KEY idx_rp_snapshot_status_time (data_source_id, snapshot_status, captured_at),
    KEY idx_rp_snapshot_hash (content_hash),
    CONSTRAINT fk_rp_snapshot_source
        FOREIGN KEY (data_source_id) REFERENCES rp_data_source(id) ON DELETE RESTRICT,
    CONSTRAINT fk_rp_snapshot_file
        FOREIGN KEY (file_object_id) REFERENCES rp_file_object(id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='不可变数据快照';

CREATE TABLE rp_data_field (
    id                  BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    snapshot_id         BIGINT UNSIGNED NOT NULL,
    field_path          VARCHAR(1024) COLLATE utf8mb4_bin NOT NULL COMMENT 'JSONPath/字段路径',
    field_name          VARCHAR(255) NOT NULL,
    data_type           VARCHAR(32) NOT NULL COMMENT 'STRING/NUMBER/BOOLEAN/DATE/OBJECT/ARRAY/IMAGE',
    is_array            TINYINT(1) NOT NULL DEFAULT 0,
    is_nullable         TINYINT(1) NOT NULL DEFAULT 1,
    sample_value_json   JSON DEFAULT NULL,
    display_order       INT NOT NULL DEFAULT 0,
    created_at          DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    PRIMARY KEY (id),
    UNIQUE KEY uk_rp_data_field (snapshot_id, field_path(512)),
    KEY idx_rp_data_field_type (snapshot_id, data_type),
    CONSTRAINT fk_rp_field_snapshot
        FOREIGN KEY (snapshot_id) REFERENCES rp_data_snapshot(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='数据快照字段目录，供前端拖拽绑定';

-- ============================================================
-- 6. 绑定配置：绑定集版本 + 明细项
-- JSON只保存复杂格式参数，不把全部映射塞进一个大JSON。
-- ============================================================
CREATE TABLE rp_binding_set (
    id                  BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    chapter_id          BIGINT UNSIGNED NOT NULL,
    version_no          INT UNSIGNED NOT NULL,
    template_version_id BIGINT UNSIGNED NOT NULL COMMENT '绑定必须固定到模板具体版本',
    binding_status      VARCHAR(32) NOT NULL DEFAULT 'DRAFT' COMMENT 'DRAFT/PUBLISHED/SUPERSEDED/INVALID',
    validation_status   VARCHAR(32) NOT NULL DEFAULT 'NOT_VALIDATED' COMMENT 'NOT_VALIDATED/VALID/WARNING/ERROR',
    validation_result_json JSON DEFAULT NULL,
    change_summary      VARCHAR(1000) DEFAULT NULL,
    created_by          BIGINT UNSIGNED DEFAULT NULL,
    created_at          DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    published_by        BIGINT UNSIGNED DEFAULT NULL,
    published_at        DATETIME(3) DEFAULT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uk_rp_binding_set (chapter_id, version_no),
    KEY idx_rp_binding_status (chapter_id, binding_status),
    CONSTRAINT fk_rp_bs_chapter
        FOREIGN KEY (chapter_id) REFERENCES rp_chapter(id) ON DELETE CASCADE,
    CONSTRAINT fk_rp_bs_template_version
        FOREIGN KEY (template_version_id) REFERENCES rp_template_version(id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='章节绑定配置版本';

CREATE TABLE rp_binding_item (
    id                  BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    binding_set_id      BIGINT UNSIGNED NOT NULL,
    template_element_id BIGINT UNSIGNED NOT NULL,
    target_property     VARCHAR(255) COLLATE utf8mb4_bin NOT NULL DEFAULT '$' COMMENT '元素内部目标：text/categories/series[0].values等',
    source_kind         VARCHAR(32) NOT NULL COMMENT 'PROJECT_CONTEXT/DATA_SOURCE/CONSTANT/EXPRESSION',
    data_source_id      BIGINT UNSIGNED DEFAULT NULL,
    source_path         VARCHAR(1024) COLLATE utf8mb4_bin DEFAULT NULL COMMENT '数据字段路径',
    constant_value_json JSON DEFAULT NULL,
    transform_config_json JSON DEFAULT NULL COMMENT '受控转换函数链，禁止任意脚本',
    format_config_json  JSON DEFAULT NULL COMMENT '数字、日期、图表、表格格式',
    fallback_value_json JSON DEFAULT NULL,
    is_required         TINYINT(1) NOT NULL DEFAULT 0,
    sort_no             INT NOT NULL DEFAULT 0,
    created_at          DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    updated_at          DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
    PRIMARY KEY (id),
    UNIQUE KEY uk_rp_binding_target (binding_set_id, template_element_id, target_property),
    KEY idx_rp_binding_source (data_source_id),
    CONSTRAINT fk_rp_bi_set
        FOREIGN KEY (binding_set_id) REFERENCES rp_binding_set(id) ON DELETE CASCADE,
    CONSTRAINT fk_rp_bi_element
        FOREIGN KEY (template_element_id) REFERENCES rp_template_element(id) ON DELETE RESTRICT,
    CONSTRAINT fk_rp_bi_source
        FOREIGN KEY (data_source_id) REFERENCES rp_data_source(id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='具体数据绑定项';

-- ============================================================
-- 7. 章节不可变版本
-- 章节版本固定：标题、模板版本、绑定版本、项目上下文版本、组装配置。
-- ============================================================
CREATE TABLE rp_chapter_revision (
    id                  BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    chapter_id          BIGINT UNSIGNED NOT NULL,
    revision_no         INT UNSIGNED NOT NULL,
    revision_status     VARCHAR(32) NOT NULL DEFAULT 'DRAFT' COMMENT 'DRAFT/PUBLISHED/SUPERSEDED',
    title_snapshot      VARCHAR(255) NOT NULL,
    template_version_id BIGINT UNSIGNED NOT NULL,
    binding_set_id      BIGINT UNSIGNED DEFAULT NULL,
    context_version_no  INT UNSIGNED NOT NULL DEFAULT 0,
    settings_json       JSON DEFAULT NULL COMMENT '分页、方向、页眉页脚、是否入目录等',
    revision_hash       CHAR(64) CHARACTER SET ascii COLLATE ascii_bin DEFAULT NULL,
    change_summary      VARCHAR(1000) DEFAULT NULL,
    created_by          BIGINT UNSIGNED DEFAULT NULL,
    created_at          DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    published_by        BIGINT UNSIGNED DEFAULT NULL,
    published_at        DATETIME(3) DEFAULT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uk_rp_chapter_revision (chapter_id, revision_no),
    KEY idx_rp_chapter_revision_status (chapter_id, revision_status),
    CONSTRAINT fk_rp_cr_chapter
        FOREIGN KEY (chapter_id) REFERENCES rp_chapter(id) ON DELETE CASCADE,
    CONSTRAINT fk_rp_cr_template_version
        FOREIGN KEY (template_version_id) REFERENCES rp_template_version(id) ON DELETE RESTRICT,
    CONSTRAINT fk_rp_cr_binding_set
        FOREIGN KEY (binding_set_id) REFERENCES rp_binding_set(id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='不可变章节版本';

ALTER TABLE rp_chapter
    ADD CONSTRAINT fk_rp_chapter_current_revision
    FOREIGN KEY (current_revision_id) REFERENCES rp_chapter_revision(id) ON DELETE SET NULL;

-- ============================================================
-- 8. 章节编辑租约锁
-- 采用独立锁表 + token + expires_at + heartbeat，不把锁塞进章节业务记录。
-- ============================================================
CREATE TABLE rp_chapter_lock (
    chapter_id          BIGINT UNSIGNED NOT NULL,
    lock_token          CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '客户端持有的租约令牌',
    owner_user_id       BIGINT UNSIGNED NOT NULL,
    lock_type           VARCHAR(32) NOT NULL DEFAULT 'EDIT' COMMENT 'EDIT/BIND/REVIEW',
    acquired_at         DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    heartbeat_at        DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    expires_at          DATETIME(3) NOT NULL,
    lock_version        INT UNSIGNED NOT NULL DEFAULT 0,
    PRIMARY KEY (chapter_id),
    UNIQUE KEY uk_rp_chapter_lock_token (lock_token),
    KEY idx_rp_chapter_lock_expire (expires_at),
    KEY idx_rp_chapter_lock_owner (owner_user_id, expires_at),
    CONSTRAINT fk_rp_lock_chapter
        FOREIGN KEY (chapter_id) REFERENCES rp_chapter(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='章节编辑租约锁';

-- ============================================================
-- 9. 生成任务、任务固定的数据快照、章节子任务、生成产物
-- ============================================================
CREATE TABLE rp_generation_job (
    id                  BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    project_id          BIGINT UNSIGNED NOT NULL,
    job_type            VARCHAR(32) NOT NULL COMMENT 'CHAPTER_PREVIEW/PROJECT_PREVIEW/FINAL',
    job_status          VARCHAR(32) NOT NULL DEFAULT 'QUEUED' COMMENT 'QUEUED/RUNNING/ASSEMBLING/SUCCEEDED/PARTIAL_FAILED/FAILED/CANCELLED',
    requested_by        BIGINT UNSIGNED DEFAULT NULL,
    context_version_no  INT UNSIGNED NOT NULL DEFAULT 0,
    manifest_json       JSON NOT NULL COMMENT '本次生成固定的章节版本、模板版本、绑定版本和参数',
    progress_percent    DECIMAL(5,2) NOT NULL DEFAULT 0.00,
    total_chapters      INT UNSIGNED NOT NULL DEFAULT 0,
    completed_chapters  INT UNSIGNED NOT NULL DEFAULT 0,
    failed_chapters     INT UNSIGNED NOT NULL DEFAULT 0,
    retry_of_job_id     BIGINT UNSIGNED DEFAULT NULL,
    error_code          VARCHAR(64) DEFAULT NULL,
    error_message       VARCHAR(2000) DEFAULT NULL,
    queued_at           DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    started_at          DATETIME(3) DEFAULT NULL,
    finished_at         DATETIME(3) DEFAULT NULL,
    created_at          DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    row_version         INT UNSIGNED NOT NULL DEFAULT 0,
    PRIMARY KEY (id),
    KEY idx_rp_job_project_status (project_id, job_status, created_at),
    KEY idx_rp_job_retry (retry_of_job_id),
    CONSTRAINT fk_rp_job_project
        FOREIGN KEY (project_id) REFERENCES rp_project(id) ON DELETE RESTRICT,
    CONSTRAINT fk_rp_job_retry
        FOREIGN KEY (retry_of_job_id) REFERENCES rp_generation_job(id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='报告生成任务';

CREATE TABLE rp_generation_job_snapshot (
    job_id              BIGINT UNSIGNED NOT NULL,
    data_source_id      BIGINT UNSIGNED NOT NULL,
    data_snapshot_id    BIGINT UNSIGNED NOT NULL,
    selected_at         DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    PRIMARY KEY (job_id, data_source_id),
    KEY idx_rp_job_snapshot_id (data_snapshot_id),
    CONSTRAINT fk_rp_gjs_job
        FOREIGN KEY (job_id) REFERENCES rp_generation_job(id) ON DELETE CASCADE,
    CONSTRAINT fk_rp_gjs_source
        FOREIGN KEY (data_source_id) REFERENCES rp_data_source(id) ON DELETE RESTRICT,
    CONSTRAINT fk_rp_gjs_snapshot
        FOREIGN KEY (data_snapshot_id) REFERENCES rp_data_snapshot(id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='生成任务固定的数据快照';

CREATE TABLE rp_artifact (
    id                  BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    project_id          BIGINT UNSIGNED NOT NULL,
    generation_job_id   BIGINT UNSIGNED DEFAULT NULL,
    chapter_id          BIGINT UNSIGNED DEFAULT NULL,
    file_object_id      BIGINT UNSIGNED NOT NULL,
    artifact_type       VARCHAR(32) NOT NULL COMMENT 'CHAPTER_DOCX/FINAL_DOCX/PDF/PREVIEW_HTML/PREVIEW_IMAGE/LOG',
    artifact_status     VARCHAR(32) NOT NULL DEFAULT 'READY' COMMENT 'CREATING/READY/FAILED/EXPIRED',
    file_format         VARCHAR(32) NOT NULL COMMENT 'DOCX/PDF/HTML/PNG/JSON',
    page_count          INT UNSIGNED DEFAULT NULL,
    metadata_json       JSON DEFAULT NULL,
    created_by          BIGINT UNSIGNED DEFAULT NULL,
    created_at          DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    expires_at          DATETIME(3) DEFAULT NULL,
    deleted_at          DATETIME(3) DEFAULT NULL,
    PRIMARY KEY (id),
    KEY idx_rp_artifact_job (generation_job_id, artifact_type),
    KEY idx_rp_artifact_chapter (chapter_id, created_at),
    KEY idx_rp_artifact_project (project_id, created_at),
    CONSTRAINT fk_rp_artifact_project
        FOREIGN KEY (project_id) REFERENCES rp_project(id) ON DELETE RESTRICT,
    CONSTRAINT fk_rp_artifact_job
        FOREIGN KEY (generation_job_id) REFERENCES rp_generation_job(id) ON DELETE SET NULL,
    CONSTRAINT fk_rp_artifact_chapter
        FOREIGN KEY (chapter_id) REFERENCES rp_chapter(id) ON DELETE SET NULL,
    CONSTRAINT fk_rp_artifact_file
        FOREIGN KEY (file_object_id) REFERENCES rp_file_object(id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='生成产物';

CREATE TABLE rp_generation_job_chapter (
    id                  BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    job_id              BIGINT UNSIGNED NOT NULL,
    chapter_id          BIGINT UNSIGNED NOT NULL,
    chapter_revision_id BIGINT UNSIGNED NOT NULL,
    sequence_no         INT UNSIGNED NOT NULL,
    task_status         VARCHAR(32) NOT NULL DEFAULT 'QUEUED' COMMENT 'QUEUED/RUNNING/SUCCEEDED/FAILED/SKIPPED',
    attempt_no          INT UNSIGNED NOT NULL DEFAULT 0,
    output_artifact_id  BIGINT UNSIGNED DEFAULT NULL,
    error_code          VARCHAR(64) DEFAULT NULL,
    error_message       VARCHAR(2000) DEFAULT NULL,
    started_at          DATETIME(3) DEFAULT NULL,
    finished_at         DATETIME(3) DEFAULT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uk_rp_job_chapter (job_id, chapter_id),
    KEY idx_rp_job_chapter_status (job_id, task_status, sequence_no),
    CONSTRAINT fk_rp_gjc_job
        FOREIGN KEY (job_id) REFERENCES rp_generation_job(id) ON DELETE CASCADE,
    CONSTRAINT fk_rp_gjc_chapter
        FOREIGN KEY (chapter_id) REFERENCES rp_chapter(id) ON DELETE RESTRICT,
    CONSTRAINT fk_rp_gjc_revision
        FOREIGN KEY (chapter_revision_id) REFERENCES rp_chapter_revision(id) ON DELETE RESTRICT,
    CONSTRAINT fk_rp_gjc_artifact
        FOREIGN KEY (output_artifact_id) REFERENCES rp_artifact(id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='生成任务的章节子任务';

-- ============================================================
-- 10. 正式发布与发布章节清单
-- 不使用 ON DELETE CASCADE，历史发布必须可追溯。
-- ============================================================
CREATE TABLE rp_release (
    id                  BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    project_id          BIGINT UNSIGNED NOT NULL,
    release_no          INT UNSIGNED NOT NULL COMMENT '项目内递增发布号',
    version_label       VARCHAR(64) DEFAULT NULL COMMENT '如 v1.0 / 2026-Q3',
    release_status      VARCHAR(32) NOT NULL DEFAULT 'DRAFT' COMMENT 'DRAFT/PUBLISHED/WITHDRAWN',
    generation_job_id   BIGINT UNSIGNED NOT NULL,
    final_artifact_id   BIGINT UNSIGNED NOT NULL,
    manifest_json       JSON NOT NULL COMMENT '完整版本清单与校验哈希',
    release_notes       TEXT DEFAULT NULL,
    created_by          BIGINT UNSIGNED DEFAULT NULL,
    created_at          DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    published_by        BIGINT UNSIGNED DEFAULT NULL,
    published_at        DATETIME(3) DEFAULT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uk_rp_release_no (project_id, release_no),
    KEY idx_rp_release_status (project_id, release_status, created_at),
    CONSTRAINT fk_rp_release_project
        FOREIGN KEY (project_id) REFERENCES rp_project(id) ON DELETE RESTRICT,
    CONSTRAINT fk_rp_release_job
        FOREIGN KEY (generation_job_id) REFERENCES rp_generation_job(id) ON DELETE RESTRICT,
    CONSTRAINT fk_rp_release_artifact
        FOREIGN KEY (final_artifact_id) REFERENCES rp_artifact(id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='正式报告发布记录';

CREATE TABLE rp_release_chapter (
    id                  BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    release_id          BIGINT UNSIGNED NOT NULL,
    chapter_id          BIGINT UNSIGNED NOT NULL,
    chapter_revision_id BIGINT UNSIGNED NOT NULL,
    sequence_no         INT UNSIGNED NOT NULL,
    artifact_id         BIGINT UNSIGNED DEFAULT NULL COMMENT '本次发布对应的章节产物',
    data_snapshot_manifest_json JSON DEFAULT NULL COMMENT '该章节实际使用的数据快照清单',
    PRIMARY KEY (id),
    UNIQUE KEY uk_rp_release_chapter (release_id, chapter_id),
    KEY idx_rp_release_chapter_order (release_id, sequence_no),
    CONSTRAINT fk_rp_rc_release
        FOREIGN KEY (release_id) REFERENCES rp_release(id) ON DELETE RESTRICT,
    CONSTRAINT fk_rp_rc_chapter
        FOREIGN KEY (chapter_id) REFERENCES rp_chapter(id) ON DELETE RESTRICT,
    CONSTRAINT fk_rp_rc_revision
        FOREIGN KEY (chapter_revision_id) REFERENCES rp_chapter_revision(id) ON DELETE RESTRICT,
    CONSTRAINT fk_rp_rc_artifact
        FOREIGN KEY (artifact_id) REFERENCES rp_artifact(id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='发布版本的章节清单';

-- ============================================================
-- 11. 审计日志
-- 审计表不设业务外键，避免源数据删除或迁移破坏历史。
-- 高数据量时可归档到日志库、ClickHouse 或按月分区。
-- ============================================================
CREATE TABLE rp_audit_log (
    id                  BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    project_id          BIGINT UNSIGNED DEFAULT NULL,
    actor_user_id       BIGINT UNSIGNED DEFAULT NULL,
    action_code         VARCHAR(64) NOT NULL COMMENT 'CREATE/UPDATE/DELETE/LOCK/BIND/VALIDATE/GENERATE/PUBLISH等',
    entity_type         VARCHAR(64) NOT NULL COMMENT 'PROJECT/TEMPLATE/CHAPTER/DATA_SOURCE/BINDING/JOB/RELEASE',
    entity_id           BIGINT UNSIGNED DEFAULT NULL,
    request_id          VARCHAR(64) CHARACTER SET ascii COLLATE ascii_bin DEFAULT NULL COMMENT '链路追踪ID',
    before_json         JSON DEFAULT NULL,
    after_json          JSON DEFAULT NULL,
    detail_json         JSON DEFAULT NULL,
    ip_address          VARCHAR(45) DEFAULT NULL,
    user_agent          VARCHAR(512) DEFAULT NULL,
    created_at          DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    PRIMARY KEY (id),
    KEY idx_rp_audit_project_time (project_id, created_at),
    KEY idx_rp_audit_actor_time (actor_user_id, created_at),
    KEY idx_rp_audit_entity (entity_type, entity_id, created_at),
    KEY idx_rp_audit_request (request_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='不可变操作审计日志';

SET FOREIGN_KEY_CHECKS = 1;

-- ============================================================
-- 推荐的锁获取逻辑（应用层事务执行）
-- ============================================================
-- 方案A：
-- 1) START TRANSACTION;
-- 2) SELECT * FROM rp_chapter_lock WHERE chapter_id = ? FOR UPDATE;
-- 3) 若无记录则 INSERT；
--    若 owner_user_id = 当前用户 或 expires_at < NOW(3)，则 UPDATE token/owner/expiry；
--    否则返回“已被锁定”。
-- 4) COMMIT;
--
-- 心跳：
-- UPDATE rp_chapter_lock
-- SET heartbeat_at = NOW(3),
--     expires_at = DATE_ADD(NOW(3), INTERVAL 2 MINUTE),
--     lock_version = lock_version + 1
-- WHERE chapter_id = ?
--   AND lock_token = ?
--   AND owner_user_id = ?;
--
-- 解锁：
-- DELETE FROM rp_chapter_lock
-- WHERE chapter_id = ?
--   AND lock_token = ?
--   AND owner_user_id = ?;
--
-- 所有章节保存同时使用 rp_chapter.row_version 做乐观锁：
-- UPDATE rp_chapter
-- SET current_title = ?,
--     row_version = row_version + 1
-- WHERE id = ?
--   AND row_version = ?;
-- affected_rows = 0 表示发生并发修改。
