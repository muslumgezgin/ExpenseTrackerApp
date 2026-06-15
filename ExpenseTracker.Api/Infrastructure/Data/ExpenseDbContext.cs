using ExpenseTracker.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Infrastructure.Data;

public class ExpenseDbContext(DbContextOptions<ExpenseDbContext> options) : DbContext(options)
{
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Budget> Budgets => Set<Budget>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure Category entity
        modelBuilder.Entity<Category>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(100);

            // seed data
            entity.HasData(
                new Category { Id = 1, Name = "Food" },
                new Category { Id = 2, Name = "Transportation" },
                new Category { Id = 3, Name = "Entertainment" },
                new Category { Id = 4, Name = "Utilities" },
                new Category { Id = 5, Name = "Healthcare" },
                new Category { Id = 6, Name = "Bills" },
                new Category { Id = 7, Name = "Shopping" }
            );
        });

        // Configure Budget entity
        modelBuilder.Entity<Budget>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Limit).HasColumnType("decimal(18,2)");
            entity.HasIndex(e => new { e.Month, e.Year }).IsUnique();
        });

        // Configure Expense entity
        modelBuilder.Entity<Expense>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Amount).HasColumnType("decimal(18,2)");


            entity.Property(e => e.Description)
                .HasMaxLength(250);

            entity.HasOne(e => e.Category)
                .WithMany(c => c.Expenses)
                .HasForeignKey(e => e.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
