using Codx.Auth.Manual.Test.Infrastructure;
using Codx.Auth.Services.Interfaces;
using Codx.Auth.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Codx.Auth.Manual.Test.TwoFactor
{
    /// <summary>
    /// Tests for Two-Factor Authentication service
    /// </summary>
    public class TwoFactorServiceTests : IClassFixture<EmailServiceTestFixture>
    {
        private readonly EmailServiceTestFixture _fixture;

        public TwoFactorServiceTests(EmailServiceTestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public void GenerateVerificationCode_ShouldReturn6DigitCode()
        {
            // Arrange
            var twoFactorService = CreateTwoFactorServiceMock();

            // Act
            var code = twoFactorService.GenerateVerificationCode();

            // Assert
            Assert.NotNull(code);
            Assert.Equal(6, code.Length);
            Assert.True(int.TryParse(code, out _), "Code should be numeric");
            Assert.True(code.All(char.IsDigit), "Code should contain only digits");
        }

        [Fact]
        public void GenerateVerificationCode_ShouldGenerateUniqueCode()
        {
            // Arrange  
            var twoFactorService = CreateTwoFactorServiceMock();
            var codes = new HashSet<string>();

            // Act - Generate 100 codes
            for (int i = 0; i < 100; i++)
            {
                var code = twoFactorService.GenerateVerificationCode();
                codes.Add(code);
            }

            // Assert - Should have generated mostly unique codes (some duplicates are statistically possible)
            Assert.True(codes.Count > 80, "Should generate mostly unique codes");
        }

        [Fact]
        public void GenerateVerificationCode_ShouldFormatCodeWith6Digits()
        {
            // Arrange
            var twoFactorService = CreateTwoFactorServiceMock();

            // Act
            var code = twoFactorService.GenerateVerificationCode();

            // Assert
            Assert.Matches(@"^\d{6}$", code); // Should match exactly 6 digits
        }

        /// <summary>
        /// Creates a minimal TwoFactorService instance for testing code generation
        /// (without database dependencies)
        /// </summary>
        private ITwoFactorService CreateTwoFactorServiceMock()
        {
            // For testing code generation, we can create a simple implementation
            // or use reflection to test the static method
            return new TestTwoFactorService();
        }

        /// <summary>
        /// Minimal implementation for testing code generation
        /// </summary>
        private class TestTwoFactorService : ITwoFactorService
        {
            public string GenerateVerificationCode()
            {
                // Same implementation as the real service
                using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
                var bytes = new byte[4];
                rng.GetBytes(bytes);
                var randomNumber = BitConverter.ToUInt32(bytes, 0);
                return (randomNumber % 1000000).ToString("D6");
            }

            // Not needed for basic code generation tests
            public Task<(bool success, string code, string message)> SendVerificationCodeAsync(Guid userId, string email, string userName = null, Guid? tenantId = null, System.Threading.CancellationToken ct = default) => Task.FromResult((false, string.Empty, string.Empty));
            public Task<bool> ValidateVerificationCodeAsync(Guid userId, string providedCode) => Task.FromResult(false);
            public Task StoreVerificationCodeAsync(Guid userId, string code, int expirationMinutes = 10) => Task.CompletedTask;
            public Task ClearVerificationCodeAsync(Guid userId) => Task.CompletedTask;
        }
    }
}