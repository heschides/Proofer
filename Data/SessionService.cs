using Sati.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Sati.Data
{
    public class SessionService : ISessionService
    {
        public User? CurrentUser { get; private set; }
        public bool AllowComplianceOverride { get; set; } = false;


        public void SetUser(User user)
        {
            CurrentUser = user;
        }
    }
}

