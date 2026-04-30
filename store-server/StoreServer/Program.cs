using Microsoft.EntityFrameworkCore;
using StoreServer.Hubs;
using StoreServer.Models;
using StoreServer.Services;

var builder = WebApplication.CreateBuilder(args);

// SQLite local DB
builder.Services.AddDbContext<StoreDbContext>(opt =>
    opt.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=store.db"));

// HTTP client for cloud API
builder.Services.AddHttpClient("cloud", c =>
{
    c.BaseAddress = new Uri(builder.Configuration["CloudApiUrl"] ?? "https://checkout.minhojan-world.site/api/");
    var apiKey = builder.Configuration["CloudApiKey"];
    if (!string.IsNullOrEmpty(apiKey))
        c.DefaultRequestHeaders.Add("X-API-Key", apiKey);
});

// Background services
builder.Services.AddSingleton<SyncService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SyncService>());
// Register as singleton so CatalogController can inject it to call TriggerNowAsync()
builder.Services.AddSingleton<CatalogRefreshService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<CatalogRefreshService>());

builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddCors(opt => opt.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// Run migrations + seed demo products on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<StoreDbContext>();

    // If the db file is corrupted or missing new columns/tables, delete it so EnsureCreated recreates it cleanly
    bool needsRecreate = false;
    try
    {
        db.Database.EnsureCreated();
        // Verify the schema has all expected columns and tables
        db.Database.ExecuteSqlRaw("SELECT WeightGrams FROM Products LIMIT 1");
        db.Database.ExecuteSqlRaw("SELECT Id FROM WeightCheckLogs LIMIT 1");
        db.Database.ExecuteSqlRaw("SELECT Key FROM StoreSettings LIMIT 1");
        db.Database.ExecuteSqlRaw("SELECT Key FROM CashDrawer LIMIT 1");
    }
    catch
    {
        needsRecreate = true;
    }

    if (needsRecreate)
    {
        var dbPath = db.Database.GetDbConnection().DataSource;
        db.Database.CloseConnection();
        if (File.Exists(dbPath)) File.Delete(dbPath);
        db.Database.EnsureCreated();
    }

    if (!db.Products.Any())
    {
        db.Products.AddRange(
            new Product { Barcode = "1234567890",    Name = "Apple",                  Price = 1.99m,  Category = "Produce",      WeightGrams = 182 },
            new Product { Barcode = "9780201379624", Name = "Programming Book",       Price = 39.99m, Category = "Books",        WeightGrams = 450 },
            new Product { Barcode = "5901234123457", Name = "Dark Chocolate",         Price = 3.49m,  Category = "Snacks",       WeightGrams = 100 },
            new Product { Barcode = "4006381333931", Name = "Staedtler Pen",          Price = 2.99m,  Category = "Stationery",   WeightGrams = 0   },
            new Product { Barcode = "0012000001086", Name = "Pepsi 500ml",            Price = 1.79m,  Category = "Drinks",       WeightGrams = 528 },
            new Product { Barcode = "5000112546415", Name = "Cadbury Dairy Milk",     Price = 2.49m,  Category = "Snacks",       WeightGrams = 200 },
            new Product { Barcode = "8801062573158", Name = "Shin Ramyun",            Price = 1.29m,  Category = "Instant Food", WeightGrams = 120 },
            new Product { Barcode = "0038000845031", Name = "Kellogg's Corn Flakes",  Price = 4.99m,  Category = "Breakfast",    WeightGrams = 500 }
        );
        db.SaveChanges();
    }
    else
    {
        // Products already exist — make sure WeightGrams are populated.
        // If they were seeded before WeightGrams was added, they'll all be 0 and the
        // weight check on the terminal will silently never trigger.
        var weightMap = new Dictionary<string, int>
        {
            ["1234567890"]    = 182,
            ["9780201379624"] = 450,
            ["5901234123457"] = 100,
            ["0012000001086"] = 528,
            ["5000112546415"] = 200,
            ["8801062573158"] = 120,
            ["0038000845031"] = 500,
        };
        bool dirty = false;
        foreach (var (barcode, grams) in weightMap)
        {
            var p = db.Products.FirstOrDefault(x => x.Barcode == barcode);
            if (p != null && p.WeightGrams == 0)
            {
                p.WeightGrams = grams;
                dirty = true;
            }
        }
        if (dirty) db.SaveChanges();
    }

    // Seed default store-wide staff PIN if not set
    if (!db.StoreSettings.Any(s => s.Key == "StaffPin"))
    {
        db.StoreSettings.Add(new StoreSetting { Key = "StaffPin", Value = "4312" });
        db.SaveChanges();
    }

    // Seed cash drawer starting inventory — one set of rows per terminal
    if (!db.CashDrawer.Any())
    {
        var denominations = new[]
        {
            new CashDrawerEntry { Key = "0.01",      Label = "1¢",      Denomination = 0.01m,  IsCoin = true,  Count = 200,  MaxCount = 1000 },
            new CashDrawerEntry { Key = "0.05",      Label = "5¢",      Denomination = 0.05m,  IsCoin = true,  Count = 200,  MaxCount = 1000 },
            new CashDrawerEntry { Key = "0.10",      Label = "10¢",     Denomination = 0.10m,  IsCoin = true,  Count = 300,  MaxCount = 1500 },
            new CashDrawerEntry { Key = "0.25",      Label = "25¢",     Denomination = 0.25m,  IsCoin = true,  Count = 250,  MaxCount = 1250 },
            new CashDrawerEntry { Key = "0.50",      Label = "50¢",     Denomination = 0.50m,  IsCoin = true,  Count = 50,   MaxCount = 250  },
            new CashDrawerEntry { Key = "1.00_coin", Label = "$1 coin", Denomination = 1.00m,  IsCoin = true,  Count = 75,   MaxCount = 375  },
            new CashDrawerEntry { Key = "1.00_bill", Label = "$1",      Denomination = 1.00m,  IsCoin = false, Count = 100,  MaxCount = 500  },
            new CashDrawerEntry { Key = "2.00",      Label = "$2",      Denomination = 2.00m,  IsCoin = false, Count = 30,   MaxCount = 150  },
            new CashDrawerEntry { Key = "5.00",      Label = "$5",      Denomination = 5.00m,  IsCoin = false, Count = 75,   MaxCount = 375  },
            new CashDrawerEntry { Key = "10.00",     Label = "$10",     Denomination = 10.00m, IsCoin = false, Count = 80,   MaxCount = 400  },
            new CashDrawerEntry { Key = "20.00",     Label = "$20",     Denomination = 20.00m, IsCoin = false, Count = 100,  MaxCount = 500  },
            new CashDrawerEntry { Key = "50.00",     Label = "$50",     Denomination = 50.00m, IsCoin = false, Count = 20,   MaxCount = 100  },
            new CashDrawerEntry { Key = "100.00",    Label = "$100",    Denomination = 100.00m,IsCoin = false, Count = 10,   MaxCount = 50   },
        };
        foreach (var terminalId in new[] { 1, 2, 3 })
            foreach (var d in denominations)
                db.CashDrawer.Add(new CashDrawerEntry
                {
                    TerminalId   = terminalId,
                    Key          = d.Key,
                    Label        = d.Label,
                    Denomination = d.Denomination,
                    IsCoin       = d.IsCoin,
                    Count        = d.Count,
                });
        db.SaveChanges();
    }

    if (!db.LoyaltyMembers.Any())
    {
        db.LoyaltyMembers.AddRange(
            // One row per person — both phone and card point to the same account
            // Pattern lock = last 4 digits of phone
            new LoyaltyMember { Phone = "01012345678", CardId = "4910000000001", Name = "Kim Ji-woo",      Points = 12450, Pin = "1236" },
            new LoyaltyMember { Phone = "01987654321", CardId = null,            Name = "Park Soo-yeon",   Points = 5670,  Pin = "1236" },
            new LoyaltyMember { Phone = "01198765432", CardId = "4910000000002", Name = "Lee Min-jun",     Points = 3280,  Pin = "1236" },
            new LoyaltyMember { Phone = "01099998888", CardId = "4910000000004", Name = "Choi Dong-hyun",  Points = 420,   Pin = "1236" },
            // Staff cards — no phone, no PIN
            new LoyaltyMember { Phone = null,          CardId = "STAFF-001",     Name = "Staff (Manager)", Points = 0,    Pin = "" },
            new LoyaltyMember { Phone = null,          CardId = "STAFF-002",     Name = "Staff (Cashier)", Points = 0,    Pin = "" }
        );
        db.SaveChanges();
    }
    else
    {
        // Backfill CardIds that may be missing from older DB runs
        var cardBackfill = new Dictionary<string, string>
        {
            ["01012345678"] = "4910000000001",
            ["01198765432"] = "4910000000002",
            ["01099998888"] = "4910000000004",
        };
        bool dirty = false;
        foreach (var (phone, cardId) in cardBackfill)
        {
            var m = db.LoyaltyMembers.FirstOrDefault(x => x.Phone == phone);
            if (m != null && m.CardId != cardId)
            {
                m.CardId = cardId;
                dirty = true;
            }
        }
        if (dirty) db.SaveChanges();
    }
}

app.UseCors();
app.MapControllers();
app.MapHub<CheckoutHub>("/hub");
app.MapGet("/health", () => "ok");

app.Run();
