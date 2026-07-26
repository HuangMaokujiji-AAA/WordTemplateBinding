-- ============================================================
-- Report Platform V2.1 -> V2.2 模板片段迁移脚本
-- MySQL 8.0+
--
-- 目标：
-- 1. 新增 rp_template_segment，保存不可变模板版本中的逻辑绑定范围；
-- 2. rp_template_element 增加片段归属和片段内顺序；
-- 3. 保留全部历史模板、模板版本和模板元素数据。
--
-- 执行前：
-- 1. 完整备份 report_platform 数据库；
-- 2. 确认已执行 V2 -> V2.1 数据库文件存储迁移；
-- 3. 确认当前没有正在解析模板或生成片段预览的任务；
-- 4. 本脚本为一次性迁移脚本，不要重复执行；
-- 5. MySQL DDL 会隐式提交，不能依赖事务整体回滚。
-- ============================================================

USE report_platform;

-- 执行前基线：记录模板元素总数；迁移后应保持一致。
SELECT COUNT(*) AS template_element_count_before
FROM rp_template_element;

CREATE TABLE rp_template_segment (
    id                      BIGINT UNSIGNED NOT NULL AUTO_INCREMENT COMMENT '模板片段ID',
    template_version_id     BIGINT UNSIGNED NOT NULL COMMENT '所属不可变模板版本ID',
    parent_segment_id       BIGINT UNSIGNED DEFAULT NULL COMMENT '父片段ID，支持片段树',

    segment_key             VARCHAR(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL
        COMMENT '模板版本内稳定业务键，仅允许小写字母、数字和短横线',
    segment_name            VARCHAR(255) NOT NULL COMMENT '片段显示名称',
    segment_type            VARCHAR(32) NOT NULL DEFAULT 'SECTION'
        COMMENT 'ROOT/SECTION',

    anchor_type             VARCHAR(32) NOT NULL
        COMMENT 'CONTENT_CONTROL/VIRTUAL；书签兼容实现后可使用BOOKMARK_RANGE',
    start_anchor_json       JSON NOT NULL COMMENT '片段起始锚点及定位上下文',
    end_anchor_json         JSON DEFAULT NULL COMMENT '片段结束锚点；内容控件或虚拟片段可为空',

    document_order_start    INT UNSIGNED NOT NULL COMMENT '片段在主文档中的起始顺序',
    document_order_end      INT UNSIGNED NOT NULL COMMENT '片段在主文档中的结束顺序',

    segment_status          VARCHAR(32) NOT NULL DEFAULT 'DRAFT'
        COMMENT 'DRAFT/READY/READY_WITH_WARNINGS/INVALID',
    segment_fingerprint     CHAR(64) CHARACTER SET ascii COLLATE ascii_bin DEFAULT NULL
        COMMENT '片段结构SHA-256指纹',

    preview_file_object_id  BIGINT UNSIGNED DEFAULT NULL COMMENT '片段预览DOCX文件对象ID，仅作缓存',
    preview_status          VARCHAR(32) NOT NULL DEFAULT 'NOT_CREATED'
        COMMENT 'NOT_CREATED/GENERATING/READY/FAILED/STALE',
    preview_error_message   VARCHAR(1000) DEFAULT NULL COMMENT '最近一次预览生成错误',

    sort_no                 INT NOT NULL DEFAULT 0 COMMENT '同级片段显示顺序',
    row_version             INT UNSIGNED NOT NULL DEFAULT 0 COMMENT '乐观锁版本',
    created_by              BIGINT UNSIGNED DEFAULT NULL,
    created_at              DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    updated_by              BIGINT UNSIGNED DEFAULT NULL,
    updated_at              DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3)
        ON UPDATE CURRENT_TIMESTAMP(3),

    PRIMARY KEY (id),
    UNIQUE KEY uk_rp_template_segment (template_version_id, segment_key),
    KEY idx_rp_segment_order (template_version_id, document_order_start),
    KEY idx_rp_segment_parent (parent_segment_id, sort_no),
    KEY idx_rp_segment_preview_file (preview_file_object_id),

    CONSTRAINT fk_rp_segment_version
        FOREIGN KEY (template_version_id)
        REFERENCES rp_template_version(id)
        ON DELETE CASCADE,
    CONSTRAINT fk_rp_segment_parent
        FOREIGN KEY (parent_segment_id)
        REFERENCES rp_template_segment(id)
        ON DELETE RESTRICT,
    CONSTRAINT fk_rp_segment_preview_file
        FOREIGN KEY (preview_file_object_id)
        REFERENCES rp_file_object(id)
        ON DELETE SET NULL,

    CONSTRAINT chk_rp_segment_document_order
        CHECK (document_order_start <= document_order_end)
) ENGINE=InnoDB
  DEFAULT CHARSET=utf8mb4
  COLLATE=utf8mb4_0900_ai_ci
  COMMENT='不可变模板版本中的逻辑绑定片段及预览缓存';

ALTER TABLE rp_template_element
    ADD COLUMN segment_id BIGINT UNSIGNED DEFAULT NULL
        COMMENT '所属最内层模板片段；NULL表示全局元素或尚未重扫'
        AFTER template_version_id,
    ADD COLUMN segment_local_order INT UNSIGNED NOT NULL DEFAULT 0
        COMMENT '元素在所属片段内的文档顺序'
        AFTER sort_no,
    ADD KEY idx_rp_element_segment (segment_id, segment_local_order),
    ADD CONSTRAINT fk_rp_element_segment
        FOREIGN KEY (segment_id)
        REFERENCES rp_template_segment(id)
        ON DELETE SET NULL;

-- 迁移后验证：历史元素总数必须与执行前一致，且历史元素暂不归属片段。
SELECT COUNT(*) AS template_element_count_after,
       SUM(segment_id IS NOT NULL) AS assigned_segment_count,
       SUM(segment_id IS NULL) AS unassigned_or_global_count
FROM rp_template_element;

SELECT COUNT(*) AS template_segment_count
FROM rp_template_segment;

SHOW CREATE TABLE rp_template_segment;
SHOW CREATE TABLE rp_template_element;
