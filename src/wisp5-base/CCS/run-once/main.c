
/** @file		main.c
 * 	@brief		Run this routine once for each new WISP. Generates and stores
 * 				a table of random values to use in quickly producing RN16, and
 * 				also generates a unique ID for each WISP.
 *
 * 				Write pre-defined Tag ID to InfoD
 * 				Write fixed Key to IntoD
 *
 * 	@author		Aaron Parks, Justin Reina, Sensor Systems Lab, University of Washington
 * 	            Yang Su, Auto-ID Lab, The University of Adelaide
 */

#define _TAG242_

#include "wisp-base.h"


/**
 * Generate a number of random 16 bit integers
 * @param RN16Vals pointer to buffer for random output vals
 * @param len number of 16 bit words to generate
 */
uint8_t genRN16Vals(uint16_t* RN16Vals, int len){
  uint8_t 	i;

  for(i=0; i<len; i++) {
    RN16Vals[i] = RAND_adcRand16();// generate a new RN16 from 16 LSBs
  }
  return TRUE;
}


 /*
  * Pulse LED at a given rate for a given number of times
  * @param count number of times to pulse
  * @param delay btwn pulses and after last pulse
 */
void ledBlinks (uint8_t count, uint16_t duration) {

	while(count--) {
		// Stay on for ~1ms, then wait for specified duration
		BITSET(PLED1OUT,PIN_LED1);
		Timer_LooseDelay(32);
		BITCLR(PLED1OUT,PIN_LED1);
		Timer_LooseDelay(duration);
	}
	return;
}

/**
 * Generates and stores a table of random numbers into Info Mem segment B/C.
 *  These are used by the WISP as a unique ID and for RN16 generation in Aloha protocol.
 */
void main (void) {
  WDTCTL = WDTPW | WDTHOLD;   // Stop watchdog timer

  uint16_t  RN16Vals[NUM_RN16_2_STORE]; // table of RN16s to store into FLASH for WISP use

  WISP_init();

  // Generate RN16 table
  genRN16Vals(RN16Vals, NUM_RN16_2_STORE);

  FRAM_init();

#if defined _TAG235_
  FRAM_write((uint16_t*)(INFO_WISP_TAGID), 0x0235);
#elif defined _TAG237_
  FRAM_write((uint16_t*)(INFO_WISP_TAGID), 0x0237);
#elif defined _TAG238_
  FRAM_write((uint16_t*)(INFO_WISP_TAGID), 0x0238);
#elif defined _TAG239_
  FRAM_write((uint16_t*)(INFO_WISP_TAGID), 0x0239);
#elif defined _TAG240_
  FRAM_write((uint16_t*)(INFO_WISP_TAGID), 0x0240);
#elif defined _TAG241_
  FRAM_write((uint16_t*)(INFO_WISP_TAGID), 0x0241);
#elif defined _TAG242_
  FRAM_write((uint16_t*)(INFO_WISP_TAGID), 0x0242);
#else
  FRAM_write((uint16_t*)(INFO_WISP_TAGID), 0xAA00);
#endif

  // Store UUID and RN16 table to memory
  FRAM_write_int_array((uint16_t*)(INFO_WISP_RAND_TBL), NUM_RN16_2_STORE, RN16Vals);

  // Blink slow when done.
  while(FOREVER) {
      ledBlinks(1,20000);
  }

}

//void TIMER3_A0_ISR(void) {}


