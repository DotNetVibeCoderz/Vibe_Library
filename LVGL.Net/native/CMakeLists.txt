cmake_minimum_required(VERSION 3.20)
project(lvglnet C)

#
# Builds a single shared library, "lvglnet", containing upstream LVGL plus the
# ABI-safe shim in shim/. One artifact means one DllImport name on the managed
# side and no load-order problems between two co-dependent shared libraries.
#
# Usage:
#   cmake -S native -B native/build -DCMAKE_BUILD_TYPE=Release
#   cmake --build native/build --config Release
#
# The result is copied to runtimes/<rid>/native/ so the .NET projects pick it
# up through the NativeAssets item group in LVGL.Net.csproj.
#

set(CMAKE_C_STANDARD 11)
set(CMAKE_C_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

if(NOT CMAKE_BUILD_TYPE AND NOT CMAKE_CONFIGURATION_TYPES)
    set(CMAKE_BUILD_TYPE Release CACHE STRING "" FORCE)
endif()

option(LVGLNET_WITH_DEMOS "Compile the bundled LVGL demos (widgets, benchmark)" ON)
set(LVGL_TAG "v9.2.2" CACHE STRING "Git tag of the LVGL release to build against")

# ---------------------------------------------------------------------------
# LVGL sources
# ---------------------------------------------------------------------------
# A local checkout (native/lvgl) wins over the network so offline and CI builds
# are reproducible; otherwise clone the pinned tag.
#
# A plain git clone rather than FetchContent: the single-argument
# FetchContent_Populate this needs was deprecated in CMake 3.30 and removed in
# 4.0, and FetchContent_MakeAvailable is wrong here because we compile LVGL's
# sources into our own target instead of using its CMake project.
set(lvgl_SOURCE_DIR "${CMAKE_CURRENT_SOURCE_DIR}/lvgl")

if(EXISTS "${lvgl_SOURCE_DIR}/lvgl.h")
    message(STATUS "lvglnet: using existing LVGL checkout at ${lvgl_SOURCE_DIR}")
else()
    find_package(Git REQUIRED)
    message(STATUS "lvglnet: cloning LVGL ${LVGL_TAG}")

    execute_process(
        COMMAND "${GIT_EXECUTABLE}" clone --depth 1 --branch ${LVGL_TAG}
                https://github.com/lvgl/lvgl.git "${lvgl_SOURCE_DIR}"
        RESULT_VARIABLE lvgl_clone_result
        OUTPUT_VARIABLE lvgl_clone_output
        ERROR_VARIABLE  lvgl_clone_output)

    if(NOT lvgl_clone_result EQUAL 0)
        message(FATAL_ERROR
            "Could not clone LVGL ${LVGL_TAG}:\n${lvgl_clone_output}\n"
            "Clone it manually into ${lvgl_SOURCE_DIR} and re-run CMake.")
    endif()

    if(NOT EXISTS "${lvgl_SOURCE_DIR}/lvgl.h")
        message(FATAL_ERROR "LVGL was cloned but ${lvgl_SOURCE_DIR}/lvgl.h is missing.")
    endif()
endif()

# LVGL guards every optional source file with its own LV_USE_* macro, so a
# blanket glob compiles to nothing for the features lv_conf.h leaves off.
file(GLOB_RECURSE LVGL_SOURCES CONFIGURE_DEPENDS "${lvgl_SOURCE_DIR}/src/*.c")

set(LVGL_DEMO_SOURCES "")
if(LVGLNET_WITH_DEMOS)
    file(GLOB_RECURSE LVGL_DEMO_SOURCES CONFIGURE_DEPENDS
        "${lvgl_SOURCE_DIR}/demos/widgets/*.c"
        "${lvgl_SOURCE_DIR}/demos/benchmark/*.c"
        "${lvgl_SOURCE_DIR}/demos/*.c")
endif()

# ---------------------------------------------------------------------------
# The library
# ---------------------------------------------------------------------------
add_library(lvglnet SHARED
    ${LVGL_SOURCES}
    ${LVGL_DEMO_SOURCES}
    shim/lvglnet_shim.c)

target_include_directories(lvglnet PRIVATE
    "${lvgl_SOURCE_DIR}"
    "${lvgl_SOURCE_DIR}/src"
    "${lvgl_SOURCE_DIR}/demos"
    "${CMAKE_CURRENT_SOURCE_DIR}"
    "${CMAKE_CURRENT_SOURCE_DIR}/shim")

# LV_CONF_INCLUDE_SIMPLE makes lv_conf_internal.h do a plain #include "lv_conf.h",
# resolved through the include path above. The alternative, LV_CONF_PATH, is
# stringified by LVGL itself, so the value must reach the compiler without quotes -
# which no amount of CMake escaping achieves reliably across shells and generators.
target_compile_definitions(lvglnet PRIVATE LV_CONF_INCLUDE_SIMPLE)

if(LVGLNET_WITH_DEMOS)
    target_compile_definitions(lvglnet PRIVATE LVGLNET_WITH_DEMOS=1)
endif()

# MSVC does not export symbols without __declspec(dllexport); LVGL's headers
# carry no export macros, so let CMake synthesise a .def for the whole library.
set_target_properties(lvglnet PROPERTIES
    WINDOWS_EXPORT_ALL_SYMBOLS ON
    C_VISIBILITY_PRESET default
    OUTPUT_NAME lvglnet)

if(MSVC)
    target_compile_options(lvglnet PRIVATE /W3 /wd4244 /wd4267 $<$<CONFIG:Release>:/O2>)
else()
    target_compile_options(lvglnet PRIVATE -Wall -Wno-unused-function $<$<CONFIG:Release>:-O2>)
    target_link_libraries(lvglnet PRIVATE m)
endif()

# Cortex-A72 on the Pi 4 - only applied when the user asked for it explicitly,
# because these flags make the binary non-portable to other boards.
option(LVGLNET_TUNE_PI4 "Tune codegen for the Raspberry Pi 4 (Cortex-A72)" OFF)
if(LVGLNET_TUNE_PI4 AND NOT MSVC)
    target_compile_options(lvglnet PRIVATE -mcpu=cortex-a72 -mtune=cortex-a72)
endif()

# ---------------------------------------------------------------------------
# Stage into runtimes/<rid>/native
# ---------------------------------------------------------------------------
if(NOT LVGLNET_RID)
    if(WIN32)
        set(LVGLNET_RID "win-x64")
    elseif(APPLE)
        if(CMAKE_SYSTEM_PROCESSOR MATCHES "arm64|aarch64")
            set(LVGLNET_RID "osx-arm64")
        else()
            set(LVGLNET_RID "osx-x64")
        endif()
    else()
        if(CMAKE_SYSTEM_PROCESSOR MATCHES "aarch64|arm64")
            set(LVGLNET_RID "linux-arm64")
        elseif(CMAKE_SYSTEM_PROCESSOR MATCHES "arm")
            set(LVGLNET_RID "linux-arm")
        else()
            set(LVGLNET_RID "linux-x64")
        endif()
    endif()
endif()

set(LVGLNET_STAGE "${CMAKE_CURRENT_SOURCE_DIR}/../runtimes/${LVGLNET_RID}/native")
add_custom_command(TARGET lvglnet POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E make_directory "${LVGLNET_STAGE}"
    COMMAND ${CMAKE_COMMAND} -E copy_if_different $<TARGET_FILE:lvglnet> "${LVGLNET_STAGE}"
    COMMENT "Staging lvglnet into runtimes/${LVGLNET_RID}/native")

message(STATUS "lvglnet: runtime identifier = ${LVGLNET_RID}")
