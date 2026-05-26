#ifndef IQIYI_ENCRYPT_PRICE_H_
#define IQIYI_ENCRYPT_PRICE_H_

#include <string>

namespace IqiyiEnryptDemo {

bool EncryptPrice(std::string price,
                  const std::string& initialize_vector,
                  const std::string& encriyption_token,
                  const std::string& integrity_token,
                  std::string* output);

}  // namespace IqiyiEnryptDemo

#endif  // IQIYI_ENCRIYT_PRICE_H_
