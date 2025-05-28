cmake_minimum_required(VERSION 3.10)
project(arm64_registers)

# Set C standard
set(CMAKE_C_STANDARD 11)
set(CMAKE_C_STANDARD_REQUIRED ON)

# Set output directories
if(APPLE)
    set(CMAKE_LIBRARY_OUTPUT_DIRECTORY ${CMAKE_INSTALL_PREFIX})
    set(CMAKE_RUNTIME_OUTPUT_DIRECTORY ${CMAKE_INSTALL_PREFIX})
endif()

# Add library
add_library(arm64_registers SHARED
    arm64_registers.c
)

# Set output name
if(APPLE)
    set_target_properties(arm64_registers PROPERTIES
        PREFIX "lib"
        SUFFIX ".dylib"
        OUTPUT_NAME "arm64_registers"
    )
endif()

# Installation
install(TARGETS arm64_registers
    LIBRARY DESTINATION ${CMAKE_INSTALL_PREFIX}
    RUNTIME DESTINATION ${CMAKE_INSTALL_PREFIX}
) 