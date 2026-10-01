-- ============================================================================
-- 存量数据修复：动态管理流程补全修复（时区统一到本地 + first_approved_at 语义 + 自引用）
--
-- 背景：
--   ① 时区：DB 会话时区原为 UTC，SQL NOW() 存 UTC 裸值；C# 用 DateTime.UtcNow/Today
--      写入本地墙钟。两者混存 → 月报 B 线周期（本地日期边界）比较偏差 8 小时。
--      代码侧已改为连接串 Timezone=Asia/Shanghai + DateTime.Now（本地墙钟），
--      本脚本一次性把存量 UTC 值 +8h 转为本地墙钟。
--   ② first_approved_at 语义应为"纳入时间"，导入档案应回填建档时间（enrolledDate），
--      而非归档时刻。否则存量导入户被月报"新增救助"口径计成当期新增。
--   ③ change_records.new_application_id 自引用（= application_id）破坏档案链归一。
--
-- 排除项（不迁移，见下）：
--   - nc_biz_applications.created_at：语义为"纳入时间"（导入档案存 enrolledDate 纯日期），
--     +8h 会把 2024-10-01 变成 2024-10-01 08:00:00，破坏日期语义。
--   - 一切 DATE 类型列（stop_date / change_date）：由 DateTime.Today 写入本地日期，本就正确。
--
-- 执行：psql -h <host> -U new_cosmos -d new_cosmos -1 -f 本文件
-- 回滚：pg_restore -c -d new_cosmos 全库备份 new_cosmos_full.dump
-- ============================================================================

BEGIN;

-- ────────────────────────────────────────────────────────────────────────────
-- 一、时区迁移：timestamp 列 +8 小时（UTC 墙钟 → 本地墙钟）
--     仅迁移"时刻"语义列；跳过 nc_biz_applications.created_at（纳入时间）。
-- ────────────────────────────────────────────────────────────────────────────

-- 业务主表：审批/提交/补全/更新/软删 时刻
UPDATE nc_biz_applications
   SET first_approved_at = first_approved_at + interval '8 hours'
 WHERE first_approved_at IS NOT NULL;

UPDATE nc_biz_applications
   SET submit_at = submit_at + interval '8 hours'
 WHERE submit_at IS NOT NULL;

UPDATE nc_biz_applications
   SET data_completed_at = data_completed_at + interval '8 hours'
 WHERE data_completed_at IS NOT NULL;

UPDATE nc_biz_applications
   SET updated_at = updated_at + interval '8 hours'
 WHERE updated_at IS NOT NULL;

UPDATE nc_biz_applications
   SET deleted_at = deleted_at + interval '8 hours'
 WHERE deleted_at IS NOT NULL;

-- created_at 为混合来源，需按值区分（**不**无条件迁移）：
--   • 纯日期（00:00:00）= 纳入时间 enrolledDate / 建档日期回填，本地语义 → 跳过
--   • 带时分秒 = C# DateTime.UtcNow 写入的真实时刻（UTC）→ +8h
-- 这是全脚本唯一一处"按值判断"的迁移，理由：该列同时承载两种语义。
UPDATE nc_biz_applications
   SET created_at = created_at + interval '8 hours'
 WHERE created_at IS NOT NULL
   AND created_at::time <> time '00:00:00';

-- 变更记录：变更时刻
UPDATE nc_biz_change_records
   SET changed_at = changed_at + interval '8 hours'
 WHERE changed_at IS NOT NULL;

UPDATE nc_biz_change_records
   SET deleted_at = deleted_at + interval '8 hours'
 WHERE deleted_at IS NOT NULL;

-- 状态流转审计留痕
UPDATE nc_biz_application_logs
   SET operated_at = operated_at + interval '8 hours'
 WHERE operated_at IS NOT NULL;

-- ────────────────────────────────────────────────────────────────────────────
-- 二、first_approved_at 语义修正：导入档案回填"纳入时间"（created_at = enrolledDate）
--     规则（与代码一致）：
--       导入建档/补全（source_type='ImportedArchive'）→ 纳入时间 = created_at
--       普通建档归档 → 审批时刻（一中的 +8h 值，保持不动）
--     注意顺序：必须在"一、时区迁移"之后执行，此处用 created_at（未迁移的纳入时间）覆盖。
-- ────────────────────────────────────────────────────────────────────────────

UPDATE nc_biz_applications
   SET first_approved_at = created_at
 WHERE source_type = 'ImportedArchive'
   AND deleted_at IS NULL
   AND created_at IS NOT NULL
   AND (first_approved_at IS NULL OR first_approved_at <> created_at);

-- ────────────────────────────────────────────────────────────────────────────
-- 三、change_records.new_application_id 自引用修正
--     无重建（经济复核同档更新）时代码把自身 ID 写成了新档 ID，形成
--     new_application_id = application_id 的自引用，档案链归一无法区分"无新档"。
--     代码侧已改为无重建写 NULL，此处修存量。
-- ────────────────────────────────────────────────────────────────────────────

UPDATE nc_biz_change_records
   SET new_application_id = NULL
 WHERE new_application_id = application_id;

COMMIT;

-- ────────────────────────────────────────────────────────────────────────────
-- 校验
-- ────────────────────────────────────────────────────────────────────────────
SELECT id, application_no, source_type, status, current_step,
       created_at, first_approved_at, data_completed_at, updated_at
FROM nc_biz_applications
WHERE deleted_at IS NULL
ORDER BY id;

SELECT id, application_id, new_application_id, change_type, changed_at
FROM nc_biz_change_records
ORDER BY id;

SELECT count(*) AS self_ref_remaining
FROM nc_biz_change_records
WHERE new_application_id = application_id;
