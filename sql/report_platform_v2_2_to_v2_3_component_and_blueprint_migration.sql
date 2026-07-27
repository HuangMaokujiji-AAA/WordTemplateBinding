-- ============================================================
-- Report Platform V2.2 → V2.3
-- 新增：组件契约、报告蓝图与节点
-- 适用数据库：MySQL 8.0+
-- ============================================================

USE report_platform;
SET NAMES utf8mb4;
SET FOREIGN_KEY_CHECKS = 0;

-- ============================================================
-- 1. 组件契约表
-- 记录每个 COMPONENT 类型模板版本的输入输出契约。
-- ============================================================
CREATE TABLE IF NOT EXISTS rp_component_contract (
    template_version_id BIGINT UNSIGNED NOT NULL COMMENT '模板版本ID',
    component_key       VARCHAR(128) NOT NULL COMMENT '组件业务键',
    contract_version    INT UNSIGNED NOT NULL DEFAULT 1 COMMENT '契约版本号',

    input_schema_json   JSON NOT NULL COMMENT '输入数据Schema',
    output_manifest_json JSON NULL COMMENT '输出元素清单',
    slot_schema_json    JSON NULL COMMENT '输出插槽定义',
    repeat_schema_json  JSON NULL COMMENT '内部Repeat块定义',
    condition_schema_json JSON NULL COMMENT '内部Condition块定义',

    created_at          DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),

    PRIMARY KEY (template_version_id),

    CONSTRAINT fk_rp_component_contract_version
        FOREIGN KEY (template_version_id)
        REFERENCES rp_template_version(id)
        ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='组件契约表';

-- ============================================================
-- 2. 报告蓝图主表
-- ============================================================
CREATE TABLE IF NOT EXISTS rp_report_blueprint (
    id                  BIGINT UNSIGNED NOT NULL AUTO_INCREMENT COMMENT '蓝图ID',
    blueprint_code      VARCHAR(64) NOT NULL COMMENT '蓝图编码',
    blueprint_name      VARCHAR(255) NOT NULL COMMENT '蓝图名称',
    description         TEXT NULL COMMENT '蓝图描述',
    blueprint_status    VARCHAR(32) NOT NULL DEFAULT 'ACTIVE' COMMENT 'ACTIVE/ARCHIVED',
    current_version_no  INT UNSIGNED NOT NULL DEFAULT 0 COMMENT '当前版本号',
    row_version         INT UNSIGNED NOT NULL DEFAULT 0 COMMENT '乐观锁版本',
    created_by          BIGINT UNSIGNED NULL COMMENT '创建人',
    created_at          DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    updated_by          BIGINT UNSIGNED NULL COMMENT '更新人',
    updated_at          DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3)
        ON UPDATE CURRENT_TIMESTAMP(3),

    PRIMARY KEY (id),
    UNIQUE KEY uk_rp_blueprint_code (blueprint_code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='报告蓝图主表';

-- ============================================================
-- 3. 蓝图版本表
-- 固定主模板版本和所有组件版本，保证历史报告可复现。
-- ============================================================
CREATE TABLE IF NOT EXISTS rp_report_blueprint_version (
    id                          BIGINT UNSIGNED NOT NULL AUTO_INCREMENT COMMENT '蓝图版本ID',
    blueprint_id                BIGINT UNSIGNED NOT NULL COMMENT '蓝图ID',
    version_no                  INT UNSIGNED NOT NULL COMMENT '版本号，从1递增',
    version_status              VARCHAR(32) NOT NULL DEFAULT 'DRAFT' COMMENT 'DRAFT/PUBLISHED/ARCHIVED',

    master_template_version_id  BIGINT UNSIGNED NOT NULL COMMENT '主模板版本ID',
    config_json                 JSON NULL COMMENT '蓝图配置',
    dependency_hash             CHAR(64) NULL COMMENT '依赖哈希SHA-256',

    created_by                  BIGINT UNSIGNED NULL,
    created_at                  DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    published_by                BIGINT UNSIGNED NULL,
    published_at                DATETIME(3) NULL,

    PRIMARY KEY (id),

    UNIQUE KEY uk_rp_blueprint_version (
        blueprint_id,
        version_no
    ),

    CONSTRAINT fk_rp_blueprint_version_blueprint
        FOREIGN KEY (blueprint_id)
        REFERENCES rp_report_blueprint(id)
        ON DELETE CASCADE,

    CONSTRAINT fk_rp_blueprint_master_template
        FOREIGN KEY (master_template_version_id)
        REFERENCES rp_template_version(id)
        ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='蓝图版本表';

-- ============================================================
-- 4. 蓝图节点表
-- 表示蓝图中的每个组件节点，支持树形结构和排序。
-- ============================================================
CREATE TABLE IF NOT EXISTS rp_report_blueprint_node (
    id                      BIGINT UNSIGNED NOT NULL AUTO_INCREMENT COMMENT '节点ID',
    blueprint_version_id    BIGINT UNSIGNED NOT NULL COMMENT '蓝图版本ID',
    parent_node_id          BIGINT UNSIGNED NULL COMMENT '父节点ID',

    node_key                VARCHAR(128) NOT NULL COMMENT '节点稳定键',
    node_name               VARCHAR(255) NOT NULL COMMENT '节点名称',
    node_type               VARCHAR(32) NOT NULL COMMENT 'STATIC_COMPONENT/REPEAT_COMPONENT/CONDITIONAL_COMPONENT/GROUP/SLOT_REFERENCE',

    template_version_id     BIGINT UNSIGNED NULL COMMENT '组件模板版本ID',
    target_slot_key         VARCHAR(128) NULL COMMENT '目标插槽键',

    data_scope_path         VARCHAR(1024) NULL COMMENT '数据作用域路径',
    item_alias              VARCHAR(64) NULL COMMENT '循环项别名',
    item_key_path           VARCHAR(1024) NULL COMMENT '实例键路径',

    condition_config_json   JSON NULL COMMENT '条件配置',
    assembly_config_json    JSON NULL COMMENT '装配配置',

    sort_key                DECIMAL(20,10) NOT NULL DEFAULT 1000.0000000000 COMMENT '排序键',
    is_enabled              TINYINT(1) NOT NULL DEFAULT 1 COMMENT '是否启用',

    PRIMARY KEY (id),

    UNIQUE KEY uk_rp_blueprint_node (
        blueprint_version_id,
        node_key
    ),

    KEY idx_rp_blueprint_node_tree (
        blueprint_version_id,
        parent_node_id,
        sort_key
    ),

    CONSTRAINT fk_rp_blueprint_node_version
        FOREIGN KEY (blueprint_version_id)
        REFERENCES rp_report_blueprint_version(id)
        ON DELETE CASCADE,

    CONSTRAINT fk_rp_blueprint_node_parent
        FOREIGN KEY (parent_node_id)
        REFERENCES rp_report_blueprint_node(id)
        ON DELETE RESTRICT,

    CONSTRAINT fk_rp_blueprint_node_template
        FOREIGN KEY (template_version_id)
        REFERENCES rp_template_version(id)
        ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='蓝图节点表';

SET FOREIGN_KEY_CHECKS = 1;
