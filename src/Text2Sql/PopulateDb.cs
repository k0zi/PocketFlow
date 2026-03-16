using Microsoft.Data.Sqlite;

namespace Text2Sql;

/// <summary>
/// Creates and populates the sample e-commerce SQLite database.
/// Port of populate_db.py.
/// </summary>
public static class PopulateDb
{
    /// <summary>Creates (or re-creates) the database and fills it with sample data.</summary>
    public static void Populate(string dbFile = "ecommerce.db")
    {
        if (File.Exists(dbFile))
        {
            File.Delete(dbFile);
            Console.WriteLine($"Removed existing database: {dbFile}");
        }

        using var conn = new SqliteConnection($"Data Source={dbFile}");
        conn.Open();
        using var cmd = conn.CreateCommand();

        // ── Create tables ────────────────────────────────────────────────────

        cmd.CommandText = """
            CREATE TABLE customers (
                customer_id       INTEGER PRIMARY KEY AUTOINCREMENT,
                first_name        TEXT NOT NULL,
                last_name         TEXT NOT NULL,
                email             TEXT UNIQUE NOT NULL,
                registration_date DATE NOT NULL,
                city              TEXT,
                country           TEXT DEFAULT 'USA'
            )
            """;
        cmd.ExecuteNonQuery();
        Console.WriteLine("Created 'customers' table.");

        cmd.CommandText = """
            CREATE TABLE products (
                product_id     INTEGER PRIMARY KEY AUTOINCREMENT,
                name           TEXT NOT NULL,
                description    TEXT,
                category       TEXT NOT NULL,
                price          REAL NOT NULL CHECK (price > 0),
                stock_quantity INTEGER NOT NULL DEFAULT 0 CHECK (stock_quantity >= 0)
            )
            """;
        cmd.ExecuteNonQuery();
        Console.WriteLine("Created 'products' table.");

        cmd.CommandText = """
            CREATE TABLE orders (
                order_id         INTEGER PRIMARY KEY AUTOINCREMENT,
                customer_id      INTEGER NOT NULL,
                order_date       TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                status           TEXT NOT NULL CHECK (status IN ('pending','processing','shipped','delivered','cancelled')),
                total_amount     REAL,
                shipping_address TEXT,
                FOREIGN KEY (customer_id) REFERENCES customers (customer_id)
            )
            """;
        cmd.ExecuteNonQuery();
        Console.WriteLine("Created 'orders' table.");

        cmd.CommandText = """
            CREATE TABLE order_items (
                order_item_id  INTEGER PRIMARY KEY AUTOINCREMENT,
                order_id       INTEGER NOT NULL,
                product_id     INTEGER NOT NULL,
                quantity       INTEGER NOT NULL CHECK (quantity > 0),
                price_per_unit REAL NOT NULL,
                FOREIGN KEY (order_id)   REFERENCES orders   (order_id),
                FOREIGN KEY (product_id) REFERENCES products (product_id)
            )
            """;
        cmd.ExecuteNonQuery();
        Console.WriteLine("Created 'order_items' table.");

        // ── Insert customers ─────────────────────────────────────────────────

        (string fn, string ln, string email, string date, string city, string country)[] customers =
        [
            ("Alice",   "Smith",    "alice.s@email.com",    "2023-01-15", "New York",     "USA"),
            ("Bob",     "Johnson",  "b.johnson@email.com",  "2023-02-20", "Los Angeles",  "USA"),
            ("Charlie", "Williams", "charlie.w@email.com",  "2023-03-10", "Chicago",      "USA"),
            ("Diana",   "Brown",    "diana.b@email.com",    "2023-04-05", "Houston",      "USA"),
            ("Ethan",   "Davis",    "ethan.d@email.com",    "2023-05-12", "Phoenix",      "USA"),
            ("Fiona",   "Miller",   "fiona.m@email.com",    "2023-06-18", "Philadelphia", "USA"),
            ("George",  "Wilson",   "george.w@email.com",   "2023-07-22", "San Antonio",  "USA"),
            ("Hannah",  "Moore",    "hannah.m@email.com",   "2023-08-30", "San Diego",    "USA"),
            ("Ian",     "Taylor",   "ian.t@email.com",      "2023-09-05", "Dallas",       "USA"),
            ("Julia",   "Anderson", "julia.a@email.com",    "2023-10-11", "San Jose",     "USA"),
        ];

        foreach (var c in customers)
        {
            cmd.CommandText =
                "INSERT INTO customers (first_name, last_name, email, registration_date, city, country)" +
                " VALUES ($fn, $ln, $email, $rd, $city, $country)";
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("$fn",    c.fn);
            cmd.Parameters.AddWithValue("$ln",    c.ln);
            cmd.Parameters.AddWithValue("$email", c.email);
            cmd.Parameters.AddWithValue("$rd",    c.date);
            cmd.Parameters.AddWithValue("$city",  c.city);
            cmd.Parameters.AddWithValue("$country", c.country);
            cmd.ExecuteNonQuery();
        }
        Console.WriteLine($"Inserted {customers.Length} customers.");

        // ── Insert products ──────────────────────────────────────────────────

        (string name, string desc, string cat, double price, int stock)[] products =
        [
            ("Laptop Pro",         "High-end laptop for professionals",  "Electronics", 1200.00, 50),
            ("Wireless Mouse",     "Ergonomic wireless mouse",           "Accessories",   25.50, 200),
            ("Mechanical Keyboard","RGB backlit mechanical keyboard",    "Accessories",   75.00, 150),
            ("4K Monitor",         "27-inch 4K UHD Monitor",            "Electronics",  350.00, 80),
            ("Smartphone X",       "Latest generation smartphone",       "Electronics",  999.00, 120),
            ("Coffee Maker",       "Drip coffee maker",                  "Home Goods",    50.00, 300),
            ("Running Shoes",      "Comfortable running shoes",          "Apparel",       90.00, 250),
            ("Yoga Mat",           "Eco-friendly yoga mat",              "Sports",        30.00, 400),
            ("Desk Lamp",          "Adjustable LED desk lamp",           "Home Goods",    45.00, 180),
            ("Backpack",           "Durable backpack for travel",        "Accessories",   60.00, 220),
        ];

        foreach (var p in products)
        {
            cmd.CommandText =
                "INSERT INTO products (name, description, category, price, stock_quantity)" +
                " VALUES ($name, $desc, $cat, $price, $stock)";
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("$name",  p.name);
            cmd.Parameters.AddWithValue("$desc",  p.desc);
            cmd.Parameters.AddWithValue("$cat",   p.cat);
            cmd.Parameters.AddWithValue("$price", p.price);
            cmd.Parameters.AddWithValue("$stock", p.stock);
            cmd.ExecuteNonQuery();
        }
        Console.WriteLine($"Inserted {products.Length} products.");

        // ── Insert orders ────────────────────────────────────────────────────

        var rng = new Random();
        var startDate = DateTime.Now.AddDays(-60);
        string[] statuses = ["pending", "processing", "shipped", "delivered", "cancelled"];

        var orderIds = new List<int>();
        for (int i = 0; i < 20; i++)
        {
            var customerId = rng.Next(1, 11);
            var orderDate  = startDate
                .AddDays(rng.Next(0, 60))
                .AddHours(rng.Next(0, 24));
            var status     = statuses[rng.Next(statuses.Length)];
            var address    = $"{rng.Next(100, 1000)} Main St, Anytown";

            cmd.CommandText =
                "INSERT INTO orders (customer_id, order_date, status, total_amount, shipping_address)" +
                " VALUES ($cid, $od, $status, NULL, $addr)";
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("$cid",    customerId);
            cmd.Parameters.AddWithValue("$od",     orderDate.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("$status", status);
            cmd.Parameters.AddWithValue("$addr",   address);
            cmd.ExecuteNonQuery();

            cmd.CommandText = "SELECT last_insert_rowid()";
            cmd.Parameters.Clear();
            orderIds.Add((int)(long)cmd.ExecuteScalar()!);
        }
        Console.WriteLine($"Inserted {orderIds.Count} orders.");

        // ── Insert order items ───────────────────────────────────────────────

        int totalItems = 0;
        var orderTotals = new Dictionary<int, double>();

        foreach (var orderId in orderIds)
        {
            int numItems   = rng.Next(1, 5);
            double total   = 0;

            for (int j = 0; j < numItems; j++)
            {
                int productId = rng.Next(1, 11);
                int quantity  = rng.Next(1, 6);

                cmd.CommandText = "SELECT price FROM products WHERE product_id = $pid";
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("$pid", productId);
                double pricePerUnit = (double)cmd.ExecuteScalar()!;

                cmd.CommandText =
                    "INSERT INTO order_items (order_id, product_id, quantity, price_per_unit)" +
                    " VALUES ($oid, $pid, $qty, $price)";
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("$oid",   orderId);
                cmd.Parameters.AddWithValue("$pid",   productId);
                cmd.Parameters.AddWithValue("$qty",   quantity);
                cmd.Parameters.AddWithValue("$price", pricePerUnit);
                cmd.ExecuteNonQuery();

                total += quantity * pricePerUnit;
                totalItems++;
            }
            orderTotals[orderId] = Math.Round(total, 2);
        }
        Console.WriteLine($"Inserted {totalItems} order items.");

        // ── Update order totals ───────────────────────────────────────────────

        foreach (var (orderId, totalAmount) in orderTotals)
        {
            cmd.CommandText = "UPDATE orders SET total_amount = $total WHERE order_id = $oid";
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("$total", totalAmount);
            cmd.Parameters.AddWithValue("$oid",   orderId);
            cmd.ExecuteNonQuery();
        }
        Console.WriteLine("Updated order totals.");

        Console.WriteLine($"Database '{dbFile}' created and populated successfully.");
    }
}



