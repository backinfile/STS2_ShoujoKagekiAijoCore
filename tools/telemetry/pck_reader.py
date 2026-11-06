"""Read and verify unencrypted Godot v3 PCK entries."""
import hashlib
import struct


def pck_index(path):
    with path.open("rb") as stream:
        header = stream.read(112)
        magic, version = struct.unpack_from("<II", header)
        if magic != 0x43504447 or version != 3:
            raise ValueError("Unsupported PCK format")
        flags = struct.unpack_from("<I", header, 20)[0]
        if flags & 1:
            raise ValueError("Encrypted directory is unsupported")
        base, directory = struct.unpack_from("<QQ", header, 24)
        stream.seek(directory)
        count = struct.unpack("<I", stream.read(4))[0]
        entries = {}
        for _ in range(count):
            length = struct.unpack("<I", stream.read(4))[0]
            if length > 65536:
                raise ValueError("Invalid entry name length")
            name = stream.read(length).rstrip(b"\0").decode("utf-8")
            offset, size = struct.unpack("<QQ", stream.read(16))
            digest = stream.read(16).hex()
            entry_flags = struct.unpack("<I", stream.read(4))[0]
            entries[name.removeprefix("res://")] = {"offset": offset + base, "size": size, "md5": digest, "flags": entry_flags}
        return entries


def pck_read(path, entry):
    if entry["flags"] & 1:
        raise ValueError("Encrypted entry is unsupported")
    with path.open("rb") as stream:
        stream.seek(entry["offset"])
        data = stream.read(entry["size"])
    if hashlib.md5(data).hexdigest() != entry["md5"]:
        raise ValueError("PCK content digest mismatch")
    return data
