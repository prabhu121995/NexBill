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
    /// Main categories without a colour - every row from before the Color column,
    /// and the freshly seeded menu - get one from the palette in rail order. That is
    /// the colour the till used to give them by position, so nothing changes on screen.
    /// </summary>
    private static async Task BackfillCategoryColorsAsync(AppDbContext db)
    {
        var level1 = await db.Categories.Where(c => c.Level == 1).OrderBy(c => c.SortOrder).ToListAsync();
        if (level1.All(c => c.Color != "")) return;

        for (var i = 0; i < level1.Count; i++)
            if (level1[i].Color == "")
                level1[i].Color = Category.Palette[i % Category.Palette.Length];

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

        var cats1 = new[]
        {
            new Category { ExternalId = "TIFFIN",     Name = "Tiffin",     Icon = "utensils",     Level = 1, SortOrder = 1 },
            new Category { ExternalId = "DRINKS",     Name = "Drinks",     Icon = "coffee",       Level = 1, SortOrder = 2 },
            new Category { ExternalId = "MEALS",      Name = "Meals",      Icon = "soup",         Level = 1, SortOrder = 3 },
            new Category { ExternalId = "BRIYANI",    Name = "Briyani",    Icon = "flame",        Level = 1, SortOrder = 4 },
            new Category { ExternalId = "FRIED RICE", Name = "Fried Rice", Icon = "wheat",        Level = 1, SortOrder = 5 },
            new Category { ExternalId = "CHINESE",    Name = "Chinese",    Icon = "salad",        Level = 1, SortOrder = 6 },
            new Category { ExternalId = "WESTERN",    Name = "Western",    Icon = "pizza",        Level = 1, SortOrder = 7 },
            new Category { ExternalId = "HOT POT",    Name = "Hot Pot",    Icon = "cooking-pot",  Level = 1, SortOrder = 8 },
            new Category { ExternalId = "PAROTTA",    Name = "Parotta",    Icon = "layers",       Level = 1, SortOrder = 9 },
            new Category { ExternalId = "SNACKS",     Name = "Snacks",     Icon = "cookie",       Level = 1, SortOrder = 10 },
            new Category { ExternalId = "DESSERTS",   Name = "Desserts",   Icon = "ice-cream-cone", Level = 1, SortOrder = 11 },
            new Category { ExternalId = "SPECIALS",   Name = "Specials",   Icon = "star",         Level = 1, SortOrder = 12 },
            new Category { ExternalId = "COMBO",      Name = "Combo",      Icon = "package",      Level = 1, SortOrder = 13 },
            new Category { ExternalId = "ALL",        Name = "All",        Icon = "layout-grid",  Level = 1, SortOrder = 14 },
        };

        var cats2 = new[]
        {
            new Category { ExternalId = "IDLY",           Name = "Idly",            Icon = "", Level = 2, ParentExternalId = "TIFFIN",   SortOrder = 1 },
            new Category { ExternalId = "DOSA",           Name = "Dosa",            Icon = "", Level = 2, ParentExternalId = "TIFFIN",   SortOrder = 2 },
            new Category { ExternalId = "UTHAPPAM",       Name = "Uthappam",        Icon = "", Level = 2, ParentExternalId = "TIFFIN",   SortOrder = 3 },
            new Category { ExternalId = "RAVA",           Name = "Rava",            Icon = "", Level = 2, ParentExternalId = "TIFFIN",   SortOrder = 4 },
            new Category { ExternalId = "PONGAL",         Name = "Pongal & Poori",  Icon = "", Level = 2, ParentExternalId = "TIFFIN",   SortOrder = 5 },
            new Category { ExternalId = "HOT DRINKS",     Name = "Hot",             Icon = "", Level = 2, ParentExternalId = "DRINKS",   SortOrder = 1 },
            new Category { ExternalId = "COLD DRINKS",    Name = "Cold",            Icon = "", Level = 2, ParentExternalId = "DRINKS",   SortOrder = 2 },
            new Category { ExternalId = "VEG MEALS",      Name = "Veg Meals",       Icon = "", Level = 2, ParentExternalId = "MEALS",    SortOrder = 1 },
            new Category { ExternalId = "NONVEG MEALS",   Name = "Non-veg Meals",   Icon = "", Level = 2, ParentExternalId = "MEALS",    SortOrder = 2 },
            new Category { ExternalId = "RICE",           Name = "Variety Rice",    Icon = "", Level = 2, ParentExternalId = "MEALS",    SortOrder = 3 },
            new Category { ExternalId = "CHICKEN BRIYANI",Name = "Chicken",         Icon = "", Level = 2, ParentExternalId = "BRIYANI",  SortOrder = 1 },
            new Category { ExternalId = "MUTTON BRIYANI", Name = "Mutton & Seafood",Icon = "", Level = 2, ParentExternalId = "BRIYANI",  SortOrder = 2 },
            new Category { ExternalId = "VEG BRIYANI",    Name = "Veg & Egg",       Icon = "", Level = 2, ParentExternalId = "BRIYANI",  SortOrder = 3 },
            new Category { ExternalId = "STARTERS",       Name = "Starters",        Icon = "", Level = 2, ParentExternalId = "CHINESE",  SortOrder = 1 },
            new Category { ExternalId = "NOODLES",        Name = "Noodles",         Icon = "", Level = 2, ParentExternalId = "CHINESE",  SortOrder = 2 },
            new Category { ExternalId = "PLAIN PAROTTA",  Name = "Parotta",         Icon = "", Level = 2, ParentExternalId = "PAROTTA",  SortOrder = 1 },
            new Category { ExternalId = "KOTHU",          Name = "Kothu",           Icon = "", Level = 2, ParentExternalId = "PAROTTA",  SortOrder = 2 },
        };

        db.Categories.AddRange(cats1);
        db.Categories.AddRange(cats2);
        await db.SaveChangesAsync();
        await BackfillCategoryColorsAsync(db);

        var items = new[]
        {
            new MenuItem { ExternalId="t1",  Name="IDLY",                    Price=1.20m,  CategoryExternalId="TIFFIN",     SubCategoryExternalId="IDLY",            IsVeg=true  },
            new MenuItem { ExternalId="t2",  Name="PLAIN DOSA",              Price=2.00m,  CategoryExternalId="TIFFIN",     SubCategoryExternalId="DOSA",            IsVeg=true  },
            new MenuItem { ExternalId="t3",  Name="EGG DOSA",                Price=4.00m,  CategoryExternalId="TIFFIN",     SubCategoryExternalId="DOSA",            IsVeg=false },
            new MenuItem { ExternalId="t4",  Name="MASALA DOSA",             Price=4.00m,  CategoryExternalId="TIFFIN",     SubCategoryExternalId="DOSA",            IsVeg=true  },
            new MenuItem { ExternalId="t5",  Name="PODI DOSA",               Price=4.50m,  CategoryExternalId="TIFFIN",     SubCategoryExternalId="DOSA",            IsVeg=true  },
            new MenuItem { ExternalId="t6",  Name="ONION DOSA",              Price=3.50m,  CategoryExternalId="TIFFIN",     SubCategoryExternalId="DOSA",            IsVeg=true  },
            new MenuItem { ExternalId="t7",  Name="KAL DOSA",                Price=2.20m,  CategoryExternalId="TIFFIN",     SubCategoryExternalId="DOSA",            IsVeg=true  },
            new MenuItem { ExternalId="t8",  Name="CHICKEN CURRY DOSA",      Price=8.50m,  CategoryExternalId="TIFFIN",     SubCategoryExternalId="DOSA",            IsVeg=false },
            new MenuItem { ExternalId="t9",  Name="MUTTON CURRY DOSA",       Price=9.50m,  CategoryExternalId="TIFFIN",     SubCategoryExternalId="DOSA",            IsVeg=false },
            new MenuItem { ExternalId="t10", Name="PLAIN UTHAPPAM",          Price=2.20m,  CategoryExternalId="TIFFIN",     SubCategoryExternalId="UTHAPPAM",        IsVeg=true  },
            new MenuItem { ExternalId="t11", Name="ONION UTHAPPAM",          Price=3.50m,  CategoryExternalId="TIFFIN",     SubCategoryExternalId="UTHAPPAM",        IsVeg=true  },
            new MenuItem { ExternalId="t12", Name="EGG UTHAPPAM",            Price=4.00m,  CategoryExternalId="TIFFIN",     SubCategoryExternalId="UTHAPPAM",        IsVeg=false },
            new MenuItem { ExternalId="t13", Name="PLAIN RAVA",              Price=3.50m,  CategoryExternalId="TIFFIN",     SubCategoryExternalId="RAVA",            IsVeg=true  },
            new MenuItem { ExternalId="t14", Name="ONION RAVA",              Price=5.00m,  CategoryExternalId="TIFFIN",     SubCategoryExternalId="RAVA",            IsVeg=true  },
            new MenuItem { ExternalId="t15", Name="EGG RAVA",                Price=5.50m,  CategoryExternalId="TIFFIN",     SubCategoryExternalId="RAVA",            IsVeg=false },
            new MenuItem { ExternalId="t16", Name="MASALA RAVA",             Price=5.50m,  CategoryExternalId="TIFFIN",     SubCategoryExternalId="RAVA",            IsVeg=true  },
            new MenuItem { ExternalId="t17", Name="GHEE DOSAI",              Price=4.00m,  CategoryExternalId="TIFFIN",     SubCategoryExternalId="DOSA",            IsVeg=true  },
            new MenuItem { ExternalId="t18", Name="GHEE ROAST",              Price=5.00m,  CategoryExternalId="TIFFIN",     SubCategoryExternalId="DOSA",            IsVeg=true  },
            new MenuItem { ExternalId="t19", Name="PONGAL",                  Price=2.50m,  CategoryExternalId="TIFFIN",     SubCategoryExternalId="PONGAL",          IsVeg=true  },
            new MenuItem { ExternalId="t20", Name="POORI SET",               Price=3.00m,  CategoryExternalId="TIFFIN",     SubCategoryExternalId="PONGAL",          IsVeg=true  },
            new MenuItem { ExternalId="t21", Name="WHEAT DOSAI",             Price=2.50m,  CategoryExternalId="TIFFIN",     SubCategoryExternalId="DOSA",            IsVeg=true  },
            new MenuItem { ExternalId="t22", Name="MINI IDLY SAMBHAR",       Price=3.20m,  CategoryExternalId="TIFFIN",     SubCategoryExternalId="IDLY",            IsVeg=true  },
            new MenuItem { ExternalId="t23", Name="PAPER ROAST",             Price=4.80m,  CategoryExternalId="TIFFIN",     SubCategoryExternalId="DOSA",            IsVeg=true  },
            new MenuItem { ExternalId="d1",  Name="TEH TARIK",               Price=1.80m,  CategoryExternalId="DRINKS",     SubCategoryExternalId="HOT DRINKS",      IsVeg=true  },
            new MenuItem { ExternalId="d2",  Name="KOPI O",                  Price=1.50m,  CategoryExternalId="DRINKS",     SubCategoryExternalId="HOT DRINKS",      IsVeg=true  },
            new MenuItem { ExternalId="d3",  Name="MILO DINOSAUR",           Price=3.20m,  CategoryExternalId="DRINKS",     SubCategoryExternalId="COLD DRINKS",     IsVeg=true  },
            new MenuItem { ExternalId="d4",  Name="MANGO LASSI",             Price=3.80m,  CategoryExternalId="DRINKS",     SubCategoryExternalId="COLD DRINKS",     IsVeg=true  },
            new MenuItem { ExternalId="d5",  Name="FRESH LIME SODA",         Price=2.50m,  CategoryExternalId="DRINKS",     SubCategoryExternalId="COLD DRINKS",     IsVeg=true  },
            new MenuItem { ExternalId="d6",  Name="MASALA CHAI",             Price=2.00m,  CategoryExternalId="DRINKS",     SubCategoryExternalId="HOT DRINKS",      IsVeg=true  },
            new MenuItem { ExternalId="d7",  Name="TENDER COCONUT",          Price=3.50m,  CategoryExternalId="DRINKS",     SubCategoryExternalId="COLD DRINKS",     IsVeg=true  },
            new MenuItem { ExternalId="d8",  Name="BADAM MILK",              Price=2.80m,  CategoryExternalId="DRINKS",     SubCategoryExternalId="HOT DRINKS",      IsVeg=true  },
            new MenuItem { ExternalId="d9",  Name="ICED LEMON TEA",          Price=2.20m,  CategoryExternalId="DRINKS",     SubCategoryExternalId="COLD DRINKS",     IsVeg=true  },
            new MenuItem { ExternalId="d10", Name="SWEET LASSI",             Price=3.00m,  CategoryExternalId="DRINKS",     SubCategoryExternalId="COLD DRINKS",     IsVeg=true  },
            new MenuItem { ExternalId="d11", Name="BRU FILTER COFFEE",       Price=2.20m,  CategoryExternalId="DRINKS",     SubCategoryExternalId="HOT DRINKS",      IsVeg=true  },
            new MenuItem { ExternalId="m1",  Name="SOUTH INDIAN THALI",      Price=8.50m,  CategoryExternalId="MEALS",      SubCategoryExternalId="VEG MEALS",       IsVeg=true  },
            new MenuItem { ExternalId="m2",  Name="SPECIAL VEG MEALS",       Price=7.00m,  CategoryExternalId="MEALS",      SubCategoryExternalId="VEG MEALS",       IsVeg=true  },
            new MenuItem { ExternalId="m3",  Name="CHICKEN CURRY MEAL",      Price=9.80m,  CategoryExternalId="MEALS",      SubCategoryExternalId="NONVEG MEALS",    IsVeg=false },
            new MenuItem { ExternalId="m4",  Name="FISH CURRY MEAL",         Price=10.50m, CategoryExternalId="MEALS",      SubCategoryExternalId="NONVEG MEALS",    IsVeg=false },
            new MenuItem { ExternalId="m5",  Name="MUTTON MEALS",            Price=12.00m, CategoryExternalId="MEALS",      SubCategoryExternalId="NONVEG MEALS",    IsVeg=false },
            new MenuItem { ExternalId="m6",  Name="SAMBAR RICE",             Price=4.50m,  CategoryExternalId="MEALS",      SubCategoryExternalId="RICE",            IsVeg=true  },
            new MenuItem { ExternalId="m7",  Name="CURD RICE",               Price=3.50m,  CategoryExternalId="MEALS",      SubCategoryExternalId="RICE",            IsVeg=true  },
            new MenuItem { ExternalId="m8",  Name="RASAM RICE",              Price=4.00m,  CategoryExternalId="MEALS",      SubCategoryExternalId="RICE",            IsVeg=true  },
            new MenuItem { ExternalId="b1",  Name="CHICKEN DUM BRIYANI",     Price=9.50m,  CategoryExternalId="BRIYANI",    SubCategoryExternalId="CHICKEN BRIYANI", IsVeg=false },
            new MenuItem { ExternalId="b2",  Name="MUTTON BRIYANI",          Price=12.00m, CategoryExternalId="BRIYANI",    SubCategoryExternalId="MUTTON BRIYANI",  IsVeg=false },
            new MenuItem { ExternalId="b3",  Name="EGG BRIYANI",             Price=7.50m,  CategoryExternalId="BRIYANI",    SubCategoryExternalId="VEG BRIYANI",     IsVeg=false },
            new MenuItem { ExternalId="b4",  Name="VEG BRIYANI",             Price=6.50m,  CategoryExternalId="BRIYANI",    SubCategoryExternalId="VEG BRIYANI",     IsVeg=true  },
            new MenuItem { ExternalId="b5",  Name="PRAWN BRIYANI",           Price=13.50m, CategoryExternalId="BRIYANI",    SubCategoryExternalId="MUTTON BRIYANI",  IsVeg=false },
            new MenuItem { ExternalId="b6",  Name="HYDERABADI BRIYANI",      Price=10.50m, CategoryExternalId="BRIYANI",    SubCategoryExternalId="CHICKEN BRIYANI", IsVeg=false },
            new MenuItem { ExternalId="b7",  Name="KUSKA RICE",              Price=5.00m,  CategoryExternalId="BRIYANI",    SubCategoryExternalId="VEG BRIYANI",     IsVeg=true  },
            new MenuItem { ExternalId="fr1", Name="VEG FRIED RICE",          Price=5.50m,  CategoryExternalId="FRIED RICE", IsVeg=true  },
            new MenuItem { ExternalId="fr2", Name="EGG FRIED RICE",          Price=6.50m,  CategoryExternalId="FRIED RICE", IsVeg=false },
            new MenuItem { ExternalId="fr3", Name="CHICKEN FRIED RICE",      Price=8.00m,  CategoryExternalId="FRIED RICE", IsVeg=false },
            new MenuItem { ExternalId="fr4", Name="SCHEZWAN RICE",           Price=8.50m,  CategoryExternalId="FRIED RICE", IsVeg=true  },
            new MenuItem { ExternalId="fr5", Name="PRAWN FRIED RICE",        Price=10.00m, CategoryExternalId="FRIED RICE", IsVeg=false },
            new MenuItem { ExternalId="fr6", Name="SINGAPORE FRIED RICE",    Price=9.00m,  CategoryExternalId="FRIED RICE", IsVeg=false },
            new MenuItem { ExternalId="ch1", Name="GOBI MANCHURIAN",         Price=6.50m,  CategoryExternalId="CHINESE",    SubCategoryExternalId="STARTERS",        IsVeg=true  },
            new MenuItem { ExternalId="ch2", Name="CHILLI PANEER",           Price=7.50m,  CategoryExternalId="CHINESE",    SubCategoryExternalId="STARTERS",        IsVeg=true  },
            new MenuItem { ExternalId="ch3", Name="CHILLI CHICKEN",          Price=8.50m,  CategoryExternalId="CHINESE",    SubCategoryExternalId="STARTERS",        IsVeg=false },
            new MenuItem { ExternalId="ch4", Name="DRAGON CHICKEN",          Price=9.00m,  CategoryExternalId="CHINESE",    SubCategoryExternalId="STARTERS",        IsVeg=false },
            new MenuItem { ExternalId="ch5", Name="VEG HAKKA NOODLES",       Price=6.00m,  CategoryExternalId="CHINESE",    SubCategoryExternalId="NOODLES",         IsVeg=true  },
            new MenuItem { ExternalId="ch6", Name="CHICKEN SCHEZWAN NOODLES",Price=8.50m,  CategoryExternalId="CHINESE",    SubCategoryExternalId="NOODLES",         IsVeg=false },
            new MenuItem { ExternalId="ws1", Name="GRILLED CHICKEN CHOP",    Price=9.50m,  CategoryExternalId="WESTERN",    IsVeg=false },
            new MenuItem { ExternalId="ws2", Name="FISH AND CHIPS",          Price=10.50m, CategoryExternalId="WESTERN",    IsVeg=false },
            new MenuItem { ExternalId="ws3", Name="FRENCH FRIES",            Price=3.50m,  CategoryExternalId="WESTERN",    IsVeg=true  },
            new MenuItem { ExternalId="ws4", Name="CHICKEN NUGGETS",         Price=4.50m,  CategoryExternalId="WESTERN",    IsVeg=false },
            new MenuItem { ExternalId="ws5", Name="MUSHROOM SOUP",           Price=4.00m,  CategoryExternalId="WESTERN",    IsVeg=true  },
            new MenuItem { ExternalId="hp1", Name="CLAYPOT CHICKEN RICE",    Price=8.50m,  CategoryExternalId="HOT POT",    IsVeg=false },
            new MenuItem { ExternalId="hp2", Name="SEAFOOD CLAYPOT",         Price=12.00m, CategoryExternalId="HOT POT",    IsVeg=false },
            new MenuItem { ExternalId="hp3", Name="HERBAL CHICKEN SOUP",     Price=9.50m,  CategoryExternalId="HOT POT",    IsVeg=false },
            new MenuItem { ExternalId="hp4", Name="TOM YUM SOUP",            Price=8.50m,  CategoryExternalId="HOT POT",    IsVeg=false },
            new MenuItem { ExternalId="p1",  Name="PLAIN PAROTTA (2PC)",     Price=2.50m,  CategoryExternalId="PAROTTA",    SubCategoryExternalId="PLAIN PAROTTA",   IsVeg=true  },
            new MenuItem { ExternalId="p2",  Name="EGG KOTHU PAROTTA",       Price=6.50m,  CategoryExternalId="PAROTTA",    SubCategoryExternalId="KOTHU",           IsVeg=false },
            new MenuItem { ExternalId="p3",  Name="CHICKEN KOTHU",           Price=8.50m,  CategoryExternalId="PAROTTA",    SubCategoryExternalId="KOTHU",           IsVeg=false },
            new MenuItem { ExternalId="p4",  Name="MUTTON KOTHU",            Price=10.50m, CategoryExternalId="PAROTTA",    SubCategoryExternalId="KOTHU",           IsVeg=false },
            new MenuItem { ExternalId="p5",  Name="CEYLON PAROTTA",          Price=7.50m,  CategoryExternalId="PAROTTA",    SubCategoryExternalId="PLAIN PAROTTA",   IsVeg=false },
            new MenuItem { ExternalId="p6",  Name="CHILLI PAROTTA",          Price=6.50m,  CategoryExternalId="PAROTTA",    SubCategoryExternalId="PLAIN PAROTTA",   IsVeg=true  },
            new MenuItem { ExternalId="s1",  Name="MEDU VADA (2PC)",         Price=2.00m,  CategoryExternalId="SNACKS",     IsVeg=true  },
            new MenuItem { ExternalId="s2",  Name="SAMOSA (2PC)",            Price=2.20m,  CategoryExternalId="SNACKS",     IsVeg=true  },
            new MenuItem { ExternalId="s3",  Name="ONION PAKODA",            Price=3.00m,  CategoryExternalId="SNACKS",     IsVeg=true  },
            new MenuItem { ExternalId="s4",  Name="MASALA VADA",             Price=2.00m,  CategoryExternalId="SNACKS",     IsVeg=true  },
            new MenuItem { ExternalId="s5",  Name="BAJJI PLATTER",           Price=4.00m,  CategoryExternalId="SNACKS",     IsVeg=true  },
            new MenuItem { ExternalId="s6",  Name="VEG CUTLET",              Price=2.50m,  CategoryExternalId="SNACKS",     IsVeg=true  },
            new MenuItem { ExternalId="de1", Name="GULAB JAMUN (2PC)",       Price=3.00m,  CategoryExternalId="DESSERTS",   IsVeg=true  },
            new MenuItem { ExternalId="de2", Name="PAYASAM",                 Price=3.50m,  CategoryExternalId="DESSERTS",   IsVeg=true  },
            new MenuItem { ExternalId="de3", Name="RASAMALAI",               Price=4.00m,  CategoryExternalId="DESSERTS",   IsVeg=true  },
            new MenuItem { ExternalId="de4", Name="KESARI",                  Price=2.50m,  CategoryExternalId="DESSERTS",   IsVeg=true  },
            new MenuItem { ExternalId="de5", Name="CARROT HALWA",            Price=3.50m,  CategoryExternalId="DESSERTS",   IsVeg=true  },
            new MenuItem { ExternalId="sp1", Name="CHEF SPL MUTTON SUKKA",   Price=11.50m, CategoryExternalId="SPECIALS",   IsVeg=false },
            new MenuItem { ExternalId="sp2", Name="CHICKEN 65",              Price=7.50m,  CategoryExternalId="SPECIALS",   IsVeg=false },
            new MenuItem { ExternalId="sp3", Name="FISH FRY",                Price=8.50m,  CategoryExternalId="SPECIALS",   IsVeg=false },
            new MenuItem { ExternalId="sp4", Name="PEPPER CHICKEN",          Price=9.00m,  CategoryExternalId="SPECIALS",   IsVeg=false },
            new MenuItem { ExternalId="sp5", Name="CRAB MASALA",             Price=14.00m, CategoryExternalId="SPECIALS",   IsVeg=false },
            new MenuItem { ExternalId="cb1", Name="BREAKFAST COMBO (IDLY+VADA+COFFEE)", Price=4.50m, CategoryExternalId="COMBO", IsVeg=true  },
            new MenuItem { ExternalId="cb2", Name="DOSA COMBO (DOSA+VADA+TEA)",         Price=5.00m, CategoryExternalId="COMBO", IsVeg=true  },
            new MenuItem { ExternalId="cb3", Name="BRIYANI COMBO (BRIYANI+COKE+65)",    Price=13.50m,CategoryExternalId="COMBO", IsVeg=false },
        };

        db.MenuItems.AddRange(items);
        await db.SaveChangesAsync();
    }
}
