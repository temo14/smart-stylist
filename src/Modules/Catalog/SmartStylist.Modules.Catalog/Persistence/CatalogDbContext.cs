using Microsoft.EntityFrameworkCore;
using SmartStylist.Modules.Catalog.Domain;

namespace SmartStylist.Modules.Catalog.Persistence;

public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public const string Schema = "catalog";

    public DbSet<Shop> Shops => Set<Shop>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<PriceSnapshot> PriceSnapshots => Set<PriceSnapshot>();

    public DbSet<CollectionRun> CollectionRuns => Set<CollectionRun>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Shop>(shop =>
        {
            shop.HasIndex(s => s.Key).IsUnique();
            shop.Property(s => s.Key).HasMaxLength(64);
            shop.Property(s => s.Name).HasMaxLength(200);
            shop.Property(s => s.BaseUrl).HasMaxLength(500);
            shop.Property(s => s.Currency).HasMaxLength(3).IsFixedLength();
            shop.Property(s => s.ConfiguredStrategy).HasConversion<string>().HasMaxLength(32);
            shop.Property(s => s.DetectedStrategy).HasConversion<string>().HasMaxLength(32);
        });

        modelBuilder.Entity<Product>(product =>
        {
            product.HasIndex(p => new { p.ShopId, p.ExternalId }).IsUnique();
            product.Property(p => p.ExternalId).HasMaxLength(500);
            product.Property(p => p.Url).HasMaxLength(2000);
            product.Property(p => p.Title).HasMaxLength(500);
            product.Property(p => p.Brand).HasMaxLength(200);
            product.Property(p => p.ImageUrl).HasMaxLength(2000);
            product.HasOne(p => p.Shop).WithMany().OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PriceSnapshot>(snapshot =>
        {
            snapshot.HasIndex(s => new { s.ProductId, s.VariantKey, s.ObservedOn }).IsUnique();
            snapshot.HasIndex(s => s.ObservedOn);
            snapshot.Property(s => s.VariantKey).HasMaxLength(200);
            snapshot.Property(s => s.SizeLabel).HasMaxLength(100);
            snapshot.Property(s => s.Price).HasPrecision(12, 2);
            snapshot.Property(s => s.ListPrice).HasPrecision(12, 2);
            snapshot.Property(s => s.Currency).HasMaxLength(3).IsFixedLength();
            snapshot.HasOne(s => s.Product).WithMany(p => p.Snapshots).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CollectionRun>(run =>
        {
            run.HasIndex(r => new { r.ShopId, r.StartedAt });
            run.Property(r => r.Strategy).HasConversion<string>().HasMaxLength(32);
            run.Property(r => r.Status).HasConversion<string>().HasMaxLength(16);
            run.Property(r => r.Error).HasMaxLength(4000);
            run.HasOne(r => r.Shop).WithMany().OnDelete(DeleteBehavior.Cascade);
        });
    }
}
