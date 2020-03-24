/** @file       main.c
 *  @brief      AES-HW encode decode
 *
 *  @author     Yang Su, Auto-ID Lab, The University of Adelaide
 */

/**
 * as a unique ID.
 */
#include <stdint.h>
#include "ccm_mode.h"


#define TC_CCM_MAX_CT_SIZE 50
#define TC_CCM_MAX_PT_SIZE 25
#define NUM_NIST_KEYS 16
#define NONCE_LEN 13
#define HEADER_LEN 8
#define M_LEN8 8
#define M_LEN10 10
#define DATA_BUF_LEN23 23
#define DATA_BUF_LEN24 24
#define DATA_BUF_LEN25 25
#define EXPECTED_BUF_LEN31 31
#define EXPECTED_BUF_LEN32 32
#define EXPECTED_BUF_LEN33 33
#define EXPECTED_BUF_LEN34 34
#define EXPECTED_BUF_LEN35 35

    int32_t result = TC_PASS;
    /* RFC 3610 test vector #1 */
    const uint8_t key[NUM_NIST_KEYS] = {
        0xc0, 0xc1, 0xc2, 0xc3, 0xc4, 0xc5, 0xc6, 0xc7,
        0xc8, 0xc9, 0xca, 0xcb, 0xcc, 0xcd, 0xce, 0xcf
    };
    uint8_t nonce[NONCE_LEN] = {
        0x00, 0x00, 0x00, 0x03, 0x02, 0x01, 0x00, 0xa0,
        0xa1, 0xa2, 0xa3, 0xa4, 0xa5
    };
    const uint8_t hdr[HEADER_LEN] = {
        0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07
    };
    const uint8_t data[DATA_BUF_LEN23] = {
        0x08, 0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f,
        0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17,
        0x18, 0x19, 0x1a, 0x1b, 0x1c, 0x1d, 0x1e
    };
    const uint8_t expected[EXPECTED_BUF_LEN31] = {
        0x58, 0x8c, 0x97, 0x9a, 0x61, 0xc6, 0x63, 0xd2,
        0xf0, 0x66, 0xd0, 0xc2, 0xc0, 0xf9, 0x89, 0x80,
        0x6d, 0x5f, 0x6b, 0x61, 0xda, 0xc3, 0x84, 0x17,
        0xe8, 0xd1, 0x2c, 0xfd, 0xf9, 0x26, 0xe0
    };
    uint16_t mlen = M_LEN8;



void main(void){
    WDTCTL = WDTPW | WDTHOLD;   // Stop watchdog timer
    PM5CTL0 &= ~LOCKLPM5;       // Lock LPM5.

    struct tc_ccm_mode_struct c;
    uint8_t ciphertext[TC_CCM_MAX_CT_SIZE];
    uint8_t decrypted[TC_CCM_MAX_PT_SIZE];

    // put veri||nver into nonce slot.

    result = tc_ccm_config(&c, key, nonce, sizeof(nonce), mlen);
    __no_operation();
    result = tc_ccm_generation_encryption(ciphertext, TC_CCM_MAX_CT_SIZE, hdr, sizeof(hdr), data, sizeof(data), &c); //encryption part
    __no_operation();
    result = tc_ccm_decryption_verification(decrypted, TC_CCM_MAX_PT_SIZE, hdr, sizeof(hdr), ciphertext, sizeof(data)+mlen, &c);
    __no_operation();

        while(1)
        {
        }
        ;

}

