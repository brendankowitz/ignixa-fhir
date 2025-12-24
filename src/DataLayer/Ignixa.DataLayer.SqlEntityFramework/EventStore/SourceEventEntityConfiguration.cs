// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ignixa.DataLayer.SqlEntityFramework.EventStore;

public class SourceEventEntityConfiguration : IEntityTypeConfiguration<SourceEventEntity>
{
    public void Configure(EntityTypeBuilder<SourceEventEntity> builder)
    {
        builder.ToTable("SourceEvents");
        
        builder.HasKey(e => e.EventId);
        
        builder.Property(e => e.EventId)
            .UseIdentityColumn();
        
        builder.Property(e => e.StreamId)
            .HasMaxLength(256)
            .IsRequired();
        
        builder.Property(e => e.EventType)
            .HasMaxLength(100)
            .IsRequired();
        
        builder.Property(e => e.EventData)
            .HasColumnType("nvarchar(max)")
            .IsRequired();
        
        builder.Property(e => e.Timestamp)
            .HasDefaultValueSql("SYSUTCDATETIME()");

        builder.Property(e => e.TransactionId)
            .HasDefaultValue(0L);

        builder.HasIndex(e => new { e.StreamId, e.EventId })
            .HasDatabaseName("IX_SourceEvents_StreamId_EventId");

        builder.HasIndex(e => e.EventId)
            .HasDatabaseName("IX_SourceEvents_EventId");

        builder.HasIndex(e => e.TransactionId)
            .HasDatabaseName("IX_SourceEvents_TransactionId");
    }
}
