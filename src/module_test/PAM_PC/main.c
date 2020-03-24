#include <stdio.h>
#include <stdlib.h>
#include <stdint.h>

int main()
{
    uint16_t VTm;
    for(VTm = 0;VTm <= 10000;VTm += 10){
        uint8_t sleepT = 0;
        uint8_t activeT = 0;
        int32_t PAM_ERR = PAM_calculate(VTm, &sleepT,&activeT);
        printf("VTm = %5d; SleepTime = %4d, ActiveTime = %4d, Error message = %d\n",VTm,sleepT,activeT,PAM_ERR);
    }
    return 0;
}
