using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using FlypackBot.Application.Extensions;
using FlypackBot.Application.Helpers;
using FlypackBot.Application.Models;
using Microsoft.Extensions.Options;
using FlypackSettings = FlypackBot.Settings.Flypack;

namespace FlypackBot.Application.Services
{
    public class PasswordEncrypterService
    {
        private readonly string privateKey;

        public PasswordEncrypterService(IOptions<FlypackSettings> settings)
            => privateKey = settings.Value.PrivateKey;

        public SaltAndPassword Encrypt(string password) => Encrypt(password, privateKey);

        private SaltAndPassword Encrypt(string plainPassword, string secretKey)
        {
            var key = PrivateKey.GetKey().Merge(Encoding.ASCII.GetBytes(secretKey));
            var salt = GenerateRandomSalt();

            var bytes = Encoding.UTF8.GetBytes(plainPassword).Merge(salt);

            byte[] encrypted;

            using (var provider = AESProvider.GetProvider(key, salt))
            using (var encriptor = provider.CreateEncryptor())
            using (MemoryStream output = new MemoryStream())
            using (CryptoStream writer = new CryptoStream(output, encriptor, CryptoStreamMode.Write))
            {
                writer.Write(bytes);
                writer.FlushFinalBlock();
                encrypted = output.ToArray();
            }

            return new SaltAndPassword
            {
                Password = Convert.ToBase64String(encrypted),
                Salt = Convert.ToBase64String(salt)
            };
        }

        private static byte[] GenerateRandomSalt()
        {
            var salt = new byte[16];
            using (var provider = RandomNumberGenerator.Create())
                provider.GetBytes(salt);

            return salt;
        }
    }
}
