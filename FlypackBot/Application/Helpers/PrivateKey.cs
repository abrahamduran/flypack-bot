using System;
using System.IO;
using System.Security.Cryptography;

namespace FlypackBot.Application.Helpers
{
    internal static class PrivateKey
    {
        private const string KEY_FILE_NAME = ".flypack.key";
        private const int KEY_SIZE = 128;

        internal static byte[] GetKey()
        {
            var path = $"{AppContext.BaseDirectory}{Path.DirectorySeparatorChar}{KEY_FILE_NAME}";
            if (File.Exists(path))
                return File.ReadAllBytes(path);

            var key = GeneratePrivateKey();
            File.WriteAllBytes(path, key);
            File.SetAttributes(path, FileAttributes.Hidden);
            File.SetAttributes(path, FileAttributes.ReadOnly);
            return key;
        }

        private static byte[] GeneratePrivateKey()
        {
            var key = new byte[KEY_SIZE];
            using (var provider = new RNGCryptoServiceProvider())
                provider.GetBytes(key);

            return key;
        }
    }
}
