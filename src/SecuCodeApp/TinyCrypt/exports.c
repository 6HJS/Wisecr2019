#include "exports.h"

#include "../../AES_library/CBC_CMAC_PC/cmac_mode.h"
#include "../../AES_library/CBC_CMAC_PC/aes.h"

#include "../../TINA_PC/tina.h"

TINYCRYPT_API int compute_cmac(
	const uint8_t* key,
	const uint8_t* data,
	uint32_t dlen,
	uint8_t* tag
)
{
	struct tc_cmac_struct s;
	return tc_cmac_AIO(&s, key, data, (size_t)dlen, tag);
}

TINYCRYPT_API int aes_encrypt(
	const uint8_t* iv,
	const uint8_t* key,
	const uint8_t* data,
	uint32_t dlen,
	uint8_t* out
) {
	AES_CBC_encrypt_buffer(out, data, dlen, key, iv);
	return 1;
}

TINYCRYPT_API void attestation_response_elaborate(
	const uint8_t* key,
	const uint8_t* challenge,
	const uint8_t* code,
	uint16_t code_len,
	uint16_t tag_id,
	uint16_t version,
	uint8_t* out
) {
	uint8_t id[2] = { tag_id & 0xFF, (tag_id >> 8) & 0xFF };
	uint8_t veri[2] = { version & 0xFF, (version >> 8) & 0xFF };

	TINAmode mode = elaborate;
	doTINA(out, key, challenge, code, code_len, id, veri, mode);
}

TINYCRYPT_API void attestation_response_fast(
	const uint8_t* key,
	const uint8_t* challenge,
	const uint8_t* code,
	uint16_t code_len,
	uint16_t tag_id,
	uint16_t version,
	uint8_t* out
) {
	uint8_t id[2] = { tag_id & 0xFF, (tag_id >> 8) & 0xFF };
	uint8_t veri[2] = { version & 0xFF, (version >> 8) & 0xFF };

	TINAmode mode = fast;
	doTINA(out, key, challenge, code, code_len, id, veri, mode);
}