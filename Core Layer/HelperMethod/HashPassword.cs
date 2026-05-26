using System;
using System.Security.Cryptography;
using System.Text;

namespace Core_Layer.HelperMethod
{
    public static class HashPassword
    {
        private const int SaltSize = 16;     // 128 bits
        private const int HashSize = 32;     // 256 bits
        private const int Iterations = 100000; // 100k iterations

        public static string Hash(string plainPassword)
        {
            if (string.IsNullOrWhiteSpace(plainPassword))
                throw new ArgumentNullException(nameof(plainPassword));

            byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);

            using var pbkdf2 = new Rfc2898DeriveBytes(plainPassword, salt, Iterations, HashAlgorithmName.SHA256);
            byte[] hash = pbkdf2.GetBytes(HashSize);

            byte[] hashBytes = new byte[SaltSize + HashSize];
            Array.Copy(salt, 0, hashBytes, 0, SaltSize);
            Array.Copy(hash, 0, hashBytes, SaltSize, HashSize);

            return Convert.ToBase64String(hashBytes);
        }

        public static bool Verify(string plainPassword, string storedHash)
        {
            if (string.IsNullOrWhiteSpace(plainPassword) || string.IsNullOrWhiteSpace(storedHash))
                return false;

            byte[] hashBytes = Convert.FromBase64String(storedHash);
            if (hashBytes.Length != SaltSize + HashSize)
                return false;

            byte[] salt = new byte[SaltSize];
            byte[] stored = new byte[HashSize];
            Array.Copy(hashBytes, 0, salt, 0, SaltSize);
            Array.Copy(hashBytes, SaltSize, stored, 0, HashSize);

            using var pbkdf2 = new Rfc2898DeriveBytes(plainPassword, salt, Iterations, HashAlgorithmName.SHA256);
            byte[] computed = pbkdf2.GetBytes(HashSize);

            return CryptographicOperations.FixedTimeEquals(computed, stored);
        }
    }
}