# 大学生「今年毕业」名单成员身份过滤 + is_household_stopped 作用域缺陷修复

> 日期：2026-10-12。代码改动 2 文件：
> `Services\Domain\SocialAssistance\CollegeStudentService.cs`（备份 `backup\20261012_135939_大学生毕业身份过滤\`）、
> `Services\Domain\ChangeManagement\ChangeService.RebuildAndReview.cs`（备份 `backup\20261012_145316_大学生application_id回写\`）。
> 另有 1 次幂等数据回填（张宇昌/张微 `application_id` → 90）。
> `dotnet build` **0 CS 错误 0 CS 警告**（仅 MSB3027/3021 exe 文件锁，因程序正在运行未产出 exe，关程序重跑即可）。
> 触发问题：张微为何仍显示在 2026 届「今年毕业」名单。

## 1. 背景

张微（档案13，2022 入学 4 年制 → 2026 届，状态 Graduated）出现在大学生管理 Tab3「今年毕业」
与首页毕业提醒横幅。2026-10-12 上午对李增会户做了成员变更：

- 身份变更：**张微 共同生活成员 → 赡养抚养扶养**（`nc_biz_change_records` id=50）
- 户月保障金 1106.00 → 79.00 元（id=49）
- 触发停旧建新：档案 89 → `Stopped`，新建档案 90 → `Draft`（`StopAndRebuildAsync` 固定行为，见 §4）

## 2. 缺陷 A：停保过滤不看成员身份（本次主修）

`HouseholdStoppedExpr` 的两个子查询只按身份证匹配 `nc_biz_family_members`，不看 `member_category`。
张微在新档 90 只是一条 `Support`（赡养抚养扶养人）记录，却被当作「该户仍在保/在途」的证据，
把她在名单里留下。但按业务口径，**赡养抚养扶养人不是保障对象、不计入共同生活家庭人数**
（`ChangeService.DeathAndQueries.cs:77-80`、`DynamicManagementRecordService.cs:156-164` 均已如此），
"核查是否需退出低保"的提醒对她不成立。

**修改**：两个子查询各加一条与既有口径一致的过滤

```sql
AND (m.member_category IS NULL OR m.member_category <> 'Support')   -- m2 同理
```

### ⚠️ EXISTS(停保) 侧故意不加 `m.deleted_at IS NULL`

身份变更的写法是「软删旧的共同生活成员行 + 插入 Support 行」。张微在档案 89 的实际行：

| 成员ID | member_category | deleted_at |
|---|---|---|
| 812 | 共同生活成员 | 已软删 |
| 815 | SharedLiving | 已软删 |
| 818 | Support | 存活 |

若 EXISTS 侧只认存活行，则只剩 Support → `EXISTS` 恒 False → 整个过滤对
「旧档共同生活成员 → 新档赡养人」这一类**静默失效**。此处按"历史上曾是保障对象"取证，
须保留软删行。已在代码注释中标注，勿"顺手补上"。

## 3. 缺陷 B：GetAllAsync 的 is_household_stopped 恒为 False（连带修复）

`GetAllAsync` 原先传裸列名 `HouseholdStoppedExpr("id_card")`，而子查询内 `m`/`m2` 也有 `id_card` 列。
PostgreSQL 就近解析，实际执行的是 `m.id_card = m.id_card`（恒真），**外层学生完全不参与关联**。

后果：`is_household_stopped` 对所有人生效同一结果（修复前实测全体 False），
「已退出」分栏恒为空——这是本次排查中发现的、改动前就存在的缺陷。

**修改**：外层表取别名 `cs`，改传 `cs.id_card`（`SelectColumns` 为裸列名，外层单表无歧义，无需改）。

> 同型陷阱：`HouseholdStoppedExpr` 是**拼接式**表达式，调用方传什么列名就绑什么表。
> 任何新消费方都必须传带外层别名的形式（如 `cs.id_card`），传裸列名即中招且不报错。

## 4. 附带确认：90 号档案为何是 Draft（非缺陷）

`StopAndRebuildAsync`（`ChangeService.RebuildAndReview.cs:865-925`）硬编码：复制旧档 →
新档 `status=Draft`、`current_step=WorkflowSteps.ENTRY_START`(=1)、`original_application_id=旧ID`，
并清空停保/渐退期字段。**成员变更触发停旧建新后，新档必须从录入第 1 步重走完整流程**，
旧档置 Stopped 并结清渐退期。90 号今晨刚生成、无人推进，停在 Draft 属「在途」，是设计预期。

## 5. 验证（SQL 干跑，逐字取自改后代码）

**今年毕业名单（Tab3 与首页横幅同源 `GetGraduatingStudentsAsync`）**

| 口径 | 结果 |
|---|---|
| 改前 | 张微、梁佳宁 |
| 改后 | 仅梁佳宁 |

张微逐条件拆解：`EXISTS(停保档案中有非赡养身份行)=True`（812/815 软删行取证）、
`EXISTS(在保在途档案中有非赡养身份行)=False`（90 档仅 Support）→ 判定保障已终止 → 剔除。

**已关联/已退出分栏（`GetAllAsync`，修复缺陷 B 后）**

- 已退出：张微、贺满程
- 已关联：刘成宇、卢世欣、庞元辛、张宇昌、张宏翼、彭艳秋、李静雯、梁佳宁、梁茗辉、王良伟、闫渤琪

两个消费方口径一致。贺满程（10-09 剔除结论）不受影响，他始终是 `HouseholdHead`。

## 6. 缺陷 C：`application_id` 无回写回路（治本 + 补现状）

### 现状核查（补前）

| 学生 | 现值 | 应为 | 判定 |
|---|---|---|---|
| 张宇昌 | 0 | 90 | **断链** |
| 张微 | 0 | 90 | **断链** |
| 贺满程 | 83 | 83 | 正确（83 虽 Stopped，但按"无在保/在途则回退最新档"口径落在 83） |
| 其余 11 人 | 0 | 0 | 正确——身份证在成员表里本就无任何档案 |

张宇昌、张微同属 2026-10-12 李增会户那次停旧建新（89→90），两条断链是同一操作造成的。

### 治本：`StopAndRebuildAsync` 增加第 4.5 步

`Services\Domain\ChangeManagement\ChangeService.RebuildAndReview.cs`，在成员复制（`memberIdMap` 构建）之后插入按身份证回写：

```sql
UPDATE nc_biz_college_students cs
   SET application_id = $1, updated_at = NOW()
 WHERE cs.deleted_at IS NULL
   AND cs.application_id IS DISTINCT FROM $1
   AND EXISTS (SELECT 1 FROM nc_biz_family_members m
                WHERE m.application_id = $1 AND m.deleted_at IS NULL
                  AND m.id_card = cs.id_card)
```

要点：

- **不按 `member_category` 过滤**（业务确认）。该列语义是「所属户档案」，不是「作为保障对象的档案」。
  张微今年毕业后不再是保障对象，但作为李增会的子女必须尽赡养义务，改判 `Support` 后**仍留在户档案内**，
  因此仍应指向 90。
- 幂等（`IS DISTINCT FROM $1`），重复执行无副作用。
- 失败即返回 Failure 回滚整个停旧建新事务，不产生"新档已建但学生仍指旧档"的半成品。
- 覆盖全部四条调用路径：经济复核、家庭成员变更、死亡处理、跨类变更。

> ⚠️ `family_member_id` 仍不回写：该列混语义（家族成员 id ∪ 导入库人员 id），13/13 已悬空。
> 主流程过滤一律只用身份证，沿用 10-09 结论，不动表结构。

### 补现状

幂等 UPDATE 执行结果：**影响 2 行**（张宇昌、张微 → 90），第二次执行 0 行（幂等验证通过）。
备份在 `backup\20261012_145316_大学生application_id回写\`。

### 补后校验

- `application_id>0` 的 3 人全部指向有效档案：贺满程→83(Stopped)、张宇昌→90(Draft)、张微→90(Draft)
- 张微在 90 档成员行 `member_category=Support` 且存活，学生表 `application_id=90` —— 未按身份过滤的预期行为
- 今年毕业名单回归：仍仅梁佳宁（未受影响）

## 7. 数据链核查结论

| 链 | 现状 | 结论 |
|---|---|---|
| `college_students.application_id` | 3 人非 0 全部有效；11 人=0 属本就无档 | **本批已治本 + 补齐** |
| `college_students.family_member_id` | 13/13 全部悬空/已软删 | 断链。沿用 10-09「遗留 2」结论：混语义必腐，不动表结构，仅文档标注 |
| 身份证 `id_card` | 13/13 完整、18 位、可关联 | **唯一可靠关联键**，过滤与回写均只依赖它 |

`GetByApplicationIdAsync`（唯一按 `application_id` 查询的方法）无任何 ViewModel 调用方，断链此前不影响功能；本批修复后该列可信，可放心被后续消费。

## 8. 待办（不在本批）

1. `family_member_id` 建议在 Schema YAML 中标注实际语义，或废弃该列——本批未动。
2. 冒烟（待 UI，程序当时正在运行）：见 §9。

## 9. 冒烟清单

- [ ] 启动后进「大学生管理」→ Tab3「今年毕业」只显示梁佳宁
- [ ] 首页大学生毕业提醒横幅计数为 1（回首页即刷新，无需重启）
- [ ] Tab「已关联」不含张微、贺满程；「已退出」含二人
- [ ] Tab0「可关联」搜索张微，仍可正常操作（本改动不影响搜索）
- [ ] 变更/复核/停保后再回首页，横幅计数即时下降
- [ ] **回写回路**：对含大学生的家庭再走一次成员变更/停旧建新，确认该生 `application_id`
      自动前移到新档（看 app_.log 的 `停旧建新回写大学生档案: 新档=N, 更新 X 人`）
- [ ] 张微档案详情仍能看到她作为赡养抚养扶养人在李增会户内（本次改动不应把她从户里去掉）
