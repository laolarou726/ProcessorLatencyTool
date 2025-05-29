#include <stdint.h>
#include <pthread.h>
#ifdef __APPLE__
#include <mach/mach.h>
#include <mach/thread_policy.h>
#include <mach/thread_act.h>
#endif

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

#ifdef __APPLE__
int set_realtime_policy(void) {
    thread_port_t thread = mach_thread_self();
    if (thread == MACH_PORT_NULL) {
        return -1;
    }

    thread_extended_policy_data_t extendedPolicy;
    extendedPolicy.timeshare = 0; // Set to realtime mode

    kern_return_t kr = thread_policy_set(
        thread,
        THREAD_EXTENDED_POLICY,
        (thread_policy_t)&extendedPolicy,
        THREAD_EXTENDED_POLICY_COUNT
    );

    if (kr != KERN_SUCCESS) {
        mach_port_deallocate(mach_task_self(), thread);
        return -2;
    }

    // Set to maximum priority
    thread_precedence_policy_data_t precedencePolicy;
    precedencePolicy.importance = 63; // Maximum importance

    kr = thread_policy_set(
        thread,
        THREAD_PRECEDENCE_POLICY,
        (thread_policy_t)&precedencePolicy,
        THREAD_PRECEDENCE_POLICY_COUNT
    );

    mach_port_deallocate(mach_task_self(), thread);
    
    if (kr != KERN_SUCCESS) {
        return -3;
    }

    return 0;
}

int get_thread_policy(int* is_realtime, int* importance) {
    thread_port_t thread = mach_thread_self();
    if (thread == MACH_PORT_NULL) {
        return -1;
    }

    thread_extended_policy_data_t extendedPolicy;
    mach_msg_type_number_t extendedCount = THREAD_EXTENDED_POLICY_COUNT;
    boolean_t get_default = FALSE;

    kern_return_t kr = thread_policy_get(
        thread,
        THREAD_EXTENDED_POLICY,
        (thread_policy_t)&extendedPolicy,
        &extendedCount,
        &get_default
    );

    if (kr != KERN_SUCCESS) {
        mach_port_deallocate(mach_task_self(), thread);
        return -2;
    }

    thread_precedence_policy_data_t precedencePolicy;
    mach_msg_type_number_t precedenceCount = THREAD_PRECEDENCE_POLICY_COUNT;

    kr = thread_policy_get(
        thread,
        THREAD_PRECEDENCE_POLICY,
        (thread_policy_t)&precedencePolicy,
        &precedenceCount,
        &get_default
    );

    mach_port_deallocate(mach_task_self(), thread);

    if (kr != KERN_SUCCESS) {
        return -3;
    }

    *is_realtime = extendedPolicy.timeshare ? 0 : 1;
    *importance = precedencePolicy.importance;

    return 0;
}
#else
int set_realtime_policy(void) {
    return -1;
}

int get_thread_policy(int* is_realtime, int* importance) {
    *is_realtime = 0;
    *importance = 0;
    return -1;
}
#endif

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

int set_realtime_policy(void) {
    return -1;
}

int get_thread_policy(int* is_realtime, int* importance) {
    *is_realtime = 0;
    *importance = 0;
    return -1;
}

#endif 