using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;
using Sati.Contracts.V1;

namespace Sati.Models
{
    public class User
    {
        public int Id { get; private set; } 
        public string Username { get; private set; } = string.Empty;
        public string DisplayName { get; private set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string Salt { get; set; } = string.Empty;
        // Role remains only as a compatibility/display label and for the orthogonal
        // PlatformOperator identity. Authorization must use Permissions.
        public UserRole Role { get; set; }
        public UserPermissions Permissions { get; set; }
        public int? SupervisorId { get; set; }
        public User? Supervisor { get; set; }
        public ICollection<User> Supervisees { get; set; } = [];
        public int AgencyId { get; set; }
        public Agency Agency { get; set; } = null!;

        // Contact details for the case manager. Mutable and off the constructor
        // — a CM edits these over time; changing a phone number shouldn't require
        // re-minting the user. Nullable until captured. Source for the CM contact
        // block snapshotted onto payment/authorization forms at creation.
        public string? Email { get; set; }
        public string? Phone { get; set; }



        private User() { }
        private User(int id, string username, string displayName, string passwordHash, string salt, UserRole role, int? supervisorId, int agencyId)
        {
            Id = id;
            Username = username;
            DisplayName = displayName;
            PasswordHash = passwordHash;
            Salt = salt;
            Role = role;
            Permissions = UserPermissionRules.FromLegacyRole(role.ToString());
            SupervisorId = supervisorId;
            AgencyId = agencyId;
        }

        public static User Create(int id, string username, string displayName,
          string passwordHash, string salt, UserRole role, int? supervisorId, int agencyId)
        {
            return new User(id, username, displayName, passwordHash, salt, role, supervisorId, agencyId);
        }

        public void SetPassword(string hash, string salt)
        {
            PasswordHash = hash;
            Salt = salt;
        }

        [NotMapped]
        public bool HasCaseManagerPermissions
        {
            get => UserPermissionRules.HasCaseManagerPermissions(Permissions);
            set => SetPermission(UserPermissions.CaseManagement, value);
        }

        [NotMapped]
        public bool HasSupervisorPermissions
        {
            get => UserPermissionRules.HasSupervisorPermissions(Permissions);
            set => SetPermission(UserPermissions.Supervision, value);
        }

        [NotMapped]
        public bool HasAdminPermissions
        {
            get => UserPermissionRules.HasAdminPermissions(Permissions);
            set => SetPermission(UserPermissions.Administration, value);
        }

        [NotMapped]
        public bool HasBillingPermissions
        {
            get => UserPermissionRules.HasBillingPermissions(Permissions);
            set => SetPermission(UserPermissions.Billing, value);
        }

        [NotMapped]
        public string PermissionSummary => UserPermissionRules.Describe(Permissions);

        public AgencyActor ToAgencyActor() => new(Id, AgencyId, Permissions);

        private void SetPermission(UserPermissions permission, bool enabled)
        {
            Permissions = enabled ? Permissions | permission : Permissions & ~permission;
            if (Role != UserRole.PlatformOperator)
            {
                var label = UserPermissionRules.LegacyLabel(Permissions);
                Role = Enum.TryParse<UserRole>(label, out var role) ? role : UserRole.CaseManager;
            }
        }
    }
}
