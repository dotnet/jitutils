project(coredistools)

set(LLVM_LINK_COMPONENTS
  AllTargetsAsmParsers
  AllTargetsDescs
  AllTargetsDisassemblers
  AllTargetsInfos
  MC
  Support)

set(SOURCES coredistools.cpp)

if (MSVC)
  add_compile_options(/guard:cf>)
  add_link_options(/guard:cf)
endif()

set(LLVM_EXPORTED_SYMBOL_FILE ${CMAKE_CURRENT_SOURCE_DIR}/coredistools.exports)

include_directories(${CMAKE_CURRENT_SOURCE_DIR})

add_llvm_library(coredistools SHARED ${SOURCES} DEPENDS intrinsics_gen)

install(TARGETS coredistools LIBRARY DESTINATION bin COMPONENT coredistools
                             ARCHIVE DESTINATION lib COMPONENT coredistools)
