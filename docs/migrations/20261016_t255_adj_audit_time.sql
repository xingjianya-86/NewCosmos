-- 20261016_t255_adj_audit_time.sql
-- 清偿方案 D3 · T255 {审批时间} 按时间轴固定（2026-10-16 业务拍板执行）。
-- 拍板原文：审批时间按照 A/B/C 类时间轴、对应各自单位（口径）进行固定。
-- 落地判定：增减员调整表属 B 线业务线 → 审批时间 = B 线 tl.AuditDate（审核确认日，
--   BusinessTimelineService 权威）；原 Sidebar 的 DateTime.Now（打印当天）废除——补打不再随打印日漂移。
-- 执行顺序说明（与"先代码后迁 DB"铁律的差异留痕）：本批 **DB 先、代码后** ——
--   目标键 AUDIT_DATE 本就由 dict A/B 供给（tl.AuditDate ISO，非净新键），不存在净新键断供窗口；
--   config 迁完立即正确（键+format 原子同改），代码侧随后改 ADJ_EFFECTIVE_DATE 口径并删死供给行。
-- 本行同步加 format：AUDIT_DATE=ISO，原 ADJ_AUDIT_TIME=中文日期 → format=yyyy年M月d日 保渲染一致（R3 同 T222）。
-- 回滚：
--   UPDATE nc_biz_templates SET config_json = b.config_json, updated_at = NOW()
--   FROM backup_20261016_t255_adjtime b WHERE nc_biz_templates.id = b.id;
--   （代码侧回滚：Sidebar adjAuditTime 恢复 DateTime.Now、恢复 ADJ_AUDIT_TIME 供给行与 dict 空初值行）
-- ============================================================================

BEGIN;

-- 0. 备份
DROP TABLE IF EXISTS backup_20261016_t255_adjtime;
CREATE TABLE backup_20261016_t255_adjtime AS
SELECT id, name, file_type, config_json, updated_at
FROM nc_biz_templates
WHERE id = 255;

-- 1. 迁移：ADJ_AUDIT_TIME → AUDIT_DATE + format（原子）
UPDATE nc_biz_templates
SET config_json = jsonb_set(config_json, '{fields}', (
        SELECT jsonb_agg(
                   CASE WHEN e.value->>'fieldKey' = 'ADJ_AUDIT_TIME'
                        THEN jsonb_set(jsonb_set(e.value, '{fieldKey}', '"AUDIT_DATE"'), '{format}', '"yyyy年M月d日"')
                        ELSE e.value
                   END
                   ORDER BY e.ord)
        FROM jsonb_array_elements(config_json -> 'fields') WITH ORDINALITY AS e(value, ord)
    ), true),
    updated_at = NOW()
WHERE id = 255;

-- 2. 验收
-- 2a. ADJ_AUDIT_TIME 全库剩余（期望 0）
SELECT count(*) AS adj_audit_time_left
FROM nc_biz_templates, jsonb_array_elements(config_json -> 'fields') f
WHERE f->>'fieldKey' = 'ADJ_AUDIT_TIME';

-- 2b. T255 AUDIT_DATE 行的 format 落位（期望 yyyy年M月d日）
SELECT f->>'fieldKey' AS k, f->>'format' AS fmt
FROM nc_biz_templates, jsonb_array_elements(config_json -> 'fields') f
WHERE id = 255 AND f->>'fieldKey' = 'AUDIT_DATE';

-- 2c. 非 ASCII（期望 0）与总行数（期望 3314）
SELECT count(*) FILTER (WHERE f->>'fieldKey' !~ '^[A-Z0-9_]+$') AS non_ascii, count(*) AS total_rows
FROM nc_biz_templates, jsonb_array_elements(config_json -> 'fields') f;

COMMIT;
