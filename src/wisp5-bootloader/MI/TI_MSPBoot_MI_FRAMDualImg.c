/*
 * \file   TI_MSPBoot_MI_FRAMDualImg.c
 *
 * \brief  Driver for memory interface using FRAM in FR59xx
 *         This file supports Dual Image
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

//
// Include files
//
#include "msp430.h"
#include "TI_MSPBoot_Common.h"
#include "TI_MSPBoot_MI.h"

//
// Local Function prototypes
//
static uint8_t TI_MSPBoot_MI_WriteByteDirect(uint32_t addr, uint8_t data);


/******************************************************************************
 *
 * @brief   Write a Byte to FRAM memory
 *      This function writes the byte to Download area
 *
 * @param  addr     Address of the Byte being written in Application area
 *                  The actual address will be in Download area
 * @param  data     Byte being written
 *
 * @return  RET_OK when sucessful,
 *          RET_PARAM_ERROR if address is outside of Application area
 *****************************************************************************/
uint8_t TI_MSPBoot_MI_WriteByte(uint32_t addr, uint8_t data)
{
#ifdef CONFIG_MI_MEMORY_RANGE_CHECK
    // Check address to be within Application range
	if ((addr < APP_START_ADDR) || (addr > APP_END_ADDR)) {
        return RET_PARAM_ERROR;
    }
#endif

    // Write the byte
    TI_MSPBoot_MI_WriteByteDirect(addr, data);

    return RET_OK;
}


/******************************************************************************
 *
 * @brief   Write a Byte Directly to Flash memory
 *          The bootloader is protected using MPU but all interrupts (except for
 *          Vector) can be reprogrammed
 *
 * @param  addr     Address of the Byte being written in Flash
 * @param  data     Byte being written
 *
 * @return  RET_OK when sucessful,
 *          RET_PARAM_ERROR if address is outside of Application area
 *****************************************************************************/
static uint8_t TI_MSPBoot_MI_WriteByteDirect(uint32_t addr, uint8_t data)
{
    __data20_write_char(addr, data);    // Write to memory

    // Since we need to make use of the interrupts as part of the RFID stack we skip updating
    // them here and instead update them as part of the boot process.
    //if ((addr >= APP_VECTOR_TABLE) && (addr < APP_RESET_VECTOR_ADDR - 2))
    //{
    //    return RET_OK;
    //}

    return RET_OK;
}

void CopyAppISRs(void) {
    uint32_t addr;
    for (addr = APP_VECTOR_TABLE; addr < APP_RESET_VECTOR_ADDR - 2; ++addr) {
        uint32_t target = (addr - APP_VECTOR_TABLE) + BOOT_VECTOR_TABLE;
        __data20_write_char(target, *((uint8_t*)addr));
    }
}

