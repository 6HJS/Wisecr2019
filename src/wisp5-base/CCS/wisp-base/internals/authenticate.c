/// Use the BlockWrite callback to support a control message interface.

#include "../wisp-base.h"
#include "authenticate.h"
#include "aes256.h"
#include "cmac_mode.h"
#include <msp430.h>
#include "cmac_mode.h"
#include "IEM.h"
#include "tina.h"

extern void TI_MSPBoot_AppMgr_JumpToApp(void);
extern RWstruct RWData;
extern RFIDstruct rfid;
extern WISP_dataStructInterface_t wispData;
extern uint8_t StatCtrl;
extern uint16_t PassWd;
uint8_t key[16];
uint8_t SesKey[16];
uint8_t s_prover[16];// hash tag from the prover
uint8_t s_token[16];// hash tag calculated by token
uint8_t nver;//new version number
uint8_t veri;//old version number
uint8_t sleepT;//sleep time received from setup
uint8_t activeT;//active time received form setup
uint8_t id[2];
uint16_t firm_len;
uint16_t firm_addr;


uint16_t prevDecryptOffset;
uint16_t* receivedWords;
uint16_t secuCommMode;

const uint8_t IV[16] =
{0xAA, 0x00, 0xAA, 0x00,
 0xAA, 0x00, 0xAA, 0x00,
 0xAA, 0x00, 0xAA, 0x00,
 0xAA, 0x00, 0xAA, 0x00};
const uint8_t foolxkey[16] =
{0x83,0xEE,0x36,0x63,
0xC1,0xD5,0x1F,0xE2,
0x33,0x57,0xAA,0x50,
0x83,0x2C,0x09,0x00};

struct AES_ctx ctx;// AEX_CBC context
TINAmode attMode; // Attestation mode



static uint16_t* BASE_ADDRESS = (uint16_t*)0x10000; //[size]
static uint8_t* XKEY_ADDRESS = (uint8_t*)0x10000; //[16B]
static uint8_t* S_PROVER_ADDRESS = (uint8_t*)(0x10000+16);//[16B]
static uint8_t* NVER_ADDRESS = (uint8_t*)(0x10000+16+16);//[1B]
static uint8_t* SLEEP_ADDRESS = (uint8_t*)(0x10000+16+16+1);//[1B]
static uint8_t* ACTIVE_ADDRESS = (uint8_t*)(0x10000+16+16+1+1);//[1B]
static uint8_t* OBSERV_FLG_ADDRESS = (uint8_t*)(0x10000+16+16+1+1+1);//[1B]

static uint16_t* ATT_BASE = (uint16_t*)0x10000; //[size]
static uint16_t* ATT_OFFSET = (uint8_t*)(0x10000); //[2B]
static uint16_t* ATT_LENGTH = (uint16_t*)(0x10000+2);//[2B]
static uint8_t* ATT_CHALLENGE = (uint8_t*)(0x10000+2+2);//[16B]

extern uint8_t usrBank[USRBANK_SIZE];

void initAuthenticateState(void) {
    receivedWords = (uint16_t*)(&dataBuf[4]);
    *receivedWords = 0;
    secuCommMode = 0;
    prevDecryptOffset = 0;
}

unsigned int j = 0;

void decryptAll(uint8_t* data) {
    unsigned int i = 0;
    AES_init_ctx_iv(&ctx,SesKey,IV); // Reset IV for later stage
    for (i = 0; i < RWData.bwrByteCount; i += 16) {
        j = i;
        _no_operation();
        AES_CBC_decrypt(&ctx, (uint8_t*)&data[i], (uint8_t*)&data[i]);
    }
    return;
}

void decryptCurrentBlock() {
    if (FLAG_SET(secuCommMode, SECU_COMM_IP_PROTECT) &&
            FLAG_SET(secuCommMode, SECU_COMM_DECRYPT_AFTER_EACH_PACKET) &&
            prevDecryptOffset < RWData.wordPtr)
    {
        uint8_t* dataPtr = (uint8_t*)(&BASE_ADDRESS[RWData.wordPtr - 8]);
        AES_CBC_decrypt(&ctx, dataPtr, dataPtr);
        prevDecryptOffset = RWData.wordPtr;
    }
}

// for benchmark only no ourput
int runHash(uint8_t* data, uint16_t size) {
    uint8_t hash[32];
    struct tc_cmac_struct cmac_ctx;
    tc_cmac_AIO(&cmac_ctx,SesKey,data,size,hash);
    return 1;
}

int validateData(uint8_t* data, uint16_t size) {
    uint8_t hash[16];
   // MD5(hash, 16, data, size-16);
    //attach the veri, nver and padding to the data
    memcpy(data+size,&veri,1);
    memcpy(data+size+1,&nver,1);
    uint16_t padding_len = TC_AES_BLOCK_SIZE - ((size+2) % TC_AES_BLOCK_SIZE);
    memset(data+size+2,0,padding_len);
    size = size+2+padding_len; // calculate new length


    struct tc_cmac_struct cmac_ctx;
    tc_cmac_AIO(&cmac_ctx,SesKey,data,size,hash); //size - 16 is the end of payload, including (firmware||veri||nver||pad{zero}) after that is the hash tag from the prover .

    //uint16_t* target_hash = &data[(size >> 1) - (16 >> 1)];
    uint8_t* target_hash = s_prover;
    uint8_t* computed_hash = (uint8_t*)(hash);
    uint8_t msize = 16; //16 byte MAC
    while (msize > 0) {
            if (*target_hash != *computed_hash) {
                return FALSE;
            }

            ++target_hash;
            ++computed_hash;
            --msize;
        }
    return true;
}

void blockWriteControlMessage(void) {
    if (TAG_STATE == (uint8_t)RWData.wordPtr) {
        // Already received and processed this control message
        return;
    }

    switch ((uint8_t)RWData.wordPtr) {
    case STATE_SET_OFFSET:
        RWData.bwrBufPtr = &BASE_ADDRESS[(RWData.controlMessage & 0x1FFF)];
        *receivedWords = 0;
        prevDecryptOffset = 0;
        break;

    case AUTHENTICATE:
        RWData.bwrBufPtr = &BASE_ADDRESS[0];
        *receivedWords = 0;
        prevDecryptOffset = 0;
        break;

    case STATE_PERFORM_HASH:
        runHash(BASE_ADDRESS, RWData.controlMessage);
        break;

    case STATE_DONE:
        if (TAG_STATE != 0x00) {
            return;
        }

        // Calculate the correct number of received bytes accounting for offset control messages
        // The address stored in bwrBufPtr is 20-bit, we want the offset from 0x10000, since the
        // highest address allowed is 0x13FFF, only the lower 16-bits matter.
        RWData.bwrByteCount = (uint16_t)RWData.bwrBufPtr + (*receivedWords << 1);

        EnterPAM(sleepT,activeT);

        decryptAll((uint8_t*)BASE_ADDRESS);

        _no_operation();

        if (!validateData(BASE_ADDRESS, RWData.bwrByteCount)) {
            RWData.bwrByteCount = 0;
            BASE_ADDRESS[0] = 0;
            dataBuf[5] = 0xEE;
            rfid.observerMode = 0;
        }
        else {
            // Mark application firmware section as invalid since we are about to start writting to it
            extern uint16_t _Appl_Reset_Vector;
            *(volatile uint16_t *)(&_Appl_Reset_Vector) = 0xFFFF;
        }
        _no_operation();
        ExitPAM();

        // Exit the main RFID loop to allow the AppManager to process the bytes
        secuCommMode = 0;
        rfid.abortFlag = 1;
        break;

    case STATE_RESTART_IN_BOOT_MODE:
        if(rfid.observerMode){ // ignore more reset to bootloader command if the tag in observer mode
            return;
        }

        TI_MSPBoot_AppMgr_RestartInBootMode();
        rfid.abortFlag = 1;
        break;

    case STATE_GET_SESKEY: // Initialization stage in protocol
        if(rfid.observerMode){ // ignore more reset to bootloader command if the tag in observer mode
            return;
        }

        AES_init_ctx_iv(&ctx,_Device_Key,IV); // Initialization, put the key and the Initial Vector
        AES256_setDecipherKey(AES256_BASE, _Device_Key, AES256_KEYLENGTH_128BIT);
        AES_CBC_decrypt(&ctx, XKEY_ADDRESS, SesKey); // decrypt and store the session key
        AES_init_ctx_iv(&ctx,SesKey,IV); // Reset IV for later stage
        AES256_setDecipherKey(AES256_BASE, SesKey, AES256_KEYLENGTH_128BIT);// rekey for decryption

        // Use received SesKey for further decrypt
        memcpy(s_prover,S_PROVER_ADDRESS,16); // store cmac tag form the prover.

        nver = *NVER_ADDRESS;
        sleepT = *SLEEP_ADDRESS;
        activeT = *ACTIVE_ADDRESS;

        *receivedWords = 0;
        prevDecryptOffset = 0;
        RWData.bwrBufPtr = &BASE_ADDRESS[0];

        if(*OBSERV_FLG_ADDRESS){// check observer mode flag in the session key command
            rfid.observerMode = 1;
        }
        _no_operation();
        break;

    case ENABLE_OBSERVER_MODE:
        rfid.observerMode = 1;
        break;

    case DISABLE_OBSERVER_MODE:
        rfid.observerMode = 0x00;
        rfid.observerRdy = 0x00;
        break;

    case STATE_FORCE_USER_CODE:
        StatCtrl = 0;
        PassWd = 0;

        // Force a PUC reset by writing incorrect Watchdog password
        // (this is generally faster than triggering a full reset)
        WDTCTL = 0xDEAD;
        break;

    case START_ATTESTATION:
        RWData.bwrBufPtr = &BASE_ADDRESS[0];
        *receivedWords = 0;
        prevDecryptOffset = 0;
        attMode = RWData.controlMessage == 0 ? fast : elaborate;
        break;

    case GET_ATTESTATION_RESPONSE:
        veri = *((volatile uint8_t *)&_Device_Version);

        id[0] = ((uint8_t *)(INFO_WISP_TAGID))[0];
        id[1] = ((uint8_t *)(INFO_WISP_TAGID))[1];
        firm_len = (((unsigned short)(*ATT_LENGTH) >> 8) | ((unsigned short)(*ATT_LENGTH) << 8));
        firm_addr = (((unsigned short)(*ATT_OFFSET) >> 8) | ((unsigned short)(*ATT_OFFSET) << 8));
        doTINA(usrBank, _Device_Key, ATT_CHALLENGE, (uint8_t*)firm_addr, firm_len, id, &veri, attMode);
        __no_operation();
        break;


    default:
        break;
    }

    TAG_STATE = (uint8_t)RWData.wordPtr;
    RWData.wordPtr = 0;
}

void blockWriteRxWord(void) {
    if (RWData.wordPtr == *receivedWords) {
        *receivedWords = RWData.wordPtr + 1;
    }
    TAG_STATE = 0x00;
}
