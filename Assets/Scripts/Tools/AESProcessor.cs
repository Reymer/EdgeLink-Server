using System;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;

public class AESProcessor
{
    private byte[] aesKey;
    private byte[] aesIV;

    public AESProcessor()
    {
        GenerateRandomKeyAndIV();
    }

    private void GenerateRandomKeyAndIV()
    {
        using Aes aes = Aes.Create();
        aes.GenerateKey();
        aes.GenerateIV();
        aesKey = aes.Key;
        aesIV = aes.IV;
    }

    public string EncryptWithAES(string plaintext)
    {
        try
        {
            using Aes aes = Aes.Create();
            aes.Key = aesKey;
            aes.IV = aesIV;

            ICryptoTransform encryptor = aes.CreateEncryptor(aes.Key, aes.IV);

            using MemoryStream msEncrypt = new MemoryStream();
            using (CryptoStream csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
            using (StreamWriter swEncrypt = new StreamWriter(csEncrypt))
            {
                swEncrypt.Write(plaintext);
            }
            return Convert.ToBase64String(msEncrypt.ToArray());
        }
        catch (Exception ex)
        {
            Debug.LogError($"AES 加密失敗：{ex.Message}");
            return string.Empty;
        }
    }

    public string DecryptWithAES(string ciphertext)
    {
        try
        {
            using Aes aes = Aes.Create();
            aes.Key = aesKey;
            aes.IV = aesIV;

            ICryptoTransform decryptor = aes.CreateDecryptor(aes.Key, aes.IV);

            using MemoryStream msDecrypt = new MemoryStream(Convert.FromBase64String(ciphertext));
            using CryptoStream csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read);
            using StreamReader srDecrypt = new StreamReader(csDecrypt);
            return srDecrypt.ReadToEnd();
        }
        catch (Exception ex)
        {
            Debug.LogError($"AES 解密失敗：{ex.Message}");
            return string.Empty;
        }
    }
}
