cmake_minimum_required(VERSION 3.20)
project(pjsip_wrapper C)

set(CMAKE_C_STANDARD 11)

# PJSIP root (set by setup.ps1)
if(NOT DEFINED PJSIP_DIR)
    set(PJSIP_DIR "$ENV{PJSIP_DIR}" CACHE PATH "PJSIP source directory")
endif()

if(NOT EXISTS "${PJSIP_DIR}/pjlib/include/pj/types.h")
    message(FATAL_ERROR "PJSIP_DIR not set or invalid: ${PJSIP_DIR}")
endif()

# Include directories
set(PJSIP_INCLUDE_DIRS
    ${PJSIP_DIR}/pjlib/include
    ${PJSIP_DIR}/pjlib-util/include
    ${PJSIP_DIR}/pjnath/include
    ${PJSIP_DIR}/pjmedia/include
    ${PJSIP_DIR}/pjsip/include
)

# Find all PJSIP static libraries
file(GLOB PJSIP_LIBS_DEBUG "${PJSIP_DIR}/lib/*-x86_64-x64-vc*-Debug.lib")
file(GLOB PJSIP_LIBS_RELEASE "${PJSIP_DIR}/lib/*-x86_64-x64-vc*-Release.lib")

# Fallback: try without Debug/Release suffix
if(NOT PJSIP_LIBS_RELEASE)
    file(GLOB PJSIP_LIBS_RELEASE "${PJSIP_DIR}/lib/*.lib")
endif()

add_library(pjsip_wrapper SHARED pjsip_wrapper.c)

target_include_directories(pjsip_wrapper PRIVATE ${PJSIP_INCLUDE_DIRS})

target_link_libraries(pjsip_wrapper PRIVATE
    $<$<CONFIG:Debug>:${PJSIP_LIBS_DEBUG}>
    $<$<CONFIG:Release>:${PJSIP_LIBS_RELEASE}>
    ws2_32 ole32 oleaut32 uuid winmm dsound
    mswsock advapi32 gdi32 user32 iphlpapi
)

target_compile_definitions(pjsip_wrapper PRIVATE
    PJ_WIN32=1
    PJ_WIN64=1
    PJMEDIA_HAS_SRTP=0
    PJ_IS_BIG_ENDIAN=0
    PJ_IS_LITTLE_ENDIAN=1
    _CRT_SECURE_NO_WARNINGS
    WRAPPER_EXPORTS
)

set_target_properties(pjsip_wrapper PROPERTIES
    RUNTIME_OUTPUT_DIRECTORY_DEBUG "${CMAKE_BINARY_DIR}/bin"
    RUNTIME_OUTPUT_DIRECTORY_RELEASE "${CMAKE_BINARY_DIR}/bin"
)

# Install rule
install(TARGETS pjsip_wrapper
    RUNTIME DESTINATION bin
    LIBRARY DESTINATION lib
)
