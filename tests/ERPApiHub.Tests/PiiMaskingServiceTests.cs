using ERPApiHub.Application.Audit;
using Xunit;

namespace ERPApiHub.Tests;

public sealed class PiiMaskingServiceTests
{
    private readonly PiiMaskingService _service = new();

    [Fact]
    public void MaskEmail_MasksMiddlePart()
    {
        var result = _service.MaskEmail("john.doe@example.com");
        Assert.Equal("j***@example.com", result);
    }

    [Fact]
    public void MaskEmail_WhenShortLocalPart_PreservesFirstChar()
    {
        var result = _service.MaskEmail("a@b.co");
        Assert.Equal("a***@b.co", result);
    }

    [Fact]
    public void MaskEmail_WhenEmpty_ReturnsEmpty()
    {
        Assert.Equal("", _service.MaskEmail(""));
        Assert.Null(_service.MaskEmail(null!));
    }

    [Fact]
    public void MaskPhone_MasksMiddleDigits()
    {
        var result = _service.MaskPhone("0912345678");
        Assert.Equal("09***5678", result);
    }

    [Fact]
    public void MaskPhone_WhenTooShort_ReturnsAsIs()
    {
        var result = _service.MaskPhone("09123");
        Assert.Equal("09123", result);
    }

    [Fact]
    public void MaskText_MasksEmailsAndPhones()
    {
        var text = "Contact john@example.com or call 0912345678";
        var result = _service.MaskText(text, maskEmails: true, maskPhones: true);

        Assert.Contains("***@example.com", result);
        Assert.Contains("09***5678", result);
        Assert.DoesNotContain("john@", result);
    }

    [Fact]
    public void MaskText_WhenMaskEmailsOnly_KeepsPhones()
    {
        var text = "Email: test@mail.com Phone: 0900123456";
        var result = _service.MaskText(text, maskEmails: true, maskPhones: false);

        Assert.Contains("***@mail.com", result);
        Assert.Contains("0900123456", result);
    }
}