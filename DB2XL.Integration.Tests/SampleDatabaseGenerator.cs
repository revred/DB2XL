using DB2XL.Core.Models;
using DB2XL.Data.Schema;
using Microsoft.Data.Sqlite;
using System.Text;

namespace DB2XL.Integration.Tests;

public static class SampleDatabaseGenerator
{
    public static string CreateSampleDatabase(string? customPath = null)
    {
        var dbPath = customPath ?? Path.Combine(Path.GetTempPath(), $"sample_{Guid.NewGuid():N}.db");
        
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();

        CreateCustomersTable(connection);
        CreateProductsTable(connection);
        CreateOrdersTable(connection);
        CreateOrderDetailsTable(connection);
        CreateEmployeesTable(connection);
        CreateSpecialCasesTable(connection);
        CreateLargeDataTable(connection);
        CreateUnicodeTable(connection);
        CreateNumericTypesTable(connection);
        CreateBlobTable(connection);
        CreateEmptyTable(connection);
        CreateViewExample(connection);

        SetDatabaseMetadata(connection);

        return dbPath;
    }

    private static void CreateCustomersTable(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE Customers (
                CustomerID INTEGER PRIMARY KEY AUTOINCREMENT,
                CompanyName TEXT NOT NULL,
                ContactName TEXT,
                ContactTitle TEXT,
                Address TEXT,
                City TEXT,
                Region TEXT,
                PostalCode TEXT,
                Country TEXT,
                Phone TEXT,
                Fax TEXT
            );";
        cmd.ExecuteNonQuery();

        cmd.CommandText = @"
            INSERT INTO Customers (CompanyName, ContactName, ContactTitle, Address, City, Region, PostalCode, Country, Phone, Fax)
            VALUES 
                ('Alfreds Futterkiste', 'Maria Anders', 'Sales Representative', 'Obere Str. 57', 'Berlin', NULL, '12209', 'Germany', '030-0074321', '030-0076545'),
                ('Ana Trujillo Emparedados', 'Ana Trujillo', 'Owner', 'Avda. de la Constitución 2222', 'México D.F.', NULL, '05021', 'Mexico', '(5) 555-4729', '(5) 555-3745'),
                ('Antonio Moreno Taquería', 'Antonio Moreno', 'Owner', 'Mataderos 2312', 'México D.F.', NULL, '05023', 'Mexico', '(5) 555-3932', NULL),
                ('Around the Horn', 'Thomas Hardy', 'Sales Representative', '120 Hanover Sq.', 'London', NULL, 'WA1 1DP', 'UK', '(171) 555-7788', '(171) 555-6750'),
                ('Berglunds snabbköp', 'Christina Berglund', 'Order Administrator', 'Berguvsvägen 8', 'Luleå', NULL, 'S-958 22', 'Sweden', '0921-12 34 65', '0921-12 34 67');";
        cmd.ExecuteNonQuery();
    }

    private static void CreateProductsTable(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE Products (
                ProductID INTEGER PRIMARY KEY AUTOINCREMENT,
                ProductName TEXT NOT NULL,
                CategoryID INTEGER,
                QuantityPerUnit TEXT,
                UnitPrice REAL,
                UnitsInStock INTEGER,
                UnitsOnOrder INTEGER,
                ReorderLevel INTEGER,
                Discontinued INTEGER DEFAULT 0
            );";
        cmd.ExecuteNonQuery();

        cmd.CommandText = @"
            INSERT INTO Products (ProductName, CategoryID, QuantityPerUnit, UnitPrice, UnitsInStock, UnitsOnOrder, ReorderLevel, Discontinued)
            VALUES 
                ('Chai', 1, '10 boxes x 20 bags', 18.00, 39, 0, 10, 0),
                ('Chang', 1, '24 - 12 oz bottles', 19.00, 17, 40, 25, 0),
                ('Aniseed Syrup', 2, '12 - 550 ml bottles', 10.00, 13, 70, 25, 0),
                ('Chef Anton''s Cajun Seasoning', 2, '48 - 6 oz jars', 22.00, 53, 0, 0, 0),
                ('Chef Anton''s Gumbo Mix', 2, '36 boxes', 21.35, 0, 0, 0, 1);";
        cmd.ExecuteNonQuery();
    }

    private static void CreateOrdersTable(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE Orders (
                OrderID INTEGER PRIMARY KEY AUTOINCREMENT,
                CustomerID INTEGER,
                EmployeeID INTEGER,
                OrderDate TEXT,
                RequiredDate TEXT,
                ShippedDate TEXT,
                ShipVia INTEGER,
                Freight REAL,
                ShipName TEXT,
                ShipAddress TEXT,
                ShipCity TEXT,
                ShipRegion TEXT,
                ShipPostalCode TEXT,
                ShipCountry TEXT,
                FOREIGN KEY (CustomerID) REFERENCES Customers(CustomerID)
            );";
        cmd.ExecuteNonQuery();

        cmd.CommandText = @"
            INSERT INTO Orders (CustomerID, EmployeeID, OrderDate, RequiredDate, ShippedDate, ShipVia, Freight, ShipName, ShipAddress, ShipCity, ShipCountry)
            VALUES 
                (1, 5, '2024-01-10', '2024-02-07', '2024-01-12', 3, 32.38, 'Alfreds Futterkiste', 'Obere Str. 57', 'Berlin', 'Germany'),
                (2, 6, '2024-01-11', '2024-02-08', '2024-01-13', 1, 11.61, 'Ana Trujillo Emparedados', 'Avda. de la Constitución 2222', 'México D.F.', 'Mexico'),
                (3, 4, '2024-01-12', '2024-02-09', '2024-01-15', 2, 65.83, 'Antonio Moreno Taquería', 'Mataderos 2312', 'México D.F.', 'Mexico');";
        cmd.ExecuteNonQuery();
    }

    private static void CreateOrderDetailsTable(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE OrderDetails (
                OrderID INTEGER NOT NULL,
                ProductID INTEGER NOT NULL,
                UnitPrice REAL NOT NULL,
                Quantity INTEGER NOT NULL,
                Discount REAL DEFAULT 0,
                PRIMARY KEY (OrderID, ProductID),
                FOREIGN KEY (OrderID) REFERENCES Orders(OrderID),
                FOREIGN KEY (ProductID) REFERENCES Products(ProductID)
            );";
        cmd.ExecuteNonQuery();

        cmd.CommandText = @"
            INSERT INTO OrderDetails (OrderID, ProductID, UnitPrice, Quantity, Discount)
            VALUES 
                (1, 1, 18.00, 12, 0),
                (1, 2, 19.00, 10, 0),
                (2, 3, 10.00, 5, 0.05),
                (2, 4, 22.00, 9, 0.05),
                (3, 1, 18.00, 20, 0.1);";
        cmd.ExecuteNonQuery();
    }

    private static void CreateEmployeesTable(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE Employees (
                EmployeeID INTEGER PRIMARY KEY AUTOINCREMENT,
                LastName TEXT NOT NULL,
                FirstName TEXT NOT NULL,
                Title TEXT,
                BirthDate TEXT,
                HireDate TEXT,
                Address TEXT,
                City TEXT,
                Region TEXT,
                PostalCode TEXT,
                Country TEXT,
                HomePhone TEXT,
                Extension TEXT,
                Notes TEXT,
                ReportsTo INTEGER,
                PhotoPath TEXT
            );";
        cmd.ExecuteNonQuery();

        cmd.CommandText = @"
            INSERT INTO Employees (LastName, FirstName, Title, BirthDate, HireDate, City, Country, HomePhone, Extension, ReportsTo)
            VALUES 
                ('Davolio', 'Nancy', 'Sales Representative', '1968-12-08', '1992-05-01', 'Seattle', 'USA', '(206) 555-9857', '5467', 2),
                ('Fuller', 'Andrew', 'Vice President, Sales', '1952-02-19', '1992-08-14', 'Tacoma', 'USA', '(206) 555-9482', '3457', NULL),
                ('Leverling', 'Janet', 'Sales Representative', '1963-08-30', '1992-04-01', 'Kirkland', 'USA', '(206) 555-3412', '3355', 2),
                ('Peacock', 'Margaret', 'Sales Representative', '1958-09-19', '1993-05-03', 'Redmond', 'USA', '(206) 555-8122', '5176', 2),
                ('Buchanan', 'Steven', 'Sales Manager', '1955-03-04', '1993-10-17', 'London', 'UK', '(71) 555-4848', '3453', 2);";
        cmd.ExecuteNonQuery();
    }

    private static void CreateSpecialCasesTable(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE SpecialCases (
                ID INTEGER PRIMARY KEY,
                NullColumn TEXT,
                EmptyString TEXT,
                LongText TEXT,
                SpecialChars TEXT,
                LeadingZeros TEXT,
                ScientificNotation TEXT,
                DateLikeText TEXT,
                BooleanText TEXT,
                JsonText TEXT,
                XmlText TEXT
            );";
        cmd.ExecuteNonQuery();

        var longText = new string('A', 5000) + " Long text content " + new string('Z', 5000);
        
        cmd.CommandText = @"
            INSERT INTO SpecialCases VALUES 
                (1, NULL, '', @longText, 'Tab'||char(9)||'Newline'||char(10)||'Quote''s', '00123', '1.23E+10', '2024-01-01', 'true', '{""key"": ""value""}', '<root><item>test</item></root>'),
                (2, NULL, '', 'Normal text', 'Special: / \ ? * [ ] :', '007', '9.87E-05', '01/01/2024', 'false', '[1,2,3]', '<data/>'),
                (3, 'Not null', '   ', 'Text with ""quotes"" and ''apostrophes''', 'Line1'||char(13)||char(10)||'Line2', '0001', '3.14159265359', '2024-12-31 23:59:59', '1', 'null', '<!-- comment -->');";
        cmd.Parameters.AddWithValue("@longText", longText);
        cmd.ExecuteNonQuery();
    }

    private static void CreateLargeDataTable(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE LargeData (
                ID INTEGER PRIMARY KEY,
                Category TEXT,
                Value REAL,
                Description TEXT,
                Timestamp TEXT
            );";
        cmd.ExecuteNonQuery();

        var random = new Random(42);
        var categories = new[] { "A", "B", "C", "D", "E" };
        
        using var transaction = connection.BeginTransaction();
        cmd.Transaction = transaction;
        for (int i = 1; i <= 1000; i++)
        {
            cmd.CommandText = @"
                INSERT INTO LargeData (ID, Category, Value, Description, Timestamp)
                VALUES (@id, @category, @value, @description, @timestamp);";
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@id", i);
            cmd.Parameters.AddWithValue("@category", categories[random.Next(categories.Length)]);
            cmd.Parameters.AddWithValue("@value", Math.Round(random.NextDouble() * 10000, 2));
            cmd.Parameters.AddWithValue("@description", $"Record {i:D6} - Sample data for testing large exports");
            cmd.Parameters.AddWithValue("@timestamp", DateTime.Now.AddDays(-random.Next(365)).ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    private static void CreateUnicodeTable(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE UnicodeTest (
                ID INTEGER PRIMARY KEY,
                Language TEXT,
                Text TEXT,
                Emoji TEXT
            );";
        cmd.ExecuteNonQuery();

        cmd.CommandText = @"
            INSERT INTO UnicodeTest VALUES 
                (1, 'English', 'Hello World', '😀🌍'),
                (2, 'Chinese', '你好世界', '🇨🇳'),
                (3, 'Japanese', 'こんにちは世界', '🇯🇵'),
                (4, 'Arabic', 'مرحبا بالعالم', '🇸🇦'),
                (5, 'Hebrew', 'שלום עולם', '🇮🇱'),
                (6, 'Russian', 'Привет мир', '🇷🇺'),
                (7, 'Greek', 'Γεια σου κόσμε', '🇬🇷'),
                (8, 'Korean', '안녕하세요 세계', '🇰🇷'),
                (9, 'Hindi', 'नमस्ते दुनिया', '🇮🇳'),
                (10, 'Mixed', '🎉 Unicode 测试 テスト тест اختبار 🚀', '✨💫⭐');";
        cmd.ExecuteNonQuery();
    }

    private static void CreateNumericTypesTable(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE NumericTypes (
                ID INTEGER PRIMARY KEY,
                IntegerCol INTEGER,
                RealCol REAL,
                NumericCol NUMERIC,
                DecimalCol DECIMAL(10,2),
                FloatCol FLOAT,
                DoubleCol DOUBLE,
                MoneyCol TEXT
            );";
        cmd.ExecuteNonQuery();

        cmd.CommandText = @"
            INSERT INTO NumericTypes VALUES 
                (1, 42, 3.14159, 123.456, 99.99, 2.71828, 299792458.0, '$1,234.56'),
                (2, -2147483648, 0.0000001, 9999999.999999, 0.01, -3.4028235E+38, 1.7976931348623157E+308, '€987,65'),
                (3, 2147483647, -0.0, 0, 12345.67, 3.4028235E+38, -1.7976931348623157E+308, '¥10000'),
                (4, 0, 1.0E-10, 1.23E+5, 999999.99, 'inf', '-inf', '£45.50'),
                (5, NULL, NULL, NULL, NULL, NULL, NULL, NULL);";
        cmd.ExecuteNonQuery();
    }

    private static void CreateBlobTable(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE BlobData (
                ID INTEGER PRIMARY KEY,
                Description TEXT,
                BinaryData BLOB,
                ImageType TEXT
            );";
        cmd.ExecuteNonQuery();

        cmd.CommandText = @"
            INSERT INTO BlobData VALUES 
                (1, 'Small binary', @blob1, 'bytes'),
                (2, 'Empty blob', @blob2, NULL),
                (3, 'Null blob', NULL, NULL),
                (4, 'PNG header simulation', @blob3, 'png'),
                (5, 'Random bytes', @blob4, 'random');";
        
        cmd.Parameters.AddWithValue("@blob1", new byte[] { 0x00, 0x01, 0x02, 0x03, 0xFF, 0xFE, 0xFD });
        cmd.Parameters.AddWithValue("@blob2", new byte[0]);
        cmd.Parameters.AddWithValue("@blob3", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        
        var randomBytes = new byte[256];
        new Random(42).NextBytes(randomBytes);
        cmd.Parameters.AddWithValue("@blob4", randomBytes);
        
        cmd.ExecuteNonQuery();
    }

    private static void CreateEmptyTable(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE EmptyTable (
                ID INTEGER PRIMARY KEY,
                Data TEXT
            );";
        cmd.ExecuteNonQuery();
    }

    private static void CreateViewExample(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE VIEW CustomerOrderSummary AS
            SELECT 
                c.CustomerID,
                c.CompanyName,
                COUNT(o.OrderID) as TotalOrders,
                SUM(o.Freight) as TotalFreight
            FROM Customers c
            LEFT JOIN Orders o ON c.CustomerID = o.CustomerID
            GROUP BY c.CustomerID, c.CompanyName;";
        cmd.ExecuteNonQuery();
    }

    private static void SetDatabaseMetadata(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        
        cmd.CommandText = "PRAGMA user_version = 42;";
        cmd.ExecuteNonQuery();
        
        cmd.CommandText = "PRAGMA application_id = 0x42584C32;";
        cmd.ExecuteNonQuery();
    }
}