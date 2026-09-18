using System.Runtime.CompilerServices;
using NewCosmos.Constants;

namespace NewCosmos.Models.Results;

/// <summary>
/// 操作结果基类 - 不包含返回    /// </summary>
public class Result
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public string ErrorCode { get; } = string.Empty;
    public string Message { get; } = string.Empty;
    public IReadOnlyList<string> Errors { get; }

    protected Result(bool isSuccess, string errorCode, string message, IReadOnlyList<string> errors)
    {
        IsSuccess = isSuccess;
        ErrorCode = errorCode;
        Message = message;
        Errors = errors ?? Array.Empty<string>();
    }

    /// <summary>
    /// 创建成功结果
    /// </summary>
    public static Result Success() => new(true, null, null, null);

    /// <summary>
    /// 创建失败结果
    /// </summary>
    public static Result Failure(string errorCode, string message, params string[] errors) =>
        new(false, errorCode, message, errors);

    /// <summary>
    /// 创建带值的成功结果
    /// </summary>
    public static Result<T> Success<T>(T value) => Result<T>.Success(value);

    /// <summary>
    /// 创建带值的失败结果
    /// </summary>
    public static Result<T> Failure<T>(string errorCode, string message, params string[] errors) =>
        Result<T>.Failure(errorCode, message, errors);

    /// <summary>
    /// 从异常创建失败结    /// </summary>
    public static Result FromException(Exception ex, string errorCode = null)
    {
        var code = errorCode ?? GetErrorCodeFromException(ex);
        return Failure(code, ex.Message);
    }

    /// <summary>
    /// 从异常创建失败结    /// </summary>
    public static Result<T> FromException<T>(Exception ex, string errorCode = null)
    {
        var code = errorCode ?? GetErrorCodeFromException(ex);
        return Failure<T>(code, ex.Message);
    }

    private static string GetErrorCodeFromException(Exception ex)
    {
        if (ex.GetType().Name == "NpgsqlException")
        {
            var errorCodeProp = ex.GetType().GetProperty("ErrorCode");
            if (errorCodeProp != null)
            {
                var errorCode = errorCodeProp.GetValue(ex)?.ToString();
                return errorCode switch
                {
                    "08001" or "08006" => ErrorCodes.DB_CONNECTION_FAILED,
                    "57014" => ErrorCodes.DB_TIMEOUT,
                    "23505" => ErrorCodes.DB_UNIQUE_VIOLATION,
                    "23503" => ErrorCodes.DB_FOREIGN_KEY_VIOLATION,
                    _ => ErrorCodes.DB_QUERY_ERROR
                };
            }
        }

        return ex switch
        {
            ValidationException => ErrorCodes.VALIDATION_FAILED,
            BusinessException be => be.ErrorCode,
            _ => ErrorCodes.UNKNOWN_ERROR
        };
    }
}

/// <summary>
/// 操作结果泛型- 包含返回    /// </summary>
public class Result<T> : Result
{
    public T Value { get; }

    private Result(bool isSuccess, T value, string errorCode, string message, IReadOnlyList<string> errors)
        : base(isSuccess, errorCode, message, errors)
    {
        Value = value;
    }

    /// <summary>
    /// 创建成功结果
    /// </summary>
    public static Result<T> Success(T value) => new(true, value, null, null, null);

    /// <summary>
    /// 创建失败结果
    /// </summary>
    public new static Result<T> Failure(string errorCode, string message, params string[] errors) =>
        new(false, default!, errorCode, message, errors);

    /// <summary>
    /// 映射到新类型
    /// </summary>
    public Result<TNew> Map<TNew>(Func<T, TNew> mapper)
    {
        return IsSuccess && Value is not null
            ? Success(mapper(Value))
            : Failure<TNew>(ErrorCode!, Message!, Errors.ToArray());
    }

    /// <summary>
    /// 异步映射到新类型
    /// </summary>
    public async Task<Result<TNew>> MapAsync<TNew>(Func<T, Task<TNew>> mapper)
    {
        return IsSuccess && Value is not null
            ? Success(await mapper(Value))
            : Failure<TNew>(ErrorCode!, Message!, Errors.ToArray());
    }

    /// <summary>
    /// 获取值或默认    /// </summary>
    public T? GetValueOrDefault(T? defaultValue = default) => IsSuccess ? Value : defaultValue;

    /// <summary>
    /// 获取值或抛出异常
    /// </summary>
    public T GetValueOrThrow()
    {
        if (IsSuccess && Value is not null)
            return Value;
        throw new BusinessException(ErrorCode ?? ErrorCodes.UNKNOWN_ERROR, Message ?? "未知错误");
    }
}

/// <summary>
/// 验证异常
/// </summary>
public class ValidationException : Exception
{
    public string FieldName { get; } = string.Empty;
    public object? AttemptedValue { get; }

    public ValidationException(string message) : base(message) { }

    public ValidationException(string fieldName, string message, object? attemptedValue = null)
        : base($"{fieldName}: {message}")
    {
        FieldName = fieldName;
        AttemptedValue = attemptedValue;
    }
}

/// <summary>
/// 业务异常
/// </summary>
public class BusinessException : Exception
{
    public string ErrorCode { get; }
    public Dictionary<string, object> Context { get; }

    public BusinessException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
        Context = new Dictionary<string, object>();
    }

    public BusinessException(string errorCode, string message, Exception innerException)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        Context = new Dictionary<string, object>();
    }

    /// <summary>
    /// 添加上下文信息（支持链式调用    /// </summary>
    public BusinessException AddContext(string key, object value)
    {
        Context[key] = value;
        return this;
    }
}
