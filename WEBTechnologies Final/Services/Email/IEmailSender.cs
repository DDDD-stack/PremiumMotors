namespace WEBTechnologies_Final.Services
{
    public interface IEmailSender
    {
        Task<bool> SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default);
    }
}
