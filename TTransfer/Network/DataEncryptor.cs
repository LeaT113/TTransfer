using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.IO;
using System.Security;
using System.Net;

namespace TTransfer.Network
{
    public class DataEncryptor
    {
        // Static
        static byte[] saltBytes = new byte[] { 39, 28, 5, 104, 66, 15, 152, 184 };
        const int aesLengthX = 16;

        // Internal
        RijndaelManaged aes;
        ICryptoTransform aesEncryptor;
        ICryptoTransform aesDecryptor;



        public DataEncryptor(SecureString password)
        {
            aes = new RijndaelManaged();

            aes.KeySize = 256;
            aes.BlockSize = 128;
            byte[] passwordBytes = Encoding.UTF8.GetBytes(new NetworkCredential("", password).Password);

            var key = new Rfc2898DeriveBytes(passwordBytes, saltBytes, 1000);
            aes.Key = key.GetBytes(aes.KeySize / 8);
            aes.IV = key.GetBytes(aes.BlockSize / 8);

            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            aesEncryptor = aes.CreateEncryptor();
            aesDecryptor = aes.CreateDecryptor();
        }



        // Encryption
        public byte[] AESEncryptBytes(byte[] bytesToBeEncrypted)
        {
            byte[] encryptedBytes = null;

            using(MemoryStream ms = new MemoryStream())
            {
                using (var cs = new CryptoStream(ms, aesEncryptor, CryptoStreamMode.Write))
                {
                    cs.Write(bytesToBeEncrypted, 0, bytesToBeEncrypted.Length);
                    cs.Close();
                }
                encryptedBytes = ms.ToArray();
            }
            
            return encryptedBytes;
        }
        public byte[] AESDecryptBytes(byte[] bytesToBeDecrypted)
        {
            byte[] decryptedBytes = null;

            using (MemoryStream ms = new MemoryStream())
            {
                using (var cs = new CryptoStream(ms, aesDecryptor, CryptoStreamMode.Write))
                {
                    cs.Write(bytesToBeDecrypted, 0, bytesToBeDecrypted.Length);
                    cs.Close();
                }
                decryptedBytes = ms.ToArray();
            }

            return decryptedBytes;
        }


        public static int PredictAESLength(int dataLength)
        {
            return (int)Math.Floor((double)dataLength / (double)aesLengthX + 1) * aesLengthX;
        }


        public void Dispose()
        {
            aesEncryptor.Dispose();
            aesDecryptor.Dispose();
            aes.Dispose();
        }
    }
}
