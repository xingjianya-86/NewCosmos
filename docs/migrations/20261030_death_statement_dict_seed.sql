-- 死亡情况说明表单字典种子（2026-10-30）
-- 背景：死亡地点/现场所见/遗体安置/相关业务事项 选项改由字典驱动（禁硬编码），
--       分类与字典项见 Resources\Seed\sys_dictionary\{categories\_index,items\death_place,
--       items\death_situation,items\death_remains_arrangement,items\business_matter}.yaml。
-- 为何需要本迁移：DictionaryService.EnsureDataInitializedAsync 仅在 nc_dict_items 为空时播种，
--       现网已有数据的库不会自动拿到新分类 96-99，需执行本脚本（或「数据中心→字典管理→从种子初始化」）。
-- 幂等：固定 ID + ON CONFLICT DO NOTHING，可重复执行；不覆盖运营后续修改（先到先得）。

-- 分类（ID 96-99）
INSERT INTO nc_dict_categories (id, category, display_name, description, sort_order, is_active)
VALUES
    (96, 'DeathPlaces',             '死亡地点',       '死亡情况说明-死亡地点下拉预设（首项为默认值）', 96, true),
    (97, 'DeathSituations',         '现场所见情况',   '死亡情况说明-现场所见情况预设（首项为默认值）', 97, true),
    (98, 'DeathRemainsArrangements','遗体安置情况',   '死亡情况说明-遗体安置情况预设（首项为默认值）', 98, true),
    (99, 'BusinessMatters',         '相关业务事项',   '死亡情况说明-相关业务事项多选项（首项默认勾选）', 99, true)
ON CONFLICT (id) DO NOTHING;

-- 死亡地点（96001-96005；首项=默认值）
INSERT INTO nc_dict_items (id, category, item_key, item_value, sort_order, is_active)
VALUES
    (96001, 'DeathPlaces', '家中', '家中', 1, true),
    (96002, 'DeathPlaces', '医院', '医院', 2, true),
    (96003, 'DeathPlaces', '养老机构（敬老院）', '养老机构（敬老院）', 3, true),
    (96004, 'DeathPlaces', '路上', '路上', 4, true),
    (96005, 'DeathPlaces', '其他', '其他', 5, true)
ON CONFLICT (id) DO NOTHING;

-- 现场所见情况（97001-97004；首项=默认值，模板 {死者情况描述}）
INSERT INTO nc_dict_items (id, category, item_key, item_value, sort_order, is_active)
VALUES
    (97001, 'DeathSituations', '经实地查看，情况属实', '经实地查看，情况属实', 1, true),
    (97002, 'DeathSituations', '经实地查看，死者无异常体征，情况属实', '经实地查看，死者无异常体征，情况属实', 2, true),
    (97003, 'DeathSituations', '经实地了解，死者生前患病卧床、抢救无效死亡，情况属实', '经实地了解，死者生前患病卧床、抢救无效死亡，情况属实', 3, true),
    (97004, 'DeathSituations', '其他', '其他', 4, true)
ON CONFLICT (id) DO NOTHING;

-- 遗体安置情况（98001-98004；首项=默认值，模板 {死亡人遗体安置情况}）
INSERT INTO nc_dict_items (id, category, item_key, item_value, sort_order, is_active)
VALUES
    (98001, 'DeathRemainsArrangements', '已由亲属接走火化安葬', '已由亲属接走火化安葬', 1, true),
    (98002, 'DeathRemainsArrangements', '已由亲属接走土葬安葬', '已由亲属接走土葬安葬', 2, true),
    (98003, 'DeathRemainsArrangements', '遗体已送殡仪馆存放', '遗体已送殡仪馆存放', 3, true),
    (98004, 'DeathRemainsArrangements', '其他', '其他', 4, true)
ON CONFLICT (id) DO NOTHING;

-- 相关业务事项（99001-99005；首项=默认勾选，多选「、」拼接）
INSERT INTO nc_dict_items (id, category, item_key, item_value, sort_order, is_active)
VALUES
    (99001, 'BusinessMatters', '申请居民死亡医学证明', '申请居民死亡医学证明', 1, true),
    (99002, 'BusinessMatters', '户口注销', '户口注销', 2, true),
    (99003, 'BusinessMatters', '社保待遇停发', '社保待遇停发', 3, true),
    (99004, 'BusinessMatters', '高龄津贴停发', '高龄津贴停发', 4, true),
    (99005, 'BusinessMatters', '其他', '其他', 5, true)
ON CONFLICT (id) DO NOTHING;
