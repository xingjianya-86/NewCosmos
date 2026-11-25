-- 2026-11-19 app 38（佟淑华）户主死亡历史补录订正
--
-- 现状（核查于 2026-11-19）：
--   * nc_biz_death_records.id=2：吴克学（原户主）死亡，is_household_head=true，
--     new_head=佟淑华，但 new_application_id=38（指向自己）——真正后继档案是 52
--     （app 52 chain_type=HouseholdDeath、original_application_id=38，change_records.id=12 也指向 52）。
--   * nc_biz_change_records.id=12：户主死亡变更（新档 52），triggered_stop 为 NULL，
--     标准停旧建新路径应写 TRUE，导致"退出对象/停保"类统计口径不一致。
-- 说明：幂等；只改这两条记录的关联字段，不动人员/金额。
-- 执行顺序：在 member_duplicate_cleanup / member_head_flag_normalize 之后执行。

BEGIN;

UPDATE nc_biz_death_records
   SET new_application_id = 52
 WHERE id = 2
   AND application_id = 38
   AND member_id_card = '23102519531006491X'
   AND is_household_head = TRUE
   AND new_application_id = 38;

UPDATE nc_biz_change_records
   SET triggered_stop = TRUE
 WHERE id = 12
   AND application_id = 38
   AND change_type = 'HouseholdDeath'
   AND COALESCE(triggered_stop, FALSE) = FALSE;

COMMIT;

-- 核对：app 38 的户主死亡记录应指向新档 52、变更记录 triggered_stop=true
\pset pager off
SELECT d.id, d.application_id, d.member_name, d.new_head_name, d.new_application_id,
       c.id AS change_id, c.triggered_stop
  FROM nc_biz_death_records d
  LEFT JOIN nc_biz_change_records c
    ON c.application_id = d.application_id
   AND c.change_type = 'HouseholdDeath'
 WHERE d.id = 2;
