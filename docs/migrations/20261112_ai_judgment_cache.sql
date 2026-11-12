-- 20261112 AI 重病判定缓存表（P1，配合 /ai/severity 客户端管线）
-- 数据可迁移，也可重复执行；SchemaService 也会建表 —— Resources\Schema\system_ai\database.yaml 为权威。
-- 缓存策略：仅缓存 LLM 结果（客户端确定性层毫秒级无需缓存）；
--   policy_version = 客户端随包清单 sha256 前16位（SevereDiseaseJudge.PolicyVersion），
--   清单发版后旧缓存读取比对不中自然失效，且会被下次判定 upsert 覆盖，无需批量清理。
-- 纯 DDL 无数据操作（check_migrations 可自动登记）。

BEGIN;

CREATE TABLE IF NOT EXISTS nc_sys_ai_judgments (
    input_key       character varying(300) NOT NULL,
    verdict         character varying(16)  NOT NULL,
    matched_item    character varying(100) NOT NULL DEFAULT '',
    confidence      real                   NOT NULL DEFAULT 0,
    reason          text                   NOT NULL DEFAULT '',
    source          character varying(16)  NOT NULL DEFAULT 'llm',
    policy_version  character varying(16)  NOT NULL,
    model           character varying(64)  NOT NULL DEFAULT '',
    created_at      timestamp with time zone NOT NULL DEFAULT NOW(),
    updated_at      timestamp with time zone NOT NULL DEFAULT NOW(),
    CONSTRAINT pk_sys_ai_judgments PRIMARY KEY (input_key)
);

COMMIT;
