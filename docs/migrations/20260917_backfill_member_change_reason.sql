-- ============================================================================
-- 2026-09-17 家庭成员变更原因补录：赵某某户（档案 64 → 65）减员 韩某某
--
-- 背景：减员发生在"增/减员原因登记"功能上线前，变更记录缺原因明细；
-- 按用户确认补录：原因 = 人员死亡，死亡/事由日期 = 2026-09-17，无备注。
--
-- 幂等：重复执行不产生重复明细/死亡记录，不重复替换原因文本。
-- 执行：psql -v ON_ERROR_STOP=1 -f <本文件>
-- ============================================================================

BEGIN;

-- ① 结构化明细（nc_biz_change_details）
INSERT INTO nc_biz_change_details (change_id, field_name, field_label, old_value, new_value)
SELECT 30, 'MemberRemove', '韩某某',
       '韩某某(231025********4644) SharedLiving Spouse',
       '人员死亡|2026-09-17|'
WHERE NOT EXISTS (
    SELECT 1 FROM nc_biz_change_details
    WHERE change_id = 30 AND field_name = 'MemberRemove' AND field_label = '韩某某');

-- ② After 快照追加 RemovedMembers 明细（读取方只取顶层字段，追加键不影响既有口径）
UPDATE nc_biz_change_snapshots
SET snapshot_data = snapshot_data || jsonb_build_object('RemovedMembers', jsonb_build_array(
        jsonb_build_object(
            'Name', '韩某某', 'IdCard', '231025********4644',
            'RelationshipToHead', 'Spouse', 'MemberCategory', 'SharedLiving',
            'ReasonCode', 'Death', 'ReasonName', '人员死亡',
            'EventDate', '2026-09-17', 'Remark', '')))
WHERE change_id = 30 AND snapshot_type = 'After' AND NOT (snapshot_data ? 'RemovedMembers');

-- ③ 变更记录可读性：原因名称写入 change_reason + 补录留痕（change_remark）
UPDATE nc_biz_change_records
SET change_reason = replace(change_reason, '（减员 韩某某）', '（减员 韩某某（人员死亡））'),
    change_remark = CASE WHEN COALESCE(change_remark, '') = ''
                         THEN '2026-09-17 补录减员原因：人员死亡（死亡日期 2026-09-17）'
                         ELSE change_remark || ' | 2026-09-17 补录减员原因：人员死亡（死亡日期 2026-09-17）' END
WHERE id = 30 AND change_reason LIKE '%（减员 韩某某）%';

-- ④ 死亡记录（member_id=680 为减员前成员行；与户主死亡同表，供自然减员/公示退出识别）
INSERT INTO nc_biz_death_records
    (application_id, member_id, member_name, member_id_card, relationship_to_head,
     death_date, death_reason, is_household_head, remark, operator_name, created_at)
SELECT 64, 680, '韩某某', '231025********4644', 'Spouse',
       '2026-09-17', '成员变更-人员死亡', false, '', '***', NOW()
WHERE NOT EXISTS (
    SELECT 1 FROM nc_biz_death_records
    WHERE application_id = 64 AND member_id_card = '231025********4644');

-- ⑤ 验证输出
SELECT 'change_details' AS item, count(*) AS cnt
FROM nc_biz_change_details WHERE change_id = 30
UNION ALL
SELECT 'after_snapshot_has_removed', count(*)
FROM nc_biz_change_snapshots
WHERE change_id = 30 AND snapshot_type = 'After' AND (snapshot_data ? 'RemovedMembers')
UNION ALL
SELECT 'death_records', count(*)
FROM nc_biz_death_records
WHERE application_id = 64 AND member_id_card = '231025********4644';

COMMIT;
