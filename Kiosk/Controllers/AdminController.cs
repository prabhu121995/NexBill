using System.Security.Claims;
using Kiosk.Data;
using Kiosk.Models;
using Kiosk.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kiosk.Controllers;

/// <summary>
/// The admin panel. Gated to the roles in <see cref="Roles.AdminRoles"/> — a
/// cashier or waiter who types the URL is bounced to the login page, which sends
/// them back to billing.
/// </summary>
[Authorize(Roles = Roles.AdminRoles)]
[Route("admin")]
public class AdminController(AppDbContext db, BusinessClock clock) : Controller
{
    /// <summary>
    /// Bills that count as takings. A bill absorbed by a merge keeps its lines and
    /// amounts as history and only moves to Status "merged", so every money query
    /// has to filter on "completed" or the same sale is counted twice.
    ///
    /// Live and archived bills together (UNION ALL, see <see cref="OrderHistory"/>):
    /// End of Day moving a bill must not change any figure it appears in.
    /// </summary>
    private IQueryable<OrderRow> Settled => db.AllSettled();

    [HttpGet]
    [Route("")]
    public async Task<IActionResult> Index()
    {
        ViewData["DisplayName"] = User.FindFirstValue(ClaimTypes.GivenName);
        ViewData["RoleCode"]    = User.FindFirstValue(ClaimTypes.Role);
        ViewData["RoleName"]    = User.FindFirstValue(AccountController.RoleNameClaim);
        // Users & Roles greys out what the server refuses for the signed-in account.
        ViewData["UserId"]      = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : 0;

        // The Orders list opens on the cafe's trading day, not the browser's date.
        ViewData["Today"] = clock.Today.ToString("yyyy-MM-dd");

        // Rendered into the Tax & Billing inputs, so the panel opens showing what
        // the tills are actually charging rather than a placeholder.
        var charges = await Charges.LoadAsync(db);
        ViewData["ServiceChargePercent"] = charges.ServiceCharge;
        ViewData["GstPercent"] = charges.Gst;

        // Store Info. The seed guarantees a row; an empty one covers a database
        // restored from before config_store existed.
        ViewData["Store"] = await db.StoreInfo.AsNoTracking().OrderBy(s => s.Id).FirstOrDefaultAsync()
                            ?? new StoreInfo();

        return View();
    }

    /// <summary>
    /// Saves the Store Info card to the single <c>config_store</c> row. App Name
    /// is not part of the request: it is shown read-only and only the seed sets it.
    /// </summary>
    [HttpPost]
    [Route("api/store")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveStore([FromBody] StoreRequest req)
    {
        var storeName = req.StoreName?.Trim() ?? "";
        var address   = req.Address?.Trim() ?? "";
        var phone     = req.Phone?.Trim() ?? "";
        var gstNo     = req.GstNo?.Trim().ToUpperInvariant() ?? "";

        if (storeName.Length == 0) return BadRequest(new { error = "Store name is required" });
        foreach (var (label, value, max) in new[] { ("Store name", storeName, 100), ("Address", address, 250), ("Phone", phone, 20), ("GST No.", gstNo, 15) })
            if (value.Length > max)
                return BadRequest(new { error = $"{label} must be at most {max} characters" });

        var userId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : AuditableEntity.SystemUserId;

        var row = await db.StoreInfo.OrderBy(s => s.Id).FirstOrDefaultAsync();
        if (row is null)
        {
            row = new StoreInfo { AppName = AppBranding.DefaultName, CreatedBy = userId };
            db.StoreInfo.Add(row);
        }
        else
        {
            row.UpdatedBy = userId;
            row.UpdatedAt = DateTime.UtcNow;
        }

        row.StoreName = storeName;
        row.Address   = address;
        row.Phone     = phone;
        row.GstNo     = gstNo;
        await db.SaveChangesAsync();

        return Ok(new { row.StoreName, row.Address, row.Phone, row.GstNo });
    }

    /// <summary>
    /// Saves the service charge and tax rates to <c>config_terminal_setting</c>.
    /// This is the only place they can be changed — the till's own Settings modal
    /// overrides them for the cart in front of it and never writes back, so a
    /// cashier cannot quietly reset the shop's tax rate.
    ///
    /// Upserts rather than requiring the seed to have run: a database restored from
    /// before this table existed would otherwise have nothing to update.
    /// </summary>
    [HttpPost]
    [Route("api/charges")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveCharges([FromBody] ChargesRequest req)
    {
        // decimal(5,2) tops out at 999.99 and a negative rate would credit the
        // customer, so both are refused here rather than overflowing in SQL.
        foreach (var (label, value) in new[] { ("Service charge", req.ServiceCharge), ("Tax", req.Gst) })
            if (value is < 0m or > 100m)
                return BadRequest(new { error = $"{label} must be between 0 and 100" });

        var userId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : AuditableEntity.SystemUserId;

        await UpsertAsync(Charges.ServiceChargeKey, req.ServiceCharge, userId);
        await UpsertAsync(Charges.GstKey, req.Gst, userId);
        await db.SaveChangesAsync();

        return Ok(new { serviceCharge = req.ServiceCharge, gst = req.Gst });
    }

    private async Task UpsertAsync(string key, decimal value, int userId)
    {
        var row = await db.TerminalSettings.FirstOrDefaultAsync(s => s.Service == key);

        if (row is null)
        {
            db.TerminalSettings.Add(new TerminalSetting
            {
                Service   = key,
                Value     = value,
                CreatedBy = userId
            });
            return;
        }

        // Reactivated on save: a row switched off is read as the default, and
        // someone setting the rate plainly means it to apply.
        row.Value     = value;
        row.IsActive  = true;
        row.UpdatedBy = userId;
        row.UpdatedAt = DateTime.UtcNow;
    }

    // ===================== CATEGORIES =====================
    // The left rail on the till. An inactive category drops off the rail along with its items, but its rows stay so past bills and
    // menu items keep pointing at something.

    /// <summary>The virtual category the till uses to list every dish.</summary>
    private const string AllCategoryId = "ALL";

    [HttpGet]
    [Route("api/categories")]
    public async Task<IActionResult> Categories()
    {
        var list = await db.Categories
            .OrderBy(c => c.SortOrder)
            .Select(c => new
            {
                id       = c.Id,
                code     = c.ExternalId,
                name     = c.Name,
                icon     = c.Icon,
                color    = c.Color,
                isActive = c.IsActive,
                // The same counts the till's rail shows under each tile.
                items    = c.ExternalId == AllCategoryId
                    ? db.MenuItems.Count(m => m.IsActive && m.Category != null && m.Category.IsActive)
                    : c.MenuItems.Count(m => m.IsActive)
            })
            .ToListAsync();

        return Ok(list);
    }

    [HttpPost]
    [Route("api/categories")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddCategory([FromBody] CategoryRequest req)
    {
        var existing = await db.Categories.ToListAsync();
        var (name, icon, color, error) = ValidateCategory(req, existing, excludeId: null);
        if (error is not null) return error;

        // No colour chosen: a random one the other active tiles are not already
        // using, so neighbours on the rail stay tellable apart.
        if (color == "")
        {
            var taken = existing.Where(c => c.IsActive).Select(c => c.Color.ToLowerInvariant()).ToHashSet();
            var free  = Category.Palette.Where(p => !taken.Contains(p)).ToArray();
            var pool  = free.Length > 0 ? free : Category.Palette;
            color = pool[Random.Shared.Next(pool.Length)];
        }

        var code = await NewCategoryCodeAsync(name);

        // New categories go in front of "All" so the catch-all stays last on the rail.
        var all = existing.FirstOrDefault(c => c.ExternalId == AllCategoryId);
        int sort;
        if (all is not null)
        {
            sort = all.SortOrder;
            all.SortOrder++;
        }
        else
        {
            sort = existing.Count == 0 ? 1 : existing.Max(c => c.SortOrder) + 1;
        }

        var cat = new Category
        {
            ExternalId = code,
            Name       = name,
            Icon       = icon,
            Color      = color,
            SortOrder  = sort,
            IsActive   = true,
            CreatedBy  = CurrentUserId()
        };
        db.Categories.Add(cat);
        await db.SaveChangesAsync();

        return Ok(new { id = cat.Id, code = cat.ExternalId, name = cat.Name, icon = cat.Icon, color = cat.Color, isActive = cat.IsActive, items = 0 });
    }

    /// <summary>Renames a category or changes its icon / colour. The code stays, so menu items keep pointing at it.</summary>
    [HttpPut]
    [Route("api/categories/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateCategory(int id, [FromBody] CategoryRequest req)
    {
        var existing = await db.Categories.ToListAsync();
        var cat = existing.FirstOrDefault(c => c.Id == id);
        if (cat is null) return NotFound(new { error = "Category not found" });

        var (name, icon, color, error) = ValidateCategory(req, existing, excludeId: id);
        if (error is not null) return error;

        cat.Name      = name;
        cat.Icon      = icon;
        if (color != "") cat.Color = color;
        cat.UpdatedBy = CurrentUserId();
        cat.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return Ok(new { id = cat.Id, name = cat.Name, icon = cat.Icon, color = cat.Color });
    }

    /// <summary>
    /// Shared add/edit checks. The till writes the icon straight into a data-lucide
    /// attribute and the colour into inline styles, so both are held to strict
    /// shapes. An empty colour means "not chosen".
    /// </summary>
    private (string Name, string Icon, string Color, IActionResult? Error) ValidateCategory(
        CategoryRequest req, List<Category> existing, int? excludeId)
    {
        var name  = (req.Name ?? "").Trim();
        var icon  = string.IsNullOrWhiteSpace(req.Icon) ? "utensils" : req.Icon.Trim();
        var color = (req.Color ?? "").Trim().ToLowerInvariant();

        if (name.Length is 0 or > 40)
            return (name, icon, color, BadRequest(new { error = "Name is required (max 40 characters)" }));

        if (icon.Length > 40 || !icon.All(ch => ch is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-'))
            return (name, icon, color, BadRequest(new { error = "Invalid icon" }));

        if (color != "" && !Category.IsValidColor(color))
            return (name, icon, color, BadRequest(new { error = "Colour must be a #rrggbb value" }));

        if (existing.Any(c => c.Id != excludeId && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
            return (name, icon, color, Conflict(new { error = $"A category named \"{name}\" already exists" }));

        return (name, icon, color, null);
    }

    [HttpPost]
    [Route("api/categories/{id:int}/active")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetCategoryActive(int id, [FromBody] CategoryActiveRequest req)
    {
        var cat = await db.Categories.FirstOrDefaultAsync(c => c.Id == id);
        if (cat is null) return NotFound(new { error = "Category not found" });

        cat.IsActive  = req.IsActive;
        cat.UpdatedBy = CurrentUserId();
        cat.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return Ok(new { id = cat.Id, isActive = cat.IsActive });
    }

    /// <summary>
    /// The code is what menu items reference, so it is derived once from the name
    /// and made unique - a renamed category keeps its code.
    /// </summary>
    private async Task<string> NewCategoryCodeAsync(string name)
    {
        var baseCode = new string(name.ToUpperInvariant().Where(ch => char.IsLetterOrDigit(ch) || ch == ' ').ToArray()).Trim();
        if (baseCode.Length == 0) baseCode = "CATEGORY";
        var code = baseCode;
        for (var n = 2; await db.Categories.AnyAsync(c => c.ExternalId == code); n++)
            code = $"{baseCode} {n}";
        return code;
    }

    // ===================== MENU ITEMS =====================
    // The dishes on the till. A dish that has ever been billed or held cannot be
    // deleted - order lines keep its code, and reports group by it - so it is
    // switched off instead. Bills already rung keep their own name and price copy,
    // so editing a dish never rewrites history.

    [HttpGet]
    [Route("api/menu-items")]
    public async Task<IActionResult> MenuItems()
    {
        var items = await db.MenuItems
            .OrderBy(m => m.Category!.SortOrder).ThenBy(m => m.Name)
            .Select(m => new
            {
                id       = m.Id,
                code     = m.ExternalId,
                name     = m.Name,
                price    = m.Price,
                isVeg    = m.IsVeg,
                category = m.CategoryExternalId,
                isActive = m.IsActive
            })
            .ToListAsync();

        // Which codes have history, so the panel can offer delete only where it will work.
        var codes = items.Select(i => i.code).ToList();
        var used = (await db.AllOrderItems().Where(o => codes.Contains(o.MenuItemExternalId)).Select(o => o.MenuItemExternalId).Distinct().ToListAsync())
            .Concat(await db.HeldOrderItems.Where(o => codes.Contains(o.MenuItemExternalId)).Select(o => o.MenuItemExternalId).Distinct().ToListAsync())
            .ToHashSet();

        return Ok(items.Select(i => new
        {
            i.id, i.code, i.name, i.price, i.isVeg, i.category, i.isActive,
            inUse = used.Contains(i.code)
        }));
    }

    [HttpPost]
    [Route("api/menu-items")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddMenuItem([FromBody] MenuItemRequest req)
    {
        var (v, error) = await ValidateMenuItemAsync(req, excludeId: null);
        if (error is not null) return error;

        // Short codes in the style of the seed (t1, d4, cb2). The number only has to
        // be unique; it is never shown to the counter.
        var n = await db.MenuItems.CountAsync() + 1;
        var code = $"m{n}";
        while (await db.MenuItems.AnyAsync(m => m.ExternalId == code)) code = $"m{++n}";

        var item = new MenuItem
        {
            ExternalId         = code,
            Name               = v.Name,
            Price              = v.Price,
            IsVeg              = req.IsVeg,
            CategoryExternalId = v.Category,
            IsActive           = req.IsActive ?? true,
            CreatedBy          = CurrentUserId()
        };
        db.MenuItems.Add(item);
        await db.SaveChangesAsync();

        return Ok(new { id = item.Id, code = item.ExternalId });
    }

    [HttpPut]
    [Route("api/menu-items/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateMenuItem(int id, [FromBody] MenuItemRequest req)
    {
        var item = await db.MenuItems.FirstOrDefaultAsync(m => m.Id == id);
        if (item is null) return NotFound(new { error = "Menu item not found" });

        var (v, error) = await ValidateMenuItemAsync(req, excludeId: id);
        if (error is not null) return error;

        item.Name               = v.Name;
        item.Price              = v.Price;
        item.IsVeg              = req.IsVeg;
        item.CategoryExternalId = v.Category;
        if (req.IsActive is bool active) item.IsActive = active;
        item.UpdatedBy = CurrentUserId();
        item.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return Ok(new { id = item.Id });
    }

    [HttpPost]
    [Route("api/menu-items/{id:int}/active")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetMenuItemActive(int id, [FromBody] CategoryActiveRequest req)
    {
        var item = await db.MenuItems.FirstOrDefaultAsync(m => m.Id == id);
        if (item is null) return NotFound(new { error = "Menu item not found" });

        item.IsActive  = req.IsActive;
        item.UpdatedBy = CurrentUserId();
        item.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return Ok(new { id = item.Id, isActive = item.IsActive });
    }

    [HttpDelete]
    [Route("api/menu-items/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteMenuItem(int id)
    {
        var item = await db.MenuItems.FirstOrDefaultAsync(m => m.Id == id);
        if (item is null) return NotFound(new { error = "Menu item not found" });

        var used = await db.AllOrderItems().AnyAsync(o => o.MenuItemExternalId == item.ExternalId)
                || await db.HeldOrderItems.AnyAsync(o => o.MenuItemExternalId == item.ExternalId);
        if (used)
            return Conflict(new { error = $"{item.Name} is on past or held bills. Deactivate it instead." });

        db.MenuItems.Remove(item);
        await db.SaveChangesAsync();
        return Ok(new { id });
    }

    private record MenuItemValues(string Name, decimal Price, string Category);

    private async Task<(MenuItemValues Values, IActionResult? Error)> ValidateMenuItemAsync(MenuItemRequest req, int? excludeId)
    {
        var name = (req.Name ?? "").Trim();
        var none = new MenuItemValues(name, 0, "");

        if (name.Length is 0 or > 80)
            return (none, BadRequest(new { error = "Name is required (max 80 characters)" }));

        // decimal(10,2): anything past two places would be silently rounded by SQL.
        if (req.Price is not (> 0m and <= 99999.99m) || decimal.Round(req.Price, 2) != req.Price)
            return (none, BadRequest(new { error = "Price must be above 0 with at most 2 decimals" }));

        // "All" is the till's virtual list of every dish, not a home for one.
        var category = await db.Categories.FirstOrDefaultAsync(c =>
            c.ExternalId == req.Category && c.ExternalId != AllCategoryId);
        if (category is null)
            return (none, BadRequest(new { error = "Choose a category" }));

        var clash = await db.MenuItems.AnyAsync(m =>
            m.Id != excludeId && m.CategoryExternalId == category.ExternalId && m.Name == name);
        if (clash)
            return (none, Conflict(new { error = $"{category.Name} already has \"{name}\"" }));

        return (new MenuItemValues(name, req.Price, category.ExternalId), null);
    }

    // ===================== TRANSACTION MODES =====================
    // The Mode dropdown in the till's Transactions popup. Only active ones are
    // offered there; a mode with entries against it can only be deactivated.

    [HttpGet]
    [Route("api/transaction-modes")]
    public async Task<IActionResult> TransactionModes()
    {
        var list = await db.TransactionModes
            .OrderBy(m => m.SortOrder).ThenBy(m => m.Name)
            .Select(m => new
            {
                id       = m.Id,
                name     = m.Name,
                isActive = m.IsActive,
                used     = db.CashTransactions.Count(t => t.ModeId == m.Id)
            })
            .ToListAsync();

        return Ok(list);
    }

    [HttpPost]
    [Route("api/transaction-modes")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddTransactionMode([FromBody] TransactionModeRequest req)
    {
        var (name, error) = await ValidateTransactionModeAsync(req, excludeId: null);
        if (error is not null) return error;

        var sort = await db.TransactionModes.MaxAsync(m => (int?)m.SortOrder) ?? 0;
        var mode = new TransactionMode { Name = name, SortOrder = sort + 1, CreatedBy = CurrentUserId() };
        db.TransactionModes.Add(mode);
        await db.SaveChangesAsync();

        return Ok(new { id = mode.Id });
    }

    /// <summary>Entries point at the row's id, so a rename carries through to them.</summary>
    [HttpPut]
    [Route("api/transaction-modes/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateTransactionMode(int id, [FromBody] TransactionModeRequest req)
    {
        var mode = await db.TransactionModes.FirstOrDefaultAsync(m => m.Id == id);
        if (mode is null) return NotFound(new { error = "Transaction mode not found" });

        var (name, error) = await ValidateTransactionModeAsync(req, excludeId: id);
        if (error is not null) return error;

        mode.Name      = name;
        mode.UpdatedBy = CurrentUserId();
        mode.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return Ok(new { id = mode.Id });
    }

    [HttpPost]
    [Route("api/transaction-modes/{id:int}/active")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetTransactionModeActive(int id, [FromBody] CategoryActiveRequest req)
    {
        var mode = await db.TransactionModes.FirstOrDefaultAsync(m => m.Id == id);
        if (mode is null) return NotFound(new { error = "Transaction mode not found" });

        mode.IsActive  = req.IsActive;
        mode.UpdatedBy = CurrentUserId();
        mode.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return Ok(new { id = mode.Id, isActive = mode.IsActive });
    }

    [HttpDelete]
    [Route("api/transaction-modes/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteTransactionMode(int id)
    {
        var mode = await db.TransactionModes.FirstOrDefaultAsync(m => m.Id == id);
        if (mode is null) return NotFound(new { error = "Transaction mode not found" });

        // Deleted entries count too: they still hold the foreign key.
        var used = await db.CashTransactions.CountAsync(t => t.ModeId == id);
        if (used > 0)
            return Conflict(new { error = $"{mode.Name} has {used} transaction(s). Deactivate it instead." });

        db.TransactionModes.Remove(mode);
        await db.SaveChangesAsync();
        return Ok(new { id });
    }

    private async Task<(string Name, IActionResult? Error)> ValidateTransactionModeAsync(TransactionModeRequest req, int? excludeId)
    {
        var name = (req.Name ?? "").Trim().ToUpperInvariant();
        if (name.Length is 0 or > 30)
            return (name, BadRequest(new { error = "Name is required (max 30 characters)" }));

        if (await db.TransactionModes.AnyAsync(m => m.Id != excludeId && m.Name == name))
            return (name, Conflict(new { error = $"\"{name}\" already exists" }));

        return (name, null);
    }

    private int CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : AuditableEntity.SystemUserId;

    /// <summary>
    /// The dashboard's live figures. Served as its own call rather than baked into
    /// the page so the refresh button re-reads the database without a reload, and
    /// so a panel left open overnight does not keep showing the previous day.
    /// </summary>
    [HttpGet]
    [Route("api/dashboard")]
    public async Task<IActionResult> Dashboard()
    {
        var today     = clock.Today;
        var yesterday = today.AddDays(-1);

        // Both days in one round trip: the cards show today against yesterday.
        var byDay = await Settled
            .Where(o => o.BusinessDate == today || o.BusinessDate == yesterday)
            .GroupBy(o => o.BusinessDate)
            .Select(g => new
            {
                date    = g.Key,
                revenue = g.Sum(o => o.NetTotal),
                orders  = g.Count()
            })
            .ToListAsync();

        var now  = byDay.FirstOrDefault(d => d.date == today);
        var prev = byDay.FirstOrDefault(d => d.date == yesterday);

        var revenue = now?.revenue ?? 0m;
        var orders  = now?.orders ?? 0;

        // Yesterday at zero gives no honest percentage to quote, so the card is told
        // there is no baseline instead of being handed a divide-by-zero or a fake 0%.
        var prevRevenue = prev?.revenue ?? 0m;
        var revenueDelta = prevRevenue > 0
            ? Math.Round((revenue - prevRevenue) / prevRevenue * 100m, 1)
            : (decimal?)null;

        var openOrders = await db.Orders
            .CountAsync(o => o.BusinessDate == today && o.Status == "ordered");

        // Signing in is what marks someone as on shift — there is no shift table.
        // LastLoginAt is UTC, so the local day is matched as a UTC window.
        var (dayFrom, dayTo) = clock.UtcWindow(today);
        var onShift = await db.Users
            .CountAsync(u => u.IsActive && u.LastLoginAt >= dayFrom && u.LastLoginAt < dayTo);
        var activeStaff = await db.Users.CountAsync(u => u.IsActive);

        return Ok(new
        {
            businessDate = today.ToString("yyyy-MM-dd"),
            asOf         = clock.LocalNow,
            revenue      = new { value = revenue, deltaPercent = revenueDelta },
            orders       = new { value = orders, delta = prev is null ? (int?)null : orders - prev.orders },
            avgOrder     = orders > 0 ? Math.Round(revenue / orders, 0, MidpointRounding.AwayFromZero) : 0m,
            staff        = new { onShift, active = activeStaff },
            openOrders,
            hourly       = await HourlyAsync(today),
            topSellers   = await TopSellersAsync(today),
            recent       = await RecentAsync(today)
        });
    }

    /// <summary>
    /// Today's takings bucketed by the hour they were rung, local time. Timestamps
    /// are stored in UTC, so the bucketing is done in memory after converting — a
    /// day's bills are a few hundred rows at most.
    /// </summary>
    private async Task<object[]> HourlyAsync(DateOnly today)
    {
        var sales = await Settled
            .Where(o => o.BusinessDate == today)
            .Select(o => new { o.CreatedAt, o.NetTotal })
            .ToListAsync();

        var buckets = new decimal[24];
        foreach (var s in sales)
            buckets[clock.ToLocal(s.CreatedAt).Hour] += s.NetTotal;

        var hourNow = clock.LocalNow.Hour;
        var sold    = Enumerable.Range(0, 24).Where(h => buckets[h] > 0).ToList();

        // Before the first sale of the day there is nothing to frame the chart with,
        // so it falls back to the hours leading up to now rather than rendering empty.
        var from = sold.Count > 0 ? Math.Min(sold[0], hourNow) : Math.Max(0, hourNow - 8);
        var to   = sold.Count > 0 ? Math.Max(sold[^1], hourNow) : hourNow;

        // One bucket would stretch a single bar across the whole card and a full
        // trading day would squash them past reading, so the window is padded
        // backwards to a readable minimum and capped at half a day.
        const int minSpan = 6, maxSpan = 12;
        if (to - from + 1 < minSpan) from = Math.Max(0, to - (minSpan - 1));
        if (to - from + 1 > maxSpan) from = to - (maxSpan - 1);

        return Enumerable.Range(from, to - from + 1)
            .Select(h => (object)new { label = HourLabel(h), value = buckets[h] })
            .ToArray();
    }

    private static string HourLabel(int hour24) =>
        $"{(hour24 % 12 == 0 ? 12 : hour24 % 12)}{(hour24 < 12 ? "am" : "pm")}";

    /// <summary>Today's best selling lines. FOC lines are given away, so they are not sales.</summary>
    private async Task<object[]> TopSellersAsync(DateOnly today) =>
        (await db.AllOrderItems()
            .Where(i => !i.IsFOC && i.Status == "completed" && i.BusinessDate == today)
            .GroupBy(i => i.Name)
            .Select(g => new { name = g.Key, qty = g.Sum(i => i.Qty) })
            .OrderByDescending(x => x.qty)
            .Take(5)
            .ToListAsync())
        .Cast<object>()
        .ToArray();

    /// <summary>
    /// The latest bills of the day. Merge tombstones are left out: their items now
    /// sit on the combined bill, so listing them would show the order twice.
    /// </summary>
    private async Task<object[]> RecentAsync(DateOnly today)
    {
        var bills = await db.AllOrders()
            .Where(o => o.BusinessDate == today && o.Status != "merged")
            .OrderByDescending(o => o.SeqNo)
            .Take(6)
            .Select(o => new { o.Id, o.SeqNo, o.CustomerName, o.TableNo, o.Status, o.NetTotal, o.CreatedAt })
            .ToListAsync();

        // Lines in a second query: a UNION cannot carry a nested collection.
        var ids   = bills.Select(b => b.Id).ToList();
        var lines = (await db.AllOrderItems()
                .Where(i => ids.Contains(i.OrderId))
                .Select(i => new { i.OrderId, i.Id, i.Name, i.Qty })
                .ToListAsync())
            .OrderBy(i => i.Id)
            .ToLookup(i => i.OrderId);

        return bills.Select(o => (object)new
        {
            id       = o.Id,
            seqNo    = o.SeqNo,
            customer = o.CustomerName,
            tableNo  = o.TableNo,
            status   = o.Status,
            total    = o.NetTotal,
            // Tagged UTC so it serialises with a Z and the browser can render
            // "2 min ago" correctly even on a machine set to another timezone.
            createdAt = DateTime.SpecifyKind(o.CreatedAt, DateTimeKind.Utc),
            // The wall clock the bill was rung at, in the cafe's timezone — which is
            // the till's, not necessarily this browser's.
            localTime = clock.ToLocal(o.CreatedAt).ToString("hh:mm tt"),
            items     = string.Join(", ", lines[o.Id].Select(i => $"{i.Qty}x {i.Name}"))
        })
        .ToArray();
    }

    // ===================== REPORTS =====================

    /// <summary>Longest range one report may cover, so a stray date cannot scan years of bills.</summary>
    private const int MaxReportDays = 366;

    /// <summary>
    /// Sales for a range of trading dates (today when none is given): totals, an
    /// hourly trend of takings and bill counts, and breakdowns by category, cashier
    /// and waiter. Only settled bills count — merge tombstones and open tabs are
    /// left out, the same rule as the dashboard.
    ///
    /// The bills are pulled once and grouped in memory: the hour of day needs the
    /// UTC timestamp converted to the cafe's zone first, and a range of a few
    /// months is a few thousand header rows.
    /// </summary>
    [HttpGet]
    [Route("api/reports")]
    public async Task<IActionResult> Reports(DateOnly? from, DateOnly? to)
    {
        var today = clock.Today;
        var start = from ?? today;
        var end   = to ?? start;

        if (end < start) return BadRequest(new { error = "The From date is after the To date" });
        if (start > today) return BadRequest(new { error = "The range starts in the future" });
        if (end > today) end = today;
        if (end.DayNumber - start.DayNumber + 1 > MaxReportDays)
            return BadRequest(new { error = $"Pick a range of {MaxReportDays} days or less" });

        var inRange = Settled.Where(o => o.BusinessDate >= start && o.BusinessDate <= end);

        var rawBills = await inRange
            .Select(o => new
            {
                o.Id,
                o.CreatedAt,
                o.NetTotal,
                o.DiscountAmount,
                o.GstAmount,
                o.ServiceChargeAmount,
                o.Guests,
                o.WaiterId,
                o.UpdatedBy,
                o.CreatedBy
            })
            .ToListAsync();

        // The cashier is whoever took the money: the first payment row of the bill.
        // Bills settled before payments were recorded fall back to who last touched
        // the bill, and then to who rang it.
        var firstTaker = (await db.AllPayments()
                .Where(p => p.Status == "completed" && p.BusinessDate >= start && p.BusinessDate <= end)
                .Select(p => new { p.OrderId, p.Id, p.ReceivedBy })
                .ToListAsync())
            .GroupBy(p => p.OrderId)
            .ToDictionary(g => g.Key, g => g.MinBy(p => p.Id)!.ReceivedBy);

        var bills = rawBills.Select(o => new
        {
            o.CreatedAt,
            o.NetTotal,
            o.DiscountAmount,
            o.GstAmount,
            o.ServiceChargeAmount,
            o.Guests,
            o.WaiterId,
            Cashier = firstTaker.TryGetValue(o.Id, out var taker) ? taker : o.UpdatedBy ?? o.CreatedBy
        }).ToList();

        // ---- hourly trend ----
        var amount = new decimal[24];
        var count  = new int[24];
        foreach (var b in bills)
        {
            var h = clock.ToLocal(b.CreatedAt).Hour;
            amount[h] += b.NetTotal;
            count[h]++;
        }

        var sold = Enumerable.Range(0, 24).Where(h => count[h] > 0).ToList();
        var hourly = Array.Empty<object>();
        if (sold.Count > 0)
        {
            // Framed on the trading hours actually used, padded to a readable
            // minimum so a single busy hour is not one bar stretched across the card.
            int lo = sold[0], hi = sold[^1];
            const int minSpan = 6;
            while (hi - lo + 1 < minSpan)
            {
                if (lo > 0) lo--;
                if (hi - lo + 1 < minSpan && hi < 23) hi++;
            }
            hourly = Enumerable.Range(lo, hi - lo + 1)
                .Select(h => (object)new { hour = h, label = HourLabel(h), amount = amount[h], orders = count[h] })
                .ToArray();
        }

        // ---- category breakdown ----
        // Line value before the bill's discount, service charge and tax, which are
        // set per bill and cannot be split back onto categories honestly. FOC lines
        // are given away, so they are not sales.
        var lines = await db.AllOrderItems()
            .Where(i => !i.IsFOC && i.Status == "completed"
                        && i.BusinessDate >= start && i.BusinessDate <= end)
            .GroupBy(i => i.MenuItemExternalId)
            .Select(g => new { code = g.Key, qty = g.Sum(i => i.Qty), value = g.Sum(i => i.Qty * i.UnitPrice) })
            .ToListAsync();

        var codes   = lines.Select(l => l.code).ToList();
        var itemCat = await db.MenuItems
            .Where(m => codes.Contains(m.ExternalId))
            .Select(m => new { m.ExternalId, m.CategoryExternalId })
            .ToDictionaryAsync(m => m.ExternalId, m => m.CategoryExternalId);
        var cats = await db.Categories
            .Select(c => new { c.ExternalId, c.Name, c.Color, c.SortOrder })
            .ToDictionaryAsync(c => c.ExternalId);

        var categories = lines
            .GroupBy(l => itemCat.TryGetValue(l.code, out var cat) && cats.ContainsKey(cat) ? cat : "")
            .Select(g =>
            {
                cats.TryGetValue(g.Key, out var c);
                return new
                {
                    // A dish deleted since it was sold has no category left to name.
                    name   = c?.Name ?? "Uncategorised",
                    color  = c?.Color is { Length: > 0 } col ? col : "#64748b",
                    qty    = g.Sum(l => l.qty),
                    amount = g.Sum(l => l.value)
                };
            })
            .OrderByDescending(c => c.amount)
            .ToList();

        // ---- staff ----
        var staffIds = bills.Select(b => b.Cashier)
            .Concat(bills.Where(b => b.WaiterId != null).Select(b => b.WaiterId!.Value))
            .Distinct().ToList();
        var names = await db.Users
            .Where(u => staffIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName);
        string NameOf(int id) =>
            names.TryGetValue(id, out var n) && n.Length > 0 ? n
            : id == AuditableEntity.SystemUserId ? "System" : $"User #{id}";

        var cashiers = bills
            .GroupBy(b => b.Cashier)
            .Select(g => new { name = NameOf(g.Key), orders = g.Count(), amount = g.Sum(b => b.NetTotal) })
            .OrderByDescending(x => x.amount)
            .ToList();

        // Bills with no waiter (take-away, counter sales) are a line of their own
        // so the column still adds up to the range's takings.
        var waiters = bills
            .GroupBy(b => b.WaiterId)
            .Select(g => new
            {
                name   = g.Key is int w ? NameOf(w) : "No waiter",
                none   = g.Key is null,
                orders = g.Count(),
                guests = g.Sum(b => b.Guests),
                amount = g.Sum(b => b.NetTotal)
            })
            .OrderBy(x => x.none).ThenByDescending(x => x.amount)
            .ToList();

        var revenue = bills.Sum(b => b.NetTotal);
        return Ok(new
        {
            from = start.ToString("yyyy-MM-dd"),
            to   = end.ToString("yyyy-MM-dd"),
            summary = new
            {
                revenue,
                orders        = bills.Count,
                avgOrder      = bills.Count > 0 ? Math.Round(revenue / bills.Count, 0, MidpointRounding.AwayFromZero) : 0m,
                itemsSold     = categories.Sum(c => c.qty),
                discount      = bills.Sum(b => b.DiscountAmount),
                gst           = bills.Sum(b => b.GstAmount),
                serviceCharge = bills.Sum(b => b.ServiceChargeAmount)
            },
            hourly,
            categories,
            cashiers,
            waiters
        });
    }

    /// <summary>Rows per page on the Orders and Held lists unless the panel asks otherwise.</summary>
    private const int DefaultPageSize = 20;

    private static (int Page, int Size) Paging(int page, int pageSize) =>
        (Math.Max(1, page), pageSize is >= 5 and <= 100 ? pageSize : DefaultPageSize);

    /// <summary>
    /// One trading day's bills (today when no date is given), newest first, one page
    /// at a time, headers only. Always held to a single day: an every-day listing
    /// scans the whole Orders table, which grows without end. The lines are left
    /// out on purpose: the panel fetches them per bill through <see cref="OrderDetail"/>
    /// when View is pressed, so the list stays light.
    /// </summary>
    [HttpGet]
    [Route("api/orders")]
    public async Task<IActionResult> Orders(DateOnly? date, string? status, string? phone, int page = 1, int pageSize = DefaultPageSize)
    {
        (page, pageSize) = Paging(page, pageSize);

        var day   = date ?? clock.Today;
        var scope = db.AllOrders().Where(o => o.BusinessDate == day);

        // Part of a number is enough, so the last few digits find the bill.
        phone = phone?.Trim();
        if (!string.IsNullOrEmpty(phone)) scope = scope.Where(o => o.CustomerPhone.Contains(phone));

        // Counted before the status filter so every chip shows its own total.
        var counts = await scope
            .GroupBy(o => o.Status)
            .Select(g => new { status = g.Key, count = g.Count() })
            .ToDictionaryAsync(x => x.status, x => x.count);

        if (!string.IsNullOrEmpty(status) && status != "all")
            scope = scope.Where(o => o.Status == status);

        var total = await scope.CountAsync();
        var pages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Min(page, pages);

        // SeqNo restarts every day, so recency is CreatedAt; Id breaks ties.
        var rows = await scope
            .OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        // Names, line counts and merge targets for this page only. Looked up after
        // the UNION rather than joined into it, so the paging query stays a plain
        // scan of two date-indexed tables.
        var (waiterNames, terminalNames) = await NamesForAsync(rows);
        var pageIds = rows.Select(o => o.Id).ToList();
        var itemCounts = await db.AllOrderItems()
            .Where(i => pageIds.Contains(i.OrderId))
            .GroupBy(i => i.OrderId)
            .Select(g => new { g.Key, qty = g.Sum(i => i.Qty) })
            .ToDictionaryAsync(x => x.Key, x => x.qty);
        var mergedSeq = await MergedSeqForAsync(rows);

        return Ok(new
        {
            date = day.ToString("yyyy-MM-dd"),
            page,
            pageSize,
            total,
            pages,
            counts = new
            {
                all       = counts.Values.Sum(),
                ordered   = counts.GetValueOrDefault("ordered"),
                completed = counts.GetValueOrDefault("completed"),
                merged    = counts.GetValueOrDefault("merged")
            },
            orders = rows.Select(o => new
            {
                id         = o.Id,
                seqNo      = o.SeqNo,
                date       = o.BusinessDate.ToString("dd MMM"),
                orderMode  = o.OrderMode,
                tableNo    = o.TableNo,
                guests     = o.Guests,
                customer   = o.CustomerName,
                phone      = o.CustomerPhone,
                status     = o.Status,
                billType   = o.BillType,
                total      = o.NetTotal,
                waiter     = o.WaiterId is int w ? waiterNames.GetValueOrDefault(w) : null,
                terminal   = o.TerminalId is int t ? terminalNames.GetValueOrDefault(t) : null,
                itemCount  = itemCounts.GetValueOrDefault(o.Id),
                mergedInto = o.MergedIntoOrderId is int m && mergedSeq.TryGetValue(m, out var ms) ? ms : (int?)null,
                archived   = o.IsArchived,
                createdAt  = DateTime.SpecifyKind(o.CreatedAt, DateTimeKind.Utc),
                localTime  = clock.ToLocal(o.CreatedAt).ToString("hh:mm tt")
            })
        });
    }

    /// <summary>Waiter and terminal names for a handful of bills, keyed by id.</summary>
    private async Task<(Dictionary<int, string> Waiters, Dictionary<int, string> Terminals)> NamesForAsync(IReadOnlyCollection<OrderRow> bills)
    {
        var waiterIds   = bills.Where(b => b.WaiterId != null).Select(b => b.WaiterId!.Value).Distinct().ToList();
        var terminalIds = bills.Where(b => b.TerminalId != null).Select(b => b.TerminalId!.Value).Distinct().ToList();
        var waiters   = await db.Users.Where(u => waiterIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName);
        var terminals = await db.TerminalConfigs.Where(t => terminalIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name);
        return (waiters, terminals);
    }

    /// <summary>The order number of the bill each merge tombstone was folded into, keyed by that bill's id.</summary>
    private async Task<Dictionary<int, int>> MergedSeqForAsync(IReadOnlyCollection<OrderRow> bills)
    {
        var targets = bills.Where(b => b.MergedIntoOrderId != null).Select(b => b.MergedIntoOrderId!.Value).Distinct().ToList();
        if (targets.Count == 0) return [];
        return await db.AllOrders().Where(o => targets.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.SeqNo);
    }

    /// <summary>One bill with its lines, bill breakdown and payments, for the View dialog.</summary>
    [HttpGet]
    [Route("api/orders/{id:int}")]
    public async Task<IActionResult> OrderDetail(int id)
    {
        // Live or archived — the bill keeps its id across End of Day.
        var o = await db.AllOrders().FirstOrDefaultAsync(x => x.Id == id);
        if (o is null) return NotFound(new { error = "That bill no longer exists" });

        var items    = await db.AllOrderItems().Where(i => i.OrderId == id).OrderBy(i => i.Id).ToListAsync();
        var payments = await db.AllPayments().Where(p => p.OrderId == id).OrderBy(p => p.Id).ToListAsync();
        var (waiterNames, terminalNames) = await NamesForAsync([o]);
        var mergedSeq = await MergedSeqForAsync([o]);

        return Ok(new
        {
            id         = o.Id,
            seqNo      = o.SeqNo,
            orderMode  = o.OrderMode,
            tableNo    = o.TableNo,
            guests     = o.Guests,
            customer   = o.CustomerName,
            phone      = o.CustomerPhone,
            status     = o.Status,
            billType   = o.BillType,
            splitCount = o.SplitCount,
            waiter     = o.WaiterId is int w ? waiterNames.GetValueOrDefault(w) : null,
            terminal   = o.TerminalId is int t ? terminalNames.GetValueOrDefault(t) : null,
            mergedInto = o.MergedIntoOrderId is int m && mergedSeq.TryGetValue(m, out var ms) ? ms : (int?)null,
            archived   = o.IsArchived,
            localTime  = clock.ToLocal(o.CreatedAt).ToString("dd MMM yyyy, hh:mm tt"),
            subTotal        = o.SubTotal,
            discountPercent = o.DiscountPercent,
            discountAmount  = o.DiscountAmount,
            serviceChargePercent = o.ServiceChargePercent,
            serviceChargeAmount  = o.ServiceChargeAmount,
            gstPercent = o.GstPercent,
            gstAmount  = o.GstAmount,
            total      = o.NetTotal,
            items = items.Select(i => new
            {
                code  = i.MenuItemExternalId,
                name  = i.Name,
                qty   = i.Qty,
                price = i.UnitPrice,
                foc   = i.IsFOC,
                note  = i.Note
            }),
            payments = payments.Select(p => new
            {
                method   = p.Method,
                amount   = p.Amount,
                tendered = p.Tendered,
                change   = p.ChangeGiven
            })
        });
    }

    /// <summary>
    /// Carts parked on any till right now. The held table only ever has live
    /// holds — recall or discard deletes the row — so there is no date filter.
    /// </summary>
    [HttpGet]
    [Route("api/held-orders")]
    public async Task<IActionResult> HeldOrders(string? phone, int page = 1, int pageSize = DefaultPageSize)
    {
        (page, pageSize) = Paging(page, pageSize);

        var scope = db.HeldOrders.AsQueryable();
        phone = phone?.Trim();
        if (!string.IsNullOrEmpty(phone)) scope = scope.Where(h => h.CustomerPhone.Contains(phone));

        var total = await scope.CountAsync();
        var pages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Min(page, pages);

        var rows = await scope
            .OrderByDescending(h => h.HeldAt).ThenByDescending(h => h.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(h => new
            {
                h.Id,
                h.BusinessDate,
                h.OrderMode,
                h.TableNo,
                h.Guests,
                h.CustomerName,
                h.CustomerPhone,
                h.NetTotal,
                h.HeldAt,
                waiter    = h.Waiter != null ? h.Waiter.DisplayName : null,
                terminal  = h.Terminal != null ? h.Terminal.Name : null,
                itemCount = h.Items.Sum(i => (int?)i.Qty) ?? 0
            })
            .ToListAsync();

        return Ok(new
        {
            page,
            pageSize,
            total,
            pages,
            held = rows.Select(h => new
            {
                id           = h.Id,
                businessDate = h.BusinessDate.ToString("yyyy-MM-dd"),
                orderMode    = h.OrderMode,
                tableNo      = h.TableNo,
                guests       = h.Guests,
                customer     = h.CustomerName,
                phone        = h.CustomerPhone,
                total        = h.NetTotal,
                waiter       = h.waiter,
                terminal     = h.terminal,
                itemCount    = h.itemCount,
                heldAt       = DateTime.SpecifyKind(h.HeldAt, DateTimeKind.Utc),
                localTime    = clock.ToLocal(h.HeldAt).ToString("hh:mm tt")
            })
        });
    }

    /// <summary>One held cart with its lines, for the View dialog.</summary>
    [HttpGet]
    [Route("api/held-orders/{id:int}")]
    public async Task<IActionResult> HeldOrderDetail(int id)
    {
        var h = await db.HeldOrders.AsNoTracking()
            .Include(x => x.Items)
            .Include(x => x.Waiter)
            .Include(x => x.Terminal)
            .FirstOrDefaultAsync(x => x.Id == id);
        // Recalled or discarded on a till since the list was loaded.
        if (h is null) return NotFound(new { error = "That hold was recalled or discarded" });

        return Ok(new
        {
            id              = h.Id,
            orderMode       = h.OrderMode,
            tableNo         = h.TableNo,
            guests          = h.Guests,
            customer        = h.CustomerName,
            phone           = h.CustomerPhone,
            waiter          = h.Waiter?.DisplayName,
            terminal        = h.Terminal?.Name,
            localTime       = clock.ToLocal(h.HeldAt).ToString("dd MMM yyyy, hh:mm tt"),
            discountPercent = h.DiscountPercent,
            total           = h.NetTotal,
            items = h.Items.OrderBy(i => i.Id).Select(i => new
            {
                code  = i.MenuItemExternalId,
                name  = i.Name,
                qty   = i.Qty,
                price = i.UnitPrice,
                foc   = i.IsFOC,
                note  = i.Note
            })
        });
    }
}

public class CategoryRequest
{
    public string? Name { get; set; }

    /// <summary>A Lucide icon name, as the till's rail renders it.</summary>
    public string? Icon { get; set; }

    /// <summary>#rrggbb; left empty on add to have one picked at random.</summary>
    public string? Color { get; set; }
}

public class MenuItemRequest
{
    public string? Name { get; set; }
    public decimal Price { get; set; }
    public bool IsVeg { get; set; }

    /// <summary>Category code (ExternalId).</summary>
    public string? Category { get; set; }

    /// <summary>Left null on edit to keep the current state.</summary>
    public bool? IsActive { get; set; }
}

public class TransactionModeRequest
{
    public string? Name { get; set; }
}

public class CategoryActiveRequest
{
    public bool IsActive { get; set; }
}

public class StoreRequest
{
    public string? StoreName { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? GstNo { get; set; }
}

public class ChargesRequest
{
    /// <summary>Service charge percentage, 0&#8211;100.</summary>
    public decimal ServiceCharge { get; set; }

    /// <summary>GST / tax percentage, 0&#8211;100.</summary>
    public decimal Gst { get; set; }
}
