extends SceneTree

func _initialize():
    var args = OS.get_cmdline_user_args()
    if args.size() != 1 or not ProjectSettings.load_resource_pack(args[0]):
        quit(1)
        return
    var root = "res://ShoujoKagekiAijoCore/"
    var texture = load(root + "images/ui/giraffe.png") as Texture2D
    var portrait = load(root + "images/cards/empty_shell.png") as Texture2D
    var audio = load(root + "audio/karen_revue.ogg") as AudioStream
    var catalog = JSON.parse_string(FileAccess.get_file_as_string(root + "audio/music_player/catalog.json"))
    var valid = texture != null and portrait != null and audio != null and catalog is Array and catalog.size() > 0
    valid = valid and FileAccess.file_exists(root + "localization/zhs/gameplay_ui.json")
    valid = valid and FileAccess.file_exists("res://src/Modules/Presentation/NTopBarShinePileButton.cs")
    print("Core resources verified ", valid)
    quit(0 if valid else 1)
