#include <assert.h>
#include <string>
#include <vector>

#include "encrypt_price.h"

int main(int argc, char** argv) {
    const std::string enc_token = "1234567890abcdefghijklmnopqrstuv";
    const std::string sign_token = "abcdefghijklmnopqrstuv1234567890";
    const std::string iv = "1a2b3c4d5e6f7g8h";
    std::vector<std::pair<std::string, std::string>> test_case_list {
        std::make_pair<std::string, std::string>("145", "MWEyYjNjNGQ1ZTZmN2c4aFOlli81T4k"),
        std::make_pair<std::string, std::string>("7800", "MWEyYjNjNGQ1ZTZmN2c4aFWpk6gAWuDZ"),
        std::make_pair<std::string, std::string>("92", "MWEyYjNjNGQ1ZTZmN2c4aFujKohFEQ"),
        std::make_pair<std::string, std::string>("7", "MWEyYjNjNGQ1ZTZmN2c4aFW8Tc8V"),
        std::make_pair<std::string, std::string>("103", "MWEyYjNjNGQ1ZTZmN2c4aFOhkCSwFiA")
    };

    for (auto& test_case : test_case_list) {
        const std::string& price = test_case.first;
        const std::string& expected_ciphertext = test_case.second;
        (void)expected_ciphertext;

        std::string ciphertext;
        IqiyiEnriptDemo::EncryptPrice(price, iv, enc_token, sign_token, &ciphertext);
        assert(expected_ciphertext == ciphertext);
    }
    return 0;
}
