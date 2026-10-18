using System;
using System.Security.Cryptography;

namespace HomeBudget.Accounting.Infrastructure.DataProtection
{
    internal static class DataProtectionConfirmationToken
    {
        public static string Create()
        {
            return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }
    }
}
