/*
 * \file   main.c
 *
 * \brief  Main routine for the bootloader for FR5969
 *
 */
/* --COPYRIGHT--,BSD
 * Copyright (c) 2012, Texas Instruments Incorporated
 * All rights reserved.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions
 * are met:
 *
 * *  Redistributions of source code must retain the above copyright
 *    notice, this list of conditions and the following disclaimer.
 *
 * *  Redistributions in binary form must reproduce the above copyright
 *    notice, this list of conditions and the following disclaimer in the
 *    documentation and/or other materials provided with the distribution.
 *
 * *  Neither the name of Texas Instruments Incorporated nor the names of
 *    its contributors may be used to endorse or promote products derived
 *    from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
 * AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO,
 * THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR
 * PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR
 * CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL,
 * EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO,
 * PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS;
 * OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY,
 * WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR
 * OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE,
 * EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 * --/COPYRIGHT--*/


#include <msp430.h>
#include <stdint.h>
#include "TI_MSPBoot_Common.h"
#include "TI_MSPBoot_CI.h"
#include "TI_MSPBoot_MI.h"
#include "TI_MSPBoot_AppMgr.h"
#include "wisp-base.h"
#include <stdbool.h>


#define WRITE_SIZE 512

static uint16_t* BASE_ADDRESS = (uint16_t*)0x10000;

uint16_t rawV;
uint16_t miliVolt_start;
uint16_t miliVolt_finish;
extern uint8_t  usrBank [USRBANK_SIZE];
extern WISP_dataStructInterface_t wispData;


extern uint8_t nver;//new version number
extern uint8_t veri;//old version number

//
//  Local function prototypes
//
static void HW_init(void);
static void MPU_init_bootloader(void);
static void MPU_init_app(void);

extern void CopyWispISRs(void);
extern inline void TTIEM(void);



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

void FRAMPrep(void)
{
  unsigned int i=0;

  for ( i= 0; i< WRITE_SIZE; i++)
  {
      BASE_ADDRESS[i] = 0x00;
  }
}

/******************************************************************************
 *
 * @brief   Main function
 *  - Initializes the MCU
 *  - Selects whether to run application or bootloader
 *  - If bootloader:
 *      - Initializes the peripheral interface
 *      - Waits for a command
 *      - Sends the corresponding response
 *  - If application:
 *      - Jump to application
 *
 * @return  none
 *****************************************************************************/
int main(void)
{
    HW_init();
    MPU_init_bootloader();

    // Initialize WISP specific internals
    WISP_init();
    ADC_init();

    //Test Touch [4551]
    P1OUT |= BIT4;
    TTIEM();
    P1OUT &= ~BIT4;
    rawV = ADC_read();
    P1OUT |= BIT4;
    TTIEM();
    P1OUT &= ~BIT4;

    miliVolt_start = 2*ADC_rawToVoltage(rawV); // times 2 because there is a half voltage divider


    usrBank[0] = (miliVolt_start >> 8);
    usrBank[1] = (miliVolt_start & 0xff);

    tBOOL app_valid = TI_MSPBoot_AppMgr_ValidateApp();

    // Validate the application and jump if needed
    if (app_valid == TRUE_t) {
        // Copy app ISRs then setup MPU segements and lock the MPU registers
        CopyAppISRs();
        CopyWispISRs();

        MPU_init_app();

        // Enable the Watchdog again then jump to app
        WDTCTL = WDTPW + WDTCNTCL;
        TI_MSPBoot_APPMGR_JUMPTOAPP();
    }
    else {
        CopyWispISRs();
    }

    TI_MSPBoot_CI_Init();      // Initialize the Communication Interface

    //need to put TeTo value after the wispData initialization
    wispData.epcBuf[6] = (miliVolt_start >> 8);
    wispData.epcBuf[7] = (miliVolt_start & 0xff);

    while(1)
    {
        // Poll PHY and Data Link interface for new packets
        TI_MSPBoot_CI_PHYDL_Poll();

        // If a new packet is detected, process it
        if (TI_MSPBoot_CI_Process() == RET_JUMP_TO_APP)
        {
            // If Packet indicates a jump to App
            TI_MSPBoot_AppMgr_JumpToApp();
        }
    }
}

/******************************************************************************
 *
 * @brief   Initializes the basic MCU HW
 *
 * @return  none
 *****************************************************************************/
static void HW_init(void)
{
    WDTCTL = WDTPW | WDTHOLD;   // Stop watchdog timer


//    P3OUT |= BIT4;
//    P3DIR |= BIT4;// mark the Tactive start.
    P1DIR |= BIT4;
    P1OUT &= ~BIT4;
    // Just initialize S2 button to force BSL mode
    P1OUT |= BIT1;
    P1REN |= BIT1;
    PM5CTL0 &= ~LOCKLPM5;       // Lock LPM5.

    // Copy out the version number from secure storage
    nver = *((volatile uint8_t *)&_Device_Version);
    veri = *((volatile uint8_t *)&_Device_Version);
}

/// Initialize MPU for bootloader execution
/// * Segment 1 = Application Code (Read + Write)
/// * Segment 2 = Secure area + Bootloader (Read + Write + Execute)
/// * Segment 3 = Download area (Read + Write)
static void MPU_init_bootloader(void) {
    // Enable access to MPU registers
    MPUCTL0 = MPUPW;

    //  Border 1 = Start of secure memory
    MPUSEGB1 = (0xA200) >> 4;
    //  Border 2 = Start of download area
    MPUSEGB2 = (0x10000) >> 4;

    // Segment 1: Read + Write
    MPUSAM &= ~MPUSEG1XE;
    MPUSAM |= (MPUSEG1RE | MPUSEG1WE | MPUSEG1VS);

    // Segment 2: Read + Write + Execute
    MPUSAM &= ~MPUSEG2VS;
    MPUSAM |= (MPUSEG2RE | MPUSEG2WE | MPUSEG2XE);

    // Segment 3: Read + Write
    MPUSAM &= ~MPUSEG3XE;
    MPUSAM |= (MPUSEG3RE | MPUSEG3WE | MPUSEG3VS);

    MPUCTL0_H = 0x00;           // Disable access to MPU registers
}

/// Initializes and locks the MPU, ready for jumping into application code
/// This allows for HW protection of the Bootloader area and protected memory
///
/// * Segment 1 = Application Code (Read + Write + Execute)
/// * Segment 2 = Secure area (no access)
/// * Segment 3 = Bootloader + Download area (Read + Execute)
static void MPU_init_app(void) {
    // Enable access to MPU registers
    MPUCTL0 = MPUPW;

    //  Border 1 = Start of secure memory
    MPUSEGB1 = (0xA000) >> 4;
    //  Border 2 = Start of bootloader
    MPUSEGB2 = (0xC000) >> 4;

    // Segment 1: Read + Write
    MPUSAM &= ~MPUSEG1VS;
    MPUSAM |= (MPUSEG1RE | MPUSEG1WE | MPUSEG1XE);

    // Segment 2 (secure area) is protected from read, write and exec
    MPUSAM &= ~(MPUSEG2WE | MPUSEG2WE | MPUSEG2XE);
    MPUSAM |= MPUSEG2VS;

    // Segment 3 (bootloader) is protected from write
    MPUSAM &= ~MPUSEG3WE;
    MPUSAM |= (MPUSEG3RE | MPUSEG3XE);

    // Enable MPU protection and disable access to MPU registers
    MPUCTL0 = MPUPW | MPUENA;
}
