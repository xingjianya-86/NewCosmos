-- 20261016_batch5_solo_keys.sql
-- 批5：SOLO B 类「先双写后迁移」—— 31 规则 / 49 行（B 档 50 行中 1 行延后，见下）。
-- 前置（铁律4：先代码后迁 DB）：双写已于同日完成并 dotnet build 0 警告 0 错误：
--   Constants\FieldKeys.Common.cs           +4 net-new 常量（RESIDENCE_ADDRESS / TOWN_OPINION /
--                                            REPORT_UNIT / APPLICANT_AGE——计划点名 2 个，实建 4 个：
--                                            REPORT_UNIT、APPLICANT_AGE 此前仅 config 孤儿读者、代码零常量，留痕）
--   DynamicManagementRecordService.cs       +RESIDENCE_ADDRESS / +TOWN_OPINION（DM_ 旁，同源同值）
--   TempReliefPrintDataBuilder.cs           +REPORT_UNIT / +APPLICANT_GENDER / +APPLICANT_AGE /
--                                            +CONTACT_PHONE / +COMMUNITY / +TOWN（同 TEMP_ 旁同源同值；
--                                            CONTACT_PHONE=app.Phone 申请人电话，非公示异议反馈电话）
--   ElderlyPrintDataBuilder.cs              +APPLICANT_NAME / +APPLICANT_AGE / +CONTACT_PHONE /
--                                            +APPLICANT_ID_CARD / +FAMILY_ADDRESS（同 ELDERLY_ 同源同值）
--   ElderlyReviewViewModel.cs               +FAMILY_ADDRESS=adjustFamilyAddress（T250 拼接口径后写覆盖，
--                                            必须晚于 BuildSingleFields 的 plain 值）
--   NearRelativePrintDataBuilder.cs         dict1 +AUDIT_DATE（T153 月报唯一供给）、dict2 +REPORT_UNIT
--                                            （T154 月报唯一供给）、dict3 +6、dict4 +7（同 pair 同源同值）
-- 档案域 5 行零代码改动（dict A L678-993 / dict B L1777-1935 已备目标键）：
--   T221 APPLICANT_PHONE→CONTACT_PHONE：dict A/B CONTACT_PHONE(L688/1792)=app.ApplicantPhone 与旧键
--        供给(L1455/1973)同源同值。
--   T241 NOTICE_TOWN→TOWN：dict A/B TOWN(L704/1803)=orgInfo.Town；旧键=app.Town——异源但同辖区
--        高概率同镇（工单 confidence=M 已留痕），不双写以免污染 TOWN 其余 4 行读者。
--   T138 SPECIAL_APPROVAL_AUDIT_DATE→AUDIT_DATE ×2：dict AUDIT_DATE=tl.AuditDate(B线审核确认日,ISO)；
--        旧键=spForm.AuditDate(一事一议表单录入,ISO)——异源同格式（工单 conf=H 判可合，语义向
--        "审核确认日"统一）；不双写以免 L1135 覆盖共享 AUDIT_DATE 污染其余 8 个读者。
--   T222 SCATTERED_AUDIT_TIME→AUDIT_DATE：同源 tl.AuditDate、异格式（中文 vs ISO）→ **本行按合并原则
--        R3 加 config format="yyyy年M月d日"**，输出 = tl.AuditDate 中文 = 旧键值，零变化。
-- ⚠️ 计划偏离（留痕）：T255 ADJ_AUDIT_TIME→AUDIT_DATE（B/MIXED）**本批不迁、延后单独处理**——
--   侦查实证 adjAuditTime=DateTime.Now（Sidebar L366，打开页当天）与 dict AUDIT_DATE=tl.AuditDate
--   异源且中文/ISO 异格式；双写会覆盖共享键污染 8 个既有读者，不双写则值+格式双变。
-- 影响面已核：27 个旧键全库恰 49 行 / 21 模板，与工单逐条吻合（零遗漏、无 C/Unknown 混入）。
-- 幂等：VALUES 三元组只匹配 (模板, 旧键)，重复执行落 ELSE no-op。
-- 回滚：
--   UPDATE nc_biz_templates SET config_json = b.config_json, updated_at = NOW()
--   FROM backup_20261016_batch5_solo b WHERE nc_biz_templates.id = b.id;
--   （代码侧回滚：删去 6 文件中标注「批5 双写/net-new」的新增行，恢复 ElderlyReview 原表达式）
-- ============================================================================

BEGIN;

-- 0. 备份受影响模板的 config_json（21 个）
DROP TABLE IF EXISTS backup_20261016_batch5_solo;
CREATE TABLE backup_20261016_batch5_solo AS
SELECT id, name, file_type, config_json, updated_at
FROM nc_biz_templates
WHERE id IN (138, 140, 141, 143, 151, 152, 153, 154, 221, 222,
             226, 229, 230, 235, 236, 237, 238, 241, 248, 249, 250);

-- 1. 迁移 49 行（按 (模板, 旧键) 三元组限定，双保险）
UPDATE nc_biz_templates t
SET config_json = jsonb_set(t.config_json, '{fields}', (
        SELECT jsonb_agg(
                   CASE WHEN m.new_key IS NOT NULL
                        THEN jsonb_set(e.value, '{fieldKey}', to_jsonb(m.new_key))
                        ELSE e.value
                   END
                   ORDER BY e.ord)
        FROM jsonb_array_elements(t.config_json -> 'fields') WITH ORDINALITY AS e(value, ord)
        LEFT JOIN (VALUES
            (248,  'DM_HEAD_RESIDENCE',            'RESIDENCE_ADDRESS'),
            (248,  'DM_TOWN_OPINION',              'TOWN_OPINION'),
            (226,  'TEMP_APPLICANT_AGE',           'APPLICANT_AGE'),
            (226,  'TEMP_APPLICANT_GENDER',        'APPLICANT_GENDER'),
            (226,  'TEMP_REPORT_UNIT',             'REPORT_UNIT'),
            (229,  'TEMP_VILLAGE',                 'COMMUNITY'),
            (229,  'TEMP_TOWN',                    'TOWN'),
            (230,  'TEMP_APPLICANT_AGE',           'APPLICANT_AGE'),
            (235,  'TEMP_APPLICANT_PHONE',         'CONTACT_PHONE'),
            (235,  'TEMP_APPLICANT_GENDER',        'APPLICANT_GENDER'),
            (236,  'TEMP_VILLAGE',                 'COMMUNITY'),
            (237,  'TEMP_APPLICANT_AGE',           'APPLICANT_AGE'),
            (237,  'TEMP_APPLICANT_GENDER',        'APPLICANT_GENDER'),
            (238,  'TEMP_APPLICANT_PHONE',         'CONTACT_PHONE'),
            (249,  'TEMP_APPLICANT_PHONE',         'CONTACT_PHONE'),
            (249,  'TEMP_APPLICANT_GENDER',        'APPLICANT_GENDER'),
            (140,  'ELDERLY_FAMILY_ADDRESS',       'FAMILY_ADDRESS'),
            (140,  'ELDERLY_PHONE',                'CONTACT_PHONE'),
            (140,  'ELDERLY_AGE',                  'APPLICANT_AGE'),
            (140,  'ELDERLY_ID_CARD',              'APPLICANT_ID_CARD'),
            (140,  'ELDERLY_NAME',                 'APPLICANT_NAME'),
            (141,  'ELDERLY_PHONE',                'CONTACT_PHONE'),
            (143,  'ELDERLY_FAMILY_ADDRESS',       'FAMILY_ADDRESS'),
            (143,  'ELDERLY_ID_CARD',              'APPLICANT_ID_CARD'),
            (143,  'ELDERLY_NAME',                 'APPLICANT_NAME'),
            (250,  'ELDERLY_ADJUST_NAME',          'APPLICANT_NAME'),
            (250,  'ELDERLY_ADJUST_ID_CARD',       'APPLICANT_ID_CARD'),
            (250,  'ELDERLY_ADJUST_HUKOU_ADDRESS', 'HUKOU_ADDRESS'),
            (250,  'ELDERLY_ADJUST_FAMILY_ADDRESS','FAMILY_ADDRESS'),
            (151,  'NEAR_RELATIVE_NAME',           'APPLICANT_NAME'),
            (151,  'NEAR_RELATIVE_GENDER',         'APPLICANT_GENDER'),
            (151,  'NEAR_RELATIVE_FAMILY_ADDRESS', 'FAMILY_ADDRESS'),
            (151,  'NEAR_RELATIVE_FAMILY_SIZE',    'FAMILY_SIZE'),
            (151,  'NEAR_RELATIVE_TOWN_OPINION',   'TOWN_OPINION'),
            (151,  'NEAR_RELATIVE_AUDIT_TIME',     'AUDIT_DATE'),
            (152,  'NEAR_RELATIVE_NAME',           'APPLICANT_NAME'),
            (152,  'NEAR_RELATIVE_ID_CARD',        'APPLICANT_ID_CARD'),
            (152,  'NEAR_RELATIVE_HUKOU_ADDRESS',  'HUKOU_ADDRESS'),
            (152,  'NEAR_RELATIVE_RESIDENCE_ADDRESS','RESIDENCE_ADDRESS'),
            (152,  'NEAR_RELATIVE_FAMILY_SIZE',    'FAMILY_SIZE'),
            (152,  'NEAR_RELATIVE_TOWN_OPINION',   'TOWN_OPINION'),
            (152,  'NEAR_RELATIVE_AUDIT_TIME',     'AUDIT_DATE'),
            (153,  'NEAR_RELATIVE_AUDIT_TIME',     'AUDIT_DATE'),
            (154,  'NEAR_RELATIVE_REPORT_UNIT',    'REPORT_UNIT'),
            (138,  'SPECIAL_APPROVAL_AUDIT_DATE',  'AUDIT_DATE'),
            (221,  'APPLICANT_PHONE',              'CONTACT_PHONE'),
            (222,  'SCATTERED_AUDIT_TIME',         'AUDIT_DATE'),
            (241,  'NOTICE_TOWN',                  'TOWN')
        ) AS m(tpl, old_key, new_key)
          ON m.tpl = t.id AND m.old_key = e.value->>'fieldKey'
    ), true),
    updated_at = NOW()
WHERE t.id IN (138, 140, 141, 143, 151, 152, 153, 154, 221, 222,
               226, 229, 230, 235, 236, 237, 238, 241, 248, 249, 250);

-- 2. T222 按合并原则 R3 补 format（同源 tl.AuditDate、异格式：ISO → 中文日期，使输出与旧键一致）
--    迁移后 T222 内 AUDIT_DATE 唯一（原 config 无此键），按 fieldKey 定位安全。
UPDATE nc_biz_templates
SET config_json = jsonb_set(config_json, '{fields}', (
        SELECT jsonb_agg(
                   CASE WHEN e.value->>'fieldKey' = 'AUDIT_DATE'
                        THEN jsonb_set(e.value, '{format}', '"yyyy年M月d日"')
                        ELSE e.value
                   END
                   ORDER BY e.ord)
        FROM jsonb_array_elements(config_json -> 'fields') WITH ORDINALITY AS e(value, ord)
    ), true),
    updated_at = NOW()
WHERE id = 222
  AND EXISTS (SELECT 1 FROM jsonb_array_elements(config_json -> 'fields') x
              WHERE x->>'fieldKey' = 'AUDIT_DATE');

-- 3. 验收
-- 3a. 27 个旧键全库剩余行数（期望 1 = T255 ADJ_AUDIT_TIME 延后）
SELECT count(*) AS old_key_rows_left
FROM nc_biz_templates, jsonb_array_elements(config_json -> 'fields') f
WHERE f->>'fieldKey' IN ('DM_HEAD_RESIDENCE','DM_TOWN_OPINION','TEMP_APPLICANT_AGE','TEMP_APPLICANT_GENDER',
    'TEMP_REPORT_UNIT','TEMP_VILLAGE','TEMP_TOWN','TEMP_APPLICANT_PHONE','ELDERLY_FAMILY_ADDRESS','ELDERLY_PHONE',
    'ELDERLY_AGE','ELDERLY_ID_CARD','ELDERLY_NAME','ELDERLY_ADJUST_NAME','ELDERLY_ADJUST_ID_CARD',
    'ELDERLY_ADJUST_HUKOU_ADDRESS','ELDERLY_ADJUST_FAMILY_ADDRESS','NEAR_RELATIVE_NAME','NEAR_RELATIVE_GENDER',
    'NEAR_RELATIVE_FAMILY_ADDRESS','NEAR_RELATIVE_FAMILY_SIZE','NEAR_RELATIVE_TOWN_OPINION','NEAR_RELATIVE_AUDIT_TIME',
    'NEAR_RELATIVE_ID_CARD','NEAR_RELATIVE_HUKOU_ADDRESS','NEAR_RELATIVE_RESIDENCE_ADDRESS','NEAR_RELATIVE_REPORT_UNIT',
    'SPECIAL_APPROVAL_AUDIT_DATE','APPLICANT_PHONE','SCATTERED_AUDIT_TIME','NOTICE_TOWN');

-- 3b. 非 ASCII 键（期望 0）与总行数（期望 3314）
SELECT count(*) FILTER (WHERE f->>'fieldKey' !~ '^[A-Z0-9_]+$') AS non_ascii,
       count(*) AS total_rows
FROM nc_biz_templates, jsonb_array_elements(config_json -> 'fields') f;

-- 3c. T222 AUDIT_DATE 行的 format 落位（期望 yyyy年M月d日）
SELECT id, f->>'fieldKey', f->>'format'
FROM nc_biz_templates, jsonb_array_elements(config_json -> 'fields') f
WHERE id = 222 AND f->>'fieldKey' = 'AUDIT_DATE';

COMMIT;
