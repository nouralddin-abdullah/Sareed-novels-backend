namespace Application.Wallet.DTOs;

/// <summary>
/// The point packs the Android app may sell through Google Play (GET /api/wallet/play-products). Never prices: the app
/// shows the price Google Play gives it for each product id.
/// </summary>
public class PlayProductsDto
{
    /// <summary>False when the server can't verify purchases (billing not configured): the app must not sell anything.</summary>
    public bool Enabled { get; set; }

    /// <summary>Why purchases are unavailable (Arabic), when <see cref="Enabled"/> is false.</summary>
    public string? Message { get; set; }

    /// <summary>
    /// The value the app passes to Google Play as obfuscatedAccountId when it launches a purchase (Flutter
    /// in_app_purchase: <c>PurchaseParam.applicationUserName</c>): lowercase hex SHA-256 of the user's id.
    /// </summary>
    public string ObfuscatedAccountId { get; set; } = default!;

    /// <summary>Sorted by points; empty when <see cref="Enabled"/> is false.</summary>
    public List<PlayProductDto> Products { get; set; } = new();
}

public class PlayProductDto
{
    public string ProductId { get; set; } = default!;
    public int Points { get; set; }
}
