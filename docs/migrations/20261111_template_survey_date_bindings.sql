-- 2026-11-11  模板日期口径改造（config_json 换键 + 目录日期字段同步）
--
-- 一、{当前日期} → {入户调查日期}（4 张，占位符文本同步改，见同目录 20261111_declare_surveydate_text_fix.ps1）
--   T224 部省协查函            placeholder {当前日期}→{入户调查日期}，fieldKey REPORT_DATE  → SURVEY_DATE（format 不变 yyyy年MM月dd日）
--   T243 档案_结婚证遗失声明    placeholder {当前日期}→{入户调查日期}，fieldKey CURRENT_DATE → SURVEY_DATE（format 不变 空）
--   T244 档案_离婚证遗失声明    同 243
--   T245 档案_无房产声明        同 243
--   ⚠️ 241 档案_变更告知书、248 模板_社会救助对象动态管理记录 也叫 {当前日期}，**不在本次范围，勿动**。
--   依赖代码同批：资产核查两条链路补 SURVEY_DATE 供给
--     （ArchiveProductionViewModel 资产核查明细字典 + QuickAssetVerificationViewModel.ResolveAssetSurveyDate），
--     否则 224 在资产核查分类下打印时占位符原样残留。档案制作 dict A/B 与 TemplateFieldBuilder 原本就供。
--
-- 二、{诚信承诺时间} 改口径 = 入户调查当天（**改代码单点，不换键**）
--   PROMISE_DATE 供给由 ResolvePromiseDate(首次审批日/钳公示起) 改为直接复用 surveyDate
--   （ArchiveProductionViewModel 两处 dict；ResolvePromiseDate 已删）。config 的 fieldKey 保持 PROMISE_DATE 不变，
--   format=yyyy年MM月dd日 对中文日期串仍可 ApplyFormat 解析重渲，无需改。
--   临时救助链路 TempReliefPrintDataBuilder 的 PROMISE_DATE 本就 = C 线调查日，不动。
--   ⚠️ 口径反转：2026-10-16 定的「承诺落款不得早于公示起」下限作废（调查日恒早于会议日 < 公示起），
--      属 2026-11-11 用户明确要求；旧文档 docs\20261016_申请日期锚点改按调查日与文书日期口径.md 保留为历史。
--
-- 三、directoryDateField 同步（目录行日期必须与文书落款一致）
--   T174 通用_申请人承诺书   PROMISE_DATE → SURVEY_DATE
--   T178 通用_经办人承诺书   PROMISE_DATE → SURVEY_DATE
--   T221 档案_照料服务协议_特困人员 PROMISE_DATE → SURVEY_DATE（其原值为 AUDIT_DATE）
--   T196/T200 已是 SURVEY_DATE、T192 已是 SURVEY_DATE，不动。
--   注：NormalizeDirectoryDate 兼容 yyyy年M月d日 / yyyy年MM月dd日，SURVEY_DATE 值为前者，安全。
--
-- 四、T182 档案_公示文件 {公示日期} → 月份
--   fieldKey PUBLICITY_RANGE("起 至 止"合成串) → PUBLICITY_START_DATE(ISO)，format 空 → yyyy年M月
--   B 线公示起恒在当月 7~17 日，整批滚月时起止一起进下月 → 当月 "2026年11月"、滚月后 "2026年12月"。
--   PUBLICITY_START_DATE 原值不动 → directoryDateField(已是它) 与 {公示开始时间} 均不受影响。
--   PUBLICITY_RANGE 代码供给保留（本次无其它模板引用，留作后续）。
--
-- 回滚：本文件末尾 BEGIN/COMMIT 内的 UPDATE 均可按 id+placeholder 反向执行；
--       模板二进制回滚见 20261111_declare_surveydate_text_fix.ps1 的备份表。

BEGIN;

-- ── 备份（回滚用：原 config_json 快照）────────────────────────────────────
CREATE TABLE IF NOT EXISTS nc_biz_templates_bak_20261111 AS
SELECT id, name, file_type, config_json, file_hash, now() AS backed_up_at
FROM nc_biz_templates
WHERE id IN (174, 178, 182, 221, 224, 243, 244, 245);

-- ── 一、{当前日期} → {入户调查日期}（224/243/244/245）─────────────────────
-- 224：REPORT_DATE → SURVEY_DATE，format 保持 yyyy年MM月dd日
UPDATE nc_biz_templates
SET config_json = jsonb_set(config_json, '{fields}', (
    SELECT jsonb_agg(
             CASE WHEN e->>'placeholder' = '{当前日期}'
                  THEN jsonb_set(jsonb_set(e, '{placeholder}', '"{入户调查日期}"'), '{fieldKey}', '"SURVEY_DATE"')
                  ELSE e END
             ORDER BY ord)
    FROM jsonb_array_elements(config_json->'fields') WITH ORDINALITY AS a(e, ord)))
WHERE id = 224;

-- 243/244/245：CURRENT_DATE → SURVEY_DATE，format 保持空
UPDATE nc_biz_templates
SET config_json = jsonb_set(config_json, '{fields}', (
    SELECT jsonb_agg(
             CASE WHEN e->>'placeholder' = '{当前日期}'
                  THEN jsonb_set(jsonb_set(e, '{placeholder}', '"{入户调查日期}"'), '{fieldKey}', '"SURVEY_DATE"')
                  ELSE e END
             ORDER BY ord)
    FROM jsonb_array_elements(config_json->'fields') WITH ORDINALITY AS a(e, ord)))
WHERE id IN (243, 244, 245);

-- ── 三、directoryDateField 同步（174/178/221 → SURVEY_DATE）───────────────
UPDATE nc_biz_templates
SET config_json = jsonb_set(config_json, '{directoryDateField}', '"SURVEY_DATE"')
WHERE id IN (174, 178, 221);

-- ── 四、T182 {公示日期} → PUBLICITY_START_DATE + yyyy年M月 ────────────────
UPDATE nc_biz_templates
SET config_json = jsonb_set(config_json, '{fields}', (
    SELECT jsonb_agg(
             CASE WHEN e->>'placeholder' = '{公示日期}'
                  THEN jsonb_set(jsonb_set(e, '{fieldKey}', '"PUBLICITY_START_DATE"'), '{format}', '"yyyy年M月"')
                  ELSE e END
             ORDER BY ord)
    FROM jsonb_array_elements(config_json->'fields') WITH ORDINALITY AS a(e, ord)))
WHERE id = 182;

-- ── 校验 ──────────────────────────────────────────────────────────────────
-- 期望 6 行：224/243/244/245 = {入户调查日期}→SURVEY_DATE；182 = {公示日期}→PUBLICITY_START_DATE+yyyy年M月
SELECT t.id, t.name, f->>'fieldKey' AS field_key, f->>'placeholder' AS placeholder, f->>'format' AS fmt
FROM nc_biz_templates t, jsonb_array_elements(t.config_json->'fields') f
WHERE (t.id = 224 AND f->>'placeholder' = '{入户调查日期}')
   OR (t.id IN (243, 244, 245) AND f->>'placeholder' = '{入户调查日期}')
   OR (t.id = 182 AND f->>'placeholder' = '{公示日期}')
ORDER BY t.id;

-- 期望 3 行：174/178/221 directoryDateField = SURVEY_DATE
SELECT id, name, config_json->>'directoryDateField' AS dir_date
FROM nc_biz_templates
WHERE id IN (174, 178, 196, 200, 221)
ORDER BY id;

-- 负向校验（应返回 0 行）：这 4 张不应再有 {当前日期}
SELECT t.id, t.name, f->>'placeholder' AS placeholder
FROM nc_biz_templates t, jsonb_array_elements(t.config_json->'fields') f
WHERE t.id IN (224, 243, 244, 245) AND f->>'placeholder' = '{当前日期}';

-- 241/248 未被误伤（应各返回 1 行，仍为 {当前日期}）
SELECT t.id, t.name, f->>'fieldKey' AS field_key, f->>'placeholder' AS placeholder
FROM nc_biz_templates t, jsonb_array_elements(t.config_json->'fields') f
WHERE t.id IN (241, 248) AND f->>'placeholder' = '{当前日期}'
ORDER BY t.id;

COMMIT;

-- ── 回滚（逐条按需执行）──────────────────────────────────────────────────
-- UPDATE nc_biz_templates t SET config_json = b.config_json
--   FROM nc_biz_templates_bak_20261111 b WHERE t.id = b.id;
-- DROP TABLE IF EXISTS nc_biz_templates_bak_20261111;
