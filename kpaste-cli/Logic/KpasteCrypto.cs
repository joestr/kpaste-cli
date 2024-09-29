﻿using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using System.IO.Compression;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using Org.BouncyCastle.Utilities.Encoders;
using SimpleBase;

namespace kpaste_cli.Logic
{
    public class KPasteCrypto
    {
        // WHO THE HELL???
        private static string PseudoBase58 = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

        private static int iterationCount = 100000;
        private static int keySize = 256;
        private static int tagSize = 128;

        private string key;
        private string vector;
        private string salt;

        public class KpasteEncryptionResultDto
        {
            public string Key { get; set; }
            public string Vector { get; set; }
            public string Salt { get; set; }
            public string Message { get; set; }
        }

        public KPasteCrypto(string key, string vector, string salt)
        {
            this.key = FromPseudoBase58(key);
            this.vector = Encoding.Unicode.GetString(Convert.FromBase64String(vector));
            this.salt = Encoding.Unicode.GetString(Convert.FromBase64String(salt));
        }
        
        public KPasteCrypto(string key, string vector, string salt, bool crypt)
        {
            if (key == null)
            {
                this.key = GetRandomBytes(32);
            }
            else
            {
                this.key = key;
            }

            if (vector == null)
            {
                this.vector = GetRandomBytes(16);
            }
            else
            {
                this.vector = vector;
            }

            if (salt == null)
            {
                this.salt = GetRandomBytes(8);
            }
            else
            {
                this.salt = salt;
            }
        }

        public KpasteEncryptionResultDto Encrypt(string plainText, string password)
        {
            var derivedKey = DeriveKey(password);
            var message = Aes256GcmEncrypt(plainText, derivedKey);

            var result = new KpasteEncryptionResultDto()
            {
                Key = ToPseudoBase58(this.key),
                Vector = Convert.ToBase64String(Encoding.Unicode.GetBytes(this.vector)),
                Salt = Convert.ToBase64String(Encoding.Unicode.GetBytes(this.salt)),
                Message = message
            };
            return result;
        }

        public string Decrypt(string cipherText, string password)
        {
            var derivedKey = DeriveKey(password);
            var message = Aes256GcmDecrypt(cipherText, derivedKey);

            return message;
        }

        private string Aes256GcmEncrypt(string plainText, byte[] derivedKey)
        {
            var plainTextBytes = StringToArrayBuffer(Utf16ToUtf8(plainText));
            var plainTextBytesMemoryStream = new MemoryStream(plainTextBytes);

            var compressedPlainTextByesMemoryStream = new MemoryStream(plainTextBytes.Length); //set to estimate of compression ratio
            using (GZipStream gZipStream = new GZipStream(compressedPlainTextByesMemoryStream, CompressionMode.Compress))
            {
                plainTextBytesMemoryStream.CopyTo(gZipStream);
            }

            var compressedPlainTextBytes = compressedPlainTextByesMemoryStream.ToArray();
            byte[] encryptedBytes;

            //var aes256Gcm = new AesGcm(derivedKey, tagSize);
            //aes256Gcm.Encrypt(this.vector, compressedStream.ToArray(), encryptedBytes, tagBytes);

            IBlockCipher cipher = new AesEngine();
            KeyParameter keyParam = new KeyParameter(derivedKey);
            AeadParameters keyParamAead = new AeadParameters(keyParam, tagSize, StringToArrayBuffer(this.vector), new byte[0]);
            GcmBlockCipher cipherMode = new GcmBlockCipher(cipher);
            cipherMode.Init(true, keyParamAead);
            int outputSize = cipherMode.GetOutputSize(compressedPlainTextBytes.Length);
            byte[] cipherTextData = new byte[outputSize];
            int result = cipherMode.ProcessBytes(compressedPlainTextBytes, 0, compressedPlainTextBytes.Length, cipherTextData, 0);
            cipherMode.DoFinal(cipherTextData, result);
            encryptedBytes = cipherTextData;

            var resultString = Convert.ToBase64String(encryptedBytes);

            return resultString;
        }

        private string Aes256GcmDecrypt(string cipherText, byte[] derivedKey)
        {
            var cipherTextBytes = Convert.FromBase64String(cipherText);
            byte[] compressedPlainTextBytes;

            //var aes256Gcm = new AesGcm(derivedKey, tagSize);
            //aes256Gcm.Decrypt(this.vector, cipherBytes, tagBytes, unencryptedBytes);

            IBlockCipher cipher = new AesEngine();
            KeyParameter keyParam = new KeyParameter(derivedKey);
            AeadParameters keyParamAead = new AeadParameters(keyParam, tagSize, StringToArrayBuffer(this.vector), new byte[0]);
            GcmBlockCipher cipherMode = new GcmBlockCipher(cipher);
            cipherMode.Init(false, keyParamAead);
            var outputSize = cipherMode.GetOutputSize(cipherTextBytes.Length);
            var plainTextData = new byte[outputSize];
            var result = cipherMode.ProcessBytes(cipherTextBytes, 0, cipherTextBytes.Length, plainTextData, 0);
            cipherMode.DoFinal(plainTextData, result);
            compressedPlainTextBytes = plainTextData;

            var compressedPlainTextBytesMemoryStream = new MemoryStream(compressedPlainTextBytes);
            var plainTextBytesMemoryStream = new MemoryStream(compressedPlainTextBytesMemoryStream.Capacity * 2 + 1024);

            using (GZipStream gZipStream = new GZipStream(compressedPlainTextBytesMemoryStream, CompressionMode.Decompress))
            {
                gZipStream.CopyTo(plainTextBytesMemoryStream);
            }

            var plainTextString = Utf8ToUtf16(Encoding.UTF8.GetString(plainTextBytesMemoryStream.ToArray()));

            return plainTextString;
        }

        private byte[] DeriveKey(string password)
        {
            byte[] newKeyBytes;
            if (password.Length > 0)
            {
                var passwordBytes = StringToArrayBuffer(password);
                var keyBytes = StringToArrayBuffer(this.key);
                newKeyBytes = new byte[keyBytes.Length + passwordBytes.Length];
                keyBytes.CopyTo(newKeyBytes, 0);
                passwordBytes.CopyTo(newKeyBytes, keyBytes.Length);
            }
            else
            {
                var keyBytes = StringToArrayBuffer(this.key);
                newKeyBytes = keyBytes;
            }

            var saltBytes = StringToArrayBuffer(this.salt);

            return Rfc2898DeriveBytes.Pbkdf2(newKeyBytes, saltBytes, iterationCount, HashAlgorithmName.SHA256,
                keySize/8);
        }

        private string ToPseudoBase58(string input)
        {
            return Base58.Bitcoin.Encode(StringToArrayBuffer(input));
        }

        private string FromPseudoBase58(string input)
        {
            return ArrayBufferToString(Base58.Bitcoin.Decode(input));
        }

        public static string ToBaseX(BigInteger number, string baseX)
        {
            int l = baseX.Length;
            string result = "";
            while (number > 0)
            {
                BigInteger remainder = number % l;
                int index = (int)remainder;
                if (index >= l)
                {
                    throw new ArgumentException($"Cannot convert {number} ToBaseX {baseX}");
                }
                result += baseX[index];
                number /= l;
            }
            return result;
        }

        public static BigInteger FromBaseX(string input, string baseX)
        {
            int l = baseX.Length;
            BigInteger result = -1;
            int pow = 0;
            foreach (char c in input)
            {
                int index = baseX.IndexOf(c);
                if (index < 0)
                {
                    throw new ArgumentException($"Cannot convert {input} FromBaseX {baseX}");
                }
                BigInteger additions = BigInteger.Pow(l, pow) * index;
                result += additions;
                pow++;
            }
            return result;
        }
        
        private string ArrayBufferToString(byte[] messageArray)
        {
            var message = "";
            for (var i = 0; i < messageArray.Length; i += 1) {
                message += (char)messageArray[i];
            }
            return message;
        }

        private byte[] StringToArrayBuffer(string message) {
            byte[] messageArray = new byte[message.Length];
            for (var i = 0; i < message.Length; i += 1) {
                messageArray[i] = (byte)message[i];
            }
            return messageArray;
        }

        private string Utf16ToUtf8(string message)
        {
            return message;
            //return Encoding.UTF8.GetString(Encoding.Unicode.GetBytes(message));
        }

        private string Utf8ToUtf16(string message)
        {
            return message;
            //return Encoding.Unicode.GetString(Encoding.UTF8.GetBytes(message));
        }

        private string GetRandomBytes(int length)
        {
            var result = "";

            var bytes = new byte[length];
            Random.Shared.NextBytes(bytes);

            for (var i = 0; i < bytes.Length; i += 1)
            {
                result += (char)bytes[i];
            }

            return result;
        }
    }
}
