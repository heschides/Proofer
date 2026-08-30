using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;

namespace Sati.Models
{
    public class User
    {
        public int Id { get; private set; } 
        public string Username { get; private set; } = string.Empty;
        public string DisplayName { get; private set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string Salt { get; set; } = string.Empty;
        public UserRole Role { get; set; }
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
    }
}
