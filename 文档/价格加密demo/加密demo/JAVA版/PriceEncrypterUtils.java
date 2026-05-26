//package com.xxx.util;
/**
 * 价格加密
 * 需要引入依赖  implementation 'commons-codec:commons-codec:1.14'
 * @author iqiyi
 * @version 1.0
 */

import org.apache.commons.codec.binary.Base64;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import javax.crypto.Mac;
import javax.crypto.spec.SecretKeySpec;
import java.nio.charset.StandardCharsets;
import java.util.Arrays;


public class PriceEncrypterUtils {
    private final static Integer  InitializeVectorLength = 16;
    private final static Integer  TokenLength = 32;
    private final static Integer  SignatureLength = 4;
    private final static String   ShaAlgo = "HmacSHA1";

    private final static Logger LOGGER = LoggerFactory.getLogger(PriceEncrypterUtils.class);


    private static String printHexBinary(byte[] bytes) {
        StringBuilder stringBuilder = new StringBuilder();
        for(byte aByte: bytes) {
            stringBuilder.append(String.format("%02x",  aByte));
        }
        return stringBuilder.toString();
    }

    private static String webSafeBase64Encode(byte[] plaintext_bytes) {
        byte[] ciphertext_bytes = Base64.encodeBase64(plaintext_bytes);
        Integer padding_count = 0;
        Integer len = ciphertext_bytes.length;
        while(padding_count < len && ciphertext_bytes[len - padding_count - 1] == '=') {
            ++padding_count;
        }
        if (padding_count > 0) {
            ciphertext_bytes = Arrays.copyOfRange(ciphertext_bytes, 0, len-padding_count);
            len = ciphertext_bytes.length;
        }

        for(int i = 0; i < len; i++) {
            if (ciphertext_bytes[i] == '+') {
                ciphertext_bytes[i] = '-';
            }else if (ciphertext_bytes[i] == '/') {
                ciphertext_bytes[i] = '_';
            }
        }

        return new String(ciphertext_bytes);
    }

    /**
     *
     * @param price  价格明文, 单位：分/CPM, 必须大于等于0
     * @param initialize_vector 建议使用16字节uuid或者其他和本次曝光id相关的字符，长度大于16字节会截断
     * @param encryption_token  32字节token
     * @param integrity_token  32字节token
     * @return 加密后的价格字符串
     * @throws Exception
     */
    public static String encryptPrice(Integer price,
                                      byte[] initialize_vector,
                                      byte[] encryption_token,
                                      byte[] integrity_token) throws Exception {
       if (price == null || price < 0 ||
           initialize_vector == null || initialize_vector.length < InitializeVectorLength ||
           encryption_token == null || encryption_token.length != TokenLength ||
           integrity_token == null || integrity_token.length != TokenLength) {
           throw new RuntimeException("params error");
       }
       byte[] initialize_bytes = Arrays.copyOfRange(initialize_vector, 0, InitializeVectorLength);
       String price_str = price.toString();
       //根据 initialize_vector，使用 encryption_token 计算出来的 hmac 摘要作为加密用的padding
       Mac HMAC = Mac.getInstance(ShaAlgo);
       HMAC.reset();
       HMAC.init(new SecretKeySpec(encryption_token, ShaAlgo));
       HMAC.update(initialize_bytes);
       byte[] pad = HMAC.doFinal();

       LOGGER.info("pad:{}", printHexBinary(pad).toLowerCase());

       //使用 padding 和价格按字节循环异或，得到加密之后的价格 enc_price
       byte[] enc_price = price_str.getBytes(StandardCharsets.UTF_8);
       for (int i = 0; i < enc_price.length; ++i) {
           enc_price[i] ^= pad[i % pad.length];
       }

       LOGGER.info("enc_price:{}", printHexBinary(enc_price).toLowerCase());

       //根据 initialize_vector 和 price 明文作为输入，使用 Integrity_token 计算 hmac 并取前 4个字节作为签名 signature
       HMAC.reset();
       HMAC.init(new SecretKeySpec(integrity_token, ShaAlgo));
       HMAC.update(price_str.getBytes(StandardCharsets.UTF_8));
       HMAC.update(initialize_bytes);
       byte[] signature_all = HMAC.doFinal();
       LOGGER.info("signature all:{}", printHexBinary(signature_all).toLowerCase());
       if (signature_all.length < SignatureLength) {
           throw new RuntimeException("internal error");
       }
       byte[] signature = Arrays.copyOfRange(signature_all, 0, SignatureLength);
       LOGGER.info("signature:{}", printHexBinary(signature).toLowerCase());

       //使用 initialize_vector + enc_price + signature 作为完整的加密串
       int mallocLength = initialize_bytes.length + enc_price.length + signature.length;
       byte[] ciphertext_bytes = new byte[mallocLength];
       System.arraycopy(initialize_bytes, 0, ciphertext_bytes, 0, initialize_bytes.length);
       System.arraycopy(enc_price, 0, ciphertext_bytes, initialize_bytes.length, enc_price.length);
       System.arraycopy(signature, 0, ciphertext_bytes, initialize_bytes.length + enc_price.length, signature.length);
       LOGGER.info("ciphertext_bytes:{}", printHexBinary(ciphertext_bytes).toLowerCase());
       //最后对加密串进行 websafe base64 编码
       String output = webSafeBase64Encode(ciphertext_bytes);

       LOGGER.info("output:{}", output);
       return output;
    }
}
