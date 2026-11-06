extends SceneTree

func _initialize():
    var args = OS.get_cmdline_user_args()
    if args.size() != 1:
        push_error("A pack output path is required")
        quit(1)
        return
    var packer = PCKPacker.new()
    var error = packer.pck_start(args[0])
    if error == OK:
        error = add_directory(packer, "res://resources/ShoujoKagekiAijoCore", "res://ShoujoKagekiAijoCore")
    if error == OK:
        error = add_directory(packer, "res://src/Modules", "res://src/Modules", true)
    if error == OK:
        error = packer.flush()
    print("Core resource pack result ", error)
    quit(0 if error == OK else 1)

func add_directory(packer: PCKPacker, source: String, destination: String, scripts_only: bool = false) -> Error:
    for file in DirAccess.get_files_at(source):
        if scripts_only and not file.ends_with(".cs"):
            continue
        var original = source.path_join(file)
        if not scripts_only and FileAccess.file_exists(original + ".import"):
            continue
        var result = packer.add_file(destination.path_join(file), original)
        if result != OK:
            return result
        if file.ends_with(".import"):
            var config = ConfigFile.new()
            result = config.load(original)
            if result != OK:
                return result
            for dependency in config.get_value("deps", "dest_files", PackedStringArray()):
                result = packer.add_file(dependency, dependency)
                if result != OK:
                    return result
    for directory in DirAccess.get_directories_at(source):
        if directory in ["bin", "obj", ".godot"]:
            continue
        var result = add_directory(packer, source.path_join(directory), destination.path_join(directory), scripts_only)
        if result != OK:
            return result
    return OK
