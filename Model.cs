using System.Security.Cryptography;
using System.Text;

namespace SkailPaymentWebhook;

/// <summary>What the payment gateway sends, and the argument of the ReleaseOrder function.</summary>
public sealed record PaymentConfirmed(string EventId, string OrderId, decimal Amount);

/// <summary>Result of the SettleInvoice command. A mismatch is a result, not an error: another attempt would not change it.</summary>
public sealed record SettleResult(bool Done, string? Reason);

/// <summary>The two ways ReleaseOrder can end.</summary>
public static class PaymentResults
{
    public const string Released = "released for delivery";
    public static string Pending(string reason) => "pending: " + reason;
}

public static class ExecutionIds
{
    /// <summary>Fixed namespace of this application: the same event id gives the same run id here, and only here.</summary>
    private static readonly Guid Namespace = Guid.Parse("975e566d-2cea-4a3b-bf9c-e0e440a274d3");

    /// <summary>
    /// The run id is a UUID v5 of the event id, so a resent webhook lands on the run that already exists and nothing
    /// needs to be stored to find it again.
    /// </summary>
    public static Guid FromEventId(string eventId)
    {
        var name = Encoding.UTF8.GetBytes(eventId);
        var input = new byte[16 + name.Length];
        Namespace.TryWriteBytes(input, bigEndian: true, out _);
        name.CopyTo(input, 16);
        var hash = SHA1.HashData(input);
        hash[6] = (byte)((hash[6] & 0x0F) | 0x50);   // version 5
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);   // RFC 4122 variant
        return new Guid(hash.AsSpan(0, 16), bigEndian: true);
    }
}
