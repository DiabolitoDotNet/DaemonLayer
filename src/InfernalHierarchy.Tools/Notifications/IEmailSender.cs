using System.Net.Mail;

namespace InfernalHierarchy.Tools.Notifications;

public interface IEmailSender
{
    Task SendAsync(MailMessage message, CancellationToken ct);
}
