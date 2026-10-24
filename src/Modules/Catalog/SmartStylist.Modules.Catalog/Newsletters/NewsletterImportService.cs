using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartStylist.Modules.Catalog.Domain;
using SmartStylist.Modules.Catalog.Persistence;

namespace SmartStylist.Modules.Catalog.Newsletters;

public sealed record NewsletterImportResult(IReadOnlyList<NewsletterEmail> Processed, int AlreadyRead);

/// <summary>Reads sale emails, pulls product prices out of them and adds those prices to the price history.</summary>
public sealed partial class NewsletterImportService
{
    private readonly CatalogDbContext _db;
    private readonly INewsletterExtractor _extractor;
    private readonly ILinkResolver _links;
    private readonly IOptions<CatalogOptions> _catalogOptions;
    private readonly IOptions<NewsletterOptions> _options;
    private readonly TimeProvider _time;
    private readonly ILogger<NewsletterImportService> _logger;

    internal NewsletterImportService(
        CatalogDbContext db,
        INewsletterExtractor extractor,
        ILinkResolver links,
        IOptions<CatalogOptions> catalogOptions,
        IOptions<NewsletterOptions> options,
        TimeProvider time,
        ILogger<NewsletterImportService> logger)
    {
        _db = db;
        _extractor = extractor;
        _links = links;
        _catalogOptions = catalogOptions;
        _options = options;
        _time = time;
        _logger = logger;
    }

    public async Task<NewsletterImportResult> ImportAsync(IMailSource source, CancellationToken cancellationToken)
    {
        // Shops that stopped being crawled (Enabled = false) still get their sale emails read.
        var shopsByKey = await _db.Shops.ToDictionaryAsync(s => s.Key, cancellationToken);
        var shops = _catalogOptions.Value.Shops
            .Where(o => shopsByKey.ContainsKey(o.Key))
            .Select(o => (Shop: shopsByKey[o.Key], Domains: new ShopDomains(o.Key, o.BaseUrl, o.EmailDomains)))
            .ToList();

        var since = _time.GetUtcNow().AddDays(-_options.Value.LookbackDays);
        var processed = new List<NewsletterEmail>();
        var alreadyRead = 0;

        _logger.LogInformation("Reading sale emails from {Source}", source.Description);
        await foreach (var message in source.ReadAsync(since, cancellationToken))
        {
            var email = await _db.NewsletterEmails.SingleOrDefaultAsync(e => e.MessageId == message.MessageId, cancellationToken);
            if (email is { Status: not NewsletterStatus.Failed })
            {
                alreadyRead++;
                continue;
            }

            if (email is null)
            {
                email = new NewsletterEmail
                {
                    MessageId = Truncate(message.MessageId, 500),
                    From = Truncate(message.FromAddress, 320),
                    Subject = Truncate(message.Subject, 500),
                };
                _db.NewsletterEmails.Add(email);
            }

            email.ReceivedAt = message.ReceivedAt;
            email.Error = null;
            var match = shops.FirstOrDefault(s => s.Domains.MatchesSender(message.FromAddress));
            email.Shop = match.Shop;

            try
            {
                if (match.Shop is null)
                {
                    email.Status = NewsletterStatus.UnknownSender;
                }
                else
                {
                    await PriceAsync(message, email, match.Shop, match.Domains, cancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("Could not read email '{Subject}' from {From}: {Error}", message.Subject, message.FromAddress, ex.Message);
                DetachPrices();
                email.Status = NewsletterStatus.Failed;
                email.Error = Truncate(ex.Message, 4000);
            }

            email.ProcessedAt = _time.GetUtcNow();
            await _db.SaveChangesAsync(CancellationToken.None);
            processed.Add(email);
            _logger.LogInformation(
                "{Shop} '{Subject}': {Status}, {Prices} prices ({Rejected} rejected)",
                match.Shop?.Key ?? message.FromAddress, message.Subject, email.Status, email.PricesWritten, email.ProductsRejected);
        }

        return new NewsletterImportResult(processed, alreadyRead);
    }

    private async Task PriceAsync(InboxMessage message, NewsletterEmail email, Shop shop, ShopDomains domains, CancellationToken cancellationToken)
    {
        var text = NewsletterText.From(message, _options.Value.MaxEmailChars);
        if (text.Text.Length == 0)
        {
            email.Status = NewsletterStatus.NoPrices;
            return;
        }

        var extraction = await _extractor.ExtractAsync(new NewsletterInput(shop.Name, message.Subject, message.ReceivedAt, text.Text), cancellationToken);
        var (offers, rejected) = ExtractionGuard.Check(extraction, text, shop.Currency);
        email.ProductsFound = extraction.Products.Count;
        email.ProductsRejected = rejected;
        email.SaleEndsOn = ExtractionGuard.SaleEnd(extraction.SaleEndsOn);
        email.PromoCode = string.IsNullOrWhiteSpace(extraction.PromoCode) ? null : Truncate(extraction.PromoCode.Trim(), 100);
        email.PricesWritten = offers.Count == 0 ? 0 : await SaveAsync(shop, domains, message.ReceivedAt, offers, cancellationToken);
        email.Status = email.PricesWritten > 0 ? NewsletterStatus.Priced : NewsletterStatus.NoPrices;
    }

    private async Task<int> SaveAsync(Shop shop, ShopDomains domains, DateTimeOffset receivedAt, IReadOnlyList<NewsletterOffer> offers, CancellationToken cancellationToken)
    {
        var observedOn = DateOnly.FromDateTime(receivedAt.UtcDateTime);
        var now = _time.GetUtcNow();
        var resolved = new Dictionary<string, Uri?>();
        var items = new List<(NewsletterOffer Offer, string ExternalId, string Url)>();

        foreach (var offer in offers)
        {
            Uri? page = null;
            if (offer.LinkUrl is not null && !resolved.TryGetValue(offer.LinkUrl, out page))
            {
                page = await _links.ResolveAsync(offer.LinkUrl, domains, cancellationToken);
                resolved[offer.LinkUrl] = page;
            }

            // A product page URL identifies the product across emails and crawls; without one, the title has to do.
            var externalId = page?.AbsoluteUri ?? "email:" + SpacesRegex().Replace(offer.Title.ToLowerInvariant(), " ");
            var url = page?.AbsoluteUri ?? offer.LinkUrl ?? shop.BaseUrl;
            items.Add((offer, Truncate(externalId, 500), url));
        }

        var unique = items.GroupBy(i => i.ExternalId).Select(g => g.First()).ToList();
        var ids = unique.Select(i => i.ExternalId).ToList();
        var products = await _db.Products
            .Where(p => p.ShopId == shop.Id && (ids.Contains(p.ExternalId) || ids.Contains(p.Url)))
            .ToListAsync(cancellationToken);

        var written = 0;
        foreach (var (offer, externalId, url) in unique)
        {
            var product = products.FirstOrDefault(p => p.ExternalId == externalId) ?? products.FirstOrDefault(p => p.Url == externalId);
            if (product is null)
            {
                product = new Product { ShopId = shop.Id, ExternalId = externalId, Url = Truncate(url, 2000), Title = Truncate(offer.Title, 500), FirstSeenAt = receivedAt, LastSeenAt = receivedAt };
                _db.Products.Add(product);
                products.Add(product);
            }
            else
            {
                if (receivedAt < product.FirstSeenAt)
                {
                    product.FirstSeenAt = receivedAt;
                }

                if (receivedAt > product.LastSeenAt)
                {
                    product.LastSeenAt = receivedAt;
                }
            }

            var snapshot = product.Id == 0
                ? null
                : await _db.PriceSnapshots.SingleOrDefaultAsync(s => s.ProductId == product.Id && s.VariantKey == "" && s.ObservedOn == observedOn, cancellationToken);
            if (snapshot is { Source: PriceSource.Crawl })
            {
                // The shop's own page that day beats an email.
                continue;
            }

            if (snapshot is null)
            {
                snapshot = new PriceSnapshot { Product = product, VariantKey = "", ObservedOn = observedOn, Currency = offer.Currency, Source = PriceSource.Newsletter };
                _db.PriceSnapshots.Add(snapshot);
            }

            snapshot.Price = offer.Price;
            snapshot.ListPrice = offer.ListPrice;
            snapshot.Currency = offer.Currency;
            snapshot.CapturedAt = now;
            written++;
        }

        return written;
    }

    private void DetachPrices()
    {
        foreach (var entry in _db.ChangeTracker.Entries().Where(e => e.Entity is Product or PriceSnapshot).ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacesRegex();
}
