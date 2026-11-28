using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.ElderlyBenefits;

namespace NewCosmos.Services.Import;

/// <summary>
/// 高龄年度认证 Excel+ZIP 批量导入。
///
/// 匹配规则（按你的收集方式定死）：
///   1) Excel 名单里每行有“图片文件名/图片1/图片2...”，文件名即 manifest；
///   2) ZIP 内按文件名一对一匹配，一人可多图；
///   3) 只做精确匹配（规范化大小写/全半角/路径分隔符/URL 解码），不做模糊猜测；
///   4) 匹配不上的人的图片进“未匹配图片”池，不删除；
///   5) 结果与凭证分两段写：先结果，再图片；图片缺失不阻塞结果。
/// </summary>
public partial class ElderlyCertificationImportService : BaseService, IElderlyCertificationImportService
{
    private readonly IDatabaseService _db;
    private readonly IElderlyInfoChangeService _infoChangeService;

    protected override string ServiceName => "ElderlyCertificationImportService";

    static ElderlyCertificationImportService()
    {
        // ZIP 内中文文件名常见 GBK；与 SheetReaderFactory 保持一致，注册代码页提供器。
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public ElderlyCertificationImportService(
        IDatabaseService db,
        IElderlyInfoChangeService infoChangeService,
        ILoggerService logger) : base(logger)
    {
        _db = db;
        _infoChangeService = infoChangeService;
    }

    #region 预览

    public async Task<Result<ElderlyCertImportPreview>> PreviewAsync(
        long campaignId,
        string excelPath,
        string zipPath,
        CancellationToken ct = default)
    {
        if (campaignId <= 0)
            return Result.Failure<ElderlyCertImportPreview>(ErrorCodes.VALIDATION_FAILED, "请先选择认证批次");
        if (string.IsNullOrWhiteSpace(excelPath) || !File.Exists(excelPath))
            return Result.Failure<ElderlyCertImportPreview>(ErrorCodes.NOT_FOUND, "Excel 名单文件不存在");
        if (!string.IsNullOrWhiteSpace(zipPath) && !File.Exists(zipPath))
            return Result.Failure<ElderlyCertImportPreview>(ErrorCodes.NOT_FOUND, "图片 ZIP 文件不存在");

        var campaignResult = await GetCampaignAsync(campaignId, ct);
        if (campaignResult.IsFailure)
            return Result.Failure<ElderlyCertImportPreview>(campaignResult.ErrorCode!, campaignResult.Message!);
        var campaign = campaignResult.Value!;
        if (campaign == null)
            return Result.Failure<ElderlyCertImportPreview>(ErrorCodes.NOT_FOUND, "认证批次不存在");
        if (campaign.Status == ElderlyCertificationConstants.CampaignStatusClosed)
            return Result.Failure<ElderlyCertImportPreview>(ErrorCodes.VALIDATION_FAILED, "批次已关闭，不能继续导入");

        var warnings = new List<string>();
        var errors = new List<string>();

        List<ElderlyCertImportRowPreview> rows;
        try
        {
            rows = ReadExcelRows(excelPath, warnings, errors);
        }
        catch (Exception ex)
        {
            LogException(ex, "PreviewAsync.ReadExcelRows");
            return Result.Failure<ElderlyCertImportPreview>(ErrorCodes.VALIDATION_FAILED, $"Excel 解析失败：{ex.Message}");
        }

        if (errors.Count > 0)
            return Result.Failure<ElderlyCertImportPreview>(ErrorCodes.VALIDATION_FAILED, string.Join("；", errors));
        if (rows.Count == 0)
            return Result.Failure<ElderlyCertImportPreview>(ErrorCodes.VALIDATION_FAILED, "Excel 中没有可识别的数据行");

        MergeDuplicateRows(rows, warnings);

        var tasksResult = await LoadTasksAsync(campaignId, ct);
        if (tasksResult.IsFailure)
            return Result.Failure<ElderlyCertImportPreview>(tasksResult.ErrorCode!, tasksResult.Message!);
        MatchTasks(rows, tasksResult.Value!, warnings);

        ZipIndex? index = null;
        var zipEncoding = string.Empty;
        if (!string.IsNullOrWhiteSpace(zipPath))
        {
            var candidates = new List<(ZipIndex Index, string EncodingName)>();
            var utf8 = BuildZipIndex(zipPath, Encoding.UTF8, keepArchive: false, out var utf8Error);
            if (utf8 != null) candidates.Add((utf8, "utf-8"));
            else if (!string.IsNullOrEmpty(utf8Error)) warnings.Add($"UTF-8 方式读取 ZIP 失败：{utf8Error}");

            var gbk = BuildZipIndex(zipPath, Encoding.GetEncoding(936), keepArchive: false, out var gbkError);
            if (gbk != null) candidates.Add((gbk, "gbk"));
            else if (!string.IsNullOrEmpty(gbkError)) warnings.Add($"GBK 方式读取 ZIP 失败：{gbkError}");

            if (candidates.Count == 0)
                return Result.Failure<ElderlyCertImportPreview>(ErrorCodes.VALIDATION_FAILED, "图片 ZIP 无法读取，请检查文件是否损坏");

            var fileNames = rows.Where(r => !r.IsDuplicate).SelectMany(r => r.FileNames).ToList();
            var chosen = candidates
                .Select(c => (c.Index, c.EncodingName, Score: ScoreZipIndex(c.Index, fileNames)))
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.EncodingName == "utf-8" ? 0 : 1)
                .First();
            index = chosen.Index;
            zipEncoding = chosen.EncodingName;
            warnings.AddRange(index.Warnings);

            foreach (var candidate in candidates.Where(c => !ReferenceEquals(c.Index, index)))
                candidate.Index.Dispose();
        }

        try
        {
            foreach (var row in rows.Where(r => !r.IsDuplicate))
                MatchEvidence(row, index);

            var referencedEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows.Where(r => !r.IsDuplicate
                         && r.MatchStatus is not (ElderlyCertificationConstants.ImportRowInvalid
                             or ElderlyCertificationConstants.ImportRowConflict)))
            {
                foreach (var entry in row.MatchedEntries)
                    referencedEntries.Add(NormalizePathKey(entry.EntryName));
            }

            var unmatched = index == null
                ? new List<ElderlyCertZipEntryInfo>()
                : index.All.Where(e => !referencedEntries.Contains(NormalizePathKey(e.EntryName))).ToList();

            var preview = new ElderlyCertImportPreview
            {
                CampaignId = campaignId,
                CampaignNo = campaign.CampaignNo,
                CampaignName = campaign.DisplayName,
                BatchNo = $"IMP{DateTime.Now:yyyyMMddHHmmssfff}",
                ExcelPath = excelPath,
                ZipPath = zipPath ?? string.Empty,
                ExcelHash = await GetFileHashAsync(excelPath, ct),
                ZipHash = string.IsNullOrWhiteSpace(zipPath) ? string.Empty : await GetFileHashAsync(zipPath, ct),
                ZipEncoding = zipEncoding,
                ExcelSize = new FileInfo(excelPath).Length,
                ZipSize = string.IsNullOrWhiteSpace(zipPath) ? 0 : new FileInfo(zipPath).Length,
                Rows = rows,
                UnmatchedEntries = unmatched,
                Warnings = warnings,
                Errors = errors,
                TotalRows = rows.Count,
                MatchedRows = rows.Count(r => !r.IsDuplicate && r.TaskId.HasValue && r.MatchStatus != ElderlyCertificationConstants.ImportRowInvalid),
                UnknownRows = rows.Count(r => r.MatchStatus == ElderlyCertificationConstants.ImportRowUnknown),
                DuplicateRows = rows.Count(r => r.IsDuplicate),
                NameMismatchRows = rows.Count(r => r.IsNameMismatch),
                InvalidRows = rows.Count(r => r.MatchStatus == ElderlyCertificationConstants.ImportRowInvalid),
                ConflictRows = rows.Count(r => r.MatchStatus == ElderlyCertificationConstants.ImportRowConflict),
                EvidenceTotal = rows.Where(r => !r.IsDuplicate).Sum(r => r.ExpectedEvidenceCount),
                EvidenceMatched = rows.Where(r => !r.IsDuplicate).Sum(r => r.MatchedEvidenceCount),
                EvidenceMissing = rows.Where(r => !r.IsDuplicate).Sum(r => r.MissingFileNames.Count),
                EvidenceUnmatched = unmatched.Count
            };

            if (rows.All(r => r.ExpectedEvidenceCount == 0))
                preview.Warnings.Add("Excel 未解析到图片文件名列；如果收集表包含图片，请检查表头是否含“图片/照片/附件/文件名”。");

            return Result.Success(preview);
        }
        finally
        {
            index?.Dispose();
        }
    }

    #endregion

    #region 应用

    public async Task<Result<ElderlyCertImportApplyResult>> ApplyAsync(
        ElderlyCertImportPreview preview,
        ElderlyCertImportApplyOptions options,
        CancellationToken ct = default)
    {
        if (preview == null || preview.CampaignId <= 0)
            return Result.Failure<ElderlyCertImportApplyResult>(ErrorCodes.VALIDATION_FAILED, "导入预览无效，请重新预览");
        if (options == null || string.IsNullOrWhiteSpace(options.OperatorName))
            return Result.Failure<ElderlyCertImportApplyResult>(ErrorCodes.VALIDATION_FAILED, "缺少操作人");

        var campaignResult = await GetCampaignAsync(preview.CampaignId, ct);
        if (campaignResult.IsFailure)
            return Result.Failure<ElderlyCertImportApplyResult>(campaignResult.ErrorCode!, campaignResult.Message!);
        var campaign = campaignResult.Value!;
        if (campaign == null)
            return Result.Failure<ElderlyCertImportApplyResult>(ErrorCodes.NOT_FOUND, "认证批次不存在");
        if (campaign.Status == ElderlyCertificationConstants.CampaignStatusClosed)
            return Result.Failure<ElderlyCertImportApplyResult>(ErrorCodes.VALIDATION_FAILED, "批次已关闭，不能继续导入");

        // 预览后文件被替换时拒绝应用，避免“预览的是 A、落库的是 B”。
        var excelHash = await GetFileHashAsync(preview.ExcelPath, ct);
        if (!string.IsNullOrEmpty(preview.ExcelHash)
            && !string.Equals(excelHash, preview.ExcelHash, StringComparison.OrdinalIgnoreCase))
            return Result.Failure<ElderlyCertImportApplyResult>(ErrorCodes.VALIDATION_FAILED, "Excel 文件在预览后已变化，请重新预览");

        if (!string.IsNullOrWhiteSpace(preview.ZipPath))
        {
            var zipHash = await GetFileHashAsync(preview.ZipPath, ct);
            if (!string.IsNullOrEmpty(preview.ZipHash)
                && !string.Equals(zipHash, preview.ZipHash, StringComparison.OrdinalIgnoreCase))
                return Result.Failure<ElderlyCertImportApplyResult>(ErrorCodes.VALIDATION_FAILED, "图片 ZIP 在预览后已变化，请重新预览");
        }

        ZipIndex? index = null;
        if (!string.IsNullOrWhiteSpace(preview.ZipPath))
        {
            var encoding = ResolveZipEncoding(preview.ZipEncoding);
            index = BuildZipIndex(preview.ZipPath, encoding, keepArchive: true, out var zipError);
            if (index == null)
                return Result.Failure<ElderlyCertImportApplyResult>(ErrorCodes.VALIDATION_FAILED, $"图片 ZIP 无法读取：{zipError}");
        }

        var result = new ElderlyCertImportApplyResult();
        var batchNo = string.IsNullOrWhiteSpace(preview.BatchNo)
            ? $"IMP{DateTime.Now:yyyyMMddHHmmssfff}"
            : preview.BatchNo;
        result.BatchNo = batchNo;

        try
        {
            await using var tx = await _db.BeginTransactionScopeAsync(ct);

            var batchId = await InsertBatchAsync(preview, campaign, batchNo, options.OperatorName, ct);
            result.BatchId = batchId;

            var tasksResult = await LoadTasksAsync(preview.CampaignId, ct);
            if (tasksResult.IsFailure)
                throw new InvalidOperationException(tasksResult.Message ?? "加载认证名单失败");
            var tasks = tasksResult.Value!;
            var evidenceStateResult = await LoadEvidenceStateAsync(preview.CampaignId, ct);
            if (evidenceStateResult.IsFailure)
                throw new InvalidOperationException(evidenceStateResult.Message ?? "加载凭证状态失败");
            var evidenceState = evidenceStateResult.Value!;

            var applied = 0;
            var skipped = 0;
            var errorRows = 0;
            var evidenceInserted = 0;
            var evidenceSkipped = 0;
            var evidenceUnmatched = 0;
            var insertedHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var infoChangeCandidates = new List<(ElderlyCertImportRowPreview Row, ElderlyCertTask Task)>();

            foreach (var row in preview.Rows)
            {
                ct.ThrowIfCancellationRequested();

                var selected = options.RowNumbers == null || options.RowNumbers.Contains(row.RowNo);
                var skipReason = GetSkipReason(row, selected, options.AllowAdverseOverwrite);
                var task = row.TaskId.HasValue && tasks.TryGetValue(row.TaskId.Value, out var t) ? t : null;

                // 已停发/已标记去世的人不允许被批量导入直接“复活”，必须先人工核实。
                if (task?.CertStatus == ElderlyCertificationConstants.CertStatusStopped)
                    skipReason ??= "该人已停发，需先处理停发档案";
                if (task?.CertStatus == ElderlyCertificationConstants.CertStatusDeceased
                    && row.CertStatus != ElderlyCertificationConstants.CertStatusDeceased)
                    skipReason ??= "该人已标记去世，需人工核实后才能改回";

                // 预览后名单状态可能被他人改动：应用时按当前状态再判一次不利结果冲突。
                if (task != null && IsAdverse(row.CertStatus)
                    && task.CertStatus is ElderlyCertificationConstants.CertStatusVerified or ElderlyCertificationConstants.CertStatusExempt
                    && !options.AllowAdverseOverwrite)
                    skipReason ??= "名单当前已通过/免核查，需允许覆盖不利结果";

                var importRowResult = await InsertImportRowAsync(batchId, row, task, skipReason, ct);
                if (importRowResult.IsFailure)
                    throw new InvalidOperationException(importRowResult.Message ?? $"第{row.RowNo}行导入留痕写入失败");

                if (skipReason != null)
                {
                    skipped++;
                    continue;
                }

                if (task == null)
                {
                    skipped++;
                    continue;
                }

                var note = BuildResultNote(row, task);
                var updateResult = await UpdateTaskFromImportAsync(
                    task.Id, row, task, note, batchNo, Path.GetFileName(preview.ExcelPath), options.OperatorName, ct);
                if (updateResult.IsFailure)
                {
                    errorRows++;
                    result.Errors.Add($"第{row.RowNo}行更新失败：{updateResult.Message}");
                    continue;
                }

                // 结果已应用；图片按文件名逐张入库，单张失败不阻断该行结果。
                if (index != null && row.MatchedEntries.Count > 0)
                {
                    var seq = evidenceState.GetNextSeq(task.Id);
                    var hashes = evidenceState.GetHashes(task.Id);

                    foreach (var entryInfo in row.MatchedEntries)
                    {
                        ct.ThrowIfCancellationRequested();
                        var entry = index.FindEntry(entryInfo.EntryName);
                        if (entry == null) continue;

                        var bytes = ReadEntryBytes(entry);
                        if (bytes == null) continue;

                        var sha = ComputeSha256(bytes);
                        if (!hashes.Add(sha))
                        {
                            evidenceSkipped++;
                            continue;
                        }

                        var inserted = await InsertEvidenceAsync(
                            task.Id, batchId, seq, entryInfo, bytes, sha, campaign, task, row,
                            preview, options.OperatorName, ElderlyCertificationConstants.EvidenceMatchMatched,
                            ElderlyCertificationConstants.EvidenceMatchMethodExcelName, ct);
                        if (inserted)
                        {
                            evidenceInserted++;
                            insertedHashes.Add(sha);
                            seq++;
                            evidenceState.SetNextSeq(task.Id, seq);
                        }
                        else
                        {
                            evidenceSkipped++;
                        }
                    }
                }

                // 认证结果与信息变更是两件事：只对“已应用且非不利结果”的行收集变更候选，
                // 真正写变更单放到导入事务提交之后，避免变更校验/写库失败把整批导入拖回滚。
                if (!IsAdverse(row.CertStatus) && ElderlyInfoChangeImportHelper.HasValues(row))
                    infoChangeCandidates.Add((row, task));

                applied++;
            }

            // 名单外/未匹配行的图片也入“未匹配图片”池，便于后续人工挂接。
            foreach (var row in preview.Rows.Where(r => !r.IsDuplicate
                         && r.MatchStatus == ElderlyCertificationConstants.ImportRowUnknown
                         && (options.RowNumbers == null || options.RowNumbers.Contains(r.RowNo))))
            {
                if (index == null) continue;
                var seq = 1;
                foreach (var entryInfo in row.MatchedEntries)
                {
                    var entry = index.FindEntry(entryInfo.EntryName);
                    if (entry == null) continue;
                    var bytes = ReadEntryBytes(entry);
                    if (bytes == null) continue;
                    var sha = ComputeSha256(bytes);
                    if (!insertedHashes.Add(sha)) continue;

                    if (await InsertEvidenceAsync(
                            null, batchId, seq, entryInfo, bytes, sha, campaign, null, row,
                            preview, options.OperatorName, ElderlyCertificationConstants.EvidenceMatchUnmatched,
                            ElderlyCertificationConstants.EvidenceMatchMethodExcelName, ct))
                    {
                        evidenceUnmatched++;
                        seq++;
                    }
                }
            }

            // ZIP 里多出来的图片：不入任务，入未匹配池。
            if (index != null && preview.UnmatchedEntries.Count > 0)
            {
                var seq = 1;
                foreach (var entryInfo in preview.UnmatchedEntries)
                {
                    ct.ThrowIfCancellationRequested();
                    var entry = index.FindEntry(entryInfo.EntryName);
                    if (entry == null) continue;
                    var bytes = ReadEntryBytes(entry);
                    if (bytes == null) continue;
                    var sha = ComputeSha256(bytes);
                    if (!insertedHashes.Add(sha)) continue;

                    if (await InsertEvidenceAsync(
                            null, batchId, seq, entryInfo, bytes, sha, campaign, null, null,
                            preview, options.OperatorName, ElderlyCertificationConstants.EvidenceMatchUnmatched,
                            null, ct))
                    {
                        evidenceUnmatched++;
                        seq++;
                    }
                }
            }

            await RefreshEvidenceStatsAsync(preview.CampaignId, ct);

            var status = skipped > 0 || errorRows > 0
                ? ElderlyCertificationConstants.ImportBatchStatusPartiallyApplied
                : ElderlyCertificationConstants.ImportBatchStatusApplied;
            await UpdateBatchAsync(batchId, preview, status, applied, skipped, errorRows,
                evidenceInserted, evidenceUnmatched, options, ct);
            await MarkCampaignReviewingAsync(preview.CampaignId, options.OperatorName, ct);

            await tx.CommitAsync(ct);

            // 导入已提交：信息变更联动失败只记入摘要，不能再把整批报成失败。
            (int Created, int Skipped, int Errors) infoChangeResult = (0, 0, 0);
            try
            {
                infoChangeResult = await CreateInfoChangesAsync(
                    infoChangeCandidates, preview, options.OperatorName, result.Errors, ct);
            }
            catch (OperationCanceledException)
            {
                infoChangeResult = (0, 0, 1);
                result.Errors.Add("导入已提交，但信息变更联动被取消，请到信息变更审批页核对");
            }
            catch (Exception ex)
            {
                LogException(ex, "CreateInfoChangesAsync");
                infoChangeResult = (0, 0, 1);
                result.Errors.Add($"导入已提交，但信息变更联动失败：{ex.Message}");
            }

            result.AppliedRows = applied;
            result.SkippedRows = skipped;
            result.ErrorRows = errorRows;
            result.EvidenceInserted = evidenceInserted;
            result.EvidenceSkipped = evidenceSkipped;
            result.EvidenceUnmatched = evidenceUnmatched;
            result.InfoChangesCreated = infoChangeResult.Created;
            result.InfoChangesSkipped = infoChangeResult.Skipped;
            result.InfoChangeErrors = infoChangeResult.Errors;

            Logger.LogBusiness("高龄年度认证批量导入完成",
                ("CampaignId", preview.CampaignId),
                ("BatchNo", batchNo),
                ("Applied", applied),
                ("Skipped", skipped),
                ("Evidence", evidenceInserted));

            return Result.Success(result);
        }
        catch (Exception ex)
        {
            LogException(ex, "ApplyAsync");
            return Result.Failure<ElderlyCertImportApplyResult>(ErrorCodes.UNKNOWN_ERROR, $"导入失败：{ex.Message}");
        }
        finally
        {
            index?.Dispose();
        }
    }

    #endregion

    #region 信息变更联动

    /// <summary>
    /// 导入联动生成 Pending 信息变更单：只提交与申请主表当前值不同的白名单字段。
    /// 校验失败不回滚已提交的导入结果，按行记入 errors，由批次摘要呈现。
    /// </summary>
    private async Task<(int Created, int Skipped, int Errors)> CreateInfoChangesAsync(
        IReadOnlyList<(ElderlyCertImportRowPreview Row, ElderlyCertTask Task)> candidates,
        ElderlyCertImportPreview preview,
        string operatorName,
        List<string> errors,
        CancellationToken ct)
    {
        var created = 0;
        var skipped = 0;
        var errorCount = 0;
        if (candidates.Count == 0) return (created, skipped, errorCount);

        var sourceFile = Path.GetFileName(preview.ExcelPath);
        foreach (var (row, task) in candidates)
        {
            ct.ThrowIfCancellationRequested();

            var currentResult = await _infoChangeService.GetApplicationValuesAsync(task.Id, ct);
            if (currentResult.IsFailure || currentResult.Value == null)
            {
                errorCount++;
                errors.Add($"第{row.RowNo}行信息变更未生成：{currentResult.Message ?? "未找到在享档案"}");
                continue;
            }

            var build = ElderlyInfoChangeImportHelper.Build(row, currentResult.Value);
            if (build.HasInvalidBankAccount)
            {
                errorCount++;
                errors.Add($"第{row.RowNo}行银行卡号格式无法识别，已跳过银行卡字段");
            }
            if (build.Details.Count == 0)
            {
                if (!build.HasInvalidBankAccount) skipped++;
                continue;
            }

            var create = await _infoChangeService.CreateAsync(
                task.Id,
                "高龄年度认证导入联动",
                ElderlyInfoChangeConstants.SourceCertificationImport,
                sourceFile,
                row.RowNo,
                null,
                build.Details,
                operatorName,
                ct);
            if (create.IsFailure)
            {
                errorCount++;
                errors.Add($"第{row.RowNo}行信息变更未生成：{create.Message}");
                continue;
            }

            created++;
        }

        return (created, skipped, errorCount);
    }

    #endregion

    #region Excel 解析

    private static readonly Dictionary<string, string[]> ColumnAliases = new()
    {
        ["name"] = new[] { "姓名", "老人姓名", "老年人姓名", "申请人姓名" },
        ["id_card"] = new[] { "身份证号", "身份证号码", "公民身份号码", "身份证" },
        ["phone"] = new[] { "联系电话", "电话", "手机号", "老人电话", "联系方式" },
        ["village"] = new[] { "村社区", "所属村", "村", "乡镇", "所属乡镇", "村居" },
        ["address"] = new[] { "现居住地址", "家庭住址", "居住地址", "地址" },
        ["bank_name"] = new[] { "社保卡开户行", "开户行", "银行名称", "新开户行" },
        ["bank_account"] = new[] { "社保卡账号", "银行卡号", "卡号", "新卡号" },
        ["agent_name"] = new[] { "代办人姓名", "新代办人姓名" },
        ["agent_relation"] = new[] { "代办人关系", "新代办人关系" },
        ["agent_receive_name"] = new[] { "代领人姓名" },
        ["agent_receive_relation"] = new[] { "代领人关系" },
        ["agent_receive_bank_name"] = new[] { "代领人开户行" },
        ["agent_receive_bank_account"] = new[] { "代领人账号", "代领人卡号" },
        ["method"] = new[] { "认证方式", "核查方式", "认证形式" },
        ["result"] = new[] { "认证结果", "核查结果", "结果", "认证情况" },
        ["verified_at"] = new[] { "认证日期", "核查日期", "认证时间", "核查时间" },
        ["submitted_at"] = new[] { "提交时间", "填写时间", "申报时间" },
        ["operator"] = new[] { "填报人", "经办人", "核查人" },
        ["remark"] = new[] { "备注", "说明" },
        ["death_date"] = new[] { "死亡日期", "去世日期" },
        ["file_names"] = new[] { "图片文件名", "照片文件名", "附件名", "文件名" }
    };

    private static List<ElderlyCertImportRowPreview> ReadExcelRows(
        string excelPath, List<string> warnings, List<string> errors)
    {
        using var reader = SheetReaderFactory.Create(excelPath);
        if (reader.IsEmpty)
        {
            errors.Add("Excel 为空");
            return new List<ElderlyCertImportRowPreview>();
        }

        var headerRow = FindHeaderRow(reader);
        if (headerRow <= 0)
        {
            errors.Add("未找到表头行（需同时包含“姓名”和“身份证号”列）");
            return new List<ElderlyCertImportRowPreview>();
        }

        var map = BuildHeaderMap(reader, headerRow);
        if (!map.Columns.ContainsKey("id_card"))
        {
            errors.Add("表头缺少身份证号列");
            return new List<ElderlyCertImportRowPreview>();
        }
        if (!map.Columns.ContainsKey("name"))
            warnings.Add("表头缺少姓名列，将仅按身份证号匹配");
        if (map.ImageColumns.Count == 0 && !map.Columns.ContainsKey("file_names"))
            warnings.Add("表头未发现图片文件名列（图片/照片/附件/文件名）");

        var rows = new List<ElderlyCertImportRowPreview>();
        for (var rowNo = headerRow + 1; rowNo <= reader.RowCount; rowNo++)
        {
            var rawName = ReadCell(reader, rowNo, map.Columns, "name");
            var rawIdCard = ReadCell(reader, rowNo, map.Columns, "id_card");
            var rawPhone = ReadCell(reader, rowNo, map.Columns, "phone");
            var rawAddress = ReadCell(reader, rowNo, map.Columns, "address");
            var rawBankName = ReadCell(reader, rowNo, map.Columns, "bank_name");
            var rawBankAccount = ReadCell(reader, rowNo, map.Columns, "bank_account");
            var rawAgentName = ReadCell(reader, rowNo, map.Columns, "agent_name");
            var rawAgentRelation = ReadCell(reader, rowNo, map.Columns, "agent_relation");
            var rawAgentReceiveName = ReadCell(reader, rowNo, map.Columns, "agent_receive_name");
            var rawAgentReceiveRelation = ReadCell(reader, rowNo, map.Columns, "agent_receive_relation");
            var rawAgentReceiveBankName = ReadCell(reader, rowNo, map.Columns, "agent_receive_bank_name");
            var rawAgentReceiveBankAccount = ReadCell(reader, rowNo, map.Columns, "agent_receive_bank_account");
            var rawVillage = ReadCell(reader, rowNo, map.Columns, "village");
            var rawMethod = ReadCell(reader, rowNo, map.Columns, "method");
            var rawResult = ReadCell(reader, rowNo, map.Columns, "result");
            var rawVerifiedAt = ReadCell(reader, rowNo, map.Columns, "verified_at");
            var rawSubmittedAt = ReadCell(reader, rowNo, map.Columns, "submitted_at");
            var rawOperator = ReadCell(reader, rowNo, map.Columns, "operator");
            var rawRemark = ReadCell(reader, rowNo, map.Columns, "remark");
            var rawDeathDate = ReadCell(reader, rowNo, map.Columns, "death_date");

            var fileNames = new List<string>();
            foreach (var col in map.ImageColumns)
                AddFileNames(fileNames, ReadCell(reader, rowNo, col));

            if (map.Columns.TryGetValue("file_names", out var fileNameCol))
                AddFileNames(fileNames, ReadCell(reader, rowNo, fileNameCol));

            if (string.IsNullOrWhiteSpace(rawName) && string.IsNullOrWhiteSpace(rawIdCard) && fileNames.Count == 0)
                continue;

            var idCard = NormalizeIdCard(rawIdCard);
            var name = (rawName ?? string.Empty).Trim();
            var certStatus = ElderlyCertificationConstants.ParseCertStatus(rawResult);
            var verifyMethod = ElderlyCertificationConstants.ParseVerifyMethod(rawMethod);

            var row = new ElderlyCertImportRowPreview
            {
                RowNo = rowNo,
                RawName = rawName,
                RawIdCard = rawIdCard,
                RawMethod = rawMethod,
                RawResult = rawResult,
                Name = name,
                IdCard = idCard,
                Phone = NormalizePhone(rawPhone),
                Address = rawAddress?.Trim() ?? string.Empty,
                BankName = rawBankName?.Trim() ?? string.Empty,
                BankAccount = rawBankAccount?.Trim() ?? string.Empty,
                AgentName = rawAgentName?.Trim() ?? string.Empty,
                AgentRelation = rawAgentRelation?.Trim() ?? string.Empty,
                AgentReceiveName = rawAgentReceiveName?.Trim() ?? string.Empty,
                AgentReceiveRelation = rawAgentReceiveRelation?.Trim() ?? string.Empty,
                AgentReceiveBankName = rawAgentReceiveBankName?.Trim() ?? string.Empty,
                AgentReceiveBankAccount = rawAgentReceiveBankAccount?.Trim() ?? string.Empty,
                Village = rawVillage?.Trim() ?? string.Empty,
                Operator = rawOperator?.Trim() ?? string.Empty,
                Remark = rawRemark?.Trim() ?? string.Empty,
                CertStatus = certStatus,
                VerifyMethod = verifyMethod,
                VerifiedAt = ParseDate(rawVerifiedAt),
                SubmittedAt = ParseDate(rawSubmittedAt),
                DeathDate = ParseDate(rawDeathDate),
                FileNames = fileNames
            };

            if (string.IsNullOrWhiteSpace(idCard))
            {
                row.MatchStatus = ElderlyCertificationConstants.ImportRowInvalid;
                row.ErrorMessage = "身份证号为空";
            }
            else if (!IdCardValidator.IsValid(idCard))
            {
                row.MatchStatus = ElderlyCertificationConstants.ImportRowInvalid;
                row.ErrorMessage = "身份证号校验不通过";
            }
            else if (certStatus == ElderlyCertificationConstants.CertStatusInvalid)
            {
                row.MatchStatus = ElderlyCertificationConstants.ImportRowInvalid;
                row.ErrorMessage = $"无法识别认证结果“{rawResult}”";
            }

            rows.Add(row);
        }

        return rows;
    }

    private sealed class HeaderMap
    {
        public Dictionary<string, int> Columns { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<int> ImageColumns { get; } = new();
    }

    private static int FindHeaderRow(IExcelSheetReader reader)
    {
        var max = Math.Min(10, reader.RowCount);
        for (var row = 1; row <= max; row++)
        {
            var hasId = false;
            var hasName = false;
            for (var col = 1; col <= reader.ColumnCount; col++)
            {
                var header = NormalizeHeader(reader.GetCellText(row, col));
                if (header.Length == 0) continue;
                if (header.Contains("身份证", StringComparison.Ordinal)) hasId = true;
                if (header.Contains("姓名", StringComparison.Ordinal)) hasName = true;
            }
            if (hasId && hasName) return row;
        }
        return 0;
    }

    private static HeaderMap BuildHeaderMap(IExcelSheetReader reader, int headerRow)
    {
        var map = new HeaderMap();
        for (var col = 1; col <= reader.ColumnCount; col++)
        {
            var header = NormalizeHeader(reader.GetCellText(headerRow, col));
            if (header.Length == 0) continue;

            if (IsImageHeader(header))
                map.ImageColumns.Add(col);

            foreach (var kv in ColumnAliases)
            {
                if (map.Columns.ContainsKey(kv.Key)) continue;
                if (kv.Value.Any(alias => header.Equals(NormalizeHeader(alias), StringComparison.OrdinalIgnoreCase)))
                    map.Columns[kv.Key] = col;
            }
        }
        return map;
    }

    private static bool IsImageHeader(string header)
    {
        if (header.Contains("文件名", StringComparison.Ordinal)) return false;
        return header.Contains("图片", StringComparison.Ordinal)
               || header.Contains("照片", StringComparison.Ordinal)
               || header.Contains("附件", StringComparison.Ordinal)
               || header.Contains("凭证", StringComparison.Ordinal)
               || header.Contains("截图", StringComparison.Ordinal);
    }

    private static string ReadCell(IExcelSheetReader reader, int row, Dictionary<string, int> columns, string key)
        => columns.TryGetValue(key, out var col) ? ReadCell(reader, row, col) : string.Empty;

    private static string ReadCell(IExcelSheetReader reader, int row, int col)
    {
        var text = reader.GetCellText(row, col);
        if (string.IsNullOrWhiteSpace(text))
            text = reader.GetMergedCellText(row, col);
        return (text ?? string.Empty).Trim();
    }

    private static string NormalizeHeader(string? header)
    {
        if (string.IsNullOrWhiteSpace(header)) return string.Empty;
        var value = header.Replace("\r", string.Empty).Replace("\n", string.Empty);
        value = Regex.Replace(value, @"[（(][^）)]*[）)]", string.Empty);
        value = value.Replace("*", string.Empty).Replace("：", string.Empty).Replace(":", string.Empty);
        value = Regex.Replace(value, @"\s+", string.Empty);
        return value.Trim();
    }

    private static void AddFileNames(List<string> fileNames, string? cell)
    {
        if (string.IsNullOrWhiteSpace(cell)) return;

        var parts = Regex.Split(cell, @"[\r\n;；,，、|]+");
        foreach (var raw in parts)
        {
            var value = raw.Trim().Trim('"', '\'');
            if (value.Length == 0) continue;
            if (value is "查看" or "下载" or "点击查看" or "无") continue;

            if (Uri.TryCreate(value, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                value = Uri.UnescapeDataString(uri.AbsolutePath);
            }
            else
            {
                try { value = Uri.UnescapeDataString(value); } catch { /* 保留原值 */ }
            }

            value = value.Replace('\\', '/').Trim();
            if (value.Length == 0) continue;
            if (!fileNames.Any(f => string.Equals(NormalizePathKey(f), NormalizePathKey(value), StringComparison.OrdinalIgnoreCase)))
                fileNames.Add(value);
        }
    }

    #endregion

    #region 匹配

    private static void MergeDuplicateRows(List<ElderlyCertImportRowPreview> rows, List<string> warnings)
    {
        var groups = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.IdCard))
            .GroupBy(r => r.IdCard!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToList();

        foreach (var group in groups)
        {
            var ordered = group
                .OrderByDescending(r => r.SubmittedAt ?? DateTime.MinValue)
                .ThenByDescending(r => r.RowNo)
                .ToList();

            // 优先取“有效行”作为保留行：最新一行可能是空行/错填结果，但同证另有一次有效提交。
            var keep = ordered.FirstOrDefault(r => r.MatchStatus != ElderlyCertificationConstants.ImportRowInvalid)
                       ?? ordered[0];
            keep.SubmittedAt ??= ordered.Select(r => r.SubmittedAt).FirstOrDefault(d => d.HasValue);

            // 保留行若没填结果，回退到最近一条有结论的提交，避免“重复提交空行”把已认证结果冲掉。
            if (keep.CertStatus is ElderlyCertificationConstants.CertStatusPending or ElderlyCertificationConstants.CertStatusInvalid)
            {
                var conclusive = ordered
                    .Where(r => !ReferenceEquals(r, keep))
                    .FirstOrDefault(r => r.CertStatus is not (
                        ElderlyCertificationConstants.CertStatusPending or ElderlyCertificationConstants.CertStatusInvalid));
                if (conclusive != null)
                {
                    keep.CertStatus = conclusive.CertStatus;
                    keep.VerifyMethod = conclusive.VerifyMethod;
                    keep.VerifiedAt = conclusive.VerifiedAt;
                    keep.DeathDate = conclusive.DeathDate;
                    keep.RawResult ??= conclusive.RawResult;
                    keep.RawMethod ??= conclusive.RawMethod;
                    if (keep.MatchStatus == ElderlyCertificationConstants.ImportRowInvalid)
                    {
                        keep.MatchStatus = ElderlyCertificationConstants.ImportRowUnknown;
                        keep.ErrorMessage = null;
                    }
                }
            }

            foreach (var duplicate in ordered.Skip(1))
            {
                duplicate.IsDuplicate = true;
                duplicate.MatchStatus = ElderlyCertificationConstants.ImportRowDuplicate;
                duplicate.ErrorMessage = $"重复提交，已合并到第{keep.RowNo}行";
                foreach (var fileName in duplicate.FileNames)
                {
                    if (!keep.FileNames.Any(f => string.Equals(NormalizePathKey(f), NormalizePathKey(fileName), StringComparison.OrdinalIgnoreCase)))
                        keep.FileNames.Add(fileName);
                }
                duplicate.FileNames.Clear();
                duplicate.ExpectedEvidenceCount = 0;
                duplicate.MatchedEvidenceCount = 0;
                duplicate.EvidenceStatus = ElderlyCertificationConstants.EvidenceStatusNone;
            }

            warnings.Add($"身份证 {DataMasker.MaskIdCard(keep.IdCard ?? string.Empty)} 有 {group.Count()} 次提交，已取第{keep.RowNo}行并合并图片。");
        }
    }

    private async Task<Result<Dictionary<long, ElderlyCertTask>>> LoadTasksAsync(long campaignId, CancellationToken ct)
    {
        const string sql = @"
            SELECT id, campaign_id, id_card, name, application_id, application_no, source_kind,
                   gender, birth_date, age_snapshot, category, identity_flag, issue_amount,
                   hukou_city_id, hukou_county_id, hukou_town_id, hukou_village_id, hukou_village, hukou_address,
                   family_city_id, family_county_id, family_town_id, family_village_id, family_address,
                   phone, bank_name, bank_account, agent_name, agent_relation,
                   cert_status, verify_method, verified_at, verified_by, verifier_org, result_note,
                   death_date, expected_evidence_count, evidence_count, evidence_status,
                   stop_decision, due_stop_date, actual_stop_date, stop_application_id, stopped_at
            FROM nc_biz_elderly_cert_tasks
            WHERE campaign_id = $1 AND deleted_at IS NULL";

        var result = await _db.QueryAsync<ElderlyCertTask>(sql, ct, campaignId);
        if (result.IsFailure)
            return Result.Failure<Dictionary<long, ElderlyCertTask>>(result.ErrorCode!, result.Message!);
        return Result.Success((result.Value ?? new List<ElderlyCertTask>()).ToDictionary(t => t.Id));
    }

    private static void MatchTasks(
        List<ElderlyCertImportRowPreview> rows,
        Dictionary<long, ElderlyCertTask> tasks,
        List<string> warnings)
    {
        var byIdCard = tasks.Values
            .GroupBy(t => t.IdCard, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows.Where(r => !r.IsDuplicate))
        {
            if (row.MatchStatus == ElderlyCertificationConstants.ImportRowInvalid) continue;
            if (string.IsNullOrWhiteSpace(row.IdCard))
            {
                row.MatchStatus = ElderlyCertificationConstants.ImportRowInvalid;
                row.ErrorMessage = "身份证号为空";
                continue;
            }

            if (!byIdCard.TryGetValue(row.IdCard, out var task))
            {
                row.MatchStatus = ElderlyCertificationConstants.ImportRowUnknown;
                row.ErrorMessage = "名单外身份证（本批次应认证名单中无此人）";
                continue;
            }

            row.TaskId = task.Id;
            row.TaskName = task.Name;
            row.TaskCertStatus = task.CertStatus;
            row.TaskEvidenceCount = task.EvidenceCount;

            if (string.IsNullOrWhiteSpace(row.Name))
                row.Name = task.Name;

            row.IsNameMismatch = !NamesEqual(row.Name, task.Name);
            if (row.IsNameMismatch)
                warnings.Add($"第{row.RowNo}行姓名与名单不一致：Excel“{row.Name}” / 系统“{task.Name}”（仍按身份证匹配）");

            if (task.CertStatus == ElderlyCertificationConstants.CertStatusStopped)
            {
                row.MatchStatus = ElderlyCertificationConstants.ImportRowConflict;
                row.ErrorMessage = "该人已停发，需先处理停发档案";
                continue;
            }

            if (IsAdverse(row.CertStatus) && task.CertStatus is
                ElderlyCertificationConstants.CertStatusVerified or
                ElderlyCertificationConstants.CertStatusExempt)
            {
                row.MatchStatus = ElderlyCertificationConstants.ImportRowConflict;
                row.ErrorMessage = $"名单当前为“{ElderlyCertificationConstants.GetCertStatusName(task.CertStatus)}”，本次为“{ElderlyCertificationConstants.GetCertStatusName(row.CertStatus)}”，需允许覆盖不利结果";
                continue;
            }

            if (task.CertStatus == ElderlyCertificationConstants.CertStatusDeceased
                && row.CertStatus != ElderlyCertificationConstants.CertStatusDeceased)
            {
                row.MatchStatus = ElderlyCertificationConstants.ImportRowConflict;
                row.ErrorMessage = "名单已标记去世，本次结果需人工核对";
                continue;
            }

            row.MatchStatus = row.IsNameMismatch
                ? ElderlyCertificationConstants.ImportRowNameMismatch
                : ElderlyCertificationConstants.ImportRowMatched;
        }
    }

    private static void MatchEvidence(ElderlyCertImportRowPreview row, ZipIndex? index)
    {
        row.ExpectedEvidenceCount = row.FileNames.Count;
        row.MatchedEntries.Clear();
        row.MissingFileNames.Clear();
        row.AmbiguousFileNames.Clear();

        if (row.FileNames.Count == 0)
        {
            row.MatchedEvidenceCount = 0;
            row.EvidenceStatus = ElderlyCertificationConstants.EvidenceStatusNone;
            return;
        }

        if (index == null)
        {
            row.MatchedEvidenceCount = 0;
            row.EvidenceStatus = ElderlyCertificationConstants.EvidenceStatusMissing;
            row.MissingFileNames.AddRange(row.FileNames);
            return;
        }

        var seenEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in row.FileNames)
        {
            var key = NormalizePathKey(file);
            if (key.Length == 0) continue;

            var list = index.Find(key);
            if (list.Count == 1)
            {
                if (seenEntries.Add(NormalizePathKey(list[0].EntryName)))
                    row.MatchedEntries.Add(list[0]);
            }
            else if (list.Count > 1)
            {
                row.AmbiguousFileNames.Add(file);
            }
            else
            {
                row.MissingFileNames.Add(file);
            }
        }

        row.MatchedEvidenceCount = row.MatchedEntries.Count;
        row.EvidenceStatus = row.MatchedEvidenceCount == 0
            ? (row.AmbiguousFileNames.Count > 0
                ? ElderlyCertificationConstants.EvidenceStatusAmbiguous
                : ElderlyCertificationConstants.EvidenceStatusMissing)
            : (row.MatchedEvidenceCount >= row.ExpectedEvidenceCount
                ? ElderlyCertificationConstants.EvidenceStatusComplete
                : ElderlyCertificationConstants.EvidenceStatusPartial);
    }

    private static bool IsAdverse(string? status) => status is
        ElderlyCertificationConstants.CertStatusRefused or
        ElderlyCertificationConstants.CertStatusFailed or
        ElderlyCertificationConstants.CertStatusDeceased or
        ElderlyCertificationConstants.CertStatusMovedOut or
        ElderlyCertificationConstants.CertStatusNotLocated;

    private static bool NamesEqual(string? a, string? b)
    {
        static string Normalize(string? value) => Regex.Replace(value ?? string.Empty, @"\s+", string.Empty);
        return string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region ZIP

    private sealed class ZipIndex : IDisposable
    {
        private readonly ZipArchive? _archive;
        private readonly Dictionary<string, ZipArchiveEntry> _entries;

        public ZipIndex(ZipArchive? archive, Dictionary<string, ZipArchiveEntry> entries)
        {
            _archive = archive;
            _entries = entries;
        }

        public Dictionary<string, List<ElderlyCertZipEntryInfo>> ByFullPath { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<ElderlyCertZipEntryInfo>> ByFileName { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<ElderlyCertZipEntryInfo> All { get; } = new();
        public List<string> Warnings { get; } = new();

        public List<ElderlyCertZipEntryInfo> Find(string normalizedKey)
        {
            if (ByFullPath.TryGetValue(normalizedKey, out var matches)) return matches;
            var baseName = GetBaseName(normalizedKey);
            return ByFileName.TryGetValue(baseName, out matches) ? matches : new List<ElderlyCertZipEntryInfo>();
        }

        public ZipArchiveEntry? FindEntry(string entryName)
            => _entries.TryGetValue(NormalizePathKey(entryName), out var entry) ? entry : null;

        public void Dispose() => _archive?.Dispose();
    }

    private static ZipIndex? BuildZipIndex(
        string zipPath,
        Encoding encoding,
        bool keepArchive,
        out string? error)
    {
        error = null;
        FileStream? stream = null;
        try
        {
            stream = File.OpenRead(zipPath);
            var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false, entryNameEncoding: encoding);
            var entryLookup = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            var index = new ZipIndex(keepArchive ? archive : null, entryLookup);
            var total = 0L;

            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                if (index.All.Count >= ElderlyCertificationConstants.MaxZipEntries)
                {
                    index.Warnings.Add($"ZIP 条目超过 {ElderlyCertificationConstants.MaxZipEntries}，其余已忽略");
                    break;
                }

                var ext = Path.GetExtension(entry.Name).ToLowerInvariant();
                if (!ElderlyCertificationConstants.AllowedImageExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                    continue;
                if (entry.Length > ElderlyCertificationConstants.MaxEvidenceFileSizeBytes)
                {
                    index.Warnings.Add($"图片过大已跳过：{entry.FullName}（{entry.Length / 1024.0 / 1024.0:F1} MB）");
                    continue;
                }

                total += entry.Length;
                if (total > ElderlyCertificationConstants.MaxZipUncompressedBytes)
                {
                    index.Warnings.Add("ZIP 解压总量超过上限，其余已忽略");
                    break;
                }

                var info = new ElderlyCertZipEntryInfo
                {
                    EntryName = entry.FullName,
                    FileName = entry.Name,
                    Length = entry.Length,
                    Extension = ext
                };
                index.All.Add(info);

                var fullKey = NormalizePathKey(entry.FullName);
                var baseKey = GetBaseName(fullKey);
                AddTo(index.ByFullPath, fullKey, info);
                AddTo(index.ByFileName, baseKey, info);
                entryLookup[fullKey] = entry;
            }

            if (!keepArchive)
            {
                archive.Dispose();
                stream = null;
            }

            return index;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            Serilog.Log.Warning(ex, "[ElderlyCertificationImportService] 读取图片 ZIP 失败: {ZipPath}", zipPath);
            stream?.Dispose();
            return null;
        }
    }

    private static void AddTo(Dictionary<string, List<ElderlyCertZipEntryInfo>> dict, string key, ElderlyCertZipEntryInfo info)
    {
        if (!dict.TryGetValue(key, out var list))
        {
            list = new List<ElderlyCertZipEntryInfo>();
            dict[key] = list;
        }
        list.Add(info);
    }

    private static int ScoreZipIndex(ZipIndex index, IEnumerable<string> fileNames)
    {
        var score = 0;
        foreach (var fileName in fileNames)
        {
            var key = NormalizePathKey(fileName);
            if (key.Length == 0) continue;
            if (index.ByFullPath.ContainsKey(key)) score += 2;
            else if (index.ByFileName.ContainsKey(GetBaseName(key))) score++;
        }
        return score;
    }

    private static Encoding ResolveZipEncoding(string? name) =>
        string.Equals(name, "gbk", StringComparison.OrdinalIgnoreCase)
            ? Encoding.GetEncoding(936)
            : Encoding.UTF8;

    private static byte[]? ReadEntryBytes(ZipArchiveEntry entry)
    {
        if (entry.Length > ElderlyCertificationConstants.MaxEvidenceFileSizeBytes) return null;
        using var stream = entry.Open();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var bytes = ms.ToArray();
        return bytes.Length > ElderlyCertificationConstants.MaxEvidenceFileSizeBytes ? null : bytes;
    }

    #endregion

    #region 落库

    private async Task<long> InsertBatchAsync(
        ElderlyCertImportPreview preview,
        ElderlyCertCampaign campaign,
        string batchNo,
        string operatorName,
        CancellationToken ct)
    {
        const string sql = @"
            INSERT INTO nc_biz_elderly_cert_import_batches
                (campaign_id, batch_no, file_name, file_hash, file_size, zip_name, zip_hash, zip_size,
                 status, remark, created_at, created_by)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, NOW(), $11)
            RETURNING id";

        var result = await _db.ExecuteScalarAsync<long>(sql, ct,
            preview.CampaignId, batchNo,
            Path.GetFileName(preview.ExcelPath), preview.ExcelHash, preview.ExcelSize,
            string.IsNullOrWhiteSpace(preview.ZipPath) ? null : Path.GetFileName(preview.ZipPath),
            string.IsNullOrEmpty(preview.ZipHash) ? null : preview.ZipHash,
            preview.ZipSize,
            ElderlyCertificationConstants.ImportBatchStatusPreviewed,
            string.Empty, operatorName);

        if (result.IsFailure || result.Value <= 0)
            throw new InvalidOperationException(result.Message ?? "创建导入批次失败");
        return result.Value;
    }

    private async Task<Result> InsertImportRowAsync(
        long batchId,
        ElderlyCertImportRowPreview row,
        ElderlyCertTask? task,
        string? skipReason,
        CancellationToken ct)
    {
        const string sql = @"
            INSERT INTO nc_biz_elderly_cert_import_rows
                (batch_id, row_no, raw_name, raw_id_card, raw_phone, raw_village, raw_method, raw_result,
                 raw_verified_at, raw_submitted_at, raw_remark, raw_file_names, raw_json,
                 id_card, name, cert_status, verify_method, verified_at,
                 expected_evidence_count, matched_evidence_count, evidence_status,
                 task_id, match_status, error_message, created_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, NULL,
                    $13, $14, $15, $16, $17, $18, $19, $20, $21, $22, $23, NOW())";

        var result = await _db.ExecuteNonQueryAsync(sql, ct,
            batchId, row.RowNo, row.RawName, row.RawIdCard, row.Phone, row.Village, row.RawMethod, row.RawResult,
            row.VerifiedAt?.ToString("yyyy-MM-dd"), row.SubmittedAt?.ToString("yyyy-MM-dd HH:mm:ss"),
            row.Remark, string.Join("\n", row.FileNames),
            row.IdCard, row.Name, row.CertStatus, row.VerifyMethod, row.VerifiedAt?.Date,
            row.ExpectedEvidenceCount, row.MatchedEvidenceCount, row.EvidenceStatus,
            task?.Id, row.MatchStatus, skipReason ?? row.ErrorMessage);

        return result.IsFailure
            ? Result.Failure(result.ErrorCode!, result.Message!)
            : Result.Success();
    }

    private async Task<Result> UpdateTaskFromImportAsync(
        long taskId,
        ElderlyCertImportRowPreview row,
        ElderlyCertTask task,
        string note,
        string batchNo,
        string sourceFile,
        string operatorName,
        CancellationToken ct)
    {
        const string sql = @"
            UPDATE nc_biz_elderly_cert_tasks
            SET cert_status = CASE WHEN $2 = $14 THEN cert_status ELSE $2 END,
                verify_method = CASE WHEN $2 = $14 THEN verify_method ELSE $3 END,
                verified_at = CASE WHEN $2 = $14 THEN verified_at ELSE COALESCE($4::date, verified_at) END,
                verified_by = CASE WHEN $2 = $14 THEN verified_by ELSE COALESCE(NULLIF($5, ''), verified_by) END,
                verifier_org = COALESCE(NULLIF($6, ''), verifier_org),
                result_note = COALESCE(NULLIF($7, ''), result_note),
                death_date = CASE WHEN $2 = $8 THEN COALESCE($9::date, death_date) ELSE death_date END,
                expected_evidence_count = $10,
                source_batch_no = $11,
                source_file = $12,
                source_row = $13,
                source_submitted_at = $15::timestamp,
                last_imported_at = NOW(),
                updated_at = NOW()
            WHERE id = $1 AND deleted_at IS NULL";

        var result = await _db.ExecuteNonQueryAsync(sql, ct,
            taskId,
            row.CertStatus,
            row.VerifyMethod,
            row.VerifiedAt?.Date,
            string.IsNullOrWhiteSpace(row.Operator) ? operatorName : row.Operator,
            row.Village,
            note,
            ElderlyCertificationConstants.CertStatusDeceased,
            row.DeathDate?.Date,
            row.ExpectedEvidenceCount,
            batchNo,
            sourceFile,
            row.RowNo,
            ElderlyCertificationConstants.CertStatusPending,
            row.SubmittedAt);

        if (result.IsFailure)
            return Result.Failure(result.ErrorCode!, result.Message!);
        return result.Value > 0
            ? Result.Success()
            : Result.Failure(ErrorCodes.NOT_FOUND, "认证任务不存在或已删除");
    }

    private async Task<bool> InsertEvidenceAsync(
        long? taskId,
        long batchId,
        int seq,
        ElderlyCertZipEntryInfo entryInfo,
        byte[] bytes,
        string sha256,
        ElderlyCertCampaign campaign,
        ElderlyCertTask? task,
        ElderlyCertImportRowPreview? row,
        ElderlyCertImportPreview preview,
        string operatorName,
        string matchStatus,
        string? matchMethod,
        CancellationToken ct)
    {
        var ext = string.IsNullOrWhiteSpace(entryInfo.Extension) ? ".jpg" : entryInfo.Extension.ToLowerInvariant();
        var format = ext.TrimStart('.') switch
        {
            "jpeg" => "jpg",
            var value => value
        };
        var safeExt = ext == ".jpeg" ? ".jpg" : ext;
        var year = campaign.Year;
        var name = task?.Name ?? row?.Name ?? "未匹配";
        var idCard = task?.IdCard ?? row?.IdCard ?? string.Empty;
        var village = row?.Village ?? string.Empty;
        var seqText = Math.Max(1, seq).ToString("D2");
        var storedName = taskId.HasValue
            ? $"{campaign.CampaignNo}_{taskId.Value:D8}_{seqText}{safeExt}"
            : $"{campaign.CampaignNo}_UNMATCHED_{batchId:D8}_{seqText}{safeExt}";
        var displayName = taskId.HasValue
            ? BuildDisplayName(year, village, name, idCard, seqText, safeExt)
            : entryInfo.FileName;
        var evidenceType = ElderlyCertificationConstants.InferEvidenceType(entryInfo.FileName, row?.VerifyMethod);

        const string sql = @"
            INSERT INTO nc_biz_elderly_cert_evidence
                (task_id, batch_id, seq, evidence_type, original_name, stored_name, display_name,
                 source_zip, source_entry, match_status, match_method, photo_data, image_format,
                 file_size, sha256, created_at, created_by)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15, NOW(), $16)
            ON CONFLICT DO NOTHING";

        var result = await _db.ExecuteNonQueryAsync(sql, ct,
            taskId, batchId, Math.Max(1, seq), evidenceType, entryInfo.FileName, storedName, displayName,
            string.IsNullOrWhiteSpace(preview.ZipPath) ? null : Path.GetFileName(preview.ZipPath),
            entryInfo.EntryName, matchStatus, matchMethod, bytes, format, bytes.LongLength, sha256, operatorName);

        return result.IsSuccess && result.Value > 0;
    }

    private async Task UpdateBatchAsync(
        long batchId,
        ElderlyCertImportPreview preview,
        string status,
        int applied,
        int skipped,
        int errorRows,
        int evidenceInserted,
        int evidenceUnmatched,
        ElderlyCertImportApplyOptions options,
        CancellationToken ct)
    {
        const string sql = @"
            UPDATE nc_biz_elderly_cert_import_batches
            SET total_rows = $2, matched_rows = $3, unknown_rows = $4, duplicate_rows = $5,
                name_mismatch_rows = $6, invalid_rows = $7,
                evidence_total = $8, evidence_matched = $9, evidence_missing = $10, evidence_unmatched = $11,
                applied_rows = $12, skipped_rows = $13, error_rows = $14,
                status = $15, applied_at = NOW(), applied_by = $16, remark = $17
            WHERE id = $1";

        await _db.ExecuteNonQueryAsync(sql, ct,
            batchId, preview.TotalRows, preview.MatchedRows, preview.UnknownRows, preview.DuplicateRows,
            preview.NameMismatchRows, preview.InvalidRows, preview.EvidenceTotal, evidenceInserted,
            preview.EvidenceMissing, evidenceUnmatched, applied, skipped, errorRows, status,
            options.OperatorName,
            options.AllowAdverseOverwrite ? "允许覆盖已通过/免核查的不利结果" : null);
    }

    private async Task MarkCampaignReviewingAsync(long campaignId, string operatorName, CancellationToken ct)
    {
        const string sql = @"
            UPDATE nc_biz_elderly_cert_campaigns
            SET status = $2, updated_at = NOW(), updated_by = $3
            WHERE id = $1 AND status = $4 AND deleted_at IS NULL";
        await _db.ExecuteNonQueryAsync(sql, ct,
            campaignId,
            ElderlyCertificationConstants.CampaignStatusReviewing,
            operatorName,
            ElderlyCertificationConstants.CampaignStatusCollecting);
    }

    private async Task RefreshEvidenceStatsAsync(long campaignId, CancellationToken ct)
    {
        const string sql = @"
            UPDATE nc_biz_elderly_cert_tasks t
            SET evidence_count = s.cnt,
                evidence_status = CASE
                    WHEN s.cnt = 0 THEN
                        CASE WHEN t.expected_evidence_count > 0 THEN $2 ELSE $3 END
                    WHEN s.cnt >= t.expected_evidence_count THEN $4
                    ELSE $5
                END,
                updated_at = NOW()
            FROM (
                SELECT t2.id, COALESCE(c.cnt, 0)::int AS cnt
                FROM nc_biz_elderly_cert_tasks t2
                LEFT JOIN (
                    SELECT task_id, COUNT(*)::int AS cnt
                    FROM nc_biz_elderly_cert_evidence
                    WHERE task_id IS NOT NULL AND deleted_at IS NULL
                    GROUP BY task_id
                ) c ON c.task_id = t2.id
                WHERE t2.campaign_id = $1 AND t2.deleted_at IS NULL
            ) s
            WHERE t.id = s.id";

        await _db.ExecuteNonQueryAsync(sql, ct,
            campaignId,
            ElderlyCertificationConstants.EvidenceStatusMissing,
            ElderlyCertificationConstants.EvidenceStatusNone,
            ElderlyCertificationConstants.EvidenceStatusComplete,
            ElderlyCertificationConstants.EvidenceStatusPartial);
    }

    private sealed class EvidenceState
    {
        private readonly Dictionary<long, int> _nextSeq = new();
        private readonly Dictionary<long, HashSet<string>> _hashes = new();

        public int GetNextSeq(long taskId) => _nextSeq.TryGetValue(taskId, out var seq) ? seq : 1;
        public void SetNextSeq(long taskId, int seq) => _nextSeq[taskId] = seq;
        public HashSet<string> GetHashes(long taskId)
        {
            if (!_hashes.TryGetValue(taskId, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _hashes[taskId] = set;
            }
            return set;
        }
    }

    private async Task<Result<EvidenceState>> LoadEvidenceStateAsync(long campaignId, CancellationToken ct)
    {
        const string sql = @"
            SELECT e.task_id, e.seq, e.sha256
            FROM nc_biz_elderly_cert_evidence e
            JOIN nc_biz_elderly_cert_tasks t ON t.id = e.task_id
            WHERE t.campaign_id = $1 AND e.deleted_at IS NULL";

        var result = await _db.QueryAsync<EvidenceKeyRow>(sql, ct, campaignId);
        if (result.IsFailure)
            return Result.Failure<EvidenceState>(result.ErrorCode!, result.Message!);

        var state = new EvidenceState();
        foreach (var row in result.Value ?? new List<EvidenceKeyRow>())
        {
            if (row.TaskId is not { } taskId) continue;
            if (row.Seq >= state.GetNextSeq(taskId))
                state.SetNextSeq(taskId, row.Seq + 1);
            if (!string.IsNullOrWhiteSpace(row.Sha256))
                state.GetHashes(taskId).Add(row.Sha256);
        }
        return Result.Success(state);
    }

    private sealed class EvidenceKeyRow
    {
        public long? TaskId { get; set; }
        public int Seq { get; set; }
        public string? Sha256 { get; set; }
    }

    #endregion

    #region 工具

    private async Task<Result<ElderlyCertCampaign?>> GetCampaignAsync(long campaignId, CancellationToken ct)
    {
        const string sql = @"
            SELECT id, campaign_no, year, round, name, period_start, period_end, valid_until,
                   status, require_evidence, deadline_stop_offset, roster_generated_at,
                   closed_at, closed_by, remark, created_at, updated_at, created_by, updated_by, deleted_at
            FROM nc_biz_elderly_cert_campaigns
            WHERE id = $1 AND deleted_at IS NULL
            LIMIT 1";
        var result = await _db.QueryAsync<ElderlyCertCampaign>(sql, ct, campaignId);
        if (result.IsFailure)
            return Result.Failure<ElderlyCertCampaign?>(result.ErrorCode!, result.Message!);
        return Result.Success<ElderlyCertCampaign?>(result.Value?.FirstOrDefault());
    }

    private static string? GetSkipReason(
        ElderlyCertImportRowPreview row,
        bool selected,
        bool allowAdverseOverwrite)
    {
        if (!selected) return "未选中";
        if (row.IsDuplicate) return "重复提交，已合并到最新一行";
        if (row.MatchStatus == ElderlyCertificationConstants.ImportRowInvalid) return row.ErrorMessage ?? "无效行";
        if (row.MatchStatus == ElderlyCertificationConstants.ImportRowUnknown) return "名单外身份证";
        if (row.MatchStatus == ElderlyCertificationConstants.ImportRowConflict && !allowAdverseOverwrite)
            return row.ErrorMessage ?? "结果冲突，未允许覆盖";
        if (!row.TaskId.HasValue) return "未匹配到认证任务";
        return null;
    }

    private static string BuildResultNote(ElderlyCertImportRowPreview row, ElderlyCertTask task)
    {
        var note = row.Remark ?? string.Empty;
        if (row.IsNameMismatch)
        {
            var mismatch = $"姓名不一致（Excel：{row.Name}，系统：{task.Name}）";
            note = string.IsNullOrWhiteSpace(note) ? mismatch : $"{note}；{mismatch}";
        }
        return note;
    }

    private static string BuildDisplayName(int year, string? village, string? name, string? idCard, string seq, string ext)
    {
        var parts = new[]
        {
            $"{year}年度",
            SanitizeFileNamePart(village, "未分组"),
            SanitizeFileNamePart(name, "未命名"),
            idCard is { Length: >= 4 } ? idCard[^4..] : "无证号",
            seq
        };
        var value = string.Join("_", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        return $"{value}{ext}";
    }

    private static string SanitizeFileNamePart(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var result = new string(chars).Trim('_', '.', ' ');
        if (result.Length == 0) return fallback;
        return result.Length > 40 ? result[..40] : result;
    }

    private static string NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return string.Empty;
        return new string(phone.Where(c => char.IsDigit(c) || c == '-' || c == '+').ToArray()).Trim();
    }

    private static string NormalizeIdCard(string? idCard)
    {
        if (string.IsNullOrWhiteSpace(idCard)) return string.Empty;
        var chars = idCard.Trim()
            .Where(c => !char.IsWhiteSpace(c))
            .Select(c => c is >= '０' and <= '９' ? (char)(c - 0xFEE0) : c)
            .ToArray();
        return new string(chars).ToUpperInvariant();
    }

    private static DateTime? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (DateTime.TryParse(text, CultureInfo.GetCultureInfo("zh-CN"), DateTimeStyles.AllowWhiteSpaces, out var dt))
            return dt;
        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out dt))
            return dt;
        return null;
    }

    private static string NormalizePathKey(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        var value = path.Trim().Trim('"', '\'');
        try { value = Uri.UnescapeDataString(value); } catch { /* 保留原值 */ }
        value = value.Replace('\\', '/');
        value = value.Split('?')[0].Split('#')[0];
        while (value.StartsWith("./", StringComparison.Ordinal)) value = value[2..];
        value = value.TrimStart('/');
        value = value.Normalize(NormalizationForm.FormC);
        return value.ToLowerInvariant();
    }

    private static string GetBaseName(string normalizedPath)
    {
        var index = normalizedPath.LastIndexOf('/');
        return index >= 0 ? normalizedPath[(index + 1)..] : normalizedPath;
    }

    private static async Task<string> GetFileHashAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string ComputeSha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    #endregion
}
