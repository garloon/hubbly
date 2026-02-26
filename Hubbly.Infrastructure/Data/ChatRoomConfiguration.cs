using Hubbly.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hubbly.Infrastructure.Data;

public class ChatRoomConfiguration : IEntityTypeConfiguration<ChatRoom>
{
    public void Configure(EntityTypeBuilder<ChatRoom> builder)
    {
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(r => r.Description)
            .HasMaxLength(500);

        builder.Property(r => r.Type)
            .IsRequired();

        builder.Property(r => r.MaxUsers)
            .IsRequired();

        builder.Property(r => r.CreatedBy)
            .IsRequired(false);

        builder.Property(r => r.PasswordHash)
            .HasMaxLength(255);

        builder.Property(r => r.CurrentUsers)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(r => r.CreatedAt)
            .IsRequired();

        builder.Property(r => r.LastActiveAt)
            .IsRequired();

        builder.Property(r => r.UpdatedAt)
            .IsRequired();

        // Indexes
        builder.HasIndex(r => r.LastActiveAt)
            .HasDatabaseName("IX_ChatRooms_LastActiveAt");

        builder.HasIndex(r => r.Type)
            .HasDatabaseName("IX_ChatRooms_Type");
    }
}