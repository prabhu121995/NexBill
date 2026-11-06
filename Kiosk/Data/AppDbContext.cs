using Kiosk.Models;
using Microsoft.EntityFrameworkCore;

namespace Kiosk.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<MenuItem> MenuItems => Set<MenuItem>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<TerminalConfig> TerminalConfigs => Set<TerminalConfig>();
    public DbSet<RoleConfig> Roles => Set<RoleConfig>();
    public DbSet<TerminalSetting> TerminalSettings => Set<TerminalSetting>();
    public DbSet<User> Users => Set<User>();
    public DbSet<HeldOrder> HeldOrders => Set<HeldOrder>();
    public DbSet<HeldOrderItem> HeldOrderItems => Set<HeldOrderItem>();
    public DbSet<OrderPayment> OrderPayments => Set<OrderPayment>();
    public DbSet<StaffAttendance> StaffAttendance => Set<StaffAttendance>();
    public DbSet<StoreInfo> StoreInfo => Set<StoreInfo>();
    public DbSet<OrderArchive> OrderArchives => Set<OrderArchive>();
    public DbSet<OrderItemArchive> OrderItemArchives => Set<OrderItemArchive>();
    public DbSet<OrderPaymentArchive> OrderPaymentArchives => Set<OrderPaymentArchive>();
    public DbSet<TransactionMode> TransactionModes => Set<TransactionMode>();
    public DbSet<CashTransaction> CashTransactions => Set<CashTransaction>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<MenuItem>()
            .HasOne(m => m.Category)
            .WithMany(c => c.MenuItems)
            .HasForeignKey(m => m.CategoryExternalId)
            .HasPrincipalKey(c => c.ExternalId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Entity<Category>()
            .HasIndex(c => c.ExternalId)
            .IsUnique();

        // Cart lines, held bills and order lines all match a dish by this code, so two
        // dishes sharing one would merge on the bill.
        b.Entity<MenuItem>().Property(m => m.ExternalId).HasMaxLength(50);
        b.Entity<MenuItem>().HasIndex(m => m.ExternalId).IsUnique();

        b.Entity<Category>().Property(c => c.Color).HasMaxLength(7).HasDefaultValue("");

        // Order numbers restart each trading day, so they are only unique within
        // a date. The index is what actually prevents two tills claiming one number.
        b.Entity<Order>()
            .HasIndex(o => new { o.BusinessDate, o.SeqNo })
            .IsUnique();

        // Self reference: an absorbed bill points at the merged bill holding its items.
        b.Entity<Order>()
            .HasOne(o => o.MergedInto)
            .WithMany()
            .HasForeignKey(o => o.MergedIntoOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Entity<Order>().HasIndex(o => o.MergedIntoOrderId);
        b.Entity<Order>().HasIndex(o => new { o.BusinessDate, o.Status });

        b.Entity<Order>().Property(o => o.BillType).HasMaxLength(10).HasDefaultValue("single");
        b.Entity<Order>().Property(o => o.SplitCount).HasDefaultValue(1);
        b.Entity<Order>().Property(o => o.SplitAmount).HasColumnType("decimal(10,2)");

        // Restrict: a terminal that has rung sales must not be deletable, or the
        // orders would lose which till produced them.
        b.Entity<Order>()
            .HasOne(o => o.Terminal)
            .WithMany()
            .HasForeignKey(o => o.TerminalId)
            .OnDelete(DeleteBehavior.Restrict);

        // Same reasoning for the waiter: a staff member with sales against their
        // name stays in the table. Deactivate them instead, which keeps them out of
        // the picker without rewriting history.
        b.Entity<Order>()
            .HasOne(o => o.Waiter)
            .WithMany()
            .HasForeignKey(o => o.WaiterId)
            .OnDelete(DeleteBehavior.Restrict);

        // "Today's bills for one waiter" is the shape every service report asks for.
        b.Entity<Order>().HasIndex(o => new { o.BusinessDate, o.WaiterId });

        // No column type on UpdatedAt: Orders.CreatedAt is datetime2, so the pair
        // match each other here rather than the datetime used on the master tables.
        b.Entity<Order>().Property(o => o.CreatedBy).HasDefaultValue(AuditableEntity.SystemUserId);

        b.Entity<TerminalConfig>(e =>
        {
            e.ToTable("config_terminal");
            e.HasIndex(t => t.MachineGuid).IsUnique();
            e.HasIndex(t => t.Code).IsUnique();
            e.Property(t => t.Code).HasMaxLength(10);
            e.Property(t => t.Name).HasMaxLength(50);
            e.Property(t => t.MachineGuid).HasMaxLength(64);
            e.Property(t => t.MachineName).HasMaxLength(100);
        });

        b.Entity<RoleConfig>(e =>
        {
            e.ToTable("config_role");
            e.HasIndex(r => r.Code).IsUnique();
            e.Property(r => r.Code).HasMaxLength(20);
            e.Property(r => r.Name).HasMaxLength(50);
        });

        b.Entity<TerminalSetting>(e =>
        {
            e.ToTable("config_terminal_setting");
            // Unique: the key is how every caller finds its row, so two rows for
            // one charge would make the rate the shop bills at a coin toss.
            e.HasIndex(s => s.Service).IsUnique();
            e.Property(s => s.Service).HasMaxLength(30);
            e.Property(s => s.Value).HasColumnType("decimal(5,2)");
        });

        // ---- End of Day archive ----
        // Column types are spelled out to match Orders / OrderItems / order_payment
        // exactly: OrderHistory UNIONs each pair, and EF refuses a set operation
        // whose two sides map a column to different store types.
        b.Entity<OrderArchive>(e =>
        {
            e.ToTable("order_archive");
            // Copied from the live row, never generated.
            e.Property(o => o.Id).ValueGeneratedNever();
            e.Property(o => o.BusinessDate).HasColumnType("date");
            e.Property(o => o.OrderMode).HasColumnType("nvarchar(max)");
            e.Property(o => o.TableNo).HasColumnType("nvarchar(max)");
            e.Property(o => o.CustomerName).HasColumnType("nvarchar(max)");
            e.Property(o => o.CustomerPhone).HasColumnType("nvarchar(max)");
            e.Property(o => o.Status).HasColumnType("nvarchar(450)");
            e.Property(o => o.BillType).HasColumnType("nvarchar(10)").HasMaxLength(10);
            foreach (var p in new[] { nameof(OrderArchive.SubTotal), nameof(OrderArchive.DiscountAmount),
                                      nameof(OrderArchive.ServiceChargeAmount), nameof(OrderArchive.GstAmount),
                                      nameof(OrderArchive.NetTotal), nameof(OrderArchive.SplitAmount) })
                e.Property(p).HasColumnType("decimal(10,2)");
            foreach (var p in new[] { nameof(OrderArchive.DiscountPercent), nameof(OrderArchive.ServiceChargePercent),
                                      nameof(OrderArchive.GstPercent) })
                e.Property(p).HasColumnType("decimal(5,2)");
            e.Property(o => o.CreatedAt).HasColumnType("datetime2");
            e.Property(o => o.UpdatedAt).HasColumnType("datetime2");
            e.Property(o => o.ArchivedAt).HasColumnType("datetime2");

            // Non-clustered, newest trading day first: every history read is "a day"
            // or "a range of days", and the recent end is what gets asked for.
            e.HasIndex(o => new { o.BusinessDate, o.Status })
             .IsDescending(true, false)
             .HasDatabaseName("IX_order_archive_BusinessDate_Status");
            e.HasIndex(o => new { o.BusinessDate, o.WaiterId })
             .IsDescending(true, false)
             .HasDatabaseName("IX_order_archive_BusinessDate_WaiterId");
            e.HasIndex(o => o.MergedIntoOrderId);

            // Restrict, as on Orders: a waiter or till with archived sales stays.
            e.HasOne(o => o.Waiter).WithMany().HasForeignKey(o => o.WaiterId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(o => o.Terminal).WithMany().HasForeignKey(o => o.TerminalId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<OrderItemArchive>(e =>
        {
            e.ToTable("order_item_archive");
            e.Property(i => i.Id).ValueGeneratedNever();
            e.Property(i => i.MenuItemExternalId).HasColumnType("nvarchar(max)");
            e.Property(i => i.Name).HasColumnType("nvarchar(max)");
            e.Property(i => i.Note).HasColumnType("nvarchar(max)");
            e.Property(i => i.UnitPrice).HasColumnType("decimal(10,2)");
            e.HasOne(i => i.Order).WithMany(o => o.Items).HasForeignKey(i => i.OrderId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<OrderPaymentArchive>(e =>
        {
            e.ToTable("order_payment_archive");
            e.Property(p => p.Id).ValueGeneratedNever();
            e.Property(p => p.Method).HasColumnType($"nvarchar({OrderPayment.MethodMaxLength})").HasMaxLength(OrderPayment.MethodMaxLength);
            e.Property(p => p.Amount).HasColumnType("decimal(10,2)");
            e.Property(p => p.Tendered).HasColumnType("decimal(10,2)");
            e.Property(p => p.ChangeGiven).HasColumnType("decimal(10,2)");
            e.Property(p => p.PaidAt).HasColumnType("datetime2");
            e.HasIndex(p => new { p.PaidAt, p.Method })
             .IsDescending(true, false)
             .HasDatabaseName("IX_order_payment_archive_PaidAt_Method");
            e.HasOne(p => p.Order).WithMany(o => o.Payments).HasForeignKey(p => p.OrderId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<StoreInfo>(e =>
        {
            e.ToTable("config_store");
            e.Property(s => s.AppName).HasMaxLength(50);
            e.Property(s => s.StoreName).HasMaxLength(100);
            e.Property(s => s.Address).HasMaxLength(250);
            e.Property(s => s.Phone).HasMaxLength(20);
            e.Property(s => s.GstNo).HasMaxLength(15);
            e.Property(s => s.CurrencySymbol).HasMaxLength(5).HasDefaultValue("");
            e.Property(s => s.PayNowType).HasMaxLength(10).HasDefaultValue("uen");
            e.Property(s => s.PayNowProxy).HasMaxLength(60).HasDefaultValue("");
            e.Property(s => s.PayNowMerchantName).HasMaxLength(25).HasDefaultValue("");
        });

        b.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasIndex(u => u.Username).IsUnique();
            e.Property(u => u.Username).HasMaxLength(50);
            e.Property(u => u.DisplayName).HasMaxLength(100);
            e.Property(u => u.Phone).HasMaxLength(25);
            e.Property(u => u.Password).HasMaxLength(100);

            // Restrict: a role that still has staff attached must not be deletable.
            e.HasOne(u => u.Role)
             .WithMany(r => r.Users)
             .HasForeignKey(u => u.RoleId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<HeldOrder>(e =>
        {
            e.ToTable("held_order");
            // The Held list is read oldest first.
            e.HasIndex(h => h.HeldAt);
            e.Property(h => h.OrderMode).HasMaxLength(10);
            e.Property(h => h.DiscountPercent).HasColumnType("decimal(5,2)");
            e.Property(h => h.NetTotal).HasColumnType("decimal(10,2)");
            e.Property(h => h.HeldBy).HasDefaultValue(AuditableEntity.SystemUserId);

            // Restrict for the same reasons as on Orders: a till or waiter with
            // history against it is deactivated, not deleted.
            e.HasOne(h => h.Terminal).WithMany().HasForeignKey(h => h.TerminalId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(h => h.Waiter).WithMany().HasForeignKey(h => h.WaiterId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<HeldOrderItem>(e =>
        {
            e.ToTable("held_order_item");
            e.Property(i => i.UnitPrice).HasColumnType("decimal(10,2)");
            e.HasOne(i => i.HeldOrder).WithMany(h => h.Items).HasForeignKey(i => i.HeldOrderId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<OrderPayment>(e =>
        {
            e.ToTable("order_payment");
            e.Property(p => p.Method).HasMaxLength(OrderPayment.MethodMaxLength);
            e.Property(p => p.Amount).HasColumnType("decimal(10,2)");
            e.Property(p => p.Tendered).HasColumnType("decimal(10,2)");
            e.Property(p => p.ChangeGiven).HasColumnType("decimal(10,2)");
            e.Property(p => p.ReceivedBy).HasDefaultValue(AuditableEntity.SystemUserId);
            // "Cash taken today" is the drawer count at close.
            e.HasIndex(p => new { p.Method, p.PaidAt });
            e.HasOne(p => p.Order).WithMany(o => o.Payments).HasForeignKey(p => p.OrderId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<StaffAttendance>(e =>
        {
            e.ToTable("staff_attendance");
            // One row per person per day. The index is also what stops two tills
            // racing to create the same day twice.
            e.HasIndex(a => new { a.UserId, a.BusinessDate }).IsUnique();
            // The admin panel reads one day at a time.
            e.HasIndex(a => a.BusinessDate);
            e.Property(a => a.Source).HasMaxLength(10);
            e.Property(a => a.InCount).HasDefaultValue(1);
            e.Property(a => a.InBy).HasDefaultValue(AuditableEntity.SystemUserId);
            e.Ignore(a => a.IsIn);

            // Restrict, like the waiter on a bill: someone with hours on record is
            // deactivated, not deleted.
            e.HasOne(a => a.User).WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<TransactionMode>(e =>
        {
            e.ToTable("config_transaction_mode");
            e.HasIndex(m => m.Name).IsUnique();
            e.Property(m => m.Name).HasMaxLength(30);
        });

        b.Entity<CashTransaction>(e =>
        {
            e.ToTable("cash_transaction");
            // Unique across deleted rows too, so a number once used is never reissued.
            e.HasIndex(t => t.TransactionNo).IsUnique();
            // The popup reads a range of trading days.
            e.HasIndex(t => t.BusinessDate);
            e.Property(t => t.TransactionNo).HasMaxLength(30);
            e.Property(t => t.Type).HasMaxLength(3);
            e.Property(t => t.Amount).HasColumnType("decimal(10,2)");
            e.Property(t => t.Remarks).HasMaxLength(250);

            // Restrict: a mode with entries against it is deactivated, not deleted.
            e.HasOne(t => t.Mode).WithMany().HasForeignKey(t => t.ModeId).OnDelete(DeleteBehavior.Restrict);
        });

        // Audit column SQL defaults and types for the master tables
        foreach (var entity in new[] { typeof(Category), typeof(MenuItem), typeof(TerminalConfig),
                                       typeof(RoleConfig), typeof(User), typeof(TerminalSetting),
                                       typeof(StoreInfo), typeof(TransactionMode), typeof(CashTransaction) })
        {
            b.Entity(entity).Property<bool>("IsActive").HasDefaultValue(true);
            b.Entity(entity).Property<int>("CreatedBy").HasDefaultValue(AuditableEntity.SystemUserId);
            b.Entity(entity).Property<DateTime>("CreatedAt").HasColumnType("datetime");
            b.Entity(entity).Property<DateTime?>("UpdatedAt").HasColumnType("datetime");
        }

        b.Entity<MenuItem>().Property(m => m.Price).HasColumnType("decimal(10,2)");
        b.Entity<Order>().Property(o => o.SubTotal).HasColumnType("decimal(10,2)");
        b.Entity<Order>().Property(o => o.DiscountAmount).HasColumnType("decimal(10,2)");
        b.Entity<Order>().Property(o => o.ServiceChargeAmount).HasColumnType("decimal(10,2)");
        b.Entity<Order>().Property(o => o.GstAmount).HasColumnType("decimal(10,2)");
        b.Entity<Order>().Property(o => o.NetTotal).HasColumnType("decimal(10,2)");
        b.Entity<Order>().Property(o => o.DiscountPercent).HasColumnType("decimal(5,2)");
        b.Entity<Order>().Property(o => o.ServiceChargePercent).HasColumnType("decimal(5,2)");
        b.Entity<Order>().Property(o => o.GstPercent).HasColumnType("decimal(5,2)");
        b.Entity<OrderItem>().Property(o => o.UnitPrice).HasColumnType("decimal(10,2)");
    }
}
