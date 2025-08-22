using DB2XL.Integration.Tests;

// Create a sample database using the existing SampleDatabaseGenerator
var dbPath = @"C:\code\DB2XL\TestData\mcp_demo.db";

Console.WriteLine("🔧 Creating sample database for MCP testing...");
var actualPath = SampleDatabaseGenerator.CreateSampleDatabase(dbPath);

Console.WriteLine($"✅ Sample database created at: {actualPath}");
Console.WriteLine("📊 Ready for MCP server testing!");

// List the tables in the database
using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={actualPath}");
connection.Open();

using var cmd = connection.CreateCommand();
cmd.CommandText = "SELECT name, type FROM sqlite_master WHERE type IN ('table', 'view') ORDER BY type, name";
using var reader = cmd.ExecuteReader();

Console.WriteLine("\n📋 Database objects:");
string lastType = "";
while (reader.Read())
{
    var type = reader.GetString(1);
    if (type != lastType)
    {
        Console.WriteLine($"\n{type}s:");
        lastType = type;
    }
    Console.WriteLine($"  - {reader.GetString(0)}");
}