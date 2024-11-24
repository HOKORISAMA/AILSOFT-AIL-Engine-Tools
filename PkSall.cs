using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public class AIL
{
    public static void Pack(string inputDir, string outputFile, bool compress = false)
    {
        try
        {
            var files = Directory.GetFiles(inputDir);
            if (files.Length == 0)
            {
                Console.WriteLine("No files to pack.");
                return;
            }

            using (var outputStream = new MemoryStream())
            {
                var fileInfos = new List<(string name, int offset, int size, bool isPacked, int unpackedSize)>();
                int indexOffset = 4 + files.Length * 4;
                outputStream.Seek(indexOffset, SeekOrigin.Begin);

                foreach (var filePath in files)
                {
                    var fileData = File.ReadAllBytes(filePath);
                    var fileName = Path.GetFileName(filePath);
                    var currentOffset = (int)outputStream.Position;

                    if (compress)
                    {
                        // Write compression header
                        outputStream.WriteByte(1);  // Signature low byte
                        outputStream.WriteByte(0);  // Signature high byte
                        outputStream.Write(BitConverter.GetBytes(fileData.Length), 0, 4);  // Original size
                        
                        var compressedData = CustomLzss.Compress(fileData);
                        outputStream.Write(compressedData, 0, compressedData.Length);
                        
                        var finalSize = compressedData.Length + 6;  // Add header size
                        fileInfos.Add((fileName, currentOffset, finalSize, true, fileData.Length));
                        
                        Console.WriteLine($"Packed {fileName} - Original: {fileData.Length:N0} bytes, Final: {finalSize:N0} bytes");
                    }
                    else
                    {
                        outputStream.Write(fileData, 0, fileData.Length);
                        fileInfos.Add((fileName, currentOffset, fileData.Length, false, fileData.Length));
                        
                        Console.WriteLine($"Stored {fileName} - Size: {fileData.Length:N0} bytes");
                    }
                }

                // Write the index
                outputStream.Seek(0, SeekOrigin.Begin);
                outputStream.Write(BitConverter.GetBytes(fileInfos.Count), 0, 4);
                foreach (var info in fileInfos)
                {
                    outputStream.Write(BitConverter.GetBytes(info.size), 0, 4);
                }

                // Write to output file
                File.WriteAllBytes(outputFile, outputStream.ToArray());
                Console.WriteLine($"\nSuccessfully packed {files.Length} files into {outputFile}");
                Console.WriteLine($"Total size: {new FileInfo(outputFile).Length:N0} bytes");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }

    public static void Main(string[] args)
    {
        try
        {
            if (args.Length != 2 && args.Length != 3)
            {
                Console.WriteLine("Usage: program.exe <input_dir> <output_file> [compress:true/false]");
                return;
            }

            string inputDir = args[0];
            string outputFile = args[1];
            bool compress = args.Length == 3 ? bool.Parse(args[2]) : false;

            Pack(inputDir, outputFile, compress);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An unexpected error occurred: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }
}
