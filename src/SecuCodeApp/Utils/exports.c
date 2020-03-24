#include "exports.h"

#include "../../PAM_PC/pam.h"

UITLS_API uint32_t compute_pam(const uint16_t VTm, uint8_t* sleepT, uint8_t* activeT)
{
	return (uint32_t)PAM_calculate(VTm, sleepT, activeT);
}
