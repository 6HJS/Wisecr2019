/* ccm_mode.h - TinyCrypt interface to a CCM mode implementation */

/*
 *  Copyright (C) 2017 by Intel Corporation, All Rights Reserved.
 *
 *  Redistribution and use in source and binary forms, with or without
 *  modification, are permitted provided that the following conditions are met:
 *
 *    - Redistributions of source code must retain the above copyright notice,
 *     this list of conditions and the following disclaimer.
 *
 *    - Redistributions in binary form must reproduce the above copyright
 *    notice, this list of conditions and the following disclaimer in the
 *    documentation and/or other materials provided with the distribution.
 *
 *    - Neither the name of Intel Corporation nor the names of its contributors
 *    may be used to endorse or promote products derived from this software
 *    without specific prior written permission.
 *
 *  THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
 *  AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
 *  IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
 *  ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE
 *  LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR
 *  CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF
 *  SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS
 *  INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN
 *  CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
 *  ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
 *  POSSIBILITY OF SUCH DAMAGE.
 */

/**
 * @file
 * @brief Interface to a CCM mode implementation.
 *
 *  Overview: CCM (for "Counter with CBC-MAC") mode is a NIST approved mode of
 *            operation defined in SP 800-38C.
 *
 *            TinyCrypt CCM implementation accepts:
 *
 *            1) Both non-empty payload and associated data (it encrypts and
 *            authenticates the payload and also authenticates the associated
 *            data);
 *            2) Non-empty payload and empty associated data (it encrypts and
 *            authenticates the payload);
 *            3) Non-empty associated data and empty payload (it degenerates to
 *            an authentication mode on the associated data).
 *
 *            TinyCrypt CCM implementation accepts associated data of any length
 *            between 0 and (2^16 - 2^8) bytes.
 *
 *  Security: The mac length parameter is an important parameter to estimate the
 *            security against collision attacks (that aim at finding different
 *            messages that produce the same authentication tag). TinyCrypt CCM
 *            implementation accepts any even integer between 4 and 16, as
 *            suggested in SP 800-38C.
 *
 *            RFC-3610, which also specifies CCM, presents a few relevant
 *            security suggestions, such as: it is recommended for most
 *            applications to use a mac length greater than 8. Besides, the
 *            usage of the same nonce for two different messages which are
 *            encrypted with the same key destroys the security of CCM mode.
 *
 *  Requires: AES-128
 *
 *  Usage:    1) call tc_ccm_config to configure.
 *
 *            2) call tc_ccm_mode_encrypt to encrypt data and generate tag.
 *
 *            3) call tc_ccm_mode_decrypt to decrypt data and verify tag.
 */

#ifndef __TC_CCM_MODE_H__
#define __TC_CCM_MODE_H__

#include <stddef.h>
#include <stdint.h>
#include "aes.h"

#ifdef __cplusplus
extern "C" {
#endif

/* max additional authenticated size in bytes: 2^16 - 2^8 = 65280 */
#define TC_CCM_AAD_MAX_BYTES 0xff00

/* max message size in bytes: 2^(8L) = 2^16 = 65536 */
#define TC_CCM_PAYLOAD_MAX_BYTES 0x10000

#define TC_AES_BLOCK_SIZE (Nb*Nk)
#define TC_AES_KEY_SIZE (Nb*Nk)

/* tc_ccm context_structure represents the state of a CCM computation */
typedef struct tc_ccm_mode_struct {
	uint8_t *nonce; /* nonce required by CCM */
	uint32_t mlen; /* mac length in bytes (parameter t in SP-800 38C) */
} *TCCcmMode_t;

uint8_t aes_key[KEYLEN];

/**
 * @brief CCM configuration procedure
 * @return returns TC_CRYPTO_SUCCESS (1)
 *          returns TC_CRYPTO_FAIL (0) if:
 *                c == NULL or
 *                nonce == NULL or
 *                mlen != {4, 6, 8, 10, 12, 16}
 * @param c -- CCM state
 * @param key -- CCM key
 * @param nonce IN - nonce
 * @param nlen -- nonce length in bytes
 * @param mlen -- mac length in bytes (parameter t in SP-800 38C)
 */
int32_t tc_ccm_config(TCCcmMode_t c, const uint8_t *key, uint8_t *nonce,
		  uint32_t nlen, uint32_t mlen);

/**
 * @brief CCM tag generation and encryption procedure
 * @return returns TC_CRYPTO_SUCCESS (1)
 *         returns TC_CRYPTO_FAIL (0) if:
 *                out == NULL or
 *                c == NULL or
 *                ((plen > 0) and (payload == NULL)) or
 *                ((alen > 0) and (associated_data == NULL)) or
 *                (alen >= TC_CCM_AAD_MAX_BYTES) or
 *                (plen >= TC_CCM_PAYLOAD_MAX_BYTES) or
 *                (olen < plen + maclength)
 *
 * @param out OUT -- encrypted data
 * @param olen IN -- output length in bytes
 * @param associated_data IN -- associated data
 * @param alen IN -- associated data length in bytes
 * @param payload IN -- payload
 * @param plen IN -- payload length in bytes
 * @param c IN -- CCM state
 *
 * @note: out buffer should be at least (plen + c->mlen) bytes long.
 *
 * @note: The sequence b for encryption is formatted as follows:
 *        b = [FLAGS | nonce | counter ], where:
 *          FLAGS is 1 byte long
 *          nonce is 13 bytes long
 *          counter is 2 bytes long
 *        The byte FLAGS is composed by the following 8 bits:
 *          0-2 bits: used to represent the value of q-1
 *          3-7 btis: always 0's
 *
 * @note: The sequence b for authentication is formatted as follows:
 *        b = [FLAGS | nonce | length(mac length)], where:
 *          FLAGS is 1 byte long
 *          nonce is 13 bytes long
 *          length(mac length) is 2 bytes long
 *        The byte FLAGS is composed by the following 8 bits:
 *          0-2 bits: used to represent the value of q-1
 *          3-5 bits: mac length (encoded as: (mlen-2)/2)
 *          6: Adata (0 if alen == 0, and 1 otherwise)
 *          7: always 0
 */
int32_t tc_ccm_generation_encryption(uint8_t *out, uint32_t olen,
			   	 const uint8_t *associated_data,
			   	 uint32_t alen, const uint8_t *payload,
				 uint32_t plen, TCCcmMode_t c);

/**
 * @brief CCM decryption and tag verification procedure
 * @return returns TC_CRYPTO_SUCCESS (1)
 *         returns TC_CRYPTO_FAIL (0) if:
 *                out == NULL or
 *                c == NULL or
 *                ((plen > 0) and (payload == NULL)) or
 *                ((alen > 0) and (associated_data == NULL)) or
 *                (alen >= TC_CCM_AAD_MAX_BYTES) or
 *                (plen >= TC_CCM_PAYLOAD_MAX_BYTES) or
 *                (olen < plen - c->mlen)
 *
 * @param out OUT -- decrypted data
 * @param associated_data IN -- associated data
 * @param alen IN -- associated data length in bytes
 * @param payload IN -- payload
 * @param plen IN -- payload length in bytes
 * @param c IN -- CCM state
 *
 * @note: out buffer should be at least (plen - c->mlen) bytes long.
 *
 * @note: The sequence b for encryption is formatted as follows:
 *        b = [FLAGS | nonce | counter ], where:
 *          FLAGS is 1 byte long
 *          nonce is 13 bytes long
 *          counter is 2 bytes long
 *        The byte FLAGS is composed by the following 8 bits:
 *          0-2 bits: used to represent the value of q-1
 *          3-7 btis: always 0's
 *
 * @note: The sequence b for authentication is formatted as follows:
 *        b = [FLAGS | nonce | length(mac length)], where:
 *          FLAGS is 1 byte long
 *          nonce is 13 bytes long
 *          length(mac length) is 2 bytes long
 *        The byte FLAGS is composed by the following 8 bits:
 *          0-2 bits: used to represent the value of q-1
 *          3-5 bits: mac length (encoded as: (mlen-2)/2)
 *          6: Adata (0 if alen == 0, and 1 otherwise)
 *          7: always 0
 */
int32_t tc_ccm_decryption_verification(uint8_t *out, uint32_t olen,
				   const uint8_t *associated_data,
				   uint32_t alen, const uint8_t *payload, uint32_t plen,
				   TCCcmMode_t c);

/**
 *  @brief AES-128 Encryption procedure
 *  Encrypts contents of in buffer into out buffer under key;
 *              schedule s
 *  @note Assumes s was initialized by aes_set_encrypt_key;
 *              out and in point to 16 byte buffers
 *  @return  returns TC_CRYPTO_SUCCESS (1)
 *           returns TC_CRYPTO_FAIL (0) if: out == NULL or in == NULL or s == NULL
 *  @param out IN/OUT -- buffer to receive ciphertext block
 *  @param in IN -- a plaintext block to encrypt
 */
int32_t tc_aes_encrypt(uint8_t *out, const uint8_t *in);

/**
 *  @brief AES-128 Encryption procedure
 *  Decrypts in buffer into out buffer under key schedule s
 *  @return returns TC_CRYPTO_SUCCESS (1)
 *          returns TC_CRYPTO_FAIL (0) if: out is NULL or in is NULL or s is NULL
 *  @note   Assumes s was initialized by aes_set_encrypt_key
 *          out and in point to 16 byte buffers
 *  @param out IN/OUT -- buffer to receive ciphertext block
 *  @param in IN -- a plaintext block to encrypt
 */
int32_t tc_aes_decrypt(uint8_t *out, const uint8_t *in);
#ifdef __cplusplus
}
#endif

#endif /* __TC_CCM_MODE_H__ */
