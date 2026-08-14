#pragma once

// libobs normally generates this header during its own CMake build.  The
// WebCamKu plugin only consumes the public source API, so the install paths are
// sufficient for compiling against the matching OBS source tag.
#define OBS_INSTALL_PREFIX "."
#define OBS_DATA_PATH "data/obs-studio"
#define OBS_PLUGIN_PATH "obs-plugins/64bit"
#define OBS_PLUGIN_DESTINATION "obs-plugins/64bit"
