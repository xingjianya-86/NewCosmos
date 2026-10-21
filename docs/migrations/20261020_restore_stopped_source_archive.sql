-- 20261020：停旧建新旧档「变更前快照」还原（一次性数据修复，幂等）
-- ---------------------------------------------------------------------------
-- 背景：经济复核/成员变更的 Step5「分类判定」与经济明细保存会把变更后的新值写回当前（旧）档案，
--       随后旧档被置 Stopped。旧档因此不再是"变更前"记录：
--       ① 分类=停保码、金额=0 → 月报停保汇总表按分类过滤整户漏出
--          （2026-10 实测漏 3 户：贺满程 82、梁玉祥 93、张彦亮 98，本应 5 户只出 2 户）；
--       ② 人口/金额=变更后值 → 与"历史快照"语义不符
--          （李增会 89 存 2 人/79+104，实际停旧建新前是 3 人/1106+104=1210）。
-- 修复：代码侧已由 StopAndRebuildAsync 第 9.5 步（RestoreSourceArchiveAsync）在停旧建新后
--       自动把旧档还原为变更前快照；本脚本只补修**存量**旧档。
--
-- 取数口径（与月报同一套「同链事件」语义）：
--   分类     = 最新 CategoryStop.old_classification，缺失回退链上最新 Before 快照 Classification
--   合计     = 快照 GuaranteeAmount，缺失回退 CategoryStop.old_guarantee_amount
--   分类施保 = 快照 OldClassifiedSubsidyAmount（快照无该字段 → 保持现值，如贺满程 82 无此字段且为 0）
--   照料费   = 快照 OldCaregiverSubsidyAmount（缺失保持现值）
--   户月     = 快照 OldHouseholdMonthlyGuaranteeAmount；快照无该字段 → CategoryStop.old_guarantee_amount − 分类施保
--   家庭人口 = 快照 FamilySize
--   合格标记 = 还原后分类属停保族 → false，否则 true（Approved 档案停保前恒为 true）
--
-- 覆盖范围（两组，命中后按差异更新，重复执行为 no-op）：
--   A 组：status='Stopped' 且分类是停保码 且存在 CategoryStop(old_* 非空) —— 全库仅 82/93/98
--   B 组：2026-10 周期内停保（stop_date ∈ [2026-10-01, 2026-11-01)）且有下链新档且有 Before 快照
--         —— 仅 82/89/93/98；**不含** 8/9 月的 38、64、68（已结月数据不动）
-- 执行：psql -w -v ON_ERROR_STOP=1 -f docs\migrations\20261020_restore_stopped_source_archive.sql

BEGIN;

\pset pager off
\pset border 2
\pset columns 240

-- ── ① 执行前核对：A 组（应为 82 贺满程 / 93 梁玉祥 / 98 张彦亮 三行停保码）──────────
SELECT 'A_BEFORE' AS phase, a.id, a.applicant_name, a.stop_date, a.status,
       a.classification_result, a.is_eligible, a.family_size,
       a.household_monthly_guarantee_amount AS hh, a.classified_subsidy_amount AS cls,
       a.total_guarantee_amount AS total
FROM nc_biz_applications a
WHERE a.status = 'Stopped' AND a.deleted_at IS NULL
  AND a.classification_result = ANY(ARRAY[
      'RuralIncomeExceeded','UrbanIncomeExceeded',
      'IneligibleWithLabor','Ineligible','IneligibleOther'])
ORDER BY a.id;

-- ── ② 执行前核对：B 组（本月停旧建新旧档 4 行）────────────────────────────────────
SELECT 'B_BEFORE' AS phase, a.id, a.applicant_name, a.stop_date,
       a.classification_result, a.is_eligible, a.family_size, snap.family_size AS snap_fam,
       a.household_monthly_guarantee_amount AS hh, a.classified_subsidy_amount AS cls,
       a.total_guarantee_amount AS total, snap.total_guarantee AS snap_total
FROM nc_biz_applications a
LEFT JOIN LATERAL (
    SELECT CASE WHEN jsonb_typeof(s.snapshot_data -> 'FamilySize') = 'number'
                THEN (s.snapshot_data ->> 'FamilySize')::int END AS family_size,
           CASE WHEN jsonb_typeof(s.snapshot_data -> 'GuaranteeAmount') = 'number'
                THEN (s.snapshot_data ->> 'GuaranteeAmount')::numeric END AS total_guarantee
    FROM nc_biz_change_records x
    JOIN nc_biz_change_snapshots s ON s.change_id = x.id AND s.snapshot_type = 'Before'
    WHERE x.application_id = a.id AND x.deleted_at IS NULL
    ORDER BY x.changed_at DESC, x.id DESC
    LIMIT 1
) snap ON TRUE
WHERE a.status = 'Stopped' AND a.deleted_at IS NULL
  AND a.stop_date >= DATE '2026-10-01' AND a.stop_date < DATE '2026-11-01'
  AND EXISTS (SELECT 1 FROM nc_biz_applications n
              WHERE n.original_application_id = a.id AND n.deleted_at IS NULL)
  AND snap.family_size IS NOT NULL
ORDER BY a.id;

-- ── ③ 回填（差异更新，幂等）────────────────────────────────────────────────────────
WITH fix AS (
    SELECT a.id,
           COALESCE(cs.old_classification, snap.classification, a.classification_result)
               AS classification_result,
           CASE WHEN COALESCE(cs.old_classification, snap.classification, a.classification_result)
                     = ANY(ARRAY['RuralIncomeExceeded','UrbanIncomeExceeded',
                                 'IneligibleWithLabor','Ineligible','IneligibleOther'])
                THEN FALSE ELSE TRUE END
               AS is_eligible,
           COALESCE(snap.family_size, a.family_size) AS family_size,
           COALESCE(snap.old_hh,
                    CASE WHEN cs.old_guarantee_amount IS NOT NULL
                         THEN cs.old_guarantee_amount - COALESCE(snap.old_cls, 0) END,
                    a.household_monthly_guarantee_amount) AS hh,
           COALESCE(snap.old_cls, a.classified_subsidy_amount) AS cls,
           COALESCE(snap.old_cg, a.caregiver_subsidy_amount) AS cg,
           COALESCE(snap.total_guarantee, cs.old_guarantee_amount, a.total_guarantee_amount) AS total
      FROM nc_biz_applications a
      LEFT JOIN LATERAL (
          SELECT cr.old_classification, cr.old_guarantee_amount
            FROM nc_biz_change_records cr
           WHERE cr.application_id = a.id AND cr.deleted_at IS NULL
             AND cr.change_type = 'CategoryStop'
             AND cr.old_classification IS NOT NULL AND cr.old_classification <> ''
             AND cr.old_guarantee_amount IS NOT NULL
           ORDER BY cr.changed_at DESC, cr.id DESC
           LIMIT 1
      ) cs ON TRUE
      LEFT JOIN LATERAL (
          SELECT s.snapshot_data ->> 'Classification' AS classification,
                 CASE WHEN jsonb_typeof(s.snapshot_data -> 'FamilySize') = 'number'
                      THEN (s.snapshot_data ->> 'FamilySize')::int END AS family_size,
                 CASE WHEN jsonb_typeof(s.snapshot_data -> 'OldHouseholdMonthlyGuaranteeAmount') = 'number'
                      THEN (s.snapshot_data ->> 'OldHouseholdMonthlyGuaranteeAmount')::numeric END AS old_hh,
                 CASE WHEN jsonb_typeof(s.snapshot_data -> 'OldClassifiedSubsidyAmount') = 'number'
                      THEN (s.snapshot_data ->> 'OldClassifiedSubsidyAmount')::numeric END AS old_cls,
                 CASE WHEN jsonb_typeof(s.snapshot_data -> 'OldCaregiverSubsidyAmount') = 'number'
                      THEN (s.snapshot_data ->> 'OldCaregiverSubsidyAmount')::numeric END AS old_cg,
                 CASE WHEN jsonb_typeof(s.snapshot_data -> 'GuaranteeAmount') = 'number'
                      THEN (s.snapshot_data ->> 'GuaranteeAmount')::numeric END AS total_guarantee
            FROM nc_biz_change_records x
            JOIN nc_biz_change_snapshots s ON s.change_id = x.id AND s.snapshot_type = 'Before'
           WHERE x.application_id = a.id AND x.deleted_at IS NULL
           ORDER BY x.changed_at DESC, x.id DESC
           LIMIT 1
      ) snap ON TRUE
     WHERE a.status = 'Stopped' AND a.deleted_at IS NULL
       AND (
           -- A 组：停保码旧档（分类被 Step5 写成停保码/0 元）
           (a.classification_result = ANY(ARRAY['RuralIncomeExceeded','UrbanIncomeExceeded',
                                                'IneligibleWithLabor','Ineligible','IneligibleOther'])
            AND cs.old_classification IS NOT NULL)
           OR
           -- B 组：本月停旧建新旧档，还原变更前的人口/金额快照
           (a.stop_date >= DATE '2026-10-01' AND a.stop_date < DATE '2026-11-01'
            AND snap.family_size IS NOT NULL
            AND EXISTS (SELECT 1 FROM nc_biz_applications n
                        WHERE n.original_application_id = a.id AND n.deleted_at IS NULL))
       )
)
UPDATE nc_biz_applications a
   SET classification_result        = f.classification_result,
       is_eligible                  = f.is_eligible,
       family_size                  = f.family_size,
       household_monthly_guarantee_amount = f.hh,
       classified_subsidy_amount    = f.cls,
       caregiver_subsidy_amount     = f.cg,
       total_guarantee_amount       = f.total,
       updated_at                   = NOW(),
       updated_by                   = 'data-repair-20261020'
  FROM fix f
 WHERE a.id = f.id
   AND (a.classification_result      IS DISTINCT FROM f.classification_result
        OR a.is_eligible             IS DISTINCT FROM f.is_eligible
        OR a.family_size             IS DISTINCT FROM f.family_size
        OR a.household_monthly_guarantee_amount IS DISTINCT FROM f.hh
        OR a.classified_subsidy_amount   IS DISTINCT FROM f.cls
        OR a.caregiver_subsidy_amount    IS DISTINCT FROM f.cg
        OR a.total_guarantee_amount      IS DISTINCT FROM f.total);

-- ── ④ 执行后核对：A 组应为空（分类已非停保码），B 组四行 = 变更前值 ─────────────────
SELECT 'A_AFTER' AS phase, COUNT(*) AS stop_code_rows
  FROM nc_biz_applications a
 WHERE a.status = 'Stopped' AND a.deleted_at IS NULL
   AND a.classification_result = ANY(ARRAY['RuralIncomeExceeded','UrbanIncomeExceeded',
                                           'IneligibleWithLabor','Ineligible','IneligibleOther']);

SELECT 'B_AFTER' AS phase, a.id, a.applicant_name, a.stop_date,
       a.classification_result, a.is_eligible, a.family_size,
       a.household_monthly_guarantee_amount AS hh, a.classified_subsidy_amount AS cls,
       a.caregiver_subsidy_amount AS cg, a.total_guarantee_amount AS total
  FROM nc_biz_applications a
 WHERE a.id IN (82, 89, 93, 98)
 ORDER BY a.id;

COMMIT;
