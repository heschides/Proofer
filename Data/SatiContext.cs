using System;
using System.Collections.Generic;
using System.Configuration;
using System.Runtime.Serialization.Formatters;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Abstractions;
using Sati.Models;
using static Sati.Enums;

namespace Sati.Data
{
    public class SatiContext : DbContext
    {
        public DbSet<Person> People { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<Form> Forms { get; set; }
        public DbSet<Note> Notes { get; set; }
        public DbSet<Settings> Settings { get; set;  }
        public DbSet<Scratchpad> Scratchpad {  get; set; }
        public DbSet<Incentive> Incentives { get; set; }

        public SatiContext(DbContextOptions<SatiContext> options) : base(options)
        { 
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(u => u.Id);
                entity.Property(u => u.Username)
                      .IsRequired()
                      .HasMaxLength(50);
                entity.HasIndex(u => u.Username)
                      .IsUnique();
            });

            modelBuilder.Entity<Person>(entity =>
            {
                entity.HasKey(p => p.Id);
                entity.Property(p => p.FirstName)
                      .IsRequired()
                      .HasMaxLength(50);
                entity.Property(p => p.LastName)
                      .IsRequired()
                      .HasMaxLength(50);
                entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(p => p.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<Note>(entity =>
            {
                entity.HasKey(n => n.Id);
                entity.Property(n => n.Narrative)
                      .IsRequired();
                entity.HasOne(n => n.Person)
                      .WithMany(p => p.Notes)
                      .HasForeignKey(n => n.PersonId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Form>(entity =>
            {
                entity.HasKey(f => f.Id);
                entity.Property(f => f.Type)
                      .HasConversion<string>();
                entity.HasOne(f => f.Person)
                      .WithMany(p => p.Forms)
                      .HasForeignKey(f => f.PersonId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Settings>(entity =>
            {
                entity.HasKey(s => s.Id);
                entity.Property(s => s.BaseIncentive).HasColumnType("decimal(18,2)");
                entity.Property(s => s.PerUnitIncentive).HasColumnType("decimal(18,2)");
            });

            modelBuilder.Entity<Incentive>(entity =>
            {
                entity.HasKey(i => i.Id);
                entity.Property(i => i.BaseIncentive).HasColumnType("decimal(18,2)");
                entity.Property(i => i.PerUnitIncentive).HasColumnType("decimal(18,2)");
                entity.HasIndex(i => new { i.UserId, i.Month, i.Year }).IsUnique();
                entity.HasOne(i => i.User)
                      .WithMany()
                      .HasForeignKey(i => i.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Scratchpad>(entity =>
            {
                entity.HasKey(s => s.Id);
                entity.HasIndex(s => new {s.UserId, s.Date})
                .IsUnique();
            });
        }
    }
}
