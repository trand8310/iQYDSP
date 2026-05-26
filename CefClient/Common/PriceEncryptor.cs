using System.Security.Cryptography;
using System.Text;

namespace CefClient.Common;

public static class PriceEncryptor
{
    private const int IvLength = 16;
    private const int SignatureLength = 4;
    private const int TokenLength = 32;

    /// <summary>
    /// 加密价格
    /// </summary>
    /// <param name="price">价格明文，例如 "100"</param>
    /// <param name="initializeVector">
    /// 初始化向量，至少 16 字节。
    /// 如果超过 16 字节，会截断前 16 字节。
    /// 每次请求建议不同。
    /// </param>
    /// <param name="encryptionToken">加密 token，必须 32 字节</param>
    /// <param name="integrityToken">完整性 token，必须 32 字节</param>
    /// <param name="output">输出 WebSafeBase64 密文</param>
    /// <returns>成功 true，失败 false</returns>
    public static bool EncryptPrice(
        string price,
        string initializeVector,
        string encryptionToken,
        string integrityToken,
        out string output)
    {
        output = string.Empty;

        if (price == null ||
            initializeVector == null ||
            encryptionToken == null ||
            integrityToken == null)
        {
            return false;
        }

        // 注意：这里按 UTF-8 字节长度判断，更适合 .NET。
        // 如果你的 token/iv 全是 ASCII 字符，则和 C++ string.size() 一致。
        byte[] ivAllBytes = Encoding.UTF8.GetBytes(initializeVector);
        byte[] encryptionTokenBytes = Encoding.UTF8.GetBytes(encryptionToken);
        byte[] integrityTokenBytes = Encoding.UTF8.GetBytes(integrityToken);

        if (ivAllBytes.Length < IvLength ||
            encryptionTokenBytes.Length != TokenLength ||
            integrityTokenBytes.Length != TokenLength)
        {
            return false;
        }

        // initialize_vector 长度若超过 16 字节，截断
        byte[] ivBytes = new byte[IvLength];
        Buffer.BlockCopy(ivAllBytes, 0, ivBytes, 0, IvLength);

        // 使用 iv、encryption_token 计算出加密用的 padding
        byte[] padding = GenerateHmacSha1(encryptionTokenBytes, ivBytes);
        if (padding.Length == 0)
        {
            return false;
        }

        byte[] priceBytes = Encoding.UTF8.GetBytes(price);

        // 循环异或得到加密之后的价格
        byte[] encPriceBytes = new byte[priceBytes.Length];
        for (int i = 0; i < priceBytes.Length; i++)
        {
            encPriceBytes[i] = (byte)(priceBytes[i] ^ padding[i % padding.Length]);
        }

        // 生成价格明文及 initialize_vector 的摘要
        byte[] signature = GenerateHmacSha1(
            integrityTokenBytes,
            priceBytes,
            ivBytes
        );

        if (signature.Length < SignatureLength)
        {
            return false;
        }

        // 最终完整密文：iv || enc_price || sign[0..4]
        byte[] finalBytes = new byte[ivBytes.Length + encPriceBytes.Length + SignatureLength];

        int offset = 0;

        Buffer.BlockCopy(ivBytes, 0, finalBytes, offset, ivBytes.Length);
        offset += ivBytes.Length;

        Buffer.BlockCopy(encPriceBytes, 0, finalBytes, offset, encPriceBytes.Length);
        offset += encPriceBytes.Length;

        Buffer.BlockCopy(signature, 0, finalBytes, offset, SignatureLength);

        // WebSafeBase64 编码
        output = WebSafeBase64Encode(finalBytes);
        return true;
    }

    /// <summary>
    /// HMAC-SHA1，支持多段内容连续 Update，等价于 C++ 里多次 HMAC_Update。
    /// </summary>
    private static byte[] GenerateHmacSha1(byte[] key, params byte[][] contentList)
    {
        using var hmac = new HMACSHA1(key);

        int totalLength = 0;
        foreach (byte[] item in contentList)
        {
            if (item != null)
            {
                totalLength += item.Length;
            }
        }

        byte[] allContent = new byte[totalLength];

        int offset = 0;
        foreach (byte[] item in contentList)
        {
            if (item == null || item.Length == 0)
            {
                continue;
            }

            Buffer.BlockCopy(item, 0, allContent, offset, item.Length);
            offset += item.Length;
        }

        return hmac.ComputeHash(allContent);
    }

    /// <summary>
    /// 标准 Base64 转 WebSafeBase64：
    /// 去掉末尾 '='，'+' 替换成 '-'，'/' 替换成 '_'
    /// </summary>
    private static string WebSafeBase64Encode(byte[] input)
    {
        string output = Convert.ToBase64String(input);

        output = output.TrimEnd('=')
                       .Replace('+', '-')
                       .Replace('/', '_');

        return output;
    }
}


