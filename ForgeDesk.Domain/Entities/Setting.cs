namespace ForgeDesk.Domain;

/// <summary>A persisted local application setting.</summary>
public sealed class Setting
{
    public const string ThemeKey = "ui.theme";

    private Setting()
    {
        Key = string.Empty;
        Value = string.Empty;
    }

    private Setting(string key, string value, DateTimeOffset updatedAtUtc)
    {
        Key = ValidateKey(key);
        Value = ValidateValue(value);
        UpdatedAtUtc = updatedAtUtc;
    }

    public static Setting Create(string key, string value, DateTimeOffset? updatedAtUtc = null)
        => new(key, value, updatedAtUtc ?? DateTimeOffset.UtcNow);

    public static Setting CreateTheme(ThemeMode mode, DateTimeOffset? updatedAtUtc = null)
    {
        if (!Enum.IsDefined(typeof(ThemeMode), mode))
        {
            throw new DomainInvariantException("The requested theme is not supported.");
        }
        return Create(ThemeKey, mode.ToString(), updatedAtUtc);
    }

    public string Key { get; private set; }
    public string Value { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAt => UpdatedAtUtc;

    public void SetValue(string value, DateTimeOffset? updatedAtUtc = null)
    {
        Value = ValidateValue(value);
        UpdatedAtUtc = updatedAtUtc ?? DateTimeOffset.UtcNow;
    }

    private static string ValidateKey(string key)
    {
        var value = key?.Trim() ?? string.Empty;
        if (value.Length is < 1 or > 200)
        {
            throw new DomainInvariantException("A setting key must contain between 1 and 200 characters.");
        }
        return value;
    }

    private static string ValidateValue(string value)
    {
        if (value is null)
        {
            throw new DomainInvariantException("A setting value is required.");
        }
        if (value.Length > 4000)
        {
            throw new DomainInvariantException("A setting value cannot exceed 4000 characters.");
        }
        return value;
    }
}
