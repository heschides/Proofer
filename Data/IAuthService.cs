using Sati.Models;
using System;
using System.Collections.Generic;
using System.Security;
using System.Text;

namespace Sati.Data
{
    public enum AuthenticationServiceIssue
    {
        TooManyAttempts,
        ServiceUnavailable,
        NetworkUnavailable
    }

    public sealed class AuthenticationServiceException(
        AuthenticationServiceIssue issue,
        string message,
        TimeSpan? retryAfter = null,
        Exception? innerException = null) : Exception(message, innerException)
    {
        public AuthenticationServiceIssue Issue { get; } = issue;
        public TimeSpan? RetryAfter { get; } = retryAfter;
    }

    public interface IAuthService
    {
        Task<User?> AuthenticateAsync(string username, SecureString password);
    }
}
