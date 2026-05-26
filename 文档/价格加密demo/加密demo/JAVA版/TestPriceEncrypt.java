//package com.xxx.util;

import java.nio.charset.StandardCharsets;

public class TestPriceEncrypt {
    public static void main(String args[]) {
        Integer price  = new Integer(145);
        String  initializeVector = "1a2b3c4d5e6f7g8h";
        String  encryptionToken = "1234567890abcdefghijklmnopqrstuv";
        String  integrityToken = "abcdefghijklmnopqrstuv1234567890";
        try {
            String  encryptResult = PriceEncrypterUtils.encryptPrice(price, initializeVector.getBytes(StandardCharsets.UTF_8),
                    encryptionToken.getBytes(StandardCharsets.UTF_8), integrityToken.getBytes(StandardCharsets.UTF_8));
            System.out.println(encryptResult);
        }catch (Exception e) {
            e.printStackTrace();
        }

    }
}
