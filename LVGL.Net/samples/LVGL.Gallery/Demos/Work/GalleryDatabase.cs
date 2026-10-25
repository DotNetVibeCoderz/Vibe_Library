using Microsoft.Data.Sqlite;

namespace Lvgl.Gallery.Work;

internal sealed record Product(long Id, string Sku, string Name, string Group, int Quantity, decimal Price, int ReorderAt)
{
    public bool IsLow => Quantity <= ReorderAt;
}

internal sealed record WorkItem(long Id, string Title, string Owner, string Status, int Points);

internal sealed record SalesPoint(string Label, decimal Amount);

/// <summary>
/// The SQLite file behind the Work demos: products for the inventory, work items for the board,
/// and a year of sales for the report. Plain ADO.NET - the point is LVGL showing real queries,
/// not an ORM. Seeded with the same data on first run, so screenshots are repeatable.
/// </summary>
internal sealed class GalleryDatabase : IDisposable
{
    public static readonly string[] Statuses = ["todo", "doing", "done"];
    public static readonly string[] Regions = ["North", "South", "East", "West"];

    private readonly SqliteConnection _connection;
    private SqliteTransaction? _transaction;

    public GalleryDatabase(string path)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        FilePath = path;
        _connection = new SqliteConnection($"Data Source={path}");
        _connection.Open();
        CreateSchema();
        if (Scalar<long>("SELECT COUNT(*) FROM products") == 0) Seed();
    }

    public string FilePath { get; }

    #region Inventory

    public IReadOnlyList<Product> Products(string? search = null)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT id, sku, name, grp, qty, price, reorder_at FROM products
            WHERE $q IS NULL OR name LIKE '%' || $q || '%' OR sku LIKE '%' || $q || '%' OR grp LIKE '%' || $q || '%'
            ORDER BY grp, name
            """;
        command.Parameters.AddWithValue("$q", string.IsNullOrWhiteSpace(search) ? DBNull.Value : search.Trim());

        var products = new List<Product>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            products.Add(new Product(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetInt32(4), reader.GetDecimal(5), reader.GetInt32(6)));
        }

        return products;
    }

    public void AdjustQuantity(long id, int delta) =>
        Execute("UPDATE products SET qty = MAX(0, qty + $d) WHERE id = $id", ("$d", delta), ("$id", id));

    public long AddProduct(string name, string group, int quantity, decimal price)
    {
        var next = Scalar<long>("SELECT COALESCE(MAX(id), 0) + 1 FROM products");
        Execute("INSERT INTO products (sku, name, grp, qty, price, reorder_at) VALUES ($sku, $name, $grp, $qty, $price, 5)",
            ("$sku", $"{group[..Math.Min(3, group.Length)].ToUpperInvariant()}-{next:D4}"), ("$name", name), ("$grp", group),
            ("$qty", quantity), ("$price", price));
        return Scalar<long>("SELECT last_insert_rowid()");
    }

    public void DeleteProduct(long id) => Execute("DELETE FROM products WHERE id = $id", ("$id", id));

    public decimal StockValue() => (decimal)Scalar<double>("SELECT COALESCE(SUM(qty * price), 0.0) FROM products");

    #endregion

    #region Work items

    public IReadOnlyList<WorkItem> WorkItems()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT id, title, owner, status, points FROM work_items ORDER BY id";

        var items = new List<WorkItem>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            items.Add(new WorkItem(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4)));
        }

        return items;
    }

    public void MoveWorkItem(long id, string status) =>
        Execute("UPDATE work_items SET status = $s WHERE id = $id", ("$s", status), ("$id", id));

    #endregion

    #region Sales

    /// <summary>Revenue per month, oldest first.</summary>
    public IReadOnlyList<SalesPoint> SalesByMonth(string? region = null) => Series("""
        SELECT substr(day, 1, 7) AS month, SUM(amount) FROM sales
        WHERE $r IS NULL OR region = $r
        GROUP BY month ORDER BY month
        """, region);

    public IReadOnlyList<SalesPoint> SalesByRegion() => Series("""
        SELECT region, SUM(amount) FROM sales WHERE $r IS NULL GROUP BY region ORDER BY SUM(amount) DESC
        """, null);

    private List<SalesPoint> Series(string sql, string? region)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$r", region is null ? DBNull.Value : region);

        var points = new List<SalesPoint>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) points.Add(new SalesPoint(reader.GetString(0), reader.GetDecimal(1)));
        return points;
    }

    #endregion

    private void CreateSchema() => Execute("""
        CREATE TABLE IF NOT EXISTS products (
            id INTEGER PRIMARY KEY, sku TEXT NOT NULL, name TEXT NOT NULL, grp TEXT NOT NULL,
            qty INTEGER NOT NULL, price REAL NOT NULL, reorder_at INTEGER NOT NULL);
        CREATE TABLE IF NOT EXISTS work_items (
            id INTEGER PRIMARY KEY, title TEXT NOT NULL, owner TEXT NOT NULL, status TEXT NOT NULL, points INTEGER NOT NULL);
        CREATE TABLE IF NOT EXISTS sales (
            id INTEGER PRIMARY KEY, day TEXT NOT NULL, region TEXT NOT NULL, amount REAL NOT NULL);
        """);

    private void Seed()
    {
        // Microsoft.Data.Sqlite requires every command inside a transaction to name it.
        using var transaction = _transaction = _connection.BeginTransaction();

        (string Name, string Group, int Qty, double Price)[] products =
        [
            ("Raspberry Pi 4 (4 GB)", "Boards", 18, 55), ("Raspberry Pi 5 (8 GB)", "Boards", 4, 80),
            ("ESP32-S3 DevKit", "Boards", 42, 15), ("7\" DSI touch panel", "Displays", 6, 64),
            ("3.5\" SPI TFT", "Displays", 3, 19), ("2.8\" round LCD", "Displays", 11, 24),
            ("BME280 sensor", "Sensors", 57, 9), ("PIR motion sensor", "Sensors", 2, 4),
            ("INA219 current meter", "Sensors", 23, 6), ("USB-C 27 W supply", "Power", 31, 12),
            ("18650 cell holder", "Power", 9, 3), ("Aluminium case", "Enclosures", 14, 17),
            ("DIN-rail mount", "Enclosures", 5, 8), ("Rotary encoder", "Input", 38, 2),
            ("Capacitive keypad", "Input", 7, 11),
        ];

        foreach (var (name, group, qty, price) in products)
        {
            var sku = $"{group[..3].ToUpperInvariant()}-{Scalar<long>("SELECT COALESCE(MAX(id), 0) + 1 FROM products"):D4}";
            Execute("INSERT INTO products (sku, name, grp, qty, price, reorder_at) VALUES ($sku, $n, $g, $q, $p, 5)",
                ("$sku", sku), ("$n", name), ("$g", group), ("$q", qty), ("$p", price));
        }

        (string Title, string Owner, string Status, int Points)[] items =
        [
            ("Kiosk boot splash", "Ayu", "done", 2), ("Touch calibration screen", "Budi", "doing", 3),
            ("OTA update flow", "Citra", "todo", 5), ("Night mode theme", "Ayu", "todo", 2),
            ("Sensor history chart", "Dewi", "doing", 3), ("Wi-Fi setup wizard", "Budi", "todo", 8),
            ("Alarm sound picker", "Citra", "done", 1), ("Energy report export", "Dewi", "todo", 3),
        ];

        foreach (var (title, owner, status, points) in items)
        {
            Execute("INSERT INTO work_items (title, owner, status, points) VALUES ($t, $o, $s, $p)",
                ("$t", title), ("$o", owner), ("$s", status), ("$p", points));
        }

        // A year of daily sales with a seasonal swell, from a fixed seed.
        var random = new Random(2026);
        var start = new DateOnly(2025, 11, 1);
        for (var day = 0; day < 365; day++)
        {
            var date = start.AddDays(day);
            var season = 1 + 0.35 * Math.Sin((date.DayOfYear - 80) / 365.0 * 2 * Math.PI);
            foreach (var region in Regions)
            {
                var weight = region switch { "North" => 1.3, "South" => 0.8, "East" => 1.05, _ => 0.9 };
                var amount = Math.Round(120 * season * weight * (0.6 + random.NextDouble() * 0.8), 2);
                Execute("INSERT INTO sales (day, region, amount) VALUES ($d, $r, $a)",
                    ("$d", date.ToString("yyyy-MM-dd")), ("$r", region), ("$a", amount));
            }
        }

        transaction.Commit();
        _transaction = null;
    }

    private void Execute(string sql, params (string Name, object Value)[] parameters)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = _transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        command.ExecuteNonQuery();
    }

    private T Scalar<T>(string sql)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = _transaction;
        command.CommandText = sql;
        return (T)Convert.ChangeType(command.ExecuteScalar()!, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }

    public void Dispose()
    {
        _connection.Dispose();
        SqliteConnection.ClearAllPools();
    }
}
