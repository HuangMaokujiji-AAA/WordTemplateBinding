-- ============================================================
-- 高校本科专业教学质量监测数据 - MySQL 8.0+
-- 数据来源：docs/高教数据
-- 报告年度：2024
--
-- 设计说明：
-- 1. 表名、字段名和索引名统一使用英文 snake_case，数据值保持不变。
-- 2. 统一使用 utf8mb4，并将导出脚本中过宽/已废弃的数值类型收敛为业务类型。
-- 3. 每张表包含自增主键、业务唯一键和常用查询索引。
-- 4. 原始数据缺少年度的 4 张表统一补充 `collection_year`，值由插入脚本显式写入 2024。
-- 5. 指标表包含“全省同专业”等汇总行，因此不建立到学校表的外键。
-- ============================================================

USE report_platform;

SET NAMES utf8mb4;
SET FOREIGN_KEY_CHECKS = 0;

-- 删除已导入的中文旧表
DROP TABLE IF EXISTS `data_专业监测_专业三级指标`;
DROP TABLE IF EXISTS `data_专业监测_专业二级指标`;
DROP TABLE IF EXISTS `data_专业监测_专业一级指标`;
DROP TABLE IF EXISTS `data_专业监测_专业所在学院信息`;
DROP TABLE IF EXISTS `data_专业监测_黄牌预警专业`;
DROP TABLE IF EXISTS `data_专业监测_优势特色专业`;
DROP TABLE IF EXISTS `data_专业监测_教学重点数据`;
DROP TABLE IF EXISTS `data_专业监测_本科专业信息`;
DROP TABLE IF EXISTS `data_专业监测_学校概要信息`;

-- 删除可能存在的英文表，保证脚本可重复执行
DROP TABLE IF EXISTS `he_major_level3_metric`;
DROP TABLE IF EXISTS `he_major_level2_metric`;
DROP TABLE IF EXISTS `he_major_level1_metric`;
DROP TABLE IF EXISTS `he_major_college`;
DROP TABLE IF EXISTS `he_warning_major`;
DROP TABLE IF EXISTS `he_featured_major`;
DROP TABLE IF EXISTS `he_teaching_key_metric`;
DROP TABLE IF EXISTS `he_undergraduate_major_summary`;
DROP TABLE IF EXISTS `he_school_overview`;

CREATE TABLE `he_school_overview` (
    `id` BIGINT UNSIGNED NOT NULL AUTO_INCREMENT COMMENT '主键',
    `collection_year` CHAR(4) NOT NULL,
    `school_code` VARCHAR(32) NOT NULL,
    `school_name` VARCHAR(128) NOT NULL,
    `institution_type` VARCHAR(64) DEFAULT NULL,
    `institution_category` VARCHAR(64) DEFAULT NULL,
    `sponsor_type` VARCHAR(128) DEFAULT NULL,
    `supervising_authority` VARCHAR(128) DEFAULT NULL,
    `undergraduate_education_start_year` CHAR(4) DEFAULT NULL,
    `private_sponsor_name` VARCHAR(255) DEFAULT NULL,
    `institution_level` VARCHAR(128) DEFAULT NULL,
    `total_major_count` INT UNSIGNED NOT NULL DEFAULT 0,
    `new_major_count` INT UNSIGNED DEFAULT NULL,
    `undergraduate_student_count` INT UNSIGNED DEFAULT NULL,
    `full_time_student_count` INT UNSIGNED DEFAULT NULL,
    `equivalent_student_count` DECIMAL(12,2) DEFAULT NULL,
    `staff_count` INT UNSIGNED NOT NULL DEFAULT 0,
    `full_time_teacher_count` INT UNSIGNED DEFAULT NULL,
    `national_teaching_master_count` INT UNSIGNED DEFAULT NULL,
    `provincial_teaching_master_count` INT UNSIGNED DEFAULT NULL,
    `national_teaching_team_count` INT UNSIGNED DEFAULT NULL,
    `provincial_teaching_team_count` INT UNSIGNED DEFAULT NULL,
    PRIMARY KEY (`id`),
    UNIQUE KEY `uk_he_school_overview_year_school` (`collection_year`, `school_code`),
    KEY `idx_he_school_overview_name` (`school_name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci
  COMMENT='学校概要信息';

CREATE TABLE `he_undergraduate_major_summary` (
    `id` BIGINT UNSIGNED NOT NULL AUTO_INCREMENT COMMENT '主键',
    `collection_year` CHAR(4) NOT NULL,
    `school_code` VARCHAR(32) NOT NULL,
    `school_name` VARCHAR(128) NOT NULL,
    `undergraduate_major_count` INT UNSIGNED NOT NULL,
    `new_major_count` INT UNSIGNED DEFAULT NULL,
    `discipline_code` VARCHAR(8) NOT NULL,
    `discipline_category` VARCHAR(32) NOT NULL,
    `major_count` INT UNSIGNED DEFAULT NULL,
    `percentage` DECIMAL(7,2) DEFAULT NULL,
    PRIMARY KEY (`id`),
    UNIQUE KEY `uk_he_major_summary_year_school_discipline` (`collection_year`, `school_code`, `discipline_code`),
    KEY `idx_he_major_summary_year_school` (`collection_year`, `school_code`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci
  COMMENT='学校本科专业门类统计';

CREATE TABLE `he_teaching_key_metric` (
    `id` BIGINT UNSIGNED NOT NULL AUTO_INCREMENT COMMENT '主键',
    `school_name` VARCHAR(128) NOT NULL,
    `collection_year` CHAR(4) NOT NULL,
    `school_code` VARCHAR(32) NOT NULL,
    `display_order` VARCHAR(8) NOT NULL,
    `metric_name` VARCHAR(128) NOT NULL,
    `metric_value` DECIMAL(12,2) DEFAULT NULL,
    PRIMARY KEY (`id`),
    UNIQUE KEY `uk_he_teaching_metric_year_school_order` (`collection_year`, `school_code`, `display_order`),
    KEY `idx_he_teaching_metric_year_school` (`collection_year`, `school_code`),
    KEY `idx_he_teaching_metric_name` (`metric_name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci
  COMMENT='学校教学重点指标数据';

CREATE TABLE `he_featured_major` (
    `id` BIGINT UNSIGNED NOT NULL AUTO_INCREMENT COMMENT '主键',
    `school_name` VARCHAR(128) NOT NULL,
    `collection_year` CHAR(4) NOT NULL,
    `school_code` VARCHAR(32) NOT NULL,
    `major_code` VARCHAR(32) NOT NULL,
    `major_name` VARCHAR(128) NOT NULL,
    `degree_category` VARCHAR(32) NOT NULL,
    `is_new_major` VARCHAR(8) NOT NULL,
    `status` VARCHAR(32) NOT NULL,
    PRIMARY KEY (`id`),
    UNIQUE KEY `uk_he_featured_major_year_school_major` (`collection_year`, `school_code`, `major_code`),
    KEY `idx_he_featured_major_year_school` (`collection_year`, `school_code`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci
  COMMENT='优势特色专业';

CREATE TABLE `he_warning_major` (
    `id` BIGINT UNSIGNED NOT NULL AUTO_INCREMENT COMMENT '主键',
    `school_name` VARCHAR(128) NOT NULL,
    `collection_year` CHAR(4) NOT NULL,
    `school_code` VARCHAR(32) NOT NULL,
    `major_code` VARCHAR(32) NOT NULL,
    `major_name` VARCHAR(128) NOT NULL,
    `degree_category` VARCHAR(32) NOT NULL,
    `is_new_major` VARCHAR(8) NOT NULL,
    `status` VARCHAR(32) NOT NULL,
    PRIMARY KEY (`id`),
    UNIQUE KEY `uk_he_warning_major_year_school_major` (`collection_year`, `school_code`, `major_code`),
    KEY `idx_he_warning_major_year_school` (`collection_year`, `school_code`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci
  COMMENT='黄牌预警专业';

CREATE TABLE `he_major_college` (
    `id` BIGINT UNSIGNED NOT NULL AUTO_INCREMENT COMMENT '主键',
    `collection_year` CHAR(4) NOT NULL COMMENT '原始表缺少年度，由导入脚本按报告年度补充',
    `school_code` VARCHAR(32) NOT NULL,
    `school_name` VARCHAR(128) NOT NULL,
    `college_code` VARCHAR(32) NOT NULL,
    `college_name` VARCHAR(128) NOT NULL,
    `major_code` VARCHAR(32) NOT NULL,
    `major_name` VARCHAR(128) NOT NULL,
    `degree_category` VARCHAR(32) DEFAULT NULL,
    `major_establishment_years` VARCHAR(32) DEFAULT NULL COMMENT '源数据可能包含多个年份，按文本保存',
    `is_new_major` VARCHAR(8) DEFAULT NULL,
    `student_count` INT UNSIGNED DEFAULT NULL,
    `full_time_teacher_count` INT UNSIGNED DEFAULT NULL,
    `monitoring_status` VARCHAR(32) DEFAULT NULL,
    `monitoring_result` VARCHAR(64) DEFAULT NULL,
    `major_rank` INT UNSIGNED NOT NULL,
    `ranked_major_count` INT UNSIGNED DEFAULT NULL,
    `rank_ratio` DECIMAL(10,6) DEFAULT NULL,
    PRIMARY KEY (`id`),
    UNIQUE KEY `uk_he_major_college_year_school_college_major`
        (`collection_year`, `school_code`, `college_code`, `major_code`),
    KEY `idx_he_major_college_year_school` (`collection_year`, `school_code`),
    KEY `idx_he_major_college_year_major` (`collection_year`, `major_code`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci
  COMMENT='专业所在学院及监测结果';

CREATE TABLE `he_major_level1_metric` (
    `id` BIGINT UNSIGNED NOT NULL AUTO_INCREMENT COMMENT '主键',
    `collection_year` CHAR(4) NOT NULL COMMENT '原始表缺少年度，由导入脚本按报告年度补充',
    `school_code` VARCHAR(32) NOT NULL COMMENT '包含真实学校代码及全省汇总标识',
    `school_name` VARCHAR(128) NOT NULL,
    `college_code` VARCHAR(32) NOT NULL,
    `college_name` VARCHAR(128) NOT NULL,
    `display_order` CHAR(2) NOT NULL,
    `major_code` VARCHAR(32) NOT NULL,
    `major_name` VARCHAR(128) NOT NULL,
    `s1_employment_score` DECIMAL(7,2) NOT NULL,
    `s2_admission_score` DECIMAL(7,2) NOT NULL,
    `s3_cultivation_score` DECIMAL(7,2) NOT NULL,
    `total_score` DECIMAL(7,2) NOT NULL,
    PRIMARY KEY (`id`),
    UNIQUE KEY `uk_he_level1_metric_business_row`
        (`collection_year`, `school_code`, `college_code`, `display_order`, `major_code`),
    KEY `idx_he_level1_metric_year_school` (`collection_year`, `school_code`, `display_order`),
    KEY `idx_he_level1_metric_year_major` (`collection_year`, `major_code`, `display_order`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci
  COMMENT='专业监测一级指标';

CREATE TABLE `he_major_level2_metric` (
    `id` BIGINT UNSIGNED NOT NULL AUTO_INCREMENT COMMENT '主键',
    `collection_year` CHAR(4) NOT NULL COMMENT '原始表缺少年度，由导入脚本按报告年度补充',
    `school_code` VARCHAR(32) NOT NULL COMMENT '包含真实学校代码及全省汇总标识',
    `school_name` VARCHAR(128) NOT NULL,
    `college_code` VARCHAR(32) NOT NULL,
    `college_name` VARCHAR(128) NOT NULL,
    `display_order` CHAR(2) NOT NULL,
    `major_code` VARCHAR(32) NOT NULL,
    `major_name` VARCHAR(128) NOT NULL,
    `s11_graduate_destination_score` DECIMAL(7,2) NOT NULL,
    `s21_admission_plan_score` DECIMAL(7,2) NOT NULL,
    `s22_student_source_quality_score` DECIMAL(7,2) NOT NULL,
    `s23_major_recognition_score` DECIMAL(7,2) NOT NULL,
    `s31_ideological_education_score` DECIMAL(7,2) NOT NULL,
    `s32_teaching_input_score` DECIMAL(7,2) NOT NULL,
    `s33_faculty_team_score` DECIMAL(7,2) NOT NULL,
    `s34_major_development_score` DECIMAL(7,2) NOT NULL,
    `s35_cultivation_outcome_score` DECIMAL(7,2) NOT NULL,
    `total_score` DECIMAL(7,2) NOT NULL,
    PRIMARY KEY (`id`),
    UNIQUE KEY `uk_he_level2_metric_business_row`
        (`collection_year`, `school_code`, `college_code`, `display_order`, `major_code`),
    KEY `idx_he_level2_metric_year_school` (`collection_year`, `school_code`, `display_order`),
    KEY `idx_he_level2_metric_year_major` (`collection_year`, `major_code`, `display_order`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci
  COMMENT='专业监测二级指标';

CREATE TABLE `he_major_level3_metric` (
    `id` BIGINT UNSIGNED NOT NULL AUTO_INCREMENT COMMENT '主键',
    `collection_year` CHAR(4) NOT NULL COMMENT '原始表缺少年度，由导入脚本按报告年度补充',
    `school_code` VARCHAR(32) NOT NULL COMMENT '包含真实学校代码及全省汇总标识',
    `school_name` VARCHAR(128) NOT NULL,
    `college_code` VARCHAR(32) NOT NULL,
    `college_name` VARCHAR(128) NOT NULL,
    `display_order` CHAR(2) NOT NULL,
    `major_code` VARCHAR(32) NOT NULL,
    `major_name` VARCHAR(128) NOT NULL,
    `s111_destination_implementation_rate_score` DECIMAL(7,2) NOT NULL,
    `s112_job_relevance_rate_score` DECIMAL(7,2) NOT NULL,
    `s211_admission_plan_completion_rate_score` DECIMAL(7,2) NOT NULL,
    `s221_student_source_quality_score` DECIMAL(7,2) NOT NULL,
    `s231_first_choice_admission_rate_score` DECIMAL(7,2) NOT NULL,
    `s232_net_transfer_in_ratio_score` DECIMAL(7,2) NOT NULL,
    `s311_ideological_course_offering_score` DECIMAL(7,2) NOT NULL,
    `s312_curriculum_ideology_development_score` DECIMAL(7,2) NOT NULL,
    `s321_professor_undergraduate_teaching_rate_score` DECIMAL(7,2) NOT NULL,
    `s322_senior_title_course_teaching_ratio_score` DECIMAL(7,2) NOT NULL,
    `s323_thesis_supervision_per_teacher_score` DECIMAL(7,2) NOT NULL,
    `s331_major_student_teacher_ratio_score` DECIMAL(7,2) NOT NULL,
    `s332_counselor_student_teacher_ratio_score` DECIMAL(7,2) NOT NULL,
    `s333_master_degree_teacher_ratio_score` DECIMAL(7,2) NOT NULL,
    `s334_associate_senior_teacher_ratio_score` DECIMAL(7,2) NOT NULL,
    `s335_industry_background_teacher_ratio_score` DECIMAL(7,2) NOT NULL,
    `s336_high_level_teacher_ratio_score` DECIMAL(7,2) NOT NULL,
    `s337_major_leader_score` DECIMAL(7,2) NOT NULL,
    `s341_featured_major_score` DECIMAL(7,2) NOT NULL,
    `s342_provincial_teaching_reform_score` DECIMAL(7,2) NOT NULL,
    `s343_teaching_research_achievement_score` DECIMAL(7,2) NOT NULL,
    `s344_high_quality_textbook_score` DECIMAL(7,2) NOT NULL,
    `s351_patent_publication_score` DECIMAL(7,2) NOT NULL,
    `s352_provincial_competition_award_score` DECIMAL(7,2) NOT NULL,
    `total_score` DECIMAL(7,2) NOT NULL,
    PRIMARY KEY (`id`),
    UNIQUE KEY `uk_he_level3_metric_business_row`
        (`collection_year`, `school_code`, `college_code`, `display_order`, `major_code`),
    KEY `idx_he_level3_metric_year_school` (`collection_year`, `school_code`, `display_order`),
    KEY `idx_he_level3_metric_year_major` (`collection_year`, `major_code`, `display_order`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci
  COMMENT='专业监测三级指标';

SET FOREIGN_KEY_CHECKS = 1;
