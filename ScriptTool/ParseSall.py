import struct
import os
import sys
import json

def extract_text_segments(file_path):
    try:
        with open(file_path, 'rb') as file:
            data = file.read()
        
        # Find start and end markers
        start_offset = data.find(b'\x6E')
        last_marker = data.rfind(b'\xff\x00\x00')
        
        if start_offset == -1 or last_marker == -1:
            print("Unable to find necessary markers.")
            return []
        
        segments = []
        current_pos = start_offset
        
        while current_pos < len(data) - 4:
            # Look for offset patterns
            if data[current_pos:current_pos+2] == b'\x00\x01':
                offset = struct.unpack('<H', data[current_pos+2:current_pos+4])[0]
                segment_start = last_marker + 3 + offset
                
                # Try to determine segment length
                next_offset_pos = data.find(b'\x00\x01', current_pos + 4)
                if next_offset_pos != -1:
                    next_offset = struct.unpack('<H', data[next_offset_pos+2:next_offset_pos+4])[0]
                    segment_length = next_offset - offset
                else:
                    segment_length = len(data) - segment_start
                
                # Extract and decode segment
                try:
                    raw_segment = data[segment_start:segment_start+segment_length]
                    text = raw_segment.decode('cp932', errors='replace').strip('\x00')
                    
                    # Basic Japanese text validation
                    if any('\u3040' <= char <= '\u309F' or  # Hiragana
                           '\u30A0' <= char <= '\u30FF' or  # Katakana
                           '\u4E00' <= char <= '\u9FFF' for char in text):
                        segments.append({
                            "pos_offset": current_pos + 2,
                            "offset": offset,
                            "orig": text,
                            "trans": text
                        })
                
                except Exception as e:
                    print(f"Error processing segment at offset {offset}: {e}")
                
                current_pos += 4
            else:
                current_pos += 1
        
        return segments
    
    except Exception as e:
        print(f"Extraction error: {e}")
        return []

def main():
    # Improved argument handling
    if len(sys.argv) < 3:
        print("Usage: python Parse.py <input_binary_file> <output_json_file>")
        sys.exit(1)
    
    input_file = sys.argv[1]
    output_file = sys.argv[2]
    
    # Validate input file
    if not os.path.exists(input_file):
        print(f"Input file not found: {input_file}")
        sys.exit(1)
    
    # Extract and save text segments
    try:
        text_segments = extract_text_segments(input_file)
        
        with open(output_file, 'w', encoding='utf-8') as f:
            json.dump(text_segments, f, indent=2, ensure_ascii=False)
        
        print(f"Extracted {len(text_segments)} text segments to {output_file}")
    
    except Exception as e:
        print(f"Error during extraction and saving: {e}")
        sys.exit(1)

if __name__ == "__main__":
    main()
