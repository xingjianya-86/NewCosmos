-- 户内单人保转出减员记录补录（2026-10-23）
-- 背景：代码在单人保档案审批通过时写 SingleRescueOut（旧户档案不停、成员行不删），
--       本脚本为本次改造前已审批的存量单人保补一条，供「最低生活保障边缘家庭停止、减员汇总表」体现减员。
-- 幂等：同一旧档 + 新档只写一次。
-- 对象：档案51 隋淑凤（单人保，2026-09-21 审批）← 旧档48 赵锦德（低收入）。

WITH ins AS (
    INSERT INTO nc_biz_change_records
        (application_id, change_no, change_type, change_reason, change_reason_type, change_date,
         original_application_id, new_application_id, old_classification, new_classification,
         triggered_grace_period, triggered_stop, operator_name, changed_at)
    SELECT s.id,
           'SRO' || TO_CHAR(NOW(), 'YYYYMMDDHH24MISSMS'),
           'SingleRescueOut',
           '单人保转出: ' || c.applicant_name || '(' || c.applicant_id_card || ') → 新档' || c.id,
           '转出单人保',
           COALESCE(c.first_approved_at, c.created_at)::date,
           s.id,
           c.id,
           s.classification_result,
           c.classification_result,
           FALSE,
           FALSE,
           'System',
           NOW()
    FROM nc_biz_applications c
    JOIN nc_biz_applications s ON s.id = c.original_application_id AND s.deleted_at IS NULL
    WHERE c.chain_type = 'SingleRescue'
      AND c.applicant_id_card = '231025194604064929'
      AND s.applicant_id_card = '23102519460110493X'
      AND c.deleted_at IS NULL
      AND NOT EXISTS (
          SELECT 1 FROM nc_biz_change_records r
          WHERE r.application_id = s.id
            AND r.change_type = 'SingleRescueOut'
            AND r.new_application_id = c.id
            AND r.deleted_at IS NULL)
    RETURNING id, application_id, new_application_id
)
INSERT INTO nc_biz_change_details
    (change_id, field_name, field_label, old_value, new_value, created_at)
SELECT ins.id,
       'MemberRemove',
       c.applicant_name,
       c.applicant_name || '(' || c.applicant_id_card || ') SharedLiving',
       '转出单人保',
       NOW()
FROM ins
JOIN nc_biz_applications c ON c.id = ins.new_application_id;
