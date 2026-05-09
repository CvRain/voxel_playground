#include "voxel_extension.h"

#include <godot_cpp/core/class_db.hpp>

namespace godot {

void VoxelExtension::_bind_methods() {
    ClassDB::bind_method(D_METHOD("add", "lhs", "rhs"), &VoxelExtension::add);
    ClassDB::bind_method(D_METHOD("get_build_info"), &VoxelExtension::get_build_info);
}

int64_t VoxelExtension::add(int64_t lhs, int64_t rhs) const {
    return lhs + rhs;
}

String VoxelExtension::get_build_info() const {
    return String("voxel_playground gdextension");
}

} // namespace godot
