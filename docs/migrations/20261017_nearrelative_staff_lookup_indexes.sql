-- 20261017_nearrelative_staff_lookup_indexes.sql
-- 近亲属备案：经办人员身份证多来源检索（系统用户 + 档案照料护理人）所需索引。
-- 背景：录入页工作人员身份证满 18 位自动检索 nc_sys_users → nc_biz_caregivers → nc_biz_destitute_persons.caregiver_id_card，
--       三处检索列原先均无索引。
-- Schema 权威定义同步：
--   Resources\Schema\social_assistance\caregivers.yaml     → idx_caregivers_id_card
--   Resources\Schema\destitute\database.yaml               → idx_destitute_persons_caregiver_id_card
--   Resources\Schema\new_permission_system\database.yaml   → idx_nc_sys_users_identity_card
-- 幂等：IF NOT EXISTS 可重复执行；应用侧「数据库管理 → 初始化数据库结构」时 SchemaService 也会补建。

CREATE INDEX IF NOT EXISTS idx_caregivers_id_card
    ON nc_biz_caregivers (id_card);

CREATE INDEX IF NOT EXISTS idx_destitute_persons_caregiver_id_card
    ON nc_biz_destitute_persons (caregiver_id_card);

CREATE INDEX IF NOT EXISTS idx_nc_sys_users_identity_card
    ON nc_sys_users (identity_card);
