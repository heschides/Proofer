using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Sati.Contracts.V1;
using Sati.Models;

namespace Sati.Data;

/// <summary>
/// One mapping and write-protection owner for the migration model and the API's matching
/// ServerChat types. Property names are anchored to the canonical persistence records.
/// This does not authorize a request; live actor and consumer access remain server concerns.
/// </summary>
public static class ChatPersistenceModel
{
    public static void Configure<TRoom, TMember, TMessage, TChange, TMarker, TAgency, TUser, TPerson>(
        ModelBuilder modelBuilder)
        where TRoom : class where TMember : class where TMessage : class
        where TChange : class where TMarker : class where TAgency : class
        where TUser : class where TPerson : class
    {
        modelBuilder.Entity<TRoom>(entity =>
        {
            entity.ToTable("ChatRooms", table =>
                table.HasCheckConstraint("CK_ChatRooms_Revision", "[Revision] > 0"));
            entity.HasKey(nameof(ChatRoom.Id));
            entity.HasAlternateKey(nameof(ChatRoom.AgencyId), nameof(ChatRoom.Id));
            entity.Property<string>(nameof(ChatRoom.Name)).IsRequired().HasMaxLength(80);
            entity.Property<string?>(nameof(ChatRoom.Description)).HasMaxLength(240);
            entity.Property<long>(nameof(ChatRoom.Revision)).IsConcurrencyToken();
            entity.HasIndex(nameof(ChatRoom.AgencyId), nameof(ChatRoom.ArchivedAtUtc));
            entity.HasOne<TAgency>().WithMany().HasForeignKey(nameof(ChatRoom.AgencyId))
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TPerson>().WithMany().HasForeignKey(nameof(ChatRoom.PersonId))
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TUser>().WithMany().HasForeignKey(nameof(ChatRoom.CreatedByUserId))
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TUser>().WithMany().HasForeignKey(nameof(ChatRoom.ArchivedByUserId))
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TMember>(entity =>
        {
            entity.ToTable("ChatRoomMembers", table =>
            {
                table.HasCheckConstraint("CK_ChatRoomMembers_VisibleSequence", "[VisibleAfterSequence] >= 0");
                table.HasCheckConstraint("CK_ChatRoomMembers_Removal",
                    "([RemovedAtUtc] IS NULL AND [RemovedByUserId] IS NULL) OR " +
                    "([RemovedAtUtc] IS NOT NULL AND [RemovedByUserId] IS NOT NULL AND [RemovedAtUtc] >= [AddedAtUtc])");
            });
            entity.HasKey(nameof(ChatRoomMember.Id));
            entity.HasIndex(nameof(ChatRoomMember.RoomId), nameof(ChatRoomMember.UserId))
                .IsUnique().HasFilter("[RemovedAtUtc] IS NULL");
            entity.HasIndex(nameof(ChatRoomMember.UserId), nameof(ChatRoomMember.RemovedAtUtc));
            entity.Property<DateTime?>(nameof(ChatRoomMember.RemovedAtUtc)).IsConcurrencyToken();
            entity.HasOne<TRoom>().WithMany()
                .HasForeignKey(nameof(ChatRoomMember.AgencyId), nameof(ChatRoomMember.RoomId))
                .HasPrincipalKey(nameof(ChatRoom.AgencyId), nameof(ChatRoom.Id))
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TUser>().WithMany().HasForeignKey(nameof(ChatRoomMember.UserId))
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TUser>().WithMany().HasForeignKey(nameof(ChatRoomMember.AddedByUserId))
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TUser>().WithMany().HasForeignKey(nameof(ChatRoomMember.RemovedByUserId))
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TMessage>(entity =>
        {
            entity.ToTable("ChatMessages", table =>
                table.HasCheckConstraint("CK_ChatMessages_Sequence", "[Sequence] > 0"));
            entity.HasKey(nameof(ChatMessage.Id));
            entity.HasAlternateKey(nameof(ChatMessage.RoomId), nameof(ChatMessage.AgencyId), nameof(ChatMessage.Id));
            entity.Property<string>(nameof(ChatMessage.Body)).IsRequired().HasMaxLength(ChatLimits.MaxBodyLength);
            entity.Property<string>(nameof(ChatMessage.AuthorDisplayName)).IsRequired().HasMaxLength(150);
            entity.HasIndex(nameof(ChatMessage.RoomId), nameof(ChatMessage.AuthorUserId), nameof(ChatMessage.ClientMessageId))
                .IsUnique();
            entity.HasIndex(nameof(ChatMessage.RoomId), nameof(ChatMessage.Sequence)).IsUnique();
            entity.HasOne<TRoom>().WithMany()
                .HasForeignKey(nameof(ChatMessage.AgencyId), nameof(ChatMessage.RoomId))
                .HasPrincipalKey(nameof(ChatRoom.AgencyId), nameof(ChatRoom.Id))
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TUser>().WithMany().HasForeignKey(nameof(ChatMessage.AuthorUserId))
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TChange>(entity =>
        {
            entity.ToTable("ChatChanges", table =>
                table.HasCheckConstraint("CK_ChatChanges_Sequence", "[Sequence] > 0"));
            entity.HasKey(nameof(ChatChange.Id));
            entity.Property<string>(nameof(ChatChange.Kind)).IsRequired().HasMaxLength(32);
            entity.Property<string?>(nameof(ChatChange.RedactionReason)).HasMaxLength(240);
            entity.HasIndex(nameof(ChatChange.RoomId), nameof(ChatChange.Sequence)).IsUnique();
            entity.HasIndex(nameof(ChatChange.MessageId)).IsUnique()
                .HasFilter("[Kind] = 'redaction' AND [MessageId] IS NOT NULL");
            entity.HasOne<TRoom>().WithMany()
                .HasForeignKey(nameof(ChatChange.AgencyId), nameof(ChatChange.RoomId))
                .HasPrincipalKey(nameof(ChatRoom.AgencyId), nameof(ChatRoom.Id))
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TMessage>().WithMany()
                .HasForeignKey(nameof(ChatChange.RoomId), nameof(ChatChange.AgencyId), nameof(ChatChange.MessageId))
                .HasPrincipalKey(nameof(ChatMessage.RoomId), nameof(ChatMessage.AgencyId), nameof(ChatMessage.Id))
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TUser>().WithMany().HasForeignKey(nameof(ChatChange.ActorUserId))
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TUser>().WithMany().HasForeignKey(nameof(ChatChange.TargetUserId))
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TMarker>(entity =>
        {
            entity.ToTable("ChatReadMarkers", table =>
                table.HasCheckConstraint("CK_ChatReadMarkers_SeenSequence", "[LastSeenSequence] >= 0"));
            entity.HasKey(nameof(ChatReadMarker.Id));
            entity.HasIndex(nameof(ChatReadMarker.RoomId), nameof(ChatReadMarker.UserId)).IsUnique();
            entity.Property<long>(nameof(ChatReadMarker.LastSeenSequence)).IsConcurrencyToken();
            entity.HasOne<TRoom>().WithMany()
                .HasForeignKey(nameof(ChatReadMarker.AgencyId), nameof(ChatReadMarker.RoomId))
                .HasPrincipalKey(nameof(ChatRoom.AgencyId), nameof(ChatRoom.Id))
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TUser>().WithMany().HasForeignKey(nameof(ChatReadMarker.UserId))
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    public static void ProtectWrites<TRoom, TMember, TMessage, TChange, TMarker>(ChangeTracker tracker)
        where TRoom : class where TMember : class where TMessage : class
        where TChange : class where TMarker : class
    {
        if (tracker.Entries<TMessage>().Any(IsChangedOrDeleted) ||
            tracker.Entries<TChange>().Any(IsChangedOrDeleted))
            throw new InvalidOperationException("Chat messages and change history are append-only.");

        foreach (var entry in tracker.Entries<TRoom>())
        {
            RejectDeletion(entry);
            if (entry.State != EntityState.Modified) continue;
            OnlyChanges(entry, nameof(ChatRoom.Name), nameof(ChatRoom.Description), nameof(ChatRoom.Revision),
                nameof(ChatRoom.ArchivedAtUtc), nameof(ChatRoom.ArchivedByUserId));
            var revision = entry.Property<long>(nameof(ChatRoom.Revision));
            if (revision.OriginalValue == long.MaxValue || revision.CurrentValue != revision.OriginalValue + 1)
                throw new InvalidOperationException("Every chat room change must advance its revision once.");
            var archivedAt = entry.Property<DateTime?>(nameof(ChatRoom.ArchivedAtUtc));
            if (archivedAt.OriginalValue is not null &&
                entry.Properties.Any(property => property.IsModified && property.Metadata.Name != nameof(ChatRoom.Revision)))
                throw new InvalidOperationException("Archived chat room details cannot change.");
            if ((archivedAt.CurrentValue is null) !=
                (entry.Property<int?>(nameof(ChatRoom.ArchivedByUserId)).CurrentValue is null))
                throw new InvalidOperationException("Chat archival requires its time and responsible user.");
        }

        foreach (var entry in tracker.Entries<TMember>())
        {
            RejectDeletion(entry);
            if (entry.State != EntityState.Modified) continue;
            OnlyChanges(entry, nameof(ChatRoomMember.RemovedAtUtc), nameof(ChatRoomMember.RemovedByUserId));
            var removedAt = entry.Property<DateTime?>(nameof(ChatRoomMember.RemovedAtUtc));
            if (removedAt.OriginalValue is not null || removedAt.CurrentValue is null ||
                entry.Property<int?>(nameof(ChatRoomMember.RemovedByUserId)).CurrentValue is null)
                throw new InvalidOperationException("A chat membership can only be closed once.");
        }

        foreach (var entry in tracker.Entries<TMarker>())
        {
            RejectDeletion(entry);
            if (entry.State != EntityState.Modified) continue;
            OnlyChanges(entry, nameof(ChatReadMarker.LastSeenSequence), nameof(ChatReadMarker.LastSeenAtUtc));
            var sequence = entry.Property<long>(nameof(ChatReadMarker.LastSeenSequence));
            if (sequence.CurrentValue < sequence.OriginalValue)
                throw new InvalidOperationException("A chat acknowledgment cannot move backward.");
        }
    }

    private static bool IsChangedOrDeleted(EntityEntry entry) =>
        entry.State is EntityState.Modified or EntityState.Deleted;

    private static void RejectDeletion(EntityEntry entry)
    {
        if (entry.State == EntityState.Deleted)
            throw new InvalidOperationException("Chat records are retained; deletion is unavailable.");
    }

    private static void OnlyChanges(EntityEntry entry, params string[] mutableProperties)
    {
        if (entry.Properties.Any(property => property.IsModified &&
            !mutableProperties.Contains(property.Metadata.Name, StringComparer.Ordinal)))
            throw new InvalidOperationException("The identity and history of a chat record cannot change.");
    }
}
