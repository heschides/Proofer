using Sati.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Sati.Data
{
    public interface ISessionService
    {
        bool AllowComplianceOverride { get; set; }
        User? CurrentUser { get; }
        void SetUser(User user);
    }
}
