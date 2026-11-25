-- 20261119_template159_remove_shared_member_indexshifts.sql
-- 档案_入户调查表（T159）共同生活成员整栏空白（单人户配偶信息全丢）修复。
-- 背景（2026-11-19 用户反馈）：郁有发（application_id=108，家庭人口2人）入户调查表
--   「共同生活家庭成员信息」全部打印为 -/0，配偶于治秀（SharedLiving，库内数据完好）不见；
--   右侧赡养义务人三行正常。打印留痕 id=640 的 xlsx 快照证实共同生活成员单元格在渲染前即被清空。
-- 根因：
--   ① 提交 addc6e3（2026-10-02「共同生活成员去户主三表统一」）把 SHARED_MEMBER_* 数据布局
--      从「_1=户主、成员从 _2 起」改为「成员从 _1 起、不含户主」
--      （ArchiveProductionViewModel.cs 共同生活成员循环 sharedIdx = i + 1），
--      同批改了模板254 的 config，但**数据库里 T159 的 config_json 漏改**；
--   ② T159 indexShifts 仍留有 5 条 SHARED_MEMBER_* skip=1 规则，其语义是
--      「删除 _1 槽（户主）、其余前移」（TemplateFieldResolver.Project）：
--      - 单成员户（maxIndex=1 不大于 skip=1）→ else 分支直接 Remove(SHARED_MEMBER_*_1)
--        → 唯一成员（配偶）被当户主槽删掉；
--      - 多成员户 → _1 被 _2 的空槽覆盖 → 第一名成员必丢。
--      空值经 FormatFieldValue：文本显 -、数值显 0，与截图完全一致。
--   同型模板 216（特困入户调查）/254（定期复核审批表）均无 indexShifts，打印正常。
-- 修法：从**所有模板** config_json.indexShifts 数组中剔除 baseKey 为
--   SHARED_MEMBER_NAME/GENDER/AGE/RELATION/EMPLOYMENT 的规则（防御式全库清理，
--   实际命中仅 T159）；保留 SHARED_NAME_* / FAMILY_MEMBER_* 等与「_1=户主」布局
--   仍然匹配的规则（T188/T218 不受影响）。
-- 幂等：剔除后为空数组，重复执行为 no-op；规则不存在时 EXISTS 守卫跳过。
-- 回滚：
--   UPDATE nc_biz_templates
--   SET config_json = jsonb_set(config_json, '{indexShifts}',
--         '[{"skip":1,"baseKey":"SHARED_MEMBER_NAME"},{"skip":1,"baseKey":"SHARED_MEMBER_GENDER"},{"skip":1,"baseKey":"SHARED_MEMBER_AGE"},{"skip":1,"baseKey":"SHARED_MEMBER_RELATION"},{"skip":1,"baseKey":"SHARED_MEMBER_EMPLOYMENT"}]'::jsonb, true),
--       updated_at = NOW()
--   WHERE id = 159;
-- 备份：backup/20261119_132452_template159_indexshifts_fix/templates_config_before.sql
-- ============================================================================

BEGIN;

-- 1. 剔除 SHARED_MEMBER_* 索引投影规则（保留其它 baseKey 规则；无规则/键不存在则不动）
UPDATE nc_biz_templates t
SET config_json = jsonb_set(
        t.config_json,
        '{indexShifts}',
        COALESCE(
            (SELECT jsonb_agg(r ORDER BY ord)
             FROM jsonb_array_elements(t.config_json -> 'indexShifts')
                      WITH ORDINALITY AS e(r, ord)
             WHERE NOT (e.r ->> 'baseKey' IN
                        ('SHARED_MEMBER_NAME', 'SHARED_MEMBER_GENDER', 'SHARED_MEMBER_AGE',
                         'SHARED_MEMBER_RELATION', 'SHARED_MEMBER_EMPLOYMENT'))),
            '[]'::jsonb),
        true),
    updated_at = NOW()
WHERE t.config_json ? 'indexShifts'
  AND EXISTS (SELECT 1
              FROM jsonb_array_elements(t.config_json -> 'indexShifts') x
              WHERE x ->> 'baseKey' IN
                    ('SHARED_MEMBER_NAME', 'SHARED_MEMBER_GENDER', 'SHARED_MEMBER_AGE',
                     'SHARED_MEMBER_RELATION', 'SHARED_MEMBER_EMPLOYMENT'));

-- 2. 验收1：应返回 0 行（全库不再有 SHARED_MEMBER_* 投影规则）
SELECT t.id, t.name, x ->> 'baseKey' AS stale_base_key
FROM nc_biz_templates t,
     jsonb_array_elements(t.config_json -> 'indexShifts') x
WHERE x ->> 'baseKey' IN ('SHARED_MEMBER_NAME', 'SHARED_MEMBER_GENDER', 'SHARED_MEMBER_AGE',
                          'SHARED_MEMBER_RELATION', 'SHARED_MEMBER_EMPLOYMENT');

-- 3. 验收2：T159 indexShifts 应为空数组 []（其原有规则全部为 SHARED_MEMBER_*）
SELECT id, name, config_json -> 'indexShifts' AS index_shifts
FROM nc_biz_templates
WHERE id = 159;

-- 4. 验收3：T188/T218 的 SHARED_NAME_*/FAMILY_MEMBER_* 规则应原样保留（防误伤）
SELECT id, name, config_json -> 'indexShifts' AS index_shifts
FROM nc_biz_templates
WHERE id IN (188, 218)
ORDER BY id;

COMMIT;
