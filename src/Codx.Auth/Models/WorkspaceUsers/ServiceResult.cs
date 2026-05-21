namespace Codx.Auth.Models.WorkspaceUsers
{
    /// <summary>
    /// Generic discriminated-union result used by workspace management service methods.
    /// Use the static factory methods; check <see cref="Status"/> to determine outcome.
    /// </summary>
    public sealed class ServiceResult<T>
    {
        public enum ResultStatus
        {
            Success,
            NotFound,
            Forbidden,
            Conflict,
            BadRequest
        }

        public ResultStatus Status { get; private init; }

        /// <summary>Populated on Success.</summary>
        public T? Data { get; private init; }

        /// <summary>Error code on Forbidden, Conflict, or BadRequest.</summary>
        public string? ErrorCode { get; private init; }

        public static ServiceResult<T> Success(T data) =>
            new() { Status = ResultStatus.Success, Data = data };

        public static ServiceResult<T> NotFound() =>
            new() { Status = ResultStatus.NotFound };

        public static ServiceResult<T> Forbidden(string errorCode) =>
            new() { Status = ResultStatus.Forbidden, ErrorCode = errorCode };

        public static ServiceResult<T> Conflict(string errorCode) =>
            new() { Status = ResultStatus.Conflict, ErrorCode = errorCode };

        public static ServiceResult<T> BadRequest(string errorCode) =>
            new() { Status = ResultStatus.BadRequest, ErrorCode = errorCode };
    }
}
