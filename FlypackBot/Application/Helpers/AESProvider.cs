using System.Security.Cryptography;

namespace FlypackBot.Application.Helpers
{
    internal static class AESProvider
    {
        private const int KEY_SIZE = 256;
        private const int BLOCK_SIZE = 128;
        private const int ITERATIONS = 12000;

        internal static Aes GetProvider(byte[] password, byte[] salt)
        {
            var aes = Aes.Create();
            aes.KeySize = KEY_SIZE;
            aes.BlockSize = BLOCK_SIZE;

            var key = new Rfc2898DeriveBytes(password, salt, ITERATIONS);
            aes.Key = key.GetBytes(aes.KeySize / 8);
            aes.IV = key.GetBytes(aes.BlockSize / 8);
            aes.Padding = PaddingMode.PKCS7;
            aes.Mode = CipherMode.CBC;

            return aes;
        }
    }
}
