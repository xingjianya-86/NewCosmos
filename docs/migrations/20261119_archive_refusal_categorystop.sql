-- 归档终结拒保存量补录 CategoryStop（2026-11-19）
-- 背景：CompleteArchiveAsync 对判不合格档案直接置 Stopped 且不写变更记录；丁玉花 app113
--       （户主死亡停旧建新 112→113、前分类=农村低保）被《档案_变更告知书》误判为"不予认定"。
--       代码侧已双修：装配端按链上前分类判方向（ArchiveProductionViewModel + Helpers\NoticeStopDirection）、
--       写入侧归档时补写 CategoryStop（ApplicationStatusService.WriteArchiveRefusalCategoryStopAsync）。
--       本脚本补录修复前已停保的存量档，使存量与新写入记录口径一致（动态管理留痕、告知书走主分支）。
-- 对象形状：Stopped + 自身为停保码 + stop_reason LIKE '复核判定不合格%'（归档终结专属标记）
--          + 链上（application_id OR new_application_id）无既有 CategoryStop
--          + 链上存在**非停保码**的在保前分类（最近一条 old_classification）。
--       当前库命中：档案113（83/99/105 已有 CategoryStop、110 无链证据，均被排除）。
-- 口径与代码写入侧一致：old=链上前分类、new=档案自身停保码、change_reason_type='归档终结拒保'、
--       triggered_stop=TRUE、change_date=stop_date、金额两列 NULL（防误入追缴链
--       RecoveryHistoryProvider——其按 new_guarantee_amount IS NOT NULL 取数）。
-- 幂等：NOT EXISTS CategoryStop 守卫，重复执行不重复补录。
-- 执行：psql -f docs\migrations\20261119_archive_refusal_categorystop.sql
--       登记：powershell -File deploy\check_migrations.ps1 -Mark 20261119_archive_refusal_categorystop.sql

INSERT INTO nc_biz_change_records
    (application_id, change_no, change_type, change_reason, change_reason_type, change_date,
     old_classification, new_classification, triggered_stop, operator_name,
     new_application_id, changed_at)
SELECT a.id,
       'CHG' || TO_CHAR(NOW(), 'YYYYMMDDHH24MISSMS'),
       'CategoryStop',
       COALESCE(a.stop_reason, '复核判定不合格'),
       '归档终结拒保',
       COALESCE(a.stop_date, CURRENT_DATE),
       pre.old_classification,
       a.classification_result,
       TRUE,
       COALESCE(NULLIF(a.updated_by, ''), 'System'),
       NULL,
       NOW()
FROM nc_biz_applications a
CROSS JOIN LATERAL (
    SELECT c.old_classification
    FROM nc_biz_change_records c
    WHERE (c.application_id = a.id OR c.new_application_id = a.id)
      AND c.deleted_at IS NULL
      AND c.old_classification IS NOT NULL AND c.old_classification <> ''
    ORDER BY c.changed_at DESC, c.id DESC
    LIMIT 1
) pre
WHERE a.deleted_at IS NULL
  AND a.status = 'Stopped'
  AND a.stop_reason LIKE '复核判定不合格%'
  AND a.classification_result IN
      ('RuralIncomeExceeded','UrbanIncomeExceeded','IneligibleWithLabor','Ineligible','IneligibleOther','IncomeExceeded')
  AND pre.old_classification NOT IN
      ('RuralIncomeExceeded','UrbanIncomeExceeded','IneligibleWithLabor','Ineligible','IneligibleOther','IncomeExceeded')
  AND NOT EXISTS (
      SELECT 1 FROM nc_biz_change_records c2
      WHERE (c2.application_id = a.id OR c2.new_application_id = a.id)
        AND c2.change_type = 'CategoryStop'
        AND c2.deleted_at IS NULL);
