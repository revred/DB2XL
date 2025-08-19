using DB2XL;
using SqliteXport.Tests;
using System.Diagnostics;

class TestExport
{
    static void Main(string[] args)
    {
        Console.WriteLine("DB2XL Export Test");
        Console.WriteLine(new string('=', 50));

        try
        {
            // Create a sample database
            Console.WriteLine("Creating sample database...");
            var dbPath = SampleDatabaseGenerator.CreateSampleDatabase();
            Console.WriteLine($"Database created: {dbPath}");

            // Set up export path
            var xlsxPath = Path.ChangeExtension(dbPath, ".xlsx");
            Console.WriteLine($"Export target: {xlsxPath}");

            // Configure export options
            var options = new SqliteToExcelOptions
            {
                WriteAllAsText = true,
                IncludeMetadataSheet = true,
                BlobMode = BlobRenderMode.Hex,
                OrderRowsDeterministically = true,
                SplitOversizeSheets = true,
                IncludeViews = true
            };

            Console.WriteLine("\nExport Options:");
            Console.WriteLine($"  Write All As Text: {options.WriteAllAsText}");
            Console.WriteLine($"  Include Metadata: {options.IncludeMetadataSheet}");
            Console.WriteLine($"  BLOB Mode: {options.BlobMode}");
            Console.WriteLine($"  Include Views: {options.IncludeViews}");

            // Perform export
            Console.WriteLine("\nStarting export...");
            var stopwatch = Stopwatch.StartNew();

            SqliteToExcel.Export(dbPath, xlsxPath, options);

            stopwatch.Stop();
            Console.WriteLine($"Export completed in {stopwatch.ElapsedMilliseconds} ms");

            // Verify the export
            var xlsxInfo = new FileInfo(xlsxPath);
            Console.WriteLine($"Excel file size: {xlsxInfo.Length:N0} bytes");

            // Validate the export
            Console.WriteLine("\nValidating export...");
            var validation = ExportValidator.ValidateExport(dbPath, xlsxPath, options.IncludeViews);

            if (validation.IsValid)
            {
                Console.WriteLine("✅ Export validation PASSED!");
                Console.WriteLine($"   Tables exported: {validation.TableResults.Count}");

                foreach (var table in validation.TableResults.OrderBy(kvp => kvp.Key))
                {
                    var val = table.Value;
                    Console.WriteLine($"   {table.Key}: {val.ExpectedRows} rows, {val.ExpectedColumns} cols");
                }
            }
            else
            {
                Console.WriteLine("❌ Export validation FAILED!");
                foreach (var error in validation.Errors)
                {
                    Console.WriteLine($"   Error: {error}");
                }
            }

            Console.WriteLine($"\nFiles created:");
            Console.WriteLine($"  Database: {dbPath}");
            Console.WriteLine($"  Excel: {xlsxPath}");
            Console.WriteLine("\nOpen the Excel file to inspect the export results.");

        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Export failed: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner: {ex.InnerException.Message}");
            }
            Console.WriteLine($"\nStack trace:\n{ex.StackTrace}");
        }

        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }
}