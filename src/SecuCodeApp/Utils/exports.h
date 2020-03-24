#ifdef UTILS_EXPORTS
#define UITLS_API __declspec(dllexport)
#else
#define UITLS_API __declspec(dllimport)
#endif

#include <stdint.h>

UITLS_API int compute_pam(const uint16_t VTm, uint8_t* sleepT, uint8_t* activeT);
