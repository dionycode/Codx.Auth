using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;

namespace Codx.Auth.Infrastructure.Identity
{
    public class PasswordResetTokenProvider<TUser> : DataProtectorTokenProvider<TUser>
        where TUser : class
    {
        public PasswordResetTokenProvider(
            IDataProtectionProvider dataProtectionProvider,
            IOptions<PasswordResetTokenProviderOptions> options,
            ILogger<DataProtectorTokenProvider<TUser>> logger)
            : base(dataProtectionProvider, options, logger) { }
    }

    public class PasswordResetTokenProviderOptions : DataProtectionTokenProviderOptions
    {
        public const string ProviderName = "PasswordReset";

        public PasswordResetTokenProviderOptions() =>
            TokenLifespan = TimeSpan.FromHours(1);
    }
}
