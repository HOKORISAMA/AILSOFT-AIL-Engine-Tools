using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace AIL
{
    class Program
    {
        static int Main(string[] args)
        {
            try
            {
                if (args.Length < 2)
                {
                    ShowUsage();
                    return 1;
                }

                string operation = args[0].ToLower();
                switch (operation)
                {
                    case "-p":
                    case "--pack":
                        return HandlePack(args);
                    case "-u":
                    case "--unpack":
                        return HandleUnpack(args);
                    default:
                        Console.WriteLine($"Error: Unknown operation '{operation}'");
                        ShowUsage();
                        return 1;
                }
            }
            catch (InvalidDataException ex)
            {
                Console.WriteLine($"Error: Invalid archive format - {ex.Message}");
                return 1;
            }
            catch (IOException ex)
            {
                Console.WriteLine($"Error accessing files: {ex.Message}");
                return 1;
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.WriteLine($"Access denied: {ex.Message}");
                return 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error: {ex.Message}");
                return 1;
            }
        }

        private static void ShowUsage()
        {
            Console.WriteLine("AIL Archive Tool");
            Console.WriteLine("Usage:");
            Console.WriteLine("  Pack:   AIL -p|--pack <input_directory> <output_file> [--no-compress]");
            Console.WriteLine("  Unpack: AIL -u|--unpack <input_file> [output_directory]");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  AIL --pack ./myfiles archive.ail");
            Console.WriteLine("  AIL --unpack archive.ail ./extracted");
        }

        private static int HandlePack(string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("Error: Pack operation requires input directory and output file");
                ShowUsage();
                return 1;
            }

            string inputDir = args[1];
            string outputFile = args[2];
            bool compress = !args.Contains("--no-compress", StringComparer.OrdinalIgnoreCase);

            // Validate input directory
            if (!Directory.Exists(inputDir))
            {
                Console.WriteLine($"Error: Input directory '{inputDir}' not found.");
                return 1;
            }

            // Get all files from directory
            string[] files;
            try
            {
                files = Directory.GetFiles(inputDir, "*.*", SearchOption.AllDirectories)
                               .OrderBy(f => f)
                               .ToArray();

                if (files.Length == 0)
                {
                    Console.WriteLine($"Error: No files found in directory '{inputDir}'");
                    return 1;
                }

                // Validate file naming pattern
                var regex = new Regex(@"#(\d{5})", RegexOptions.IgnoreCase);
                var indices = files.Select(f => 
                {
                    var match = regex.Match(Path.GetFileNameWithoutExtension(f));
                    if (!match.Success)
                    {
                        throw new ArgumentException($"Invalid filename format: {f}\nExpected format: basename#XXXXX where XXXXX is a 5-digit number");
                    }
                    return int.Parse(match.Groups[1].Value);
                }).ToList();

                int maxIndex = indices.Max();
                var missingIndices = Enumerable.Range(0, maxIndex + 1)
                                             .Except(indices)
                                             .OrderBy(i => i)
                                             .ToList();

                if (missingIndices.Any())
                {
                    Console.WriteLine("\nWarning: The following file indices are missing and will be treated as placeholders:");
                    foreach (var index in missingIndices)
                    {
                        Console.WriteLine($"  #{index:D5}");
                    }
                    Console.WriteLine();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading directory: {ex.Message}");
                return 1;
            }

            // Ensure output directory exists
            string outputDir = Path.GetDirectoryName(outputFile);
            if (!string.IsNullOrEmpty(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            Console.WriteLine($"Packing directory: {inputDir}");
            Console.WriteLine($"Output file: {outputFile}");
            Console.WriteLine($"Compression: {(compress ? "enabled" : "disabled")}");
            Console.WriteLine($"Files to pack: {files.Length}");

            var archive = new AilArchive();
            var startTime = DateTime.Now;
            Console.Write("Processing... ");

            archive.Pack(files, outputFile, compress);

            var endTime = DateTime.Now;
            var duration = endTime - startTime;
            Console.WriteLine("Done!");
            Console.WriteLine($"Packing completed in {duration.TotalSeconds:F1} seconds");

            return 0;
        }

        private static int HandleUnpack(string[] args)
        {
            string inputFile = args[1];
            string outputDir;

            // Validate input file
            if (!File.Exists(inputFile))
            {
                Console.WriteLine($"Error: Input file '{inputFile}' not found.");
                return 1;
            }

            // Determine output directory
            if (args.Length >= 3)
            {
                outputDir = args[2];
            }
            else
            {
                // Use input filename without extension as output directory
                outputDir = Path.Combine(
                    Path.GetDirectoryName(inputFile) ?? "",
                    Path.GetFileNameWithoutExtension(inputFile)
                );
            }

            // Create output directory if it doesn't exist
            try
            {
                if (!Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating output directory: {ex.Message}");
                return 1;
            }

            Console.WriteLine($"Extracting: {inputFile}");
            Console.WriteLine($"Output to: {outputDir}");

            var archive = new AilArchive();
            var startTime = DateTime.Now;
            Console.Write("Processing... ");

            archive.Unpack(inputFile, outputDir);

            var endTime = DateTime.Now;
            var duration = endTime - startTime;
            Console.WriteLine("Done!");
            Console.WriteLine($"Extraction completed in {duration.TotalSeconds:F1} seconds");

            return 0;
        }
    }
}
