#encoding:cp932
import json
import struct

def append_text_with_relative_offsets(binary_file, json_file, output_file):
    try:
        # Read the binary file
        with open(binary_file, 'rb') as f:
            content = f.read()
        
        # Parse the header
        header = struct.unpack("<HHHHHH", content[:12])  
        offset_table_start = 12 + header[2]
        offset_table_end = offset_table_start + header[3]
        offset_table = content[offset_table_start:offset_table_end]
        string_table_len = header[5]
        
        # Read the JSON data
        with open(json_file, 'r', encoding='utf-8') as f:
            replacements = json.load(f)
        
        binary_data = bytearray(content) 
        current_string_table_end = len(binary_data) 
        
        appended_offsets = {}
        
        for entry in replacements:
            orig = entry['orig'].encode('cp932') + b'\x00\x00'
            trans = (entry['trans'].encode('cp932') + b'\x00\x00') if entry.get('trans') else orig
            
            appended_offsets[entry['orig']] = ((current_string_table_end - offset_table_end) + (string_table_len + len(trans)))
            binary_data.extend(trans)
            current_string_table_end += len(trans)
        
        for entry in replacements:
            pos_offset = entry.get('pos_offset') 
            if pos_offset is not None and entry['orig'] in appended_offsets:
                new_relative_offset = appended_offsets[entry['orig']]
                offset_as_uint16 = struct.pack('<H', new_relative_offset) 
                binary_data[pos_offset:pos_offset + 2] = offset_as_uint16
        
        # Additional search and replace for lines starting with 【
        for entry in replacements:
            if entry.get('trans') and entry['orig'].startswith('【'):
                orig_encoded = entry['orig'].encode('cp932') + b'\x00\x00'
                trans_encoded = entry['trans'].encode('cp932') + b'\x00\x00'
                search_results = find_all_occurrences(binary_data, orig_encoded)
                
                for pos in search_results:
                    if len(trans_encoded) <= len(orig_encoded):
                        replacement = trans_encoded.ljust(len(orig_encoded), b'\x00')
                        binary_data[pos:pos+len(orig_encoded)] = replacement
                    else:
                        binary_data[pos:pos+len(trans_encoded)] = trans_encoded[:len(trans_encoded)]
                        
        with open(output_file, 'wb') as f:
            f.write(binary_data)
        
        print(f"Appending with relative offsets and line replacements completed. New file created: {output_file}")
    
    except Exception as e:
        print(f"An error occurred: {e}")

def find_all_occurrences(data, search_bytes):
    """Find all occurrences of search_bytes in data."""
    occurrences = []
    start = 0
    while True:
        pos = data.find(search_bytes, start)
        if pos == -1:
            break
        occurrences.append(pos)
        start = pos + 1
    return occurrences

binary_file = 'sall#00048' 
json_file = 'sall#00048.json'  
output_file = 'sall#00048_modified'

append_text_with_relative_offsets(binary_file, json_file, output_file)
