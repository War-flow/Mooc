using System;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;
using Mooc.Data;
using Mooc.Services;
using Microsoft.AspNetCore.Components.Forms;

// Ajoutez cette interface si elle n'existe pas déjà
public interface ISecurityAuditService
{
    Task LogSecurityEventAsync(string eventType, string userId, string details, HttpContext? context = null);
}

// Ajoutez la définition de la classe SecurityAuditLog si elle n'existe pas déjà
public class SecurityAuditLog
{
    public string EventType { get; set; }
    public string UserId { get; set; }
    public string Details { get; set; }
    public string IpAddress { get; set; }
    public string UserAgent { get; set; }
    public DateTime Timestamp { get; set; }
}