using System.IO;
using System.Security.Cryptography;

namespace SecuCodeApp.Native
{
    /// <summary>
    /// Alternative AES implementation using `System.Security.Cryptography` instead of TinyCrypt.
    /// 
    /// This is primarily used for testing.
    /// </summary>
    static class Aes
    {
        public static byte[] EncryptBytes(byte[] bytes, byte[] key, CipherMode cipherMode = CipherMode.CBC)
        {
            var iv = new byte[16] {
                0xAA, 0x00, 0xAA, 0x00,
                0xAA, 0x00, 0xAA, 0x00,
                0xAA, 0x00, 0xAA, 0x00,
                0xAA, 0x00, 0xAA, 0x00
            };

            using var algorithm = new RijndaelManaged
            {
                BlockSize = 128,
                KeySize = 128,
                Padding = PaddingMode.Zeros,
                Mode = cipherMode
            };

            using var memoryStream = new MemoryStream();

            var encryptor = algorithm.CreateEncryptor(key, iv);
            using var cryptoStream = new CryptoStream(memoryStream, encryptor, CryptoStreamMode.Write);
            cryptoStream.Write(bytes, 0, bytes.Length);
            cryptoStream.Flush();
            
            return memoryStream.ToArray();
        }

        public static byte[] DecryptBytes(byte[] bytes, byte[] key, CipherMode cipherMode = CipherMode.CBC)
        {
            var iv = new byte[16] {
                0xAA, 0x00, 0xAA, 0x00,
                0xAA, 0x00, 0xAA, 0x00,
                0xAA, 0x00, 0xAA, 0x00,
                0xAA, 0x00, 0xAA, 0x00
            };

            using var algorithm = new RijndaelManaged
            {
                BlockSize = 128,
                KeySize = 128,
                Padding = PaddingMode.Zeros,
                Mode = cipherMode
            };
            
            using var memoryStream = new MemoryStream();
            
            var decryptor = algorithm.CreateDecryptor(key, iv);
            using var cryptoStream = new CryptoStream(memoryStream, decryptor, CryptoStreamMode.Write);
            cryptoStream.Write(bytes, 0, bytes.Length);
            cryptoStream.Flush();
            
            return memoryStream.ToArray();
        }
    }
}
