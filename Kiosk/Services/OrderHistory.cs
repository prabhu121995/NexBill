using Kiosk.Data;
using Kiosk.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Kiosk.Services;

/// <summary>A bill from either <c>Orders</c> or <c>order_archive</c>, column for column.</summary>
public class OrderRow
{
    public int Id { get; init; }
    public DateOnly BusinessDate { get; init; }
    public int SeqNo { get; init; }
    public string OrderMode { get; init; } = "";
    public string TableNo { get; init; } = "";
    public int Guests { get; init; }
    public string CustomerName { get; init; } = "";
    public string CustomerPhone { get; init; } = "";
    public decimal SubTotal { get; init; }
    public decimal DiscountPercent { get; init; }
    public decimal DiscountAmount { get; init; }
    public decimal ServiceChargePercent { get; init; }
    public decimal ServiceChargeAmount { get; init; }
    public decimal GstPercent { get; init; }
    public decimal GstAmount { get; init; }
    public decimal NetTotal { get; init; }
    public string Status { get; init; } = "";
    public string BillType { get; init; } = "";
    public int SplitCount { get; init; }
    public DateTime CreatedAt { get; init; }
    public int? WaiterId { get; init; }
    public int CreatedBy { get; init; }
    public int? UpdatedBy { get; init; }
    public int? TerminalId { get; init; }
    public int? MergedIntoOrderId { get; init; }

    /// <summary>True when the row came from <c>order_archive</c>.</summary>
    public bool IsArchived { get; init; }
}

/// <summary>
/// A bill line from either table, carrying its bill's date and status so a
/// report can filter lines without joining back to the UNION of the headers.
/// </summary>
public class OrderItemRow
{
    public int Id { get; init; }
    public int OrderId { get; init; }
    public DateOnly BusinessDate { get; init; }
    public string Status { get; init; } = "";
    public string MenuItemExternalId { get; init; } = "";
    public string Name { get; init; } = "";
    public int Qty { get; init; }
    public decimal UnitPrice { get; init; }
    public bool IsFOC { get; init; }
    public string Note { get; init; } = "";
}

/// <summary>A tender from either <c>order_payment</c> or <c>order_payment_archive</c>.</summary>
public class OrderPaymentRow
{
    public int Id { get; init; }
    public int OrderId { get; init; }
    public DateOnly BusinessDate { get; init; }
    public string Status { get; init; } = "";
    public string Method { get; init; } = "";
    public decimal Amount { get; init; }
    public decimal Tendered { get; init; }
    public decimal ChangeGiven { get; init; }
    public int ReceivedBy { get; init; }
}

/// <summary>
/// Bills live in two places: <c>Orders</c> (today's work, kept small) and
/// <c>order_archive</c> (everything End of Day has moved out). Every read of past
/// sales — dashboard, reports, the Orders list, "is this dish / waiter in use" —
/// goes through these queries, which <c>UNION ALL</c> the live and archive tables
/// so a bill counts the same wherever it sits.
///
/// UNION ALL rather than UNION: a bill is in exactly one of the two tables (End
/// of Day inserts and deletes in one transaction), so there are no duplicates to
/// remove, and UNION would make SQL Server sort and compare every row to prove it.
///
/// The till's own writes — new bills, settling, merging, recalling — stay on
/// <c>Orders</c> alone. An archived bill is history and is never edited.
/// </summary>
public static class OrderHistory
{
    public static IQueryable<OrderRow> AllOrders(this AppDbContext db) =>
        db.Orders.Select(o => new OrderRow
        {
            Id = o.Id, BusinessDate = o.BusinessDate, SeqNo = o.SeqNo, OrderMode = o.OrderMode,
            TableNo = o.TableNo, Guests = o.Guests, CustomerName = o.CustomerName, CustomerPhone = o.CustomerPhone,
            SubTotal = o.SubTotal, DiscountPercent = o.DiscountPercent, DiscountAmount = o.DiscountAmount,
            ServiceChargePercent = o.ServiceChargePercent, ServiceChargeAmount = o.ServiceChargeAmount,
            GstPercent = o.GstPercent, GstAmount = o.GstAmount, NetTotal = o.NetTotal, Status = o.Status,
            BillType = o.BillType, SplitCount = o.SplitCount, CreatedAt = o.CreatedAt, WaiterId = o.WaiterId,
            CreatedBy = o.CreatedBy, UpdatedBy = o.UpdatedBy, TerminalId = o.TerminalId,
            MergedIntoOrderId = o.MergedIntoOrderId, IsArchived = false
        })
        .Concat(db.OrderArchives.Select(o => new OrderRow
        {
            Id = o.Id, BusinessDate = o.BusinessDate, SeqNo = o.SeqNo, OrderMode = o.OrderMode,
            TableNo = o.TableNo, Guests = o.Guests, CustomerName = o.CustomerName, CustomerPhone = o.CustomerPhone,
            SubTotal = o.SubTotal, DiscountPercent = o.DiscountPercent, DiscountAmount = o.DiscountAmount,
            ServiceChargePercent = o.ServiceChargePercent, ServiceChargeAmount = o.ServiceChargeAmount,
            GstPercent = o.GstPercent, GstAmount = o.GstAmount, NetTotal = o.NetTotal, Status = o.Status,
            BillType = o.BillType, SplitCount = o.SplitCount, CreatedAt = o.CreatedAt, WaiterId = o.WaiterId,
            CreatedBy = o.CreatedBy, UpdatedBy = o.UpdatedBy, TerminalId = o.TerminalId,
            MergedIntoOrderId = o.MergedIntoOrderId, IsArchived = true
        }));

    /// <summary>Bills that count as takings, from both tables. See <c>AdminController.Settled</c>.</summary>
    public static IQueryable<OrderRow> AllSettled(this AppDbContext db) =>
        db.AllOrders().Where(o => o.Status == "completed");

    public static IQueryable<OrderItemRow> AllOrderItems(this AppDbContext db) =>
        db.OrderItems.Select(i => new OrderItemRow
        {
            Id = i.Id, OrderId = i.OrderId, BusinessDate = i.Order!.BusinessDate, Status = i.Order.Status,
            MenuItemExternalId = i.MenuItemExternalId, Name = i.Name, Qty = i.Qty, UnitPrice = i.UnitPrice,
            IsFOC = i.IsFOC, Note = i.Note
        })
        .Concat(db.OrderItemArchives.Select(i => new OrderItemRow
        {
            Id = i.Id, OrderId = i.OrderId, BusinessDate = i.Order!.BusinessDate, Status = i.Order.Status,
            MenuItemExternalId = i.MenuItemExternalId, Name = i.Name, Qty = i.Qty, UnitPrice = i.UnitPrice,
            IsFOC = i.IsFOC, Note = i.Note
        }));

    public static IQueryable<OrderPaymentRow> AllPayments(this AppDbContext db) =>
        db.OrderPayments.Select(p => new OrderPaymentRow
        {
            Id = p.Id, OrderId = p.OrderId, BusinessDate = p.Order!.BusinessDate, Status = p.Order.Status,
            Method = p.Method, Amount = p.Amount, Tendered = p.Tendered, ChangeGiven = p.ChangeGiven,
            ReceivedBy = p.ReceivedBy
        })
        .Concat(db.OrderPaymentArchives.Select(p => new OrderPaymentRow
        {
            Id = p.Id, OrderId = p.OrderId, BusinessDate = p.Order!.BusinessDate, Status = p.Order.Status,
            Method = p.Method, Amount = p.Amount, Tendered = p.Tendered, ChangeGiven = p.ChangeGiven,
            ReceivedBy = p.ReceivedBy
        }));

    public record EodResult(int Orders, int Items, int Payments, int KeptOpen);

    /// <summary>
    /// End of Day: moves every bill off <c>Orders</c> into <c>order_archive</c>,
    /// with its lines and payments, in one transaction — a bill is never in both
    /// tables and never in neither.
    ///
    /// Today's unpaid bills stay, along with any merge tombstones pointing at
    /// them: they are still on the floor and the till has to be able to recall,
    /// merge and settle them. An unpaid bill from an earlier day is archived as it
    /// is — the till only recalls today's, so leaving it would keep it on
    /// <c>Orders</c> for ever.
    ///
    /// One SQL batch rather than tracked entities: a busy day is thousands of rows,
    /// and loading them only to copy them would be slow for no gain.
    /// </summary>
    public static async Task<EodResult> ArchiveAsync(this AppDbContext db, DateOnly today, int userId)
    {
        var pToday  = new SqlParameter("@today", System.Data.SqlDbType.Date) { Value = today.ToDateTime(TimeOnly.MinValue) };
        var pNow    = new SqlParameter("@now", System.Data.SqlDbType.DateTime2) { Value = DateTime.UtcNow };
        var pBy     = new SqlParameter("@by", userId);
        var pOrders = new SqlParameter("@orders", System.Data.SqlDbType.Int) { Direction = System.Data.ParameterDirection.Output };
        var pItems  = new SqlParameter("@items", System.Data.SqlDbType.Int) { Direction = System.Data.ParameterDirection.Output };
        var pPays   = new SqlParameter("@pays", System.Data.SqlDbType.Int) { Direction = System.Data.ParameterDirection.Output };
        var pKept   = new SqlParameter("@kept", System.Data.SqlDbType.Int) { Direction = System.Data.ParameterDirection.Output };

        const string sql = """
            DECLARE @ids TABLE (Id int PRIMARY KEY);

            -- UPDLOCK/HOLDLOCK: a till settling a bill mid-move waits for this
            -- transaction instead of writing to a row that is about to leave.
            INSERT @ids (Id)
            SELECT o.Id
            FROM [Orders] o WITH (UPDLOCK, HOLDLOCK)
            WHERE NOT (o.BusinessDate = @today AND o.Status = 'ordered')
              AND NOT EXISTS (SELECT 1 FROM [Orders] t
                              WHERE t.Id = o.MergedIntoOrderId
                                AND t.BusinessDate = @today AND t.Status = 'ordered');

            INSERT INTO [order_archive]
                (Id, BusinessDate, SeqNo, OrderMode, TableNo, Guests, CustomerName, CustomerPhone,
                 SubTotal, DiscountPercent, DiscountAmount, ServiceChargePercent, ServiceChargeAmount,
                 GstPercent, GstAmount, NetTotal, Status, BillType, SplitCount, SplitAmount, CreatedAt,
                 WaiterId, CreatedBy, UpdatedBy, UpdatedAt, TerminalId, MergedIntoOrderId,
                 ArchivedAt, ArchivedBy)
            SELECT o.Id, o.BusinessDate, o.SeqNo, o.OrderMode, o.TableNo, o.Guests, o.CustomerName, o.CustomerPhone,
                 o.SubTotal, o.DiscountPercent, o.DiscountAmount, o.ServiceChargePercent, o.ServiceChargeAmount,
                 o.GstPercent, o.GstAmount, o.NetTotal, o.Status, o.BillType, o.SplitCount, o.SplitAmount, o.CreatedAt,
                 o.WaiterId, o.CreatedBy, o.UpdatedBy, o.UpdatedAt, o.TerminalId, o.MergedIntoOrderId,
                 @now, @by
            FROM [Orders] o JOIN @ids x ON x.Id = o.Id;
            SET @orders = @@ROWCOUNT;

            INSERT INTO [order_item_archive] (Id, OrderId, MenuItemExternalId, Name, Qty, UnitPrice, IsFOC, Note)
            SELECT i.Id, i.OrderId, i.MenuItemExternalId, i.Name, i.Qty, i.UnitPrice, i.IsFOC, i.Note
            FROM [OrderItems] i JOIN @ids x ON x.Id = i.OrderId;
            SET @items = @@ROWCOUNT;

            INSERT INTO [order_payment_archive] (Id, OrderId, Method, Amount, Tendered, ChangeGiven, ReceivedBy, PaidAt)
            SELECT p.Id, p.OrderId, p.Method, p.Amount, p.Tendered, p.ChangeGiven, p.ReceivedBy, p.PaidAt
            FROM [order_payment] p JOIN @ids x ON x.Id = p.OrderId;
            SET @pays = @@ROWCOUNT;

            DELETE p FROM [order_payment] p JOIN @ids x ON x.Id = p.OrderId;
            DELETE i FROM [OrderItems] i JOIN @ids x ON x.Id = i.OrderId;
            -- Tombstones point at bills leaving in this same batch; unhook them first
            -- so the self-referencing key does not block the delete.
            UPDATE o SET MergedIntoOrderId = NULL FROM [Orders] o JOIN @ids x ON x.Id = o.Id
            WHERE o.MergedIntoOrderId IS NOT NULL;
            DELETE o FROM [Orders] o JOIN @ids x ON x.Id = o.Id;

            SELECT @kept = COUNT(*) FROM [Orders];
            """;

        // Joins a caller's transaction when there is one, so the move can be part of
        // something larger (or rolled back by a test); otherwise it owns its own.
        await using var tx = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync()
            : null;
        await db.Database.ExecuteSqlRawAsync(sql, pToday, pNow, pBy, pOrders, pItems, pPays, pKept);
        if (tx is not null) await tx.CommitAsync();

        return new EodResult((int)pOrders.Value, (int)pItems.Value, (int)pPays.Value, (int)pKept.Value);
    }
}
