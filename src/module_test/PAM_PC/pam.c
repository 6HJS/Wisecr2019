#include "pam.h"

// const float Vi = 2.14;
// const float Vf = 3.83;
// const float T = 30;
// const float Vbo = 1.8;
// const float C = 52.7 / 1000000.0;
// const float Iactive = 240.0 / 1000000.0;

pam_err_t PAM_calculate(const uint16_t VTm, uint8_t *sleepT,uint8_t *activeT){
    if(VTm < 2140){// invalid VTm reading
        *sleepT = 0;
        *activeT = 0;
        return PAM_VT_TOOLO;
    }
    if(VTm >3850){// invalid VTm reading
        *sleepT = 0;
        *activeT = 0;
        return PAM_VT_TOOHI;
    }


    float Vt = VTm/1000.0; // convert tag voltage form mV to V

    // float Fnow = log((Vf - Vi)/(Vf - Vt)); // coefficient of current capacity
    // float F98 = log((Vf - Vi)/(Vf - 0.98*Vf)); // coefficient of fully charged capacity
    // float sleepTf = (T * (F98/Fnow)/10.0);// in 10 ms resolution
    // *sleepT = (uint8_t)sleepTf;

    // float Q = C * (Vt - Vbo);
    // float activeTf = (Q / (Iactive*10.0))*1000.0;// in 10 ms resolution times 1000 convert s to ms
    // *activeT = (uint8_t)(activeTf);

	if(Vt > 2.393){
		*sleepT = 0;
		*activeT = 255;
	}else if (Vt > 2.183){
		*sleepT = 67;
		*activeT = 16;
	}else if (Vt > 2.143){
		*sleepT = 77;
		*activeT = 7;
	}else{
		*sleepT = 0;
		*activeT = 5;
	}



    if(*sleepT > 255){
        *sleepT = 255; //cap to 255
        return PAM_TSL_OF;
    }
    if(*sleepT < 0){
        *sleepT = 0; //cap to 0
        return PAM_TSL_UF;
    }
    if(*activeT > 255){
        *activeT = 255; //cap to 255
        return PAM_TAC_OF;
    }
    if(*activeT < 0){
        *activeT = 0; //cap to 0
        return PAM_TAC_UF;
    }

    return PAM_GOOD;
}
