/* ccm_mode.c - TinyCrypt implementation of CCM mode */

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

#include "ccm_mode.h"
#include "ccm_mode.h"

#include <stdio.h>
#include <string.h>

#define MASK_TWENTY_SEVEN 0x1b

struct AES_ctx ccm_ctx;//

uint32_t _copy(uint8_t *to, uint32_t to_len,
           const uint8_t *from, uint32_t from_len)
{
    if (from_len <= to_len) {
        (void)memcpy(to, from, from_len);
        return from_len;
    } else {
        return TC_CRYPTO_FAIL;
    }
}

void _set(void *to, uint8_t val, uint32_t len)
{
    (void)memset(to, val, len);
}

/*
 * Doubles the value of a byte for values up to 127.
 */
uint8_t _double_byte(uint8_t a)
{
    return ((a<<1) ^ ((a>>7) * MASK_TWENTY_SEVEN));
}

int32_t _compare(const uint8_t *a, const uint8_t *b, size_t size)
{
    const uint8_t *tempa = a;
    const uint8_t *tempb = b;
    uint8_t result = 0;
    uint32_t i;
    for (i = 0; i < size; i++) {
        result |= tempa[i] ^ tempb[i];
    }
    return result;
}

int32_t tc_ccm_config(TCCcmMode_t c,
                      const uint8_t *key,
                      uint8_t *nonce,
                      uint32_t nlen,
                      uint32_t mlen
                      ){

	/* input sanity check: */
	if (c == (TCCcmMode_t) 0 ||
        key == (uint8_t *) 0 ||
	    nonce == (uint8_t *) 0) {
		return TC_CRYPTO_FAIL;
	} else if (nlen != 13) {
		return TC_CRYPTO_FAIL; /* The allowed nonce size is: 13. See documentation.*/
	} else if ((mlen < 4) || (mlen > 16) || (mlen & 1)) {
		return TC_CRYPTO_FAIL; /* The allowed mac sizes are: 4, 6, 8, 10, 12, 14, 16.*/
	}
	c->mlen = mlen;
	c->nonce = nonce;
	AES_init_ctx_iv(&ccm_ctx,key,NULL); // Initialization, put the key and the Initial Vector

	return TC_CRYPTO_SUCCESS;
}

/**
 * Variation of CBC-MAC mode used in CCM.
 */
static void ccm_cbc_mac(uint8_t *T,
                        const uint8_t *data,
                        uint32_t dlen,
                        uint32_t flag
                        ){

	uint32_t i;

	if (flag > 0) {
		T[0] ^= (uint8_t)(dlen >> 8);
		T[1] ^= (uint8_t)(dlen);
		dlen += 2; i = 2;
	} else {
		i = 0;
	}

	while (i < dlen) {
		T[i++ % (Nb * Nk)] ^= *data++;
		if (((i % (Nb * Nk)) == 0) || dlen == i) {
			(void) tc_aes_encrypt(T, T);
		}
	}
}

/**
 * Variation of CTR mode used in CCM.
 * The CTR mode used by CCM is slightly different than the conventional CTR
 * mode (the counter is increased before encryption, instead of after
 * encryption). Besides, it is assumed that the counter is stored in the last
 * 2 bytes of the nonce.
 */
static int32_t ccm_ctr_mode(uint8_t *out,
                            uint32_t outlen,
                            const uint8_t *in,
                            uint32_t inlen,
                            uint8_t *ctr
                            ){

	uint8_t buffer[TC_AES_BLOCK_SIZE];
	uint8_t nonce[TC_AES_BLOCK_SIZE];
	uint16_t block_num;
	uint32_t i;

	/* input sanity check: */
	if (out == (uint8_t *) 0 ||
	    in == (uint8_t *) 0 ||
	    ctr == (uint8_t *) 0 ||
	    inlen == 0 ||
	    outlen == 0 ||
	    outlen != inlen) {
		return TC_CRYPTO_FAIL;
	}

	/* copy the counter to the nonce */
	(void) _copy(nonce, sizeof(nonce), ctr, sizeof(nonce));

	/* select the last 2 bytes of the nonce to be incremented */
	block_num = (uint16_t) ((nonce[14] << 8)|(nonce[15]));
	for (i = 0; i < inlen; ++i) {
		if ((i % (TC_AES_BLOCK_SIZE)) == 0) {
			block_num++;
			nonce[14] = (uint8_t)(block_num >> 8);
			nonce[15] = (uint8_t)(block_num);
			if (!tc_aes_encrypt(buffer, nonce)) {
				return TC_CRYPTO_FAIL;
			}
		}
		/* update the output */
		*out++ = buffer[i % (TC_AES_BLOCK_SIZE)] ^ *in++;
	}

	/* update the counter */
	ctr[14] = nonce[14]; ctr[15] = nonce[15];

	return TC_CRYPTO_SUCCESS;
}

int32_t tc_ccm_generation_encryption(uint8_t *out, uint32_t olen,
				 const uint8_t *associated_data,
				 uint32_t alen, const uint8_t *payload,
				 uint32_t plen, TCCcmMode_t c)
{

	/* input sanity check: */
	if ((out == (uint8_t *) 0) ||
		(c == (TCCcmMode_t) 0) ||
		((plen > 0) && (payload == (uint8_t *) 0)) ||
		((alen > 0) && (associated_data == (uint8_t *) 0)) ||
		(alen >= TC_CCM_AAD_MAX_BYTES) || /* associated data size unsupported */
		(plen >= TC_CCM_PAYLOAD_MAX_BYTES) || /* payload size unsupported */
		(olen < (plen + c->mlen))) {  /* invalid output buffer size */
		return TC_CRYPTO_FAIL;
	}

	uint8_t b[Nb * Nk];
	uint8_t tag[Nb * Nk];
	uint32_t i;

	/* GENERATING THE AUTHENTICATION TAG: */

	/* formatting the sequence b for authentication: */
	b[0] = ((alen > 0) ? 0x40:0) | (((c->mlen - 2) / 2 << 3)) | (1);
	for (i = 1; i <= 13; ++i) {
		b[i] = c->nonce[i - 1];
	}
	b[14] = (uint8_t)(plen >> 8);
	b[15] = (uint8_t)(plen);

	/* computing the authentication tag using cbc-mac: */
	(void) tc_aes_encrypt(tag, b);
	if (alen > 0) {
		ccm_cbc_mac(tag, associated_data, alen, 1);
	}
	if (plen > 0) {
		ccm_cbc_mac(tag, payload, plen, 0);
	}

	/* ENCRYPTION: */

	/* formatting the sequence b for encryption: */
	b[0] = 1; /* q - 1 = 2 - 1 = 1 */
	b[14] = b[15] = TC_ZERO_BYTE;

	/* encrypting payload using ctr mode: */
	ccm_ctr_mode(out, plen, payload, plen, b);

	b[14] = b[15] = TC_ZERO_BYTE; /* restoring initial counter for ctr_mode (0):*/

	/* encrypting b and adding the tag to the output: */
	(void) tc_aes_encrypt(b, b);
	out += plen;
	for (i = 0; i < c->mlen; ++i) {
		*out++ = tag[i] ^ b[i];
	}

	return TC_CRYPTO_SUCCESS;
}

int32_t tc_ccm_decryption_verification(uint8_t *out, uint32_t olen,
				   const uint8_t *associated_data,
				   uint32_t alen, const uint8_t *payload,
				   uint32_t plen, TCCcmMode_t c)
{

	/* input sanity check: */
	if ((out == (uint8_t *) 0) ||
	    (c == (TCCcmMode_t) 0) ||
	    ((plen > 0) && (payload == (uint8_t *) 0)) ||
	    ((alen > 0) && (associated_data == (uint8_t *) 0)) ||
	    (alen >= TC_CCM_AAD_MAX_BYTES) || /* associated data size unsupported */
	    (plen >= TC_CCM_PAYLOAD_MAX_BYTES) || /* payload size unsupported */
	    (olen < plen - c->mlen)) { /* invalid output buffer size */
		return TC_CRYPTO_FAIL;
  }

	uint8_t b[Nb * Nk];
	uint8_t tag[Nb * Nk];
	uint32_t i;

	/* DECRYPTION: */

	/* formatting the sequence b for decryption: */
	b[0] = 1; /* q - 1 = 2 - 1 = 1 */
	for (i = 1; i < 14; ++i) {
		b[i] = c->nonce[i - 1];
	}
	b[14] = b[15] = TC_ZERO_BYTE; /* initial counter value is 0 */

	/* decrypting payload using ctr mode: */
	ccm_ctr_mode(out, plen - c->mlen, payload, plen - c->mlen, b);

	b[14] = b[15] = TC_ZERO_BYTE; /* restoring initial counter value (0) */

	/* encrypting b and restoring the tag from input: */
	(void) tc_aes_encrypt(b, b);
	for (i = 0; i < c->mlen; ++i) {
		tag[i] = *(payload + plen - c->mlen + i) ^ b[i];
	}

	/* VERIFYING THE AUTHENTICATION TAG: */

	/* formatting the sequence b for authentication: */
	b[0] = ((alen > 0) ? 0x40:0)|(((c->mlen - 2) / 2 << 3)) | (1);
	for (i = 1; i < 14; ++i) {
		b[i] = c->nonce[i - 1];
	}
	b[14] = (uint8_t)((plen - c->mlen) >> 8);
	b[15] = (uint8_t)(plen - c->mlen);

	/* computing the authentication tag using cbc-mac: */
	(void) tc_aes_encrypt(b, b);
	if (alen > 0) {
		ccm_cbc_mac(b, associated_data, alen, 1);
	}
	if (plen > 0) {
		ccm_cbc_mac(b, out, plen - c->mlen, 0);
	}

	/* comparing the received tag and the computed one: */
	if (_compare(b, tag, c->mlen) == 0) {
		return TC_CRYPTO_SUCCESS;
  	} else {
		/* erase the decrypted buffer in case of mac validation failure: */
		_set(out, 0, plen - c->mlen);
		return TC_CRYPTO_FAIL;
	}
}

int32_t tc_aes_encrypt(uint8_t *out, const uint8_t *in)
{

	if (out == (uint8_t *) 0) {
		return TC_CRYPTO_FAIL;
	} else if (in == (const uint8_t *) 0) {
		return TC_CRYPTO_FAIL;
	}

    AES256_setCipherKey(AES256_BASE, ccm_ctx.Key, AES256_KEYLENGTH_128BIT);
    // Encrypt data with preloaded cipher key
    AES256_encryptData(AES256_BASE, in, out);//[185]

	return TC_CRYPTO_SUCCESS;
}

int32_t tc_aes_decrypt(uint8_t *out, const uint8_t *in)
{

	if (out == (uint8_t *) 0) {
		return TC_CRYPTO_FAIL;
	} else if (in == (const uint8_t *) 0) {
		return TC_CRYPTO_FAIL;
	}

    AES256_setDecipherKey(AES256_BASE, ccm_ctx.Key, AES256_KEYLENGTH_128BIT);
    // Decrypt data
    AES256_decryptData(AES256_BASE, in, out);

	return TC_CRYPTO_SUCCESS;
}
