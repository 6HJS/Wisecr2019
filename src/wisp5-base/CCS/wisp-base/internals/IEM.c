/*
 * IEM.c
 *
 *  Created on: 2018Äê9ÔÂ28ÈÕ
 *      Author: Yang
 */

//================================================================IEM

#include "IEM.h"
#include <stdbool.h>

IEMstruct IEMctx;

#pragma vector=TIMER3_A0_VECTOR
__interrupt void TIMER3_A0_ISR(void){
    if(IEMctx.currentState == ACTIVE_S){                                                                //active currently
        // Use Timer 3A for a short delay to accumulate power===============
        TA3R = 0;                               //reset counter
        TA3CCTL0 = CCIE;                        // TACCR0 interrupt enabled
        TA3CCR0 = IEMctx.sleeptime;
        TA3CTL = TASSEL__ACLK | MC__UP;
        IEMctx.currentState = SLEEP_S;
        //==================================================================
        __bis_SR_register_on_exit(LPM3_bits | GIE); //enter sleep mode on exit of interrupt routine.
    }else{                                                                                  //sleep currently
        // Use Timer 3A for a short period for code execution===============
        TA3R = 0;                               //reset counter
        TA3CCTL0 = CCIE;                        // TACCR0 interrupt enabled
        TA3CCR0 = IEMctx.activetime;
        TA3CTL = TASSEL__ACLK | MC__UP;
        IEMctx.currentState = ACTIVE_S;
        //==================================================================
        __bic_SR_register_on_exit(LPM3_bits);
        __bis_SR_register_on_exit(GIE); //enter global interrupt on exit of interrupt routine.
    }
}

#pragma vector=TIMER0_B0_VECTOR
__interrupt void TIMER0_B0_ISR(void){
    TB0CTL &= ~(MC__UP);                    // Stop Timer
    TB0CCTL0 &= !CCIE; ; // disable Timer0B7 timer
    __no_operation();                         // For debugger
    __bic_SR_register_on_exit(LPM3_bits);
}
//REF for blockWrite
inline void BWIEM(void){
    TB0R = 1;
    TB0CCTL0 = CCIE;                        // TACCR0 interrupt enabled
    TB0CCR0 = 100;
    TB0CTL = TBSSEL__ACLK | MC__UP | TBCLR | TBIE;
    //==================================================================
    __bis_SR_register(LPM3_bits | GIE); //enter sleep mode on exit of interrupt routine.
    __no_operation();                         // For debugger
}

// REF for inventory
inline void INIEM(void){
    TB0R = 1;
    TB0CCTL0 = CCIE;                        // TACCR0 interrupt enabled
    TB0CCR0 = 100;
    TB0CTL = TBSSEL__ACLK | MC__UP | TBCLR | TBIE;
    //==================================================================
    __bis_SR_register(LPM3_bits | GIE); //enter sleep mode on exit of interrupt routine.
    __no_operation();                         // For debugger
}

//Test Touch IEM time
inline void TTIEM(void){
    IEMctx.oneTimeInt = true;
    TB0R = 1;
    TB0CCTL0 = CCIE;                        // TACCR0 interrupt enabled
    TB0CCR0 = 1000; //Drift ratio 31.6/30
    TB0CTL = TBSSEL__ACLK | MC__UP | TBCLR | TBIE;
    //==================================================================
    __bis_SR_register(LPM3_bits | GIE); //enter sleep mode on exit of interrupt routine.
    __no_operation();                         // For debugger
}

// 1 unit = 1 mS
inline void EnterPAM(uint8_t sleepT, uint8_t activeT){

    if(sleepT == 0)
        return;
    IEMctx.sleeptime = sleepT*10;
    IEMctx.activetime = activeT*10;
    IEMctx.currentState = SLEEP_S; //sleep first.
    // Use Timer 3A for a short delay to accumulate power===============
    TA3R = 0;                               //reset counter
    TA3CCTL0 = CCIE;                        // TACCR0 interrupt enabled
    TA3CCR0 = IEMctx.sleeptime;
    TA3CTL = TASSEL__ACLK | MC__UP;
    //==================================================================
    __bis_SR_register(LPM3_bits |GIE); //enter global interrupt on exit of interrupt routine.

}

inline void ExitPAM(void){
    TA3CTL &= ~(MC__UP);                    // Stop Timer
    TA3CCTL0 &= !CCIE; ; // disable Timer3 A0 timer
}

