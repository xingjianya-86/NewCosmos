-- ============================================================================
-- 2026-11-03 模板 241《档案_变更告知书》正文文案修正（配套应用侧：具体原因称谓与尾句号，
--                    详见 docs\20261103_告知书称谓与双句号修正.md）
--
-- 修正内容（仅 word/document.xml 正文两处字面文本，占位符/config_json 均不变）：
--   1) 「申请家庭人口{户主家庭人口}人。」→「申请家庭人口{户主家庭人口}。」
--      字段值 NOTICE_FAMILY_SIZE_TEXT 自带"人"字（"1人"），模板再带"人"渲染成"1人人"。
--   2) 「决定对您家庭{承办分类}的社会救助保障待遇{享受类型}{享受结果}。」
--      →「决定对您家庭{承办分类}{享受类型}{享受结果}。」
--      字面"的社会救助保障待遇"与 {享受结果} 重复，渲染成"…社会救助保障待遇停止享受社会救助保障待遇"。
--
-- 执行情况：2026-11-03 已在 new_cosmos 库执行；
--   docx 二进制（file_data/file_hash）由同目录 20261103_changenotice_template_text_fix.ps1 修正
--  （密码读环境变量 NEWCOSMOS_DB_PASSWORD，AGENTS §12），更正后 SHA256 见脚本输出与
--   backup_20261103_changenotice_template 对照行。
--   体检：Scripts\check_template_fields.ps1 通过。
-- ============================================================================

BEGIN;

-- 更正前快照（回滚依据；重复执行安全）
CREATE TABLE IF NOT EXISTS backup_20261103_changenotice_template AS
SELECT id, name, file_type, file_data, file_hash, config_json, sort_order, updated_at, now() AS backed_up_at
FROM nc_biz_templates WHERE id = 241;

COMMIT;

-- 复核查询（应为 1 / 0 / 0；file_hash 应为 64 位 hex 非空）：
--   SELECT id,
--          octet_length(file_data)                          AS bytes,
--          file_hash IS NOT NULL                            AS has_hash,
--          updated_at
--   FROM nc_biz_templates WHERE id = 241;
--   -- 正文两处旧文案应均为 0 命中（需解包 docx 核对，见 fix.ps1 回读校验）：
--   --   '人。根据《牡丹江市低收入人口认定实施细则》' → 0
--   --   '<w:t>的社会救助保障待遇</w:t>'             → 0
--
-- 回滚（恢复更正前模板二进制）：
--   UPDATE nc_biz_templates t
--   SET file_data = b.file_data, file_hash = b.file_hash, updated_at = b.updated_at
--   FROM backup_20261103_changenotice_template b WHERE t.id = b.id;
