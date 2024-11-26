import struct
import os
import sys
import json

def replace_text_in_binary(input_file, translation_file, output_file):
    try:
        with open(translation_file, 'r', encoding='utf-8') as f:
            translations = json.load(f)
    except Exception as e:
        print(f"Error reading translation file: {e}")
        return False

    try:
        with open(input_file, 'rb') as f:
            binary_data = bytearray(f.read())
    except Exception as e:
        print(f"Error reading input binary file: {e}")
        return False

    # Find the last 0xFF 0x00 0x00 sequence
    last_index = binary_data.rfind(b'\xff\x00\x00')
    if last_index == -1:
        print("Could not find last 0xFF 0x00 0x00 sequence.")
        return False

    start_search = last_index + 3
    sorted_translations = sorted(translations, key=lambda x: x['offset'])
    total_replaced = 0
    total_length_change = 0

    for translation in sorted_translations:
        orig_text = translation.get('orig', '')
        trans_text = translation.get('trans', '')
        original_offset = translation.get('offset')

        # Skip translations without text or with special markers
        if not trans_text or '[' in orig_text:
            continue

        try:
            # Encode texts with null termination
            encoded_orig = orig_text.encode('cp932') + b'\x00\x00'
            encoded_trans = trans_text.encode('cp932') + b'\x00\x00'
            
            # Dynamically adjust segment start based on previous replacements
            segment_start = start_search + original_offset + total_length_change
            
            # More robust text matching
            current_text = binary_data[segment_start:segment_start + len(encoded_orig)]
            decoded_current = current_text.decode('cp932', errors='replace').rstrip('\x00')
            
            # Strict matching
            if decoded_current != orig_text:
                print(f"Text mismatch at offset {original_offset}. Expected '{orig_text}', found '{decoded_current}'. Skipping.")
                continue

            # Calculate length difference
            length_diff = len(encoded_trans) - len(encoded_orig)
            
            # Replace text
            binary_data[segment_start:segment_start + len(encoded_orig)] = encoded_trans

            # Update total length change
            total_length_change += length_diff

            # Update offsets for subsequent entries
            for entry in sorted_translations:
                pos = entry['pos_offset']
                try:
                    current_offset = struct.unpack('<H', binary_data[pos:pos + 2])[0]
                    new_offset = current_offset
                    if entry['offset'] > original_offset:
                        new_offset += length_diff
                    binary_data[pos:pos + 2] = struct.pack('<H', new_offset)
                except struct.error:
                    print(f"Error updating offset at {pos}. Skipping.")
                    continue

            total_replaced += 1
            print(f"Replaced '{orig_text}' -> '{trans_text}'")

        except Exception as e:
            print(f"Error processing translation for '{orig_text}': {e}")
            continue

    try:
        with open(output_file, 'wb') as f:
            f.write(binary_data)
        print(f"Output written to {output_file}")
        print(f"Total texts replaced: {total_replaced}")
        print(f"Total length change: {total_length_change}")
        return True
    except Exception as e:
        print(f"Error writing output file: {e}")
        return False

def main():
    if len(sys.argv) != 4:
        print("Usage: python script.py <input_binary_file> <translation_json_file> <output_binary_file>")
        sys.exit(1)

    input_file, translation_file, output_file = sys.argv[1], sys.argv[2], sys.argv[3]

    if not os.path.exists(input_file) or not os.path.exists(translation_file):
        print("Input or translation file not found.")
        sys.exit(1)

    if replace_text_in_binary(input_file, translation_file, output_file):
        sys.exit(0)
    else:
        sys.exit(1)

if __name__ == "__main__":
    main()
