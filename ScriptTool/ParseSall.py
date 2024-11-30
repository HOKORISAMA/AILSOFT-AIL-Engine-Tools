#encoding:cp932
import json
import re
import struct
import argparse
import os
import glob

def extract(file_path):
    texts = []
    with open(file_path, 'rb') as file:
        content = file.read()

    # 1st uint16 reserved, 2nd uint16 reserved, 3rd reserved [size of command] after header, 4rth uint16 [size of offset_and_command_table after command], 5th uint16 [size of string_table], 6th uint16 reverved.
    # Unpack header information
    header = struct.unpack("<HHHHHH", content[:12])
    
    # Calculate key section positions
    offset_table_start = 12 + header[2]
    string_table_start = offset_table_start + header[3]
    
    # Extract string data
    string_data = content[string_table_start:string_table_start + header[4]]
    segments = re.split(b'\x00{2,}', string_data)

    for idx, segment in enumerate(segments):
        try:
            text = re.sub(r'[\u0000-\u001F\u007F-\u009F]', '', segment.decode('shift_jis'))
            if text:
                entry = {
                    "orig": text,
                    "trans": "",
                    "length": len(segment) + 2
                }
                
                if text.startswith('【'):
                    entry['name_marker'] = True
                    entry['name_pos'] = sum(len(s) + 2 for s in segments[:idx])
                
                texts.append(entry)
        except UnicodeDecodeError:
            continue
    
    return texts

def locate_offsets(file_path, texts):
    with open(file_path, 'rb') as file:
        content = file.read()
    
    header = struct.unpack("<HHHHHH", content[:12])
    offset_table_start = 12 + header[2]
    offset_table_end = offset_table_start + header[3]
    offset_table = content[offset_table_start:offset_table_end]
    
    offsets = []
    accumulated_length = 0
    previous_offset = 0

    for entry in texts:
        search_length = accumulated_length + entry['length']
        accumulated_length += entry['length']
        search_bytes = search_length.to_bytes(2, byteorder='little')
        
        offset = offset_table.find(search_bytes)
        while offset != -1:
            # Check if byte before the offset is 00 or 01 or 110
            if offset > 0:
                prev_byte = offset_table[offset-1]
                if prev_byte not in [0, 1, 110]:
                    offset = offset_table.find(search_bytes, offset + 1)
                    continue
            
            if offset > previous_offset:
                final_offset = offset + offset_table_start
                offsets.append(final_offset)
                previous_offset = offset
                break
            
            offset = offset_table.find(search_bytes, offset + 1)
        else:
            offsets.append(-1)
    
    return offsets

def process_file(input_file, output_file):
    texts = extract(input_file)
    offsets = locate_offsets(input_file, texts)

    result = []
    cumulative_length = 0
    for text, offset in zip(texts, offsets):
        cumulative_length += text['length']
        entry = {
            "pos_offset": offset,
            "offset": cumulative_length,
            "orig": text['orig'],
            "trans": text['orig']
        }
        
        if text.get('name_marker', False):
            entry.update({
                'name_marker': True,
                'name_pos': text['name_pos']
            })
        
        result.append(entry)

    with open(output_file, 'w', encoding='utf-8') as f:
        json.dump(result, f, ensure_ascii=False, indent=2)

    print(f"Processed {input_file} -> {output_file}")

def main():
    parser = argparse.ArgumentParser(description="Extract bilingual text from binary files in a directory")
    parser.add_argument('input_dir', help="Path to input directory")
    parser.add_argument('output_dir', help="Path to output directory")
    args = parser.parse_args()

    input_dir = args.input_dir
    output_dir = args.output_dir
    os.makedirs(output_dir, exist_ok=True)

    for input_file in glob.glob(os.path.join(input_dir, '*')):
        filename = os.path.basename(input_file)
        output_file = os.path.join(output_dir, f"{os.path.splitext(filename)[0]}.json")
        process_file(input_file, output_file)

if __name__ == "__main__":
    main()
