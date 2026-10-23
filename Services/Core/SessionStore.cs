using System.Text.Json;
using NewCosmos.Models.Session;

namespace NewCosmos.Services.Core;

public class SessionStore : ISessionStore
{
    private const string SessionKey = "newcosmos.session";
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(7);

    private readonly ILoggerService _logger;

    public SessionStore(ILoggerService logger)
    {
        _logger = logger;
    }

    public async Task SaveAsync(UserSession session)
    {
        try
        {
            var json = JsonSerializer.Serialize(session);
            await SecureStorage.SetAsync(SessionKey, json);
        }
        catch (Exception ex)
        {
            _logger.Warn($"保存登录会话失败: {ex.Message}");
        }
    }

    public async Task<UserSession?> LoadAsync()
    {
        try
        {
            var json = await SecureStorage.GetAsync(SessionKey);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            var session = JsonSerializer.Deserialize<UserSession>(json);
            // SessionId 为空 = 升级前的历史缓存（当时数据库无会话记录），无法校验，直接作废
            if (session == null || session.UserId <= 0 || session.SessionId == Guid.Empty)
            {
                await ClearAsync();
                return null;
            }

            // 有效期优先取数据库签发的 expires_at；旧缓存无该字段时按登录时间 + 默认 7 天兜底
            var expiresAt = session.ExpiresAtUtc == default
                ? session.LoginAtUtc + SessionLifetime
                : session.ExpiresAtUtc;
            if (DateTime.UtcNow > expiresAt)
            {
                await ClearAsync();
                return null;
            }

            return session;
        }
        catch (Exception ex)
        {
            _logger.Warn($"读取登录会话失败: {ex.Message}");
            return null;
        }
    }

    public Task ClearAsync()
    {
        try
        {
            SecureStorage.Remove(SessionKey);
        }
        catch (Exception ex)
        {
            _logger.Warn($"清除登录会话失败: {ex.Message}");
        }

        return Task.CompletedTask;
    }
}
