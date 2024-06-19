#This code can be used to extarct AIL Game Engine Archives namely: Gall0.dat(Graphics file), Vall00-n(Ogg files) and many more.
import os
import io
import argparse
from typing import List

class Entry:
    def __init__(self):
        self.Name = ""
        self.Offset = 0
        self.Size = 0
        self.IsPacked = False
        self.Type = ""
        self.UnpackedSize = 0

class PackedEntry(Entry):
    pass

class ArcFile:
    def __init__(self, file, formatter, dir):
        self.File = file
        self.Formatter = formatter
        self.Dir = dir

class ArcView:
    def __init__(self, file_path):
        self.Name = os.path.basename(file_path)
        with open(file_path, "rb") as f:
            self.View = bytearray(f.read())
        self.MaxOffset = len(self.View)

def TryOpen(file_path):
    arc_view = ArcView(file_path)
    count = read_int32(arc_view.View, 0)
    if not IsSaneCount(count):
        return None
    dir = ReadIndex(arc_view, 4, count)
    if dir is None:
        return None
    return ArcFile(arc_view, None, dir)

def OpenEntry(arc, entry):
    input_stream = io.BytesIO(arc.File.View[entry.Offset:entry.Offset + entry.Size])
    if not isinstance(entry, PackedEntry) or not entry.IsPacked:
        return input_stream
    with input_stream:
        data = bytearray(entry.UnpackedSize)
        LzssUnpack(input_stream, data)
        return io.BytesIO(data)

def ReadIndex(arc_view, index_offset, count):
    base_name = os.path.splitext(os.path.basename(arc_view.Name))[0]
    offset = index_offset + count * 4
    if offset >= arc_view.MaxOffset:
        return None
    dir_entries = []
    for i in range(count):
        size = read_uint32(arc_view.View, index_offset)
        if size != 0 and size != 0xFFFFFFFF:
            entry = PackedEntry()
            entry.Name = f"{base_name}#{i:05d}"
            entry.Offset = offset
            entry.Size = size
            entry.IsPacked = False
            if not CheckPlacement(entry, arc_view.MaxOffset):
                return None
            dir_entries.append(entry)
            offset += size
        index_offset += 4
    if len(dir_entries) == 0 or (arc_view.MaxOffset - offset) > 0x80000:
        return None
    DetectFileTypes(arc_view, dir_entries)
    return dir_entries

def DetectFileTypes(arc_view, dir_entries):
    preview = bytearray(16)
    sign_buf = bytearray(4)
    for entry in dir_entries:
        extra = 6
        if extra > entry.Size:
            continue
        signature = read_uint32(arc_view.View, entry.Offset)
        if signature & 0xFFFF == 1:
            entry.IsPacked = True
            entry.UnpackedSize = read_uint32(arc_view.View, entry.Offset + 2)
        elif signature == 0 or arc_view.View[entry.Offset + 4:entry.Offset + 8] == b"OggS":
            extra = 4
        entry.Offset += extra
        entry.Size -= extra
        if entry.IsPacked:
            preview = arc_view.View[entry.Offset:entry.Offset + len(preview)]
            with io.BytesIO(preview) as input_stream:
                LzssUnpack(input_stream, sign_buf)
                signature = int.from_bytes(sign_buf, byteorder='little')
        else:
            signature = read_uint32(arc_view.View, entry.Offset)
        if signature != 0:
            SetEntryType(entry, signature)

def SetEntryType(entry, signature):
    if signature == 0xBA010000:
        entry.Type = "video"
        entry.Name = os.path.splitext(entry.Name)[0] + ".mpg"
    else:
        res = DetectFileType(signature)
        if res is not None:
            entry.ChangeType(res)

def LzssUnpack(input_stream, output):
    frame_pos = 0xfee
    frame = bytearray(0x1000)
    for i in range(frame_pos):
        frame[i] = 0x20
    dst = 0
    ctl = 0

    while dst < len(output):
        ctl >>= 1
        if ctl & 0x100 == 0:
            ctl = input_stream.read(1)
            if ctl == b"":
                break
            ctl = ctl[0] | 0xff00  # Convert bytes to int and apply bitwise OR
        if ctl & 0x1 == 0:
            v = input_stream.read(1)
            if v == b"":
                break
            output[dst] = v[0]
            frame[frame_pos] = v[0]
            frame_pos = (frame_pos + 1) & 0xfff
            dst += 1
        else:
            offset = input_stream.read(1)
            if offset == b"":
                break
            count = input_stream.read(1)
            if count == b"":
                break
            offset = offset[0] | ((count[0] & 0xf0) << 4)
            count = (count[0] & 0x0f) + 3

            for _ in range(count):
                if dst >= len(output):
                    break
                v = frame[offset]
                offset = (offset + 1) & 0xfff
                frame[frame_pos] = v
                frame_pos = (frame_pos + 1) & 0xfff
                output[dst] = v
                dst += 1

def IsSaneCount(count):
    return count > 0 and count < 400000  # Example sanity check, adjust as needed

def CheckPlacement(entry, max_offset):
    return entry.Offset + entry.Size <= max_offset

def DetectFileType(signature):
    # Example function to detect file type from signature
    if signature == 0x89504E47:
        return "png"
    elif signature == 0x47494638:
        return "gif"
    elif signature == 0x25504446:
        return "pdf"
    return None

def read_int32(data, offset):
    return int.from_bytes(data[offset:offset + 4], byteorder='little', signed=True)

def read_uint32(data, offset):
    return int.from_bytes(data[offset:offset + 4], byteorder='little', signed=False)

def unpack_archive(input_file, output_dir):
    arc_file = TryOpen(input_file)
    if arc_file is None:
        print(f"Failed to open {input_file}")
        return
    
    if not os.path.exists(output_dir):
        os.makedirs(output_dir)
    
    for entry in arc_file.Dir:
        output_file_path = os.path.join(output_dir, entry.Name)
        with open(output_file_path, "wb") as output_file:
            entry_data = OpenEntry(arc_file, entry).read()
            output_file.write(entry_data)
            print(f"Extracted {entry.Name}")

def main():
    parser = argparse.ArgumentParser(description="Unpack archive files to a directory.")
    parser.add_argument("input_file", help="Path to the input archive file.")
    parser.add_argument("output_dir", help="Directory to extract the files to.")
    args = parser.parse_args()

    unpack_archive(args.input_file, args.output_dir)

if __name__ == "__main__":
    main()
