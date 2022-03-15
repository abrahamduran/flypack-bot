using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using FlypackBot.Application.Extensions;
using FlypackBot.Application.Helpers;
using Microsoft.Extensions.Options;
using FlypackSettings = FlypackBot.Settings.Flypack;

namespace FlypackBot.Application.Services
{
    public class PasswordDecrypterService
    {
        private readonly string privateKey;

        public PasswordDecrypterService(IOptions<FlypackSettings> settings)
            => privateKey = settings.Value.PrivateKey;

        public string Decrypt(string password, string salt) => Decrypt(password, salt, privateKey);

        private string Decrypt(string encryptedPassword, string salt, string secretKey)
        {
            var key = PrivateKey.GetKey().Merge(Encoding.ASCII.GetBytes(secretKey));

            var saltBytes = Convert.FromBase64String(salt);
            var passwordBytes = Convert.FromBase64String(encryptedPassword);

            byte[] decrypted;

            using (var provider = AESProvider.GetProvider(key, saltBytes))
            using (var decryptor = provider.CreateDecryptor())
            using (MemoryStream input = new MemoryStream(passwordBytes))
            using (CryptoStream reader = new CryptoStream(input, decryptor, CryptoStreamMode.Read))
            using (MemoryStream output = new MemoryStream())
            {
                byte[] buffer = new byte[8];
                int length;
                while ((length = reader.Read(buffer, 0, buffer.Length)) > 0)
                    output.Write(buffer, 0, length);

                decrypted = output.ToArray().SkipLast(saltBytes.Length).ToArray();
            }

            return Encoding.UTF8.GetString(decrypted);
        }
    }
}
