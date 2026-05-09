#pragma once

#include <godot_cpp/classes/ref_counted.hpp>

namespace godot {

class VoxelExtension : public RefCounted {
    GDCLASS(VoxelExtension, RefCounted)

protected:
    static void _bind_methods();

public:
    int64_t add(int64_t lhs, int64_t rhs) const;
    String get_build_info() const;
};

} // namespace godot
