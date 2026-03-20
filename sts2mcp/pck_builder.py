"""Pure Python Godot PCK file builder for STS2 modding.

Creates valid .pck files compatible with Godot 4.5.1 (format v2).
Handles .tscn scenes (as-is), .png images (converted to .ctex), .json/.tres (as-is).
"""

import hashlib
import os
import struct
from pathlib import Path
from typing import BinaryIO


# PCK Format Constants
PCK_MAGIC = 0x43504447  # "GDPC" little-endian
PCK_FORMAT_VERSION = 2  # Compatible with Godot 4.0+
GODOT_MAJOR = 4
GODOT_MINOR = 5
GODOT_PATCH = 1
PCK_FLAGS = 0  # No encryption, absolute offsets
PCK_ALIGNMENT = 32


def _pad_to_alignment(f: BinaryIO, alignment: int = PCK_ALIGNMENT):
    """Write zero padding to reach alignment boundary."""
    pos = f.tell()
    padding = (alignment - (pos % alignment)) % alignment
    if padding > 0:
        f.write(b'\x00' * padding)


def _md5_of_bytes(data: bytes) -> bytes:
    return hashlib.md5(data).digest()


def _encode_path(path: str) -> bytes:
    """Encode a PCK file path (without res:// prefix) as padded UTF-8."""
    # Paths in PCK use forward slashes, no res:// prefix
    path = path.replace('\\', '/')
    if path.startswith('res://'):
        path = path[6:]
    path_bytes = path.encode('utf-8') + b'\x00'
    # Pad to 4-byte alignment
    pad_len = (4 - (len(path_bytes) % 4)) % 4
    path_bytes += b'\x00' * pad_len
    return path_bytes


def png_to_ctex(png_path: str) -> bytes:
    """Convert a PNG file to Godot .ctex format (GST2 header wrapping PNG data)."""
    png_data = Path(png_path).read_bytes()

    # Parse PNG header for dimensions
    if png_data[:8] != b'\x89PNG\r\n\x1a\n':
        raise ValueError(f"Not a valid PNG file: {png_path}")

    # IHDR chunk starts at offset 8, length at 8-12, type at 12-16, data at 16+
    ihdr_data = png_data[16:24]
    width = struct.unpack('>I', ihdr_data[0:4])[0]
    height = struct.unpack('>I', ihdr_data[4:8])[0]

    # GST2 header
    header = struct.pack('<4sIIII',
        b'GST2',           # magic
        1,                 # version
        width,             # texture width
        height,            # texture height
        0,                 # flags
    )
    header += struct.pack('<i', -1)  # mipmap limit (-1 = none)
    header += b'\x00' * 12  # reserved (3 uint32s)

    # Image data header
    image_header = struct.pack('<IHHII',
        1,                 # data_format = 1 (PNG)
        width & 0xFFFF,    # image width (uint16)
        height & 0xFFFF,   # image height (uint16)
        0,                 # mipmap count (0 = base only)
        5,                 # pixel format = RGBA8
    )

    chunk = struct.pack('<I', len(png_data)) + png_data
    return header + image_header + chunk


def generate_import_file(original_path: str, ctex_path: str) -> str:
    """Generate a .import remap file for a texture."""
    # Generate a simple deterministic UID from the path
    uid_hash = hashlib.md5(original_path.encode()).hexdigest()[:12]
    return f"""[remap]

importer="texture"
type="CompressedTexture2D"
uid="uid://{uid_hash}"
path="res://{ctex_path}"
metadata={{
"vram_texture": false
}}
"""


class PckEntry:
    """A single file entry in a PCK archive."""
    def __init__(self, pck_path: str, data: bytes):
        self.pck_path = pck_path.replace('\\', '/')
        if self.pck_path.startswith('res://'):
            self.pck_path = self.pck_path[6:]
        self.data = data
        self.md5 = _md5_of_bytes(data)
        self.offset = 0  # Set during write


def build_pck(
    source_dir: str,
    output_path: str,
    base_prefix: str = "",
    convert_pngs: bool = True,
) -> dict:
    """Build a PCK file from a directory.

    Args:
        source_dir: Directory containing mod assets
        output_path: Output .pck file path
        base_prefix: Prefix for all paths in PCK (e.g., "MyMod/" so files appear at res://MyMod/)
        convert_pngs: If True, convert .png files to .ctex format with .import remaps

    Returns:
        Dict with stats about the build
    """
    source = Path(source_dir)
    if not source.exists():
        return {"success": False, "error": f"Source directory not found: {source_dir}"}

    entries: list[PckEntry] = []
    stats = {"scenes": 0, "textures": 0, "other": 0, "total_files": 0, "total_bytes": 0}

    # Collect all files
    for file_path in sorted(source.rglob('*')):
        if not file_path.is_file():
            continue
        if file_path.name.startswith('.'):
            continue

        rel_path = str(file_path.relative_to(source)).replace('\\', '/')
        pck_path = f"{base_prefix}{rel_path}" if base_prefix else rel_path

        ext = file_path.suffix.lower()

        if ext == '.png' and convert_pngs:
            # Convert PNG to .ctex and create .import remap
            try:
                ctex_data = png_to_ctex(str(file_path))
                ctex_filename = file_path.stem + ".png-" + hashlib.md5(pck_path.encode()).hexdigest()[:8] + ".ctex"
                ctex_pck_path = f".godot/imported/{ctex_filename}"

                # Add the .ctex file
                entries.append(PckEntry(ctex_pck_path, ctex_data))

                # Add the .import remap
                import_content = generate_import_file(pck_path, ctex_pck_path)
                entries.append(PckEntry(pck_path + ".import", import_content.encode('utf-8')))

                # Also add the raw PNG (some code paths use it)
                raw_data = file_path.read_bytes()
                entries.append(PckEntry(pck_path, raw_data))

                stats["textures"] += 1
            except Exception as e:
                # Fallback: just add raw PNG
                raw_data = file_path.read_bytes()
                entries.append(PckEntry(pck_path, raw_data))
                stats["other"] += 1

        elif ext == '.tscn':
            data = file_path.read_bytes()
            entries.append(PckEntry(pck_path, data))
            stats["scenes"] += 1

        else:
            # .json, .tres, .cfg, etc. — pack as-is
            data = file_path.read_bytes()
            entries.append(PckEntry(pck_path, data))
            stats["other"] += 1

    if not entries:
        return {"success": False, "error": "No files found to pack"}

    stats["total_files"] = len(entries)

    # Write PCK file
    output = Path(output_path)
    output.parent.mkdir(parents=True, exist_ok=True)

    with open(output, 'wb') as f:
        # ─── Header (96 bytes for format v2) ───
        f.write(struct.pack('<I', PCK_MAGIC))
        f.write(struct.pack('<I', PCK_FORMAT_VERSION))
        f.write(struct.pack('<I', GODOT_MAJOR))
        f.write(struct.pack('<I', GODOT_MINOR))
        f.write(struct.pack('<I', GODOT_PATCH))
        f.write(struct.pack('<I', PCK_FLAGS))
        f.write(struct.pack('<Q', 0))  # file_base (will update)
        f.write(b'\x00' * 64)  # 16 reserved uint32s

        # ─── File data ───
        _pad_to_alignment(f, PCK_ALIGNMENT)
        file_base = f.tell()

        for entry in entries:
            _pad_to_alignment(f, PCK_ALIGNMENT)
            entry.offset = f.tell() - file_base
            f.write(entry.data)
            stats["total_bytes"] += len(entry.data)

        # ─── Directory ───
        _pad_to_alignment(f, PCK_ALIGNMENT)
        f.write(struct.pack('<I', len(entries)))

        for entry in entries:
            path_bytes = _encode_path(entry.pck_path)
            f.write(struct.pack('<I', len(path_bytes)))
            f.write(path_bytes)
            f.write(struct.pack('<Q', entry.offset))  # offset (relative to file_base)
            f.write(struct.pack('<Q', len(entry.data)))  # size
            f.write(entry.md5)  # 16 bytes MD5
            f.write(struct.pack('<I', 0))  # flags

        # ─── Update file_base in header ───
        f.seek(24)
        f.write(struct.pack('<Q', file_base))

    stats["success"] = True
    stats["output"] = str(output)
    stats["output_size"] = output.stat().st_size
    return stats


def list_pck_contents(pck_path: str) -> dict:
    """Read and list the contents of a PCK file."""
    p = Path(pck_path)
    if not p.exists():
        return {"error": f"PCK file not found: {pck_path}"}

    with open(p, 'rb') as f:
        magic = struct.unpack('<I', f.read(4))[0]
        if magic != PCK_MAGIC:
            return {"error": "Not a valid PCK file"}

        fmt_version = struct.unpack('<I', f.read(4))[0]
        major = struct.unpack('<I', f.read(4))[0]
        minor = struct.unpack('<I', f.read(4))[0]
        patch = struct.unpack('<I', f.read(4))[0]
        flags = struct.unpack('<I', f.read(4))[0]
        file_base = struct.unpack('<Q', f.read(8))[0]

        if fmt_version >= 3:
            dir_offset = struct.unpack('<Q', f.read(8))[0]
            f.read(64)  # reserved
            f.seek(dir_offset)
        else:
            f.read(64)  # reserved
            # For format v2, directory is after all file data.
            # We wrote it right after the last file data block (aligned).
            # Scan from file_base forward: read file data entries to find directory.
            # Simpler approach: the directory starts after all data. We can find it
            # by seeking from the end of the file backwards, or by knowing our layout.
            # Our layout: [header 96][pad][file data...][pad][directory]
            # The directory has file_count as first uint32. We need to find it.
            # Strategy: seek to end - try reading the last portion as directory.
            # Actually, just scan from after header+file_base area.
            # For files we wrote, we know the pattern. Let's try scanning for the directory.
            # Skip to just past the file_base offset by scanning each file.
            # Easiest: the PCK we create puts directory right after data, so seek
            # past all aligned data blocks. We'll just try sequential scan.
            # For robustness, scan backwards from EOF for a valid file_count.
            f.seek(0, 2)
            eof = f.tell()
            # Try positions from file_base forward, aligned
            pos = file_base
            found = False
            while pos < eof - 4:
                f.seek(pos)
                candidate = struct.unpack('<I', f.read(4))[0]
                if 0 < candidate < 100000:  # reasonable file count
                    # Try to validate by reading first entry
                    try:
                        path_len = struct.unpack('<I', f.read(4))[0]
                        if 0 < path_len < 4096:
                            f.seek(pos)
                            found = True
                            break
                    except:
                        pass
                pos += PCK_ALIGNMENT
            if not found:
                return {"error": "Could not locate directory in PCK"}

        file_count = struct.unpack('<I', f.read(4))[0]
        files = []
        for _ in range(file_count):
            path_len = struct.unpack('<I', f.read(4))[0]
            path_bytes = f.read(path_len)
            path = path_bytes.decode('utf-8').rstrip('\x00')
            offset = struct.unpack('<Q', f.read(8))[0]
            size = struct.unpack('<Q', f.read(8))[0]
            md5 = f.read(16).hex()
            entry_flags = struct.unpack('<I', f.read(4))[0]
            files.append({"path": path, "size": size, "offset": offset})

        return {
            "format_version": fmt_version,
            "godot_version": f"{major}.{minor}.{patch}",
            "flags": flags,
            "file_count": file_count,
            "files": files,
        }
