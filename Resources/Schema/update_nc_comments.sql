-- ============================================
-- NC 表注释批量更新脚本
-- 生成时间: 2026-05-12
-- 说明: 为所有 NC 表添加模块前缀注释
-- ============================================

-- ============================================
-- 字典模块 (nc_dict_)
-- ============================================

-- nc_dict_categories
COMMENT ON TABLE nc_dict_categories IS '[字典] 字典分类种子数据表（固定ID）';
COMMENT ON COLUMN nc_dict_categories.id IS '主键ID（固定）';
COMMENT ON COLUMN nc_dict_categories.category IS '分类代码';
COMMENT ON COLUMN nc_dict_categories.display_name IS '分类显示名称';
COMMENT ON COLUMN nc_dict_categories.description IS '分类描述';
COMMENT ON COLUMN nc_dict_categories.sort_order IS '排序序号';
COMMENT ON COLUMN nc_dict_categories.is_active IS '是否启用';

-- nc_dict_categories_updates
COMMENT ON TABLE nc_dict_categories_updates IS '[字典] 字典分类更新数据表';
COMMENT ON COLUMN nc_dict_categories_updates.id IS '引用种子数据ID';
COMMENT ON COLUMN nc_dict_categories_updates.category IS '分类代码';
COMMENT ON COLUMN nc_dict_categories_updates.display_name IS '分类显示名称';
COMMENT ON COLUMN nc_dict_categories_updates.description IS '分类描述';
COMMENT ON COLUMN nc_dict_categories_updates.sort_order IS '排序序号';
COMMENT ON COLUMN nc_dict_categories_updates.is_active IS '是否启用';
COMMENT ON COLUMN nc_dict_categories_updates.source IS '更新来源（user/system）';
COMMENT ON COLUMN nc_dict_categories_updates.action IS '操作类型（update/add/delete）';
COMMENT ON COLUMN nc_dict_categories_updates.created_at IS '创建时间';
COMMENT ON COLUMN nc_dict_categories_updates.created_by IS '创建人ID';
COMMENT ON COLUMN nc_dict_categories_updates.updated_at IS '更新时间';

-- nc_dict_items
COMMENT ON TABLE nc_dict_items IS '[字典] 字典项种子数据表（固定ID）';
COMMENT ON COLUMN nc_dict_items.id IS '主键ID（固定）';
COMMENT ON COLUMN nc_dict_items.category IS '字典分类';
COMMENT ON COLUMN nc_dict_items.item_key IS '字典项键';
COMMENT ON COLUMN nc_dict_items.item_value IS '字典项值';
COMMENT ON COLUMN nc_dict_items.sort_order IS '排序序号';
COMMENT ON COLUMN nc_dict_items.is_active IS '是否启用';
COMMENT ON COLUMN nc_dict_items.description IS '描述';

-- nc_dict_items_updates
COMMENT ON TABLE nc_dict_items_updates IS '[字典] 字典项更新数据表';
COMMENT ON COLUMN nc_dict_items_updates.id IS '引用种子数据ID';
COMMENT ON COLUMN nc_dict_items_updates.category IS '字典分类';
COMMENT ON COLUMN nc_dict_items_updates.item_key IS '字典项键';
COMMENT ON COLUMN nc_dict_items_updates.item_value IS '字典项值';
COMMENT ON COLUMN nc_dict_items_updates.sort_order IS '排序序号';
COMMENT ON COLUMN nc_dict_items_updates.is_active IS '是否启用';
COMMENT ON COLUMN nc_dict_items_updates.description IS '描述';
COMMENT ON COLUMN nc_dict_items_updates.source IS '更新来源（user/system）';
COMMENT ON COLUMN nc_dict_items_updates.action IS '操作类型（update/add/delete）';
COMMENT ON COLUMN nc_dict_items_updates.created_at IS '创建时间';
COMMENT ON COLUMN nc_dict_items_updates.created_by IS '创建人ID';
COMMENT ON COLUMN nc_dict_items_updates.updated_at IS '更新时间';

-- ============================================
-- 业务模块 (nc_biz_)
-- ============================================

-- nc_biz_low_income_families
COMMENT ON TABLE nc_biz_low_income_families IS '[业务] 低收入家庭表';
COMMENT ON COLUMN nc_biz_low_income_families.id IS '主键ID';
COMMENT ON COLUMN nc_biz_low_income_families.applicant_name IS '户主姓名';
COMMENT ON COLUMN nc_biz_low_income_families.applicant_id_card IS '户主身份证号';
COMMENT ON COLUMN nc_biz_low_income_families.family_size IS '家庭人数';
COMMENT ON COLUMN nc_biz_low_income_families.annual_income IS '家庭年收入';
COMMENT ON COLUMN nc_biz_low_income_families.phone IS '联系电话';
COMMENT ON COLUMN nc_biz_low_income_families.address IS '家庭地址';
COMMENT ON COLUMN nc_biz_low_income_families.township IS '乡镇';
COMMENT ON COLUMN nc_biz_low_income_families.village IS '村';
COMMENT ON COLUMN nc_biz_low_income_families.status IS '状态（Draft草稿/Approved已审批/Completed已完成）';
COMMENT ON COLUMN nc_biz_low_income_families.remark IS '备注';
COMMENT ON COLUMN nc_biz_low_income_families.imported_at IS '导入时间';
COMMENT ON COLUMN nc_biz_low_income_families.created_at IS '创建时间';
COMMENT ON COLUMN nc_biz_low_income_families.updated_at IS '更新时间';

-- nc_biz_low_income_persons
COMMENT ON TABLE nc_biz_low_income_persons IS '[业务] 低收入人员表';
COMMENT ON COLUMN nc_biz_low_income_persons.id IS '主键ID';
COMMENT ON COLUMN nc_biz_low_income_persons.family_id IS '关联家庭ID';
COMMENT ON COLUMN nc_biz_low_income_persons.name IS '姓名';
COMMENT ON COLUMN nc_biz_low_income_persons.id_card IS '身份证号';
COMMENT ON COLUMN nc_biz_low_income_persons.gender IS '性别';
COMMENT ON COLUMN nc_biz_low_income_persons.birth_date IS '出生日期';
COMMENT ON COLUMN nc_biz_low_income_persons.age IS '年龄';
COMMENT ON COLUMN nc_biz_low_income_persons.ethnicity IS '民族';
COMMENT ON COLUMN nc_biz_low_income_persons.relationship IS '与户主关系';
COMMENT ON COLUMN nc_biz_low_income_persons.health_status IS '健康状况';
COMMENT ON COLUMN nc_biz_low_income_persons.disability_level IS '残疾等级';
COMMENT ON COLUMN nc_biz_low_income_persons.disability_type IS '残疾类型';
COMMENT ON COLUMN nc_biz_low_income_persons.work_capacity IS '劳动能力';
COMMENT ON COLUMN nc_biz_low_income_persons.education_level IS '文化程度';
COMMENT ON COLUMN nc_biz_low_income_persons.marital_status IS '婚姻状况';
COMMENT ON COLUMN nc_biz_low_income_persons.employment_status IS '就业状况';
COMMENT ON COLUMN nc_biz_low_income_persons.is_disabled IS '是否残疾';
COMMENT ON COLUMN nc_biz_low_income_persons.disability_certificate IS '残疾证号';
COMMENT ON COLUMN nc_biz_low_income_persons.self_care_ability IS '生活自理能力';
COMMENT ON COLUMN nc_biz_low_income_persons.disease_type IS '患病病种';
COMMENT ON COLUMN nc_biz_low_income_persons.school_name IS '在读学校名称';
COMMENT ON COLUMN nc_biz_low_income_persons.school_nature IS '在读学校性质';
COMMENT ON COLUMN nc_biz_low_income_persons.enrollment_date IS '入学时间';
COMMENT ON COLUMN nc_biz_low_income_persons.poverty_flag IS '是否建档立卡';
COMMENT ON COLUMN nc_biz_low_income_persons.registration_type IS '户籍性质';
COMMENT ON COLUMN nc_biz_low_income_persons.admission_month IS '最初享受月份';
COMMENT ON COLUMN nc_biz_low_income_persons.community IS '所属社区';
COMMENT ON COLUMN nc_biz_low_income_persons.family_address IS '家庭地址';
COMMENT ON COLUMN nc_biz_low_income_persons.support_institution IS '供养机构';
COMMENT ON COLUMN nc_biz_low_income_persons.caregiver_name IS '照料护理人姓名';
COMMENT ON COLUMN nc_biz_low_income_persons.support_mode IS '供养方式';
COMMENT ON COLUMN nc_biz_low_income_persons.certificate_number IS '低保（特困）证号';
COMMENT ON COLUMN nc_biz_low_income_persons.protection_amount IS '分类施保金额';
COMMENT ON COLUMN nc_biz_low_income_persons.political_status IS '政治面貌';
COMMENT ON COLUMN nc_biz_low_income_persons.operator_town IS '经办乡镇';
COMMENT ON COLUMN nc_biz_low_income_persons.protection_category IS '救助类别';
COMMENT ON COLUMN nc_biz_low_income_persons.imported_at IS '导入时间';
COMMENT ON COLUMN nc_biz_low_income_persons.created_at IS '创建时间';
COMMENT ON COLUMN nc_biz_low_income_persons.updated_at IS '更新时间';

-- nc_biz_high_oil_soybean
COMMENT ON TABLE nc_biz_high_oil_soybean IS '[业务] 高油大豆补贴数据表';
COMMENT ON COLUMN nc_biz_high_oil_soybean.id IS '主键ID';
COMMENT ON COLUMN nc_biz_high_oil_soybean.name IS '姓名';
COMMENT ON COLUMN nc_biz_high_oil_soybean.id_card IS '身份证号';
COMMENT ON COLUMN nc_biz_high_oil_soybean.phone IS '联系电话';
COMMENT ON COLUMN nc_biz_high_oil_soybean.address IS '家庭地址';
COMMENT ON COLUMN nc_biz_high_oil_soybean.total_area IS '总面积';
COMMENT ON COLUMN nc_biz_high_oil_soybean.self_area IS '自种面积';
COMMENT ON COLUMN nc_biz_high_oil_soybean.rented_area IS '租种面积';
COMMENT ON COLUMN nc_biz_high_oil_soybean.variety IS '品种';
COMMENT ON COLUMN nc_biz_high_oil_soybean.seed_quantity IS '种子数量';
COMMENT ON COLUMN nc_biz_high_oil_soybean.subsidy_area IS '补贴面积';
COMMENT ON COLUMN nc_biz_high_oil_soybean.source IS '来源';
COMMENT ON COLUMN nc_biz_high_oil_soybean.data_year IS '数据年份';
COMMENT ON COLUMN nc_biz_high_oil_soybean.imported_at IS '导入时间';
COMMENT ON COLUMN nc_biz_high_oil_soybean.created_at IS '创建时间';
COMMENT ON COLUMN nc_biz_high_oil_soybean.updated_at IS '更新时间';

-- nc_biz_high_protein_soybean
COMMENT ON TABLE nc_biz_high_protein_soybean IS '[业务] 高蛋白大豆补贴数据表';
COMMENT ON COLUMN nc_biz_high_protein_soybean.id IS '主键ID';
COMMENT ON COLUMN nc_biz_high_protein_soybean.name IS '姓名';
COMMENT ON COLUMN nc_biz_high_protein_soybean.id_card IS '身份证号';
COMMENT ON COLUMN nc_biz_high_protein_soybean.phone IS '联系电话';
COMMENT ON COLUMN nc_biz_high_protein_soybean.address IS '家庭地址';
COMMENT ON COLUMN nc_biz_high_protein_soybean.total_area IS '总面积';
COMMENT ON COLUMN nc_biz_high_protein_soybean.self_area IS '自种面积';
COMMENT ON COLUMN nc_biz_high_protein_soybean.rented_area IS '租种面积';
COMMENT ON COLUMN nc_biz_high_protein_soybean.variety IS '品种';
COMMENT ON COLUMN nc_biz_high_protein_soybean.seed_quantity IS '种子数量';
COMMENT ON COLUMN nc_biz_high_protein_soybean.subsidy_area IS '补贴面积';
COMMENT ON COLUMN nc_biz_high_protein_soybean.source IS '来源';
COMMENT ON COLUMN nc_biz_high_protein_soybean.data_year IS '数据年份';
COMMENT ON COLUMN nc_biz_high_protein_soybean.imported_at IS '导入时间';
COMMENT ON COLUMN nc_biz_high_protein_soybean.created_at IS '创建时间';
COMMENT ON COLUMN nc_biz_high_protein_soybean.updated_at IS '更新时间';

-- ============================================
-- 家庭经济模块 (nc_family_)
-- ============================================

-- nc_family_incomes
COMMENT ON TABLE nc_family_incomes IS '[家庭经济] 家庭收入表';
COMMENT ON COLUMN nc_family_incomes.id IS '主键ID';
COMMENT ON COLUMN nc_family_incomes.family_id IS '家庭ID（关联nc_biz_applications.id或nc_low_income_families.id）';
COMMENT ON COLUMN nc_family_incomes.income_type IS '收入类别代码（Wage/Business/Property/Transfer/Alimony/Other）';
COMMENT ON COLUMN nc_family_incomes.amount IS '收入金额（元）';
COMMENT ON COLUMN nc_family_incomes.source IS '收入来源说明';
COMMENT ON COLUMN nc_family_incomes.recipient_member_id IS '收入归属家庭成员ID（NULL表示全家）';
COMMENT ON COLUMN nc_family_incomes.recipient_member_name IS '收入归属家庭成员姓名（显示用）';
COMMENT ON COLUMN nc_family_incomes.created_at IS '创建时间';
COMMENT ON COLUMN nc_family_incomes.updated_at IS '更新时间';
COMMENT ON COLUMN nc_family_incomes.version IS '版本号（乐观并发控制）';

-- nc_family_assets
COMMENT ON TABLE nc_family_assets IS '[家庭经济] 家庭资产表';
COMMENT ON COLUMN nc_family_assets.id IS '主键ID';
COMMENT ON COLUMN nc_family_assets.family_id IS '家庭ID（关联nc_biz_applications.id或nc_low_income_families.id）';
COMMENT ON COLUMN nc_family_assets.asset_type IS '资产类别代码（RealEstate/Vehicle/Deposit/Securities/Insurance/Other）';
COMMENT ON COLUMN nc_family_assets.asset_name IS '资产名称/描述';
COMMENT ON COLUMN nc_family_assets.estimated_value IS '估算价值（元）';
COMMENT ON COLUMN nc_family_assets.ownership_status IS '所有权状态代码（SelfOwned/JointOwned/Rented/Other）';
COMMENT ON COLUMN nc_family_assets.is_exempted IS '是否豁免';
COMMENT ON COLUMN nc_family_assets.exemption_type IS '豁免类型代码';
COMMENT ON COLUMN nc_family_assets.owner_member_id IS '资产归属家庭成员ID（NULL表示全家）';
COMMENT ON COLUMN nc_family_assets.owner_member_name IS '资产归属家庭成员姓名（显示用）';
COMMENT ON COLUMN nc_family_assets.remark IS '备注';
COMMENT ON COLUMN nc_family_assets.created_at IS '创建时间';
COMMENT ON COLUMN nc_family_assets.updated_at IS '更新时间';
COMMENT ON COLUMN nc_family_assets.version IS '版本号（乐观并发控制）';

-- nc_family_expenditures
COMMENT ON TABLE nc_family_expenditures IS '[家庭经济] 家庭支出表（刚性支出）';
COMMENT ON COLUMN nc_family_expenditures.id IS '主键ID';
COMMENT ON COLUMN nc_family_expenditures.family_id IS '家庭ID（关联nc_biz_applications.id或nc_low_income_families.id）';
COMMENT ON COLUMN nc_family_expenditures.expenditure_type IS '支出类别代码（Living/Medical/Education/DisabilityRehab/Housing/Other）';
COMMENT ON COLUMN nc_family_expenditures.amount IS '支出金额（元）';
COMMENT ON COLUMN nc_family_expenditures.description IS '支出说明';
COMMENT ON COLUMN nc_family_expenditures.related_member_id IS '支出关联家庭成员ID（如医疗支出对应患病成员）';
COMMENT ON COLUMN nc_family_expenditures.related_member_name IS '支出关联家庭成员姓名（显示用）';
COMMENT ON COLUMN nc_family_expenditures.occurred_at IS '支出发生日期（可选）';
COMMENT ON COLUMN nc_family_expenditures.created_at IS '创建时间';
COMMENT ON COLUMN nc_family_expenditures.updated_at IS '更新时间';
COMMENT ON COLUMN nc_family_expenditures.version IS '版本号（乐观并发控制）';

-- ============================================
-- 配置模块 (nc_config_)
-- ============================================

-- nc_config_income_standards
COMMENT ON TABLE nc_config_income_standards IS '[配置] 收入标准表';
COMMENT ON COLUMN nc_config_income_standards.id IS '主键ID';
COMMENT ON COLUMN nc_config_income_standards.hukou_type IS '户籍类型（农村/城市）';
COMMENT ON COLUMN nc_config_income_standards.monthly_standard IS '月收入标准（元）';
COMMENT ON COLUMN nc_config_income_standards.effective_start_date IS '生效开始日期';
COMMENT ON COLUMN nc_config_income_standards.effective_end_date IS '生效结束日期';
COMMENT ON COLUMN nc_config_income_standards.created_at IS '创建时间';
COMMENT ON COLUMN nc_config_income_standards.updated_at IS '更新时间';

-- nc_config_subsidy_standards
COMMENT ON TABLE nc_config_subsidy_standards IS '[配置] 分类补贴标准表';
COMMENT ON COLUMN nc_config_subsidy_standards.id IS '主键ID';
COMMENT ON COLUMN nc_config_subsidy_standards.hukou_type IS '户籍类型（农村/城市）';
COMMENT ON COLUMN nc_config_subsidy_standards.per_person_amount IS '人均补贴金额（元/月）';
COMMENT ON COLUMN nc_config_subsidy_standards.effective_start_date IS '生效开始日期';
COMMENT ON COLUMN nc_config_subsidy_standards.effective_end_date IS '生效结束日期';
COMMENT ON COLUMN nc_config_subsidy_standards.created_at IS '创建时间';
COMMENT ON COLUMN nc_config_subsidy_standards.updated_at IS '更新时间';

-- nc_config_destitute_standards
COMMENT ON TABLE nc_config_destitute_standards IS '[配置] 特困供养标准表';
COMMENT ON COLUMN nc_config_destitute_standards.id IS '主键ID';
COMMENT ON COLUMN nc_config_destitute_standards.hukou_type IS '户籍类型（农村/城市）';
COMMENT ON COLUMN nc_config_destitute_standards.support_mode IS '供养方式（Centralized集中/Scattered分散）';
COMMENT ON COLUMN nc_config_destitute_standards.monthly_standard IS '月供养标准（元）';
COMMENT ON COLUMN nc_config_destitute_standards.effective_start_date IS '生效开始日期';
COMMENT ON COLUMN nc_config_destitute_standards.effective_end_date IS '生效结束日期';
COMMENT ON COLUMN nc_config_destitute_standards.created_at IS '创建时间';
COMMENT ON COLUMN nc_config_destitute_standards.updated_at IS '更新时间';

-- nc_config_subsidy_prices
COMMENT ON TABLE nc_config_subsidy_prices IS '[配置] 补贴价格配置表';
COMMENT ON COLUMN nc_config_subsidy_prices.id IS '主键ID';
COMMENT ON COLUMN nc_config_subsidy_prices.year IS '年份';
COMMENT ON COLUMN nc_config_subsidy_prices.subsidy_type IS '补贴类型代码';
COMMENT ON COLUMN nc_config_subsidy_prices.subsidy_type_name IS '补贴类型名称';
COMMENT ON COLUMN nc_config_subsidy_prices.price_per_acre IS '每亩补贴金额（元）';
COMMENT ON COLUMN nc_config_subsidy_prices.created_at IS '创建时间';
COMMENT ON COLUMN nc_config_subsidy_prices.updated_at IS '更新时间';

-- nc_config_system_standards
COMMENT ON TABLE nc_config_system_standards IS '[配置] 系统配置标准表';
COMMENT ON COLUMN nc_config_system_standards.id IS '主键ID';
COMMENT ON COLUMN nc_config_system_standards.config_key IS '配置键（唯一标识）';
COMMENT ON COLUMN nc_config_system_standards.config_value IS '配置值';
COMMENT ON COLUMN nc_config_system_standards.display_name IS '显示名称';
COMMENT ON COLUMN nc_config_system_standards.unit IS '单位';
COMMENT ON COLUMN nc_config_system_standards.category IS '分类';
COMMENT ON COLUMN nc_config_system_standards.sort_order IS '排序序号';
COMMENT ON COLUMN nc_config_system_standards.description IS '描述说明';
COMMENT ON COLUMN nc_config_system_standards.effective_start_date IS '生效开始日期（NULL表示长期有效）';
COMMENT ON COLUMN nc_config_system_standards.effective_end_date IS '生效结束日期（NULL表示长期有效）';
COMMENT ON COLUMN nc_config_system_standards.created_at IS '创建时间';
COMMENT ON COLUMN nc_config_system_standards.updated_at IS '更新时间';

-- nc_config_standards
COMMENT ON TABLE nc_config_standards IS '[配置] 统一标准配置表（新系统）';
COMMENT ON COLUMN nc_config_standards.id IS '主键ID';
COMMENT ON COLUMN nc_config_standards.standard_type IS '标准类型代码';
COMMENT ON COLUMN nc_config_standards.standard_name IS '标准名称（显示用）';
COMMENT ON COLUMN nc_config_standards.hukou_type IS '户籍类型（Rural农村/Urban城市）';
COMMENT ON COLUMN nc_config_standards.support_mode IS '供养方式（Centralized集中/Scattered分散）';
COMMENT ON COLUMN nc_config_standards.standard_value IS '标准值';
COMMENT ON COLUMN nc_config_standards.unit IS '单位';
COMMENT ON COLUMN nc_config_standards.effective_start_date IS '生效开始日期';
COMMENT ON COLUMN nc_config_standards.effective_end_date IS '生效结束日期（NULL表示长期有效）';
COMMENT ON COLUMN nc_config_standards.version IS '版本号';
COMMENT ON COLUMN nc_config_standards.is_active IS '是否启用';
COMMENT ON COLUMN nc_config_standards.description IS '描述说明';
COMMENT ON COLUMN nc_config_standards.created_by IS '创建人';
COMMENT ON COLUMN nc_config_standards.created_at IS '创建时间';
COMMENT ON COLUMN nc_config_standards.updated_at IS '更新时间';

-- ============================================
-- 权限模块 (nc_perm_)
-- ============================================

-- nc_perm_role_permissions
COMMENT ON TABLE nc_perm_role_permissions IS '[权限] 角色-权限关联表（直接存储权限代码）';
COMMENT ON COLUMN nc_perm_role_permissions.role_id IS '角色ID（引用 nc_sys_roles 表）';
COMMENT ON COLUMN nc_perm_role_permissions.permission_code IS '权限代码（直接存储代码，不引用权限表）';
COMMENT ON COLUMN nc_perm_role_permissions.created_at IS '创建时间';

-- nc_perm_user_permissions
COMMENT ON TABLE nc_perm_user_permissions IS '[权限] 用户-权限关联表（用户直接权限）';
COMMENT ON COLUMN nc_perm_user_permissions.user_id IS '用户ID（引用 nc_sys_users 表）';
COMMENT ON COLUMN nc_perm_user_permissions.permission_code IS '权限代码';
COMMENT ON COLUMN nc_perm_user_permissions.granted_at IS '授权时间';
COMMENT ON COLUMN nc_perm_user_permissions.granted_by IS '授权人ID（引用 nc_sys_users 表）';

-- nc_perm_cache_version
COMMENT ON TABLE nc_perm_cache_version IS '[权限] 权限系统缓存版本表';
COMMENT ON COLUMN nc_perm_cache_version.key IS '缓存键名（如 permissions, initialized）';
COMMENT ON COLUMN nc_perm_cache_version.version IS '版本号';
COMMENT ON COLUMN nc_perm_cache_version.updated_at IS '更新时间';

-- ============================================
-- 视图模块 (nc_view_)
-- ============================================

COMMENT ON VIEW nc_view_dict_categories IS '[视图] 字典分类视图（合并种子数据和更新数据）';
COMMENT ON VIEW nc_view_dict_items IS '[视图] 字典项视图（合并种子数据和更新数据）';

-- nc_biz_land_contract_contractor
COMMENT ON TABLE nc_biz_land_contract_contractor IS '[业务] 农村土地承包权归户-承包方总表';
COMMENT ON COLUMN nc_biz_land_contract_contractor.id IS '主键ID';
COMMENT ON COLUMN nc_biz_land_contract_contractor.contractor_name IS '承包方（代表）姓名';
COMMENT ON COLUMN nc_biz_land_contract_contractor.contractor_pinyin IS '承包方姓名拼音（辅助查询）';
COMMENT ON COLUMN nc_biz_land_contract_contractor.employer_name IS '发包方名称';
COMMENT ON COLUMN nc_biz_land_contract_contractor.total_contract_area IS '合同面积（总）';
COMMENT ON COLUMN nc_biz_land_contract_contractor.total_measured_area IS '实测面积（总）';
COMMENT ON COLUMN nc_biz_land_contract_contractor.data_year IS '数据年份';
COMMENT ON COLUMN nc_biz_land_contract_contractor.imported_at IS '导入时间';
COMMENT ON COLUMN nc_biz_land_contract_contractor.created_at IS '创建时间';
COMMENT ON COLUMN nc_biz_land_contract_contractor.updated_at IS '更新时间';

-- nc_biz_land_contract_plot
COMMENT ON TABLE nc_biz_land_contract_plot IS '[业务] 农村土地承包权归户-地块明细表';
COMMENT ON COLUMN nc_biz_land_contract_plot.id IS '主键ID';
COMMENT ON COLUMN nc_biz_land_contract_plot.contractor_id IS '关联承包方ID';
COMMENT ON COLUMN nc_biz_land_contract_plot.plot_name IS '地块名称';
COMMENT ON COLUMN nc_biz_land_contract_plot.plot_code IS '地块代码';
COMMENT ON COLUMN nc_biz_land_contract_plot.location_east IS '东至';
COMMENT ON COLUMN nc_biz_land_contract_plot.location_south IS '南至';
COMMENT ON COLUMN nc_biz_land_contract_plot.location_west IS '西至';
COMMENT ON COLUMN nc_biz_land_contract_plot.location_north IS '北至';
COMMENT ON COLUMN nc_biz_land_contract_plot.contract_area IS '合同面积';
COMMENT ON COLUMN nc_biz_land_contract_plot.measured_area IS '实测面积';
COMMENT ON COLUMN nc_biz_land_contract_plot.remarks IS '备注';
COMMENT ON COLUMN nc_biz_land_contract_plot.created_at IS '创建时间';
COMMENT ON COLUMN nc_biz_land_contract_plot.updated_at IS '更新时间';

-- nc_biz_elderly_subsidy_history
COMMENT ON TABLE nc_biz_elderly_subsidy_history IS '[业务] 普惠高龄补贴历史数据表';
COMMENT ON COLUMN nc_biz_elderly_subsidy_history.id IS '主键ID';
COMMENT ON COLUMN nc_biz_elderly_subsidy_history.name IS '老人姓名';
COMMENT ON COLUMN nc_biz_elderly_subsidy_history.pinyin_name IS '姓名拼音（辅助查询）';
COMMENT ON COLUMN nc_biz_elderly_subsidy_history.id_card IS '身份证号码';
COMMENT ON COLUMN nc_biz_elderly_subsidy_history.gender IS '性别（从身份证计算）';
COMMENT ON COLUMN nc_biz_elderly_subsidy_history.birth_date IS '出生日期（从身份证计算）';
COMMENT ON COLUMN nc_biz_elderly_subsidy_history.age IS '年龄';
COMMENT ON COLUMN nc_biz_elderly_subsidy_history.phone IS '联系电话';
COMMENT ON COLUMN nc_biz_elderly_subsidy_history.address IS '户籍地址';
COMMENT ON COLUMN nc_biz_elderly_subsidy_history.subsidy_amount IS '发放金额';
COMMENT ON COLUMN nc_biz_elderly_subsidy_history.bank_account IS '银行卡号';
COMMENT ON COLUMN nc_biz_elderly_subsidy_history.person_type IS '人员类型（系统）';
COMMENT ON COLUMN nc_biz_elderly_subsidy_history.original_type IS '原始类型（Excel）';
COMMENT ON COLUMN nc_biz_elderly_subsidy_history.data_year IS '数据年份';
COMMENT ON COLUMN nc_biz_elderly_subsidy_history.imported_at IS '导入时间';
COMMENT ON COLUMN nc_biz_elderly_subsidy_history.created_at IS '创建时间';
COMMENT ON COLUMN nc_biz_elderly_subsidy_history.updated_at IS '更新时间';

-- nc_biz_planting_subsidy
COMMENT ON TABLE nc_biz_planting_subsidy IS '[业务] 农村种植补贴数据表';
COMMENT ON COLUMN nc_biz_planting_subsidy.id IS '主键ID';
COMMENT ON COLUMN nc_biz_planting_subsidy.name IS '姓名';
COMMENT ON COLUMN nc_biz_planting_subsidy.id_card IS '身份证号';
COMMENT ON COLUMN nc_biz_planting_subsidy.phone IS '联系电话';
COMMENT ON COLUMN nc_biz_planting_subsidy.address IS '家庭地址';
COMMENT ON COLUMN nc_biz_planting_subsidy.total_acreage IS '总种植面积';
COMMENT ON COLUMN nc_biz_planting_subsidy.corn_acreage IS '玉米面积';
COMMENT ON COLUMN nc_biz_planting_subsidy.soybean_acreage IS '大豆面积';
COMMENT ON COLUMN nc_biz_planting_subsidy.rice_total_acreage IS '稻谷总面积';
COMMENT ON COLUMN nc_biz_planting_subsidy.rice_surface_water_acreage IS '地表水灌溉稻谷面积';
COMMENT ON COLUMN nc_biz_planting_subsidy.rice_groundwater_acreage IS '地下水灌溉稻谷面积';
COMMENT ON COLUMN nc_biz_planting_subsidy.data_year IS '数据年份';
COMMENT ON COLUMN nc_biz_planting_subsidy.imported_at IS '导入时间';
COMMENT ON COLUMN nc_biz_planting_subsidy.created_at IS '创建时间';
COMMENT ON COLUMN nc_biz_planting_subsidy.updated_at IS '更新时间';

-- nc_biz_rotation_subsidy
COMMENT ON TABLE nc_biz_rotation_subsidy IS '[业务] 农村轮作补贴数据表';
COMMENT ON COLUMN nc_biz_rotation_subsidy.id IS '主键ID';
COMMENT ON COLUMN nc_biz_rotation_subsidy.name IS '收款人姓名';
COMMENT ON COLUMN nc_biz_rotation_subsidy.id_card IS '身份证号';
COMMENT ON COLUMN nc_biz_rotation_subsidy.phone IS '联系电话';
COMMENT ON COLUMN nc_biz_rotation_subsidy.address IS '家庭地址';
COMMENT ON COLUMN nc_biz_rotation_subsidy.rotation_area IS '轮作面积（亩）';
COMMENT ON COLUMN nc_biz_rotation_subsidy.subsidy_amount IS '补贴金额';
COMMENT ON COLUMN nc_biz_rotation_subsidy.account_number IS '银行账号';
COMMENT ON COLUMN nc_biz_rotation_subsidy.bank_name IS '银行名称';
COMMENT ON COLUMN nc_biz_rotation_subsidy.bank_branch IS '开户网点';
COMMENT ON COLUMN nc_biz_rotation_subsidy.subsidy_type IS '补贴类型（社保卡/非社保卡）';
COMMENT ON COLUMN nc_biz_rotation_subsidy.rotation_type IS '轮作类型';
COMMENT ON COLUMN nc_biz_rotation_subsidy.data_year IS '数据年份';
COMMENT ON COLUMN nc_biz_rotation_subsidy.imported_at IS '导入时间';
COMMENT ON COLUMN nc_biz_rotation_subsidy.created_at IS '创建时间';
COMMENT ON COLUMN nc_biz_rotation_subsidy.updated_at IS '更新时间';

-- nc_biz_soil_subsidy
COMMENT ON TABLE nc_biz_soil_subsidy IS '[业务] 农村地力补贴数据表';
COMMENT ON COLUMN nc_biz_soil_subsidy.id IS '主键ID';
COMMENT ON COLUMN nc_biz_soil_subsidy.name IS '收款人姓名';
COMMENT ON COLUMN nc_biz_soil_subsidy.id_card IS '身份证号';
COMMENT ON COLUMN nc_biz_soil_subsidy.phone IS '联系电话';
COMMENT ON COLUMN nc_biz_soil_subsidy.address IS '家庭地址';
COMMENT ON COLUMN nc_biz_soil_subsidy.subsidy_amount IS '补贴金额';
COMMENT ON COLUMN nc_biz_soil_subsidy.account_number IS '银行账号';
COMMENT ON COLUMN nc_biz_soil_subsidy.bank_name IS '银行名称';
COMMENT ON COLUMN nc_biz_soil_subsidy.bank_branch IS '开户网点';
COMMENT ON COLUMN nc_biz_soil_subsidy.subsidy_type IS '补贴类型（社保卡/非社保卡）';
COMMENT ON COLUMN nc_biz_soil_subsidy.data_year IS '数据年份';
COMMENT ON COLUMN nc_biz_soil_subsidy.imported_at IS '导入时间';
COMMENT ON COLUMN nc_biz_soil_subsidy.created_at IS '创建时间';
COMMENT ON COLUMN nc_biz_soil_subsidy.updated_at IS '更新时间';

-- ============================================
-- 脚本执行完成
-- ============================================
