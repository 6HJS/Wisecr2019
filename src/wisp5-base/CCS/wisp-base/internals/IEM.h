/*
 * IEM.h
 *
 *  Created on: 2018Äê9ÔÂ28ÈÕ
 *      Author: Yang
 */

#ifndef INTERNALS_IEM_H_
#define INTERNALS_IEM_H_

#define ACTIVE_S 1
#define SLEEP_S 0
#include "../wisp-base.h"


inline void EnterPAM(uint8_t sleepT, uint8_t activeT);
inline void BWIEM(void);
inline void INIEM(void);
inline void ExitPAM(void);

typedef struct {
    uint16_t activetime;
    uint16_t sleeptime;
    uint8_t currentState;
    uint8_t oneTimeInt;

}IEMstruct;


#endif /* INTERNALS_IEM_H_ */
