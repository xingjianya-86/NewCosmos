using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Services.System;

/// <summary>
/// 客户端版本台账服务实现：本机身份 = %LOCALAPPDATA%\NewCosmos\client_id.txt 持久 GUID。
/// </summary>
public class ClientVersionService : BaseService, IClientVersionService
{
    protected override string ServiceName => "ClientVersionService";

    private static readonly string StateRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NewCosmos");
    private static readonly string ClientIdFile = Path.Combine(StateRoot, "client_id.txt");

    private readonly IDatabaseService _db;

    public ClientVersionService(IDatabaseService db, ILoggerService logger) : base(logger)
    {
        _db = db;
    }

    public async Task<Result> ReportAsync(string appVersion, CancellationToken ct = default)
    {
        try
        {
            var clientId = GetOrCreateClientId();
            var machineName = Environment.MachineName;
            var userName = string.IsNullOrWhiteSpace(App.CurrentUserFullName)
                ? App.CurrentUserName
                : App.CurrentUserFullName;

            // 单语句 upsert（client_id 无唯一约束依赖：UPDATE 命中则不 INSERT）
            var sql = @"WITH upd AS (
                            UPDATE nc_sys_client_versions
                               SET machine_name = $2, user_name = $3, app_version = $4,
                                   last_seen_at = NOW(), updated_at = NOW()
                             WHERE client_id = $1
                             RETURNING id
                        )
                        INSERT INTO nc_sys_client_versions
                            (client_id, machine_name, user_name, app_version, first_seen_at, last_seen_at, updated_at)
                        SELECT $1, $2, $3, $4, NOW(), NOW(), NOW()
                        WHERE NOT EXISTS (SELECT 1 FROM upd);";

            var result = await _db.ExecuteNonQueryAsync(sql, ct, clientId, machineName, userName ?? string.Empty, appVersion);
            if (result.IsFailure)
                return Result.Failure(result.ErrorCode ?? Constants.ErrorCodes.DB_QUERY_ERROR, result.Message ?? "客户端版本上报失败");

            Logger.LogBusiness("客户端版本上报完成", ("ClientId", clientId), ("Version", appVersion));
            return Result.Success();
        }
        catch (Exception ex)
        {
            LogException(ex, "客户端版本上报");
            return Result.FromException(ex);
        }
    }

    public async Task<Result<List<ClientVersionRow>>> GetListAsync(CancellationToken ct = default)
    {
        var sql = @"SELECT id, client_id, machine_name, user_name, app_version, first_seen_at, last_seen_at
                      FROM nc_sys_client_versions
                     ORDER BY last_seen_at DESC NULLS LAST
                     LIMIT 500";
        return await _db.QueryAsync<ClientVersionRow>(sql, ct);
    }

    private static string GetOrCreateClientId()
    {
        try
        {
            if (File.Exists(ClientIdFile))
            {
                var existing = File.ReadAllText(ClientIdFile).Trim();
                if (!string.IsNullOrWhiteSpace(existing)) return existing;
            }

            Directory.CreateDirectory(StateRoot);
            var id = Guid.NewGuid().ToString("N");
            File.WriteAllText(ClientIdFile, id);
            return id;
        }
        catch
        {
            // 极端情况下回退机器名（上报仍可用，仅多机同名时合并）
            return $"fallback-{Environment.MachineName}".ToLowerInvariant();
        }
    }
}
