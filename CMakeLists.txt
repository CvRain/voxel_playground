cmake_minimum_required(VERSION 3.16)

project(voxel_playground_gdextension LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)
set(CMAKE_EXPORT_COMPILE_COMMANDS ON)

if(NOT EXISTS "${CMAKE_SOURCE_DIR}/godot-cpp/CMakeLists.txt")
    message(FATAL_ERROR "godot-cpp submodule is missing. Run: git submodule update --init --recursive")
endif()

if(NOT GODOTCPP_API_VERSION)
    set(GODOTCPP_API_VERSION "4.6" CACHE STRING "Target Godot API version" FORCE)
endif()

add_subdirectory(godot-cpp)

function(voxel_map_godot_platform OUT_VAR OUT_EXT)
    if(CMAKE_SYSTEM_NAME STREQUAL "Linux")
        set(_platform "linux")
        set(_ext ".so")
    elseif(CMAKE_SYSTEM_NAME STREQUAL "Windows")
        set(_platform "windows")
        set(_ext ".dll")
    elseif(CMAKE_SYSTEM_NAME STREQUAL "Darwin")
        set(_platform "macos")
        set(_ext ".dylib")
    else()
        message(FATAL_ERROR "Unsupported platform for GDExtension output: ${CMAKE_SYSTEM_NAME}")
    endif()

    set(${OUT_VAR} "${_platform}" PARENT_SCOPE)
    set(${OUT_EXT} "${_ext}" PARENT_SCOPE)
endfunction()

function(voxel_map_godot_arch OUT_VAR)
    string(TOLOWER "${CMAKE_SYSTEM_PROCESSOR}" _processor)

    if(_processor MATCHES "^(x86_64|amd64)$")
        set(_arch "x86_64")
    elseif(_processor MATCHES "^(aarch64|arm64)$")
        set(_arch "arm64")
    elseif(_processor STREQUAL "riscv64")
        set(_arch "rv64")
    elseif(_processor MATCHES "^(x86|i[3-6]86)$")
        set(_arch "x86_32")
    else()
        message(FATAL_ERROR "Unsupported architecture for GDExtension output: ${CMAKE_SYSTEM_PROCESSOR}")
    endif()

    set(${OUT_VAR} "${_arch}" PARENT_SCOPE)
endfunction()

voxel_map_godot_platform(VOXEL_GDEXT_PLATFORM VOXEL_GDEXT_LIB_EXT)
voxel_map_godot_arch(VOXEL_GDEXT_ARCH)

set(VOXEL_GDEXTENSION_OUTPUT_DIR "${CMAKE_SOURCE_DIR}/project/bin")
set(VOXEL_GDEXTENSION_ENTRY_SYMBOL "voxel_playground_library_init")

set(VOXEL_GDEXTENSION_NAME_EXTRA "")
if(GODOTCPP_DEV_BUILD)
    string(APPEND VOXEL_GDEXTENSION_NAME_EXTRA ".dev")
endif()
if(GODOTCPP_PRECISION STREQUAL "double")
    string(APPEND VOXEL_GDEXTENSION_NAME_EXTRA ".double")
endif()
if(NOT GODOTCPP_THREADS)
    string(APPEND VOXEL_GDEXTENSION_NAME_EXTRA ".nothreads")
endif()

set(VOXEL_GDEXTENSION_DEBUG_LIBRARY
    "res://bin/libvoxel_playground.${VOXEL_GDEXT_PLATFORM}.template_debug${VOXEL_GDEXTENSION_NAME_EXTRA}.${VOXEL_GDEXT_ARCH}${VOXEL_GDEXT_LIB_EXT}"
)
set(VOXEL_GDEXTENSION_RELEASE_LIBRARY
    "res://bin/libvoxel_playground.${VOXEL_GDEXT_PLATFORM}.template_release${VOXEL_GDEXTENSION_NAME_EXTRA}.${VOXEL_GDEXT_ARCH}${VOXEL_GDEXT_LIB_EXT}"
)

configure_file(
    "${CMAKE_SOURCE_DIR}/cmake/voxel_playground.gdextension.in"
    "${VOXEL_GDEXTENSION_OUTPUT_DIR}/voxel_playground.gdextension"
    @ONLY
)

if(CMAKE_EXPORT_COMPILE_COMMANDS)
    execute_process(
        COMMAND
            "${CMAKE_COMMAND}" -E create_symlink
            "${CMAKE_BINARY_DIR}/compile_commands.json"
            "${CMAKE_SOURCE_DIR}/compile_commands.json"
    )
endif()

add_library(voxel_playground SHARED)

target_sources(
    voxel_playground
    PRIVATE
        src/register_types.cpp
        src/voxel_extension.cpp
        src/register_types.h
        src/voxel_extension.h
)

target_include_directories(voxel_playground PRIVATE "${CMAKE_SOURCE_DIR}/src")
target_link_libraries(voxel_playground PRIVATE godot-cpp)

get_target_property(GODOTCPP_SUFFIX godot-cpp GODOTCPP_SUFFIX)

set_target_properties(
    voxel_playground
    PROPERTIES
        CXX_VISIBILITY_PRESET ${GODOTCPP_SYMBOL_VISIBILITY}
        POSITION_INDEPENDENT_CODE ON
        BUILD_RPATH_USE_ORIGIN ON
        LIBRARY_OUTPUT_DIRECTORY "$<1:${VOXEL_GDEXTENSION_OUTPUT_DIR}>"
        RUNTIME_OUTPUT_DIRECTORY "$<1:${VOXEL_GDEXTENSION_OUTPUT_DIR}>"
        PDB_OUTPUT_DIRECTORY "$<1:${VOXEL_GDEXTENSION_OUTPUT_DIR}>"
        PREFIX "lib"
        OUTPUT_NAME "voxel_playground${GODOTCPP_SUFFIX}"
    )

message(STATUS "GDExtension manifest: ${VOXEL_GDEXTENSION_OUTPUT_DIR}/voxel_playground.gdextension")
message(STATUS "Debug library path: ${VOXEL_GDEXTENSION_DEBUG_LIBRARY}")
message(STATUS "Release library path: ${VOXEL_GDEXTENSION_RELEASE_LIBRARY}")
