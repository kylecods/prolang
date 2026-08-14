cmake_minimum_required(VERSION 3.16)
project(prl_runtime VERSION 1.0.0 LANGUAGES C)

set(CMAKE_C_STANDARD 11)
set(CMAKE_C_STANDARD_REQUIRED ON)

# -----------------------------------------------------------------------
# Build type default
# -----------------------------------------------------------------------
if(NOT CMAKE_BUILD_TYPE)
    set(CMAKE_BUILD_TYPE Release CACHE STRING "Build type" FORCE)
endif()

# -----------------------------------------------------------------------
# Library type: default to STATIC; override with -DBUILD_SHARED_LIBS=ON
# -----------------------------------------------------------------------
option(BUILD_SHARED_LIBS "Build as a shared library (.dll / .so)" OFF)

# -----------------------------------------------------------------------
# Sources
# -----------------------------------------------------------------------
set(PRL_SOURCES
    base.c
    arena.c
    strings.c
    vector.c
)

set(PRL_HEADERS
    base.h
    arena.h
    strings.h
    vector.h
    prl_runtime.h
)

# -----------------------------------------------------------------------
# Library target
# -----------------------------------------------------------------------
add_library(prl_runtime ${PRL_SOURCES})

# Public include directory so consumers only need #include "prl_runtime.h"
target_include_directories(prl_runtime
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}>
        $<INSTALL_INTERFACE:include/prl_runtime>
)

# -----------------------------------------------------------------------
# Platform-specific settings
# -----------------------------------------------------------------------
if(WIN32)
    target_compile_definitions(prl_runtime PRIVATE
        _CRT_SECURE_NO_WARNINGS
        WIN32_LEAN_AND_MEAN
    )

    if(BUILD_SHARED_LIBS)
        # Export all symbols automatically on Windows (MSVC / MinGW)
        set_target_properties(prl_runtime PROPERTIES
            WINDOWS_EXPORT_ALL_SYMBOLS ON
        )
    endif()

    # Link against standard Windows runtime libs (no-op on MinGW)
    if(MSVC)
        target_compile_options(prl_runtime PRIVATE /W4)
    else()
        target_compile_options(prl_runtime PRIVATE -Wall -Wextra)
    endif()

elseif(UNIX)
    target_compile_options(prl_runtime PRIVATE -Wall -Wextra -Wpedantic)

    if(BUILD_SHARED_LIBS)
        # Shared libraries on Linux need position-independent code
        set_target_properties(prl_runtime PROPERTIES
            POSITION_INDEPENDENT_CODE ON
        )
    endif()
endif()

# -----------------------------------------------------------------------
# Output naming
# -----------------------------------------------------------------------
set_target_properties(prl_runtime PROPERTIES
    OUTPUT_NAME    "prl_runtime"
    DEBUG_POSTFIX  "d"
    VERSION        ${PROJECT_VERSION}
    SOVERSION      ${PROJECT_VERSION_MAJOR}   # Linux .so versioning
)

# -----------------------------------------------------------------------
# Install rules
# -----------------------------------------------------------------------
include(GNUInstallDirs)

install(TARGETS prl_runtime
    ARCHIVE DESTINATION ${CMAKE_INSTALL_LIBDIR}   # .lib / .a
    LIBRARY DESTINATION ${CMAKE_INSTALL_LIBDIR}   # .so
    RUNTIME DESTINATION ${CMAKE_INSTALL_BINDIR}   # .dll (Windows)
)

install(FILES ${PRL_HEADERS}
    DESTINATION ${CMAKE_INSTALL_INCLUDEDIR}/prl_runtime
)

# -----------------------------------------------------------------------
# Summary
# -----------------------------------------------------------------------
message(STATUS "---------------------------------------------------")
message(STATUS "  Project   : ${PROJECT_NAME} v${PROJECT_VERSION}")
message(STATUS "  Platform  : ${CMAKE_SYSTEM_NAME}")
message(STATUS "  Build type: ${CMAKE_BUILD_TYPE}")
message(STATUS "  Library   : ${BUILD_SHARED_LIBS} (shared=ON, static=OFF)")
message(STATUS "---------------------------------------------------")