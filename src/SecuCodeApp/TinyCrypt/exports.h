#ifdef TINYCRYPT_EXPORTS
#define TINYCRYPT_API __declspec(dllexport)
#else
#define TINYCRYPT_API __declspec(dllimport)
#endif

#include <stdint.h>

TINYCRYPT_API int compute_cmac(
	const uint8_t* key,
	const uint8_t* data,
	uint32_t dlen,
	uint8_t* tag
);

TINYCRYPT_API int aes_encrypt(
	const uint8_t* iv,
	const uint8_t* key,
	const uint8_t* data,
	uint32_t dlen,
	uint8_t* out
);

TINYCRYPT_API void attestation_response_fast(
	const uint8_t* key,
	const uint8_t* challenge,
	const uint8_t* code,
	uint16_t code_len,
	uint16_t tag_id,
	uint16_t version,
	uint8_t* out
);

TINYCRYPT_API void attestation_response_elaborate(
	const uint8_t* key,
	const uint8_t* challenge,
	const uint8_t* code,
	uint16_t code_len,
	uint16_t tag_id,
	uint16_t version,
	uint8_t* out
);

