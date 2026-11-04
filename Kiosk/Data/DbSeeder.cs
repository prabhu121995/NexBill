using Kiosk.Models;
using Kiosk.Services;
using Microsoft.EntityFrameworkCore;

namespace Kiosk.Data;

public static class DbSeeder
{
    /// <summary>
    /// Guarded separately from the menu seed, which returns early once categories
    /// exist — roles added after that point would otherwise never be inserted.
    /// Inserting only what is missing makes this safe to extend later.
    /// </summary>
    private static async Task SeedRolesAsync(AppDbContext db)
    {
        var wanted = new[]
        {
            new RoleConfig { Code = "ADMIN",      Name = "Admin",      SortOrder = 1 },
            new RoleConfig { Code = "SUPERVISOR", Name = "Supervisor", SortOrder = 2 },
            new RoleConfig { Code = "CASHIER",    Name = "Cashier",    SortOrder = 3 },
            new RoleConfig { Code = "WAITER",     Name = "Waiter",     SortOrder = 4 }
        };

        var existing = db.Roles.Select(r => r.Code).ToHashSet();
        var missing = wanted.Where(r => !existing.Contains(r.Code)).ToList();
        if (missing.Count == 0) return;

        db.Roles.AddRange(missing);
        await db.SaveChangesAsync();
    }


    /// <summary>
    /// The charges the shop bills at. Guarded the same way as the roles — only what
    /// is missing is inserted, so a rate an admin has since changed is never reset
    /// to the default on restart, and a charge added to this list later still lands
    /// on databases that are already seeded.
    /// </summary>
    private static async Task SeedChargesAsync(AppDbContext db)
    {
        var wanted = new[]
        {
            new TerminalSetting { Service = Charges.ServiceChargeKey, Value = Charges.DefaultServiceCharge },
            new TerminalSetting { Service = Charges.GstKey,           Value = Charges.DefaultGst }
        };

        var existing = db.TerminalSettings.Select(s => s.Service).ToHashSet();
        var missing = wanted.Where(s => !existing.Contains(s.Service)).ToList();
        if (missing.Count == 0) return;

        db.TerminalSettings.AddRange(missing);
        await db.SaveChangesAsync();
    }


    /// <summary>
    /// The single <c>config_store</c> row, inserted only while the table is empty
    /// so details an admin has since edited are never reset on restart.
    /// </summary>
    private static async Task SeedStoreAsync(AppDbContext db)
    {
        if (await db.StoreInfo.AnyAsync()) return;

        db.StoreInfo.Add(new StoreInfo
        {
            AppName   = AppBranding.DefaultName,
            StoreName = "NexBill",
            Address   = "123 Main St, Tiruchirappalli",
            Phone     = "+91 98765 43210",
            GstNo     = "33AAACR5055K1Z5"
        });
        await db.SaveChangesAsync();
    }


    /// <summary>
    /// The starting transaction modes, inserted only while the table is empty.
    /// Inserting what is missing, as the roles do, would bring back on restart any
    /// mode an admin had deleted.
    /// </summary>
    private static async Task SeedTransactionModesAsync(AppDbContext db)
    {
        if (await db.TransactionModes.AnyAsync()) return;

        var names = new[] { "CASH", "NETS", "CREDIT", "MASTER", "VISA", "DINERS", "AMEX", "KIOSK CASH" };
        db.TransactionModes.AddRange(names.Select((name, i) => new TransactionMode { Name = name, SortOrder = i + 1 }));
        await db.SaveChangesAsync();
    }


    /// <summary>
    /// The first admin, inserted only while <c>users</c> is empty. Without it a
    /// fresh database has no account at all and nobody can reach any screen,
    /// including the one that would create the first account.
    ///
    /// It is a known credential, so it is a way in, not a password: the Readme
    /// says to change it before the till goes live, and this never runs again
    /// once any user row exists — deleting the row does not bring it back while
    /// other staff remain.
    /// </summary>
    private static async Task SeedFirstAdminAsync(AppDbContext db, ILogger? log)
    {
        if (db.Users.Any()) return;

        var admin = await db.Roles.FirstOrDefaultAsync(r => r.Code == "ADMIN");
        if (admin is null) return;

        db.Users.Add(new User
        {
            Username    = "admin",
            DisplayName = "Administrator",
            Password    = "admin123",
            RoleId      = admin.Id
        });
        await db.SaveChangesAsync();

        log?.LogWarning(
            "Seeded the first admin account 'admin' with the default password. " +
            "Change it before this terminal takes real orders.");
    }

    /// <summary>
    /// Categories without a colour - every row from before the Color column,
    /// and the freshly seeded menu - get one from the palette in rail order. That is
    /// the colour the till used to give them by position, so nothing changes on screen.
    /// </summary>
    private static async Task BackfillCategoryColorsAsync(AppDbContext db)
    {
        var cats = await db.Categories.OrderBy(c => c.SortOrder).ToListAsync();
        if (cats.All(c => c.Color != "")) return;

        for (var i = 0; i < cats.Count; i++)
            if (cats[i].Color == "")
                cats[i].Color = Category.Palette[i % Category.Palette.Length];

        await db.SaveChangesAsync();
    }

    public static async Task SeedAsync(AppDbContext db, ILogger? log = null)
    {
        await SeedRolesAsync(db);
        await SeedChargesAsync(db);
        await SeedStoreAsync(db);
        await SeedTransactionModesAsync(db);
        await SeedFirstAdminAsync(db, log);

        if (db.Categories.Any())
        {
            await BackfillCategoryColorsAsync(db);
            return;
        }

        var categories = new[]
        {
            new Category { ExternalId = "STAPLES",    Name = "Rice & Staples",  Icon = "wheat",       SortOrder = 1 },
            new Category { ExternalId = "FRESH",      Name = "Fruits & Veg",    Icon = "apple",       SortOrder = 2 },
            new Category { ExternalId = "DAIRY",      Name = "Dairy & Eggs",    Icon = "milk",        SortOrder = 3 },
            new Category { ExternalId = "BAKERY",     Name = "Bakery",          Icon = "croissant",   SortOrder = 4 },
            new Category { ExternalId = "BEVERAGES",  Name = "Beverages",       Icon = "cup-soda",    SortOrder = 5 },
            new Category { ExternalId = "SNACKS",     Name = "Snacks & Sweets", Icon = "cookie",      SortOrder = 6 },
            new Category { ExternalId = "FROZEN",     Name = "Frozen",          Icon = "snowflake",   SortOrder = 7 },
            new Category { ExternalId = "PERSONAL",   Name = "Personal Care",   Icon = "sparkles",    SortOrder = 8 },
            new Category { ExternalId = "HOUSEHOLD",  Name = "Household",       Icon = "spray-can",   SortOrder = 9 },
            new Category { ExternalId = "BABY",       Name = "Baby Care",       Icon = "baby",        SortOrder = 10 },
            new Category { ExternalId = "HEALTH",     Name = "Health",          Icon = "pill",        SortOrder = 11 },
            new Category { ExternalId = "STATIONERY", Name = "Stationery",      Icon = "pencil",      SortOrder = 12 },
            new Category { ExternalId = "ALL",        Name = "All",             Icon = "layout-grid", SortOrder = 13 },
        };

        db.Categories.AddRange(categories);
        await db.SaveChangesAsync();
        await BackfillCategoryColorsAsync(db);

        var items = new[]
        {
            new MenuItem { ExternalId="st1", Name="BASMATI RICE 5KG",           Price=14.90m, CategoryExternalId="STAPLES",    IsVeg=true  },
            new MenuItem { ExternalId="st2", Name="PONNI RICE 5KG",             Price=11.50m, CategoryExternalId="STAPLES",    IsVeg=true  },
            new MenuItem { ExternalId="st3", Name="ATTA WHEAT FLOUR 2KG",       Price=5.20m,  CategoryExternalId="STAPLES",    IsVeg=true  },
            new MenuItem { ExternalId="st4", Name="TOOR DAL 1KG",               Price=4.80m,  CategoryExternalId="STAPLES",    IsVeg=true  },
            new MenuItem { ExternalId="st5", Name="SUGAR 1KG",                  Price=2.30m,  CategoryExternalId="STAPLES",    IsVeg=true  },
            new MenuItem { ExternalId="st6", Name="IODISED SALT 1KG",           Price=0.90m,  CategoryExternalId="STAPLES",    IsVeg=true  },
            new MenuItem { ExternalId="st7", Name="SUNFLOWER OIL 2L",           Price=7.90m,  CategoryExternalId="STAPLES",    IsVeg=true  },
            new MenuItem { ExternalId="st8", Name="INSTANT NOODLES 5PK",        Price=3.60m,  CategoryExternalId="STAPLES",    IsVeg=true  },
            new MenuItem { ExternalId="fr1", Name="BANANA (PER KG)",            Price=2.80m,  CategoryExternalId="FRESH",      IsVeg=true  },
            new MenuItem { ExternalId="fr2", Name="RED APPLE (4PC)",            Price=3.50m,  CategoryExternalId="FRESH",      IsVeg=true  },
            new MenuItem { ExternalId="fr3", Name="ONION 1KG",                  Price=1.90m,  CategoryExternalId="FRESH",      IsVeg=true  },
            new MenuItem { ExternalId="fr4", Name="POTATO 1KG",                 Price=2.20m,  CategoryExternalId="FRESH",      IsVeg=true  },
            new MenuItem { ExternalId="fr5", Name="TOMATO 500G",                Price=1.80m,  CategoryExternalId="FRESH",      IsVeg=true  },
            new MenuItem { ExternalId="fr6", Name="CARROT 500G",                Price=1.50m,  CategoryExternalId="FRESH",      IsVeg=true  },
            new MenuItem { ExternalId="fr7", Name="LEMON (3PC)",                Price=1.20m,  CategoryExternalId="FRESH",      IsVeg=true  },
            new MenuItem { ExternalId="da1", Name="FRESH MILK 1L",              Price=3.40m,  CategoryExternalId="DAIRY",      IsVeg=true  },
            new MenuItem { ExternalId="da2", Name="LOW FAT MILK 1L",            Price=3.60m,  CategoryExternalId="DAIRY",      IsVeg=true  },
            new MenuItem { ExternalId="da3", Name="PLAIN YOGURT 500G",          Price=2.90m,  CategoryExternalId="DAIRY",      IsVeg=true  },
            new MenuItem { ExternalId="da4", Name="SALTED BUTTER 250G",         Price=4.50m,  CategoryExternalId="DAIRY",      IsVeg=true  },
            new MenuItem { ExternalId="da5", Name="CHEDDAR SLICES 200G",        Price=4.20m,  CategoryExternalId="DAIRY",      IsVeg=true  },
            new MenuItem { ExternalId="da6", Name="PANEER 200G",                Price=3.80m,  CategoryExternalId="DAIRY",      IsVeg=true  },
            new MenuItem { ExternalId="da7", Name="EGGS (10PC)",                Price=3.20m,  CategoryExternalId="DAIRY",      IsVeg=false },
            new MenuItem { ExternalId="bk1", Name="WHITE BREAD LOAF",           Price=2.40m,  CategoryExternalId="BAKERY",     IsVeg=true  },
            new MenuItem { ExternalId="bk2", Name="WHOLEMEAL BREAD LOAF",       Price=2.80m,  CategoryExternalId="BAKERY",     IsVeg=true  },
            new MenuItem { ExternalId="bk3", Name="BUTTER CROISSANT (2PC)",     Price=3.00m,  CategoryExternalId="BAKERY",     IsVeg=true  },
            new MenuItem { ExternalId="bk4", Name="CHOCOLATE MUFFIN",           Price=1.80m,  CategoryExternalId="BAKERY",     IsVeg=true  },
            new MenuItem { ExternalId="bk5", Name="BURGER BUNS (4PC)",          Price=2.20m,  CategoryExternalId="BAKERY",     IsVeg=true  },
            new MenuItem { ExternalId="bk6", Name="RUSK 300G",                  Price=2.50m,  CategoryExternalId="BAKERY",     IsVeg=true  },
            new MenuItem { ExternalId="bv1", Name="MINERAL WATER 1.5L",         Price=1.20m,  CategoryExternalId="BEVERAGES",  IsVeg=true  },
            new MenuItem { ExternalId="bv2", Name="COLA CAN 330ML",             Price=1.30m,  CategoryExternalId="BEVERAGES",  IsVeg=true  },
            new MenuItem { ExternalId="bv3", Name="ORANGE JUICE 1L",            Price=3.90m,  CategoryExternalId="BEVERAGES",  IsVeg=true  },
            new MenuItem { ExternalId="bv4", Name="GREEN TEA BAGS (25PC)",      Price=3.20m,  CategoryExternalId="BEVERAGES",  IsVeg=true  },
            new MenuItem { ExternalId="bv5", Name="INSTANT COFFEE 200G",        Price=7.50m,  CategoryExternalId="BEVERAGES",  IsVeg=true  },
            new MenuItem { ExternalId="bv6", Name="ENERGY DRINK 250ML",         Price=2.40m,  CategoryExternalId="BEVERAGES",  IsVeg=true  },
            new MenuItem { ExternalId="bv7", Name="CHOCOLATE MALT DRINK 400G",  Price=6.80m,  CategoryExternalId="BEVERAGES",  IsVeg=true  },
            new MenuItem { ExternalId="sn1", Name="POTATO CHIPS 150G",          Price=2.60m,  CategoryExternalId="SNACKS",     IsVeg=true  },
            new MenuItem { ExternalId="sn2", Name="CREAM BISCUITS 300G",        Price=2.10m,  CategoryExternalId="SNACKS",     IsVeg=true  },
            new MenuItem { ExternalId="sn3", Name="MILK CHOCOLATE BAR 100G",    Price=2.90m,  CategoryExternalId="SNACKS",     IsVeg=true  },
            new MenuItem { ExternalId="sn4", Name="SALTED PEANUTS 200G",        Price=2.30m,  CategoryExternalId="SNACKS",     IsVeg=true  },
            new MenuItem { ExternalId="sn5", Name="MIXTURE NAMKEEN 400G",       Price=3.40m,  CategoryExternalId="SNACKS",     IsVeg=true  },
            new MenuItem { ExternalId="sn6", Name="CHEWING GUM PACK",           Price=1.10m,  CategoryExternalId="SNACKS",     IsVeg=true  },
            new MenuItem { ExternalId="sn7", Name="MICROWAVE POPCORN (3PK)",    Price=3.80m,  CategoryExternalId="SNACKS",     IsVeg=true  },
            new MenuItem { ExternalId="fz1", Name="VANILLA ICE CREAM 1L",       Price=6.50m,  CategoryExternalId="FROZEN",     IsVeg=true  },
            new MenuItem { ExternalId="fz2", Name="FROZEN GREEN PEAS 500G",     Price=2.70m,  CategoryExternalId="FROZEN",     IsVeg=true  },
            new MenuItem { ExternalId="fz3", Name="CHICKEN NUGGETS 500G",       Price=7.20m,  CategoryExternalId="FROZEN",     IsVeg=false },
            new MenuItem { ExternalId="fz4", Name="FROZEN PARATHA (5PC)",       Price=3.90m,  CategoryExternalId="FROZEN",     IsVeg=true  },
            new MenuItem { ExternalId="fz5", Name="FISH FINGERS 400G",          Price=6.90m,  CategoryExternalId="FROZEN",     IsVeg=false },
            new MenuItem { ExternalId="fz6", Name="FROZEN MIXED VEG 500G",      Price=2.90m,  CategoryExternalId="FROZEN",     IsVeg=true  },
            new MenuItem { ExternalId="pc1", Name="BATH SOAP (3PK)",            Price=3.50m,  CategoryExternalId="PERSONAL",   IsVeg=true  },
            new MenuItem { ExternalId="pc2", Name="SHAMPOO 340ML",              Price=6.20m,  CategoryExternalId="PERSONAL",   IsVeg=true  },
            new MenuItem { ExternalId="pc3", Name="TOOTHPASTE 150G",            Price=3.10m,  CategoryExternalId="PERSONAL",   IsVeg=true  },
            new MenuItem { ExternalId="pc4", Name="TOOTHBRUSH (2PK)",           Price=2.80m,  CategoryExternalId="PERSONAL",   IsVeg=true  },
            new MenuItem { ExternalId="pc5", Name="HAND WASH 250ML",            Price=3.30m,  CategoryExternalId="PERSONAL",   IsVeg=true  },
            new MenuItem { ExternalId="pc6", Name="DEODORANT SPRAY 150ML",      Price=5.40m,  CategoryExternalId="PERSONAL",   IsVeg=true  },
            new MenuItem { ExternalId="pc7", Name="FACE WASH 100ML",            Price=4.90m,  CategoryExternalId="PERSONAL",   IsVeg=true  },
            new MenuItem { ExternalId="hh1", Name="DISHWASH LIQUID 500ML",      Price=2.90m,  CategoryExternalId="HOUSEHOLD",  IsVeg=true  },
            new MenuItem { ExternalId="hh2", Name="DETERGENT POWDER 1KG",       Price=5.60m,  CategoryExternalId="HOUSEHOLD",  IsVeg=true  },
            new MenuItem { ExternalId="hh3", Name="FLOOR CLEANER 1L",           Price=4.20m,  CategoryExternalId="HOUSEHOLD",  IsVeg=true  },
            new MenuItem { ExternalId="hh4", Name="TOILET ROLLS (10PK)",        Price=6.90m,  CategoryExternalId="HOUSEHOLD",  IsVeg=true  },
            new MenuItem { ExternalId="hh5", Name="GARBAGE BAGS (30PC)",        Price=2.50m,  CategoryExternalId="HOUSEHOLD",  IsVeg=true  },
            new MenuItem { ExternalId="hh6", Name="AA BATTERIES (4PK)",         Price=4.80m,  CategoryExternalId="HOUSEHOLD",  IsVeg=true  },
            new MenuItem { ExternalId="hh7", Name="KITCHEN TOWEL (2 ROLLS)",    Price=2.60m,  CategoryExternalId="HOUSEHOLD",  IsVeg=true  },
            new MenuItem { ExternalId="by1", Name="BABY DIAPERS M (24PC)",      Price=12.90m, CategoryExternalId="BABY",       IsVeg=true  },
            new MenuItem { ExternalId="by2", Name="BABY WIPES (80PC)",          Price=3.40m,  CategoryExternalId="BABY",       IsVeg=true  },
            new MenuItem { ExternalId="by3", Name="BABY SHAMPOO 200ML",         Price=4.60m,  CategoryExternalId="BABY",       IsVeg=true  },
            new MenuItem { ExternalId="by4", Name="BABY POWDER 200G",           Price=3.80m,  CategoryExternalId="BABY",       IsVeg=true  },
            new MenuItem { ExternalId="by5", Name="BABY CEREAL 300G",           Price=6.40m,  CategoryExternalId="BABY",       IsVeg=true  },
            new MenuItem { ExternalId="hl1", Name="PARACETAMOL 500MG (10S)",    Price=1.80m,  CategoryExternalId="HEALTH",     IsVeg=true  },
            new MenuItem { ExternalId="hl2", Name="ADHESIVE BANDAGES (20PC)",   Price=2.40m,  CategoryExternalId="HEALTH",     IsVeg=true  },
            new MenuItem { ExternalId="hl3", Name="ANTISEPTIC LIQUID 100ML",    Price=3.20m,  CategoryExternalId="HEALTH",     IsVeg=true  },
            new MenuItem { ExternalId="hl4", Name="COUGH LOZENGES",             Price=2.10m,  CategoryExternalId="HEALTH",     IsVeg=true  },
            new MenuItem { ExternalId="hl5", Name="FACE MASKS (10PC)",          Price=2.90m,  CategoryExternalId="HEALTH",     IsVeg=true  },
            new MenuItem { ExternalId="hl6", Name="HAND SANITISER 100ML",       Price=2.50m,  CategoryExternalId="HEALTH",     IsVeg=true  },
            new MenuItem { ExternalId="sv1", Name="A4 NOTEBOOK 100PG",          Price=1.90m,  CategoryExternalId="STATIONERY", IsVeg=true  },
            new MenuItem { ExternalId="sv2", Name="BALL PENS BLUE (5PK)",       Price=1.60m,  CategoryExternalId="STATIONERY", IsVeg=true  },
            new MenuItem { ExternalId="sv3", Name="PENCILS HB (10PK)",          Price=1.40m,  CategoryExternalId="STATIONERY", IsVeg=true  },
            new MenuItem { ExternalId="sv4", Name="ERASER & SHARPENER SET",     Price=0.90m,  CategoryExternalId="STATIONERY", IsVeg=true  },
            new MenuItem { ExternalId="sv5", Name="GLUE STICK 20G",             Price=1.20m,  CategoryExternalId="STATIONERY", IsVeg=true  },
            new MenuItem { ExternalId="sv6", Name="A4 COPIER PAPER (500SH)",    Price=6.50m,  CategoryExternalId="STATIONERY", IsVeg=true  },
        };

        db.MenuItems.AddRange(items);
        await db.SaveChangesAsync();
    }
}
