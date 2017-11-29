﻿using Microsoft.EntityFrameworkCore;
using System;

namespace Samples.AspNetCore.Models
{
    public class SampleContext : DbContext
    {
        public SampleContext(DbContextOptions<SampleContext> dbOpt)
            : base(dbOpt)
        {
        }

        public DbSet<RouteHit> RouteHits { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<RouteHit>()
                .HasKey(t => t.Id);

            //modelBuilder.Entity<RouteHit>()
            //    .Property(ep => ep.RowVersion)
            //    .IsConcurrencyToken()
            //    .ValueGeneratedOnAddOrUpdate();
        }
    }

    public class RouteHit
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public DateTime? UpdateTime { get; set; }
        // public byte[] RowVersion { get; set; }
    }
}
