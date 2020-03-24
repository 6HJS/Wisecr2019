#ifndef INTERNALS_AUTHENTICATE_H_
#define INTERNALS_AUTHENTICATE_H_

#include <stdint.h>

#define SECU_COMM_IP_PROTECT (1)
#define SECU_COMM_FIXED_KEY (2)
#define SECU_COMM_BLAKE_HASH (4)
#define SECU_COMM_HASH_LEN_256 (8)
#define SECU_COMM_DECRYPT_AFTER_EACH_PACKET (16)
#define SECU_COMM_RX_AUTHENTICATE (128)

#define FLAG_SET(x, flag) (((x) & (flag)) == (flag))

#define STATE_SET_OFFSET (1)
#define STATE_DONE (2)
#define AUTHENTICATE (3)
#define STATE_PERFORM_HASH (4)
#define STATE_GET_SESKEY (5)
#define STATE_FORCE_USER_CODE (0x06)
#define ENABLE_OBSERVER_MODE    (0x0B)
#define DISABLE_OBSERVER_MODE   (0x0C)
#define STATE_RESTART_IN_BOOT_MODE (0x7F)
#define START_ATTESTATION (0x07)
#define GET_ATTESTATION_RESPONSE (0x08)




void initAuthenticateState(void);
void blockWriteControlMessage(void);
void blockWriteRxWord(void);
void decryptCurrentBlock(void);

#define TAG_STATE (dataBuf[3])

#endif /* INTERNALS_AUTHENTICATE_H_ */
