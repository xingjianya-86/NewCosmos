-- ============================================================================
-- 变更记录停保口径回填（CategoryStop）
-- 日期：2026-09-17
--
-- 背景：
--   1) 修复前"经济复核同大类转停保"未补写 CategoryStop 伴随记录（漏记录，告知书打不出）；
--   2) 旧版代码把跨大类转入（如低保→特困）写的 CategoryStop 记录误置 triggered_stop=true，
--      导致打印新旧档案都会误附《档案_变更告知书》（"停止享受"）。
--
-- 目标不变量：
--   告知书仅由真实停保触发 = change_type='CategoryStop' AND triggered_stop=true
--                            AND new_classification ∈ 停保类（收入超标/不符合）。
--
-- 幂等：可重复执行。②仅纠正"新分类已知且非停保类"的记录；信息不足（新分类空且无下游）者保留待人工。
-- 执行：psql -v ON_ERROR_STOP=1 -f <本文件>（执行前先导出 nc_biz_change_records 全表备份）
-- ============================================================================

BEGIN;

-- ① 纠偏：CategoryStop 且新分类为空 → 从下游未删新档案回填分类
UPDATE nc_biz_change_records c
SET new_classification = d.classification_result
FROM (
    SELECT DISTINCT ON (n.original_application_id)
           n.original_application_id,
           n.classification_result
    FROM nc_biz_applications n
    WHERE n.deleted_at IS NULL
      AND n.original_application_id IS NOT NULL
      AND n.classification_result IS NOT NULL
      AND n.classification_result <> ''
    ORDER BY n.original_application_id, n.id DESC
) d
WHERE c.change_type = 'CategoryStop'
  AND c.deleted_at IS NULL
  AND (c.new_classification IS NULL OR c.new_classification = '')
  AND d.original_application_id = c.application_id;

-- ② 纠偏：CategoryStop + triggered_stop=true + 新分类非停保类 → false（跨类转入不属于停保）
UPDATE nc_biz_change_records c
SET triggered_stop = false,
    change_remark = CASE
        WHEN COALESCE(c.change_remark, '') = ''
            THEN '2026-09-17 停保口径回填：非停保类结果纠正 triggered_stop'
        ELSE c.change_remark || ' | 2026-09-17 停保口径回填：非停保类结果纠正 triggered_stop'
    END
WHERE c.change_type = 'CategoryStop'
  AND c.deleted_at IS NULL
  AND c.triggered_stop = true
  AND c.new_classification IS NOT NULL
  AND c.new_classification <> ''
  AND c.new_classification NOT IN
      ('RuralIncomeExceeded','UrbanIncomeExceeded','IneligibleWithLabor','Ineligible','IneligibleOther');

-- ③ 补漏：真实停保（源记录新分类为停保类）缺 CategoryStop 伴随记录 → 补写
INSERT INTO nc_biz_change_records
    (application_id, change_no, change_type, change_category, change_reason, change_reason_type, change_date,
     old_classification, new_classification, old_per_capita_income, new_per_capita_income,
     old_guarantee_amount, new_guarantee_amount, triggered_grace_period, triggered_stop,
     operator_name, new_application_id, change_remark, changed_at)
SELECT src.application_id,
       'CHGBF' || to_char(now(), 'YYYYMMDD') || lpad(src.id::text, 6, '0'),
       'CategoryStop',
       NULL,
       COALESCE(src.change_reason, '经济复核收入超标停保'),
       COALESCE(src.change_reason_type, '经济复核'),
       COALESCE(src.change_date, src.changed_at::date),
       src.old_classification,
       src.new_classification,
       src.old_per_capita_income,
       src.new_per_capita_income,
       src.old_guarantee_amount,
       src.new_guarantee_amount,
       COALESCE(src.triggered_grace_period, false),
       true,
       src.operator_name,
       COALESCE(src.new_application_id,
                (SELECT n.id FROM nc_biz_applications n
                 WHERE n.original_application_id = src.application_id AND n.deleted_at IS NULL
                 ORDER BY n.id DESC LIMIT 1)),
       '2026-09-17 数据回填：经济复核停保补写 CategoryStop（源记录 id=' || src.id || '）',
       src.changed_at
FROM nc_biz_change_records src
WHERE src.deleted_at IS NULL
  AND src.change_type IN ('FundChange','MemberAdd','MemberRemove','MemberModify')
  AND src.new_classification IN
      ('RuralIncomeExceeded','UrbanIncomeExceeded','IneligibleWithLabor','Ineligible','IneligibleOther')
  AND NOT EXISTS (
      SELECT 1 FROM nc_biz_change_records x
      WHERE x.deleted_at IS NULL
        AND x.change_type = 'CategoryStop'
        AND (x.application_id = src.application_id
             OR (src.new_application_id IS NOT NULL AND x.new_application_id = src.new_application_id))
  );

-- ④ 验证：V1/V2 均应为 0
SELECT 'V1 non-stop-but-triggered' AS check_name, count(*) AS remaining
FROM nc_biz_change_records
WHERE change_type = 'CategoryStop'
  AND triggered_stop = true
  AND deleted_at IS NULL
  AND (new_classification IS NULL OR new_classification NOT IN
      ('RuralIncomeExceeded','UrbanIncomeExceeded','IneligibleWithLabor','Ineligible','IneligibleOther'))
UNION ALL
SELECT 'V2 missing-companion', count(*)
FROM nc_biz_change_records src
WHERE src.deleted_at IS NULL
  AND src.change_type IN ('FundChange','MemberAdd','MemberRemove','MemberModify')
  AND src.new_classification IN
      ('RuralIncomeExceeded','UrbanIncomeExceeded','IneligibleWithLabor','Ineligible','IneligibleOther')
  AND NOT EXISTS (
      SELECT 1 FROM nc_biz_change_records x
      WHERE x.deleted_at IS NULL
        AND x.change_type = 'CategoryStop'
        AND (x.application_id = src.application_id
             OR (src.new_application_id IS NOT NULL AND x.new_application_id = src.new_application_id))
  );

COMMIT;
