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
            if (session == null || session.UserId <= 0)
            {
                await ClearAsync();
                return null;
            }

            if (DateTime.UtcNow - session.LoginAtUtc > SessionLifetime)
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
