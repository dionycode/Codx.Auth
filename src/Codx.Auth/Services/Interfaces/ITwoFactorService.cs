using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Codx.Auth.Services.Interfaces
{
    /// <summary>
    /// Service for handling Two-Factor Authentication operations
    /// </summary>
    public interface ITwoFactorService
    {
        /// <summary>
        /// Generate a 6-digit verification code
        /// </summary>
        /// <returns>6-digit numeric code</returns>
        string GenerateVerificationCode();

        /// <summary>
        /// Generate and send 2FA verification code via email
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <param name="email">User email address</param>
        /// <param name="userName">User name for personalization</param>
        /// <param name="tenantId">Optional tenant ID for template resolution</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Success result and code (for testing purposes)</returns>
        Task<(bool success, string code, string message)> SendVerificationCodeAsync(Guid userId, string email, string userName = null, Guid? tenantId = null, CancellationToken ct = default);

        /// <summary>
        /// Verify the provided code against the stored code for the user
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <param name="providedCode">Code provided by the user</param>
        /// <returns>True if code is valid and not expired</returns>
        Task<bool> ValidateVerificationCodeAsync(Guid userId, string providedCode);

        /// <summary>
        /// Store verification code for user with expiration
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <param name="code">Verification code</param>
        /// <param name="expirationMinutes">Expiration time in minutes (default 10)</param>
        /// <returns>Task</returns>
        Task StoreVerificationCodeAsync(Guid userId, string code, int expirationMinutes = 10);

        /// <summary>
        /// Clear any existing verification codes for the user
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <returns>Task</returns>
        Task ClearVerificationCodeAsync(Guid userId);
    }
}