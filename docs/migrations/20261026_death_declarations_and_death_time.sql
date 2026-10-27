-- 2026-10-26 死亡申报台账 + 既有死亡登记表补「死亡确切时刻」
--
-- 背景：村内需要出具《死亡情况说明》，要求留痕「谁在什么时候什么时间，申报了谁的死亡」，
--       且死亡要有确切时间（时分）。既有 nc_biz_death_records 只有 death_date(date)、
--       且 application_id NOT NULL（无档案的普通村民写不进），故新增独立申报台账。
-- 权威定义：新表结构以 Resources/Schema/death_declarations/database.yaml 为单一事实来源，
--           本文件为部署期幂等副本（与 YAML 同步维护，避免未跑 Schema 初始化时缺表）；
--           nc_biz_death_records 补列以本文件 + Resources/Schema/death_records/database.yaml 同步维护。
-- 口径：
--   · 新表 death_time NOT NULL（新表全量新数据）；
--   · 旧表 death_time 允许 NULL：存量行无法回填真实时刻，新写入必填由应用层双门校验
--     （HouseholdDeathChangeViewModel / MemberChangeReasonPopupViewModel UI 必填 + ChangeService 入参校验）；
--   · 允许同一死者多次申报（无唯一约束），重复由提交前校验 + 提醒规则检测，作废走 status='Revoked'。
-- 说明：只做建表/加列/建索引/建外键，无 DROP/TRUNCATE；可重复执行（幂等）。
-- 执行：psql -h <host> -p 5432 -U <user> -d <db> -f docs/migrations/20261026_death_declarations_and_death_time.sql

BEGIN;

-- ① 既有死亡登记表：补「死亡确切时刻」（存量可空）
ALTER TABLE nc_biz_death_records ADD COLUMN IF NOT EXISTS death_time time without time zone;
COMMENT ON COLUMN nc_biz_death_records.death_time IS '死亡确切时刻（新写入必填；存量历史行为 NULL=未采集）';

-- ② 新表：死亡申报台账
CREATE TABLE IF NOT EXISTS nc_biz_death_declarations (
    id                   BIGSERIAL PRIMARY KEY,
    -- 死者（申报了谁）
    deceased_name        varchar(100)  NOT NULL,
    deceased_id_card     varchar(18)   NOT NULL,
    deceased_relation    varchar(50),
    -- 死亡确切时间
    death_date           date          NOT NULL,
    death_time           time without time zone NOT NULL,
    death_reason         varchar(200),
    death_certificate_no varchar(50),
    death_place          varchar(200),
    -- 申报（谁在什么时候申报）
    declared_by_name     varchar(100)  NOT NULL,
    declared_by_id_card  varchar(18),
    declared_by_phone    varchar(20),
    declared_by_type     varchar(20)   NOT NULL,
    declared_at          timestamp     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    declare_channel      varchar(20)   NOT NULL DEFAULT 'PC',
    status               varchar(20)   NOT NULL DEFAULT 'Pending',
    -- 关联（可空：无档案的普通村民也能申报）
    application_id       bigint,
    death_record_id      bigint,
    -- 归属与系统
    village_id           integer,
    town_id              integer,
    village_name         varchar(100),
    operator_name        varchar(100)  NOT NULL,
    remark               text,
    created_by           varchar(100),
    created_at           timestamp     DEFAULT CURRENT_TIMESTAMP,
    updated_at           timestamp,
    deleted_at           timestamp
);

COMMENT ON TABLE  nc_biz_death_declarations IS '[流程] 死亡申报台账 - 村内死亡申报留痕（可无关联档案，允许多次申报+作废）';
COMMENT ON COLUMN nc_biz_death_declarations.death_time        IS '死亡确切时刻（新表全量新数据，必填）';
COMMENT ON COLUMN nc_biz_death_declarations.declared_by_name  IS '申报人姓名（与 operator_name 录入者分列）';
COMMENT ON COLUMN nc_biz_death_declarations.declared_at       IS '申报（受理）时间';
COMMENT ON COLUMN nc_biz_death_declarations.declare_channel   IS '申报渠道（PC/Android/Window）';
COMMENT ON COLUMN nc_biz_death_declarations.status            IS '状态（Pending 待核实 / Verified 已核实 / Revoked 已作废）';
COMMENT ON COLUMN nc_biz_death_declarations.death_record_id   IS '关联死亡登记ID（一期不回填，靠 deceased_id_card 语义关联）';

-- ③ 索引（幂等）
CREATE INDEX IF NOT EXISTS idx_biz_decl_id_card     ON nc_biz_death_declarations (deceased_id_card);
CREATE INDEX IF NOT EXISTS idx_biz_decl_death_date  ON nc_biz_death_declarations (death_date);
CREATE INDEX IF NOT EXISTS idx_biz_decl_village     ON nc_biz_death_declarations (village_id);
CREATE INDEX IF NOT EXISTS idx_biz_decl_declared_at ON nc_biz_death_declarations (declared_at);

-- ④ 外键（幂等，全部可空；补建失败只告警不中断）
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_biz_decl_application') THEN
        ALTER TABLE nc_biz_death_declarations
            ADD CONSTRAINT fk_biz_decl_application
            FOREIGN KEY (application_id) REFERENCES nc_biz_applications (id);
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_biz_decl_death_record') THEN
        ALTER TABLE nc_biz_death_declarations
            ADD CONSTRAINT fk_biz_decl_death_record
            FOREIGN KEY (death_record_id) REFERENCES nc_biz_death_records (id);
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_biz_decl_village') THEN
        ALTER TABLE nc_biz_death_declarations
            ADD CONSTRAINT fk_biz_decl_village
            FOREIGN KEY (village_id) REFERENCES nc_regions_villages (id);
    END IF;
END $$;

COMMIT;

-- ⑤ 核对
\pset pager off
SELECT column_name, data_type, is_nullable, column_default
FROM information_schema.columns
WHERE table_name = 'nc_biz_death_declarations'
ORDER BY ordinal_position;

SELECT column_name, data_type, is_nullable
FROM information_schema.columns
WHERE table_name = 'nc_biz_death_records' AND column_name = 'death_time';
