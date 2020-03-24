#include "ota_demo.h"

#ifdef EPC_DEMO

extern WISP_dataStructInterface_t wispData;

int main(void) {
    WISP_init();

    // Initialize BlockWrite data buffer.
    uint16_t bwr_array[6] = {0};
    RWData.bwrBufPtr = bwr_array;


    // Set up operating parameters for WISP comm routines
    WISP_setMode( MODE_READ | MODE_WRITE | MODE_USES_SEL);
    WISP_setAbortConditions(CMD_ID_READ | CMD_ID_WRITE | CMD_ID_ACK);

    // Set up EPC
    wispData.epcBuf[0] = 0x0B;        // Tag type
    wispData.epcBuf[1] = 0;           // Unused data field
    wispData.epcBuf[2] = 0;           // Unused data field
    wispData.epcBuf[3] = 0;           // Unused data field
    wispData.epcBuf[4] = 0;           // Unused data field
    wispData.epcBuf[5] = 0;           // Unused data field
    wispData.epcBuf[6] = 0;           // Unused data field
    wispData.epcBuf[7] = 0x00;        // Unused data field
    wispData.epcBuf[8] = 0x00;        // Unused data field
    wispData.epcBuf[9] = 0x09;        // Tag firmware version
    wispData.epcBuf[10] = *((uint8_t*)INFO_WISP_TAGID+1); // WISP ID MSB: Pull from INFO seg
    wispData.epcBuf[11] = *((uint8_t*)INFO_WISP_TAGID); // WISP ID LSB: Pull from INFO seg

    // Talk to the RFID reader.
    while (FOREVER) {
      WISP_doRFID();
    }
}
#endif
