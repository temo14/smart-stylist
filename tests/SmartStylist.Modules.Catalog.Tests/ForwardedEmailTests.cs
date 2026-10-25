using MimeKit;
using SmartStylist.Modules.Catalog.Newsletters;

namespace SmartStylist.Modules.Catalog.Tests;

public class ForwardedEmailTests
{
    private static MimeMessage Mail(string from, string subject, string? html = null, string? text = null)
    {
        var body = new BodyBuilder { HtmlBody = html, TextBody = text };
        var message = new MimeMessage { Subject = subject, Body = body.ToMessageBody(), Date = new DateTimeOffset(2026, 10, 25, 7, 0, 0, TimeSpan.Zero) };
        message.From.Add(MailboxAddress.Parse(from));
        message.MessageId = "fwd-1@example.com";
        return message;
    }

    [Fact]
    public void Gmail_inline_forward_uses_the_shop_as_sender()
    {
        var html = """
            <div dir="ltr"><br><div class="gmail_quote"><div dir="ltr" class="gmail_attr">---------- Forwarded message ---------<br>
            From: <strong>J.Crew</strong> <span dir="auto">&lt;<a href="mailto:jcrew@e.jcrew.com">jcrew@e.jcrew.com</a>&gt;</span><br>
            Date: Fri, Oct 24, 2026 at 4:05 PM<br>Subject: Four must-have fall pants<br>To: &lt;me@gmail.com&gt;<br></div>
            <p>Chino $53.70</p></div></div>
            """;

        var message = InboxMessage.FromMime(Mail("Temo <temo@work.example>", "Fwd: Four must-have fall pants", html));

        Assert.Equal("jcrew@e.jcrew.com", message.FromAddress);
        Assert.Equal("temo@work.example", message.ForwardedBy);
        Assert.Equal("Four must-have fall pants", message.Subject);
        Assert.Contains("Chino $53.70", message.Html);
    }

    [Fact]
    public void Outlook_inline_forward_uses_the_shop_as_sender()
    {
        var text = """
            ________________________________
            From: Levi's <info@mail.levi.com>
            Sent: Saturday, October 24, 2026 1:05 PM
            To: Temo
            Subject: Jeans from $39 + free shipping

            501 Original Jeans $39.00
            """;

        var message = InboxMessage.FromMime(Mail("temo@work.example", "FW: Jeans from $39 + free shipping", text: text));

        Assert.Equal("info@mail.levi.com", message.FromAddress);
        Assert.Equal("Jeans from $39 + free shipping", message.Subject);
    }

    [Fact]
    public void Forward_as_attachment_uses_the_attached_email()
    {
        var shopEmail = Mail("jcrew@e.jcrew.com", "Four must-have fall pants", "<p>Chino $53.70</p>");
        shopEmail.MessageId = "original@e.jcrew.com";
        var outer = new MimeMessage { Subject = "FW: Four must-have fall pants" };
        outer.From.Add(MailboxAddress.Parse("temo@work.example"));
        var multipart = new Multipart("mixed") { new TextPart("plain") { Text = "see attached" }, new MessagePart { Message = shopEmail } };
        outer.Body = multipart;

        var message = InboxMessage.FromMime(outer);

        Assert.Equal("jcrew@e.jcrew.com", message.FromAddress);
        Assert.Equal("original@e.jcrew.com", message.MessageId);
        Assert.Equal("temo@work.example", message.ForwardedBy);
        Assert.Contains("Chino $53.70", message.Html);
    }

    [Fact]
    public void A_normal_email_is_left_alone()
    {
        var message = InboxMessage.FromMime(Mail("jcrew@e.jcrew.com", "Fwd your style: new arrivals", "<p>From: our stylists, with love</p>"));

        Assert.Equal("jcrew@e.jcrew.com", message.FromAddress);
        Assert.Null(message.ForwardedBy);
        Assert.Equal("Fwd your style: new arrivals", message.Subject);
    }
}
