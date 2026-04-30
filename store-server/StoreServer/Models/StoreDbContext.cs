using Microsoft.EntityFrameworkCore;

namespace StoreServer.Models;

public class StoreDbContext(DbContextOptions<StoreDbContext> options) : DbContext(options)
{
    public DbSet<Product>         Products        => Set<Product>();
    public DbSet<Transaction>     Transactions    => Set<Transaction>();
    public DbSet<TransactionItem> Items           => Set<TransactionItem>();
    public DbSet<LoyaltyMember>   LoyaltyMembers  => Set<LoyaltyMember>();
    public DbSet<WeightCheckLog>  WeightCheckLogs => Set<WeightCheckLog>();
    public DbSet<StoreSetting>    StoreSettings   => Set<StoreSetting>();
    public DbSet<CashDrawerEntry> CashDrawer      => Set<CashDrawerEntry>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Product>().HasIndex(p => p.Barcode).IsUnique();
        mb.Entity<CashDrawerEntry>().HasIndex(e => new { e.TerminalId, e.Key }).IsUnique();
        // Phone and CardId are each unique when set (nullable unique index)
        mb.Entity<LoyaltyMember>().HasIndex(m => m.Phone).IsUnique().HasFilter("[Phone] IS NOT NULL");
        mb.Entity<LoyaltyMember>().HasIndex(m => m.CardId).IsUnique().HasFilter("[CardId] IS NOT NULL");
        mb.Entity<TransactionItem>()
          .HasOne(i => i.Product)
          .WithMany()
          .HasForeignKey(i => i.ProductId);
        mb.Entity<TransactionItem>()
          .HasOne<Transaction>()
          .WithMany(t => t.Items)
          .HasForeignKey(i => i.TransactionId);
    }
}
