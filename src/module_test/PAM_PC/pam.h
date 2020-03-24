#ifndef PAM_H_INCLUDED
#define PAM_H_INCLUDED

    #ifdef __cplusplus
    extern "C" {
    #endif

#include <math.h>
#include <stdint.h>

enum pam_err{PAM_GOOD = 0,
    PAM_VT_TOOLO = -1,
    PAM_VT_TOOHI = -2,

    PAM_TSL_OF = -3, //sleep time overflow
    PAM_TAC_OF = -4, //active time overflow
    PAM_TSL_UF = -5, //sleep time underflow
    PAM_TAC_UF = -6 //active time underflow
};
typedef enum pam_err pam_err_t;

/*
* This function calculate SLEEP and ACTIVE time based on received tag VT
* Parameter:
* In- (const uint16_t) VTm: tag voltage in mV
* Out- (uint8_t) sleepT: sleep time resolution 10 ms
* Out- (uint8_t) activeT: active time resolution 10 ms
*/
pam_err_t PAM_calculate(const uint16_t VTm, uint8_t *sleepT,uint8_t *activeT);


    #ifdef __cplusplus
    }
    #endif
#endif // PAM_H_INCLUDED
