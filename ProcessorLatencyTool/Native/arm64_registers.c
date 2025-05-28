#include <stdint.h>

#ifdef __aarch64__

uint64_t read_tpidr_el0(void) {
    uint64_t value;
    __asm__ volatile(
        "isb \n"
        "mrs %0, TPIDR_EL0 \n"
        "isb \n"
        : "=r"(value)
    );
    return value;
}

uint64_t read_cntvct_el0(void) {
    uint64_t value;
    __asm__ volatile(
        "isb \n"
        "mrs %0, CNTVCT_EL0 \n"
        "isb \n"
        : "=r"(value)
    );
    return value;
}

uint64_t read_cntfrq_el0(void) {
    uint64_t value;
    __asm__ volatile(
        "isb \n"
        "mrs %0, CNTFRQ_EL0 \n"
        "isb \n"
        : "=r"(value)
    );
    return value;
}

#else

uint64_t read_tpidr_el0(void) {
    return 0;
}

uint64_t read_cntvct_el0(void) {
    return 0;
}

uint64_t read_cntfrq_el0(void) {
    return 0;
}

#endif 