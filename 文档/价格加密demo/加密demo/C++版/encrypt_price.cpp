#include <string>
#include <vector>

#include <openssl/bio.h>
#include <openssl/buffer.h>
#include <openssl/hmac.h>
#include <openssl/evp.h>

namespace {

constexpr size_t kIvLength = 16;
constexpr size_t kSignatureLength = 4;
constexpr size_t kTokenLength = 32;

// 标准base64加密
std::string std_base64_encode(const std::string& input) {
    BIO *bmem, *b64;
    BUF_MEM *bptr;
    b64 = BIO_new(BIO_f_base64());
    // openssl 提供的 base64 加密接口会在字符串最后添加一个换行，
    // 通过设置 BIO_FLAGS_BASE64_NO_NL 去掉这个默认的换行符
    BIO_set_flags(b64, BIO_FLAGS_BASE64_NO_NL);
    bmem = BIO_new(BIO_s_mem());
    b64 = BIO_push(b64,bmem);
    BIO_write(b64, input.c_str(), input.size());
    (void)BIO_flush(b64);
    BIO_get_mem_ptr(b64, &bptr);
    std::string output;
    output.assign(reinterpret_cast<const char*>(bptr->data), bptr->length);
    BIO_free_all(b64);
    return output;
}

// 使用sha1作为哈希算法, 计算HMAC摘要
bool GenerateHmac(const std::string& key,
                  const std::vector<std::string>& content_list,
                  std::string* padding) {
    // 使用 sha1 作为哈希算法
    const EVP_MD* engine = EVP_sha1();
    HMAC_CTX ctx;
    HMAC_CTX_init(&ctx);
    if (HMAC_Init_ex(&ctx, key.c_str(), key.size(), engine, nullptr) != 1) {
        return false;
    }
    for (const std::string& content : content_list) {
        if (HMAC_Update(&ctx,
                        reinterpret_cast<const unsigned char*>(content.c_str()),
                        content.size()) != 1) {
            return false;
        }
    }
    unsigned char buff[EVP_MAX_MD_SIZE] = {0};
    unsigned output_length = 0;
    if (HMAC_Final(&ctx, buff, &output_length) != 1 || output_length > sizeof(buff)) {
        return false;
    }
    padding->assign(reinterpret_cast<const char*>(buff), output_length);
    HMAC_CTX_cleanup(&ctx);
    return true;
}

std::string WebSafeBase64Encode(const std::string& input) {
  std::string output = std_base64_encode(input);
  size_t padding_count = 0;
  const size_t len = output.size();
  // 去掉base64的填充字符'='
  while (padding_count < len && output[len - padding_count - 1] == '=') {
    ++padding_count;
  }
  output = output.substr(0, len - padding_count);

  // 替换掉标准base64中的'+'、'/'，方便在web环境传输
  for (char& c : output) {
    if (c == '+') {
      c = '-';
    } else if (c == '/') {
      c = '_';
    }
  }
  return output;
}

}  // namespace

namespace IqiyiEnriptDemo {

// 加密函数实现
bool EncryptPrice(std::string price,
                  const std::string& initialize_vector,
                  const std::string& encriyption_token,
                  const std::string& integrity_token,
                  std::string* output) {
  if (initialize_vector.size() < kIvLength ||
      encriyption_token.size() != kTokenLength ||
      integrity_token.size() != kTokenLength) {
    return false;
  }

  // initialize_vector 长度若超过16字节，进行截断
  // initialize_vector 每个请求的 initialize_vector 的内容不要完全一样,
  // 否则容易被破解
  std::string iv = initialize_vector.substr(0, kIvLength);

  // 使用iv、encriyption_token 计算出加密用的padding
  std::string padding;
  if (!GenerateHmac(encriyption_token,
                    std::vector<std::string>{iv},
                    &padding)) {
      return false;
  }

  // 循环异或得到加密之后的价格
  std::string enc_price(price);
  for (size_t i = 0; i < enc_price.size(); ++i) {
    enc_price[i] ^= padding[i % padding.size()];
  }


  // 生成价格明文及initialize_vector的摘要
  std::string signature;
  if (!GenerateHmac(integrity_token,
                    std::vector<std::string>{price, iv},
                    &signature)) {
      return false;
  }

  // 最终完整的密文由 initialize_vector || enc_price || sign 拼接得到
  *output = iv;
  output->append(enc_price);
  output->append(signature.substr(0, kSignatureLength));

  // WebSafeBase64 编码，便于通过url传输
  *output = WebSafeBase64Encode(*output);
  return true;
}


}  // namespace IqiyiEnriptDemo
