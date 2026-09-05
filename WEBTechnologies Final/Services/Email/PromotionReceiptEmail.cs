using System.Net;
using WEBTechnologies_Final.Models;

namespace WEBTechnologies_Final.Services.Email
{
    /// <summary>
    /// Builds the receipt a seller gets when they buy a placement.
    ///
    /// PLACEHOLDER, AND DELIBERATELY NOT SENT YET. Nothing is charged for anything: placements
    /// are arranged off-site and granted by hand, so an automatic "thank you for your payment"
    /// would be the site's first outright false statement to a customer. The body is written
    /// now, and tested now, because the reference it carries is the whole point of the
    /// Promotion table and the thing the admin lookup expects sellers to be quoting.
    ///
    /// TO TURN IT ON: inject IEmailSender into PromotionService and send this after the grant
    /// commits - after, so a failed email cannot roll back a placement somebody paid for. Do
    /// it at the same time as checkout, not before. See section 8 of the IDEAS notepad.
    /// </summary>
    public static class PromotionReceiptEmail
    {
        public static string Subject(Promotion promotion) =>
            $"Your Premium Motors promotion — {promotion.Reference}";

        /// <summary>
        /// The reference is stated twice, in the first line and again beside the details,
        /// because the first line is all that shows in a mail client's preview and this code
        /// is the one thing the seller will come back looking for.
        /// </summary>
        public static string HtmlBody(Promotion promotion, string? siteUrl = null)
        {
            var reference = WebUtility.HtmlEncode(promotion.Reference);
            var title = WebUtility.HtmlEncode(promotion.CarTitle);
            var tier = promotion.Tier == PromotionTier.FrontPage
                ? "Front page"
                : "Promoted in the marketplace";

            var price = promotion.PriceEur is decimal paid
                ? $"€{paid:N2}"
                : "Arranged directly with us";

            var link = string.IsNullOrWhiteSpace(siteUrl)
                ? string.Empty
                : $"<p><a href=\"{WebUtility.HtmlEncode(siteUrl)}\">View your listing</a></p>";

            return $@"
<p>Your reference is <strong>{reference}</strong>. Keep it — if you ever need to ask us
about this promotion, quoting it is the fastest way for us to find it.</p>

<table cellpadding=""6"" style=""border-collapse:collapse"">
  <tr><td><strong>Reference</strong></td><td>{reference}</td></tr>
  <tr><td><strong>Listing</strong></td><td>{title}</td></tr>
  <tr><td><strong>Placement</strong></td><td>{tier}</td></tr>
  <tr><td><strong>Starts</strong></td><td>{promotion.StartedUtc:dd MMM yyyy}</td></tr>
  <tr><td><strong>Runs until</strong></td><td>{promotion.EndsUtc:dd MMM yyyy}</td></tr>
  <tr><td><strong>Paid</strong></td><td>{price}</td></tr>
</table>

{link}

<p>What this buys: a higher position in the marketplace{(promotion.Tier == PromotionTier.FrontPage ? ", and a place on the front pages" : string.Empty)} until the date above.
It does not change anything about the car itself, and your listing still has to match a
buyer's filters to appear to them.</p>

<p>A promotion stops automatically if the car is marked reserved or sold, so you are never
paying to advertise something you have already sold.</p>";
        }
    }
}
