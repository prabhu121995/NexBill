using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using Kiosk.Data;
using Kiosk.Models;
using Kiosk.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kiosk.Controllers;

/// <summary>
/// The till. Signing in is required for every billing screen and billing API, so
/// the gate is on the class and the few actions that must stay open opt out
/// individually — a new billing endpoint is then protected by default.
/// </summary>
[Authorize]
public class HomeController(AppDbContext db, BusinessClock clock, TerminalIdentity machine) : Controller
{
    /// <summary>
    /// Next order number for the current trading day, restarting at 1 daily. Read
    /// across the archive too: End of Day run mid-day empties <c>Orders</c>, and the
    /// next bill must carry on from #023 rather than start again at #001.
    /// </summary>
    private async Task<int> NextSeqForTodayAsync()
    {
        var today = clock.Today;
        return (await db.AllOrders()
            .Where(o => o.BusinessDate == today)
            .MaxAsync(o => (int?)o.SeqNo) ?? 0) + 1;
    }

    /// <summary>The registered, active terminal for this machine, or null if unregistered.</summary>
    private Task<TerminalConfig?> CurrentTerminalAsync() =>
        db.TerminalConfigs.FirstOrDefaultAsync(t => t.MachineGuid == machine.MachineGuid && t.IsActive);

    private static bool IsDuplicateKey(DbUpdateException ex) =>
        ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 };

    /// <summary>
    /// The signed-in staff member's <see cref="User.Id"/>, for the audit columns.
    /// Falls back to <see cref="AuditableEntity.SystemUserId"/> rather than throwing:
    /// a bill must never fail to save because a claim is missing.
    /// </summary>
    private int CurrentUserId =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : AuditableEntity.SystemUserId;

    /// <summary>
    /// Staff who can be picked as the waiter on a bill: active accounts on an active
    /// role that is flagged as serving. Deactivating someone removes them from the
    /// picker while their past sales keep pointing at them.
    /// </summary>
    private IQueryable<User> SelectableWaiters =>
        db.Users
            .Where(u => u.IsActive && u.Role != null && u.Role.IsActive && u.Role.Code == Roles.Waiter)
            .OrderBy(u => u.DisplayName);

    public async Task<IActionResult> Index()
    {
        // Only active categories reach the till, and the items under an inactive one
        // are left out too so they cannot surface under "All" or search.
        var categories = await db.Categories
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder)
            .Select(c => new { id = c.ExternalId, name = c.Name, icon = c.Icon, color = c.Color })
            .ToListAsync();

        var catalog = await db.MenuItems
            .Where(m => m.IsActive && m.Category != null && m.Category.IsActive)
            .OrderBy(m => m.Id)
            .Select(m => new
            {
                id       = m.ExternalId,
                name     = m.Name,
                price    = m.Price,
                category = m.CategoryExternalId,
                isVeg    = m.IsVeg
            })
            .ToListAsync();

        // The rates the shop bills at live in config_terminal_setting, not in the
        // page: a till that shipped its own hardcoded percentages would keep
        // charging them after an admin changed the rate.
        var charges = await Charges.LoadAsync(db);

        var posData = JsonSerializer.Serialize(new
        {
            categories,
            catalog,
            serviceChargePercent = charges.ServiceCharge,
            gstPercent           = charges.Gst
        });
        ViewData["PosData"] = posData;

        // Also as ViewData: the Settings inputs and the summary labels are rendered
        // server side, so they start out agreeing with what the cart is charged.
        ViewData["ServiceChargePercent"] = charges.ServiceCharge;
        ViewData["GstPercent"] = charges.Gst;

        ViewData["NextSeq"] = await NextSeqForTodayAsync();

        var terminal = await CurrentTerminalAsync();

        if (terminal is not null)
        {
            terminal.LastSeenAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        ViewData["TerminalName"] = terminal?.Name ?? "Unregistered";
        ViewData["TerminalCode"] = terminal?.Code ?? "";
        ViewData["MachineGuid"] = machine.MachineGuid;
        ViewData["MachineName"] = machine.MachineName;

        ViewData["DisplayName"] = User.FindFirstValue(ClaimTypes.GivenName);
        ViewData["RoleName"]    = User.FindFirstValue(AccountController.RoleNameClaim);
        ViewData["CanAdmin"]    = Roles.CanAdmin(User.FindFirstValue(ClaimTypes.Role));

        return View();
    }

    /// <summary>Saves the order without taking payment — the guest pays on leaving.</summary>
    [HttpPost]
    [Route("api/billing/order")]
    public Task<IActionResult> PlaceOrder([FromBody] CheckoutRequest req) => PersistAsync(req, "ordered");

    /// <summary>Settles the order. Updates the open row when one exists rather than duplicating it.</summary>
    [HttpPost]
    [Route("api/billing/checkout")]
    public Task<IActionResult> Checkout([FromBody] CheckoutRequest req) => PersistAsync(req, "completed");

    private async Task<IActionResult> PersistAsync(CheckoutRequest req, string status)
    {
        if (req.Items == null || req.Items.Count == 0)
            return BadRequest(new { error = "Cart is empty" });

        var order = req.OrderId is > 0
            ? await db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == req.OrderId)
            : null;

        if (req.OrderId is > 0 && order is null)
            return NotFound(new { error = "Order no longer exists" });

        if (order is not null && order.Status == "completed")
            return Conflict(new { error = "Order is already settled" });

        // The picker is a dropdown, but the id arrives over HTTP like anything else:
        // check it still names a selectable waiter rather than writing it through.
        // Null clears the attribution, which is what a take-away bill wants.
        int? waiterId = req.WaiterId is > 0 ? req.WaiterId : null;
        if (waiterId is not null && !await SelectableWaiters.AnyAsync(u => u.Id == waiterId))
            return BadRequest(new { error = "That waiter is no longer available. Pick another." });

        // Only a settle records payments. The parts must use known methods and add
        // up to the bill, or the drawer and card totals would not reconcile.
        var payments = status == "completed" ? req.Payments ?? [] : [];
        if (payments.Any(p => !OrderPayment.Methods.Contains(p.Method) || p.Amount <= 0))
            return BadRequest(new { error = "Invalid payment line" });
        if (payments.Count > 0 && Math.Abs(payments.Sum(p => p.Amount) - req.NetTotal) > 0.01m)
            return BadRequest(new { error = "Payments do not add up to the bill" });

        var isNew = order is null;

        // Branch on the reference, not on isNew, so nullable analysis can see that
        // order is set from here on.
        if (order is null)
        {
            order = new Order
            {
                BusinessDate = clock.Today,
                TerminalId   = (await CurrentTerminalAsync())?.Id,
                CreatedAt    = DateTime.UtcNow,
                // Who took the order. Set once, on insert only: settling a recalled
                // bill must not rewrite the person who opened it.
                CreatedBy    = CurrentUserId
            };
            db.Orders.Add(order);
        }
        else
        {
            // Who last touched it — in practice whoever settled a bill someone else
            // opened.
            order.UpdatedBy = CurrentUserId;
            order.UpdatedAt = DateTime.UtcNow;
        }

        order.WaiterId = waiterId;

        order.OrderMode             = req.OrderMode ?? "dinein";
        order.TableNo               = req.TableNo ?? "";
        order.Guests                = req.Guests;
        order.CustomerName          = req.CustomerName ?? "Walk-in";
        order.CustomerPhone         = req.CustomerPhone ?? "";
        order.SubTotal              = req.SubTotal;
        order.DiscountPercent       = req.DiscountPercent;
        order.DiscountAmount        = req.DiscountAmount;
        order.ServiceChargePercent  = req.ServiceChargePercent;
        order.ServiceChargeAmount   = req.ServiceChargeAmount;
        order.GstPercent            = req.GstPercent;
        order.GstAmount             = req.GstAmount;
        order.NetTotal              = req.NetTotal;
        order.Status                = status;
        // Only payment shape lives here. A merge is expressed by the source bills
        // moving to Status "merged", never by BillType.
        order.BillType              = req.BillType == "split" ? "split" : "single";

        // Never trust the client's arithmetic: clamp the head count and fall back to
        // an even share so SplitCount x SplitAmount always reconciles to the bill.
        order.SplitCount  = Math.Clamp(req.SplitCount ?? 1, 1, 50);
        order.SplitAmount = req.SplitAmount is > 0
            ? req.SplitAmount.Value
            : Math.Round(req.NetTotal / order.SplitCount, 2, MidpointRounding.AwayFromZero);

        // Guests can add a round later, so the saved lines are replaced wholesale
        // rather than merged.
        db.OrderItems.RemoveRange(order.Items);
        order.Items = req.Items.Select(i => new OrderItem
        {
            MenuItemExternalId = i.Id,
            Name               = i.Name,
            Qty                = i.Qty,
            UnitPrice          = i.UnitPrice,
            IsFOC              = i.IsFOC,
            Note               = i.Note ?? ""
        }).ToList();

        var receivedBy = CurrentUserId;
        var paidAt = DateTime.UtcNow;
        foreach (var p in payments)
        {
            var isCash = p.Method == OrderPayment.Cash;
            order.Payments.Add(new OrderPayment
            {
                Method      = p.Method,
                Amount      = p.Amount,
                Tendered    = isCash ? Math.Max(p.Tendered, p.Amount) : 0,
                ChangeGiven = isCash ? Math.Max(0, p.Tendered - p.Amount) : 0,
                ReceivedBy  = receivedBy,
                PaidAt      = paidAt
            });
        }

        var mergeFrom = (req.MergeFrom ?? []).Distinct().ToList();

        // Saving the bill and absorbing the bills it was merged from must be one
        // unit: a partial commit would leave takings double counted.
        await using var tx = mergeFrom.Count > 0
            ? await db.Database.BeginTransactionAsync()
            : null;

        if (!isNew)
        {
            await db.SaveChangesAsync();
        }
        else
        {
            // Reading the max and inserting are two steps, so a second till can claim
            // the same number in between. The unique index rejects that, and we retry
            // with a freshly read number rather than issuing a duplicate.
            const int maxAttempts = 5;
            for (var attempt = 1; ; attempt++)
            {
                order.SeqNo = await NextSeqForTodayAsync();
                try
                {
                    await db.SaveChangesAsync();
                    break;
                }
                catch (DbUpdateException ex) when (attempt < maxAttempts && IsDuplicateKey(ex))
                {
                    // Number taken a moment ago; loop and take the next one.
                }
            }
        }

        if (mergeFrom.Count > 0)
        {
            var failure = await ApplyMergeAsync(order, mergeFrom);
            if (failure is not null) return Conflict(new { error = failure });

            await db.SaveChangesAsync();
            await tx!.CommitAsync();
        }

        return Ok(new
        {
            orderId      = order.Id,
            seqNo        = order.SeqNo,
            status       = order.Status,
            billType     = order.BillType,
            mergedCount  = mergeFrom.Count,
            businessDate = order.BusinessDate,
            // Settling a recalled bill must not rewind the counter: #010 paid while
            // the till is on #022 leaves the next bill at #022, not #011.
            nextSeq      = await NextSeqForTodayAsync()
        });
    }

    /// <summary>Full detail of one unpaid bill, so the till can load it to collect payment.</summary>
    [HttpGet]
    [Route("api/billing/order/{id:int}")]
    public async Task<IActionResult> GetOrder(int id)
    {
        var o = await db.Orders.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id);
        if (o is null) return NotFound(new { error = "Order not found" });
        if (o.Status != "ordered") return Conflict(new { error = $"That bill is already {o.Status}" });

        return Ok(new
        {
            orderId         = o.Id,
            seqNo           = o.SeqNo,
            orderMode       = o.OrderMode,
            tableNo         = o.TableNo,
            guests          = o.Guests,
            customerName    = o.CustomerName,
            customerPhone   = o.CustomerPhone,
            waiterId        = o.WaiterId,
            discountPercent = o.DiscountPercent,
            netTotal        = o.NetTotal,
            items = o.Items.Select(i => new
            {
                id       = i.MenuItemExternalId,
                name     = i.Name,
                qty      = i.Qty,
                rPrice   = i.UnitPrice,
                modifier = i.Note,
                foc      = i.IsFOC
            })
        });
    }

    /// <summary>
    /// Staff the till may attribute a bill to, for the waiter picker on the settle
    /// dialog. Served as its own call rather than baked into the page so a waiter
    /// added mid-shift shows up without reloading the till.
    /// </summary>
    [HttpGet]
    [Route("api/billing/waiters")]
    public async Task<IActionResult> Waiters() =>
        Ok(await SelectableWaiters
            .Select(u => new { id = u.Id, name = u.DisplayName })
            .ToListAsync());

    // ===================== WAITER PUNCH =====================
    // Waiters never sign in, so the till clocks them: the user icon in the header
    // opens a popup to punch a waiter in, and lists who is in with a punch-out each.
    // The rows go to staff_attendance next to the sign-ins of everyone else.

    /// <summary>
    /// Every selectable waiter with where they stand: in (and since when) or out.
    /// "In" looks back to yesterday so a shift that crossed midnight is still listed.
    /// </summary>
    [HttpGet]
    [Route("api/billing/attendance/waiters")]
    public async Task<IActionResult> WaiterAttendance()
    {
        var since = clock.Today.AddDays(-1);
        var waiters = await SelectableWaiters.Select(u => new { u.Id, u.DisplayName }).ToListAsync();
        var ids = waiters.Select(w => w.Id).ToList();

        // Latest row per waiter. A handful of rows, so grouped in memory.
        var latest = (await db.StaffAttendance
                .Where(a => ids.Contains(a.UserId) && a.BusinessDate >= since)
                .ToListAsync())
            .GroupBy(a => a.UserId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.BusinessDate).First());

        return Ok(waiters.Select(w =>
        {
            latest.TryGetValue(w.Id, out var row);
            var isIn = row is { IsIn: true };
            return new
            {
                id        = w.Id,
                name      = w.DisplayName,
                isIn,
                // Tagged UTC so the browser shows the cafe's wall-clock time.
                firstInAt = isIn ? Utc(row!.FirstInAt) : (DateTime?)null,
                lastInAt  = isIn ? Utc(row!.LastInAt) : (DateTime?)null,
                // A day other than today: the punch-out was forgotten, or it is past midnight.
                fromYesterday = isIn && row!.BusinessDate != clock.Today
            };
        }));
    }

    [HttpPost]
    [Route("api/billing/attendance/punch-in")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PunchIn([FromBody] PunchRequest req, [FromServices] Attendance attendance)
    {
        var waiter = await SelectableWaiters.FirstOrDefaultAsync(u => u.Id == req.WaiterId);
        if (waiter is null) return NotFound(new { error = "Waiter not found or inactive" });

        var open = await attendance.OpenRowAsync(waiter.Id);
        if (open is not null && open.BusinessDate == clock.Today)
            return Conflict(new { error = $"{waiter.DisplayName} is already punched in" });

        await attendance.ClockInAsync(waiter.Id, StaffAttendance.SourcePunch, CurrentUserId);
        return Ok(new { id = waiter.Id, name = waiter.DisplayName });
    }

    /// <summary>
    /// Not limited to selectable waiters: someone deactivated mid-shift still has
    /// to be punched out.
    /// </summary>
    [HttpPost]
    [Route("api/billing/attendance/punch-out")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PunchOut([FromBody] PunchRequest req, [FromServices] Attendance attendance)
    {
        var waiter = await db.Users.FirstOrDefaultAsync(u => u.Id == req.WaiterId && u.Role != null && u.Role.Code == Roles.Waiter);
        if (waiter is null) return NotFound(new { error = "Waiter not found" });

        var row = await attendance.ClockOutAsync(waiter.Id, CurrentUserId);
        if (row is null) return Conflict(new { error = $"{waiter.DisplayName} is not punched in" });

        return Ok(new { id = waiter.Id, name = waiter.DisplayName });
    }

    // ================= SETTLEMENT =================
    // Read-only takings for the shop across all tills. Open to everyone who can sign
    // in to the till, as Transactions is.

    /// <summary>Longest range the popup may cover, so a stray date cannot scan years of bills.</summary>
    private const int MaxSettlementDays = 366;

    private static string PaymodeLabel(string method) => method switch
    {
        OrderPayment.Cash  => "Cash",
        OrderPayment.Card  => "Card",
        OrderPayment.Qr    => "QR / UPI",
        OrderPayment.Split => "Split",
        _                  => method
    };

    /// <summary>
    /// Figures for a range of trading dates, today when none is given. Reads the live
    /// and archive tables, so End of Day does not change them. Only settled bills
    /// count as sales; open bills show as unbilled. Card is the till's only credit
    /// tender, so Sales and Credit Sales together make up the Sales Summary total.
    /// </summary>
    [HttpGet]
    [Route("api/billing/settlement")]
    public async Task<IActionResult> Settlement(DateOnly? from, DateOnly? to)
    {
        var today = clock.Today;
        var start = from ?? today;
        var end   = to ?? start;

        if (end < start) return BadRequest(new { error = "The From date is after the To date" });
        if (start > today) return BadRequest(new { error = "The range starts in the future" });
        if (end > today) end = today;
        if (end.DayNumber - start.DayNumber + 1 > MaxSettlementDays)
            return BadRequest(new { error = $"Pick a range of {MaxSettlementDays} days or less" });

        var bills = await db.AllSettled()
            .Where(o => o.BusinessDate >= start && o.BusinessDate <= end)
            .Select(o => new { o.Id, o.SubTotal, o.DiscountAmount, o.ServiceChargeAmount, o.GstAmount, o.NetTotal, o.Guests })
            .ToListAsync();

        var tenders = await db.AllPayments()
            .Where(p => p.Status == "completed" && p.BusinessDate >= start && p.BusinessDate <= end)
            .Select(p => new { p.OrderId, p.Method, p.Amount })
            .ToListAsync();

        var unbilled = await db.AllOrders()
            .Where(o => o.Status == "ordered" && o.BusinessDate >= start && o.BusinessDate <= end)
            .SumAsync(o => (decimal?)o.NetTotal) ?? 0m;

        var cash = await db.CashTransactions
            .Where(t => t.IsActive && t.BusinessDate >= start && t.BusinessDate <= end)
            .GroupBy(t => t.Mode!.Name)
            .Select(g => new
            {
                paymode = g.Key,
                cashIn  = g.Sum(t => t.Type == CashTransaction.TypeIn ? t.Amount : 0m),
                cashOut = g.Sum(t => t.Type == CashTransaction.TypeOut ? t.Amount : 0m)
            })
            .OrderBy(t => t.paymode)
            .ToListAsync();

        var paymodes = tenders
            .GroupBy(p => p.Method)
            .OrderBy(g => Array.IndexOf(OrderPayment.Methods, g.Key))
            .Select(g => new SettlementPaymode(g.Key, PaymodeLabel(g.Key), g.Sum(p => p.Amount), g.Select(p => p.OrderId).Distinct().Count()))
            .ToList();

        // Bills settled before payments were recorded have no tender rows. They stay
        // visible so the paymode total still reaches Net Sales.
        var paidIds = tenders.Select(p => p.OrderId).ToHashSet();
        var unrecorded = bills.Where(b => !paidIds.Contains(b.Id)).ToList();
        if (unrecorded.Count > 0)
            paymodes.Add(new SettlementPaymode("unrecorded", "Not recorded", unrecorded.Sum(b => b.NetTotal), unrecorded.Count));

        var salesTotal = bills.Sum(b => b.SubTotal);
        var discount   = bills.Sum(b => b.DiscountAmount);
        var service    = bills.Sum(b => b.ServiceChargeAmount);
        var gst        = bills.Sum(b => b.GstAmount);
        var netSales   = bills.Sum(b => b.NetTotal);

        return Ok(new
        {
            from = start.ToString("yyyy-MM-dd"),
            to   = end.ToString("yyyy-MM-dd"),
            asOf = clock.LocalNow.ToString("dd-MM-yyyy hh:mm tt"),
            summary = new
            {
                salesTotal,
                discount,
                serviceCharge = service,
                gst,
                // Each bill's parts are rounded separately, so they can miss its net by a cent.
                roundOff = netSales - (salesTotal - discount + service + gst),
                netSales,
                guests   = bills.Sum(b => b.Guests),
                receipts = bills.Count,
                unbilled
            },
            paymodes,
            transactions = cash,
            sales       = paymodes.Where(p => p.Method != OrderPayment.Card),
            creditSales = paymodes.Where(p => p.Method == OrderPayment.Card),
            discounts   = discount > 0
                ? new[] { new { name = "Bill Discount", amount = discount } }
                : []
        });
    }

    private record SettlementPaymode(string Method, string Paymode, decimal Amount, int Receipts);

    // ================= CASH IN / OUT =================
    // Money moved outside a bill. Open to everyone who can sign in to the till, as
    // the punch screen is. Not part of End of Day: these rows stay in one table.

    /// <summary>Longest range the popup may list, so a stray date cannot scan years of entries.</summary>
    private const int MaxTransactionDays = 366;

    /// <summary>Rows sent to the popup. The totals still cover the whole range.</summary>
    private const int MaxTransactionRows = 500;

    /// <summary>
    /// The next free number for today. Deleted rows are counted, and an edited
    /// number may sit in the same series, so it is checked rather than assumed free.
    /// </summary>
    private async Task<string> NextTransactionNoAsync()
    {
        var prefix = $"TXN-{clock.Today:yyyyMMdd}-";
        var n = await db.CashTransactions.CountAsync(t => t.TransactionNo.StartsWith(prefix)) + 1;
        var number = $"{prefix}{n:D4}";
        while (await db.CashTransactions.AnyAsync(t => t.TransactionNo == number)) number = $"{prefix}{++n:D4}";
        return number;
    }

    [HttpGet]
    [Route("api/billing/transaction-modes")]
    public async Task<IActionResult> ActiveTransactionModes() =>
        Ok(await db.TransactionModes
            .Where(m => m.IsActive)
            .OrderBy(m => m.SortOrder).ThenBy(m => m.Name)
            .Select(m => new { id = m.Id, name = m.Name })
            .ToListAsync());

    [HttpGet]
    [Route("api/billing/transactions/next-number")]
    public async Task<IActionResult> NextTransactionNumber() =>
        Ok(new { number = await NextTransactionNoAsync() });

    /// <summary>Entries for a range of trading dates, today when none is given, newest first.</summary>
    [HttpGet]
    [Route("api/billing/transactions")]
    public async Task<IActionResult> Transactions(DateOnly? from, DateOnly? to)
    {
        var start = from ?? clock.Today;
        var end   = to ?? start;

        if (end < start) return BadRequest(new { error = "The From date is after the To date" });
        if (end.DayNumber - start.DayNumber + 1 > MaxTransactionDays)
            return BadRequest(new { error = $"Pick a range of {MaxTransactionDays} days or less" });

        var scope = db.CashTransactions.Where(t => t.IsActive && t.BusinessDate >= start && t.BusinessDate <= end);

        var totals = await scope
            .GroupBy(t => t.Type)
            .Select(g => new { type = g.Key, amount = g.Sum(t => t.Amount), count = g.Count() })
            .ToListAsync();

        var rows = await scope
            .OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
            .Take(MaxTransactionRows)
            .Select(t => new { t.Id, t.TransactionNo, t.Type, t.ModeId, mode = t.Mode!.Name, t.Amount, t.Remarks, t.CreatedAt, t.CreatedBy })
            .ToListAsync();

        var userIds = rows.Select(r => r.CreatedBy).Distinct().ToList();
        var names = await db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName);

        return Ok(new
        {
            from     = start.ToString("yyyy-MM-dd"),
            to       = end.ToString("yyyy-MM-dd"),
            totalIn  = totals.Where(t => t.type == CashTransaction.TypeIn).Sum(t => t.amount),
            totalOut = totals.Where(t => t.type == CashTransaction.TypeOut).Sum(t => t.amount),
            count    = totals.Sum(t => t.count),
            rows = rows.Select(r => new
            {
                id            = r.Id,
                transactionNo = r.TransactionNo,
                type          = r.Type,
                modeId        = r.ModeId,
                mode          = r.mode,
                amount        = r.Amount,
                remarks       = r.Remarks,
                by            = names.GetValueOrDefault(r.CreatedBy) ?? "System",
                localTime     = clock.ToLocal(r.CreatedAt).ToString("dd MMM, hh:mm tt")
            })
        });
    }

    /// <summary>
    /// An empty number means "generate one": it is drawn here rather than trusting
    /// the one the popup was shown, which another till may have used since.
    /// </summary>
    [HttpPost]
    [Route("api/billing/transactions")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddTransaction([FromBody] CashTransactionRequest? req)
    {
        var (v, error) = await ValidateTransactionAsync(req, existing: null);
        if (error is not null) return error;

        var auto = v.Number.Length == 0;
        var row = new CashTransaction
        {
            TransactionNo = auto ? await NextTransactionNoAsync() : v.Number,
            Type          = v.Type,
            ModeId        = v.ModeId,
            Amount        = v.Amount,
            Remarks       = v.Remarks,
            BusinessDate  = clock.Today,
            CreatedBy     = CurrentUserId
        };
        db.CashTransactions.Add(row);

        // Two tills can draw the same number between the check and the save. The
        // unique index catches it; a generated number simply draws again.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await db.SaveChangesAsync();
                return Ok(new { id = row.Id, transactionNo = row.TransactionNo });
            }
            catch (DbUpdateException ex) when (IsDuplicateKey(ex))
            {
                if (!auto || attempt == 3)
                    return Conflict(new { error = $"Transaction number {row.TransactionNo} is already used" });
                row.TransactionNo = await NextTransactionNoAsync();
            }
        }
    }

    /// <summary>The trading date stays as entered: correcting an entry does not move it to another day.</summary>
    [HttpPut]
    [Route("api/billing/transactions/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateTransaction(int id, [FromBody] CashTransactionRequest? req)
    {
        var row = await db.CashTransactions.FirstOrDefaultAsync(t => t.Id == id && t.IsActive);
        if (row is null) return NotFound(new { error = "That transaction no longer exists" });

        var (v, error) = await ValidateTransactionAsync(req, row);
        if (error is not null) return error;

        row.TransactionNo = v.Number.Length == 0 ? row.TransactionNo : v.Number;
        row.Type          = v.Type;
        row.ModeId        = v.ModeId;
        row.Amount        = v.Amount;
        row.Remarks       = v.Remarks;
        row.UpdatedBy     = CurrentUserId;
        row.UpdatedAt     = DateTime.UtcNow;

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsDuplicateKey(ex))
        {
            return Conflict(new { error = $"Transaction number {row.TransactionNo} is already used" });
        }
        return Ok(new { id = row.Id, transactionNo = row.TransactionNo });
    }

    [HttpDelete]
    [Route("api/billing/transactions/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteTransaction(int id)
    {
        var row = await db.CashTransactions.FirstOrDefaultAsync(t => t.Id == id && t.IsActive);
        if (row is null) return NotFound(new { error = "That transaction no longer exists" });

        row.IsActive  = false;
        row.UpdatedBy = CurrentUserId;
        row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(new { id });
    }

    private record TransactionValues(string Number, string Type, int ModeId, decimal Amount, string Remarks);

    private async Task<(TransactionValues Values, IActionResult? Error)> ValidateTransactionAsync(CashTransactionRequest? req, CashTransaction? existing)
    {
        var none = new TransactionValues("", "", 0, 0, "");

        // A body that is not valid JSON, or an amount that is not a number, binds to null.
        if (req is null) return (none, BadRequest(new { error = "Enter a valid transaction" }));

        var number  = (req.TransactionNo ?? "").Trim();
        var remarks = (req.Remarks ?? "").Trim();

        if (number.Length > 30 || !number.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '/'))
            return (none, BadRequest(new { error = "Transaction number: up to 30 letters, digits, - _ or /" }));

        if (number.Length > 0 && await db.CashTransactions.AnyAsync(t => t.TransactionNo == number && t.Id != (existing == null ? 0 : existing.Id)))
            return (none, Conflict(new { error = $"Transaction number {number} is already used" }));

        if (req.Type is not (CashTransaction.TypeIn or CashTransaction.TypeOut))
            return (none, BadRequest(new { error = "Choose Cash In or Cash Out" }));

        // A mode switched off since the entry was made can stay on that entry.
        if (!await db.TransactionModes.AnyAsync(m => m.Id == req.ModeId && (m.IsActive || (existing != null && m.Id == existing.ModeId))))
            return (none, BadRequest(new { error = "Choose a transaction mode" }));

        // decimal(10,2): anything past two places would be silently rounded by SQL.
        if (req.Amount is not (> 0m and <= 99999.99m) || decimal.Round(req.Amount, 2) != req.Amount)
            return (none, BadRequest(new { error = "Amount must be above 0, up to 99999.99, with at most 2 decimals" }));

        if (remarks.Length > 250)
            return (none, BadRequest(new { error = "Remarks must be at most 250 characters" }));

        return (new TransactionValues(number, req.Type, req.ModeId, req.Amount, remarks), null);
    }

    private static DateTime Utc(DateTime t) => DateTime.SpecifyKind(t, DateTimeKind.Utc);

    /// <summary>Today's unpaid bills, for the recall and merge pickers.</summary>
    [HttpGet]
    [Route("api/billing/open-orders")]
    public async Task<IActionResult> OpenOrders()
    {
        var today = clock.Today;
        var orders = await db.Orders
            .Where(o => o.Status == "ordered" && o.BusinessDate == today)
            .OrderBy(o => o.SeqNo)
            .Select(o => new
            {
                id            = o.Id,
                seqNo         = o.SeqNo,
                tableNo       = o.TableNo,
                orderMode     = o.OrderMode,
                customerName  = o.CustomerName,
                customerPhone = o.CustomerPhone,
                waiterId      = o.WaiterId,
                waiter        = o.Waiter != null ? o.Waiter.DisplayName : null,
                netTotal      = o.NetTotal,
                lines         = o.Items.Count
            })
            .ToListAsync();

        return Ok(orders);
    }

    /// <summary>
    /// Parks the cart in <c>held_order</c>. Unlike <see cref="PlaceOrder"/> this
    /// writes no bill and takes no order number — the cart simply waits until
    /// someone recalls it, on this till or any other.
    /// </summary>
    [HttpPost]
    [Route("api/billing/hold")]
    public async Task<IActionResult> Hold([FromBody] CheckoutRequest req)
    {
        if (req.Items == null || req.Items.Count == 0)
            return BadRequest(new { error = "Cart is empty" });

        // Holding must not fail over a stale waiter: drop the attribution instead,
        // and the picker asks again when the bill is settled.
        int? waiterId = req.WaiterId is > 0 ? req.WaiterId : null;
        if (waiterId is not null && !await SelectableWaiters.AnyAsync(u => u.Id == waiterId))
            waiterId = null;

        var held = new HeldOrder
        {
            BusinessDate    = clock.Today,
            TerminalId      = (await CurrentTerminalAsync())?.Id,
            HeldBy          = CurrentUserId,
            HeldAt          = DateTime.UtcNow,
            OrderMode       = req.OrderMode ?? "dinein",
            TableNo         = req.TableNo ?? "",
            Guests          = req.Guests,
            CustomerName    = req.CustomerName ?? "Walk-in",
            CustomerPhone   = req.CustomerPhone ?? "",
            WaiterId        = waiterId,
            DiscountPercent = req.DiscountPercent,
            NetTotal        = req.NetTotal,
            Items = req.Items.Select(i => new HeldOrderItem
            {
                MenuItemExternalId = i.Id,
                Name               = i.Name,
                Qty                = i.Qty,
                UnitPrice          = i.UnitPrice,
                IsFOC              = i.IsFOC,
                Note               = i.Note ?? ""
            }).ToList()
        };

        db.HeldOrders.Add(held);
        await db.SaveChangesAsync();
        return Ok(new { heldId = held.Id });
    }

    /// <summary>
    /// Every hold still parked, from every till. Not limited to today: a hold left
    /// over at closing should still be there to recall or discard in the morning,
    /// not vanish silently.
    /// </summary>
    [HttpGet]
    [Route("api/billing/held")]
    public async Task<IActionResult> HeldOrders() =>
        Ok(await db.HeldOrders
            .OrderBy(h => h.HeldAt)
            .Select(h => new
            {
                id           = h.Id,
                orderMode    = h.OrderMode,
                tableNo      = h.TableNo,
                customerName = h.CustomerName,
                waiter       = h.Waiter != null ? h.Waiter.DisplayName : null,
                terminal     = h.Terminal != null ? h.Terminal.Code : null,
                heldBy       = db.Users.Where(u => u.Id == h.HeldBy).Select(u => u.DisplayName).FirstOrDefault(),
                heldAt       = h.HeldAt,
                netTotal     = h.NetTotal,
                lines        = h.Items.Count
            })
            .ToListAsync());

    /// <summary>
    /// Takes a hold back onto the till and removes it from <c>held_order</c>. The
    /// cart is read first, then the row is deleted with a single DELETE: when two
    /// tills tap the same hold, only the one whose delete removed the row gets the
    /// cart.
    /// </summary>
    [HttpPost]
    [Route("api/billing/held/{id:int}/recall")]
    public async Task<IActionResult> RecallHeld(int id)
    {
        var h = await db.HeldOrders.AsNoTracking().Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id);
        if (h is null || !await DeleteHeldAsync(id))
            return Conflict(new { error = "That hold was already recalled or discarded" });

        return Ok(new
        {
            heldId          = h.Id,
            orderMode       = h.OrderMode,
            tableNo         = h.TableNo,
            guests          = h.Guests,
            customerName    = h.CustomerName,
            customerPhone   = h.CustomerPhone,
            waiterId        = h.WaiterId,
            discountPercent = h.DiscountPercent,
            items = h.Items.OrderBy(i => i.Id).Select(i => new
            {
                id       = i.MenuItemExternalId,
                name     = i.Name,
                qty      = i.Qty,
                rPrice   = i.UnitPrice,
                modifier = i.Note,
                foc      = i.IsFOC
            })
        });
    }

    /// <summary>Throws a hold away, deleting it and its items.</summary>
    [HttpPost]
    [Route("api/billing/held/{id:int}/discard")]
    public async Task<IActionResult> DiscardHeld(int id) =>
        await DeleteHeldAsync(id)
            ? Ok()
            : Conflict(new { error = "That hold was already recalled or discarded" });

    /// <summary>True only for the caller whose DELETE removed the row. Items go by cascade.</summary>
    private async Task<bool> DeleteHeldAsync(int id) =>
        await db.HeldOrders.Where(h => h.Id == id).ExecuteDeleteAsync() == 1;

    /// <summary>
    /// Returns the lines of the selected unpaid bills so the till can pull them
    /// into the current cart. Writes nothing — the merge is only committed when
    /// the combined bill is saved, so cancelling mid-flow leaves no trace.
    /// </summary>
    [HttpPost]
    [Route("api/billing/merge-preview")]
    public async Task<IActionResult> MergePreview([FromBody] MergeRequest req)
    {
        var ids = (req.OrderIds ?? []).Distinct().ToList();
        if (ids.Count < 1)
            return BadRequest(new { error = "Select at least one bill" });

        var today = clock.Today;
        var bills = await db.Orders
            .Include(o => o.Items)
            .Where(o => ids.Contains(o.Id) && o.Status == "ordered" && o.BusinessDate == today)
            .OrderBy(o => o.SeqNo)
            .ToListAsync();

        if (bills.Count != ids.Count)
            return Conflict(new { error = "One or more bills are no longer open" });

        return Ok(new
        {
            bills = bills.Select(b => new { b.Id, b.SeqNo, b.TableNo, b.Guests, b.NetTotal }),
            guests = bills.Sum(b => b.Guests),
            items = bills.SelectMany(b => b.Items).Select(i => new
            {
                id       = i.MenuItemExternalId,
                name     = i.Name,
                qty      = i.Qty,
                rPrice   = i.UnitPrice,
                modifier = i.Note,
                foc      = i.IsFOC
            })
        });
    }

    /// <summary>
    /// Commits a staged merge onto <paramref name="target"/>. The source bills keep
    /// their lines and amounts as a historical record and only move to
    /// Status "merged". Re-checks status so a bill cannot be merged away twice.
    ///
    /// Because the amounts are retained, the combined bill and its sources both
    /// hold the money: every takings query must filter on Status, which it already
    /// must do to exclude unpaid bills.
    /// </summary>
    private async Task<string?> ApplyMergeAsync(Order target, List<int> sourceIds)
    {
        var today = clock.Today;
        var sources = await db.Orders
            .Where(o => sourceIds.Contains(o.Id) && o.Id != target.Id
                        && o.Status == "ordered" && o.BusinessDate == today)
            .ToListAsync();

        if (sources.Count != sourceIds.Distinct().Count(id => id != target.Id))
            return "One or more bills are no longer open";

        foreach (var src in sources)
        {
            src.Status            = "merged";
            src.MergedIntoOrderId = target.Id;
        }

        return null;
    }

    // ===================== END OF DAY =====================
    // The EOD icon in the till header. Moves every closed bill off Orders into the
    // archive tables so the live tables only ever hold the day in progress. Admin
    // and Supervisor only: it is a shop-wide action, not a till's.

    /// <summary>What End of Day would do now, for the confirm dialog.</summary>
    [HttpGet]
    [Route("api/billing/eod")]
    public async Task<IActionResult> EodPreview()
    {
        if (!Roles.CanAdmin(User.FindFirstValue(ClaimTypes.Role)))
            return StatusCode(403, new { error = "Only Admin or Supervisor can run End of Day" });

        var today = clock.Today;
        var openToday = await db.Orders.CountAsync(o => o.BusinessDate == today && o.Status == "ordered");
        var total = await db.Orders.CountAsync();
        return Ok(new
        {
            businessDate = today.ToString("dd MMM yyyy"),
            toArchive    = total - openToday,
            openToday,
            heldCarts    = await db.HeldOrders.CountAsync()
        });
    }

    [HttpPost]
    [Route("api/billing/eod")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EndOfDay([FromServices] ILogger<HomeController> log)
    {
        if (!Roles.CanAdmin(User.FindFirstValue(ClaimTypes.Role)))
            return StatusCode(403, new { error = "Only Admin or Supervisor can run End of Day" });

        var r = await db.ArchiveAsync(clock.Today, CurrentUserId);
        log.LogInformation("End of Day by user {User}: archived {Orders} bills, {Items} lines, {Payments} payments; {Kept} open bills kept",
            CurrentUserId, r.Orders, r.Items, r.Payments, r.KeptOpen);

        return Ok(new { orders = r.Orders, items = r.Items, payments = r.Payments, keptOpen = r.KeptOpen });
    }

    /// <summary>
    /// The customer-facing screen. Open: the launcher gives that window its own
    /// <c>--user-data-dir</c>, so it has its own cookie jar and can never hold the
    /// till's sign-in. It only ever displays what the till pushes.
    /// </summary>
    [AllowAnonymous]
    [Route("customer-display")]
    public async Task<IActionResult> CustomerDisplay()
    {
        // Only for the labels before the first cart arrives — every push from the
        // till carries the percentages it actually billed at.
        var charges = await Charges.LoadAsync(db);
        ViewData["ServiceChargePercent"] = charges.ServiceCharge;
        ViewData["GstPercent"] = charges.Gst;
        return View();
    }

    /// <summary>Open for the same reason as the customer screen; already loopback-only.</summary>
    [AllowAnonymous]
    [HttpGet]
    [Route("api/screens")]
    public IActionResult Screens()
    {
        // These are the SERVER's monitors, which only describe the caller when
        // both run on the same till. Remote callers (IIS, domain) get nothing
        // rather than a layout belonging to another machine.
        var remoteIp = HttpContext.Connection.RemoteIpAddress;
        if (remoteIp is null || !System.Net.IPAddress.IsLoopback(remoteIp))
            return Ok(Array.Empty<Kiosk.Services.ScreenBounds>());

        return Ok(Kiosk.Services.ScreenInfo.All());
    }

    /// <summary>
    /// Ends the shift and closes the kiosk windows the launcher opened — the power
    /// button's real shutdown, since Chrome blocks <c>window.close()</c> on a window
    /// it did not open from script.
    ///
    /// Loopback only: closing browser windows is a local act and must not be
    /// reachable from the network.
    ///
    /// <c>[AllowAnonymous]</c> because the login screen carries a power button too —
    /// a terminal nobody has signed in to still has to be closable, and the shift
    /// such a call would end is already over. The antiforgery token is what takes
    /// over from <c>[Authorize]</c> as the cross-site guard, and it has to: loopback
    /// says nothing about who asked, since a page on any site can have the local
    /// browser post here. What that page cannot do is read this origin's token or
    /// set the header carrying it, so the token is the check that actually holds.
    /// The till's own power button posts it as well, where before it relied on the
    /// auth cookie being <c>SameSite=Lax</c>.
    ///
    /// The service itself stays up, so the next launch is instant.
    /// </summary>
    [AllowAnonymous]
    [HttpPost]
    [Route("api/terminal/close")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CloseTerminal([FromServices] KioskSession session,
                                                   [FromServices] Attendance attendance,
                                                   [FromServices] DisplayHub hub,
                                                   [FromServices] ILogger<HomeController> log)
    {
        var remoteIp = HttpContext.Connection.RemoteIpAddress;
        if (remoteIp is null || !System.Net.IPAddress.IsLoopback(remoteIp))
            return Forbid();

        // Closing the terminal is a sign-out as far as the shift's hours go.
        await AccountController.ClockOutCurrentUserAsync(HttpContext, attendance, log);

        // Without this the next launch's display replays the abandoned order.
        hub.ClearFor(User.FindFirstValue(ClaimTypes.NameIdentifier));

        // Closing the terminal ends the shift. Without this the next launch reopens
        // straight onto the previous cashier's till, which is both a surprise and a
        // way for one person's sign-in to ring up another person's bills. A no-op
        // when the call came from the login screen: there is no cookie to clear.
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        // Resolve now, close later. The answer has to say whether there is really a
        // live window to close, not merely whether a pid file is lying around — a
        // stale file would have the till skip its fallback and appear to do nothing.
        var windows = session.ResolveOpenWindows();

        if (windows.Count == 0)
            // Started by hand, hosted remotely, or the windows are already gone. The
            // sign-out above still stands and the page sends itself to /login.
            return Ok(new { launcherSession = false });

        // Deliberately after the response: closing the till mid-reply would leave the
        // page unable to tell a successful close from a dropped request. The count
        // goes to the log rather than the body for the same reason — by the time
        // there is a number, the caller is gone.
        Response.OnCompleted(() =>
        {
            session.Close(windows);
            return Task.CompletedTask;
        });

        return Ok(new { launcherSession = true });
    }

    [HttpPost]
    [Route("api/display/state")]
    public async Task<IActionResult> PushDisplayState([FromServices] DisplayHub hub)
    {
        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(payload)) return BadRequest();

        // Cart updates are retained for replay; one-off events like "paid" are not.
        using var doc = JsonDocument.Parse(payload);
        var type = doc.RootElement.TryGetProperty("type", out var t) ? t.GetString() : "update";
        hub.Publish(payload, type == "update", User.FindFirstValue(ClaimTypes.NameIdentifier));

        return Ok();
    }

    /// <summary>Subscribed to by the customer screen, which has no sign-in — see above.</summary>
    [AllowAnonymous]
    [HttpGet]
    [Route("api/display/stream")]
    public async Task DisplayStream([FromServices] DisplayHub hub, CancellationToken ct)
    {
        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        var (id, reader) = hub.Subscribe();
        try
        {
            await WriteSse(hub.Snapshot, ct);
            await foreach (var message in reader.ReadAllAsync(ct))
                await WriteSse(message, ct);
        }
        catch (OperationCanceledException) { /* display window closed */ }
        finally
        {
            hub.Unsubscribe(id);
        }
    }

    private async Task WriteSse(string json, CancellationToken ct)
    {
        await Response.WriteAsync($"data: {json}\n\n", ct);
        await Response.Body.FlushAsync(ct);
    }

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    /// <summary>
    /// Frameless page for 404/403/401 and friends, re-executed into by
    /// <c>UseStatusCodePagesWithReExecute</c>. A kiosk window has no address bar,
    /// so the browser's own error page would trap staff with no way out.
    /// </summary>
    [AllowAnonymous]
    [Route("error/{code:int}")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Status(int code) => View("Status", StatusViewModel.For(code));
}

public class PunchRequest
{
    public int WaiterId { get; set; }
}

public class CashTransactionRequest
{
    /// <summary>Empty to have one generated.</summary>
    public string? TransactionNo { get; set; }

    /// <summary><see cref="CashTransaction.TypeIn"/> or <see cref="CashTransaction.TypeOut"/>.</summary>
    public string? Type { get; set; }

    public int ModeId { get; set; }
    public decimal Amount { get; set; }
    public string? Remarks { get; set; }
}

public class MergeRequest
{
    public List<int>? OrderIds { get; set; }

    /// <summary>Tax rates from the till that is doing the merge.</summary>
    public decimal ServiceChargePercent { get; set; }
    public decimal GstPercent { get; set; }

    public string? TableNo { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
}

public class CheckoutRequest
{
    /// <summary>Existing order to update; null or 0 creates a new one.</summary>
    public int? OrderId { get; set; }

    /// <summary>Bills staged for merge, committed only when this bill is saved.</summary>
    public List<int>? MergeFrom { get; set; }

    /// <summary>"single", "split" or "merge". Anything else is stored as "single".</summary>
    public string? BillType { get; set; }

    public int? SplitCount { get; set; }
    public decimal? SplitAmount { get; set; }

    public string? OrderMode { get; set; }
    public string? TableNo { get; set; }
    public int Guests { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }

    /// <summary>The waiter the bill is attributed to. Null or 0 means none.</summary>
    public int? WaiterId { get; set; }

    public decimal SubTotal { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal ServiceChargePercent { get; set; }
    public decimal ServiceChargeAmount { get; set; }
    public decimal GstPercent { get; set; }
    public decimal GstAmount { get; set; }
    public decimal NetTotal { get; set; }
    public List<CheckoutItem>? Items { get; set; }

    /// <summary>How the bill is being paid, one line per method. Read on settle only.</summary>
    public List<PaymentLine>? Payments { get; set; }
}

public class PaymentLine
{
    public string Method { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal Tendered { get; set; }
}

public class CheckoutItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public bool IsFOC { get; set; }
    public string? Note { get; set; }
}
