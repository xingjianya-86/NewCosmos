-- 死亡联动待办表（2026-10-31 方案2：申报自动生成各域待办，有权限者承接）
-- 双轨：Schema 权威定义在 Resources/Schema/death_declarations/database.yaml（SchemaService 幂等建表），
--       本脚本供现网库直接执行（新装库由 SchemaService 自动建，执行本脚本亦幂等无害）。
-- 定位：派活表，非办结回写表——办结以目标域实时状态判定，status=Completed 仅为读取自愈标记。

CREATE TABLE IF NOT EXISTS nc_biz_death_linkage_tasks (
    id                BIGSERIAL PRIMARY KEY,
    declaration_id    BIGINT NOT NULL REFERENCES nc_biz_death_declarations(id),
    deceased_id_card  varchar(18) NOT NULL,
    deceased_name     varchar(100) NOT NULL,
    task_domain       varchar(30) NOT NULL,
    ref_application_id BIGINT NULL,
    ref_elderly_id    BIGINT NULL,
    permission_code   varchar(50) NOT NULL,
    status            varchar(20) NOT NULL DEFAULT 'Pending',
    village_id        integer NULL,
    town_id           integer NULL,
    created_at        timestamp NULL DEFAULT CURRENT_TIMESTAMP,
    completed_at      timestamp NULL
);

COMMENT ON TABLE  nc_biz_death_linkage_tasks IS '[流程] 死亡联动待办 - 死亡申报触发的跨域办理任务（高龄停发/户主死亡变更/成员减员/大学生退出/追缴结案）';
COMMENT ON COLUMN nc_biz_death_linkage_tasks.declaration_id   IS '来源申报ID（申报作废/归档时任务随 JOIN 失效，不物理删除）';
COMMENT ON COLUMN nc_biz_death_linkage_tasks.task_domain      IS '任务域（ElderlyStop/ArchiveHead/ArchiveMember/College/Recovery）';
COMMENT ON COLUMN nc_biz_death_linkage_tasks.permission_code  IS '承接所需权限码（承接面按此过滤=有权限者可见可办）';
COMMENT ON COLUMN nc_biz_death_linkage_tasks.status           IS '状态（Pending=待办 / Completed=已办结——读取时按目标域实时状态自愈）';
COMMENT ON COLUMN nc_biz_death_linkage_tasks.completed_at     IS '办结标记时间（读取自愈时写入，非人工操作）';

CREATE INDEX IF NOT EXISTS idx_biz_death_task_pending    ON nc_biz_death_linkage_tasks (status, task_domain);
CREATE INDEX IF NOT EXISTS idx_biz_death_task_id_card    ON nc_biz_death_linkage_tasks (deceased_id_card);
CREATE INDEX IF NOT EXISTS idx_biz_death_task_declaration ON nc_biz_death_linkage_tasks (declaration_id);
